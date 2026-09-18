# ADR-505 — Configurable business-hours attention policy

**Status:** Locked
**Date:** 2026-09-17
**Related:** ADR-124, ADR-374, ADR-451, ADR-489, ADR-490; GAP-100, DEF-025, DEF-037

## Context

Keep currently records account response targets and absolute UTC deadlines, but has no business
calendar or closure model. Its deadline writers use elapsed time and list queries compare stored
timestamps directly. A service business cannot express a short staffed-hours response promise
without creating false overnight/weekend overdue signals.

GAP-100 makes response timing configurable to the business's real operating model. It is not an
opinionated universal SLA, a notification quiet-hours feature, an on-call system, or a live
calendar-query engine.

## Decision

### Account response policy

New accounts work immediately with no schedule setup. The canonical unsaved-policy defaults are:

| Target | Default | Timing basis |
| --- | ---: | --- |
| First response | 60 minutes | Continuous |
| Standard reply | 240 minutes | Continuous |
| Priority reply | 60 minutes | Continuous |

Every account may later configure each target independently as either:

- **Continuous** — elapsed UTC time always consumes the target; or
- **Staffed hours** — only minutes in the account's shared open calendar consume the target.

There is one shared account calendar for every staffed-hours target. There are no target-specific
calendars. `Priority` is an existing response tier, not an emergency/on-call classification; a
business may deliberately select continuous timing for that tier without Keep claiming emergency
coverage, routing, or notification delivery.

### V1 calendar boundary

The existing account IANA timezone is the sole timezone for its calendar. V1 supports:

- zero or one same-day open interval per weekday; a missing interval means closed; and
- full-day local-date closures for holidays and ad-hoc shutdowns.

The interval is half-open: `[opensAt, closesAt)`. `opensAt` is included and `closesAt` is excluded.
An interval must begin before it ends on the same local date.

Closures are persisted and evaluated as account-local `DateOnly` values, never as UTC-midnight
instants. The clock converts an obligation timestamp to the account's IANA local date before it
tests whether that date is closed.

V1 explicitly excludes split shifts, multiple intervals per day, overnight/cross-midnight or
24-hour windows, partial-day closures, recurring/floating holiday rules, target-specific schedules,
on-call rotations, escalation/routing rules, and public "open now" behavior.

### Staffed-hours clock

For a staffed-hours target, the server consumes real elapsed minutes only while the account calendar
is open. A customer obligation received while closed begins consuming at the next opening. When an
obligation crosses close, it preserves its remaining staffed minutes and resumes at the next
opening. Weekends and full-day closures are both closed time and accrue zero minutes.

The calendar is evaluated using IANA timezone rules and real elapsed time:

- skipped spring-forward local time contributes zero minutes; and
- both occurrences of repeated fall-back local time contribute.

The calculator must resolve invalid/ambiguous local boundary times deterministically and must be
bounded: invalid or pathological configuration must produce a controlled configuration failure,
never an unbounded forward search or CPU starvation.

For V1, a staffed-hours calculation may advance no more than five local calendar years from its
obligation timestamp. A Settings mutation that selects staffed timing preflights every staffed
target against the proposed calendar using this same bound. If the target cannot be fulfilled,
the server returns a controlled business-calendar configuration failure; it never falls back to
continuous timing or invents a deadline. The write-time calculator retains the same guard for
legacy, concurrent, or subsequently pathological data.

A target may use staffed-hours timing only when the account has at least one valid weekly open
interval. Conversely, a calendar mutation may not remove the final weekly interval while any target
uses staffed-hours timing. Both directions are server-enforced and transactionally safe.

Changing the account IANA timezone preserves saved local wall-clock hours for future calculations:
08:00–17:00 remains 08:00–17:00 in the new timezone. It does not rewrite existing UTC deadlines.
All account-timezone entry points must use one reusable IANA-timezone validator outside the API
layer, so signup and Settings enforce the same rule.

