# Session Log — OpHalo Foundation

**Last updated:** 2026-06-26 (Session 10 complete; Session 11 next)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** 864 unit · 14 arch · 676 integration = 1,554 total, 0 failures
**Next free ADR:** ADR-372
**Current session:** Session 11 — Quick Capture Backend Contract

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

**Current build log:** `docs/build-log/065-session-11-quick-capture-backend-contract.md`
**Last completed build log:** `docs/build-log/064-session-9-account-classification-delivery-eligibility.md`
**Pilot readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1
**Current session:** Session 11 — Quick Capture Backend Contract

**Pre-work status: complete.** Pre-build pass done 2026-06-26. Build log 065 is the implementation spec.

**Session 11 scope:** Authenticated staff can create a Keep request immediately after a customer
contact, with required source/channel. Two independently compiling slices:

- **S11a** — Source/channel + NeedsShare flag (creation + detail response). 10 production files,
  EF migration. Locked decisions: ADR-369 (KeepRequestSource enum), ADR-370 (NeedsShare flag),
  ADR-371 (S11 batch split).
- **S11b** — List summary indicators + share intent clearing. Deferred.

**S11a file-level gate (10 production files):**
1. `src/OpHalo.Keep.Core/Entities/Enums/KeepRequestSource.cs` — NEW
2. `src/OpHalo.Keep.Core/Entities/KeepRequest.cs` — Source, NeedsShare, CreateByBusiness, CreateCore, ClearNeedsShare
3. `src/OpHalo.Keep.Application/Requests/CreateBusinessRequestCommand.cs` — add Source
4. `src/OpHalo.Keep.Application/Requests/CreateBusinessRequestService.cs` — slug parser; reject public_intake
5. `src/OpHalo.Keep.Application/Requests/KeepRequestDetailResult.cs` — add Source, NeedsShare
6. `src/OpHalo.Keep.Application/Requests/KeepRequestDetailMapper.cs` — map Source slug, NeedsShare
7. `src/OpHalo.Api/Keep/CreateBusinessRequestBody.cs` — add Source
8. `src/OpHalo.Keep.Infrastructure/Persistence/Configurations/KeepRequestConfiguration.cs` — EF config
9. `src/OpHalo.Api/Program.cs` — pass body.Source to command
10. `src/OpHalo.Api/Helpers/ErrorHttpMapper.cs` — 3 source error codes

**Migration (generated, ready to apply):**
```
dotnet ef database update --startup-project src/OpHalo.Keep.Infrastructure
```
Migration file: `20260627003337_QuickCaptureSourceAndNeedsShare`

**Session 10 (docs-only, complete):**
- ADR-367 — brand architecture: branded house with product accents.
- ADR-368 — type-and-color lock: Source Serif 4 / Inter / terracotta / amber / navy.
- UX design system tracked in git.
- Frontend wiring deferred to Session 13 (`web/ophalo-web` is empty placeholder).

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
