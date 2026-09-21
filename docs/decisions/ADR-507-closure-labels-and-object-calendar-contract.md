# ADR-507 — Closure labels and object calendar contract

**Status:** Locked
**Date:** 2026-09-21
**Amends:** ADR-505 §V1 calendar boundary; ADR-506 §§1, 3–4
**Related:** GAP-100 batch 6; ADR-501

## Context

A date alone tells an owner or dispatcher that the business is closed, but not why. That creates
avoidable review and deletion mistakes for holidays, training, and one-off shutdowns. Closure
context is an operator need, not a cosmetic UI enhancement.

There are no live web clients for the current dates-only calendar contract. GAP-100 may therefore
replace that contract directly instead of carrying a legacy adapter, dual-schema validation, or
transitional client diffing.

## Decision

### Object-native closures

The canonical calendar read and full-snapshot write use closure objects only:

```json
{
  "weeklyIntervals": [
    { "weekday": "Monday", "opensAt": "08:00", "closesAt": "17:00" }
  ],
  "closures": [
    { "date": "2026-11-26", "reason": "Thanksgiving Day" },
    { "date": "2026-12-25", "reason": null }
  ],
  "settingsVersion": "opaque-server-version"
}
```

`closures` replaces the dates-only closure array on `GET /keep/setup` and
`PUT /keep/setup/calendar`. The API exposes no parallel dates-only field and accepts no legacy
shape. The caller still submits the complete desired calendar snapshot.

Each closure is one account-local, full-day `date` (`YYYY-MM-DD`) plus an optional internal
`reason`. The date is unique per account and remains the only input to the staffed-hours clock.
The reason is Settings-only operational context; it is not exposed to public customers, does not
change availability claims, and does not recalculate or reorder existing stored deadlines.

### Reason validation and lifecycle

`reason` is nullable. On write, trim it; normalize an empty result to `null`; otherwise require a
single-line, control-character-free value of at most 60 characters. Ordinary punctuation and
Unicode text are allowed. The UI supports adding a date with an optional reason, editing or
clearing that reason, and removing the closure. Ranges, recurring/floating holidays, and
partial-day closures remain out of scope.

Persistence reuses the existing nullable `KeepCalendarClosure.Label` column as the bounded reason
field; no schema change or migration is required. The implementation must not invent a second
closure column or table. Existing closure rows retain their `NULL` reasons.

### Versioning, authority, and audit

Closure reasons are part of the coupled settings snapshot. Adding, removing, changing, or clearing
a reason changes the opaque, base64url `settingsVersion` and receives the same transactional
stale-save protection as every other calendar change. This protects full-snapshot writes; it does
not retroactively alter any existing deadline.

The existing `keep.settings.manage` authorization boundary remains the only write authority.

One changed date produces one `ClosureChanged` audit event. Content is deterministic and includes
only changed values:

- add without a reason: `2026-11-26: absent -> closed`;
- add with a reason: `2026-11-26: absent -> closed; reason: unset -> "Thanksgiving"`;
- change reason: `2026-11-26: reason: "Thanksgiving" -> "Annual training"`;
- clear reason: `2026-11-26: reason: "Thanksgiving" -> unset`; and
- remove: `2026-11-26: closed -> absent` with `; reason: "Thanksgiving" -> unset` only when a
  reason existed.

Reason text in audit content uses a quoted, deterministic escaped representation (escape `\\` and
`"`). A no-op, including a reason normalized to its existing value, emits no event.

## Consequences

- The Settings calendar UI can show useful business context without inventing a UI-only field.
- The API is clean from V1: one object schema, no compatibility adapter, and no future
  string-array-to-object-array contract migration.
- The existing backend and the committed batch-6b frontend need a bounded follow-up that updates
  schema, setup/calendar DTOs, version computation, full-snapshot diffing, audit output, API tests,
  API client/mocks, and closure editing UI. Mechanical preflight must split that work under the
  normal file and handler-family gates.

## Out of scope

Public display of closure reasons, staff queue decoration, recurring/range closures, localization,
and audit-history UI.
