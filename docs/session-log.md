# Session Log — OpHalo Foundation

**Last updated:** 2026-06-14
**Next session tier:** Tier 1 — Discovery · **Phase 4 entitlement surface COMPLETE; next = orchestration or persistence (pick at discovery)**
**Branch:** `main` (no remote yet) · Phase 4d committed (`eef4b07`)

> Phase 4d (**feature keys + entitlements**, §4.11 / ADR-009 "Account is entitled") is **committed**
> (`eef4b07`). This closes the Phase 4 authorization triad: account **allowed** (4a/4b) · user
> **permitted** (4c) · account **entitled** (4d) — three independent policy halves that compose at
> the call site, never collapse. Working tree clean.

---

## Where we are

Phases 0–4d built. **Phase 4d this session:** `FeatureKeys` + `FeatureLimitKeys` catalogs,
static `PlanEntitlements` map, and fail-closed `FeatureAccessPolicy`. Build clean (0 warnings).
**Tests: UnitTests 163, ArchitectureTests 14, IntegrationTests 1 — all green.**

| Phase | Status | Notes |
|-------|--------|-------|
| 1 — Skeleton + architecture tests | ✅ done | `00227b0` |
| 2 — Legacy exclusion (doc) | ✅ done | `2fce382` |
| 3 — SharedKernel + abstraction cleanup | ✅ done | `2fce382` |
| 4a — Account/User/AccountUser + lifecycle + access policy | ✅ done | `ec4c35c`, build-log/003 |
| 4b — AccountEntitlements (commercial posture producer) | ✅ done | `7cf49aa`, build-log/004 |
| 4c — Permission keys + role access policy (User permitted) | ✅ done | `034eee4`, build-log/005 |
| 4d — Feature keys / entitlements (Account entitled) | ✅ done | `eef4b07`, build-log/006 |

## What 4d shipped

- **Application** (new `Accounts/Entitlements/`): `FeatureKeys` (boolean capability catalog,
  `domain.capability`, all `keep.*`), `FeatureLimitKeys` (numeric allowances, `account.user_limit`
  + `keep.monthly_request_limit`, `Unlimited = int.MaxValue`), `PlanEntitlements` (static
  `AccountPlan → features + limit defaults`, explicit set composition), `IFeatureAccessPolicy` /
  `FeatureAccessPolicy` (`IsEnabled` / `GetLimit` / `ResolveLimit`, fail-closed).
- **Tests:** `FeatureAccessPolicyTests` — plan/feature/limit/seat-override matrix (+24).
- **Decisions:** ADR-035 (plan→feature static map, no per-account feature booleans), ADR-036
  (separate `FeatureKeys` vs `FeatureLimitKeys` catalogs; `keep.active_request_limit` deferred),
  ADR-037 (limits = plan default + `MaxUserSeats` seat-only override; `Unlimited` sentinel),
  ADR-038 (fail-closed + standalone; provisional matrix not locked).
- **No Core change** — `AccountEntitlements` stays compact; capabilities derive from plan.

## The locked authorization triad (for reference)

- **Account allowed** (`AccountAccessPolicy`, 4a/4b) — commercial/lifecycle/operating state.
- **User permitted** (`UserAccessPolicy`, 4c) — role × membership status × purpose → permission key.
- **Account entitled** (`FeatureAccessPolicy`, 4d) — plan → feature key / limit.
- Compose at the call site later: `allowed && permitted && enabled`. **No combined facade yet**
  (no caller exists). Three catalogs — `PermissionKeys` / `FeatureKeys` / `FeatureLimitKeys` —
  stay distinct; permission ≠ feature ≠ limit.

## Provisional plan matrix (NOT locked — §4.11; lives only in `PlanEntitlements`)

- Features: KeepCore (12 keys) every plan; Starter +`browser_push`; Professional/Business/
  Enterprise/Internal/Trial = Full (+`mobile_push`, `insights`). Trial full-featured for eval —
  limits constrain, not features.
- Limits (`user_limit` / `monthly_request_limit`): Trial 3/50, Starter 3/250, Professional 10/1500,
  Business 25/6000, Enterprise & Internal Unlimited/Unlimited.

## Next — candidate slices (pick one at discovery, confirm with Christian)

- **Account-creation orchestration** *(natural next)* — the handler/application service calling
  `Account.CreateVerified` + `AccountEntitlements.CreateTrial/Pilot/Internal` + `AssignPrimaryOwner`
  together. First real caller — would also surface the shape of the composed
  `allowed && permitted && enabled` check and the DI registrations all three policies await.
- **Persistence phase** — EF config/migrations for the 4a + 4b entities (Infrastructure; Christian
  runs `dotnet ef` / migrations himself). Policies/catalogs are static — nothing to migrate there.
- **Invitations flow** — handler/orchestration around `CreatePendingInvite`/`Activate`
  (would exercise the `account.user_limit` seat check via `FeatureAccessPolicy.ResolveLimit`).
- **Wire the policies into the request pipeline** — behavior/endpoint filter + DI registration,
  once a caller exists.

## Scope intentionally deferred (decided with Christian)

- **Combined permitted+entitled facade** → built only when a real handler call site exists (ADR-038).
- **Limit *enforcement*** (seat check at invite, monthly request cap) → the relevant handlers /
  usage-tracking slice. 4d resolves the allowance; it does not count usage against it.
- **`keep.active_request_limit`** → add with key + plan-default row when an enforcement caller needs it.
- **Stripe / billing / payment block, plan-matrix UI, generic flag engine** → billing-integration phase (§4.11).
- **Locking the tier matrix / pricing** → later product/billing slice (all in `PlanEntitlements`).

## Watch-outs / debt carried forward

- **No persistence yet** — EF config for `Account`/`AccountUser`/`User`/`AccountEntitlements`
  (one-to-one `Account↔Entitlements`, FKs, unique normalized-email index, computed
  `AccountUser.IsActive` per ADR-023) lands in the persistence phase.
- **Nothing provisions `AccountEntitlements` yet** — `CreateTrial/Pilot/Internal` exist but no
  handler calls them (account-creation orchestration slice).
- **No policy wired/registered** — `AccountAccessPolicy` / `UserAccessPolicy` / `FeatureAccessPolicy`
  have no callers and no DI registration yet; arrives with the handler/auth phases.
- **`MaxUserSeats` is the only stored limit override** — `ResolveLimit` honors it for
  `account.user_limit` only (`> 0` wins); all other limits are plan-derived.
- Never glob through `_reference/**/bin` (recursive nesting). Read specific source paths.
- Legacy `decision-index`/`decisions/**`/`coding-rules` remain **pending validation** — do not load.
- No GitHub remote yet. When added, repo must be named `ophalo-foundation`.
