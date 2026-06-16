# Session Log — OpHalo Foundation

**Last updated:** 2026-06-16
**Next session tier:** Tier 1 — Discovery (Phase 5F or next phase)
**Branch:** `main` (no remote yet)

---

## Phase 5E-C — COMPLETE

Member management API + integration tests. 126/126 integration tests passing (417/417 total).

### What was built

**Foundation.Core:**
- `Entities/Accounts/Errors/MemberErrors.cs` — added two internal routing error codes
  (`PreviouslyRemovedNeedsReactivate`, `PreviouslyRemovedNeedsResend`) with explicit comments
  that they are service-to-endpoint routing codes, not public API codes.

**Foundation.Application:**
- `Auth/SendInviteService.cs` — `PreviouslyRemoved` branch now picks the correct routing code
  based on `existing.UserId`; the endpoint intercepts both before `ErrorHttpMapper`.

**Api:**
- `Accounts/AccountEndpoints.cs` — added 6 new member-management routes (`ListMembers`,
  `ChangeRole`, `ResendInvite`, `Suspend`, `Reactivate`, `Remove`); internal routing-code
  intercept in `SendInvite` translates to public `Member.PreviouslyRemoved` + `suggestedAction`;
  renamed `ParseRole` → `ParseRoleForInvite`; added `ParseRoleForManagement` (includes Owner);
  added `ParseDeliveryMode` (null/whitespace → Email default); added `ChangeRoleBody`,
  `ResendInviteBody` request records.
- `Program.cs` — registered `MemberManagementService` and `EfMemberManagementPersistence`.
- `Helpers/ErrorHttpMapper.cs` — safety-net 409 mappings for both internal routing codes
  (fires only if endpoint fails to intercept; will expose internal code name in that case).

**Integration Tests:**
- `tests/OpHalo.IntegrationTests/Api/MemberManagementTests.cs` — NEW: 28 tests covering all
  26 required scenarios from build-log/022 (tests 18, 23, 25 each split into sub-cases).
  Tests 25a/25b assert `code == "Member.PreviouslyRemoved"` (not the internal routing code).

### Key decisions applied

| # | Decision | Applied |
|---|----------|---------|
| Routing codes | Two internal routing errors in `MemberErrors`; endpoint intercepts, not ErrorHttpMapper | ✓ |
| Public contract | `suggestedAction: "reactivate"` or `"resend_invite"` in 409 response; code always `Member.PreviouslyRemoved` | ✓ |
| Safety-net | ErrorHttpMapper 409 arms for routing codes; acknowledged they leak internal names if fired | ✓ |

---

## Phase 5E-B — COMPLETE

Member management: Application + Infrastructure layer. 389/389 tests passing.

### What was built

**Foundation.Core:**
- `Entities/Accounts/Errors/MemberErrors.cs` — NEW: `Forbidden`, `NotFound`, `CannotModifySelf`, `CannotModifyOwner`, `PrimaryOwnerProtected`, `OwnerLimitReached`, `LastOwner`, `InvalidRole`, `InvalidStatusTransition`, `SeatLimitReached`, `PreviouslyRemoved`
- `Entities/Accounts/AccountUser.cs` — added `ChangeRole(newRole)`, `Reactivate()`, `RestoreInvite(tokenHash, expiresAtUtc, nowUtc)` domain methods; `Remove()` now unconditionally clears `InviteTokenHash`/`InviteExpiresAtUtc` (idempotent); `Reactivate()` also clears invite state

**Foundation.Application:**
- `Abstractions/Security/IAccountSessionService.cs` — added `RevokeAllSessionsByAccountUserId(accountId, accountUserId, ct)`
- `Members/InviteDeliveryMode.cs` — NEW: `Email = 1`, `ManualShare = 2`
- `Members/IMemberManagementPersistence.cs` — NEW: `GetMemberListContextAsync`, `GetMemberManagementContextAsync`, `CommitAsync`; `MemberListContext`, `MemberListItem`, `MemberManagementContext` records
- `Members/MemberManagementService.cs` — NEW: `ListMembersAsync`, `ChangeRoleAsync`, `SuspendAsync`, `ReactivateAsync`, `RemoveAsync`, `ResendInviteAsync(targetId, deliveryMode, ct)` → `Result<ResendInviteResult>`; `ResendInviteResult(DeliveryMode, InviteUrl?)`, `ListMembersResponse`, `MemberItem` response types
- `Auth/SendInviteService.cs` — Removed members now return routing codes for 5E-C endpoint intercept

**Foundation.Infrastructure:**
- `Security/AccountSessionService.cs` — implemented `RevokeAllSessionsByAccountUserId` via `ExecuteUpdateAsync` bulk revocation with accountId cross-check
- `Members/EfMemberManagementPersistence.cs` — NEW: 5-query context load (caller, account, entitlements, owner counts, target); list query with optional Removed filter; `CommitAsync` in transaction
- `Auth/EfInvitePersistence.cs` — expiry edge fix: `< nowUtc` → `<= nowUtc`

