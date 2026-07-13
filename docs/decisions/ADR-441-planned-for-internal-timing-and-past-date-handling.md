# ADR-441 — Planned For Internal Timing And Past-Date Handling

**Status:** Locked  
**Date:** 2026-07-13  
**Related:** ADR-338, ADR-339, ADR-434, ADR-439, build-log/083

## Decision

`Planned For` is internal timing context only in V1. It can be set, changed, or removed, but it does
not have a completion workflow.

Past Planned For remains internal timing context, not follow-up attention. It stays visible as
`Planned date passed` and prompts staff to:

- mark work done;
- move the planned date;
- remove the planned date.

Active requests with past Planned For may feed needs-status-check/review until resolved.

## Behavior

| State | Behavior |
|---|---|
| Future Planned For | Quiet chip such as `Planned Fri`. |
| Planned today | Visible timing signal, but not customer-promise attention. |
| Past Planned For | Show `Planned date passed`. |
| Work completed | Use existing `Mark work done`. |
| Work moved | Move Planned For to a new date. |
| Plan no longer relevant | Remove Planned For. |
| Still active/no clear resolution | Feed needs-status-check/review. |

## Rationale

Planned For means the business intends to do or resume work around a date. It is closer to internal
work timing than customer communication promise. Adding a separate completion workflow would pull
Keep toward task management, dispatch, and scheduling. Existing Work completed, planned-date edit,
customer update, external contact, and Follow Up On flows already cover the real follow-up actions.

## Consequences

- Product UI should avoid "missed promise" language for past Planned For.
- Past Planned For should not override stronger attention reasons.
- Planned For can inform status-check review and list scan context without becoming push-worthy or
  customer-visible by default.
