# ADR-448 — Opaque Desktop Call Handoff

**Status:** Locked
**Date:** 2026-07-19
**Related:** ADR-443, ADR-445, GAP-020

## Decision

Desktop QR codes that transfer a phone call to the Owner/Admin's phone must never encode the
customer's raw phone number. Both existing desktop call-QR surfaces (`CustomerContactStrip`'s
`CallQrModal` and the Log external contact modal on Request Detail) encode `tel:{customerPhone}`
directly today, contradicting the Locked Responsive-PWA Strategy (ADR-443). This is the same class
of problem ADR-445 solved for the pre-capture SMS handoff; this decision defines the equivalent
contract for calling an already-known request's customer.

## Contract

- Creation: `POST /keep/requests/{requestId}/call-handoff`, authenticated, account/request scoped.
  Gated by the same Owner/Admin/Operator authorization, OffSeason, and feature-flag boundaries as
  the existing SMS handoff (`CreateSmsHandoffService`). Viewers are blocked. A request without a
  customer phone number cannot mint a handoff. Terminal request state does not block creation — a
  call handoff is a communication action, not a domain state mutation, consistent with existing
  precedent (`CreateSmsHandoffService` does not gate on `IsTerminal`).
- Storage: `KeepCallHandoff` — a distinct entity from `KeepSmsHandoff`, not an extension of it,
  so the SMS message-body `NOT NULL` invariant is never weakened for an unrelated purpose. Stores
  only a SHA-256 hash of the token, `RequestId`, `AccountId`, `CustomerPhone`, `CreatedBy`, and
  `ExpiresAtUtc`. The raw token is never persisted. Expires 15 minutes after creation.
- Resolve: `GET /keep/share-call/{handoffToken}`, public, anonymous. Returns `{customerPhone,
  expiresAtUtc}` for a valid, non-expired token; `404 NotFound` for unknown, malformed, expired, or
  replayed tokens. Invalid and expired cases are intentionally indistinguishable — no payload
  leakage. Sets `Cache-Control: no-store, private` and is rate-limited under the existing
  `public-intake` policy.
- The QR payload contains only the opaque handoff URL (`{AppBaseUrl}/keep/share-call/{token}`),
  never the raw phone number, in any form: not in the QR image, route/query logs, telemetry,
  clipboard state, durable client storage, or page titles.
- The resolver page (owner's phone, reached by scanning the QR) launches or prepares the `tel:`
  action there. Scanning or launching a call handoff does not itself log an external contact, clear
  `Needs Share`, or claim a call was completed — contact logging remains a separate, explicit action
  (mirrors ADR-401's contact-launch-does-not-log-contact principle).
- Mobile PWA is unaffected: it continues to launch `tel:{customerPhone}` directly (ADR-443). Only
  the desktop QR path is replaced.

## Sibling correction

While repairing this, `GET /keep/share-sms/{handoffToken}` (the existing request-scoped SMS
handoff resolver, S25a) was found missing the same `Cache-Control: no-store, private` header, the
`public-intake` rate limit, and redaction in `PublicTokenPathRedactor` that its sibling
`/keep/intake-sms/{token}` (R88f) already had. All three are corrected on `/keep/share-sms/` in the
same change rather than leaving a known, unfixed gap beside the new route.

## Consequences

- `KeepCallHandoff` is a new table (`keep_call_handoffs`), not a column addition to
  `keep_sms_handoffs`.
- `CustomerContactStrip.tsx` and the Request Detail Log external contact modal must both call the
  new create endpoint and encode the returned `handoffUrl`, not `tel:{phone}`, in their desktop QR
  codes. A narrow shared call-handoff QR component/hook should back both call sites so the same
  privacy-sensitive logic is not maintained twice, without merging their differing surrounding
  contact-log/close/focus behavior.
- No platform calling, automated SMS, contact logging, or new customer-facing capability URL is
  introduced by this decision.
