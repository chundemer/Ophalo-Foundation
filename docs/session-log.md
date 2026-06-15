# Session Log — OpHalo Foundation

**Last updated:** 2026-06-14
**Next session tier:** Tier 1 — Discovery · **Persistence phase (Phase 6) is the natural next — it is the consumer this slice was built for**
**Branch:** `main` (no remote yet) · Account-creation orchestration **committed** (`e09d876`)

> The first **composing caller** is built: `AccountProvisioningService` assembles the canonical
> new-account graph (`User` + `Account` + owner `AccountUser` + `AccountEntitlements`) from the
> Phase 4 factories. Pure domain composition — **no persistence, no DI, no handler abstractions**
> (ADR-039/040). Build clean (0 warnings). **UnitTests 176, ArchitectureTests 14, IntegrationTests 1 — all green.**

---

## Where we are

Phases 0–4d built; the Phase 4 authorization triad is complete (account allowed · user permitted
· account entitled). **This session** added the orchestration slice that finally *composes* the
Phase 4 factories. Work is **uncommitted** pending approval.

| Phase | Status | Notes |
|-------|--------|-------|
| 1 — Skeleton + architecture tests | ✅ done | `00227b0` |
| 2 — Legacy exclusion (doc) | ✅ done | `2fce382` |
| 3 — SharedKernel + abstraction cleanup | ✅ done | `2fce382` |
| 4a — Account/User/AccountUser + lifecycle + access policy | ✅ done | `ec4c35c`, build-log/003 |
| 4b — AccountEntitlements (commercial posture producer) | ✅ done | `7cf49aa`, build-log/004 |
| 4c — Permission keys + role access policy (User permitted) | ✅ done | `034eee4`, build-log/005 |
| 4d — Feature keys / entitlements (Account entitled) | ✅ done | `eef4b07`, build-log/006 |
| Account-creation orchestration (first composing caller) | ✅ done | `e09d876`, build-log/007, ADR-039/040 |

## What this session shipped

- **Application** (new `Accounts/Provisioning/`): `AccountProvisioningService.CreateVerified(...)`
  → `Result<AccountProvisioningResult>`, composing the four factories in order, enforcing the
  internal-account triad + trial window fail-closed, sourcing seats from `PlanEntitlements`.
  `AccountProvisioningResult` record carries the assembled graph.
- **Core:** added 4 provisioning errors to `AccountErrors` (`InternalAccountPlanMismatch`,
  `InternalAccountCannotBePilot`, `InternalAccountAllowsNoTrialWindow`, `TrialWindowRequired`).
- **Tests:** `AccountProvisioningServiceTests` — happy paths (trial/pilot/internal), owner
  assignment, plan-derived seats, 5 fail-closed rules (+13 → UnitTests 176).
- **Decisions:** ADR-039 (pure domain provisioning service, no persistence/DI/handler; `DateTime`
  not `DateTimeOffset`), ADR-040 (fail-closed cross-aggregate rules, plan-derived seats with no
  seat param, in-memory owner assignment, errors in Core `AccountErrors`).

## Composition order (locked)

`User.CreateVerified` → `Account.CreateVerified` → `AccountUser.CreateOwner` →
`account.AssignPrimaryOwner(owner)` (propagated on failure) →
`AccountEntitlements.CreateInternal/CreatePilot/CreateTrial` → return graph. Owner assignment is
**in-memory** (IDs exist at construction via `Guid.CreateVersion7`; ADR-019/024) — no DB round-trip.

## The authorization triad (for reference)

- **Account allowed** (`AccountAccessPolicy`, 4a/4b) — commercial/lifecycle/operating state.
- **User permitted** (`UserAccessPolicy`, 4c) — role × membership status × purpose → permission key.
- **Account entitled** (`FeatureAccessPolicy`, 4d) — plan → feature key / limit.
- Compose at the call site later: `allowed && permitted && enabled`. **Still no combined facade**
  — account *creation* precedes membership, so this orchestration is *not* the caller that surfaces it.

## Next — candidate slices (pick one at discovery, confirm with Christian)

- **Persistence phase (Phase 6)** *(natural next — the consumer this slice was built for)* —
  `OpHaloDbContext` + EF config/migrations for the 4a/4b entities. Decides the save contract,
  transaction boundary, the unique normalized-email index, duplicate-email handling, computed
  `AccountUser.IsActive` (ADR-023), one-to-one `Account↔Entitlements`. Christian runs `dotnet ef`.
- **Auth / magic links / sessions (Phase 5)** — the `/auth/exchange` flow that would *call*
  `AccountProvisioningService` for a brand-new account, plus session/device fields. Pairs naturally
  with persistence (both needed for a working sign-in/create path).
- **Invitations flow** — handler/orchestration around `CreatePendingInvite`/`Activate`; exercises
  the `account.user_limit` seat check via `FeatureAccessPolicy.ResolveLimit`.
- **Wire the policies + provisioning into the request pipeline** — DI registration + endpoint/
  behavior filter, once persistence + a host endpoint exist.

## Scope intentionally deferred (decided with Christian)

- **Persistence for the provisioning graph** → Phase 6 owns the save contract/transaction/unique
  constraints after the domain graph is settled (ADR-039). This slice assembles, never saves.
- **DI registration / endpoint** → arrives with the auth (Phase 5) / persistence (Phase 6) phases.
- **Combined permitted+entitled facade** → built only when a real authorize-an-action caller exists
  (ADR-038); account creation is not that caller.
- **Trial-duration policy/config** → caller supplies `trialEndsAtUtc`; centralizing is later config.
- **Seat overrides** → a separate entitlement mutation *after* provisioning, not part of creation.
- **Limit *enforcement*** (seat check at invite, monthly request cap) → relevant handlers/usage slice.
- **Stripe / billing, plan-matrix UI, generic flag engine, locking the tier matrix** → billing phase (§4.11).

## Watch-outs / debt carried forward

- **`AssignPrimaryOwner` failure branch is not independently tested** — structurally unreachable via
  the public surface (service always builds a valid owner); kept as defensive propagation. The
  invariant itself is covered in `AccountLifecycleTests`/`AccountUserMembershipTests`.
- **No persistence yet** — EF config for `Account`/`AccountUser`/`User`/`AccountEntitlements`
  (one-to-one `Account↔Entitlements`, FKs, unique normalized-email index, computed `IsActive`).
- **No policy/service wired or DI-registered** — `AccountAccessPolicy`/`UserAccessPolicy`/
  `FeatureAccessPolicy`/`AccountProvisioningService` have no callers/DI yet.
- **`MaxUserSeats` is the only stored limit override** — provisioning seeds it from the plan default.
- Never glob through `_reference/**/bin` (recursive nesting). Read specific source paths.
- Legacy `decision-index`/`decisions/**`/`coding-rules` remain **pending validation** — do not load.
- No GitHub remote yet. When added, repo must be named `ophalo-foundation`.
