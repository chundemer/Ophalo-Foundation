# Build Log 088 — PWA Launch Readiness Remediation

**Status:** Active — R88f correction locked before remaining implementation  
**Date:** 2026-07-16  
**Controlling decisions:** ADR-442, ADR-443, ADR-444, ADR-445

## R88f Correction — Pre-Capture Customer Intake Text Handoff

### Problem found after R88f-a/b

R88f-a/b implemented an account-scoped opaque handoff that opens a blank-recipient SMS draft and
builds its message with `{AppBaseUrl}/keep/{slug}`. That does not satisfy GAP-018's intended remote
caller workflow and does not point to the durable public-intake route.

The intended workflow is: an Owner/Admin speaking with a caller confirms only the caller's mobile
number, sends that caller the durable public intake link, and lets the caller complete name,
address, and request details. Manual staff capture remains the immediate fallback.

### Locked contract

- Public intake URL: `{PublicBaseUrl.TrimEnd('/')}/keep/s/{publicSlug}`.
- Desktop Owner/Admin: opaque QR handoff; the scanned page opens a pre-addressed SMS draft to the
  confirmed caller number.
- Mobile Owner/Admin: direct pre-addressed SMS draft; no QR token.
- QR payload: opaque URL only; never raw phone, message, account ID, or slug.
- The New Request / **Text a Link** panel displays only the desktop opaque handoff QR. A durable
  customer QR belongs to a separately prepared counter/print display sourced from Public Link
  settings, never beside the staff QR in this active workflow.
- Customer self-service appears at the beginning of New Request for Owner/Admin. It does not belong
  in Request Detail. Operators go directly to staff-entry fallback.
- Keep does not send SMS, receive SMS replies, or create another public intake form.

### Required repair order

1. Repair the R88f-a backend contract and migration: persist `CustomerPhone` server-side, accept a
   confirmed normalized phone on handoff creation, build the exact `PublicBaseUrl/keep/s/{slug}`
   message, and keep opaque-token, expiry, rate-limit, cache-control, and log-redaction safeguards.
2. Repair R88f-b: resolve phone plus message from the opaque token and launch a pre-addressed SMS
   URI using the established iOS/Android separator handling.
3. Implement R88f-c only after those repairs: Owner/Admin Quick Capture begins at the customer
   self-service handoff panel; desktop renders the opaque QR; mobile launches the direct SMS URI;
   manual entry remains the explicit fallback.
4. Verify both desktop-to-phone and mobile-direct paths with a confirmed caller number and the
   manual-entry fallback. Verify any durable public QR separately from its Public Link settings
   source; it must not appear in the Text a Link panel.

### Non-goals

- No backend SMS provider, automatic sending, delivery receipt, reply ingestion, or notification
  ledger.
- No private per-request page before a request exists.
- No customer phone/message data embedded in a QR code.
