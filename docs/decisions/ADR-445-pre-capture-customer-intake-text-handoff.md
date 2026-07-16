# ADR-445 — Pre-Capture Customer Intake Text Handoff

**Status:** Locked  
**Date:** 2026-07-16  
**Related:** ADR-442, ADR-443, ADR-444, GAP-018, build-log/088-pwa-launch-readiness-remediation

## Decision

Customer self-service remains the primary New Request route. The business sends the caller the
existing durable public intake form; Keep does not create a second intake form and does not send an
SMS itself.

The durable customer URL is exactly:

```
{PublicBaseUrl.TrimEnd('/')}/keep/s/{publicSlug}
```

It is the customer-facing `ophalo-web` origin (`App:PublicBaseUrl`), never the authenticated PWA
origin (`App:AppBaseUrl`). A private request page is created only after a request exists and is not
part of this pre-capture flow.

## Caller Text Handoff

When an Owner/Admin is speaking with a remote customer, Keep asks only for the customer's mobile
number (or lets the Owner/Admin confirm the number already known from the call). It then prepares a
short-lived SMS draft addressed to that number and containing the durable public intake URL.

| Surface | Required behavior |
|---|---|
| Desktop PWA | Keep creates an opaque, short-lived handoff URL and renders it as a QR code. The Owner/Admin scans it with their phone; the phone opens a pre-addressed SMS draft to the confirmed customer number. |
| Mobile PWA | Keep opens the same pre-addressed SMS draft directly. No QR code or handoff token is needed. |
| Customer | The customer receives the durable public intake URL and completes the existing public form. |
| Fallback | **Enter request for customer** remains immediately available for urgent calls or customers who cannot or will not self-serve. |

The QR payload contains only the opaque handoff URL. It must never expose a raw phone number,
message body, account identifier, or public slug. The server-side, short-lived handoff record may
store the confirmed customer phone and message body so that the scanned URL can resolve to a
pre-addressed draft; keeping data out of the QR payload does not prohibit this server-side record.

The handoff expires after 15 minutes. Invalid and expired tokens are indistinguishable, are
rate-limited, receive `Cache-Control: no-store, private`, and are redacted from application logs.

## In-Person Option

An Owner/Admin may additionally show a durable QR code containing the public intake URL directly
when a customer is physically present. This is an optional in-person convenience, not the primary
remote-caller workflow and not a replacement for the caller-text handoff.

## Consequences

- The pre-capture handoff entity includes `CustomerPhone`; a blank-recipient `sms:?body=...` flow
  does not satisfy this decision.
- The desktop handoff page resolves the opaque token to both customer phone and message body and
  uses the established platform-specific SMS URI separator behavior.
- The PWA must lead Owner/Admin New Request with customer self-service controls and must not put
  those controls on Request Detail. Operators continue directly to the staff-entry path.
- Existing R88f-a/b code that builds `/keep/{slug}` from `AppBaseUrl`, or that opens a
  blank-recipient draft, is superseded and must be repaired before the PWA handoff panel is built.
