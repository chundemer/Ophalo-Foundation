# Build Log 106 — Production Reliability and Release Safety Gate

**Status:** Launch-critical operational decision record
**Date:** 2026-07-29
**Scope:** Safe operation of the first paid/pilot contractor launch; not a generic observability
platform
**Related:** GAP-039a/b/c, OPS-008, Build 104, deployment runbook

## Why this is a launch gate

Keep has health/readiness endpoints, correlation IDs, startup configuration validation, release
identity, rate-limit proof, and a smoke-test/runbook foundation. Those are valuable foundations,
not a complete production operating posture.

OPS-008 demonstrated the remaining risk: a migration-dependent release reached production before
its database migration was applied and caused request-list failures. A production release must be
safe even when a deploy, dependency, migration, browser cache, or provider component fails.

## Required launch evidence

### Error detection and investigation

- Complete GAP-039b: browser and API Sentry projects, errors-only unhandled capture, existing
  release/correlation metadata, and founder new-issue/regression alerts.
- Preserve the locked redaction policy: no authorization headers, cookies, codes, page tokens,
  capability URLs, customer names/contact data, request text, or free-form data in telemetry.
- Sentry replay, tracing, profiling, logs/telemetry, and user identity remain disabled for launch.
- Railway/Vercel logs remain the detailed correlated investigation source.

### Database recovery

- Record the actual provider backup retention, point-in-time recovery capability, restore method,
  expected recovery time, owner, and cost posture.
- Perform a safe restore rehearsal or explicitly document why the provider cannot support one before
  launch; do not assume a managed database makes recovery proven.
- Lock a business RPO and RTO appropriate to the contractor's daily operation and communicate the
  fallback process.

### Migration and deployment safety

- Every release with a migration has an ordered, recorded migration step and a post-migration schema
  verification before dependent application code serves real traffic.
- Prefer backward-compatible expand/contract migrations. A destructive or irreversible migration
  requires a separately approved recovery plan.
- Define rollback versus forward-fix/degradation behavior before deployment; an application rollback
  cannot necessarily undo an applied migration.
- Run the repository smoke check and targeted real-browser paths after production deployment.

### Human operation

- Name the release owner, alert recipient, technical incident responder, and contractor-facing
  communication owner.
- Write a short support/fallback path for the contractor when Keep is unavailable: where work is
  recorded, how it is reconciled, and who re-enters it after recovery.
- Keep a controlled production smoke account/inbox separate from pilot/customer accounts.

## Non-goals

- Paid observability, uptime, incident-management, or persistent staging services before measured
  revenue/pilot need.
- A database exception table or an app-built monitoring dashboard.
- Claims of five-nines availability, automatic disaster recovery, or zero data loss without proof.
