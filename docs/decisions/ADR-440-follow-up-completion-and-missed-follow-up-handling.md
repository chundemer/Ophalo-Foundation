# ADR-440 — Follow Up Completion And Missed Follow-Up Handling

**Status:** Locked  
**Date:** 2026-07-13  
**Related:** ADR-337, ADR-359, ADR-439, build-log/083

## Decision

Completing Follow Up On requires a lightweight completion reason, not mandatory free text.

Follow Up On can be:

- completed;
- moved to a new date;
- left active after recorded activity.

Missed follow-ups are recorded for metrics and remain visible as overdue active promise work until
staff completes or moves them. They are never hidden or expired away.

## Completion Rules

- Silent clear is not the default completion path.
- The user must choose a completion reason.
- Free-text note is optional.
- Customer update content is the audit record when the completion path sends a customer update.
- External contact details are the audit record when the completion path logs contact.
- Internal/no-longer-needed completion creates an internal completion event.
- Moving follow-up sets a new Follow Up On date/reason and records that the promise moved.
- Recording activity and keeping follow-up leaves the promise active.

V1 outcomes:

| Outcome | Meaning |
|---|---|
| Complete follow-up | The loop is handled; clear Follow Up On. |
| Move follow-up | The loop is not handled; set a new date/reason. |
| Record activity and keep follow-up | Something happened, but the loop still needs attention. |

## Backend Command

Follow-up completion should be a narrow backend command so audit activity and Follow Up On state
changes commit together.

Minimum command behavior:

- Requires `X-Keep-Request-Version`.
- Applies existing account/role/row/action authorization.
- Returns updated `KeepRequestDetailResult`.
- Commits the audit/completion event and timing state in one transaction.
- Fails without partial activity/clear state when validation, authorization, or concurrency fails.
- Does not bundle Planned For completion semantics.

Candidate route shape:

```text
POST /keep/requests/{requestId}/follow-up-resolution
```

Candidate outcomes:

```text
complete
move
keep_active
```

## Missed Follow-Up Rules

Use `Overdue follow-up` / `Follow-up overdue` in product UI. Avoid `expired`; expired implies the
work can be discarded.

Metrics should distinguish:

| Metric | Meaning |
|---|---|
| Follow-ups due | Count of follow-ups whose date arrived. |
| Follow-ups completed on time | Completed on or before due date. |
| Follow-ups completed late | Completed after due date. |
| Currently overdue follow-ups | Still open after due date. |
| Average overdue age | How long promises sit overdue. |
| Follow-ups moved | Legitimate reschedules. |
| Follow-ups moved after due | Recovery/defer after a missed promise. |

## Rationale

Keep should help small businesses recover missed promises, not hide them or shame users with
heavyweight process. A required completion reason preserves the promise trail without forcing typing
for every follow-up. A backend command prevents half-states where an audit record is written but the
follow-up remains due, or a follow-up is cleared without audit context.

## Consequences

- The old raw "clear follow-up" affordance should become a lower-level timing edit or be replaced by
  the completion workflow in staff-facing UI.
- Due/overdue follow-ups remain actionable until completed or moved.
- Reporting can later measure broken/recovered promises from durable follow-up completion and missed
  follow-up state.
