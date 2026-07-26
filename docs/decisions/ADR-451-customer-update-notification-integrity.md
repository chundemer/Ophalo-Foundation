# ADR-451 — Customer Update Notification Integrity

**Status:** Locked  
**Date:** 2026-07-26  
**Related:** ADR-291, ADR-370, ADR-421, ADR-432, ADR-443, ADR-445, ADR-447; GAP-052

## Decision

Keep treats customer-page publication, notification preparation, owner confirmation of sending, and
customer receipt as separate facts. It never claims receipt or delivery.

A customer-page update alone may be posted at any time, but it does not clear a gated
customer-waiting obligation or record first response. The owner may then prepare notification using
the business's existing communication posture: opaque desktop QR handoff for SMS, direct mobile PWA
SMS launch, or owner-launched `mailto:`. Every prepared customer-update notification includes the
private customer request-page link. The page is business-first (business logo/name header) with a
quiet OpHalo footer; SMS remains business-authored and carries no OpHalo footer/product links.

Only a separate, server-authorized owner confirmation linked to the posted update and selected
channel can record a notification as sent. This confirmation clears `NeedsShare` when the visible
prepared notification includes the customer-page link. Preparation, OS launch, expired QR, failed
launch, cancellation, page view, and unconfirmed email/text never clear `NeedsShare`, attention, or
first response.

## Customer-Waiting Policy

- Confirmed text/email or a completed live call clears communication attention for customer
  message, update-request, first-response-due, schedule/timing-change, change/cancel,
  cancellation-request, and complaint reasons. It counts as first response only when first response
  is still pending.
- For `CallRequested`, only a completed live call clears attention; text/email does not satisfy an
  explicit call request.
- A deliberately logged detailed voicemail never counts as customer informed or first response. It
  preserves lifecycle status, ends the immediate response-overdue escalation, and creates a visible,
  editable follow-up promise defaulting to the next business day in the business timezone.
- `UnresolvedFeedback` stays in its separate terminal review workflow.
- No-answer attempts, dialer/mail launch, handoff preparation, failed email, and customer-page
  views never clear anything.

Customer contact preference selects the suggested channel but never hard-blocks a permitted
alternative. Viewer is blocked. Existing server row authorization governs preparation and
confirmation; confirmation belongs to the same authenticated user who prepared the handoff.

## Recovery And Scope

If notification cannot be completed, Keep preserves the posted update and unresolved obligation,
then presents the next available truthful action. It never saves separate SMS/email drafts: it
regenerates a handoff from the stored customer-page update and link. Custom message templates and
multiple-unnotified-update behavior are deferred until Request List recovery is complete and pilot
evidence demonstrates a need.
