# Session Log — OpHalo Foundation

**Last updated:** 2026-06-23 (P6c-2 complete — ready for P6c-3 completion gate)
**Branch:** `main` tracking `origin/main` (currently 6 local commits ahead)
**Current baseline:** 1334 tests (722 unit · 14 architecture · 598 integration) — full suite green.
**Next free ADR:** ADR-345
**Next batch: P6c-3 — P6c completion gate.**

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

Session 6 proper remains paused. P6b is complete and the active prerequisite is P6c.

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
- **Active:** P6c-3 — P6c completion gate.

### P6c-2 Coding Brief

Source decisions:

- ADR-341: customer page viewed is a confidence/adoption signal, not presence or a read receipt.
- DEF-030 remains open until page viewed telemetry/metadata is implemented.

Read first:

- `docs/build-log/060-phase-8-b5-session-6-prerequisites-decisions.md` sections ADR-341, P6c-2, and Exclusions.
- `src/OpHalo.Keep.Application/Requests/KeepPublicCustomerAccessGuard.cs`
- `src/OpHalo.Keep.Application/Requests/GetKeepCustomerPageService.cs`
- `src/OpHalo.Keep.Application/Requests/KeepCustomerPageResult.cs`
- `src/OpHalo.Keep.Application/Requests/KeepCustomerPageMapper.cs`
- `src/OpHalo.Keep.Application/Requests/IKeepCustomerWritePersistence.cs`
- `src/OpHalo.Keep.Infrastructure/Persistence/EfKeepCustomerWritePersistence.cs`
- `src/OpHalo.Keep.Infrastructure/Persistence/Configurations/KeepRequestConfiguration.cs`
- `src/OpHalo.Keep.Application/Requests/KeepRequestDetailResult.cs`
- `src/OpHalo.Keep.Application/Requests/KeepRequestDetailMapper.cs`
- `tests/OpHalo.IntegrationTests/Api/KeepCustomerPageTests.cs`

Implement:

- Durable page-view telemetry with debounce/rate-limit semantics so refreshes do not spam writes.
- Staff-facing metadata: last viewed, viewed after latest business update, never viewed.
- Metadata must be cautious: no presence/online/read-receipt language.
- Page views must not raise business attention and must not suppress stale/status-check in this slice.

Do not implement:

- Full signal/projection engine.
- Needs-status-check logic.
- Notification delivery.
- Customer identity portal or access-link management.

Verify:

- Unit/domain tests for debounce and metadata derivation.
- Customer-page API tests for active/expired/terminal behavior and no internal data leakage.
- Authenticated detail/list tests only if staff-facing metadata is exposed there.
- `dotnet build`.

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
