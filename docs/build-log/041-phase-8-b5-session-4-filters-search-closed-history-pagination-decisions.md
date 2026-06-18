# Build Log 041 — Phase 8-B5 Session 4 Filters, Search, Closed History, Pagination Decisions

**Phase:** 8-B5 Session 4 discovery / pre-implementation gate
**Date:** 2026-06-18
**Status:** Decisions locked. Ready to split into bounded Claude coding sessions.
**Build log preceding this:** `040-phase-8-b5-session-3-assignment-watch-mute-decisions.md`
**ADRs locked:** 237..260
**Next free ADR:** ADR-261

---

## Purpose

Session 4 turns the trusted B5 request list into a navigable backend surface. Operators and
Owner/Admin users need named work views, search, closed/cancelled history browsing, pagination,
counts, and visual context metadata without changing the default command-center behavior or adding
new terminal-state workflows.

The key boundary is:

```text
Session 4 is backend/foundation list navigation.
It does not implement web/PWA or native mobile UI.
It does not add archive, mark-feedback-reviewed, close-and-next, or realtime list refresh.
```

---

## Locked Decisions

### ADR-237 — Session 4 extends `GET /keep/requests`

Session 4 extends the existing request-list endpoint with optional query parameters instead of
adding parallel list endpoints.

Examples:

```text
GET /keep/requests
GET /keep/requests?view=closed_history
GET /keep/requests?view=feedback_review
GET /keep/requests?q=gate
GET /keep/requests?status=resolved
```

Rules:

- the same endpoint owns default list, named views, filters, search, and pagination;
- the same authorization and row-shape rules apply across modes;
- no-query `GET /keep/requests` remains the default command-center list.

Reason: one list resource avoids contract drift between default, filtered, search, and history APIs.

### ADR-238 — No-query list remains the B5 command center

Plain `GET /keep/requests` continues to return the B5 default command-center list.

Default behavior remains:

```text
active operational requests
deterministic attention-first ranking
closed unresolved feedback for Owner/Admin only
no normal closed history
no cancelled history
no search/filter mode unless query params are supplied
```

Reason: Session 4 adds navigation without changing the first-screen operational surface.

### ADR-239 — Named views first, filters second

Session 4 uses named views for product-meaningful queues, with limited filters layered on top.

Initial views:

```text
default
assigned_to_me
watching
unassigned
needs_attention
feedback_review
closed_history
cancelled_history
all_history
```

Initial filters:

```text
status
attentionReason
assignedAccountUserId
q
createdFrom / createdTo
closedFrom / closedTo
```

Reason: operators and owners think in work views such as "my work", "needs attention", "available",
and "feedback review", not raw database predicates.

### ADR-240 — Operators get an explicit Unassigned/Available view

Session 4 adds an explicit unassigned/available view.

Rules:

- Owner/Admin can see unassigned active requests in default and unassigned views;
- Operators do not see unassigned requests in their default list;
- Operators may use `view=unassigned`;
- Operator unassigned view returns only requests they are eligible to self-assign;
- Closed, Cancelled, and closed unresolved-feedback rows are excluded from Operator unassigned view;
- viewing the queue does not assign work.

Session 4 should also re-enable the narrow Operator self-assign path from ADR-223 for requests
returned through this allowed surface. Session 3B blocked Operator self-assign until this surface
existed; Session 4 supplies that missing visibility boundary.

Reason: keep the Operator default list calm while allowing intentional work claiming.

### ADR-241 — Operational view counts are lightweight and role-aware

Session 4 list responses include lightweight counts for operational named views.

Initial counts:

```text
default
assigned_to_me
watching
unassigned
needs_attention
feedback_review
```

Rules:

- counts mean "visible/actionable for the current user", not global account totals;
- broad history views do not require exact counts in Session 4;
- counts are request-time snapshots;
- counts are not realtime subscriptions.

Reason: counts prevent pointless taps and help dispatch without turning Session 4 into reporting.

### ADR-242 — Closed unresolved feedback appears in relevant views with context

Closed unresolved feedback appears in multiple relevant Owner/Admin contexts:

```text
default:
post-close follow-up

feedback_review:
focused review queue

closed_history:
closed retained record
```

Operators do not see closed unresolved feedback in default, feedback_review, or closed_history in
Session 4.

Reason: the same record has urgency in default, focus in feedback review, and retention in history,
but it must never look reopened.

### ADR-243 — Rows expose stable context/type metadata

Rows expose stable backend context metadata so clients can visually distinguish request types.

Initial `rowContext` values:

```text
active_work
needs_attention
first_response
waiting_on_customer
feedback_review
closed_history
cancelled_history
unassigned_available
```

Rules:

- do not include `ready_to_close` in Session 4 row contexts;
- ready-to-close belongs to the later closeout workflow;
- closed unresolved feedback carries:

