# ADR-506 — Keep settings contract and audit content

**Status:** Locked
**Date:** 2026-09-20
**Related:** ADR-501, ADR-505, GAP-100 batches 4–6

> **Amended by [ADR-507](ADR-507-closure-labels-and-object-calendar-contract.md):** calendar
> closures are object entries with an optional internal reason, and reason changes participate in
> the opaque settings version and closure audit diff.

## Context

GAP-100 batch 4 supplies the governed application operations and append-only settings-audit
storage, but intentionally defers the HTTP/UI contract. Before that surface is built, its request
shape and the audit event granularity/content need one stable interpretation. In particular, a
calendar edit must be validated against the complete proposed schedule, and audit records must be
human-readable without introducing a JSON-diff schema.

## Decision

### 1. Keep the existing setup route family

Settings remain under the established authenticated `/keep/setup/*` family (ADR-501); no parallel
`/keep/settings/*` tree is introduced.

- `GET /keep/setup` is extended additively with the response timing bases, one account calendar
  (seven-or-fewer weekly intervals plus full-day local closure dates), and an opaque
  `settingsVersion`. It remains the canonical settings read.
- `PUT /keep/setup/policy` becomes the governed policy write. Its body supplies the complete
  response-policy snapshot: all three target durations, `statusCheckThresholdDays`, and all three
  timing bases. It delegates to `KeepResponsePolicyService.UpdatePolicyTargetsAsync`; the older
  ungoverned setup-policy write is not retained as a parallel path.
- `PUT /keep/setup/calendar` supplies the complete desired calendar snapshot: weekly intervals and
  closure dates. The server diffs it against the persisted calendar, invokes the governed calendar
  operation, and is the sole authority for add/update/remove audit events. Successful writes return
  the refreshed canonical setup snapshot, including its next `settingsVersion`.
- Timezone stays in `PUT /keep/setup/profile`, alongside the account profile fields it already
  owns. That write composes the governed timezone staging path introduced in batch 4c; a separate
  timezone endpoint would duplicate an existing profile contract without adding a user outcome.

All writes continue to use `keep.settings.manage` and return the existing authorization/business
errors through the standard HTTP error mapper. The request DTOs, not the persistence interface,
are the HTTP contract.

### 2. One opaque version protects the coupled settings snapshot

Policy, calendar, and timezone-affecting profile saves must echo the exact `settingsVersion` read
from `GET /keep/setup`. It represents the coupled settings state: account timezone, response-policy
durations and bases, weekly intervals, and closures. It may be a canonical server-computed hash of
that state; it is opaque to the client and requires no new persisted revision column.

Inside the existing serializable transaction, the server recomputes the current version before
writing. A missing or mismatched version returns a stable `409 Conflict` settings-refresh error and
writes nothing. The existing serialization/unique-violation conflict also remains a retryable
`409`. Syntax/binding errors remain `400`; a syntactically valid but impossible staffed-hours
configuration is a controlled `422 Unprocessable Content` business error.

The Settings UI must explain that another user changed settings, preserve unsaved form values where
practical, and provide an explicit **Refresh settings** action. It must never silently retry a
stale full-snapshot write.

### 3. Full snapshots at the HTTP boundary

Policy and calendar writes express the caller's complete desired state, not a series of local
patches. This makes the server's cross-aggregate validation deterministic, lets it calculate audit
diffs from one authoritative before/after pair, and avoids client-side ordering hazards (for
example, selecting staffed timing before adding an interval). The service/persistence layer may
internally derive closure add/remove deltas; that is not exposed to clients.

Timing-basis fields are additive during rollout. When a compatibility caller supplies a policy
write without one or more timing bases, the server preserves the persisted basis for each omitted
field; omission never means `Continuous`. New Settings clients always send the complete policy
snapshot and required `settingsVersion`.

Weekly interval rows use one IANA-independent weekday plus same-day `HH:mm` open/close local times.
Closure entries are account-local `YYYY-MM-DD` dates. Labels, partial-day closures, split shifts,
overnight hours, and recurring closures remain outside ADR-505 V1.

### 4. Audit-event granularity and content

`KeepSettingsAuditEvent.Content` remains bounded free text (`varchar(4000)`), never JSON. It is a
concise deterministic summary using stable field labels and `before -> after` values; it is not a
UI sentence, localization surface, or a schema for replaying settings history.

One successful save produces:

- at most one `ResponseTargetDurationChanged` event for all changed target-duration and
  status-check fields;
- at most one `ResponseTimingBasisChanged` event for all changed target bases;
- one `WeeklyIntervalChanged` event per changed weekday;
- one `ClosureChanged` event per changed local date; and
- one `TimeZoneChanged` event when the timezone changes.

New policy creation records the supplied values as `unset -> value`; a no-op produces no audit
event. Calendar entries use `closed -> HH:mm-HH:mm`, `HH:mm-HH:mm -> closed`, or the corresponding
before/after interval; closures use `absent -> closed` and `closed -> absent`; timezone uses
`oldIanaId -> newIanaId`. The actor and occurrence timestamp remain on the event columns, not in
`Content`.

## Consequences

- Batch 5 can implement a compact, additive settings backend without reopening batch 4's
  concurrency or validation rules.
- Batch 6 renders the returned canonical setup snapshot, handles explicit refresh conflicts, and
  never computes schedule validity or audit descriptions client-side.
- Existing preliminary audit strings from batch 4b are replaced as part of batch 5 so future audit
  rows follow this locked format; no migration or rewrite of prior rows is required.

## Out of scope

Settings-audit read/history UI, JSON snapshots, localization of audit content, profile-field audit
events other than timezone, per-request policy provenance, and all business-clock/writer work.
