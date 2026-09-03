# ADR-495 — GAP-039 Redacted Error Capture and Release Safety

**Status:** Locked  
**Date:** 2026-09-03  
**Related:** GAP-039; Build Log 106; ADR-011; ADR-347

## Context

Keep already has a server-generated correlation ID, Railway release identity, opaque liveness and
readiness endpoints, safe ProblemDetails support, and public-token path redaction. It does not yet
have an errors-only production error-capture boundary. A default Sentry integration could send
customer content, credentials, capability URLs, or other protected request data. Conversely, an
optional, silently unconfigured production integration would make GAP-039 appear complete while
providing neither detection nor alerting.

This decision defines the narrow, testable contract before either SDK is introduced.

## Decision

### D1 — Scope and delivery are errors-only

Sentry captures unhandled API failures and authenticated PWA failures only. The public web site,
session replay, performance tracing, profiling, logs, broad behavioral analytics, a data warehouse,
and Sentry user identity are out of scope. Railway and Vercel logs remain the detailed investigation
source; Sentry is the redacted detection, grouping, and alerting layer.

The initial recipient is the founder by email. Actual DSNs, Sentry organization/project names,
authentication tokens, and recipient address are deployment secrets/console configuration and are
never committed to the repository.

### D2 — Telemetry is an allowlist, not a best-effort redaction policy

Every retained event may contain only:

- environment and a release identifier;
- the API's server-generated correlation ID, when an API request exists;
- a safe route, HTTP method, and status/outcome metadata;
- exception type and stack-frame metadata needed for grouping/diagnosis; and
- `AccountId` only for an authenticated account request, as a Sentry **tag** (never `User`, and
  never on public or failed-authentication requests).

`AccountUserId`, user/customer names, email addresses, phone numbers, service addresses, request
text, form values, free-form extras, request/response bodies, query strings, URL fragments,
authorization headers, cookies, sessions, client IP, breadcrumbs, exception messages, and exception
data are not permitted. `SendDefaultPii` is false and server request-body capture is disabled.

A safe route has no query string or fragment and passes through `PublicTokenPathRedactor`. Its
known public-token route families are therefore represented with `[redacted]`; raw capability URLs
or bearer tokens never leave the process. The scrubber must use a final post-scrub invariant check:
if protected request content or an unredacted capability-token route remains anywhere in the event,
the event is discarded rather than sent. Automated tests assert against the final event representation
for representative PII, credential, query, cookie, and each token-route family.

### D3 — Production configuration must prove the integration is live

The API uses `Sentry:Dsn` (deployment variable `Sentry__Dsn`). A missing or malformed DSN is a
production configuration error and prevents startup through `ProductionConfigurationValidator`.
Development and test environments may deliberately run with no DSN and perform no external send.

The authenticated PWA uses `VITE_SENTRY_DSN`; a browser DSN is public configuration, not a secret.
The production build/deployment must provide a valid value. Preview/local builds may omit it and
run without capture. `VITE_PUBLIC_BASE_URL` is separately required for every production build and
is parsed once by a shared accessor; malformed or missing configuration renders a safe configuration
failure state instead of allowing a request-detail string operation to throw.

The API release is `ReleaseIdentity.Current` (`RAILWAY_GIT_COMMIT_SHA`, otherwise `local`). The PWA
release must be the commit SHA supplied by its deployment environment, using the same SHA when API
and PWA are deployed from one commit. The environments are explicit (`production`, `preview`,
`development`); production events must never be grouped with preview/local events.

### D4 — Browser errors require private release-matched source maps

The authenticated PWA uploads source maps for its exact production release to Sentry during the
production build. The upload credential is build-only and secret. Uploaded source maps are not
publicly served in the Vercel artifact. A deployment that changes the PWA release without its
matching source maps cannot satisfy GAP-039 production-candidate verification.

### D5 — Safe failure behavior and operational gate

Sentry sending is fail-soft after startup: a transport outage, quota condition, or SDK send failure
must not alter API ProblemDetails/status behavior, health responses, or user-facing PWA operation.
The SDK must not capture `/health/live` or `/health/ready` as normal request events.

Before the pilot gate is passed, the founder records in the GAP-039 runbook: the Railway health
check targeting `/health/ready`; the Sentry new-issue/regression email rule; release owner, alert
recipient, technical responder, and contractor-facing communication owner; the rollback versus
mitigation decision path; and the controlled production-candidate verification evidence. The
runbook records references and outcomes, never secret values.

## Consequences

- The first implementation batch is the server scrubber/boundary and ASP.NET SDK, including tests
  proving its final output is safe and an integration proof that existing ProblemDetails behavior is
  unchanged.
- The second batch adds PWA initialization, source-map upload, the public-base-URL accessor and
  fail-safe UI, and tests for absent/malformed configuration.
- Configuration and alert-console work remains a deliberate deployment task, but it is not an
  optional follow-up: lack of live DSNs, alert delivery, or source-map evidence blocks the pilot.
- Package versions are exact, reviewed and pinned at implementation time; "latest" is not a
  reproducible dependency policy.

