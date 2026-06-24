# Session Log — OpHalo Foundation

**Last updated:** 2026-06-24 (P6f-5 complete)
**Branch:** `main` tracking `origin/main`
**Current baseline:** 1409 tests (787 unit · 14 architecture · 612 integration) — unit + arch green; integration green on focused suite.
**Next free ADR:** ADR-345
**Next batch: Session 6 complete — P6f-1 through P6f-5 shipped.**

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

**Current build log:** `docs/build-log/061-phase-8-b5-session-6-proper.md`  
**Prerequisite build log:** `docs/build-log/060-phase-8-b5-session-6-prerequisites-decisions.md`  
**Decisions:** ADR-337..ADR-344; next free ADR-345.

Session 6 prerequisites are complete. Session 6 proper is now active.

Current handoff:

- **Completed:** P6b-P6e prerequisites. Follow Up On, Planned For, customer intent menu,
  customer-page viewed signal, needs-status-check queue, and notification/badge boundaries are complete.
  See `docs/build-log/060-phase-8-b5-session-6-prerequisites-decisions.md`.
- **Completed:** P6f-1 — close permission + CanClose affordance + review fixes. `CanClose` field on
  `KeepRequestActionDecision` and `AvailableActionsMetadata`; computed as `isOwnerAdmin && Status==Resolved &&
  AttentionLevel==None` (ADR-343); `AllowedStatuses` consistent with `canClose`; role + attention guards added
  to `ChangeKeepRequestStatusService` and `AddBusinessUpdateService` (Operator → 403, attention → 409).
  See `docs/build-log/061-phase-8-b5-session-6-proper.md`.
- **Completed:** P6f-2 — ready-to-close queue. `ActiveViewKind.ReadyToClose`; `ReadyToClose` count in
  `KeepRequestViewCounts`; `KeepRequestReadyToCloseInfo(HasCustomerActivityAfterResolution)` on every row;
  `view=ready_to_close` Owner/Admin-only active view with DB pre-filter (non-terminal + no-attention) and
  in-memory eligibility (Resolved + no-attention); DEF-036/DEF-063 finalized; `_resolvedRequestId` tracked
  in B5 fixture; brittle `SingleOrDefault(status==resolved)` test fixed.
  773 unit · 14 arch · 612 integration — full suite green.

### Completed This Session

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

---

## Active Carry-Forward Boundaries

- One durable public intake link; no ordinary pause/resume. Exceptional replacement is
  transactional and warns about stale links.
- Public-intake abuse posture before pilot: bounded validation, trusted-IP/rate-limit proof,
  Spam/Test classification in its planned V1 slice, token-safe logs, and internal emergency path.
- Hashed-source blocking, adaptive bot challenges, anomaly detection, and phone verification remain
  post-pilot/V1.1 unless evidence requires earlier work.
- Terminal customer pages expire 30 days after Closed/Cancelled; active and Resolved do not.
- Mobile is the on-the-road Operator surface; PWA is the Owner/Admin operational surface. Backend
  authorization remains identical regardless of client.
- No platform SMS is sent until consent/compliance posture is reviewed.
- Keep remains fresh-not-realtime for V1: refetch after writes, focus/resume sync, pull-to-refresh,
  bounded polling, and later push for urgent off-screen work.

## Operational Watch-Outs

- GitHub remote `origin` is configured; push local commits daily when green.
- Integration tests reset PostgreSQL schema and run migrations.
- Testing environment intentionally skips runtime rate limiting; G8 must use a production-like host.
- Deployment requires correct Cloudflare/Railway trusted-proxy and token-redaction configuration.
- Persistent local PostgreSQL setup/migration/smoke/reset runbook remains GAP-016 after Session 6 and
  before notifications.
