# Phase 5E — Member Management Discovery

**Status:** Pre-work complete. Decisions confirmed — ready for implementation sessions.
**Build-log preceding this:** 021-phase-5d-member-invites-discovery.md
**Date:** 2026-06-16

---

## Why This Phase Is Next

Phase 5D added member invites:

- `POST /accounts/me/invite`
- `POST /accounts/invite/accept`

The next account foundation gap is member management after invite creation and acceptance:

- list members;
- change roles;
- resend or restore pending invites;
- cancel pending invites;
- suspend/reactivate members;
- remove members;
- revoke sessions when access is removed;
- protect owner and primary-owner invariants.

This phase keeps the API/domain language as **members**. Product UI may later say "Team", but backend routes, services, and errors should use member/member-management naming because the domain model is account membership (`AccountUser`).

---

## Current State Verified

- Roles are fixed to `Owner`, `Admin`, `Operator`, `Viewer`.
- Membership statuses are fixed to `Invited`, `Active`, `Suspended`, `Removed`.
- `Account.PrimaryOwnerAccountUserId` exists and is a separate invariant from role.
- `PermissionKeys.Account.MembersManage` exists.
- Owner/Admin hold `account.members.manage`; Operator/Viewer do not.
- Session auth fails closed unless the `AccountUser` is Active.
- Session lookup reloads `AccountUser` role/status on request, so role changes do not require session revocation to take effect.
- Suspend/remove must revoke durable sessions immediately.
- Seat limit counts Active + Invited + Suspended, and excludes Removed.
- Invites already use the `AccountUser` invite fields:
  - `InviteTokenHash`
  - `InviteExpiresAtUtc`
  - `RefreshInvite(...)`
  - `CreatePendingInvite(...)`

---

## Role Guide

These descriptions are product-facing source-of-truth copy for signup, subscription setup, invite forms, and role-change menus.

| Role | Description |
|---|---|
| Owner | Full account control: billing, subscription, settings, members, and all Keep work. Maximum 2 Owners per account. Only Owners can promote/demote Owner role. |
| Admin | Can manage account settings, members, notifications, audit, Keep settings, and all Keep work. Cannot manage billing. Cannot manage Owners. |
| Operator | Can create and work requests: view, create, update, respond, send updates, message customers, close requests, add internal notes, and view insights. Cannot manage account settings, members, billing, or Keep settings. |
| Viewer | Read-only operational access: view account, requests, and insights. Cannot create/update/close requests, message customers, manage settings, members, or billing. |

Do not add custom roles or enterprise RBAC in Phase 5E.

---

## Proposed Routes

All routes use the current authenticated account from the server-side session. Do not accept caller-supplied `AccountId`.

```text
GET    /accounts/me/members
PATCH  /accounts/me/members/{accountUserId}/role
POST   /accounts/me/members/{accountUserId}/resend-invite
POST   /accounts/me/members/{accountUserId}/suspend
POST   /accounts/me/members/{accountUserId}/reactivate
DELETE /accounts/me/members/{accountUserId}
```

Route notes:

- `DELETE` means "remove/cancel membership" by setting `MembershipStatus.Removed`; it is not EF soft-delete.
- `resend-invite` covers both active pending invites and removed/canceled invites that never had a `UserId`.
- Reactivation covers previously active removed members that do have a `UserId`.
- All mutation routes require `account.members.manage`.
- List route should require authenticated Active membership; response can include only members in the current account.

---

## List Contract

Default:

- include `Active`, `Invited`, and `Suspended`;
- exclude `Removed`.

Optional:

```text
GET /accounts/me/members?includeRemoved=true
```

Response shape:

```json
{
  "members": [
    {
      "accountUserId": "guid",
      "email": "owner@example.com",
      "role": "owner",
      "status": "active",
      "isCurrentUser": true,
      "isPrimaryOwner": true,
      "activatedAtUtc": "2026-06-16T12:00:00Z",
      "inviteExpiresAtUtc": null
    }
  ]
}
```

Field meanings:

