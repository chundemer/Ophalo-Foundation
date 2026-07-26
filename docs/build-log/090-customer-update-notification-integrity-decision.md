# Build 090 — Customer Update Notification Integrity Decision Brief

**Status:** Locked design — implementation split approved  
**Date:** 2026-07-25  
**Related:** ADR-291, ADR-370, ADR-421, ADR-432, ADR-443, ADR-445, ADR-447; GAP-052

## Problem Verified

`KeepRequest.AddBusinessUpdate` and `AddBusinessUpdateWithStatus` currently create a
customer-visible page update, mark first response for customer-originated requests, and clear
business-waiting attention. The PWA update modal states only that the update appears on the private
customer request page. It does not notify the customer.

This permits an unseen page update to remove a customer-waiting request from Needs Attention and
to make first-response reporting appear satisfied. This is a domain-integrity problem, not a copy
or PWA-only problem.

## Agreed Direction

- A customer-page update, notification preparation, owner notification attestation, and actual
  customer receipt are distinct facts.
- Keep remains outside backend SMS, SMS-reply ingestion, broad automated customer email, and
  delivery-proof claims.
- An update commits independently of any notification path; failed, declined, or abandoned
  notification cannot discard the page update.
- Desktop PWA retains opaque QR handoff for a pre-addressed SMS draft on the Owner/Admin phone.
  Mobile PWA may launch a prefilled SMS draft directly. Owner-launched `mailto:` remains available
  where appropriate. Launch/preparation alone proves neither contact nor delivery.
- Customer-waiting attention and first-response state must not clear merely because the customer
  page was updated.

## Locked Contract

1. `Post customer-page update` creates only the customer-visible update event. It may be used with
   `Not now`; page publication never clears a gated customer-waiting obligation or first response.
2. A separate confirmation, linked to the posted update and selected channel, is the only
   notification attestation. Do not add `customerInformed: boolean` to the ordinary update endpoint
   and do not use a composite post-and-notify mutation.
3. Every prepared update SMS/email visibly includes the private customer request-page link. The
   customer page is business-first with business logo/name header and quiet OpHalo footer; SMS has
   no OpHalo footer/product links.
4. A confirmed link-bearing notification atomically records the attestation, applies the permitted
   attention/first-response effects, and clears `NeedsShare`. Preparation/launch alone never does.
5. Customer preference suggests the channel but does not hard-block another permitted channel.
   Existing server row authorization applies; Viewer is blocked; the same authenticated user who
   prepared the handoff confirms it.
6. Keep stores no separate SMS/email drafts. It retains only the posted update and unresolved
   notification obligation, then regenerates a fresh handoff when the owner returns.

## Locked Reason-By-Outcome Matrix

| Attention reason | Confirmed text/email | Completed live call | Detailed voicemail |
|---|---|---|---|
| Customer message, update request, first response due | Clears attention; counts first response if pending | Same | Callback/follow-up state; no first response |
| Schedule/timing change, change/cancel, cancellation request, complaint | Clears communication attention; underlying work remains open | Same | Callback/follow-up state; no first response |
| Call requested | Does not satisfy the request | Clears attention; counts first response if pending | Callback/follow-up state; does not clear/count first response |
| Unresolved feedback | Separate review workflow | Separate review workflow | No effect on review state |

No page update, handoff preparation, SMS/email/dialer launch, no-answer attempt, failed email, or
customer-page view clears any customer-waiting state. A detailed voicemail is a logged response
attempt, not a customer-informed outcome. It preserves the lifecycle status and creates an editable
follow-up promise defaulting to the next business day.

## Proposed Implementation Split After Lock

### Slice A — GAP-052a: Domain/API integrity gate

Prevent page-only updates from recording first response or clearing gated business-waiting
attention. Add the durable obligation/attestation model and audit event, exhaustive reason policy,
permitted external-contact effects, same-actor/server authorization, concurrency, and focused
domain/integration coverage. Detailed voicemail must create its follow-up promise without mutating
the lifecycle status. This slice must be shippable without PWA notification UI and must leave the
owner/Admin queue truthful.

### Slice B — GAP-052b: Responsive PWA notification flow

Build the owner/Admin post → prepare channel → explicit attestation/recovery flow using
update-specific text and the mandatory visible request-page link. Desktop text uses opaque QR;
mobile text uses direct OS launch; email uses `mailto:`. Reuse security primitives where
appropriate, but do not reuse one-time ShareLinkModal ceremony for routine updates. Add focused
desktop/mobile, cancellation, expiry, conflict, preference, privacy/redaction, and queue-refresh
tests. No custom templates or multiple-unnotified-update workflow is in this slice.

## Go-Live Evidence

- A page-only update never clears a gated obligation or first-response state.
- Prepared/abandoned SMS and email never clear attention, first response, or `NeedsShare`.
- Every accepted satisfier produces a precise audit event and the intended atomic state change.
- QR URLs contain no raw phone, update text, or customer-page capability data; expiry, replay, and
  cache behavior are proven on real desktop/mobile handoff paths.
- Owner/Admin can understand the state after post, after prepare, after cancellation, and after
  attestation without mistaking any state for delivery proof.
