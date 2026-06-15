# Build Log 005 — Phase 4c: Permission keys + role access policy

**Date:** 2026-06-14
**Phase:** 4c — the "User is permitted" half of ADR-007 (build plan §4.8, permission-key layer)
**Reference repo:** `/Users/christian/application/ophalo` (read-only; aliased at `_reference/`)
**Target repo:** `/Users/christian/saas/ophalo-foundation`

---

## Purpose

4a/4b built **"Account is entitled"** (`AccountAccessPolicy` + its `AccountEntitlements` producer).
This slice builds the orthogonal **"User is permitted"** layer: a role→permission map gated by
membership status and account purpose, expressed through **permission keys** instead of the
scattered role checks the build plan warns against.

The legacy app has **no permission-key layer** — it gates with ad-hoc role checks and a single
`AdminGuard` (`AccountPurpose == Internal`). So this is genuinely *added*, not ported; the one
behavior preserved is the AdminGuard semantic (internal authority derives from the account's
purpose boundary, not a role name).

Deliberately **not** in this slice (→ Phase 4d / later per ADR-031): feature keys
(`keep.enabled`, `keep.public_intake`, …), usage limits (`account.user_limit`,
`keep.monthly_request_limit`), plan packaging, billing, persistence, and any `AccountEntitlements`
change.

---

## What was built

**`OpHalo.Foundation.Application`** (new `Accounts/Authorization/` folder, parallel to `Accounts/Access/`)
- `PermissionKeys.cs` — Foundation-owned string catalog, `domain.resource.action` convention
  (ADR-032), grouped `Account` / `Keep` / `Internal`; `InternalPrefix = "internal."` marks
  purpose-gated keys. Comments lock the permission-vs-feature distinction.
- `RolePermissions.cs` — two `FrozenDictionary<AccountUserRole, FrozenSet<string>>` maps,
  `Base` (`account.*`/`keep.*`, all accounts) and `Internal` (`internal.*`, Internal-purpose only).
  Hierarchy via **explicit set composition** (`[.. lower, …higher]`), never enum comparison (ADR-033).
- `IUserAccessPolicy.cs` / `UserAccessPolicy.cs` — `IsPermitted(role, membershipStatus,
  accountPurpose, key)`; three composed gates, fail-closed (ADR-034).

**`OpHalo.UnitTests`**
- `Accounts/UserAccessPolicyTests.cs` — the full role/status/purpose/key matrix (+40 tests).

---

## Catalog + matrix (locked this session)

Convention `domain.resource.action`; **permission key ≠ feature key** (ADR-031).

| Role | Base permissions (cumulative) |
|------|------|
| Viewer | `account.view`, `keep.requests.view`, `keep.insights.view` |
| Operator | + `keep.requests.{create,update,close,respond}`, `keep.updates.send`, `keep.customer_messages.send`, `keep.internal_notes.add` |
| Admin | + `account.{settings,members,notifications}.manage`, `account.audit.view`, `keep.settings.manage` |
| Owner | + `account.billing.manage` |

`internal.*` (only when `AccountPurpose == Internal`, same hierarchy): Viewer
`internal.accounts.view`; Admin + `internal.accounts.manage`, `internal.entitlements.manage`.
Operator currently mirrors Viewer and Owner mirrors Admin here until the deferred
`internal.support.*` / `internal.platform.manage` keys land.

**Matrix decisions:** `account.billing.manage` is **Owner-only** (ownership-level authority);
Operator **does** get `keep.insights.view` (operational visibility — volume/stale/timing).

---

## Preserved / Added / Adapted

**Preserved (behavior):**
- The `AdminGuard` semantic — internal capability requires `AccountPurpose == Internal`, not a
  role name. Now generalized into the composed `internal.*` gate rather than a one-off guard.

**Added (no legacy equivalent):**
- The entire permission-key catalog, role→permission maps, and `UserAccessPolicy`. Legacy had
  scattered role checks; the build plan §4.8 calls for keys "instead of scattering role checks".

**Adapted (with rationale):**
- **Hierarchical `domain.resource.action` naming** adopted over the plan's flat-verb examples
  (ADR-032) — predictable, testable, room to grow.
- **`keep.customer.message → keep.customer_messages.send`** for symmetry with `keep.updates.send`
  / `keep.internal_notes.add` and to stay distinct from business `updates.send`.
- **Hierarchy by explicit composition, not `role >= Admin`** (ADR-033) — enum values are identity,
  not authorization arithmetic.
- **Fail-closed, no trimming** (ADR-034) — empty/whitespace/unknown/non-canonical keys denied;
  callers own canonical keys. Prevents typos from leaking access if map logic changes.

---

## Tests

- **Unit:** +40. Membership gating (Invited/Suspended/Removed → nothing, even Owner); the full
  per-role base matrix; `internal.*` denied in Business accounts for every base role; `internal.*`
  granted by role only in Internal accounts; internal keys still require Active membership;
  Internal-account members retain base permissions; fail-closed cases
  (empty / whitespace / `" account.view "` / unknown / deferred `internal.platform.manage`).
  `OpHalo.UnitTests` now **139 passing** (was 99).
- **Architecture:** unchanged, **14 passing** — the catalog is strings only, so no Keep reference
  entered Foundation (§8 boundary stayed green).
- Build: **0 warnings / 0 errors**. Full run green (UnitTests 139, ArchitectureTests 14, IntegrationTests 1).

---

## Phase 4c exit gate

- ✅ Role-to-permission mapping has unit tests (build plan §4 exit gate item).
- ✅ Permission keys replace scattered role checks; the `AdminGuard` purpose semantic is preserved.
- ✅ Membership-status and account-purpose composition enforced and fail-closed.
- ✅ Foundation has no Keep references; architecture tests stay green.

---

## Risks / follow-ups

- **Feature-key layer (§4.11) is the natural next slice — Phase 4d.** Feature keys + usage limits;
  composes with this policy (permitted **and** entitled), never collapses into it.
- **Not yet wired to callers.** No handler/endpoint calls `UserAccessPolicy` yet — request-pipeline
  authorization (e.g. a behavior or endpoint filter) arrives with the handler/auth phases.
- **No persistence.** Permission keys are static config, not stored state; nothing to migrate here.
- **Deferred internal keys** (`internal.support.*`, `internal.platform.manage`) — add only when a
  real caller/ADR needs them; `internal.platform.manage` especially is a broad "magnet" key.
- **`IUserAccessPolicy` not yet DI-registered** — composition-root wiring lands when a caller needs it.
