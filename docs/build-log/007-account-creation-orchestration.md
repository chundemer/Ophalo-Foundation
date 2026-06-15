# Build Log 007 — Account-creation orchestration (first composing caller)

**Date:** 2026-06-14
**Phase:** Post-4d — the first application service that *composes* the Phase 4 factories
**Reference repo:** `/Users/christian/application/ophalo` (read-only; aliased at `_reference/`)
**Target repo:** `/Users/christian/saas/ophalo-foundation`

---

## Purpose

Phase 4 produced the parts (Account, User, AccountUser, AccountEntitlements + their factories
and the three standalone access policies) but **no caller assembled them**. This slice adds the
first one: given verified account-creation inputs, build the canonical object graph that should
exist for a brand-new account.

It deliberately answers **only** *"what graph should exist?"* — not *"how is it persisted?"*.
The save contract, transaction boundary, unique constraints, and aggregate boundary are
**persistence-phase** questions, settled after the domain graph is fixed (decided with Christian).

---

## What was built

**`OpHalo.Foundation.Application`** (new `Accounts/Provisioning/` folder, parallel to
`Accounts/Access/`, `Accounts/Authorization/`, `Accounts/Entitlements/`)
- `AccountProvisioningService.cs` — a stateless service with one method,
  `CreateVerified(email, name, businessName, purpose, timeZone, plan, isPilot, nowUtc,
  trialEndsAtUtc) → Result<AccountProvisioningResult>`. Composes the factories in order,
  enforces the cross-aggregate rules, sources seats from the entitlement catalog.
- `AccountProvisioningResult.cs` — a `record` carrying the assembled graph
  (`User` + `Account` + owner `AccountUser` + `AccountEntitlements`).

**`OpHalo.Foundation.Core`**
- `AccountErrors.cs` — added the provisioning composition errors (`InternalAccountPlanMismatch`,
  `InternalAccountCannotBePilot`, `InternalAccountAllowsNoTrialWindow`, `TrialWindowRequired`).
  Kept in Core `AccountErrors` to honor its documented "single source of truth" directive
  ("referenced by both Core entity methods and Application handlers; do not duplicate elsewhere").

**`OpHalo.UnitTests`**
- `Accounts/AccountProvisioningServiceTests.cs` — happy paths (business trial / pilot / internal),
  primary-owner assignment, plan-derived seats, and the five fail-closed composition rules (+13).

No new abstractions: **no repositories, no `IUnitOfWork`, no MediatR, no command/handler, no DI**
registration. By design (ADR-039).

---

## Composition order (the value of the slice)

1. `User.CreateVerified(email, name, nowUtc)`
2. `Account.CreateVerified(businessName, purpose, timeZone)`
3. `AccountUser.CreateOwner(account.Id, user.Id, email, normalizedEmail)`
4. `account.AssignPrimaryOwner(owner)` → `Result` (propagated on failure)
5. `AccountEntitlements.CreateInternal / CreatePilot / CreateTrial(account.Id, plan, seats, …)`
6. return the graph

Step 4 is the point of the design: because `BaseEntity.Id` is generated at construction
(`Guid.CreateVersion7`), the owner membership already has its Id and AccountId, so the
self-validating `AssignPrimaryOwner` (ADR-024) checks same-account / Owner / Active **in-memory**
— no persistence round-trip, no nullable-FK window leaking out of the service.

---

## Cross-aggregate rules (fail-closed)

The internal-account triad and the trial window are enforced before any entity is built:

| Input | Outcome |
|-------|---------|
| `purpose = Internal`, `plan ≠ Internal` | `InternalAccountPlanMismatch` |
| `purpose ≠ Internal`, `plan = Internal` | `InternalAccountPlanMismatch` |
| `purpose = Internal`, `isPilot = true` | `InternalAccountCannotBePilot` |
| `purpose = Internal`, `trialEndsAtUtc` set | `InternalAccountAllowsNoTrialWindow` |
| `purpose ≠ Internal`, `trialEndsAtUtc` null | `TrialWindowRequired` |