- `status: "active"` means the member currently has account access.
- `isCurrentUser: true` means this row is the signed-in session's own `AccountUser`; the UI can disable self-destructive actions.
- `isPrimaryOwner: true` identifies the primary owner row protected by Phase 5E.

---

## Authorization Rules

Caller gate:

- caller must be authenticated;
- caller membership must be Active;
- caller must hold `PermissionKeys.Account.MembersManage`;
- current `UserAccessPolicy.IsPermitted(...)` is the authority.

Role-management authority:

- Owner can manage Admin, Operator, Viewer, and the second Owner subject to owner safety rules.
- Admin can manage Admin, Operator, and Viewer.
- Admin cannot manage Owners.
- Operator and Viewer cannot manage members.

Self-management:

- no self role-change;
- no self suspend;
- no self remove through member-management v1;
- a future "leave account" flow can be designed separately.

---

## Owner Safety Rules

Owner policy:

- allow up to 2 Owner-role memberships per account;
- at least 1 Active Owner must always remain;
- only an Owner can promote another member to Owner;
- only an Owner can demote an Owner;
- Admin cannot promote to Owner, demote Owner, suspend Owner, or remove Owner.

Primary owner policy:

- `Account.PrimaryOwnerAccountUserId` is protected in Phase 5E;
- primary owner cannot be demoted, suspended, or removed;
- transfer-primary-owner is a later explicit flow and is not part of Phase 5E.

Second owner policy:

- a second Owner may be demoted, suspended, or removed by another Owner;
- the operation must leave at least one Active Owner.

Owner cap:

- promotion to Owner fails when the account already has 2 non-Removed Owner memberships.
- Owner count should include Active, Invited, and Suspended Owner memberships; Removed does not count.

---

## Status And Role Transition Rules

### Role Changes

Allowed target roles:

```text
Owner, Admin, Operator, Viewer
```

Rules:

- Invited members can have role changed before acceptance.
- Suspended members can have role changed while still Suspended.
- Active members can have role changed.
- Removed members cannot have role changed; use reactivate or resend-invite flow first.
- Only Owners can assign or remove Owner role.
- Primary owner cannot be role-changed in Phase 5E.
- Role change does not revoke sessions because auth reloads role/status from persistence each request.

### Suspend

Rules:

- Active -> Suspended.
- Suspended -> idempotent success or controlled no-op; implementation should choose one and test it.
- Invited cannot be suspended; cancel the invite instead.
- Removed cannot be suspended.
- Primary owner cannot be suspended.
- Suspending a member revokes their sessions immediately.

### Reactivate

Rules:

- Suspended -> Active.
- Removed with `UserId != null` -> Active, after seat-limit check.
- Removed with `UserId == null` is not reactivated; use resend-invite.
- Invited cannot be reactivated; they are already pending access.
- Reactivating Suspended does not need a seat-limit check because Suspended already occupies a seat.
- Reactivating Removed needs a seat-limit check because Removed does not occupy a seat.
- Reactivation does not create a session; the member signs in normally.

### Remove / Cancel

Rules:

- Active -> Removed.
- Suspended -> Removed.
- Invited -> Removed and clear invite token/expiry so the old link becomes invalid.
- Removed -> idempotent success or controlled no-op; implementation should choose one and test it.
- Primary owner cannot be removed.
- Removing Active/Suspended revokes sessions immediately.
- Removing Invited does not need session revocation because no session should exist.
- Removal is `MembershipStatus.Removed`, not EF soft-delete.

### Resend Invite

Rules:

- Invited -> rotate token, reset expiry, send fresh invite email.
- Removed with `UserId == null` -> restore to Invited, rotate token, reset expiry, send fresh invite email.
- Removed with `UserId != null` -> reject; use reactivate.
- Active or Suspended -> reject.
- Resend/restore invite checks seat limit only when restoring from Removed, because Removed does not occupy a seat.
- Pending Invited already occupies a seat, so regular resend bypasses the seat-limit check.

---

## Removed Member Semantics

`Removed` is the durable membership-history state. It gives owners/admins a repair path and prevents silent duplicate identities.

