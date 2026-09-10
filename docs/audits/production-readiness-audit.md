# Production-Readiness Solution Audit

**Opened:** 2026-09-10 · **Context:** running alongside the announcements + feedback (GAP-038) work,
ahead of the HVAC supervised pilot.

## How this runs

- One vector per session. Do **not** attempt more than one vector in a single pass — each is a
  codebase-wide read and will otherwise be shallow.
- Each vector produces **findings only**, recorded in its section below: `file:line`, the concrete
  failure scenario, severity (`blocker` / `pilot-risk` / `hardening` / `ok`), and a recommendation.
- Remediation is **not** done here. Each confirmed blocker/pilot-risk becomes a workboard item or a
  DEF ticket and goes through the normal batch gate.
- Fan-out reads (enumerate endpoints, find query sites) go to `Explore` / `general-purpose`
  subagents; synthesis and the write-up stay in the driving session.
- Scope note: repository docs are authoritative (`workboard.md`, `decision-index.md`). `_reference/`
  is not in scope and is never edited.

## Status board

| # | Vector | Status | Session | Blockers | Pilot-risks |
| --- | --- | --- | --- | --- | --- |
| 1 | Multi-tenancy, security & entitlement gating | Not started | — | — | — |
| 2 | State concurrency & transactional integrity | Not started | — | — | — |
| 3 | Backend performance & database optimization | Not started | — | — | — |
| 4 | Client reflow, UX state & layout resilience | Not started | — | — | — |
| 5 | Edge cases & offline / network resilience | Not started | — | — | — |
| 6 | Public surface & rate limiting | Not started | — | — | — |
| 7 | File size & solution architecture (+ dependency scan) | Not started | — | — | — |
| 8 | Auth & session security | Not started | — | — | — |
| 9 | HTTP & transport hardening | Not started | — | — | — |
| 10 | Deploy & release safety | Not started | — | — | — |
| 11 | Multi-instance / horizontal-scaling readiness | Not started | — | — | — |

---

## Vector 1 — Multi-tenancy, security & entitlement gating

**Checklist**

- [ ] Every DB query explicitly scopes by `account_id` / tenant context; route-param IDs cannot
      cross tenant boundaries (IDOR).
- [ ] Optional capability-package routes (e.g. Price Book) enforce
      `account_capability_package_enrollments` server-side in API middleware, not client UI.
- [ ] RBAC (Owner / Admin / Operator / Technician) enforced at the API route level.
- [ ] Price-blind rules (cost/margin hidden from technicians) stripped at the serialization layer,
      not via CSS.
- [ ] All incoming payloads pass strict schema validation before business logic / persistence.

**Findings**

_None recorded yet._

---

## Vector 2 — State concurrency & transactional integrity

**Checklist**

- [ ] Mutations carry an expected `version` / timestamp; stale concurrent writes rejected with
      `409` and unsaved client text preserved during recovery.
- [ ] Multi-step operations (account creation on `/auth/exchange`, contact-event logging, draft-visit
      conversion) run inside a DB transaction; partial failure rolls back fully.
- [ ] State-mutating `POST`/`PUT` use idempotency keys or submit lockouts against double-submit.
- [ ] Disabling a package / subscription is a soft revocation — submitted visits, financial
      snapshots, and activity history remain intact and immutable.

**Findings**

_None recorded yet._

---

## Vector 3 — Backend performance & database optimization

**Checklist**

- [ ] List endpoints (Request Queue, Activity Logs, Price Book catalog) free of N+1 ORM patterns —
      eager loading / join constraints / batch fetch.
- [ ] Compound indexes exist for high-frequency filters (`account_id + status`,
      `account_id + priority`, `account_id + updated_at`).
- [ ] Every list endpoint enforces a hard record cap; no unbounded arrays into memory.
- [ ] Background tasks (SMS/email, webhooks) have retry backoff, dead-lettering / abandon, and
      explicit error handlers — a failed notification never crashes the API.

**Findings**

_None recorded yet._

---

## Vector 4 — Client reflow, UX state & layout resilience

