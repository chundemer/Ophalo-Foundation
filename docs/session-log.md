# Session Log — OpHalo Foundation

**Last updated:** 2026-06-26 (Session 11 S11a complete; S11b next)
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
**Last completed build log:** `docs/build-log/065-session-11-quick-capture-backend-contract.md` (S11a)
**Pilot readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1
**Current session:** Session 11 — Quick Capture Backend Contract

**Pre-work status: complete.** Pre-build pass done 2026-06-26. Build log 065 is the implementation spec.

**Session 11 scope:** Authenticated staff can create a Keep request immediately after a customer
contact, with required source/channel. Two independently compiling slices:

- **S11a** — Source/channel + NeedsShare flag (creation + detail response). Complete and committed
  in `bea0eb0`. Locked decisions: ADR-369 (KeepRequestSource enum), ADR-370 (NeedsShare flag),
  ADR-371 (S11 batch split).
- **S11b** — List summary indicators + share intent clearing. Next.

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

**S11a completion summary:**

- Commit: `bea0eb0` — `S11a: KeepRequestSource + NeedsShare — creation contract and detail response`.
- Migration generated and applied successfully.
- Build successful.
- Verification reported by Christian: `872 unit · 14 arch · 16 integration (KeepBusinessRequestApi) = green`.
- No commit is needed for S11a code/migration; only this session-log cleanup remains after the commit.

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

**Next work — S11b:** Pre-build complete (2026-06-26). Mini-brief locked in build log 065 S11b section.

- Surface `NeedsShare`/`Source` on `KeepRequestSummary` + `GetKeepRequestListService`.
- Add `POST /keep/requests/{id}/share-intent` → `ClearShareIntentService` (body: `{ method }`, OffSeason/Viewer → 403, idempotent, emits `ShareIntentRecorded`).
- 7 production files, 3 test files, 2 mutation families. Within gate.

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
