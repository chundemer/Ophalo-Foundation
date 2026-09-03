# Build Log 141 — GAP-039 Batch 1: API Telemetry Boundary and Sentry Error Capture

**Status:** Implemented and accepted (2026-09-03); not yet committed at time of writing.
**Date:** 2026-09-03
**Authority:** [GAP-039](../pilot-readiness-bug-tracker.md#gap-039--production-failures-and-pilot-health-are-not-observable-enough-to-earn-trust), [ADR-495](../decisions/ADR-495-gap-039-redacted-error-capture-and-release-safety.md), [BL140](140-gap-039-sentry-implementation-handoff.md), [BL106](106-production-reliability-release-safety-gate.md)

## Delivered outcome

The API host now has an errors-only, redacted Sentry boundary for unhandled server failures. It is
a complete no-op unless a DSN is configured, and it does not change any existing response, health,
or logging behavior.

- **Dependency:** `Sentry.AspNetCore` `6.10.0` (pinned; explicit `net10.0` target).
- **`SentryTelemetryScrubber`** runs as the SDK `BeforeSend` hook. It does not redact in place — it
  builds a fresh event carrying only the ADR-495 D2 allowlist: environment, release, `Platform`,
  `Level`, sanitized exception type + stack frames, a safe HTTP method, a redacted route, and the
  `correlation_id` / `account_id` / `http.status_code` tags. Everything else (messages, exception
  messages/`Data`, locals and source-context lines, request/response bodies, query/fragment,
  headers, cookies, sessions, client IP, breadcrumbs, `User`, contexts, modules, server name,
  thread dumps, **fingerprints**, `Logger`) is never copied across.
- **Path handling:** `SanitizePath` strips fragment then query string, then applies
  `PublicTokenPathRedactor`, and is used for both the request route and every retained stack-frame
  `FileName`/`AbsolutePath`. `/health/live` and `/health/ready` events are discarded.
- **Tag validation:** an allowlisted tag whose value is malformed is dropped —
  `correlation_id` must be 32 hex chars, `account_id` a non-empty GUID, `http.status_code` an
  integer 100–599.
- **Residual guard:** after the rebuild, if any known capability-token route still carries a raw
  token in a retained string, the whole event is discarded.
- **`RequestContextSentryEventProcessor`** (singleton, `IHttpContextAccessor` only) attaches
  `correlation_id` from `HttpContext.Items` and attaches `account_id` **only** when the framework
  request is authenticated and carries a valid `account_id` + `NameIdentifier` GUID claim pair.
- **Program wiring:** `SendDefaultPii=false`, `MaxRequestBodySize=None`, `MaxBreadcrumbs=0`,
  `AutoSessionTracking=false`, `CaptureFailedRequests=false`, no tracing/profiling/replay,
  `BeforeBreadcrumb ⇒ null`. `options.InitializeSdk` is set to *DSN present*, so with no DSN the
  SDK never touches the global Sentry hub.
- **`ProductionConfigurationValidator`** now requires `Sentry:Dsn` (present and an absolute
  http/https URI) in every non-Development/Testing environment; a missing or malformed DSN fails
  startup. `appsettings.json` carries an empty `Sentry:Dsn` placeholder.
- **`CorrelationIdMiddleware`** additionally stores the id in `HttpContext.Items` for capture-time
  consumers.

## Preserved contracts

- `AddProblemDetails()` behavior, all existing status codes, and the opaque `/health/*` bodies are
  unchanged. No exception-handler middleware was introduced; an unhandled failure is not rewritten.
- Sentry sending is fail-soft — a transport/quota/SDK failure cannot alter API behavior.
- The locked GAP-013 token-redaction policy remains centralized in `PublicTokenPathRedactor`.

## Test-only failure route

`GET /__test/unhandled` is mapped **only** when `IsEnvironment("Testing")` and raises an
`InvalidOperationException`. It exists so an integration test can prove the Sentry boundary
observes a real unhandled exception, redacted, without changing normal API behavior. It cannot be
present in any production deployment. (Requested during Batch 1 review; distinct from the BL140 §4
prohibition on a permanent public failure endpoint.)

## Verification

- `OpHalo.UnitTests` full: 1759 passed. New: `SentryTelemetryScrubberTests` (24),
  `ProductionConfigurationValidatorTests` (+7).
- `OpHalo.IntegrationTests` full: 1594 passed. New: `SentryBoundaryTests` (5),
  `SentryUnhandledCaptureTests` (1, recording-transport proof of redacted capture),
  `RequestContextSentryEventProcessorTests` (9).
- `OpHalo.ArchitectureTests`: 14 passed. `git diff --check` clean.

## Changed files

Production: `src/OpHalo.Api/OpHalo.Api.csproj`, `Program.cs`,
`Diagnostics/SentryTelemetryScrubber.cs` (new), `Diagnostics/RequestContextSentryEventProcessor.cs`
(new), `Diagnostics/CorrelationIdMiddleware.cs`, `Diagnostics/ProductionConfigurationValidator.cs`,
`appsettings.json`.
Tests: `UnitTests/Diagnostics/SentryTelemetryScrubberTests.cs` (new),
`UnitTests/Diagnostics/ProductionConfigurationValidatorTests.cs`,
`IntegrationTests/Api/SentryBoundaryTests.cs` (new),
`IntegrationTests/Api/RequestContextSentryEventProcessorTests.cs` (new).

## Not in this batch

Authenticated-PWA Sentry, `VITE_PUBLIC_BASE_URL` shared accessor + fail-safe UI, source-map upload
(BL140 Batch 2); Railway health-check / DSN / alert-rule console config and the runbook
(BL140 Batch 3); production-candidate verification (BL140 Batch 4); product-operations digest.

## Founder-owned console prerequisites before a production deploy

1. Create the API Sentry project; obtain its DSN.
2. Set `Sentry__Dsn` in Railway — until then a production/staging deploy fails fast with
   `Required production configuration is missing or invalid: Sentry__Dsn.`
