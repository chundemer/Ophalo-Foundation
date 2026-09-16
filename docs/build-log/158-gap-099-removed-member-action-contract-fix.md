# BL158 — GAP-099 (slice): removed-member action contract fix

**Status:** Delivered 2026-09-15, awaiting Christian's diff review.

**Scope reference:** [workboard](../workboard.md) Next item 1 (GAP-099). Found while verifying
[BL157](157-gap-099-invited-member-cancel-invite.md)'s cancel-invite fix live: canceling a pending
invite, then re-inviting the same address, correctly shows "This person was previously invited.
Resend their invite to restore access." — but the Team list's Removed row for that person offers
**Reactivate**, not **Resend invite**, and Reactivate always fails for that case.

## Problem

`AccountUser.Remove()` (`src/OpHalo.Foundation.Core/Entities/Accounts/AccountUser.cs:217-223`)
unconditionally clears `InviteExpiresAtUtc = null` on any removal, whether the member ever accepted
or not. `TeamSection.tsx`'s row-action classification (`:169-170`) infers which action to show from
that same field:

```
isRemovedInvite = status === "removed" && inviteExpiresAtUtc !== null   → Resend invite / Manual share
isRemovedMember = status === "removed" && inviteExpiresAtUtc === null   → Reactivate
```

Since `Remove()` always nulls the field, every Removed row now falls into `isRemovedMember`
regardless of whether `UserId` was ever set — so a canceled, never-accepted invite shows
**Reactivate**. `AccountUser.Reactivate()` (`:173`) explicitly rejects `Removed && UserId is null`
with `InvalidStatusTransition`, which has no case in `memberErrorMsg` and falls to the generic
"Something went wrong. Please try again." — a dead end.

This was very likely unreachable before GAP-099 (BL157): there was no UI path to remove a still-
`Invited` member, so a Removed-with-no-`UserId` row could probably never occur from normal use.
BL157 makes it common (canceling any bad invite), turning a latent contract gap into an everyday
dead end.

The real, stable signal already exists in the domain — `AccountUser.UserId` is set exactly once, on
accept (`AccountUser.cs:85`), and never cleared — but it is missing from every layer of the read
path: `MemberListItem` (persistence projection), `MemberItem` (application DTO), and the frontend
`MemberItem` type.

## Locked decision (Christian, 2026-09-15)

- The API must expose a stable acceptance fact, not let the UI keep inferring it from a field whose
  meaning removal intentionally destroys.
- New field: **`hasAcceptedBefore`** (bool), derived server-side as `UserId != null`. Positive
  framing, not a repurposed nullable timestamp.
- Frontend selects the Removed-row action set **solely** from `hasAcceptedBefore`:
  - `false` (never accepted) → Resend invite / Manual share
  - `true` (accepted before) → Reactivate
- **Held out, separate follow-up (not this slice):** linking the invite-form's "Resend their invite
  to restore access." message directly to the action (auto-expand/scroll/highlight the Removed row).
  The row is already reachable via the existing "Show removed members" toggle; auto-navigation adds
  its own interaction/accessibility decisions and must not delay the contract repair.

## File gate

Backend:

1. **Modified** `src/OpHalo.Foundation.Application/Members/IMemberManagementPersistence.cs` —
   `MemberListItem` gains `HasAcceptedBefore` (bool).
2. **Modified** `src/OpHalo.Foundation.Infrastructure/Members/EfMemberManagementPersistence.cs` —
   projection adds `au.UserId`; `MemberListItem` construction sets
   `HasAcceptedBefore: r.UserId is not null`.
3. **Modified** `src/OpHalo.Foundation.Application/Members/MemberManagementService.cs` —
   `MemberItem` gains `HasAcceptedBefore` (bool); `ListMembersAsync`'s mapping passes it through
   unchanged from `MemberListItem`.

Frontend:

4. **Modified** `web/ophalo-app/src/lib/apiClient.types.ts` — `MemberItem` gains
   `hasAcceptedBefore: boolean`.
5. **Modified** `web/ophalo-app/src/pages/settings/TeamSection.tsx` — `isRemovedInvite` /
   `isRemovedMember` (`:169-170`) switch from `member.inviteExpiresAtUtc` to
   `member.hasAcceptedBefore`.

Tests: backend regression covering both Removed states in the list response (never-accepted vs.
accepted-then-removed); frontend regression covering both action sets rendered from
`hasAcceptedBefore` on a Removed row.

Two production backend files, one production frontend file, one shared DTO/type file — 4 production
files total, well inside the batch gate.

## Delivered

Exactly the file gate above, plus fixture/test-shape updates required by the new required DTO
field (not counted against the gate — mechanical fixture updates, no behavior change):
`web/ophalo-app/src/mocks/fixtures.ts`, `Settings.v2Shell.test.tsx`,
`TeamSection.recipe.test.tsx`.

New tests:
- `tests/OpHalo.IntegrationTests/Api/MemberManagementTests.cs` —
  `ListMembers_IncludeRemoved_HasAcceptedBeforeReflectsPriorUserId`, using the existing
  `SeedRemovedMemberWithUserAsync` (accepted before → `true`) and `SeedRemovedInviteAsync`
  (never accepted → `false`) seed helpers.
- `web/ophalo-app/src/pages/settings/__tests__/TeamSection.inviteClarity.test.tsx` — two cases:
  a Removed row with `hasAcceptedBefore: false` renders Resend invite/Manual share and never
  Reactivate; a Removed row with `hasAcceptedBefore: true` renders Reactivate and never
  Resend invite/Manual share.

## Verification

- `dotnet build src/OpHalo.Api` (pulls in Application/Infrastructure) clean, 0 warnings/errors.
- `dotnet test tests/OpHalo.IntegrationTests --filter MemberManagementTests` — 35/35 passed
  against a real Postgres container (34 existing + 1 new).
- `tsc --noEmit` clean; `check:tokens` pass; focused `src/pages/settings/__tests__` 43/43
  (7 files, 2 new).
- `git diff --check` clean.
