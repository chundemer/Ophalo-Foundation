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

## Delivery record

Batch 1 (API boundary and error capture) is delivered — see
[BL141](141-gap-039-batch-1-api-telemetry-boundary-and-error-capture.md) for full evidence.

### Batch 2a — `VITE_PUBLIC_BASE_URL` shared accessor — done, accepted

New `src/lib/publicBaseUrl.ts` (throw-free; `publicBaseUrlResult` typed valid/invalid +
`getPublicBaseUrl()`) and `src/components/ConfigurationError.tsx` (static safe screen). `main.tsx`
renders it before mocks or `<App>` load when config is invalid. Converted `lib/redirectToSignIn.ts`,
`pages/settings/PublicLinkSection.tsx`, `components/ShareLinkModal.tsx`, `components/QuickCapture.tsx`.
`env.d.ts` marks `VITE_PUBLIC_BASE_URL` optional. Tests: accessor
(valid/trailing-slash/base-path/missing/malformed/bad-scheme), `main.tsx` gate. Full app suite 1040
passed; production build passes.

### Batch 2b — request-detail consumer conversions — done, accepted

Converted `pages/RequestDetail.tsx` (2 uses), `pages/request-detail/DetailPanels.tsx`,
`DetailHero.tsx`, `NotifyCustomerPanel.tsx` to `getPublicBaseUrl()`. The request-detail test env
stubs (`NotifyCustomerPanel.test.tsx`, `CallHandoffQr.test.tsx`) now `vi.mock` the accessor rather
than `vi.stubEnv` (the accessor parses at module load). No raw
`import.meta.env.VITE_PUBLIC_BASE_URL` read remains outside `publicBaseUrl.ts`. Full app suite 1040
passed; production build passes.

### Batch 2c — `@sentry/react` init + private source-map upload — done, accepted

`@sentry/react` 10.73.0 + `@sentry/vite-plugin` 5.4.0 (exact pins). `src/lib/sentry.ts`
`initSentry()` runs before render in `main.tsx` — errors-only, no tracing/replay,
`maxBreadcrumbs: 0`; a no-op without `VITE_SENTRY_DSN`. `src/lib/sentryScrub.ts` is the browser
`beforeSend`: a fresh allowlisted event (release, environment, safe pathname, exception type +
sanitized frame metadata) with opaque-token/query/fragment detection scoped to the retained
pathname and frame filename/function only (never the Sentry event id or release SHA), discarding
the whole event if the invariant fails. `ErrorBoundary` forwards React-caught render errors through
that path; the user-facing fallback is unchanged. `scripts/resolveDeployment.ts` is the fail-closed
build gate: `OPHALO_DEPLOY_ENV` (`production`/`preview` only, else the build throws) is
authoritative and independent of Vercel System Environment Variables; a classified build requires
`VERCEL_ENV` to match and a non-local `VERCEL_GIT_COMMIT_SHA`, production additionally requires DSN
+ `SENTRY_AUTH_TOKEN`/`ORG`/`PROJECT`. Source-map upload runs only for a classified build with
complete upload config (`build.sourcemap: "hidden"` + `filesToDeleteAfterUpload:
["./dist/**/*.map"]`, no `errorHandler`); a local build generates no maps. Local build verified:
`dist` has zero `.map` files and no `sourceMappingURL` comments. Full app suite 1064 passed.

The two preflight questions from the original handoff are resolved: (1) environment/release come
from `OPHALO_DEPLOY_ENV` (explicit, system-var-independent) corroborated by `VERCEL_ENV` +
`VERCEL_GIT_COMMIT_SHA`, never `import.meta.env.PROD`; (2) `filesToDeleteAfterUpload` physically
removes every `.map` from the build output after upload — proven locally by `dist` containing zero
`.map` files.

### Batch 3 — founder console configuration — done (founder-owned, no code)

Credentials are recorded only in provider consoles: separate `ophalo-api` and `workbench-pwa`
Sentry projects exist; Railway Production has `Sentry__Dsn` and healthcheck path `/health/ready`;
Vercel Production has the Workbench DSN, organization CI token, organization/project identifiers,
explicit `OPHALO_DEPLOY_ENV=production`, and System Environment Variables enabled. The first
classified Workbench deployment succeeded and its Vercel log confirmed upload of two source-map
artifacts to Sentry release `c37542adb4a8875fc209edd17ce2896757dfb73b`; Sentry shows that release.
New-issue and resolved-issue-regression founder-email alert rules are live for both projects, and
each alert's test notification reached the founder inbox. The non-secret configuration/rotation
record is [Sentry Configuration Runbook](../runbook/sentry-configuration.md).

### Batch 4 — production-candidate verification — paused (founder-owned)

Paused at the safe smoke-login provision step: `support@ophalo.com` is available as the dedicated
alias, but must be explicitly invited as a lowest-permission member of the founder's internal-only
account before use. After that: verify a controlled browser error and a deliberately safe
authenticated API error; inspect release/environment/correlation/redaction and founder-email
delivery; prove deployed source maps are absent; exercise the invalid-public-base-URL fail-safe;
test preview separation if preview capture is enabled; and record the evidence plus named incident
roles in the runbook. The API controlled-error route remains an explicit implementation/operational
decision: there is no permanent production failure endpoint. This gate precedes GAP-033.