```text
rowContext = feedback_review
isPostCloseFollowUp = true
needsFeedbackReview = true
status = closed
```

Reason: `Closed` alone is ambiguous. A closed unresolved-feedback row is urgent post-close follow-up;
a normal closed row is history.

### ADR-244 — Mark feedback reviewed remains deferred

Session 4 adds the `feedback_review` view/queue but does not implement `Mark feedback reviewed`.

Deferred policy questions:

```text
who can mark feedback reviewed
whether it clears unresolved-feedback attention
whether it hides the row from default and feedback_review
whether an internal note is required
whether undo/reopen-review exists
whether it creates an internal timeline event
whether it affects closed history
```

Reason: review completion is a write workflow with state-clearing semantics; Session 4 is read/list
navigation.

### ADR-245 — Archive remains deferred

Session 4 supports history browsing but not archive/unarchive.

Included:

```text
closed_history
cancelled_history
all_history
```

Excluded:

```text
archive
unarchive
auto-archive
include-archived
hide-from-history
archive notes/reasons
```

Reason: archive introduces retained-but-hidden product state and should be decided in a closeout /
history workflow slice.

### ADR-246 — Cancelled requests are history only

Cancelled requests appear in history views but never in the default command-center list.

Behavior:

```text
default:
exclude Cancelled

cancelled_history:
Cancelled only

closed_history:
Closed only

all_history:
Closed + Cancelled
```

Reason: cancelled requests are retained business records, not active operational work.

### ADR-247 — Search starts with visible request/customer fields

Session 4 search includes practical visible fields:

```text
request reference code
customer display name
customer phone
customer email
request title/summary/description if present
visible list preview text if already available/denormalized
feedback comment for Owner/Admin only
```

Session 4 search excludes:

```text
full internal timeline history
internal notes
participation notes
external-contact summaries
private staff/user fields
customer-page tokens
raw internal IDs
```

Reason: first search should find normal customer/request records without expensive event-history
search or internal visibility risk.

### ADR-248 — Broad terminal history is Owner/Admin-only in Session 4

Owner/Admin may use:

```text
closed_history
cancelled_history
all_history
search within history views
```

Operators may search operationally visible active/work views but cannot browse broad
closed/cancelled history in Session 4.

Viewers receive no broad history expansion in Session 4.

Reason: terminal history can include feedback, cancellation context, and management-sensitive
resolution detail.

### ADR-249 — Cursor pagination with client-selected limits

Session 4 uses cursor pagination, not offset pagination.

Response contract:

```json
{
  "requests": [],
  "pageInfo": {
    "limit": 50,
    "hasMore": true,
    "nextCursor": "opaque-token"
  }
}
```

Query params:

```text
limit
cursor
```

Defaults:

```text
default limit: 50
max limit: 100
```

Rules:

- the API is device-neutral;
- clients choose page size and default view based on surface, role, and workflow;
- web/PWA may use larger Owner/Admin command-center pages;
- native mobile may use smaller Operator-focused pages with counts/search/named views.

Reason: request rows move as attention, assignment, feedback, and activity change. Cursor pagination
is more stable than offset pagination.

### ADR-250 — All views/search/filters are server-side

The backend applies authorization, view filters, search, sorting, and pagination.

Backend flow:

```text
resolve current user/account/role
apply authorization and role visibility
apply named view
apply filters/search
apply deterministic sort
fetch limit + 1
return rows + pageInfo + role-aware counts
```

Clients render returned rows. Clients do not fetch all requests and filter/search locally.

Reason: server-side filtering protects visibility, improves mobile performance, keeps counts honest,
and makes pagination correct.

### ADR-251 — No exact arbitrary totals in Session 4

Session 4 does not expose exact totals for arbitrary query/search/history combinations.

Included:

```text
viewCounts for operational views
pageInfo.hasMore
pageInfo.nextCursor
pageInfo.limit
```

Excluded:

```text
totalResults
totalClosedHistory
totalCancelledHistory
exact totals for every q/status/date combination
```

Reason: exact totals can require extra expensive count queries. Businesses can request reporting-like
totals after go-live if needed.

### ADR-252 — Sorting depends on selected view

Sorting rules:

```text
default:
B5 command-center ranking

needs_attention:
B5 attention-first ranking

assigned_to_me:
B5 ranking within assigned-to-current-user subset

watching:
B5 ranking within current-user Watching subset

unassigned:
B5 ranking within unassigned/available subset

feedback_review:
oldest unresolved feedback attention first

closed_history:
closed/terminated most recent first

cancelled_history:
cancelled/terminated most recent first

all_history:
terminal most recent first
```

Reason: active work should use operational priority, history should be recent-first, and unresolved
feedback should age oldest issues to the top.

