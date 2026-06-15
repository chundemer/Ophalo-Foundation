# Session Log — OpHalo Foundation

**Last updated:** 2026-06-14
**Next session tier:** Tier 1 — Discovery · **next slice = Phase 4d (feature keys / entitlements, §4.11)**
**Branch:** `main` (no remote yet) · Phase 4b committed (`7cf49aa`); Phase 4c committed (`034eee4`)

> Phase 4c (**permission keys + role access policy**, the "User is permitted" half of ADR-007) is
> **committed** (`034eee4`); Phase 4b committed earlier this session (`7cf49aa`). The authz **core**
> is now complete: Account entitled (4a/4b) + User permitted (4c). Working tree clean. Next is
> Phase 4d (feature keys, §4.11) — run discovery and confirm before code.

---

## Where we are

Phases 0–4c complete and committed. **Phase 4c committed this session** (`034eee4`):
the permission-key catalog, role→permission maps, and `UserAccessPolicy`. Build clean (0 warnings).
**Tests: UnitTests 139, ArchitectureTests 14, IntegrationTests 1 — all green.**

| Phase | Status | Notes |
|-------|--------|-------|
| 1 — Skeleton + architecture tests | ✅ done | `00227b0` |
| 2 — Legacy exclusion (doc) | ✅ done | `2fce382` |
| 3 — SharedKernel + abstraction cleanup | ✅ done | `2fce382` |
| 4a — Account/User/AccountUser + lifecycle + access policy | ✅ done | `ec4c35c`, build-log/003 |
| 4b — AccountEntitlements (commercial posture producer) | ✅ done | `7cf49aa`, build-log/004 |
| 4c — Permission keys + role access policy (User permitted) | ✅ done | `034eee4`, build-log/005 |
| 4d — Feature keys / entitlements / usage limits (§4.11) | ⬜ not started | next discovery |

## What 4c shipped

- **Application** (new `Accounts/Authorization/`): `PermissionKeys` (string catalog,
  `domain.resource.action`), `RolePermissions` (`Base` + `Internal` frozen maps, explicit set
  composition), `IUserAccessPolicy` / `UserAccessPolicy`
  (`IsPermitted(role, membershipStatus, accountPurpose, key)`, fail-closed).
- **Tests:** `UserAccessPolicyTests` — full role/status/purpose/key matrix (+40).
- **Decisions:** ADR-031 (scope = §4.8 only, permission≠feature, split from 4d), ADR-032 (hierarchical
  naming, Foundation-owned string catalog), ADR-033 (hierarchy by explicit composition, two maps),
  ADR-034 (3-gate composed policy; Active-only; `internal.*` requires Internal purpose; fail-closed;
  billing Owner-only; Operator gets `keep.insights.view`).

## The locked 4c model (for reference)

- **Permission key** = "can this *user* do this *action*?" · **Feature key** = "is this *account*
  allowed this *capability*?" — distinct catalogs, **compose later, never collapse**.
- Roles Owner ⊇ Admin ⊇ Operator ⊇ Viewer via explicit sets. Billing = Owner-only.
  Operator gets `keep.insights.view`.
- `internal.*` honored **only** when `AccountPurpose == Internal` (preserves the legacy
  `AdminGuard` semantic). Only Active members are permitted anything.
- Deferred internal keys: `internal.support.*`, `internal.platform.manage` (no caller yet).

## Scope intentionally deferred (decided with Christian)

- **Feature keys + usage limits** (§4.11) → **Phase 4d** (next). Composes with `UserAccessPolicy`
  (permitted **and** entitled).
- **Stripe / billing / payment block** → billing-integration phase.
- **Wiring `UserAccessPolicy` into the request pipeline** (behavior/endpoint filter, DI registration)
  → handler/auth phases, when a caller exists.

## Next — candidate slices (pick one at discovery, confirm with Christian)

- **Phase 4d — Feature keys / entitlements (§4.11)** *(recommended next)* — feature-key catalog
  (`keep.enabled`, `keep.public_intake`, …) + usage limits (`account.user_limit`,
  `keep.monthly_request_limit`) as explicit entitlement fields / snapshot on/around
  `AccountEntitlements`. **No** generic flag-rules engine, Stripe catalog, or plan-matrix UI (§4.11).
  Completes the Phase 4 entitlement exit gate.
- **Account-creation orchestration** — the handler calling `Account.CreateVerified` +
  `AccountEntitlements.CreateTrial/Pilot/Internal` + `AssignPrimaryOwner` together.
- **Persistence phase** — EF config/migrations for the 4a + 4b entities (Infrastructure;
  Christian runs `dotnet ef` / migrations himself).
- **Invitations flow** — handler/orchestration around `CreatePendingInvite`/`Activate`.

## Reference source map (for next discovery)

- Feature keys: build plan §4.11 lists `keep.*` capability keys + `account.user_limit` /
  `keep.*_limit`. Legacy gated capability via **halo booleans** (dropped in ADR-028) and
  `ContinuityEntitlementSnapshot` (`_reference/src/OpHalo.Continuity.Core/ReadModels/`).
- Legacy entitlements entity: `_reference/src/OpHalo.Core/Entities/Accounts/AccountEntitlements.cs`.
- Permissions (done): `Accounts/Authorization/` in Foundation.Application.

## Watch-outs / debt carried forward

- **No persistence yet** — EF config for `Account`/`AccountUser`/`User`/`AccountEntitlements`
  (one-to-one `Account↔Entitlements`, FKs, unique normalized-email index, computed
  `AccountUser.IsActive` per ADR-023) lands in the persistence phase. Permission keys are static
  config — nothing to migrate there.
- **Nothing provisions `AccountEntitlements` yet** — `CreateTrial/Pilot/Internal` factories exist
  but no handler calls them (account-creation orchestration slice).
- **`UserAccessPolicy` not yet wired/registered** — no caller invokes it yet; DI + pipeline
  integration arrive with the handler/auth phases.
- **`CreateInternal` sets `CommercialState = Active`** informationally — the policy bypasses
  commercial checks for internal accounts via purpose, so it is never read.
- Never glob through `_reference/**/bin` (recursive nesting). Read specific source paths.
- Legacy `decision-index`/`decisions/**`/`coding-rules` remain **pending validation** — do not load.
- No GitHub remote yet. When added, repo must be named `ophalo-foundation`.