**Api:**
- `Helpers/ErrorHttpMapper.cs` — explicit 409 mappings for `Member.OwnerLimitReached`, `Member.LastOwner`, `Member.SeatLimitReached`, `Member.PreviouslyRemoved`; suffix 422 patterns for `.CannotModifySelf`, `.CannotModifyOwner`, `.PrimaryOwnerProtected`, `.InvalidStatusTransition`

**Tests:**
- `IntegrationTests/Api/InviteTests.cs` — updated `SendInvite_RemovedMember_Returns409` to expect `Member.PreviouslyRemoved`

---

## Phase 5D — COMPLETE

Member invites: send (`POST /accounts/me/invite`) + accept (`POST /accounts/invite/accept`). 389/389 tests passing.

### What was built

**Foundation.Core:**
- `Entities/Accounts/Enums/EntryContext.cs` — added `InvitedUser = 3`
- `Entities/Accounts/Errors/InviteErrors.cs` — NEW: `Forbidden`, `InvalidToken`, `Expired`, `AlreadyActive`, `SeatLimitReached`

**Foundation.Application:**
- `Auth/InviteTokenGenerator.cs` — NEW: 32-byte URL-safe Base64 token; uppercase SHA-256 hex hash (ADR-076)
- `Auth/IInvitePersistence.cs` — NEW: `GetSendInviteContextAsync`, `CommitSendInviteAsync`, `CommitAcceptInviteAsync`; `SendInviteContext`, `AcceptedInvite` records
- `Auth/SendInviteService.cs` — NEW: permission gate, seat-limit check, best-effort email
- `Auth/AcceptInviteService.cs` — NEW: blank-token guard, session outside transaction
- `Auth/InviteEmailTemplate.cs` — NEW
- `Auth/MagicLinkSettings.cs` — added `OperatorBaseUrl`

**Foundation.Infrastructure:**
- `Auth/EfInvitePersistence.cs` — NEW: 5-query context load; savepoint for User creation race; `ExecuteUpdateAsync` conditioned on `Invited` state

**Api:**
- `Accounts/AccountEndpoints.cs` — `POST /accounts/me/invite` + `POST /accounts/invite/accept`
- `Program.cs` — registered invite services
- `Helpers/ErrorHttpMapper.cs` — `Invite.SeatLimitReached` → 409

**Tests:**
- `UnitTests/Auth/InviteTokenGeneratorTests.cs` — NEW: 9 tests
- `IntegrationTests/Api/InviteTests.cs` — NEW: 26 tests

---

## Build state

- `dotnet build` → 0 errors, 0 warnings
- Architecture tests → 14/14 passing
- Unit tests → 280/280 passing
- Integration tests → 126/126 passing (95 pre-5E-C + 31 new)
- Total → 417/417 passing

---

## Watch-outs carried forward

- **ADR-058** — Superseded by ADR-061..064 in decision index.
- **AnonymousCurrentUser** — kept for potential worker/test use; not registered in production.
- **SystemClock** FQDN in Program.cs — `OpHalo.Foundation.Infrastructure.Services.SystemClock` (avoids collision).
- **Schema-drop reset pattern** — integration test factory uses `DROP SCHEMA public CASCADE` + recreate + `MigrateAsync`.
- **Migration generation** — always `--startup-project src/OpHalo.Keep.Infrastructure`; needs `ConnectionStrings__DefaultConnection` env var for design-time factory.
- **No GitHub remote yet.**
- **Resend `ApiKey` and `FromAddress`** — must be set via user secrets in production.
- **`App:PublicBaseUrl`** — must point at the public frontend/auth site.
- **`App:OperatorBaseUrl`** — must point at the operator app.
- **`UseRateLimiter` skipped in Testing** — intentional.
- **`CountActivePilotAccountsAsync`** — counts all `IsPilot = true` without filtering `CommercialState != Canceled`. Conservative; safe for now.
- **`SignupDefaultsSettings` startup validation** — `TrialDurationDays <= 0` and `MaxPilotAccounts <= 0` not validated at startup. Add `IValidateOptions<SignupDefaultsSettings>` in a follow-up.
- **Session creation failure test (invite accept)** — not covered. Would require overriding `IAccountSessionService`. Deferred.
- **Mobile invite accept** — deferred (D9). Needs `clientType` parsing and bearer response.
- **`SendInvite` manual-share** — `POST /accounts/me/invite` does not yet support `manual_share` delivery mode. `POST .../resend-invite` does. Deferred for invite send.
- **ErrorHttpMapper safety-net arms** for `Member.PreviouslyRemovedNeedsReactivate` / `PreviouslyRemovedNeedsResend` — will expose internal code names if the `SendInvite` intercept fails. Known; acceptable; covered by tests.

---

## Next session

Phase 5E is complete (5E-A discovery, 5E-B application + infrastructure, 5E-C API + tests).

Next: read the build plan for the next phase and determine what to tackle next. Likely candidates are Phase 5F (account settings API) or Phase 8 (account admin shell) depending on the build plan order.
