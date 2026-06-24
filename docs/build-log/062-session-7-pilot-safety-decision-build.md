# Build Log 062 — Session 7 Pilot Safety Decision/Build

**Started:** 2026-06-24  
**Status:** S7a+S7a-2 complete. S7b next.

Session 7 prepares the backend for pilot operation before notification delivery. Decisions are now
locked; implementation should proceed in the bounded slices below, with a fresh file-level gate before
each slice writes code.

---

## Scope

Primary goal: make the backend safe to operate during pilot.

Decision areas:

- persistent local PostgreSQL setup, migration, smoke, and reset runbook;
- production-like trusted-proxy behavior for Cloudflare/Railway;
- rate-limit proof outside the `Testing` host path;
- token-safe logging and redaction for public intake, customer pages, auth/session, request links,
  page tokens, invite tokens, and future notification payloads;
- public-intake abuse posture needed before pilot, including bounded validation, spam/test
  classification timing, founder/internal emergency runbook, and what remains post-pilot only.

Out of scope:

- notification-device table and push delivery;
- native app UX;
- broad reporting/analytics;
- adaptive bot challenges, source blocking, and phone verification unless pilot evidence pulls them
  forward.
- broad demo/sales scenario seeding, account classification, and run-as/impersonation tooling
  (deferred to DEF-079 and DEF-080).

---

## Preflight Instructions

1. Classify each implementation slice as mechanical implementation preflight unless new code
   discovery contradicts a locked decision.
2. Inspect existing hosting, middleware, logging, rate-limiting, and test patterns before proposing
   code.
3. Produce a file-level implementation gate before edits in every slice.
4. Keep each implementation slice inside the standard gate: at most 3 mutation families, 8
   production files, and 12 total files including tests/docs.
5. Preserve fail-closed behavior for account, row, action, and public-token paths.

---

## Locked Decisions

### ADR-345 — Persistent local PostgreSQL runbook is required before notifications

Session 7 requires a persistent local PostgreSQL runbook covering:

- applying EF migrations to a persistent local database;
- running a small production-like smoke check;
- safely resetting local pilot/dev data.

Broad seed/demo data is excluded from normal setup. Sales/demo scenario data is deferred until the
app has explicit Demo/InternalTest account classification plus safe reset and role-demo rules
(DEF-079, DEF-080).

### ADR-346 — Rate limiting needs automated production-like proof

Normal integration tests may continue skipping runtime rate limiting in the `Testing` environment.
Session 7 must add a focused production-like smoke/integration path outside `Testing`, with tiny
deterministic thresholds, proving protected public/auth routes return `429` when exceeded.

### ADR-347 — Trusted client IP and optional country are internal safety context

Behind Cloudflare/Railway, the API must resolve trusted client IP explicitly for rate limiting and
internal abuse/security logging. When a trusted Cloudflare country signal is available, the app may
capture/use country as optional internal context. V1 does not depend on precise geolocation, does not
expose raw IP/location to businesses, and does not make broad authenticated access decisions from
country. U.S.-only or country-based public-write restrictions remain a later decision unless pilot
abuse pulls them forward.

### ADR-348 — Token-bearing flows are secret-safe and supportable

Logs, errors, request logging, and test output must not include raw public intake tokens, customer
page tokens, invite tokens, auth/session/bearer credentials, exchange codes, raw token URLs, or
future notification secrets. The app may expose safe user-facing reason messages such as expired or
invalid link. Internal diagnostics may use route templates, correlation IDs, entity IDs after
lookup, failure reason codes, expiry/rotation metadata, and stable keyed token fingerprints.

### ADR-349 — Spam/Test classification is pre-notification V1 scope

Owner/Admin can classify requests as `Spam` or `Test` through an explicit confirmed action with an
optional internal reason. Classification removes the request from operational queues, disables
further customer-page activity, excludes the request from operational/impact metrics, and preserves
audit history. Operators cannot classify in V1. Deletion, source blocking, adaptive bot checks,
phone verification, and abuse dashboards remain post-pilot unless real abuse pulls them forward.

### ADR-350 — Spam and Test are explicit terminal statuses

`Spam` and `Test` are explicit terminal request statuses, not cancellation reasons layered onto
`Cancelled`. They remove the request from operational queues, disable customer-page activity,
preserve audit/history, and are excluded from operational/impact metrics. `Spam` and `Test` remain
distinct because `Test` supports onboarding, demos, training, and QA, while `Spam` represents
junk/abuse.

---

## Claude Coding Sessions

### S7a — Local PostgreSQL runbook and pilot safety docs

**Goal:** Make persistent local database operations repeatable before notification work.

**Status:** Complete — doc runbook added.

Scope:

- Document local persistent PostgreSQL setup, migrate, smoke, and reset commands.
- Include required environment variables/connection-string shape.
- Record local-only reset guardrails.
- Add or document minimal smoke commands; do not add broad seed/demo data.
- Add founder/internal public-intake abuse emergency runbook notes at the doc level.

Out of scope:

- Demo account classification or scenario packs.
- Production deployment automation.
- Spam/Test API implementation.

Completion gate:

```text
docs updated with exact commands
commands verified locally when feasible
session-log updated with results and carry-forwards
```

**Delivered:**

