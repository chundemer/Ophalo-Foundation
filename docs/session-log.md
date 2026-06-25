# Session Log — OpHalo Foundation

**Last updated:** 2026-06-24 (Session 8 pre-work locked; S8a next)
**Branch:** `main` tracking `origin/main`
**Current baseline:** 815 unit · 14 arch green; S7e: 12 classify integration tests green.
**Next free ADR:** ADR-363
**Next batch: Session 8 — Notification/device foundation (S8a).**

---

## Session Protocol

This file is the current execution brief, not the historical build archive. Completed implementation
detail lives in `docs/build-log/`; authoritative decisions live in
`docs/decisions/decision-index.md`.

For every implementation slice:

- classify the work explicitly: discovery when pre-work is incomplete; mechanical implementation
  preflight when the current brief is marked pre-work complete;
- during preflight, use targeted `rg` only to confirm named signatures and enumerate compile-impact
  callers; do not rediscover already-locked architecture, scope, tests, or decisions;
- inspect current signatures and failure modes before calling existing code;
- always present the file-level gate before writing; when pre-work is complete it is a mechanical
  caller/signature gate, while incomplete pre-work also requires design decisions and approval;
- enforce the hard slice gate: at most 3 independent mutation handler families, 8 production files,
  and 12 total changed files including tests/fakes/docs; exact aliases sharing one handler/service
  count as one family only when all aliases are enumerated and parameterized-contract tested;
- preserve fail-closed account, row, action, and public-token behavior;
- preserve ADR-336: business-first request capture is a pilot must-have UI path. Public intake and
  customer pages enrich/collaborate after capture; they are not the gate for work to enter Keep.
- add focused authorization/regression tests and run the proportionate broader suite;
- self-review for policy drift, accidental visibility expansion, untested direct-ID paths, stale
  documentation, and unrelated scope;
- commit only after Christian approves the completed diff.

## Current Work

**Current build log:** `docs/build-log/063-session-8-notification-device-foundation.md`
**Completed Session 7 build log:** `docs/build-log/062-session-7-pilot-safety-decision-build.md`
**Completed Session 6 build log:** `docs/build-log/061-phase-8-b5-session-6-proper.md`
**Completed Session 6 prerequisite build log:** `docs/build-log/060-phase-8-b5-session-6-prerequisites-decisions.md`
**Next implementation:** S8a — Device table + `/me/devices/{appInstallationId}` register/revoke API.

Session 8 pre-work is locked in build-log/063. It builds the narrow V1 notification/device
foundation: device table, token registration/revocation, personal badge count, push abstraction with
no-op delivery, minimal non-sensitive payloads, post-commit fail-soft hooks, and P6e routing/actor
suppression. Real APNs/FCM adapters, notification history/outbox/retry/analytics, full preferences,
quiet hours, customer SMS/email, Demo/InternalTest classification, and Planned For-aware push
escalation remain out of the first Session 8 foundation.

Session 6 is complete. P6b-P6f shipped the Follow Up On / Planned For prerequisites,
customer intent menu, customer-page viewed signal, needs-status-check queue, notification/badge
boundaries, close permission, ready-to-close queue, closed-history shortcuts, detail navigation,
close-and-next, and Closed customer-page expiry.

### Session 6 Completion Summary

#### P6f-3 — Closed-history date shortcuts — COMPLETE

`closedShortcut=yesterday|this_week` added as a UTC-backed query parameter for history views.
Unknown shortcut → `RequestListInvalidClosedShortcut` (400). Shortcut on non-history view or combined
with explicit `closedFrom`/`closedTo` → `ContradictoryParameters`. Cursor fingerprint includes the
shortcut name. Account timezone deferred (DEF-078).
5 new unit tests, 3 new integration tests. 779 unit · 14 arch — full suite green.

#### P6f-4 — Detail next/previous navigation — COMPLETE

`navView=ready_to_close` query param added to `GET /keep/requests/{requestId}`. Owner/Admin-only;
unknown value → `RequestDetailInvalidNavView` (400); Operator → 403. Navigation computed by
`GetReadyToCloseNavigationIdsAsync` (new method on `IKeepRequestDetailPersistence`): fetches
`Resolved + AttentionLevel.None` rows for the account, sorted by coalesced last-activity DESC, Id ASC
(matches B5 group-7 ranking). Returns `KeepRequestNavigation(PreviousId, NextId, Position, Total)` on
`KeepRequestDetailResult`; null when no `navView`. Position=0 when the current request is no longer
in the queue. `KeepRequestDetailMapper.ToDetailResult` accepts optional `navigation` param (existing
callers unchanged).
6 new unit tests, 6 new integration tests (`KeepRequestDetailB5Tests`). 785 unit · 14 arch green.

#### P6f-5 — Close-and-next + Closed 30-day customer-page expiry — COMPLETE

