# Session Log — OpHalo Foundation

**Last updated:** 2026-06-23 (P6d-1 complete — needs-status-check signal foundation)
**Branch:** `main` tracking `origin/main`
**Current baseline:** 1334 tests (739 unit · 14 architecture · 598 integration — unit count updated; integration not re-run) — full unit suite green.
**Next free ADR:** ADR-345
**Next batch: P6d-2 — needs-status-check list/query surface.**

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

**Build log:** `docs/build-log/060-phase-8-b5-session-6-prerequisites-decisions.md`  
**Decisions:** ADR-337..ADR-344.

Session 6 proper remains paused. P6b and P6c are complete; the active prerequisite is P6d.

Locked implementation split:

- **P6b — Follow Up On + Planned For foundation:** active-request date-only follow-up and planned
  timing context, scan metadata, versioned mutations, and stale-suppression inputs.
- **P6c — Customer intent menu + page viewed signal:** simplified customer-language intent actions
  and debounced customer-page usage/confidence metadata.
- **P6d — Needs-status-check policy/signal model:** account-policy threshold with 5-day default and
  centralized latest-meaningful-activity calculation.
- **P6e — Notification candidate/badge contract notes:** immediate-attention push candidates,
  badge/list-only categories, and delivery boundary.

Current handoff:

- **Completed:** P6b — Follow Up On + Planned For domain/API/detail/list. Final baseline:
  1320 tests (715 unit · 14 architecture · 591 integration), full suite green.
- **Completed:** P6c-1 — ADR-342 customer intent menu. Commits `304cb92` (routes/mapper/enum/domain)
  and `5a5eaa8` (fix: detail/list mapper gaps and regression test).
- **Completed:** P6c-2 — ADR-341 customer-page viewed signal. Commit `beebba2`
  (KeepRequest domain field + debounce method, GetKeepCustomerPageService page-view recording,
  IKeepCustomerWritePersistence.CommitPageViewAsync, staff detail metadata, migration, 7 unit + 6 integration tests).
  Also fixed stale KeepOffSeasonTests customer message route from P6c-1 intent split.
- **Completed:** P6c-3 — docs/ledger completion gate. DEF-030 implemented; ADR-341/342 implemented;
  DEF-037 remains open for P6d.
- **Completed:** P6d-1 — needs-status-check signal foundation. `KeepRequestNeedsStatusCheckInputs` value object; `GetNeedsStatusCheckInputs(DateOnly today)` domain method; `LastBusinessActivityAt` updated on SetFollowUpOn/ClearFollowUpOn/SetPlannedFor/ClearPlannedFor; 17 unit tests (4 exclusion, 3 suppressor-boundary, 8 signal-max, 2 follow-up/planned activity assertions). 739 unit tests green.
- **Active:** P6d-2 — needs-status-check list/query surface.

### P6d-1 Coding Brief

Source decisions:

- ADR-339: needs-status-check is a policy-driven, signal-extensible human review queue.
- DEF-037 remains open for P6d until the queue/signal model is implemented.

Read first:

- `docs/build-log/060-phase-8-b5-session-6-prerequisites-decisions.md` sections ADR-339, P6d, P6d-1, and Exclusions.
- `src/OpHalo.Keep.Core/Entities/KeepRequest.cs`
- `src/OpHalo.Keep.Core/Entities/KeepRequestEvent.cs`
- `src/OpHalo.Keep.Core/Entities/Enums/KeepRequestEventType.cs`
- `src/OpHalo.Keep.Application/Requests/GetKeepRequestListService.cs`
- `src/OpHalo.Keep.Application/Requests/IKeepRequestListPersistence.cs`
- `src/OpHalo.Keep.Infrastructure/Persistence/KeepRequestListPersistence.cs`
- P6b timing fields and P6c customer-page viewed fields as needed.

Implement:

- Centralized latest-meaningful-activity helper/model for active request review.
- Signal inputs for created, customer message/intent, business update, external contact, status
  change, Follow Up On changes, Planned For changes, and customer page viewed.
- Fail-closed exclusions for active business attention, future Follow Up On, future Planned For,
  Resolved, Closed, and Cancelled.

Do not implement:

- Auto-close, auto-resolve, automatic customer update, or background jobs.
- Notification delivery.
- Broad analytics/reporting or full signal/projection engine.
- The list/query route if it would exceed the slice gate; keep that for P6d-2.

Verify:

- Focused unit tests for signal calculation and exclusions.
- `dotnet build`.

### P6d-2 Preview

- Expose the needs-status-check list/query surface using the P6d-1 signal model.
- Add account-policy threshold with 5-calendar-day pilot default when unset.
- Test due/not-due rows, timing suppressors, active attention suppressor, Resolved/terminal
  exclusions, and role/row visibility.

Do not implement:

- Notification delivery.
- Customer identity portal or access-link management.

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
