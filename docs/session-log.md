# Session Log — OpHalo Foundation

**Last updated:** 2026-06-24 (P6f-2 complete — P6f-3 next)
**Branch:** `main` tracking `origin/main`
**Current baseline:** 1399 tests (773 unit · 14 architecture · 612 integration) — full suite green.
**Next free ADR:** ADR-345
**Next batch: P6f-3 — closed-history date shortcuts.**

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

### Next Two Slices

Work these one at a time. Do not start P6f-3 until P6f-2 is implemented, reviewed, tested,
documented, and Christian approves the commit.

#### P6f-2 — Ready-to-close queue

Status: ready to code after the file-level gate.

Read before coding:

- `docs/build-log/061-phase-8-b5-session-6-proper.md` (P6f-2 scope).
- `docs/deferred-topics.md` DEF-036 and DEF-063 (customer-activity warning contract).
- Current `GetKeepRequestListService.cs` and `KeepRequestListPersistence.cs` for `NeedsStatusCheck` precedent.

Implementation target:

- Add `view=ready_to_close`, `ActiveViewKind.ReadyToClose`, and a `ReadyToClose` view count.
- Add `ReadyToCloseInfo` on `KeepRequestSummary`.
- Eligibility: `Status == Resolved && AttentionLevel == None`, matching `CanClose`.
- Row warning: `ReadyToCloseInfo.HasCustomerActivityAfterResolution =
  LastCustomerActivityAt > LastBusinessActivityAt` on Resolved rows.
- Keep notification/device, archive/unarchive, batch close, close-and-next, and detail navigation out of scope.

Likely files:

- `src/OpHalo.Keep.Application/Requests/IKeepRequestListPersistence.cs`
- `src/OpHalo.Keep.Application/Requests/GetKeepRequestListResult.cs`
- `src/OpHalo.Keep.Application/Requests/KeepRequestSummary.cs`
- `src/OpHalo.Keep.Application/Requests/GetKeepRequestListService.cs`
- `src/OpHalo.Keep.Infrastructure/Persistence/KeepRequestListPersistence.cs`
- query/API binding only if the current generic `view` parser is not enough
- focused unit + integration tests

#### P6f-3 — Closed-history date shortcuts

Status: next after P6f-2.

Read before coding:

- `docs/build-log/061-phase-8-b5-session-6-proper.md`
- `docs/build-log/039-phase-8-b5-claude-coding-sessions.md` Session 6 outline
- Current list query binding, validation, cursor fingerprinting, and closed-history filter tests

Implementation target:

- Add API-level shortcuts for closed-history date windows such as closed yesterday / this week.
- Reuse existing `ClosedFrom` / `ClosedTo` and `TerminatedAtUtc` history filtering.
- Keep shortcuts limited to history views where closed date filters are already valid.
- Do not add reporting totals, archive behavior, or closeout navigation in this slice.

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