**Expiry fix:** `KeepRequest.ChangeStatus` and `AddBusinessUpdateWithStatus` now set `ExpiresAtUtc = TerminatedAtUtc + 30d`
for `Closed` (previously only `Cancelled` did). `CancelledPageRetentionDays` renamed to `TerminalPageRetentionDays`.
`KeepPublicCustomerAccessGuard` unchanged — guard already checks `IsTerminal && ExpiresAtUtc.HasValue`.

**Close-and-next:** `string? navView` added as query param on `PATCH /keep/requests/{requestId}/status`.
`ChangeKeepRequestStatusCommand` gains `NavView: string?`. In `ChangeKeepRequestStatusService`, after the close guard:
unknown navView → `RequestDetailInvalidNavView` (400); Operator navView → 403; valid navView fetches
`GetReadyToCloseNavigationIdsAsync` **before** the domain mutation to snapshot queue order. After close,
response includes `Navigation(PreviousId: null, NextId: nextFromSnapshot, Position: 0, Total: count−1)`.
`navView=null` → `Navigation=null` (unchanged behavior for existing callers).

**Files changed (6):**
1. `src/OpHalo.Keep.Core/Entities/KeepRequest.cs` — expiry in `ChangeStatus` + `AddBusinessUpdateWithStatus`; constant renamed
2. `src/OpHalo.Keep.Application/Requests/ChangeKeepRequestStatusService.cs` — `NavView` on command; navView block
3. `src/OpHalo.Api/Program.cs` — bind `navView` on PATCH endpoint
4. `tests/OpHalo.UnitTests/Keep/KeepRequestTests.cs` — 2 new expiry tests
5. `tests/OpHalo.IntegrationTests/Api/ChangeKeepRequestStatusTests.cs` — test 11 gains ExpiresAtUtc assertion; 2 new navView tests; STATUS006 seed
6. `docs/session-log.md`

**Test gate:** 787 unit · 14 arch green. 27 integration (ChangeKeepRequestStatus + B5 detail) green.

**Review fixes (commit after):** navView gated to `parsedStatus == Closed`; close-and-next integration
test enhanced with two-item fixture asserting `nextId`; assumption recorded that no real Closed rows
exist pre-pilot (backfill SQL noted in build log if needed at launch). 787 unit · 14 arch green;
22 ChangeKeepRequestStatus integration tests green.

---

## Current Work — Session 7 Decision/Build

Session 7 decisions are locked and implementation is split into bounded Claude coding sessions in
`docs/build-log/062-session-7-pilot-safety-decision-build.md`. Treat each slice as mechanical
implementation preflight unless code discovery contradicts a locked decision.

### Session 7 Goal

Make the backend safe to operate before notification delivery and pilot rollout.

### Locked Session 7 Decisions

- **ADR-345:** Persistent local PostgreSQL runbook is required before notifications. It must cover
  migrate, smoke, and safe local reset. Broad demo seed data is excluded from normal setup and
  deferred behind Demo/InternalTest account classification.
- **ADR-346:** Rate limiting needs an automated production-like proof outside the normal `Testing`
  host path, with tiny deterministic thresholds and `429` assertions.
- **ADR-347:** Trusted client IP must be explicit behind Cloudflare/Railway. Trusted Cloudflare
  country may be optional internal context only; no broad authenticated access decisions or business
  user exposure from IP/location.
- **ADR-348:** Token-bearing flows are secret-safe and supportable. Raw tokens/URLs/credentials do
  not appear in logs, errors, request logging, or test output; safe diagnostics use route templates,
  reason codes, entity IDs after lookup, expiry metadata, correlation IDs, and keyed fingerprints.
- **ADR-349:** Spam/Test classification is pre-notification V1 scope. Owner/Admin only; explicit
  confirmed action; optional internal reason; preserve audit/history; exclude from operational and
  impact metrics; no source blocking/adaptive abuse controls yet.
- **ADR-350:** `Spam` and `Test` are explicit terminal request statuses, not cancellation reasons.

### S7a + S7a-2 — Local PostgreSQL runbook and bootstrap — COMPLETE

Added `docs/deployment/local-postgres-runbook.md`. Bootstrapped `ophalo_local` in Docker PostgreSQL:

- User secrets initialized on `src/OpHalo.Api` (connection string + cursor signing key) and
  `src/OpHalo.Keep.Infrastructure` (connection string for EF design-time factory). Both projects
  require the connection string independently — the runbook was updated to make this explicit.
- All 15 migrations applied via `dotnet ef database update` against `ophalo_local`.
- API started with `ASPNETCORE_ENVIRONMENT=Development`; smoke check confirmed `422
  keep.public_intake.unavailable` for the unknown-token intake route — database-backed route
  executed correctly against an empty database.
