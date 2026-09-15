# BL157 — GAP-099 (slice): cancel a pending invite

**Status:** Delivered 2026-09-15, awaiting Christian's diff review.

**Scope reference:** [workboard](../workboard.md) Next item 1 (GAP-099 — team-member access
clarity). Discovered while reproducing a Resend-suppression support case: a business had no way to
correct a mistyped invite email or drop an invite that will never be accepted, and the bad invite
permanently occupied a paid seat.

## Problem

Confirmed in code (`web/ophalo-app/src/pages/settings/TeamSection.tsx`):

`MemberRow`'s "Invited row actions" block (`:284-303`) renders only **Resend invite** and
**Manual share**. Every other status row in the same component — Active (`:256`), Suspended
(`:305`), and even an already-Removed invite (`:339`) — has a **Remove** action wired to the
existing `handleRemove` → `api.removeMember(member.accountUserId)` → `RemoveAsync` path. Invited is
the only status missing it, with no code comment or test explaining the omission — an accidental
gap, not a locked decision.

The backend already supports this correctly: `AccountUser.Remove()`
(`src/OpHalo.Foundation.Core/Entities/Accounts/AccountUser.cs:217`) is unconditional — it
transitions any `MembershipStatus`, `Invited` included, straight to `Removed`, and
`MemberManagementService.RemoveAsync` has no guard excluding `Invited` (unlike `SuspendAsync`,
which explicitly rejects `Invited` with "cancel the invite instead"). `Removed` does not occupy a
seat (per `ReactivateAsync`'s own comment), so canceling frees the seat.

## Locked scope (Christian, 2026-09-15)

- Invited members gain the existing confirmed **Remove** action — same `handleRemove` /
  `confirmRemove` state and `api.removeMember` call already used by the other three row types.
- **No email editing.** A mistyped or suppressed invite is corrected by canceling and re-inviting
  with the right address, not by mutating the pending invite in place.
- **No endpoint or domain change.** `RemoveAsync` / `AccountUser.Remove()` already handle this
  transition; this is a UI-parity fix only.
- Cancellation frees the occupied seat (already true server-side; this slice makes it reachable
  from the Invited row).

## File gate

Production (`web/ophalo-app/src`):

1. **Modified** `pages/settings/TeamSection.tsx` — add the same confirm-remove block already used
   for Active/Suspended/Removed-invite rows to the "Invited row actions" block (`:284-303`),
   reusing `handleRemove`, `confirmRemove`, `setConfirmRemove` (all already declared in `MemberRow`
   at `:70-71`). No new state, no new API call.

Tests: new assertion (in `TeamSection.inviteClarity.test.tsx` or a small new file) that an Invited
row shows Remove, confirms, calls `api.removeMember(accountUserId)`, and triggers a refresh.

One production file, well inside the batch gate.

## Delivered

Exactly the 1-file preflight gate. `web/ophalo-app/src/pages/settings/TeamSection.tsx` — added the
same confirm-remove block used by the Active/Suspended/Removed-invite rows to the Invited row
actions, reusing `handleRemove`/`confirmRemove`/`setConfirmRemove` unchanged.

Test: new case in `TeamSection.inviteClarity.test.tsx` — an Invited row shows Remove, confirms, and
calls `api.removeMember(accountUserId)`.

Verification: `tsc --noEmit` clean; `check:tokens` pass; `git diff --check` clean; focused suite
`src/pages/settings/__tests__` 7 files / 41 tests passed (2 new).
