# Phase 5D — Member Invites Discovery

**Status:** Pre-work complete. Decisions confirmed — ready for implementation session.
**Build-log preceding this:** 020-phase-5c-new-account-registration-implementation.md
**Date:** 2026-06-15 (discovery) / 2026-06-15 (confirmations added)

**Reference files read:**
- `_reference/src/OpHalo.API/Endpoints/V1/AccountEndpoints.cs`
- `_reference/src/OpHalo.API/Requests/V1/InviteAccountMemberBody.cs`
- `_reference/src/OpHalo.API/Requests/V1/AcceptAccountInviteBody.cs`
- `_reference/src/OpHalo.Application/Accounts/Commands/Invite/SendAccountInviteCommand.cs`
- `_reference/src/OpHalo.Application/Accounts/Commands/Invite/SendAccountInviteCommandHandler.cs`
- `_reference/src/OpHalo.Application/Accounts/Commands/Invite/SendAccountInviteCommandValidator.cs`
- `_reference/src/OpHalo.Application/Accounts/Commands/Invite/AcceptAccountInviteCommand.cs`
- `_reference/src/OpHalo.Application/Accounts/Commands/Invite/AcceptAccountInviteCommandHandler.cs`
- `_reference/src/OpHalo.Application/Accounts/Commands/Invite/AcceptAccountInviteCommandValidator.cs`
- `_reference/src/OpHalo.Application/Accounts/Commands/Invite/InviteTokenGenerator.cs`
- `_reference/src/OpHalo.Core/Entities/Accounts/Errors/InviteErrors.cs`
- `_reference/web/ophalo-app/src/app/invite/accept/route.ts`
- `_reference/web/ophalo-app/src/app/keep/(protected)/team/_components/InviteForm.tsx`
- Current Foundation `AccountUser`, session, auth, entitlement, permission, and error code

---

## Phase 5C state verified

Phase 5C completed new-account registration:

- `POST /auth/start` creates `NewAccount` or `ExistingMember` codes.
- `/auth/exchange` handles `NewAccount` and `ExistingMember`.
- `/auth/start` treats invited-only membership as neutral 200/no-code.
- `AccountUser` already has invite lifecycle fields and methods:
  - `MembershipStatus.Invited`
  - `InviteTokenHash`
  - `InviteExpiresAtUtc`
  - `CreatePendingInvite(...)`
  - `RefreshInvite(...)`
  - `Activate(...)`
- Session auth fails closed unless `MembershipStatus.Active`.
- `PermissionKeys.Account.MembersManage` exists and is granted to Admin and Owner.
- Seat limit exists through `AccountEntitlements.MaxUserSeats` and `FeatureLimitKeys.Account.UserLimit`.

---

## Reference behavior summary

The reference invite flow uses account/team routes, not `/auth/exchange`:

```text
POST /accounts/me/invite       authenticated owner/admin sends or resends invite
POST /accounts/invite/accept   anonymous invitee accepts raw token
```

Reference send invite:

1. Requires authenticated current user.
2. Loads caller `AccountUser` from session `AccountUserId`.
3. Requires caller active and Owner/Admin.
4. Normalizes invited email.
5. Loads account for business name.
6. If no account-user row exists for that email in the account:
   - creates pending invite row.
7. If pending invite exists:
   - rotates token and expiry in place;
   - returns `"resent"`.
8. If active/removed membership exists:
   - returns conflict.
9. Builds invite link with `App:OperatorBaseUrl`:

```text
{OperatorBaseUrl}/invite/accept?token={rawToken}
```

10. Reference commits invite state + email outbox + work item atomically.

Reference accept invite:

1. Hashes raw invite token.
2. Finds pending invite by token hash.
3. Rejects invalid/used/missing token.
4. Rejects expired token.
5. Loads account from invite row.
6. Finds or creates `User` by invited normalized email.
7. Activates `AccountUser`, linking `UserId`, setting Active, clearing invite token/expiry.
8. Saves user creation + membership activation atomically.
9. Creates session outside that transaction.
10. Sets auth cookie and redirects/returns success to the frontend.

Reference invite token:

- 32 random bytes, URL-safe Base64 without padding.
- SHA-256 hex hash persisted.
- Reference uses uppercase hex for invite tokens; Foundation session/auth code hashes currently use lowercase hex in other helpers.

