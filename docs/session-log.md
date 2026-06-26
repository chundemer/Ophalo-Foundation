# Session Log — OpHalo Foundation

**Last updated:** 2026-06-26 (Session 9 complete; Session 10 next)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** 864 unit · 14 arch · 676 integration = 1,554 total, 0 failures
**Next free ADR:** ADR-369
**Current session:** Session 10 — Brand Guide And UI Foundation

---

## Session Protocol

This file is the current execution brief, not the historical build archive. Completed implementation
detail lives in `docs/build-log/`; authoritative decisions live in
`docs/decisions/decision-index.md`.

For every implementation slice:

- Classify the work explicitly: discovery when pre-work is incomplete; mechanical implementation
  preflight when the current brief is marked pre-work complete.
- Use targeted `rg` during preflight to confirm named signatures and compile-impact callers. Do not
  rediscover already-locked architecture, scope, tests, or decisions.
- Inspect current signatures, endpoint/persistence patterns, failure modes, and tests before editing.
- Present the file-level gate before writing.
- Keep the hard slice gate unless explicitly split: at most 3 mutation families, 8 production files,
  and 12 total changed files including tests/docs.
- Preserve fail-closed account, row, action, membership, and public-token behavior.
- Add focused authorization/regression tests and run the proportionate broader suite.
- Self-review for policy drift, accidental visibility expansion, token leakage, untested direct-ID
  paths, stale docs, and unrelated scope.
- Commit only after Christian approves the completed diff.

---

## Current Work

**Last completed build log:** `docs/build-log/064-session-9-account-classification-delivery-eligibility.md`
**Pilot readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1
**Current session:** Session 10 — Brand Guide And UI Foundation (design foundation slice).

**Session 10 progress (locked, docs-only — no app code):**

- ADR-367 — brand architecture: branded house with product accents; differentiation by brand
  (OpHalo/Keep/future) via accent + personality, not by surface. Keep Web and Keep Mobile are one
  identity at two densities.
- ADR-368 — type-and-color lock: Source Serif 4 headlines over Inter body (Poppins = wordmark only);
  terracotta `#BF6B43` added as `--ophalo-accent`; attention nudged to amber `#C8741A`; gold removed
  (`--primary` → navy). Contract written into `ux-design-model-v1.md` (new Typography section).
- UX design system + decisions are now tracked in git (were untracked).

**Deferred to the real frontend build (no foundation frontend exists yet — `web/ophalo-web` is an
empty placeholder; built Session 13):** wire Source Serif, `--primary`→navy, amber attention,
`--ophalo-accent` token, serif headings, stronger marketing section contrast, container-width
rework, `brand-kit/BRAND.md` §5 correction. Tracked in `ux-design-decisions.md` Open Gaps.

**Next decision:** proceed to Session 11 (quick-capture backend contract) per roadmap, or pull
frontend scaffolding forward and apply the ADR-368 spec into a new `web/ophalo-web`.

Session 9 is complete. It replaced `AccountEntitlements.IsPilot` with `AccountClassification` on
`AccountEntitlements`, updated `SignupDefaultsSettings`, migrated existing data, and added the
Demo/InternalTest delivery eligibility gate required before real APNs/FCM delivery.

Locked Session 9 decisions:

- ADR-363 — account classification replaces `AccountEntitlements.IsPilot`.
- ADR-364 — signup defaults set classification, not a pilot boolean.
- ADR-365 — pilot cap counts classification `Pilot`.
- ADR-366 — Demo/InternalTest suppress production push delivery.

---

## Session 9 Closeout

### S9a — Classification model, provisioning, and migration

**Status: complete.**

Code complete and verified:

- `AccountClassification` enum added (`Production=1`, `Pilot=2`, `Demo=3`, `InternalTest=4`).
- `AccountEntitlements.IsPilot` removed; `Classification` property added (string-persisted).
- Factories unified: `Create(accountId, plan, seats, trialEnd, classification)` + `CreateInternal` (forces `InternalTest`).
- `AccountErrors.InternalAccountCannotBePilot` → `ClassificationNotAllowedForPublicSignup`.
- `SignupDefaultsSettings.IsPilot: bool` → `Classification: AccountClassification` (default `Pilot`).
- `StartAuthService` + `ExchangeAuthService` pilot-cap checks use `Classification == Pilot`.
- `IAuthCodePersistence.CountActivePilotAccountsAsync` → `CountPilotClassifiedAccountsAsync`.
- `EfAuthCodePersistence` count queries on `Classification == Pilot`.
- `AccountEntitlementsConfiguration` persists `Classification` as `varchar(50)`; `IsPilot` removed.
- `AccountProvisioningService` signature: `isPilot: bool` → `classification: AccountClassification`.
- `appsettings.json`: `IsPilot: true` → `Classification: "Pilot"`.
- EF migration `20260626111822_AccountClassification` inspected: adds `classification`, maps
  `is_pilot`/internal rows, makes classification required, and drops `is_pilot`.

### S9b — Delivery eligibility gate and docs ledger

**Status: complete.**

- `FindActiveDevicesForDeliveryAsync` is gated by account classification.
- `Production` and `Pilot` return active delivery devices; `Demo` and `InternalTest` return none.
- Focused integration coverage proves all four classification cases.
- Docs and deferred-topic ledger reconciled.

Verification:

```text
dotnet test tests/OpHalo.IntegrationTests/OpHalo.IntegrationTests.csproj --filter FullyQualifiedName~AccountUserDeviceApiTests
Passed: 30, Failed: 0

dotnet build
Succeeded with 2 NU1900 package-vulnerability feed warnings from nuget.org lookup; no compile errors.
```

---

## Next Session

### Session 10 — Brand Guide And UI Foundation

Goal: create the practical V1 product/brand guide before serious PWA/native/customer-page buildout.

Read:

- `docs/pilot-readiness-decision-questions.md`;
- roadmap section 9.1 in
  `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md`;
- relevant product positioning notes in `docs/keep-product-positioning.md`.

Likely outputs:

- brand voice and customer-facing language rules;
- typography, color, spacing, icon, status, and attention visual conventions;
- form, empty/loading/error, customer-page trust, PWA, and native component guidance;
- explicit deferred UX/design questions before Quick Capture and app buildout.

No code implementation is assumed until Session 10 scope is locked with Christian.

---

## Carry-Forward Boundaries

- Real APNs/FCM provider implementation remains future work.
- Demo scenario packs, demo reset UI, and admin/internal classification management remain deferred.
- Classification is operational/reporting/safety posture, separate from commercial lifecycle.
- Public signup cannot create Demo/InternalTest accounts.
- Production push delivery must stay suppressed for Demo/InternalTest accounts.
- Keep sends no backend SMS/email to customers in V1; native `sms:`, `tel:`, and `mailto:` handoff
  remains operator-initiated on the user's device.

---

## Operational Watch-Outs

- GitHub remote `origin` is configured; push local commits daily when green.
- Integration tests reset PostgreSQL schema and run migrations.
- Testing environment intentionally skips runtime rate limiting; production-like proof exists in
  `RateLimitTesting` (G8a/S7b).
- Deployment still requires correct Cloudflare/Railway trusted-proxy and token-redaction
  configuration even though application-level proofs are complete.
- Persistent local PostgreSQL setup/migration/smoke runbook is verified against local `ophalo_local`
  in Docker; guarded reset remains documented but was not exercised.