**Checklist**

- [ ] When optional capability packages / onboarding steps are absent, the DOM omits the unused
      tabs / toolbars / panels — no empty gaps or `visibility:hidden` whitespace.
- [ ] Active package enrollments and role permissions resolve during `/auth/exchange` and update the
      local store without a full page reload.
- [ ] Major sections (Queue, Workbench, Memory Rail) wrapped in error boundaries — a sub-component
      crash does not blank the screen.
- [ ] Optimistic UI reverts and shows an accessible toast when the API write fails.

**Findings**

_None recorded yet._

---

## Vector 5 — Edge cases & offline / network resilience

**Checklist**

- [ ] Field workflows (draft visit entries) survive high-latency / dropped connections — drafts
      persist locally so work is not lost.
- [ ] Dirty-state forms (long customer reply, line-item entry) warn on navigate-away
      (`beforeunload` + in-app route guard).
- [ ] Timestamps stored UTC at the DB layer, converted to the business timezone
      (`America/Chicago`) at the view layer.

**Findings**

_None recorded yet._

---

## Vector 6 — Public surface & rate limiting

**Checklist**

- [ ] Public intake forms (`/keep/s/[business-slug]`, magic-link exchange) have IP-based +
      token-bucket rate limiting against spam / abuse.
- [ ] Public pages expose zero internal metadata — no internal notes, pricing margins, tech
      assignment history, or raw internal DB IDs.
- [ ] `GET /updates/guides/img/{name}` (and any other name/path-parameter file serving) rejects
      path traversal and only serves from the allowlisted asset store.

**Findings**

_None recorded yet._

---

## Vector 7 — File size & solution architecture (+ dependency scan)

**Checklist**

- [ ] No production source file is large enough to be a maintenance / review hazard; oversized files
      have a split recommendation.
- [ ] Layer boundaries hold (Foundation ⇏ Keep; Core ⇏ Application/Infrastructure; Application ⇏
      Infrastructure; single `OpHalo.Api` host) — confirmed by architecture tests.
- [ ] No god-objects / god-services concentrating unrelated responsibilities.
- [ ] `dotnet list package --vulnerable --include-transitive` and `pnpm audit` (all three web/mobile
      workspaces) are clean or have documented accepted risk.
- [ ] All dependencies pinned (no floating ranges in what ships); `pnpm-lock.yaml` / `packages.lock`
      committed and current.
- [ ] Dockerfile pins an explicit base-image digest/tag and runs the API as a non-root user.

**Initial size scan (2026-09-10, non-generated files)**

Backend (`src/**/*.cs`, excludes `Migrations/`):

| Lines | File |
| --- | --- |
| 1605 | `src/OpHalo.Keep.Core/Entities/KeepRequest.cs` |
| 1555 | `src/OpHalo.Api/Keep/KeepEndpoints.cs` |
| 1442 | `src/OpHalo.Keep.Application/Requests/GetKeepRequestListService.cs` |
| 900 | `src/OpHalo.Keep.Core/Entities/KeepRequestEvent.cs` |
| 681 | `src/OpHalo.Keep.Infrastructure/Persistence/KeepRequestListPersistence.cs` |
| 642 | `src/OpHalo.Keep.Application/PriceBook/ActualWorkDraftApiService.cs` |
| 615 | `src/OpHalo.Keep.Application/Requests/KeepRequestDetailMapper.cs` |
| 509 | `src/OpHalo.Api/Program.cs` |

Frontend (`web/**`, excludes tests / node_modules):

| Lines | File |
| --- | --- |
| 2135 | `web/ophalo-app/src/pages/request-detail/ActualWorkComposer.tsx` |
| 1560 | `web/ophalo-app/src/lib/apiClient.types.ts` |
| 1281 | `web/ophalo-app/src/mocks/fixtures.ts` |
| 1270 | `web/ophalo-app/src/pages/PriceBook.tsx` |
| 1159 | `web/ophalo-app/src/pages/request-detail/DetailPanels.tsx` |
| 1135 | `web/ophalo-app/src/lib/apiClient.ts` |
| 855 | `web/ophalo-app/src/pages/RequestDetail.tsx` |
| 746 | `web/ophalo-app/src/App.tsx` |

