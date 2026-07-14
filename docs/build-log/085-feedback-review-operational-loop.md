# Build Log 085 — Feedback Review Operational Loop

**Prepared:** 2026-07-14  
**Status:** Complete — commit `315b231`; verified 2026-07-14 (1,073 unit tests, 14 architecture tests, TypeScript clean)  
**Source tracker:** `docs/pilot-readiness-bug-tracker.md` GAP-015  
**Baseline:** Session 30 / Build 084 — 1,069 unit tests, 14 architecture tests, and TypeScript clean
for `ophalo-app` and `ophalo-web` (reported at Session 30 close; re-run before closeout).

---

## Purpose

Build 084 delivered the underlying feedback loop: customers submit binary feedback on a Closed
request; unresolved feedback becomes Owner/Admin review work; staff can mark it reviewed; and the
request remains Closed. This build completes the operational presentation and accountability trail.

This is an enhancement of the existing implementation, not a new feedback model. The current request
already persists `FeedbackWasResolved`, `FeedbackComment`, `FeedbackSubmittedAtUtc`,
`FeedbackReviewedAtUtc`, `FeedbackReviewedByAccountUserId`, and `FeedbackReviewNote`.

## Locked Product Behavior

1. A customer submits `Yes, this was resolved` or `No, I still need help`, with an optional comment,
   after a request is Closed.
2. Positive (`Yes`) feedback is informational. It stays visible in Utilities and has no staff
   acknowledgement action or queue entry.
3. Negative (`No`) feedback is active Owner/Admin work. It appears in Feedback Review until reviewed.
4. Opening a request from Feedback Review promotes an unreviewed negative-feedback card to the main
   column directly above Activity. This is contextual: opening the same request normally keeps the
   active card in Utilities, with a subtle `New feedback` treatment.
5. An Owner/Admin clicks **Mark as reviewed**. The action is one click; an internal review note may
   remain optional but is never required.
6. A successful review clears the active queue item/count and feedback attention, removes the active
   card from the main column and Utilities, and shows a short success confirmation.
7. The accountability trail remains in authenticated **All activity**:
   - a `feedback_received` event records the customer response, timestamp, and optional comment;
   - the existing `feedback_reviewed` event records the reviewer, timestamp, and optional internal
     note, without duplicating the customer comment.
8. No feedback action reopens the original request or exposes internal review state to the customer.

## Existing Seams (Verified)

- Domain fields and review mutation: `src/OpHalo.Keep.Core/Entities/KeepRequest.cs`.
- Owner/Admin endpoint: `POST /keep/requests/{requestId}/feedback-review` in
  `src/OpHalo.Api/Keep/KeepEndpoints.cs`.
- Review service and activity event: `MarkFeedbackReviewedService.cs` and `KeepRequestEvent.cs`.
- Customer submission service: `SubmitFeedbackService.cs`.
- Public tracker UI: `web/ophalo-web/src/app/keep/r/[pageToken]/CustomerTrackerView.tsx` and
  `TrackerActionCard.tsx`.
- Authenticated list/detail UI: `web/ophalo-app/src/pages/Requests.tsx` and `RequestDetail.tsx`.

## Implemented Slices

Keep the normal file-level preflight and split the work if it exceeds the session gate.

### S85a — Feedback receipt data and audit trail — implemented

- Confirm whether `KeepRequestEventType` is persisted as an integer; add a durable internal
  `FeedbackReceived` / `feedback_received` event without a new request-feedback schema.
- Create it during successful customer feedback submission. Its content must preserve the response
  and optional customer comment in a displayable structured or safely formatted form.
- Add authenticated detail mapping/presentation for the new type. It must not expose internal review
  state on the public tracker.
- Fix `SubmitFeedbackService` so its immediate success response carries `FeedbackComment` as well as
  the saved response and timestamp. The public tracker recap must retain comment after submit and on
  revisit.

### S85b — Feedback-review entry and focused presentation — implemented

- In `Requests.tsx`, selecting a row from the `feedback_review` tab must pass
  `focusPanel="feedback_review"`; all other list navigation stays unchanged.
- In `RequestDetail.tsx`, render an unreviewed negative-feedback focus card above Activity only when
  entered with that focus context. Move the focus-scroll target to that card.
- The focused card shows customer feedback, timestamp, optional comment, and primary **Mark as
  reviewed**. Use existing server action metadata; no client-side permission inference.
- When opened normally, retain the active negative feedback control in Utilities with a subtle `New
  feedback` treatment.

### S85c — Clear state and closeout — implemented

- Keep positive feedback as informational Utilities content.
- Remove reviewed negative feedback from Utilities entirely; do not retain Build 084's quiet
  `Negative feedback reviewed` summary card.
- After successful review, use the returned detail state, immediately remove the active focused/card
  state, and show a brief `Feedback marked as reviewed` toast or transient inline confirmation.
- Ensure queries/counts refresh or invalidate so Feedback Review no longer includes the request.

## Hard Boundaries

- No new Prisma/schema feedback fields and no feedback-system rewrite.
- No ratings, stars, CSAT/NPS, testimonials, or public-review features.
- No acknowledgement action for positive feedback.
- No required review note, automatic follow-up request, or automatic reopen.
- Do not retain reviewed negative feedback as active Utilities content.
- Preserve Owner/Admin-only review authorization, optimistic concurrency/version checks, account/row
  visibility, and existing comment-visibility policy.
- Do not expose internal review events, reviewer identity, or review notes through public tracker
  endpoints.

## Acceptance Criteria

- A negative feedback item opened from Feedback Review visibly lands on a main-column feedback card.
- The same item opened through normal navigation stays in Utilities with `New feedback` treatment.
- Owner/Admin can mark it reviewed in one click; it disappears from active presentation and Feedback
  Review counts, while the request remains Closed.
- Positive feedback stays display-only in Utilities.
- All Activity presents feedback received and feedback reviewed as separate events without repeating
  the customer comment in the latter.
- The customer feedback recap includes the submitted choice, optional comment, and timestamp both
  immediately after submit and after revisit.
- Focused backend/API tests, relevant PWA/public tracker tests, and TypeScript checks pass before
  closeout.

## Required Test Coverage

- Customer feedback submission persists comment and produces `feedback_received`.
- Owner/Admin review produces separate `feedback_reviewed`, clears active feedback state, and leaves
  the original feedback untouched.
- Public immediate POST response includes `feedbackComment`.
- Positive feedback cannot be marked reviewed and never appears in the review queue.
- Feedback Review row navigation supplies feedback focus.
- Reviewed negative feedback no longer renders in Utilities.

## Implementation Evidence

Commit `315b231` implements this brief. It adds a `FeedbackReceived` event and migration, appends the
event during feedback submission, returns the saved comment in the immediate public response, promotes
negative feedback from the Feedback Review list, removes reviewed negative feedback from Utilities,
and invalidates request-list queries after review. It also adds associated backend tests and updates
the authenticated timeline presentation.

The normal Utilities treatment is implemented through the existing secondary/recommended-action
pattern rather than literal `New feedback` copy. Confirm this is sufficiently discoverable in pilot
testing before opening a new polish slice.
