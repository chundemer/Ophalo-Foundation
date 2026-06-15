# Build Log 006 — Phase 4d: Feature keys + entitlements (§4.11)

**Date:** 2026-06-14
**Phase:** 4d — the "Account is entitled" capability layer (build plan §4.11, ADR-009)
**Reference repo:** `/Users/christian/application/ophalo` (read-only; aliased at `_reference/`)
**Target repo:** `/Users/christian/saas/ophalo-foundation`

---

## Purpose

4a/4b answered *"is the account commercially allowed?"* (`AccountAccessPolicy` over
`AccountEntitlements`'s commercial state). 4c answered *"can this user do this action?"*
(`UserAccessPolicy` over permission keys). This slice answers the third, orthogonal question —
*"which capabilities is this account entitled to?"* — via **feature keys** (ADR-009), the analog
of 4c's permission-key layer but keyed on the account's **plan**, not the member's role.

The legacy app gated capability with **halo booleans** (`HasContinuityHalo`, …), dropped in
ADR-028 as a second gating model. So this is genuinely *added*, not ported: plans produce
entitlements; runtime checks the feature key, never the plan name.

Deliberately **not** in this slice: a generic feature-flag/rules engine, Stripe catalog, overage
billing, plan-matrix admin UI (§4.11), persistence, and any combined permitted+entitled facade
(no caller exists yet).

---

## What was built

**`OpHalo.Foundation.Application`** (new `Accounts/Entitlements/` folder, parallel to
`Accounts/Authorization/` and `Accounts/Access/`)
- `FeatureKeys.cs` — Foundation-owned string catalog of **boolean capabilities**
  (`domain.capability`); all v1 keys are `keep.*` (build plan §4.11). Strings only → no Keep
  reference enters Foundation (§8).
- `FeatureLimitKeys.cs` — **separate** catalog of **numeric allowances** (`domain.x_limit`):
  `account.user_limit`, `keep.monthly_request_limit`; `Unlimited = int.MaxValue` sentinel.
  Separation locks the "is it enabled?" vs "what is the allowance?" distinction (ADR-036).
- `PlanEntitlements.cs` — static `AccountPlan → FrozenSet<feature>` and
  `AccountPlan → FrozenDictionary<limit,int>` maps. Features built by **explicit set composition**
  (`KeepCore → Starter → Full`, explicit `InternalFeatures` alias) — the same idiom as
  `RolePermissions`, never enum arithmetic. "Plans produce entitlements" (ADR-035).
- `IFeatureAccessPolicy.cs` / `FeatureAccessPolicy.cs` — `IsEnabled(plan, key)`,
  `GetLimit(plan, key)`, `ResolveLimit(entitlements, key)`; fail-closed (ADR-038).

**`OpHalo.UnitTests`**
- `Accounts/FeatureAccessPolicyTests.cs` — the plan/feature/limit/override matrix (+24 tests).

No Core change: `AccountEntitlements` stays compact (Plan/CommercialState/OperatingMode/
MaxUserSeats/IsPilot). Feature capabilities are **not** stored per-account (ADR-035).

---

## Provisional matrix (NOT locked — §4.11)

Features (cumulative): **KeepCore** = `keep.enabled`, `public_intake`, `customer_page`,
`operator_queue`, `request_detail`, `operator_messaging`, `customer_messaging`, `internal_notes`,
`close_request`, `sse_live_updates`, `email_notifications`, `request_subscriptions`.

| Plan | Features | `account.user_limit` | `keep.monthly_request_limit` |
|------|----------|------|------|
| Trial | Full (eval) | 3 | 50 |
| Starter | KeepCore + `browser_push` | 3 | 250 |
| Professional | + `mobile_push`, `insights` (= Full) | 10 | 1500 |
| Business | Full | 25 | 6000 |
| Enterprise | Full | Unlimited | Unlimited |
| Internal | Full (all) | Unlimited | Unlimited |

**Decisions:** Trial is full-featured for evaluation — *limits* constrain usage, not features.
`browser_push` is Starter+; `mobile_push` + `insights` are Professional+ (mobile-notification
reliability is a higher-tier/serious-pilot capability). `keep.active_request_limit` (a §4.11
example) is **deferred** — out of v1 locked scope.

---

## Preserved / Added / Adapted

**Added (no legacy equivalent):** the entire feature-key + limit catalog, plan→entitlement maps,
and `FeatureAccessPolicy`. Legacy gated via halo booleans (dropped, ADR-028).

**Adapted (with rationale):**
- **Plan→feature static map, not stored feature booleans** (ADR-035) — keeps account state compact
  and avoids re-introducing the halo-boolean coupling under new names. Feature packaging is product
  strategy, not per-account durable state.
- **Two catalogs, not one** (ADR-036) — capability flags (`FeatureKeys`) and numeric limits
  (`FeatureLimitKeys`) are different questions; merging them blurs the model.
- **Limits = plan default + seat-only override** (ADR-037) — reuses the existing stored
  `MaxUserSeats` for `account.user_limit`; no new per-account limit storage (premature billing
  machinery). `Unlimited = int.MaxValue` so `count < limit` works unmodified.
- **Fail-closed + standalone** (ADR-038) — unknown/blank → denied (false / 0); compose
  permitted+entitled at the call site later, once a real handler shape exists.

---

## Tests

- **Unit:** +24. Feature surface per plan (Starter has browser-push but not mobile-push/insights;
  Professional has mobile-push + insights; Trial full-featured; every plan enables KeepCore);
  fail-closed feature handling (blank/unknown key, a *permission* key passed as a feature key,
  undefined plan); plan limit defaults; Internal = Unlimited; fail-closed limit handling
  (blank/unknown key/undefined plan → 0); `ResolveLimit` seat override affects **only**
  `account.user_limit` and not the monthly limit; fallback to plan default when no seat override;
  null-entitlements guard. `OpHalo.UnitTests` now **163 passing** (was 139).
- **Architecture:** unchanged, **14 passing** — catalog is strings only, no Keep reference entered
  Foundation (§8 stayed green); policy depends only on Core entities (Application→Core allowed).
- Build: **0 warnings / 0 errors**. Full run green (UnitTests 163, ArchitectureTests 14, IntegrationTests 1).

---

## Phase 4d exit gate

- ✅ Feature keys + usage limits modeled; runtime authorizes against keys, not plan names (ADR-009).
- ✅ Plans produce entitlements via a single static map; tier matrix kept unlocked (§4.11).
- ✅ Fail-closed feature/limit resolution with unit coverage.
- ✅ No generic flag engine / Stripe / plan-matrix UI; no per-account feature booleans.
- ✅ Foundation has no Keep references; architecture tests stay green.

This completes the Phase 4 **entitlement** surface: account allowed (4a/4b) · user permitted (4c) ·
account entitled (4d). Three independent policy halves, composing at the call site, never collapsed.

---

## Risks / follow-ups

- **Not yet wired to callers / DI-registered.** No handler calls `FeatureAccessPolicy`; the
  combined `allowed && permitted && enabled` check and composition-root wiring land with the
  handler/auth phases.
- **Tier matrix + limit values are provisional.** Locking pricing/packaging is a later
  product/billing slice; everything lives in `PlanEntitlements` so it changes in one place.
- **`keep.active_request_limit` deferred** — add with the key + a plan-default row when a real
  enforcement caller needs it.
- **Limit *enforcement* is not built here** — this slice resolves the allowance; counting
  usage against it (seat checks at invite time, monthly request caps) belongs to the relevant
  handlers/usage-tracking slice.
- **No persistence** — feature/limit config is static; only the existing `MaxUserSeats` is stored
  (already covered by the 4b persistence debt).