Cases:

- Removed active member with `UserId`: use `reactivate`.
- Removed/canceled invite with no `UserId`: use `resend-invite` to restore to Invited and email a fresh token.
- Sending a brand-new invite to an email with a Removed membership should not silently create a new row. It should tell the caller this person was previously removed and suggest reactivation/resend depending on `UserId`.

Suggested API error:

```text
Member.PreviouslyRemoved
```

The response may include safe action metadata such as:

```json
{
  "code": "Member.PreviouslyRemoved",
  "suggestedAction": "reactivate"
}
```

or:

```json
{
  "code": "Member.PreviouslyRemoved",
  "suggestedAction": "resend_invite"
}
```

Do not include raw invite tokens or sensitive data.

---

## Seat Limit Policy

Seat count remains:

```text
Active + Invited + Suspended
```

Excluded:

```text
Removed
```

Seat-limit checks:

- new invite: yes;
- resend existing Invited: no;
- restore removed invite to Invited: yes;
- reactivate Removed with UserId: yes;
- reactivate Suspended: no;
- role change: no;
- suspend: no;
- remove: no.

---

## Session Revocation Policy

Revoke durable sessions immediately for:

- suspend Active member;
- remove Active member;
- remove Suspended member if any existing sessions remain.

Do not revoke sessions for:

- role change, because auth reloads role/status each request;
- reactivation, because reactivation does not create sessions;
- invite resend/cancel for never-accepted invites.

Implementation should add a persistence or session-service method that can revoke all sessions by `AccountUserId` in the current account.

---

## Error Catalog

Add member-management errors under Foundation Core:

```text
Member.Forbidden
Member.NotFound
Member.CannotModifySelf
Member.CannotModifyOwner
Member.PrimaryOwnerProtected
Member.OwnerLimitReached
Member.LastOwner
Member.InvalidRole
Member.InvalidStatusTransition
Member.SeatLimitReached
Member.PreviouslyRemoved
```

HTTP mapping:

- `.Forbidden` -> 403
- `.NotFound` -> 404
- `.CannotModifySelf` -> 422
- `.CannotModifyOwner` -> 422
- `.PrimaryOwnerProtected` -> 422
- `.OwnerLimitReached` -> 409
- `.LastOwner` -> 409
- `.InvalidRole` or validation role errors -> 400
- `.InvalidStatusTransition` -> 422
- `.SeatLimitReached` -> 409
- `.PreviouslyRemoved` -> 409

If existing suffix patterns do not cover a code, add explicit mappings in `ErrorHttpMapper`.

---

## Off-Season Subscription Note

Off-season mode is a strong retention idea but is explicitly **not Phase 5E implementation**.

Direction to carry forward:

- model off-season as account/commercial posture, not `MembershipStatus`;
- owners only can sign in;
- owner access is read-only at first;
- Admin/Operator/Viewer are blocked while off-season;
- no new public submissions or outbound notifications;
- business can still view data and see new features.

This deserves a later discovery/build log because it touches billing, account access posture, Keep write gates, and UX.

---

## Implementation Sessions For Claude Code

### Session 5E-A — Discovery + Contracts

Scope:

- create this discovery log;
- update `docs/decisions/decision-index.md`;
- update `docs/session-log.md`;
- no product implementation.

Exit gate:

- ADR entries locked for Phase 5E;
- route contract, transition matrix, owner rules, error catalog, tests, and non-goals documented.

### Session 5E-B — Application + Infrastructure

Scope:

- add member-management errors;
- add application services and persistence seam;
- implement list, role change, resend-invite, suspend, reactivate, remove/cancel;
- add all-session revocation by `AccountUserId`;
- add unit tests for transition/owner guard logic if logic is factored outside EF.

Must not:

- add custom roles;
- change `MembershipStatus`;
- soft-delete members for removal;
- allow primary-owner mutation;
- build off-season mode;
- add audit persistence;
- rework invite acceptance.

### Session 5E-C — API + Integration Tests

Scope:

