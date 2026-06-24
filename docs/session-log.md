# Session Log — OpHalo Foundation

**Last updated:** 2026-06-24 (P6f-4/P6f-5 handoff ready)
**Branch:** `main` tracking `origin/main`
**Current baseline:** 1405 tests (779 unit · 14 architecture · 612 integration) — unit + arch green; integration green on focused suite.
**Next free ADR:** ADR-345
**Next batch: P6f-4 — detail next/previous navigation. Then P6f-5 — close-and-next + Closed page expiry.**

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

### Next Slice

#### P6f-5 — Close-and-next + Closed 30-day customer-page expiry

Status: ready for preflight.

Goal: add context-aware detail navigation for queue workflows without changing request state.

Read before coding:

- `docs/build-log/061-phase-8-b5-session-6-proper.md`
- `docs/build-log/039-phase-8-b5-claude-coding-sessions.md` Session 6 outline
- current detail read service/result/mapper files
- current list query/cursor files for existing context naming and view contracts

Implementation direction:

- Add read-only next/previous metadata to request detail when the caller supplies a supported list context.
- Support at least `ready_to_close`; include other existing owner/admin review contexts only if the file-level
  gate stays small and the query contract is explicit.
- Preserve row authorization and role gates. Do not let navigation reveal IDs outside the current user's
  visible scope.
- Do not add close mutation behavior, batch close, archive/unarchive, or customer-page expiry here.
- Prefer reusing existing list query/cursor/view semantics rather than inventing a separate navigation model.

Preflight must answer:

- Which detail query parameter(s) carry context: view, cursor, current request ID, filters, or a compact context token.
- Whether next/previous is computed by list service reuse or a narrow detail-navigation persistence method.
- Exact DTO shape and null behavior when no previous/next row exists.
- Exact file gate before code.

#### P6f-5 — Close-and-next + Closed 30-day customer-page expiry

Status: next after P6f-4.

Goal: make the ready-to-close workflow efficient and finish the normal Closed lifecycle gap.

Read before coding:

- `docs/build-log/061-phase-8-b5-session-6-proper.md`
- `docs/deferred-topics.md` DEF-036 and DEF-063
- `src/OpHalo.Keep.Core/Entities/KeepRequest.cs`
- `ChangeKeepRequestStatusService.cs`, `AddBusinessUpdateService.cs`, detail mapper/result, and customer-page guard/tests

Implementation direction:

- "Close and next" should be an app/API affordance over the existing close/status mutation, not a batch close endpoint.
- Preserve P6f-1 close gates: Owner/Admin only, `Status==Resolved`, `AttentionLevel==None`, expected version required,
  row access required, OffSeason write rules unchanged.
- Return enough metadata for the client to move to the next ready-to-close item after a successful close, using the
  P6f-4 navigation contract if available.
- Add/verify normal `Closed` customer-page expiry: when a request transitions to `Closed`, set `ExpiresAtUtc =
  TerminatedAtUtc + 30 days`, matching the already-implemented Cancelled retention behavior.
- If the Closed expiry change is small (domain status transition + focused unit/integration/customer-page assertions),
  include it in P6f-5. If close-and-next plus expiry exceeds the slice gate, split expiry into a tiny P6f-6 before
  declaring Session 6 complete.
- Do not add archive/unarchive, closeout-reviewed state, broad analytics/reporting, notification delivery, or batch close.

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