Seats are **plan-derived**, never caller-supplied:
`PlanEntitlements.LimitDefaults[plan][FeatureLimitKeys.Account.UserLimit]` (ADR-037). The method
takes no seat parameter — the design makes seat drift between account and entitlements
unrepresentable.

Per-field input validity (non-empty business name, UTC timestamps, IANA time zone) is **trusted
to the entity factories**, which throw `ArgumentException` on garbage. `Result.Failure` is
reserved for cross-aggregate composition rules; validation lives upstream (ADR-022).

---

## Preserved / Added / Adapted

**Added (no legacy equivalent):** the provisioning service and its result graph. The legacy app
created the graph inline in handlers; this extracts the composition into one auditable place with
the ordering invariants made explicit.

**Adapted (with rationale):**
- **Pure domain composition, persistence deferred** (ADR-039) — the slice introduces no
  persistence abstractions; Phase 6 owns the save contract after the graph is settled. Avoids the
  classic early-repository churn at exactly the seam most prone to it.
- **`DateTime` (UTC), not `DateTimeOffset`** — the approved sketch used `DateTimeOffset`, but every
  Phase 4 entity factory takes `DateTime` with a `Kind == Utc` guard. The service matches the
  entity contracts and the established idiom rather than convert at each call site.
- **Provisioning errors in Core `AccountErrors`** — these are account-domain consistency rules
  (account ↔ entitlements pairing), and `AccountErrors` is the declared single source of truth.

---

## Tests

- **Unit:** +13. Business trial / pilot / internal happy paths assert the full graph
  (normalized user email, account identity/purpose, owner role+status+UserId+AccountId,
  entitlements account/plan/commercial-state/trial/pilot); primary owner assigned to the created
  owner membership; `MaxUserSeats` sourced from plan defaults (theory over Starter/Professional/
  Business + Internal); five fail-closed composition rules. `OpHalo.UnitTests` now **176 passing**
  (was 163).
- **Architecture:** unchanged, **14 passing** — service depends only on Core entities + the
  Application entitlement catalog (Application→Core allowed); no Infrastructure dependency entered
  Application (§8 stayed green).
- Build: **0 warnings / 0 errors**. Full run green (UnitTests 176, ArchitectureTests 14,
  IntegrationTests 1).

**Not independently testable:** the step-4 `AssignPrimaryOwner` *failure* branch. The service
always builds a same-account, Owner-role, Active membership, so the failure is structurally
unreachable through the public surface without inventing an injection seam (which ADR-039
forbids). It remains in code as defensive propagation; the `AssignPrimaryOwner` invariants
themselves are independently covered in `AccountLifecycleTests` / `AccountUserMembershipTests`.

---

## Exit gate

- ✅ First composing caller exists; the four Phase 4 factories assemble into one verified graph.
- ✅ Creation ordering + the in-memory primary-owner assignment are enforced and tested.
- ✅ Internal-account triad and trial window enforced fail-closed.
- ✅ Seats plan-derived (ADR-037); account ↔ entitlements drift unrepresentable.
- ✅ No persistence/DI/handler abstractions introduced; Foundation→Keep boundary untouched.

---

## Risks / follow-ups

- **No persistence.** The graph is assembled but not saved. Phase 6 owns the transaction,
  the unique normalized-email index, duplicate-email handling, and the save contract — and is
  the natural consumer of this service.
- **No DI registration / no endpoint.** `AccountProvisioningService` is `new`-able and pure; it is
  wired into the composition root and an endpoint when the auth/exchange flow (Phase 5) needs it.
  The combined `allowed && permitted && enabled` access check is still deferred — account
  *creation* precedes membership, so it is not the caller that surfaces that facade (ADR-038).
- **Trial length is caller-supplied** (`trialEndsAtUtc`). No trial-duration policy/config is
  modeled yet; the caller computes the end date. Centralizing it is a later config concern.
- **Plan/seat coupling is one-directional** — provisioning seeds `MaxUserSeats` from the plan
  default. A later *seat override* is a separate entitlement mutation after provisioning, not part
  of initial creation (decided with Christian).
