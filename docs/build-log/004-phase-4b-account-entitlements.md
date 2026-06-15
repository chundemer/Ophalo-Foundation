# Build Log 004 — Phase 4b: AccountEntitlements (commercial posture + state machine)

**Date:** 2026-06-14
**Phase:** 4b — the entitlement producer for the §4.8–§4.10 access policy (build plan §4.11, posture subset)
**Reference repo:** `/Users/christian/application/ophalo` (read-only; aliased at `_reference/`)
**Target repo:** `/Users/christian/saas/ophalo-foundation`

---

## Purpose

Phase 4a built `AccountAccessPolicy` + `AccountAccessContext`, but four context inputs had
**no producer**: `CommercialState`, `TrialEndsAtUtc`, `GracePeriodEndsUtc`, `OperatingMode`.
This slice adds `AccountEntitlements` — the entity that owns commercial/operating posture —
and the bridge that feeds the policy real data. The loop from account commercial state →
access decision is now closed end-to-end.

Deliberately **not** in this slice (deferred per ADR-027): feature-key authorization, the
Stripe/billing/payment block, and provisioning keys.

---

## What was built

**`OpHalo.Foundation.Core`**
- `Entities/Accounts/Enums/AccountPlan.cs` — `Trial/Starter/Professional/Business/Enterprise/Internal`, explicit 1-based values (ADR-029).
- `Entities/Accounts/AccountEntitlements.cs` — entity with `AccountId`, `Plan`, `CommercialState`,
  `OperatingMode`, `TrialEndsAtUtc`, `PastDueGraceEndsAtUtc`, `MaxUserSeats`, `IsPilot`; the commercial
  state machine (`MarkPastDue`, `ResolvePastDue`, `ExpireTrial`, `Cancel`, `EnterOffSeason`,
  `ResumeFromOffSeason`); provisioning factories (`CreateTrial`, `CreatePilot`, `CreateInternal`).
- `Entities/Accounts/Errors/AccountErrors.cs` — +4 errors (`CommercialAccessAlreadyCanceled`,
  `AlreadyInOffSeason`, `OffSeasonEntryNotAllowed`, `NotInOffSeason`). `NotPastDue` already existed.

**`OpHalo.Foundation.Application`**
- `Accounts/Access/AccountEntitlementsAccess.cs` — `ToAccessContext` extension mapping the entity
  (+ Account lifecycle/purpose + off-season request flag + clock) into `AccountAccessContext` (ADR-030).

**`OpHalo.UnitTests`**
- `Accounts/AccountEntitlementsTests.cs` — factories, the full state machine, and the
  `ToAccessContext → AccountAccessPolicy` closed-loop path (+28 tests).

Doc touch-ups: `AccountCommercialState` / `AccountOperatingMode` comments no longer call
`AccountEntitlements` "deferred".

---

## Preserved / Moved / Renamed / Adapted / Redesigned

**Preserved (behavior):**
- The commercial transition semantics — past-due grace window, idempotent re-mark, blocking
  past-due from terminal Canceled/Expired, trial expiry as an idempotent no-op, off-season
  requiring Active. Logic matches the reference `AccountEntitlements`.

**Renamed:**
- `CancelCommercial → Cancel`, `ResolvePastDueBilling → ResolvePastDue` (ADR-027) — the billing
  qualifier is meaningless until the billing block exists.
- Legacy `GracePeriodEndsUtc → PastDueGraceEndsAtUtc` on the entity (maps to the context's
  `GracePeriodEndsUtc` parameter in `ToAccessContext`).
- Legacy `StartPilot` replaced by an explicit `CreatePilot` factory (pilot = Trial + `IsPilot`).

**Adapted / Redesigned (with rationale):**
- **Halo booleans dropped** (ADR-028) — excluded families; no `keep.enabled` placeholder.
- **`IsDelinquent`/`DelinquentSinceUtc` dropped** (ADR-027) — `CommercialState == PastDue` plus a
  non-null grace window is the single source of truth; the boolean was a denormalized dual-write.
- **Stripe/billing/payment fields and `ProvisioningKey` not ported** — billing-integration and auth
  phases respectively (build plan §4.11 "no Stripe catalog yet").
- **`Pilot` is a flag, not a plan** (ADR-029); kept singular (`IsPilot`), no flag-bag.
- **`ToAccessContext` homed in Application, not on the Core entity** (ADR-030) — Core⊀Application.

---

## Tests

- **Unit:** +28 (factory provisioning + validation guards; every state-machine branch incl.
  idempotency and terminal-state blocks; the `ToAccessContext` field mapping and a real
  policy decision through it). `OpHalo.UnitTests` now **99 passing** (was 71).
- **Architecture:** unchanged, **14 passing** — the new Application mapper introduced **no**
  Core→Application dependency (the boundary that forced the mapper out of Core stayed green).
- Build: **0 warnings / 0 errors**. Full run green (UnitTests 99, ArchitectureTests 14, IntegrationTests 1).

---

## Phase 4b exit gate

- ✅ The access policy now has a real producer for all four previously-unsourced context fields.
- ✅ Commercial state machine unit tests green (past-due/grace, resolve, expire, cancel, off-season).
- ✅ `ToAccessContext` drives `AccountAccessPolicy` to correct postures (Trial full-access → blocked after expiry).
- ✅ Build + architecture tests stay green; no excluded-family structure pulled into Foundation.

---

## Risks / follow-ups

- **No persistence yet.** EF config for `AccountEntitlements` (one-to-one with `Account`, the
  `AccountId` FK/unique index) lands in the persistence phase, alongside the 4a entities.
- **Provisioning wiring deferred.** Nothing yet *creates* an `AccountEntitlements` alongside
  `Account.CreateVerified` — the create-account handler that calls `CreateTrial/CreatePilot/CreateInternal`
  arrives with the account-creation orchestration (handler phase).
- **Feature authorization deferred.** `keep.*` feature keys + role→permission map are the next
  candidate slice (permissions / feature-auth).
- **Billing-integration deferred.** Stripe ids, billing dates, payment-failure counts, delinquency —
  a later phase; the state machine here is the producer those events will drive.
- **`CreateInternal` sets `CommercialState = Active`** as an informational default — the policy
  bypasses commercial checks for internal accounts via purpose, so the value is never read.