- Added `docs/deployment/local-postgres-runbook.md`.
- Documented the persistent local database name (`ophalo_local`), `DefaultConnection` environment
  variable shape, and required `Keep__RequestListCursorSigningKey` local runtime key.
- Documented the Keep-aware EF migration command using
  `--project src/OpHalo.Foundation.Infrastructure`,
  `--startup-project src/OpHalo.Keep.Infrastructure`, and `--context OpHaloDbContext`.
- Documented minimal local smoke checks: Development OpenAPI endpoint plus invalid-token public
  intake execution with a valid-shaped body.
- Documented a guarded local reset using the integration-test pattern (`DROP SCHEMA public CASCADE`,
  recreate schema, then migrate) with a hard database-name guard for `ophalo_local`.
- Added founder/internal public-intake abuse emergency notes: safe evidence capture, no raw tokens,
  intake-link replacement if compromised, Spam/Test classification once available, and deferred
  adaptive controls.

**Verification:**

- Source inspection confirmed runtime `DefaultConnection`, `Keep:RequestListCursorSigningKey`,
  migration history table `__OpHaloMigrationsHistory`, Keep design-time factory command shape, and
  integration reset pattern.
- Local command execution was not performed in this doc slice; no persistent local PostgreSQL
  instance was assumed.

### S7b — Trusted proxy/client IP plus rate-limit smoke proof

**Goal:** Prove rate limiting works in a production-like host path and uses the intended client
identity.

Scope:

- Inspect current forwarded-header, Cloudflare/Railway, and rate-limiter wiring.
- Add explicit trusted client IP resolution/configuration if missing.
- Prefer trusted Cloudflare `CF-Connecting-IP` when configured; fall back safely to standard
  forwarded headers/remote IP per configuration.
- Treat trusted country as optional internal context only.
- Add focused non-`Testing` smoke/integration proof with tiny deterministic limits and `429`
  assertions for protected public/auth routes.

Out of scope:

- Country-based public-write blocking.
- Full load tests.
- Adaptive bot challenges/source blocking.

Completion gate:

```text
focused proxy/rate-limit tests green
normal integration Testing host behavior unchanged
docs/session-log records exact protected routes proven
```

### S7c — Token-safe logging and redaction guardrails

**Goal:** Keep token-bearing flows debuggable without leaking secrets.

Scope:

- Inspect request logging, error handling, token parsing, public/customer/invite/auth routes, and
  test output patterns.
- Add redaction helpers/middleware/configuration/tests if gaps exist.
- Ensure raw tokens and raw token URLs are not logged or echoed.
- Preserve safe troubleshooting through route templates, correlation IDs, reason codes, entity IDs
  after lookup, expiry metadata, and keyed fingerprints where useful.

Out of scope:

- New analytics/logging platform.
- Customer-visible location/IP information.
- Notification delivery implementation.

Completion gate:

```text
targeted redaction tests or proof green
known token-bearing routes inventoried
session-log records any remaining operational logging caveats
```

### S7d — Spam/Test terminal-status foundation

**Goal:** Add explicit terminal statuses and make read surfaces treat them as non-operational.

Scope:

- Add `Spam` and `Test` as terminal `KeepRequestStatus` values.
- Update terminal/status helpers, allowed transitions, serialization/mapping, and persistence
  configuration as needed.
- Exclude Spam/Test from operational queues, active counts, stale/status-check, ready-to-close, and
  customer-page activity.
- Preserve history/audit visibility where authorized.
- Add focused unit/integration coverage for terminal behavior and list exclusion.

Out of scope:

- Owner/Admin classification endpoint/service.
- Source blocking or deletion.
- Reporting platform metrics beyond current operational surfaces.

Completion gate:

```text
status/domain/list/customer-page focused tests green
migration inspected if generated
no operational list shows Spam/Test as active work
```

### S7e — Spam/Test classification command

**Goal:** Let Owner/Admin classify junk or test requests safely before notification delivery.

Scope:

- Add explicit confirmed Owner/Admin action to classify a request as `Spam` or `Test`.
- Require expected request version like other existing-request mutations.
- Allow optional short internal reason.
- Reject Operators/Viewers and cross-account/invisible rows with existing fail-closed patterns.
- Return updated detail/history result according to existing mutation style.
- Create internal audit/history event metadata for classification.
- Ensure classification disables further customer-page writes.

Out of scope:

- Bulk classification.
- Delete/restore/unclassify workflow.
- Adaptive abuse controls.

Completion gate:

```text
authorization, concurrency, validation, and customer-page-block tests green
classification preserves audit/history and rotates version
docs/session-log records endpoint and file changes
```

### S7f — Final pilot-safety ledger and regression gate

**Goal:** Close Session 7 with docs, deferred topics, and verification aligned.

Scope:

- Update decision index, deferred topics, and session log for completed slices.
- Confirm DEF-061 reflects implemented Spam/Test behavior and remaining abuse controls.
- Confirm DEF-079/DEF-080 remain deferred.
- Run proportionate broader tests, then full suite if feasible.
- Record any deployment/manual checks for Cloudflare/Railway and persistent PostgreSQL.

Completion gate:

```text
session-log points to next work
decision index next ADR is correct
full suite green if feasible, otherwise focused suite + reason recorded
Session 8 notification/device foundation remains next
```
