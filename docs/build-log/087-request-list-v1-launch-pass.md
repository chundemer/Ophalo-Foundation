# Build Log 087 — Request List V1 Launch Pass

**Prepared:** 2026-07-15  
**Status:** Decisions locked — implementation pending  
**Scope:** Authenticated PWA Request List only

---

## Purpose

The initial PWA build is complete. This launch-pass slice makes the Request List a calm, decisive
work queue for busy business owners. It reduces alert fatigue, aligns displayed urgency with queue
membership and counts, makes the next useful action clear when the workflow defines one, and
improves readability without changing request lifecycle or permission policy.

## Locked Product Decisions

### 1. Default Queue contains active work; Feedback Review owns closed feedback work

- A Closed request with unresolved negative feedback appears in **Feedback Review**, not the
  Default Queue.
- The Default Queue remains an active-work queue. Its summary and empty-state copy must not imply
  that Closed work is ordinary active work.
- Feedback Review remains Owner/Admin-only and continues to use existing server policy and
  visibility rules.
- Closed rows suppress stale first-response, follow-up, and planned-date operational signals.
  The active feedback-review signal and permitted review/contact actions remain visible where
  appropriate.

### 2. Needs Attention means displayed operational attention

- The Needs Attention view and count include:
  - persisted non-none attention;
  - due or overdue Follow-up On promises; and
  - customer-origin requests whose first response is overdue.
- First-response SLA attention is only applicable where the domain defines it: a customer-origin
  request with no recorded first response and a due time at or before the current time.
- Business-created requests do not acquire a first-response SLA merely because they are new.
- A row must not display a red operational-overdue signal while being excluded from the Needs
  Attention view/count for that same signal.

### 3. Each card has one status, one exception, and clear context

- A row renders at most one ordinary status pill and one highest-priority exception pill.
- The UI uses server ranking/severity and defined workflow state to choose the exception; it does
  not render every available timing, attention, feedback, priority, and share-state signal at once.
- Other useful timing and source information becomes quiet metadata rather than additional alert
  badges.
- The row shows one clamped customer-request description preview by default. This gives enough
  triage context without requiring an owner to open every row.
- The customer name remains a readable serif anchor and preserves entered spelling/capitalization.
  Do not auto-title-case customer data.

### 4. A primary action is shown only when the workflow is unambiguous

The list may promote one permitted row action using this order. Server-provided permissions remain
authoritative; a presentation rule never invents an unavailable action.

1. Closed unresolved negative feedback → **Review feedback**.
2. Complaint, cancellation, schedule-change, or timing-change attention → **Review request**.
3. Explicit call request → **Log contact**.
4. Customer page not shared, with no higher-priority workflow state → **Share Link**.
5. Customer message or update request → **Update customer**.
6. Customer-origin first response pending/overdue → **Update customer**; if the customer’s stated
   contact preference is phone call, promote **Log contact** instead.
7. Follow-up/planned-date exception without a clear customer-facing instruction → **Review request**.
8. Ready-to-close request → **Review closeout** or **Close request**, only when existing server
   policy permits it.
9. Pending Customer and routine active work → no forced next-action cue.

- When no one action is justified, omit the `Next:` cue rather than presenting a vague choice such
  as “Log contact or Update customer.”
- The secondary action may be a relevant permitted customer update or contact action.
- **Internal Note** is not a repeated list-row action; it belongs in the locked Request Detail
  composer.

### 5. List interactions are fast, readable, and not redundant

- Each row exposes at most two quick actions: one promoted action and one relevant secondary action.
- Quick actions use visible labels and at least 14px text with a 40px minimum target height.
- The row itself remains the route to full detail. Remove the redundant bottom **Open detail** action;
  retain the clear row click target and chevron.
- Search filters after a short debounce. Do not require an invisible Enter-only submission behavior.
- Default Queue stays automatically sorted by defined urgency. Show quiet explanatory copy such as
  **Sorted by what needs attention first**; do not add a manual sort control for V1.

### 6. Readability and information density are launch requirements

- Customer name: 17px serif, semibold.
- Primary status, exception labels, and quick actions: at least 14px.
- Metadata and next-action copy: 13–14px with comfortable line-height.
- Request reference: 12px monospace.
- Tab labels and filters: 14–15px.
- No instructional or operational text is below 12px.
- Badge reduction and removal of the third repeated action create the room for this readable scale;
  do not preserve the current small type merely to maximize rows per viewport.
- Verify list behavior at 200% browser zoom and with long customer names, locations, descriptions,
  and source/contact-preference combinations.

### 7. Color and icons reinforce words; they do not require a legend

