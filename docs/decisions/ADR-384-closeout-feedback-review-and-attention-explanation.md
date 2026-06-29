# ADR-384 — Closeout, Feedback Review, And Attention Explanation

**Date:** 2026-06-28  
**Status:** Locked  
**Source:** Session 13 S13h closeout/feedback-review discussion; ADR-343; ADR-380; build-log 067

## Context

Session 13 needs to make the end of a request understandable: staff should know when work is ready to
close, who can close it, how negative customer feedback is handled, and which attention signals require
human action.

The backend already exposes:

- `GET /keep/requests?view=ready_to_close`;
- `GET /keep/requests?view=feedback_review`;
- `PATCH /keep/requests/{requestId}/status`;
- `POST /keep/requests/{requestId}/feedback-review`;
- detail action metadata including `canClose`, `canMarkFeedbackReviewed`, `allowedStatuses`, and
  validation hints.

S13h is therefore a frontend/workflow composition decision, not a new backend workflow surface.

## Decision

S13h extends the existing list and detail surfaces. It does not add a new top-level route or standalone
closeout screen.

List touch points:

- Owner/Admin `Ready to Close` view from `view=ready_to_close`;
- Owner/Admin `Feedback Review` view from `view=feedback_review`.

Detail touch points:

- close action through the existing status mutation;
- feedback review panel and mark-reviewed action.

Do not add separate primary nav items for closeout or feedback review. Counts/badges on the main
request-list nav item are enough when actionable rows exist.

Closing is available only when:

- `availableActions.canClose == true`; and
- `availableActions.allowedStatuses` includes `closed`.

The close action uses:

`PATCH /keep/requests/{requestId}/status`

with `status: "closed"` and `X-Keep-Request-Version: {detail.version}`.

Feedback review is available only when:

- `availableActions.canMarkFeedbackReviewed == true`; and
- detail feedback fields indicate submitted negative feedback that still needs review.

Feedback review UI language:

- headline: `Customer left negative feedback`;
- action: `Mark reviewed`;
- note label: `Internal note (optional)`;
- explanatory sentence:
  `This feedback was submitted after the request was closed. Reviewing it does not reopen the request.`

The review note is optional. Use `validation.feedbackReviewNoteMaxLength` for local length validation.

`feedbackReviewAgeBucket` maps to visible badges:

- `new` -> `New`;
- `aging` -> `Aging`;
- `overdue` -> `Overdue`.

Avoid user-facing labels such as `unresolved feedback`.

Use `Ready to close` as the list/view label and row/detail phrase.

Page-view confidence and post-resolution customer activity are separate signals. Use
`customerPageViewedAfterLatestUpdate` only for copy such as:

`Customer viewed tracker after your last update.`

Do not use that field to claim customer activity after resolution. If post-resolution activity cannot
be supported by the available fields/context, show nothing.

Operators may move work to `resolved` when `allowedStatuses` permits it, but closeout and feedback
review remain Owner/Admin workflows through server-provided list authorization and action flags.

Do not add dedicated next/previous review navigation in S13h. Preserve existing ready-to-close
navigation context where `navView=ready_to_close` is already supported; dedicated feedback-review
queue navigation remains deferred.

## Rationale

Closeout and feedback review are accountability extensions of the existing workbench. Creating
separate destinations would fragment navigation for workflows that are naturally reached from list
filters and detail context.

Rendering from server-provided action flags keeps frontend behavior aligned with backend policy:
Operators can resolve, Owner/Admin can close when eligible, and feedback review remains restricted to
the roles and states the server permits.

The language avoids internal taxonomy. Customers leave negative feedback; staff review it. The request
does not reopen just because feedback needs review.

Separating page views from post-resolution activity avoids implying more certainty than the data
supports.

## Consequences

- S13h frontend work should extend request-list view options and request-detail panels/actions.
- No new backend endpoint is required.
- Feedback-review note handling must preserve optionality.
- The UI must adopt returned `KeepRequestDetailResult` after close/review mutations.
- Dedicated closeout/feedback-review routes and next-review navigation remain deferred.

## Deferred

- Dedicated top-level closeout/feedback-review routes.
- Dedicated next-feedback-review navigation.
- Batch closeout.
- Analytics dashboards.
- Public review generation.
- Customer-visible feedback replies.