---

## Foundation divergences

### Roles

Reference invite validator only allows `"Member"` and maps to legacy `AccountUserRole.Member`.

Foundation roles are:

```text
Owner
Admin
Operator
Viewer
```

ADR-025 already says pending invites may use any non-owner role: Admin, Operator, Viewer. Owner remains reserved for `CreateOwner`.

### Auth/authorization style

Reference handlers inject `IPlatformDbContext` and MediatR. Foundation should keep Application free of EF and use persistence interfaces implemented in Infrastructure, following 5B/5C.

Recommended access gate:

- Endpoint requires authentication.
- Service verifies current session/membership through persistence.
- Caller must be Active.
- Caller must be permitted for `account.members.manage` using `IUserAccessPolicy`, or equivalently Owner/Admin based on the existing permission map.
- Do not trust caller-provided `AccountId`; use `ICurrentUser.AccountId`.

### Email delivery

Reference uses outbox/work item. Foundation still has direct `IEmailSender`.

Recommended 5D behavior:

- Commit invite state first.
- Send invite email best-effort after commit.
- Public API response should not expose raw token unless we deliberately choose manual share-link mode.
- Delivery failure should not roll back the invite row.

### OperatorBaseUrl

Phase 5B explicitly deferred `App:OperatorBaseUrl` to invite links. Phase 5D should add:

```json
"App": {
  "PublicBaseUrl": "...",
  "OperatorBaseUrl": "https://app.ophalo.com"
}
```

`OperatorBaseUrl` owns `/invite/accept?token=...`.

---

## Proposed 5D contract

### Send invite

```text
POST /accounts/me/invite
Authorization: OpHalo session
```

Request:

```json
{
  "email": "member@example.com",
  "role": "operator"
}
```

Response:

```json
{
  "status": "sent"
}
```

or

```json
{
  "status": "resent"
}
```

Validation:

- blank/invalid email -> `400 Validation.EmailInvalid` or `Validation.EmailRequired`
- invalid role -> `400 Validation.RoleInvalid`
- owner role -> `400 Validation.RoleInvalid` or `422 Invite.OwnerRoleNotAllowed`

Authorization:

- anonymous -> 401
- authenticated but missing active membership -> 401/403, matching current auth posture
- active but lacks `account.members.manage` -> 403

Conflicts:

- target already active/suspended/removed in same account -> `409 Invite.AlreadyExists` or `Invite.AlreadyActive`
- seat limit reached -> `409 Invite.SeatLimitReached`

### Accept invite

```text
POST /accounts/invite/accept
```

Request:

```json
{
  "token": "raw-url-safe-token"
}
```

Browser response:

```json
{
  "status": "accepted",
  "destination": "/keep"
}
```

with `ophalo.sid` cookie.

Mobile response should follow the existing `/auth/exchange` convention if `clientType = "mobile_app"` is accepted: return `{ sessionToken, expiresAtUtc }` and no cookie. This needs a decision.

Errors:

- blank token -> `400 Validation.TokenRequired`
- invalid/used token -> `422 Invite.InvalidToken`
- expired token -> `422 Invite.Expired`
- activation race loser -> `422 Invite.InvalidToken` or `AuthCode.AlreadyConsumed` equivalent; decision needed
- session creation failure after activation -> `503 Account.SessionCreationFailed`

---

## Implementation shape

**Foundation.Core**

- Add `InviteErrors` under `Entities/Accounts/Errors`.
- Possibly add `AccountUserErrors` values only if existing ones are insufficient.

**Foundation.Application**

- `Auth/InviteTokenGenerator.cs` or `Accounts/Invites/InviteTokenGenerator.cs`
- `Invites/SendAccountInviteService.cs`
- `Invites/AcceptAccountInviteService.cs`
- `Invites/IAccountInvitePersistence.cs`
- `Invites/InviteSettings.cs` only if `MagicLinkSettings` is not broadened to include `OperatorBaseUrl`
- `SendInviteResult`, `AcceptInviteResult`

**Foundation.Infrastructure**

- `Invites/EfAccountInvitePersistence.cs`
- No migration expected if existing `AccountUser` invite columns/index already satisfy 5D.

**Api**

- Add account endpoints, likely new `Accounts/AccountEndpoints.cs`:
  - `POST /accounts/me/invite` authenticated
  - `POST /accounts/invite/accept` anonymous/rate-limited
