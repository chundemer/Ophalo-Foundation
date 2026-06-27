# Session Log — OpHalo Foundation

**Last updated:** 2026-06-27 (Session 12a implementation complete; migration + commit pending)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** 864 unit · 14 arch · 676 integration = 1,554 total, 0 failures
**Next free ADR:** ADR-377
**Current session:** Session 12 — Account Settings And Onboarding

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
**Last completed build log:** `docs/build-log/065-session-11-quick-capture-backend-contract.md`
**Pilot readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1
**Current session:** Session 12 — Account Settings And Onboarding

**Session 11 status: complete.** S11a and S11b are committed.

**Session 11 scope delivered:** Authenticated staff can create a Keep request immediately after a
customer contact, with required source/channel. Staff-created requests start with tracker sharing
marked needed; list summaries expose `NeedsShare`/`Source`; explicit share-intent actions clear
`NeedsShare` and persist an internal request event.

- **S11a** — Source/channel + NeedsShare flag (creation + detail response). Complete and committed
  in `bea0eb0`. Locked decisions: ADR-369 (KeepRequestSource enum), ADR-370 (NeedsShare flag),
  ADR-371 (S11 batch split).
- **S11b** — List summary indicators + share intent clearing. Complete and committed in `e13703c`.
  Locked decision: ADR-372 (share intent clearing contract).

**S11a file-level gate (10 production files, gate exception):**
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

**S11a completion summary:**

- Commit: `bea0eb0` — `S11a: KeepRequestSource + NeedsShare — creation contract and detail response`.
- Migration generated and applied successfully.
- Build successful.
- Verification reported by Christian: `872 unit · 14 arch · 16 integration (KeepBusinessRequestApi) = green`.

**Migration note:**

Migration file:
`src/OpHalo.Foundation.Infrastructure/Migrations/20260627003337_QuickCaptureSourceAndNeedsShare.cs`

Generated migration shape:
- Adds `keep_requests.needs_share` as non-null boolean with `defaultValue: false`.
- Adds `keep_requests.source` as nullable `varchar(50)`.
- No historical backfill was added because current working assumption is no meaningful persisted
  Keep request data. If a non-empty database matters later, business-origin rows would need an
  explicit `needs_share = true` backfill.

Correct EF commands for this repo layout:
```
dotnet ef migrations add QuickCaptureSourceAndNeedsShare \
  --project src/OpHalo.Foundation.Infrastructure \
  --startup-project src/OpHalo.Keep.Infrastructure \
  --context OpHaloDbContext

dotnet ef database update \
  --project src/OpHalo.Foundation.Infrastructure \
  --startup-project src/OpHalo.Keep.Infrastructure \
  --context OpHaloDbContext
```

Why: migrations live in `OpHalo.Foundation.Infrastructure`, but Keep model configuration is included
only through `KeepDesignTimeDbContextFactory` in `OpHalo.Keep.Infrastructure`.

**S11b file-level gate (9 production + 3 test, gate boundary):**

- List summary enrichment: `KeepRequestSummary`, `GetKeepRequestListService`.
- Share intent clearing: `ClearShareIntentCommand`, `ClearShareIntentService`,
  `ShareIntentBody`, `Program.cs`, `ErrorHttpMapper.cs`.
- Event persistence: `KeepRequestEventType.ShareIntentRecorded`, `KeepRequestEvent.CreateShareIntentRecorded(...)`.

**S11b completion summary:**

- Commit: `e13703c` — `S11b: list summary indicators + share intent clearing`.
- Build successful.
- Verification reported by Christian: `893 unit · 14 arch · 24 integration = 931 focused suite green`.
- Share intent endpoint: `POST /keep/requests/{id}/share-intent`.
- Allowed methods: `copy_link`, `native_share`, `manual_mark_shared`.
- Idempotent: already-cleared requests return `204` without re-writing.
- Viewer and OffSeason are blocked with explicit 403 errors.

**S12a status: implementation complete, migration + commit pending.**

**S12a file-level gate (9 production files + 2 test files):**
1. `src/OpHalo.Keep.Core/Entities/KeepBusinessProfile.cs` — NEW
2. `src/OpHalo.Keep.Infrastructure/Persistence/Configurations/KeepBusinessProfileConfiguration.cs` — NEW
3. `src/OpHalo.Keep.Application/Setup/IKeepSetupPersistence.cs` — NEW
4. `src/OpHalo.Keep.Application/Setup/KeepSetupResults.cs` — NEW
5. `src/OpHalo.Keep.Application/Setup/KeepSetupService.cs` — NEW
6. `src/OpHalo.Keep.Infrastructure/Persistence/EfKeepSetupPersistence.cs` — NEW
7. `src/OpHalo.Keep.Core/Entities/KeepResponsePolicy.cs` — StatusCheckThresholdDays + Update()
8. `src/OpHalo.Keep.Infrastructure/Persistence/Configurations/KeepResponsePolicyConfiguration.cs` — status_check_threshold_days mapping
9. `src/OpHalo.Api/Program.cs` — 3 endpoints + 2 DI registrations