- Do not add a permanent color-key legend to the header.
- Use the same semantic colors in Request List and Request Detail:
  - red: urgent, overdue, customer-risk, or unresolved critical work;
  - amber: timing risk, attention soon, or customer page not yet shared;
  - teal: customer-facing communication or customer-page action;
  - navy/neutral: ordinary status and internal operations;
  - green: completed or confirmed state.
- Every color signal carries plain-language text. Urgency signals also carry an appropriate icon.
- Use icons where they improve scanning: alert triangle for urgent exception, clock for timing,
  message for customer update/feedback, phone for contact, share for Share Link, and check for
  confirmed completion.
- Do not replace high-consequence text with icons, add icons to every status/metadata item, or use
  icon-only primary actions.
- A future **How priorities work** help surface may explain the signals; it is not a permanent
  swatch legend.

### 8. Responsive, loading, error, and keyboard behavior are first-class

- On mobile, the promoted action remains first and the secondary action wraps cleanly without
  conflicting with the row’s detail click target.
- List loading uses a stable row skeleton rather than a lone `Loading…` label.
- List failures provide a Retry action in addition to safe explanatory copy.
- Existing tab, search, and row focus states remain visible and keyboard-operable.
- Do not add global single-letter action shortcuts. Existing keyboard guidance remains contextual;
  `?` help must not trigger while focus is inside a text field.

## Recommended Row Structure

```text
Reference  Customer name                                                    >

[ Status ]  [ Highest-priority exception ]  Next: Clear action when known

One-line customer request description
Assignment · Last touch · Source/location · quiet timing context

[ Primary action ]  [ Secondary action ]
```

## Hard Boundaries

- No new request status, lifecycle transition, permission grant, or client-side authorization
  inference.
- Use existing server-supplied action metadata; never display an action the current actor cannot
  perform.
- No automatic customer update, contact log, feedback review, or status change from queue ranking.
- No automatic name casing changes.
- No permanent header legend, icon-only primary action, or full manual-sorting system in V1.
- Do not regress public/customer visibility boundaries while changing list presentation.

## Implementation Preflight

Before implementation, confirm and preserve:

- the current action-policy matrix for Owner, Admin, Operator, Viewer, and OffSeason states;
- customer-origin first-response fields versus business-created request behavior;
- terminal/Closed suppression for first-response state and timing presentation;
- Default, Needs Attention, Feedback Review, and Ready-to-Close query/count composition;
- row-click/button event behavior, focus order, and keyboard handling;
- list refresh, cursor pagination, search, and filter behavior.

The screenshot state showing a Closed row with **Response overdue** must be reproduced against the
current API/build during preflight. Current list-service logic excludes terminal requests from
first-response-overdue calculation; do not claim or patch a backend defect without that evidence.

## Acceptance Criteria

- Closed unresolved feedback no longer appears in Default Queue and remains actionable in Feedback
  Review for permitted roles.
- Needs Attention count/view and every displayed red operational-overdue row agree on membership.
- Business-created rows never display a customer first-response SLA.
- Closed rows do not display stale response-SLA, follow-up, or planned-date alarms.
- Each row shows no more than one status pill and one exception pill, plus quiet context.
- Each row shows a one-line request description preview without breaking long-data layouts.
- A row promotes only the defined, permitted primary action; ambiguous states have no forced cue.
- Repeated Add Note and Open Detail actions are removed from the row action bar.
- Search filters without requiring Enter, and action targets are readable and touch-safe.
- Color and icon treatment is consistent, labeled, and passes the contrast audit.
- Mobile, skeleton, Retry, keyboard, screen-reader-label, long-data, and 200% zoom checks pass.

## Verification Plan

- Add focused list service/API tests for first-response-overdue Needs Attention membership/count,
  Closed default-queue exclusion, closed timing suppression, business-origin SLA exclusion, and
  action-matrix inputs.
- Add PWA tests for badge prioritization, description preview, row-action cap/order, ambiguous
  next-action omission, debounced search, Retry, and keyboard/focus behavior.
- Test Owner/Admin, Operator, Viewer, and OffSeason rendering with each actionable state.
- Test desktop at 1366px and mobile at 320px/768px, plus 200% zoom.
- Run automated accessibility checks and manual contrast, keyboard, and touch-target review.
- Run `ophalo-app` TypeScript/build checks and relevant request-list/backend tests before marking
  the slice complete.

## Deferred Until a Later Page Pass

- A dismissible queue-level first-use explanation.
- A compact `How priorities work` help surface.
- Broader PWA and marketing-site typography/token consolidation outside Request List scope.