Mobile: `mobile/ophalo-mobile/app/requests/[id].tsx` — 1588.

Auto-generated EF `*.Designer.cs` / model snapshot files (~4.9k lines each) are excluded — not a
maintenance concern.

**Findings**

_None recorded yet._

---

## Vector 8 — Auth & session security

**Checklist**

- [ ] Session tokens are opaque, high-entropy, server-side; storage uses a hash, not the raw token.
- [ ] Sessions have a bounded lifetime + idle expiry; renewal path is safe.
- [ ] Sessions are revoked (or re-evaluated) on role change, member removal, and account
      deactivation — a removed member cannot keep acting on a live session.
- [ ] Magic-link and invite tokens are single-use, short-expiry, and rate-limited per
      email / IP; consumed links cannot be replayed.
- [ ] `/auth/exchange` and sign-in request endpoints have abuse protection (see Vector 6) and do not
      leak whether an email exists.
- [ ] Account-creation-on-exchange runs in one transaction (cross-refs Vector 2).

**Findings**

_None recorded yet._

---

## Vector 9 — HTTP & transport hardening

**Checklist**

- [ ] Response security headers present: HSTS, `X-Content-Type-Options: nosniff`, frame/`frame-ancestors`
      protection, `Referrer-Policy`. (CSP is tracked separately as a deferred DEF ticket.)
- [ ] CORS is an explicit origin allowlist — no `*` with credentials, no reflected origin.
- [ ] Session cookies set `Secure` + `HttpOnly` + `SameSite`; no sensitive data in
      non-`HttpOnly` cookies or `localStorage`.
- [ ] Production error responses return a stable error contract with no stack traces, framework
      detail, SQL, or internal IDs (`ErrorHttpMapper.cs`).
- [ ] `4xx` vs `5xx` discipline — expected domain rejections are not logged/counted as server errors.
- [ ] No client-bundle env leakage — only intentionally public `VITE_` / `NEXT_PUBLIC_` vars reach
      the browser; no secrets, internal URLs, or source maps exposing internals in production.

**Findings**

_None recorded yet._

---

## Vector 10 — Deploy & release safety

**Checklist**

- [ ] Migration ordering vs code deploy is defined and safe (expand/contract for destructive
      changes; new code tolerates the pre-migration schema or migration runs first).
- [ ] Documented rollback plan for a bad API deploy and for a bad migration.
- [ ] Post-deploy smoke check (health, auth, one read, one write) is defined.
- [ ] Feature-flag hygiene: paired-config fail-fast (the `Feedback:Enabled` +
      `FounderChannel:WebhookUrl` pattern) applied consistently to every flag that needs external
      config; committed production defaults are safe.
- [ ] `ProductionConfigurationValidator` covers every required production setting; startup fails
      loudly on a missing/invalid one.
- [ ] Single API host assumption is documented where it matters (cross-refs Vector 11).

**Findings**

_None recorded yet._

---

## Vector 11 — Multi-instance / horizontal-scaling readiness

**Checklist**

- [ ] Inventory every in-process / per-instance store and state holder: `UpdatesFeedCache`,
      `FounderAlertThrottle` buckets, feedback per-`account_user` rate-limit counters, any
      `IMemoryCache` / static state.
- [ ] For each: state the behavior if 2+ API instances run (double alerts, halved rate limits,
      cache divergence) and whether that is acceptable for the pilot.
- [ ] Background workers (`FeedbackDeliveryWorker`, `FeedbackMaintenanceBackgroundService`) are
      safe under >1 instance — row locking (`SKIP LOCKED`) or a documented single-worker constraint.
- [ ] Session store, rate limiting that must be authoritative, and idempotency keys are
      DB-backed (shared), not in-process.
- [ ] Decision recorded: pilot runs exactly one API instance (documented) **or** the above are
      made instance-safe.

**Findings**

_None recorded yet._