- Add `App:OperatorBaseUrl` config binding.
- Add `ErrorHttpMapper` cases if invite error suffixes do not map correctly.

**Tests**

- New integration test file, likely `Api/AccountInviteTests.cs`.
- Unit tests for invite token generator and/or `AccountUser` invite lifecycle if not already covered.

---

## Persistence contract

Send invite persistence should atomically:

- verify caller membership/role snapshot;
- resolve current account;
- check current seat usage and entitlement limit if enabled;
- create pending invite or refresh existing pending invite;
- save invite row/token hash/expiry.

Accept invite persistence should atomically:

- find invited membership by token hash;
- verify `MembershipStatus.Invited`;
- verify non-expired token;
- find or create `User`;
- activate `AccountUser`;
- clear token and expiry;
- save all changes.

Concurrency/race expectations:

- Resending invite rotates token. Old token must become invalid.
- Concurrent accept of same token: exactly one should activate; loser should get invalid/used token response.
- Concurrent user creation for same email can hit unique user index; handle by re-querying or returning controlled conflict.
- Session creation remains outside transaction. Failure leaves membership active but sessionless; user can sign in afterward.

---

## Seat limit policy

Foundation can resolve `account.user_limit` through:

- `AccountEntitlements.MaxUserSeats`
- `FeatureAccessPolicy.ResolveLimit(...)`

Decision needed:

- Count only Active members?
- Count Active + Invited seats?
- Count Active + Invited + Suspended?
- Exclude Removed?

Recommendation: count **Active + Invited + Suspended**, exclude Removed and soft-deleted rows. Invited seats should reserve capacity so owners cannot over-invite beyond the plan; Suspended members still occupy a seat unless removed.

---

## Logging policy

Log only server-side unexpected failures with safe IDs:

- invite email delivery failure: warning with `AccountUserId` / invite row id, no email/token/link;
- session creation failure after accept: error with `AccountId`, `AccountUserId`, no token/email;
- unique constraint race: warning with account id and safe classification, no email/token.

Never log:

- raw invite token;
- invite token hash;
- invite URL;
- invitee email;
- invitee name;
- session token.

Expected outcomes should not be error logs:

- invalid token;
- expired token;
- already active/conflict;
- seat limit reached;
- forbidden caller;
- email delivery failure if invite row committed.

---

## Tests to require

Minimum integration tests:

1. Anonymous send invite -> 401.
2. Viewer/Operator send invite -> 403.
3. Admin or Owner send invite -> 200 sent.
4. Invite email contains operator accept URL and raw token.
5. Pending invite row stores only token hash, not raw token.
6. Resend pending invite -> 200 resent, rotates token, old token invalid.
7. Existing active member invite -> 409.
8. Existing removed/suspended member invite behavior per decision.
9. Seat limit reached -> 409, no invite row created/refreshed.
10. Accept invite with missing token -> 400.
11. Accept invalid token -> 422.
12. Accept expired token -> 422.
13. Accept valid token creates or links `User`, activates `AccountUser`, clears token/expiry.
14. Accept sets browser cookie and allows `/auth/me`.
15. Mobile accept behavior if included.
16. Accept same token twice -> first succeeds, second invalid/used.
17. Concurrent accept same token -> exactly one success.
18. Session creation failure after activation -> 503 and membership remains active.
19. Direct email delivery failure on send invite still returns success if invite committed.
20. Logging tests if existing harness supports it; otherwise explicit code review check.

---

## Decisions — confirmed