### Stored deadline and attention semantics

The server calculates one absolute UTC deadline when a response obligation is created and stores it
in the existing deadline field (`FirstResponseDueAtUtc` for public intake and `NextAttentionAtUtc`
for response attention). Existing indexed SQL filtering/sorting and direct UTC `now` comparisons
remain authoritative. Clients render server-authored deadlines and states; they do not calculate
calendar, closure, or overdue state.

Policy, schedule, closure, target, timing-basis, and timezone changes apply only to obligations
created or re-raised after the change. They never rewrite existing stamped deadlines. Manual
per-request or bulk deadline recalculation is deferred to V2.

A customer message creates `Waiting` immediately, whether or not the business is open. Staffed-hours
timing changes only its deadline and overdue outcome. Keep adds no `Paused` attention level, hidden
after-hours queue state, or client-calculated queue rule.

Same-priority follow-up customer messages preserve the active deadline. A Standard-to-Priority
message calculates its priority deadline from the newer message but stores the earlier of that value
and the existing deadline; escalation can never make an active commitment less urgent. A customer
message after the business was waiting on the customer is a new response obligation and gets a new
deadline.

V1 persistence is relational: one response-policy row per account; at most seven weekly interval
rows, unique by account and weekday; closure rows unique by account and local date; and append-only
account-scoped settings audit rows. The account's existing IANA timezone remains authoritative and
is not duplicated in calendar storage.

### Scope and authority

Timing basis applies consistently to every current response-deadline writer that uses its target:

- First Response: public-intake `FirstResponseDueAtUtc`;
- Standard Reply: standard customer-message and inbound-contact response obligations; and
- Priority Reply: priority customer-message and feedback response obligations.

It does not alter `Follow Up On`, ADR-451 voicemail promises, planned-for timing, or DEF-037
status-check logic. `Follow Up On` remains an explicit operator workflow/customer-promise tool,
not an SLA override.

Until ADR-451 is revised, its voicemail follow-up rule retains the existing Mon–Fri local-midnight
calculation and may land on a configured closed date. GAP-100 does not make voicemail promises
calendar-aware.

Calendar, closure, target, and timing-basis changes use the existing `keep.settings.manage`
authorization boundary and create durable account-scoped settings audit events. They are internal
settings and are not exposed on public customer pages.

Settings must state that changes apply to new or re-raised obligations and that existing deadlines
remain unchanged. Request Detail may state that a response deadline was set when its customer
obligation was created. V1 does not claim detailed historical schedule/policy provenance without
persisted provenance data.

## Consequences

- Keep becomes a configurable response-policy engine: the business chooses its target durations,
  their timing bases, and—when needed—its one staffed-hours calendar.
- Continuous timing remains the safe no-setup default; there is no forced onboarding calendar.
- `NextAttentionAtUtc` remains SQL-filterable, sortable, deterministic, and audit-stable.
- Server tests must cover opening/closing boundaries, closed arrivals, cross-close carry-over,
  weekend/closure skips, repeat-message escalation, policy snapshot behavior, timezone changes,
  both DST transitions, the five-year configuration bound, both priority-escalation `min` outcomes,
  and identical state for viewers in different device timezones.

## Deferred

- DEF-037's calendar-versus-business-date status-check policy.
- Notification quiet-hours, push/SMS delivery timing, and automated customer notices (DEF-012).
- Calendar-aware ADR-451 voicemail promises; the existing Mon–Fri local-midnight rule can conflict
  with a GAP-100 staffed-hours calendar or closure.
- Per-request SLA overrides/recalculations, bulk recalculation, and policy/schedule provenance
  snapshots.
- Split/overnight/24-hour schedules, partial-day or recurring closures, multi-location calendars,
  on-call/routing/escalation, and public availability display.
- API composition, migration sequencing, Settings UX design, and the implementation build plan;
  these follow in a separate solution-planning discussion.