- Runbook corrected: removed duplicate env-var signing key section; added explicit User Secrets Setup
  section documenting both projects and the `dotnet user-secrets init` prerequisite. macOS AirPlay
  Receiver occupies port 5000; API bound to 5092 locally.
- Guarded reset not exercised this slice (not needed; ADR-345 baseline is verified).

### S7d — Spam/Test terminal-status foundation — COMPLETE

Added `Spam = 8` and `Test = 9` to `KeepRequestStatus`. Updated `IsTerminal`, all active-view
terminal exclusion filters, `all_history` inclusion, customer-write guard, action policy
(`canSetTiming`, `ComputeAllowedStatuses`), and four `MapStatus` exhaustive switches
(`KeepRequestDetailMapper`, `KeepCustomerPageMapper`, `GetKeepRequestListService`,
`GetKeepRequestListService`'s private summary mapper). Status stored as string via EF
`HasConversion<string>()` — no migration required.

**Files changed (8 production; 3 test; 1 docs):**
1. `src/OpHalo.Keep.Core/Entities/Enums/KeepRequestStatus.cs` — Spam=8, Test=9
2. `src/OpHalo.Keep.Core/Entities/KeepRequest.cs` — IsTerminal; customer-write block via IsTerminal
3. `src/OpHalo.Keep.Application/Requests/KeepRequestActionPolicy.cs` — canSetTiming; terminal arm
4. `src/OpHalo.Keep.Application/Requests/KeepRequestDetailMapper.cs` — MapStatus
5. `src/OpHalo.Keep.Application/Requests/KeepCustomerPageMapper.cs` — MapStatus
6. `src/OpHalo.Keep.Application/Requests/GetKeepRequestListService.cs` — StatusSlugs + private MapStatus
7. `src/OpHalo.Keep.Infrastructure/Persistence/KeepRequestListPersistence.cs` — all active filters + all_history
8. `src/OpHalo.Keep.Infrastructure/Persistence/KeepRequestRowQueryFactory.cs` — ApplyAvailable
9. `tests/OpHalo.UnitTests/Keep/KeepRequestTests.cs` — IsTerminal×2; customer-write-blocked×2; ChangeStatus-blocked×2
10. `tests/OpHalo.UnitTests/Keep/KeepRequestActionPolicyTests.cs` — terminal write-disabled×2; internal-note allowed×2
11. `tests/OpHalo.IntegrationTests/Api/KeepRequestListQueryApiTests.cs` — Spam excluded default + in all_history; Test excluded default

**Note:** Gate preflight missed `GetKeepRequestListService`'s private `MapStatus` (separate from
`KeepRequestDetailMapper.MapStatus`); discovered and fixed during integration test failure.
`GetKeepRequestListService.cs` remains one production file with two S7d changes.

**Test gate:** 797 unit · 14 arch green. 46 list query integration tests green.

### S7e — Spam/Test classification command — COMPLETE

`POST /keep/requests/{requestId}/classify` with body `{ targetStatus, reason? }` and version header.
Owner/Admin only (ADR-349); Operator → 403 (`ClassificationRequiresOwnerOrAdmin`). Confirmation is
UI-enforced before submit; backend treats an authenticated, authorized POST as intentional. Loads
request with AccountWide scope; cross-account → 404. Expected-version check; stale → 409. Slug
parse: `spam`/`test` only; unknown/null/blank → 422 (`InvalidClassification`). Domain `Classify`
method guards IsTerminal (→ 409), validates reason ≤ 500 chars (→ 400), sets
`Status`/`TerminatedAtUtc`/`ExpiresAtUtc`/`ClearAllAttentionForTerminal`, creates Internal
`RequestClassified` event. `MapEventType` updated with `request_classified`. Returns full detail
result.

**Files changed (8 production + 3 new):**
1. `src/OpHalo.Keep.Core/Entities/Enums/KeepRequestEventType.cs` — `RequestClassified = 14`
2. `src/OpHalo.Keep.Core/Entities/KeepRequestEvent.cs` — `CreateClassified` factory
3. `src/OpHalo.Keep.Core/Entities/KeepRequest.cs` — `Classify` domain method
4. `src/OpHalo.Keep.Core/Errors/KeepRequestErrors.cs` — 3 new classification errors
5. `src/OpHalo.Keep.Application/Requests/ClassifyKeepRequestService.cs` — new command + service
6. `src/OpHalo.Keep.Application/Requests/KeepRequestDetailMapper.cs` — MapEventType
7. `src/OpHalo.Api/Program.cs` — service registration + endpoint
8. `src/OpHalo.Api/Helpers/ErrorHttpMapper.cs` — 3 new error mappings
9. `src/OpHalo.Api/Keep/ClassifyRequest.cs` — new request body record
10. `tests/OpHalo.UnitTests/Keep/KeepRequestTests.cs` — 11 new Classify domain tests
11. `tests/OpHalo.IntegrationTests/Api/ClassifyKeepRequestTests.cs` — 12 new integration tests

