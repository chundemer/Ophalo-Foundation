# Session Log — OpHalo Foundation

**Last updated:** 2026-06-14
**Next session tier:** Tier 1 — Discovery (next Phase 4 slice not yet confirmed)
**Branch:** `main` (no remote yet)

> Phase 4a is **built and green**. The next session picks the next Phase 4 slice
> (permissions or entitlements) and must run discovery first — its implementation
> target is **not** pre-confirmed. Do not assume scope; read the build-plan phase and
> the relevant reference source before writing code.

---

## Where we are

Phases 0–3 complete. **Phase 4a complete this session** (commit pending Christian's
approval): the trimmed Account/User/membership domain + the account-access policy,
ported per ADR-015…026. Build clean (0 warnings). **Tests: UnitTests 71, ArchitectureTests 14,
IntegrationTests 1 — all green.**

| Phase | Status | Notes |
|-------|--------|-------|
| 1 — Skeleton + architecture tests | ✅ done | `00227b0` |
| 2 — Legacy exclusion (doc) | ✅ done | `2fce382` |
| 3 — SharedKernel + abstraction cleanup | ✅ done | `2fce382` |
| 4a — Account/User/AccountUser + lifecycle + access policy | ✅ done | build-log/003, this session |
| 4b+ — permissions, entitlements, invitations | ⬜ not started | next discovery |

## What 4a shipped

- **Core:** `BaseEntity`, `EmailNormalizer` (in Foundation.Core), 6 account enums (explicit values),
  trimmed `AccountErrors`/`AccountUserErrors`, `User`, trimmed `Account`, membership-only `AccountUser`.
- **Application:** `Accounts/Access/*` — `AccountAccessPolicy` ported verbatim.
- **Tests:** account lifecycle, AccountUser membership, access-posture matrix (+38 tests).
- **Decisions locked & built:** ADR-015…020 now `Implemented`; new ADR-021…026 (see decision-index):
  Account drops `Email`; `CreateVerified(businessName, purpose, timeZone)` required/non-null;
  `IsActive` computed; self-validating `AssignPrimaryOwner(AccountUser)`; invites accept any
  non-owner role; explicit enum values + `Pilot` dropped.

## Next — candidate Phase 4 slices (pick one at discovery, confirm with Christian)

- **Permissions:** permission keys + role→permission map (plan §4.8) — Application-layer authorization.
- **Entitlements:** feature-key entitlements + the `AccountEntitlements` entity (plan §4.11),
  which is what actually *sources* the commercial/operating-mode inputs the access policy already consumes.
- **Invitations flow:** the handler/orchestration around `CreatePendingInvite`/`Activate`.

Recommendation: do **AccountEntitlements** next — it closes the loop on the access policy
(currently fed by a context with no producer) and unlocks the commercial-posture path end-to-end.

## Reference source map (for next discovery)

- Permissions: `_reference/src/OpHalo.Application/...` (locate permission-key definitions + role map).
- Entitlements: `_reference/src/OpHalo.Core/Entities/Accounts/AccountEntitlements.cs` (+ `AccountPlan` enum).
- Invitations: `_reference/src/OpHalo.Application/...Invitations/` and `InviteErrors.cs`.

## Watch-outs / debt carried forward (from build-log/003)

- **No persistence yet** — EF config (FKs, unique normalized-email index, handling the computed
  `AccountUser.IsActive` per ADR-023) lands in the persistence phase, not here.
- **No membership reactivate** (`Suspended → Active`) — deferred to the admin/invitations flow.
- **Internal accounts** still require a `BusinessName` via `CreateVerified` — revisit if a distinct
  internal-account factory is needed.
- Never glob through `_reference/**/bin` (recursive nesting). Read specific source paths.
- Legacy `decision-index`/`decisions/**`/`coding-rules` remain **pending validation** — do not load.
- Real repo is `/Users/christian/saas/ophalo-foundation`; the launch cwd `Ophalo/` is empty.
- No GitHub remote yet. When added, repo must be named `ophalo-foundation`.
