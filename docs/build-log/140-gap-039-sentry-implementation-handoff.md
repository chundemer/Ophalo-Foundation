# Build Log 140 — GAP-039 Sentry Implementation Handoff

**Status:** Ready for implementation  
**Date:** 2026-09-03  
**Authority:** [GAP-039](../pilot-readiness-bug-tracker.md#gap-039--production-failures-and-pilot-health-are-not-observable-enough-to-earn-trust), [ADR-495](../decisions/ADR-495-gap-039-redacted-error-capture-and-release-safety.md), Build Log 106

## Outcome

Deliver a no-new-recurring-cost, errors-only Sentry safety net for the API and authenticated PWA.
It answers *what broke, in which release, and how to correlate it to infrastructure logs* without
transmitting protected request/customer data. It is not product analytics and must not be expanded
into one by implication.

## Non-negotiable boundary

Only release, environment, server-generated correlation ID, safe route/method/status, exception
type/stack frames, and authenticated `AccountId` as a tag may survive. Strip or do not create all
other event data, including request/response bodies, query/fragment data, headers, cookies,
sessions, client IP, breadcrumbs, exception messages/data, user identity, customer/business contact
data, service addresses, request text, and capability URLs/tokens. Run `PublicTokenPathRedactor`
on any retained API route. If a final validation pass cannot prove an event safe, discard it.

`SendDefaultPii` is false. Replay, tracing, profiling, Sentry logs, metrics, performance monitoring,
and the public `ophalo-web` client are excluded. Do not introduce a database exception table.

## Current repository facts

- API host: `src/OpHalo.Api`, .NET 10, entry point `Program.cs`.
- `CorrelationIdMiddleware` assigns a fresh server ID, echoes `X-Correlation-Id`, and logs it with
  `ReleaseIdentity.Current`; it never trusts an inbound value.
- `ReleaseIdentity.Current` is `RAILWAY_GIT_COMMIT_SHA`, falling back to `local`.
- `PublicTokenPathRedactor` covers public-intake, continuity, tracker, intake-SMS, share-SMS, and
  share-call token routes. Its unit tests are the base test matrix to extend.
- API currently calls `AddProblemDetails()` but has no exception handler. Preserve existing status
  and safe ProblemDetails behavior while adding unhandled-error capture.
- Production configuration currently validates database, public base URL, and Resend. Add the API
  Sentry DSN to this same production-only validator.
- `/health/live` is opaque and `/health/ready` checks the database. Neither is normal telemetry.
- Authenticated PWA: `web/ophalo-app`; entry point `src/main.tsx`; Vite configuration is
  `vite.config.ts`. Do not add Sentry to `web/ophalo-web` in this slice.
- `VITE_PUBLIC_BASE_URL` has multiple unsafe direct uses. Replace every one with a single parsed
  accessor and a safe configuration-failure UI path. Do not leave any direct `.replace()` on an
  environment variable.

## Batches and file gates

### 1. API boundary and error capture

- Add a pinned, reviewed `Sentry.AspNetCore` package version compatible with .NET 10.
- Add a testable scrubber/final-validator in `src/OpHalo.Api/Diagnostics/`; keep token-redaction
  logic centralized in `PublicTokenPathRedactor`.
- Wire the SDK in `Program.cs` before the app begins handling requests. Attach release,
  environment, correlation ID, safe route/status, and authenticated `AccountId` tag only.
- Add `Sentry:Dsn` placeholder to `appsettings.json` and require `Sentry__Dsn` outside local/test
  environments via `ProductionConfigurationValidator`.
- Tests: final retained event has no representative PII/credentials/query/cookie/body/token route;
  malformed or omitted production DSN fails startup; unhandled-failure capture does not alter the
  established API ProblemDetails/status contract; health endpoints are not captured.

### 2. Authenticated PWA capture and configuration safety

- Add pinned, reviewed `@sentry/react` and the build integration required for private source-map
  upload. Initialize before rendering in `main.tsx`.
- Require `VITE_SENTRY_DSN` for a production build/deployment but leave preview/local capture off
  when it is absent. It is public configuration, not a secret.
- Use deployment-provided commit SHA for the PWA release and explicit production/preview/development
  environments. API/PWA built from one commit must share that release value.
- Configure build-only Sentry source-map upload. Its authentication token is secret; source maps
  must not be publicly served in the Vercel artifact.
- Test malformed/absent `VITE_PUBLIC_BASE_URL` renders a safe failure rather than throwing. Test
  the shared accessor and all converted consumers.

### 3. Console/runbook work — founder-owned, not a code substitute

- Create distinct API and authenticated-PWA Sentry projects.
- Store `Sentry__Dsn` in Railway; `VITE_SENTRY_DSN`, release SHA, and the source-map upload token
  in the appropriate Vercel build environment. Never commit or paste values into an issue/log.
- Configure Railway health checking to `GET /health/ready` and create Sentry new-issue/regression
  email alerts to the founder.
- Document the actual release owner, technical responder, contractor-facing communication owner,
  alert recipient, health-check settings, and rollback-versus-mitigation path in the runbook.

### 4. Production-candidate verification

Use the dedicated smoke account/inbox described in `docs/runbook/production-smoke-test.md`; never
use a pilot business. Verify normal and unhealthy readiness, a controlled API error, a controlled
PWA error, release/environment/correlation context, redaction, alert arrival, source-map resolution,
and invalid public-base-URL fail-safe behavior. Do not introduce a permanent public debug/failure
endpoint merely to perform this test.

## Product-operations visibility is separate follow-up work

Do not route signups or ordinary adoption events to Sentry. The repository already has the durable
`KeepProductOpsEvent` table and `KeepProductOpsEventType` vocabulary introduced by ADR-375/Build
Log 066. It is the zero-new-vendor-cost source of truth for account creation, onboarding, first-use,
engagement, inactivity/risk, and feedback signals.

The later product-ops slice must first wire the currently deferred event types at their authoritative
write points, then provide a founder-only weekly Markdown/text digest. The digest can resolve the
account's business name through the normal internal database read, but event rows themselves retain
only account ID, event type, and UTC time. Repeated/weekly signals require a deliberate schema
change because the current unique `(AccountId, EventType)` constraint correctly enforces singleton
first-use events.

## Completion standard

Do not mark GAP-039 complete merely because packages compile. It completes only with the redaction
tests, unchanged safe API behavior, production configuration checks, private source maps, a live
founder alert, verified Railway readiness monitoring, and recorded production-candidate evidence.

