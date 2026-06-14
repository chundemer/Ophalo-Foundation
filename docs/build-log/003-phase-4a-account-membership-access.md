# Build Log 003 — Phase 4a: Account / User / AccountUser + Lifecycle + Access Policy

**Date:** 2026-06-14
**Phase:** 4a — Account/membership domain + account-access posture (build plan §4.8–§4.10, partial)
**Reference repo:** `/Users/christian/application/ophalo` (read-only; aliased at `_reference/`)
**Target repo:** `/Users/christian/saas/ophalo-foundation`

---

## Purpose

Land the first slice of the Foundation account domain: the `Account`, `User`, and
`AccountUser` entities (trimmed and membership-only per Phase 4 discovery), the
account enums and errors they need, and the **account-access policy** ported verbatim
from the reference. This is the "account posture / lifecycle" exit-gate slice. Permission
keys, feature-key entitlements, the `AccountEntitlements` entity, the invitations flow,
and sessions/devices remain deferred to later Phase 4 / Phase 5 sessions.

---

## What was built

**`OpHalo.Foundation.Core`**
- `Entities/Shared/BaseEntity.cs` — ported verbatim (identity, audit timestamps, soft-delete).
- `Helpers/EmailNormalizer.cs` — ported; **homed in Foundation.Core, not SharedKernel** (email is a business concept, §8).
- `Entities/Accounts/Enums/` — `AccountUserRole`, `MembershipStatus`, `AccountLifecycleState`, `AccountPurpose`, `AccountCommercialState`, `AccountOperatingMode`. All carry explicit numeric values (ADR-026).
- `Entities/Accounts/Errors/` — trimmed `AccountErrors` (lifecycle/access/commercial + primary-owner invariants) and `AccountUserErrors` (membership transitions).
- `Entities/Users/User.cs` — ported near-verbatim.
- `Entities/Accounts/Account.cs` — trimmed (ADR-018/019/020/021/022).
- `Entities/Accounts/AccountUser.cs` — membership-only (ADR-016/017/023/025).

**`OpHalo.Foundation.Application`**
- `Accounts/Access/` — `AccountAccessContext`, `AccountAccessDecision`, `AccountAccessPosture`, `AccountAccessReason`, `IAccountAccessPolicy`, `AccountAccessPolicy`. Logic **verbatim**; only namespaces and `Result/Error` home changed.

**`OpHalo.UnitTests`**
- `Accounts/AccountLifecycleTests.cs`, `Accounts/AccountUserMembershipTests.cs`, `Accounts/AccountAccessPolicyTests.cs`.
- Added `Foundation.Core` + `Foundation.Application` project references to the test csproj.

---

## Preserved / Moved / Renamed / Adapted / Redesigned

**Preserved (verbatim or near-verbatim behavior):**
- `BaseEntity`, `EmailNormalizer`, `User`.
- `AccountAccessPolicy` evaluation logic and all decision branches.
- `Account` lifecycle guards (`Suspend`/`Close`/`Reactivate`) and `AccountUser` `Activate`/`RefreshInvite`/`Remove` semantics.

**Moved / renamed:**
- Namespaces re-homed `OpHalo.Core.* → OpHalo.Foundation.Core.*`, `OpHalo.Application.* → OpHalo.Foundation.Application.*`, `OpHalo.Shared.Results → OpHalo.SharedKernel.Results`. `EmailNormalizer` moved `OpHalo.Shared.Helpers → OpHalo.Foundation.Core.Helpers`.
- `MembershipStatus.PendingInvite → Invited` (ADR-016); legacy roles `Technician/Member → Operator/Viewer` (ADR-015). The `AccountUserRole` enum, mis-namespaced in the reference, now sits correctly under `…Accounts.Enums`.

**Adapted / Redesigned (with rationale):**
- **`Account.Email` dropped** (ADR-021) — account identity is the business; inbox identity lives on `User`/`AccountUser`.
- **`Account.CreateVerified`** trimmed to `(businessName, purpose, timeZone)` with required, non-nullable `BusinessName`/`TimeZone` (ADR-022). The legacy create-then-name flow and slug/intake-token/quota machinery are not ported.
- **`AccountUser.IsActive` is computed** from `MembershipStatus` (ADR-023) — kills the dual-write hazard with the new `Suspended` status.
- **`AccountUser.Suspend()`** added for the new membership-level `Suspended` (ADR-016): `Active→Suspended` only; `Suspended→Suspended` idempotent; `Invited`/`Removed` → `InvalidStatusTransition`.
- **`AccountUser.CreateOwner`** now sets `Role = Owner` (was legacy `Admin`); the `IsPrimaryOwner` flag and welcome/install-prompt/push fields are gone.
- **Ownership** moved to `Account.PrimaryOwnerAccountUserId` with a self-validating `AssignPrimaryOwner(AccountUser)` method (ADR-019/024).
- **`CreatePendingInvite`** now accepts any non-owner role (ADR-025), replacing the legacy Member-only constraint.
- Obsolete `AccountCommercialState.Pilot` dropped; commercial enum renumbered (ADR-026).

---

## Tests

- **Unit:** +38 tests (account lifecycle + creation/owner invariants; AccountUser membership transitions + factories; the full access-posture matrix). `OpHalo.UnitTests` now **71 passing** (was 33).
- **Architecture:** unchanged, **14 passing** — Foundation has no Keep references; Core depends only on SharedKernel; Application depends on Core + SharedKernel only.
- Build: **0 warnings / 0 errors**. Full run green (UnitTests 71, ArchitectureTests 14, IntegrationTests 1).

---

## Phase 4a exit gate (plan §Phase 4, posture/lifecycle slice)

- ✅ Account-posture unit tests green (lifecycle blocks, Internal bypass, Trial/PastDue-grace/Expired/Canceled, OffSeason read-only).
- ✅ Foundation has no Keep references (arch tests green).
- ✅ Build + architecture tests stay green.

---

## Risks / follow-ups

- **No persistence yet.** EF configuration (FKs for `PrimaryOwnerAccountUserId`, `AccountUser.UserId`, unique indexes on normalized email, mapping the computed `IsActive`) arrives with the persistence phase. The computed `IsActive` (ADR-023) must be handled there (ignore or map to a stored shadow if querying needs it).
- **No member-reactivate.** `Suspended → Active` for a membership is intentionally absent (out of 4a scope; arrives with the invitations/admin flow).
- **Internal-account creation** still flows through `CreateVerified`, so it requires a `BusinessName` — revisit if internal/admin accounts need a distinct factory.
- **Deferred (not 4a):** permission keys + role→permission map, feature-key entitlements (§4.11), `AccountEntitlements` entity, invitations flow, sessions/devices (Phase 5).