| # | Decision | Status |
|---|----------|--------|
| D1 | Route shape: `POST /accounts/me/invite` (authenticated) + `POST /accounts/invite/accept` (anonymous, rate-limited). Do not route via `/auth/exchange`. `EntryContext.InvitedUser = 3` added to enum for parity but not wired to exchange flow. | **Confirmed** |
| D2 | `OperatorBaseUrl` added to `MagicLinkSettings`, bound from the existing `"App"` config section. Stale `"Auth"` comment in `MagicLinkSettings.cs` corrected. Invite link: `{OperatorBaseUrl}/invite/accept?token=...`. | **Confirmed** |
| D3 | Allow Admin, Operator, Viewer; reject Owner. No default role — caller must supply explicitly. `CreatePendingInvite` factory guards Owner at the domain level. | **Confirmed** |
| D4 | Caller must hold `account.members.manage`, checked via `UserAccessPolicy.IsPermitted(role, membershipStatus, purpose, key)`. Owner + Admin hold this key per existing role map. | **Confirmed** |
| D5 | Seat limit counts Active + Invited + Suspended; excludes Removed and soft-deleted. Invited seats reserve capacity; Suspended members still occupy a seat unless removed. | **Confirmed** |
| D6 | Invite token TTL = 7 days, matching reference. | **Confirmed** |
| D7 | Direct `IEmailSender`, best-effort after commit; no outbox. Same pattern as 5B/5C. | **Confirmed** |
| D8 | No raw token or invite URL in API response; email delivery only. Follows ADR-011. | **Confirmed** |
| D9 | Browser-only accept for this phase: `clientType = Browser`, `deviceName = null`. Mobile accept deferred. | **Confirmed** |
| D10 | Accept invite finds or creates `User` by `NormalizedEmail`; no duplicate User. Unique index on `Users.Email` closes the race at DB level. | **Confirmed** |
| D11 | Active, Suspended, or Removed membership → `Invite.AlreadyActive` (cannot re-invite). Port reference behavior verbatim. | **Confirmed** |
| D12 | Session creation failure after accept returns error; `AccountUser` stays Active and user can sign in afterward. | **Confirmed** |
| D13 | Invite token hash = uppercase SHA-256 hex (`Convert.ToHexString`), matching the reference `InviteTokenGenerator`. Invite-subsystem-local; documented in ADR-076. | **Confirmed** |
| D14 | HTTP error mapping: `Invite.Forbidden` → 403, `Invite.InvalidToken` → 422, `Invite.Expired` → 422, `Invite.AlreadyActive` → 409, `Invite.SeatLimitReached` → 409. | **Confirmed** |

---

## ADRs to add in implementation session

| # | Decision |
|---|---|
| ADR-074 | `EntryContext.InvitedUser = 3` added for parity; not wired to exchange flow in this phase |
| ADR-075 | Invite send: any non-Owner role (ADR-025 applied); `UserAccessPolicy.IsPermitted` + `PermissionKeys.Account.MembersManage` gates caller; `OperatorBaseUrl` added to `MagicLinkSettings` (bound from `"App"` section) |
| ADR-076 | Invite accept: `POST /accounts/invite/accept` direct raw-token endpoint; `InviteTokenGenerator` (uppercase SHA-256, mirrors auth code generator pattern); activate + find-or-create User in one transaction |
| ADR-077 | `IInvitePersistence` as a separate persistence seam — invite storage model (`AccountUser`) is unrelated to auth code storage; seat limit checked via `FeatureAccessPolicy.ResolveLimit` at send time |

---

## Implementation scope

**Foundation.Core (2 files):**
- `Entities/Accounts/Enums/EntryContext.cs` — add `InvitedUser = 3`
- `Entities/Accounts/Errors/InviteErrors.cs` — NEW: `Forbidden`, `InvalidToken`, `Expired`, `AlreadyActive`, `SeatLimitReached`

**Foundation.Application (5 files — `MagicLinkSettings.cs` already updated this session):**
- `Auth/InviteTokenGenerator.cs` — NEW
- `Auth/IInvitePersistence.cs` — NEW
- `Auth/SendInviteService.cs` — NEW
- `Auth/AcceptInviteService.cs` — NEW
- `Auth/InviteEmailTemplate.cs` — NEW

**Foundation.Infrastructure (1 file):**
- `Auth/EfInvitePersistence.cs` — NEW

**Api (3 files):**
- `Accounts/AccountEndpoints.cs` — NEW: `POST /accounts/me/invite` + `POST /accounts/invite/accept`
- `Program.cs` — register `SendInviteService`, `AcceptInviteService`, `IInvitePersistence`; map `AccountEndpoints`
- `Helpers/ErrorHttpMapper.cs` — add `Invite.*` error mappings

**Tests (2 files):**
- `UnitTests/Auth/InviteTokenGeneratorTests.cs` — NEW
- `IntegrationTests/Api/InviteTests.cs` — NEW

**No migration needed** — all invite columns and indexes exist in `InitialFoundationSchema` from Phase 4.