**Deferred:** `CanClassify` in `KeepRequestActionDecision`/`KeepRequestActionPolicy`/`AvailableActionsMetadata`
— advisory UI metadata deferred to S7f to stay within production file limit.

**Review fix:** `ClassifyRequestBody.TargetStatus` is nullable; null/blank target now returns 422
instead of risking a 500. Spam success test asserts version rotation and persisted internal
`request_classified` event with reason and `statusAfter`.

**Test gate:** 808 unit · 14 arch green. 12 classify integration tests green.

### S7f — Final pilot-safety ledger and regression gate — COMPLETE

`CanClassify` advisory metadata added to `KeepRequestActionDecision`, `AvailableActionsMetadata`,
`KeepRequestActionPolicy`, and `KeepRequestDetailMapper.ToAvailableActionsMetadata`. Policy:
Owner/Admin + non-terminal → true; Operator or terminal → false (ADR-349). ADR-297 marked
Implemented (Spam/Test customer-page disabling shipped in S7d/S7e). DEF-061 fully reflected in
ADR-296/ADR-349/ADR-350. DEF-079/DEF-080 (demo seed data) remain deferred as required by ADR-345.

**Files changed (5):**
1. `src/OpHalo.Keep.Application/Requests/KeepRequestActionDecision.cs` — `CanClassify` field
2. `src/OpHalo.Keep.Application/Requests/KeepRequestDetailResult.cs` — `CanClassify` in `AvailableActionsMetadata`
3. `src/OpHalo.Keep.Application/Requests/KeepRequestActionPolicy.cs` — `DenyAll` + `Evaluate`
4. `src/OpHalo.Keep.Application/Requests/KeepRequestDetailMapper.cs` — `ToAvailableActionsMetadata`
5. `tests/OpHalo.UnitTests/Keep/KeepRequestActionPolicyTests.cs` — 7 new `CanClassify` assertions/tests

**Test gate:** 815 unit · 14 arch green.

### Session 7 Claude Coding Sessions

- **S7a-2 — Local PostgreSQL bootstrap and verification:** COMPLETE — see above.
- **S7b — Trusted proxy/client IP plus rate-limit smoke proof:** COMPLETE — shipped as G8a
  (commit `e055458`). `ClientIpResolver`, `RateLimitTesting` host, 3 integration + 16 unit tests.
- **S7c — Token-safe logging and redaction guardrails:** COMPLETE — shipped as G8b
  (commit `09d5e79`). `PublicTokenPathRedactor`, Diagnostics AddFilter, 4 integration sentinel proofs.
- **S7d — Spam/Test terminal-status foundation:** COMPLETE — see below.
- **S7e — Spam/Test classification command:** COMPLETE — see below.
- **S7f — Final pilot-safety ledger and regression gate:** COMPLETE — see below.

### Session 8 Handoff

Session 8 pre-work is locked in `docs/build-log/063-session-8-notification-device-foundation.md`.
Claude should start with S8a and treat each slice as mechanical implementation preflight unless
current code discovery contradicts a locked ADR.

---

## Active Carry-Forward Boundaries

- One durable public intake link; no ordinary pause/resume. Exceptional replacement is
  transactional and warns about stale links.
- Public-intake abuse posture before pilot: bounded validation, trusted-IP/rate-limit proof,
  Spam/Test terminal classification before notifications, token-safe logs, and internal emergency
  path.
- Hashed-source blocking, adaptive bot challenges, anomaly detection, and phone verification remain
  post-pilot/V1.1 unless evidence requires earlier work.
- Terminal customer pages expire 30 days after Closed/Cancelled; active and Resolved do not.
- Mobile is the on-the-road Operator surface; PWA is the Owner/Admin operational surface. Backend
  authorization remains identical regardless of client.
- No platform SMS/email is sent by Keep in V1. Automated customer messaging would require
  A2P/TCPA/opt-out compliance; native `sms:`, `tel:`, and `mailto:` launchers keep pilot contact
  operator-initiated on the user's device.
- Keep remains fresh-not-realtime for V1: refetch after writes, focus/resume sync, pull-to-refresh,
  bounded polling, and later push for urgent off-screen work.

## Operational Watch-Outs

- GitHub remote `origin` is configured; push local commits daily when green.
- Integration tests reset PostgreSQL schema and run migrations.
- Testing environment intentionally skips runtime rate limiting; production-like proof exists in
  `RateLimitTesting` (G8a/S7b).
- Deployment still requires correct Cloudflare/Railway trusted-proxy and token-redaction
  configuration even though application-level proofs are complete.
- Persistent local PostgreSQL setup/migration/smoke runbook is verified against local `ophalo_local`
  in Docker; guarded reset remains documented but was not exercised.