### ADR-253 — List response shape includes context, counts, and page info

Session 4 evolves the list response shape in a controlled way.

Recommended shape:

```json
{
  "requests": [],
  "pageInfo": {
    "limit": 50,
    "hasMore": false,
    "nextCursor": null
  },
  "viewCounts": {
    "default": 12,
    "assigned_to_me": 4,
    "watching": 2,
    "unassigned": 0,
    "needs_attention": 5,
    "feedback_review": 1
  },
  "listContext": {
    "view": "default",
    "isDefaultCommandCenter": true,
    "isHistory": false,
    "isSearch": false
  }
}
```

Rules:

- `viewCounts` are request-time snapshots;
- current user's UI may refresh counts after successful writes or normal refetch;
- cross-user realtime list/count invalidation is deferred.

Reason: clients need one stable response shape for default, search, history, and named views.

### ADR-254 — History rows expose minimal read/review actions only

History rows are read-oriented in Session 4.

Rules:

```text
closed_history:
open_detail only

cancelled_history:
open_detail only

all_history:
open_detail only

feedback_review:
review_feedback / open_detail focused on feedback
```

Session 4 history rows do not expose:

```text
contact_customer
post_customer_update
acknowledge_attention
mark_feedback_reviewed
archive
unarchive
close
cancel
reopen
assign/watch/mute
contextual previous-request panel
```

Reason: history browsing is lookup/inspection. Feedback review opens context, but review completion
is deferred.

### ADR-255 — Internal/team-memory search is deferred

Session 4 search does not include internal notes or internal timeline/team-memory content.

Excluded:

```text
internal notes
participation notes
external-contact summaries
full internal timeline
```

Reason: internal search needs its own role, privacy, indexing, and result-display decisions.

### ADR-256 — Session 4 is backend/foundation only

Session 4 does not implement web/PWA or native mobile UI.

Session 4 must provide future UI work with:

```text
stable query params
named views
server-side search/filtering
cursor pagination
pageInfo
role-aware viewCounts
listContext
rowContext/type metadata
role-safe actions per row
closed/cancelled history access rules
feedback_review queue
tests/docs
```

Out of scope:

```text
web/PWA screens
native mobile screens
swipe actions
visual design implementation
client-side state management
realtime count refresh
```

Reason: settle the backend contract before surface-specific UI work.

### ADR-257 — Invalid query parameters fail explicitly

Invalid request-list query parameters return `400`.

Invalid cases include:

```text
unknown view
invalid status
invalid attentionReason
invalid date format
invalid limit
invalid cursor
contradictory view/filter combinations
cursor reused with a different query shape
```

Rules:

- do not silently ignore unknown or invalid query params;
- `status` never expands visibility;
- terminal statuses require an explicit history view;
- contradictory combinations such as `view=closed_history&status=received` return `400`.

Reason: explicit failures avoid confusing client bugs and accidental visibility expansion.

### ADR-258 — Search and date validation are bounded

Search and date filters are bounded and deterministic.

Search rules:

```text
trim q
blank q behaves like no search
case-insensitive matching
phone search normalizes digits
max q length: 200 characters
```

Date rules:

```text
createdFrom / closedFrom are inclusive
createdTo / closedTo are exclusive
dates must be full ISO-8601/RFC3339 timestamps with UTC or explicit offset
closed* filters use TerminatedAtUtc for Closed and Cancelled history
```

Reason: bounded input protects performance and gives clients predictable filter semantics.

### ADR-259 — View counts are base operational counts, not query-specific totals

`viewCounts` reflect base operational named views for the current user. They are not recalculated for
the current `q`, date range, or arbitrary status filter.

Example:

```text
GET /keep/requests?view=assigned_to_me&q=smith
```

returns Smith-matching rows for `assigned_to_me`, but `viewCounts.assigned_to_me` remains the
current user's total assigned-to-me operational count.

Reason: counts stay cheap and stable while the current page still reflects the selected query.

### ADR-260 — Session 4 indexing is narrow and implementation-driven

Session 4 should use existing indexes where possible and add narrowly scoped indexes only if needed
for server-side history/search pagination.

Rules:

- do not add a full-text search engine in Session 4;
- do not add event-history search indexes in Session 4;
- migration/index additions must be justified by the implemented query shape.

Reason: first navigation should be safe and bounded. Heavier search infrastructure can follow real
usage.

---

## Claude Coding Session Split

Session 4 should be implemented in bounded slices:

```text
4A — Query contract, validation, response shape, cursor/page primitives
4B — Named views, server-side filters/search, history visibility, counts, sorting
4C — Operator unassigned surface + narrow self-assign re-enable, row context/action metadata
4D — Integration verification, docs, decision index, deferred tracker
```

No open product decision should block Session 4 implementation after this log.