- map member endpoints;
- add request/response DTOs;
- add validation and error mapping;
- add integration tests for authorization, role changes, owner cap, primary-owner protection, self-actions, suspend/remove session revocation, reactivation, cancel invite, resend invite, and removed-member suggested actions;
- update session log with final build/test state.

---

## Tests To Require

Minimum integration tests:

1. Anonymous member list -> 401.
2. Active member list returns Active/Invited/Suspended and excludes Removed by default.
3. `includeRemoved=true` includes Removed.
4. `isCurrentUser` marks the caller row.
5. Viewer/Operator mutation -> 403.
6. Admin can manage Admin/Operator/Viewer.
7. Admin cannot manage Owner.
8. Owner can promote Admin to Owner when owner count < 2.
9. Owner promotion fails at 2 Owners.
10. Primary owner cannot be demoted/suspended/removed.
11. Last Active Owner cannot be demoted/suspended/removed.
12. No self role-change/suspend/remove.
13. Invited member role can be changed before acceptance.
14. Suspended member role can be changed while suspended.
15. Removed member role change rejected.
16. Suspend Active -> Suspended and revokes sessions.
17. Reactivate Suspended -> Active, no seat check.
18. Reactivate Removed with `UserId` -> Active, with seat check.
19. Reactivate Removed with no `UserId` rejected; use resend-invite.
20. Remove Invited clears token/expiry; old token invalid.
21. Remove Active/Suspended sets Removed and revokes sessions.
22. Resend Invited rotates token and sends email.
23. Resend Removed no-UserId restores to Invited, checks seat limit, sends email.
24. Resend Removed with UserId rejected with suggested reactivation path.
25. New invite to Removed email returns `Member.PreviouslyRemoved` with suggested action.
26. Role change does not revoke session, but new permissions/status are observed on subsequent requests.

---

## Decisions — Confirmed

| # | Decision | Status |
|---|----------|--------|
| D1 | Backend/API/domain language is `members`; UI may say "Team" later. | Confirmed |
| D2 | Caller must be authenticated, Active, and hold `account.members.manage`. | Confirmed |
| D3 | Roles remain Owner/Admin/Operator/Viewer only. | Confirmed |
| D4 | Owner cap is 2. Only Owners can promote/demote Owner role. | Confirmed |
| D5 | Primary owner cannot be demoted/suspended/removed in Phase 5E. Primary-owner transfer is deferred. | Confirmed |
| D6 | Admin can manage Admin/Operator/Viewer, but cannot manage Owners. | Confirmed |
| D7 | Invited and Suspended members can have role changed. Removed members cannot. | Confirmed |
| D8 | No self role-change, self suspend, or self remove in member-management v1. | Confirmed |
| D9 | Removal uses `MembershipStatus.Removed`, not EF soft-delete. | Confirmed |
| D10 | Suspend/remove revoke sessions immediately. Role change does not revoke sessions. | Confirmed |
| D11 | Removed with `UserId` reactivates through `reactivate`; Removed with no `UserId` restores through `resend-invite`. | Confirmed |
| D12 | Cancel Invited by setting Removed and clearing invite token/expiry. | Confirmed |
| D13 | Default list excludes Removed; `includeRemoved=true` includes Removed. | Confirmed |
| D14 | Off-season mode is future account/commercial posture, not Phase 5E and not a membership status. | Confirmed |

---

## ADRs To Add

| # | Decision |
|---|---|
| ADR-078 | Phase 5E member-management route and naming contract: `members` API/domain language; current account from session only. |
| ADR-079 | Member-management authorization: `account.members.manage`; Owner/Admin manage non-owner roles; Owner-only owner management. |
| ADR-080 | Owner safety: max 2 Owners, at least 1 Active Owner, primary owner protected; transfer deferred. |
| ADR-081 | Member status transitions: role changes for Active/Invited/Suspended; Removed reactivation/resend split; cancel invite clears token; removal is status not soft-delete. |
| ADR-082 | Session revocation: suspend/remove revoke sessions; role change does not. |
| ADR-083 | Off-season subscription posture deferred: account/commercial read-only owner access, not membership status. |