**S12a endpoints (final):**
- `GET /keep/setup` — returns combined businessName, timeZone, customerFacingPhone, customerFacingEmail, responsePolicy
- `PUT /keep/setup/profile` — updates Account + KeepBusinessProfile; returns combined result
- `PUT /keep/setup/policy` — upserts KeepResponsePolicy; returns combined result

**S12a pre-commit verification:**
- Build: 0 errors, 0 warnings
- Unit tests: 29 new (KeepResponsePolicyTests, KeepBusinessProfileTests) — all green
- Architecture tests: 14/14 green

**Migration required (run before commit):**
```
dotnet ef migrations add KeepSetupBusinessProfileAndPolicyThreshold \
  --project src/OpHalo.Foundation.Infrastructure \
  --startup-project src/OpHalo.Keep.Infrastructure \
  --context OpHaloDbContext

dotnet ef database update \
  --project src/OpHalo.Foundation.Infrastructure \
  --startup-project src/OpHalo.Keep.Infrastructure \
  --context OpHaloDbContext
```

Migration shape:
- Adds `keep_business_profiles` table (id, account_id unique, customer_facing_phone, customer_facing_email, base audit columns)
- Adds `keep_response_policies.status_check_threshold_days` as non-null integer (existing rows will need a default — recommend `DEFAULT 5` in the migration if the table is non-empty)

**Session 12 pre-work: complete. S12a is implementation-ready.**

---

## Session 12 — S12a Mini-Brief

**Pre-work complete.** Locked decisions: ADR-373, ADR-374, ADR-375, ADR-376.

### Scope

Keep business profile + response policy settings API. No S12b (product-ops events) in this session.

### What exists today

- `Account.BusinessName` + `Account.TimeZone` — entity + `UpdateProfile()` domain method; no API endpoint
- `KeepResponsePolicy` — entity + EF config + DB table; missing `StatusCheckThresholdDays`; no GET/PUT API
- `KeepPublicIntakeLink` + intake setup API — complete; no changes needed in S12a

### New and changed files

**New:**

1. `src/OpHalo.Keep.Core/Entities/KeepBusinessProfile.cs` — entity: `AccountId`, `CustomerFacingPhone` (nullable), `CustomerFacingEmail` (nullable); `Upsert()` or `UpdateContact()` domain method; factory `Create(accountId)`
2. `src/OpHalo.Keep.Infrastructure/Persistence/Configurations/KeepBusinessProfileConfiguration.cs` — table `keep_business_profiles`; unique index on `account_id`; nullable phone/email columns
3. `src/OpHalo.Keep.Application/Setup/KeepSetupService.cs` — application service: `GetProfileAsync`, `UpdateProfileAsync`, `GetPolicyAsync`, `UpsertPolicyAsync`
4. `src/OpHalo.Keep.Application/Setup/IKeepSetupPersistence.cs` — persistence abstraction
5. `src/OpHalo.Keep.Application/Setup/KeepSetupResults.cs` — result records: `KeepProfileResult`, `KeepPolicyResult`
6. `src/OpHalo.Keep.Infrastructure/Persistence/EfKeepSetupPersistence.cs` — EF implementation

**Modified:**

7. `src/OpHalo.Keep.Core/Entities/KeepResponsePolicy.cs` — add `StatusCheckThresholdDays`; add `Update(...)` domain method; adjust `Create()` factory signature
8. `src/OpHalo.Keep.Infrastructure/Persistence/Configurations/KeepResponsePolicyConfiguration.cs` — add `status_check_threshold_days` column mapping
9. `src/OpHalo.Api/Program.cs` — 4 new endpoints (see below)

**Migration required** — adds `keep_business_profiles` table and `keep_response_policies.status_check_threshold_days` column.

### API endpoints

```
GET  /keep/setup/profile   → { businessName, timeZone, customerFacingPhone?, customerFacingEmail? }
PUT  /keep/setup/profile   → body: { businessName, timeZone, customerFacingPhone?, customerFacingEmail? }
GET  /keep/setup/policy    → { firstResponseTargetMinutes, standardResponseTargetMinutes,
                               priorityResponseTargetMinutes, statusCheckThresholdDays }
                             (returns V1 defaults if no policy row exists yet)
PUT  /keep/setup/policy    → body: same four fields; upsert semantics
```

Authorization: Owner/Admin only on all four endpoints.

### Policy V1 defaults (when no row exists)

| Field | Default |
|---|---|
| `firstResponseTargetMinutes` | 15 |
| `standardResponseTargetMinutes` | 240 |
| `priorityResponseTargetMinutes` | 60 |
| `statusCheckThresholdDays` | 5 |

### Boundary notes

- Profile GET reads `Account` (name, timezone) + `KeepBusinessProfile` (phone, email). Profile PUT writes to both.
- `Account.UpdateProfile()` already exists and validates name/timezone; call it from the service.
- Policy GET returns defaults when no row exists — no error, no 404.
- No changes to `KeepPublicIntakeLink` or intake endpoints in S12a.
- Intake link status is already returned by `GET /keep/setup/intake`; profile/policy are separate endpoints.

### S12b (next session after S12a commit)

`KeepProductOpsEvent` entity, `KeepProductOpsEventType` enum, onboarding signal recording on key
action paths, and `GET /keep/setup/onboarding` checklist endpoint derived from event rows.
Locked signals: ADR-376 + pilot-readiness INT-003.

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
