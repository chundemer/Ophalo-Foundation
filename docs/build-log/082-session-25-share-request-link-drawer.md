# Build Log 082 — Session 25: Share Request Link Drawer

**Started:** 2026-07-12
**Completed:** 2026-07-12
**Status:** Complete — S25a–S25d done; end-to-end QR/SMS test deferred to first deployment
**Session name:** S25 share request link drawer / zero double-copy sharing
**Related ADRs:** ADR-381, ADR-382, ADR-432
**Next free ADR:** ADR-438

---

## Purpose

This session builds the authenticated PWA workflow for sharing an existing customer request page
link from the request list and request detail workbench.

The business need is direct: after a request exists, the operator must be able to get the customer
their private request page link without awkward double-copy steps, server-side SMS infrastructure, or
false "sent" audit language.

The V1 product posture is:

```text
OpHalo prepares the handoff.
The human sends through their chosen channel.
OpHalo records only the human confirmation: Mark as Shared.
```

This keeps V1 out of backend SMS delivery, carrier compliance, proof-of-send ledgers, and automated
customer notification preferences while still giving desktop PWA users a polished sharing workflow.

---

## Current Code Validation

The customer request page already supports the customer promise used by the share message.

Validated surfaces:

- `web/ophalo-web/src/app/keep/r/[pageToken]/CustomerTrackerView.tsx`
  - active customer actions include question/update, request update, request call, share
    availability, add details, and cancellation request;
  - customer can view request status/history and copy/share the page link;
  - closed eligible requests expose feedback.
- `src/OpHalo.Api/Program.cs`
  - anonymous rate-limited routes exist for `/question`, `/update_request`,
    `/information_added`, `/call_requested`, `/timing_change_requested`,
    `/cancellation_requested`, and `/feedback`.
- `src/OpHalo.Keep.Application/Requests/KeepCustomerPageMapper.cs`
  - active allowed actions are emitted for received/scheduled/in-progress/pending-customer/resolved
    requests;
  - closed requests expose `feedback` only when eligible.

Therefore the outbound share copy may honestly tell customers they can view updates, ask a question,
request a call, and send details from the request page.

---

## Locked Decisions

### Decision 1 — Primary V1 method is Scan to Text QR

Scan to Text is the primary V1 share method for desktop PWA users.

The owner can scan a QR code from the desktop screen with their phone and open a native SMS draft
addressed to the customer. OpHalo does not send the SMS.

### Decision 2 — Drawer remains open until Mark as Shared

The drawer must not close automatically after QR, email, WhatsApp, copy-message, or copy-link
actions.

After any handoff action, the drawer shows a prepared state and keeps **Mark as Shared** available.

### Decision 3 — Audit language is Mark as Shared / Shared Manually

Use:

```text
Mark as Shared
Shared manually
```

Never use **Sent**, **Delivered**, or similar delivery language for these external handoffs.

### Decision 4 — V1 share actions

V1 includes exactly these actions:

- **Scan to Text**
- **Open Email**
- **Open WhatsApp**
- **Copy Message**
- **Copy Link**

Server-side SMS, Google Voice integration, mobile app push handoff, proof-of-send, and automated
customer notification workflows are not in V1.

### Decision 5 — Phone is required; email is optional

Request creation requires customer phone. Missing phone is an exceptional/legacy data integrity
state, not a normal share-drawer path.

Email is optional. If missing, disable **Open Email** with quiet copy:

```text
Add customer email to enable email draft.
```

### Decision 6 — Unified share message uses first name and business name

Default template:

```text
Hi {firstName} — {businessName} created a private request page for you:
{customerPageUrl}

No account is needed. You can save this link to view updates, ask a question, request a call, or send details anytime.
```

The message must stay short enough for SMS and professional enough for email/WhatsApp/copy-paste.

### Decision 7 — Message is editable in V1

The drawer loads the default generated template into an editable message body.

V1 does not include saved template management, variable editors, or per-business template settings.

### Decision 8 — Edited message body is not saved permanently

The edited message is local to the drawer session except for short-lived SMS handoff records.

Do not save edited message bodies to:

- request data;
- request event history;
- internal notes;
- customer-visible history.

### Decision 9 — Mark as Shared is always enabled

**Mark as Shared** remains enabled even before a share action is used, because the owner may have
already shared the link elsewhere.

If clicked without a known prepared method, log:

```text
sharedVia = manual_other
```

### Decision 10 — Entry points are request list and request detail

Expose **Share Link** from:

- request row/list view;
- request detail workbench.

If `needsShare == true`, the action can be more prominent. After sharing, keep the action available
for reshares.

### Decision 11 — After Mark as Shared succeeds

After the backend confirms the manual share update:

- close the drawer;
- clear `needsShare`;
- add request event history;
- update list/detail state;
- show a small confirmation such as `Request marked as shared.`;
- keep **Share Link** available for future reshares.

Repeated shares create additional history entries while latest share fields can reflect the most
recent method/time/user.

### Decision 12 — This drawer shares only the customer request page link

The drawer is opened from an existing request and shares that request's private customer page.

The business new request link is separate and belongs to Settings, mobile share flows, or a future
business-link quick-share surface.

### Decision 13 — Customer page promise and caution

The share message may promise that the customer can view updates, ask a question, request a call,
and send details from the page.

The customer request page must also disclose:

```text
Anyone with this private link can view this page. Please don't include sensitive personal, financial, or medical information.
```

Use **private link**, not **public page**, in customer-facing language.

### Decision 14 — Owner privacy notice

The Share Link Drawer shows a light owner-facing notice:

```text
Anyone with this private link can view the customer request page.
```

### Decision 15 — Scan to Text uses a secure micro-handoff page

Do not encode the full SMS payload directly in the QR code.

Instead, Scan to Text uses a compact, opaque, short-lived SMS handoff token scoped to one request and
one prepared message context.

QR target shape:

```text
https://app.ophalo.com/keep/share-sms/{handoffToken}
```

Constraints:

- no raw request IDs in public handoff URLs;
- no customer phone/message payload embedded in the QR;
- token expires after 15 minutes;
- token does not assume SMS launch succeeded;
- fallback path prevents dead ends.

### Decision 16 — QR sync uses explicit refresh with stale guard

The QR represents the last minted SMS handoff token and message body.

When the owner edits the message after a token is minted:

- immediately mark the QR stale;
- visually block/dim the QR so it cannot be accidentally scanned;
- show:

```text
Update Text Link to Match Edits
```

Clicking the callout creates a fresh 15-minute SMS handoff token for the current message and restores
the QR to a scannable state.

Email, WhatsApp, and Copy Message always use the live edited message directly.

### Decision 17 — Expired handoff tokens expose no payload

Desktop drawer expiry state:

```text
Text link expired
Create New Text Link
```

Mobile handoff page expiry state:

```text
This text link expired. Return to OpHalo and create a new text link.
```

Expired tokens must not expose phone number, message body, or copy fallback.

### Decision 18 — Handoff tokens are reusable until fixed expiry

SMS handoff tokens are reusable until their fixed 15-minute expiry.

Do not extend expiry on use. Do not make tokens one-time-use in V1, because auto-launch may fail and
the fallback buttons need to remain usable.

### Decision 19 — Valid mobile handoff page includes fallback actions

While the token is valid, the mobile handoff page includes:

- **Open Text Message**
- **Copy Message**

The page may attempt native SMS launch automatically, but must still provide these explicit
interaction-triggered fallbacks.

### Decision 20 — Only Mark as Shared creates request history

Do not create request history for:

- token creation;
- QR render;
- QR scan;
- mobile handoff page open;
- SMS app launch;
- email open;
- WhatsApp open;
- copy message;
- copy link.

Those are prepared states. Only **Mark as Shared** writes request history/audit history.

### Decision 21 — Store handoff payload only in short-lived handoff record

The SMS handoff record stores the prepared message body only while the token is valid.

Suggested fields:

```text
handoffTokenHash
requestId
accountId
customerPhone
messageBody
createdBy
createdAt
expiresAt
```

Store a token hash, not the raw token. The message body must not become permanent request/event data.

### Decision 22 — Email and WhatsApp use direct launch links

Only SMS QR uses the handoff-token architecture.

Email uses:

```text
mailto:{customer_email}?subject=...&body={current_message}
```

WhatsApp uses:

```text
https://wa.me/{customer_phone}?text={current_message}
```

Both use the current editable message body at click time.

### Decision 23 — Frontend renders QR

Backend returns the compact `handoffUrl`.

The authenticated PWA frontend renders the QR from that URL using a small QR library. Do not use an
external QR service. Do not make the backend generate QR images in V1.

### Decision 24 — Drawer is a modal/dialog

Use a modal/dialog in V1.

Desktop layout may be two-column:

```text
message/contact/link preview | QR/actions/Mark as Shared
```

Narrow screens stack into one column.

---

## API And Data Model Implications

### New authenticated handoff-token endpoint

The PWA needs an authenticated endpoint to create or refresh an SMS handoff token for a request and
prepared message body.

Expected inputs:

- request ID;
- current message body;
- current request/customer link context;
- current request version if the implementation needs concurrency protection.

Expected output:

```json
{
  "handoffUrl": "https://app.ophalo.com/keep/share-sms/{token}",
  "expiresAtUtc": "..."
}
```

The endpoint must:

- require authenticated access to the request;
- preserve account scoping and row/action policy;
- require a non-empty message body;
- require customer phone to exist;
- store only a token hash;
- create a fixed 15-minute expiry;
- not write request event history.

### New public mobile handoff route

The public web app needs a lightweight mobile page:

```text
/keep/share-sms/{handoffToken}
```

The page resolves the token through the API, then:

- detects platform enough to format a practical `sms:` URI;
- attempts native launch;
- shows **Open Text Message** and **Copy Message** fallbacks;
- shows no payload after expiry or invalid token.

### Manual share commit endpoint

Use or extend the existing share-intent clearing flow only if it can honestly represent manual
sharing.

The commit should record:

```text
needsShare = false
sharedAt = now
sharedBy = currentUserId
sharedVia = sms_qr | email | whatsapp | copy_message | copy_link | manual_other
```

and a request event such as:

```text
Shared manually via SMS QR.
```

If an existing endpoint such as clear-share-intent is reused, avoid wording that implies automated
delivery.

---

## UI Behavior

### Initial state

When the drawer opens:

- load customer name, phone, optional email, and customer request page link;
- generate the default message;
- create the initial SMS handoff token for that message;
- render a clear QR;
- show the owner privacy notice;
- keep **Mark as Shared** enabled.

### Prepared states

After a launch/copy action:

```text
Prepared via Email. After you send it, mark this request as shared.
```

Use equivalent copy for SMS QR, WhatsApp, Copy Message, and Copy Link. Do not say sent.

### Missing optional email

If customer email is missing:

- disable **Open Email**;
- keep all phone-based and clipboard actions available.

### Exceptional missing phone

If a legacy/invalid request lacks customer phone:

- show a data warning;
- disable Scan to Text and WhatsApp;
- keep Copy Message and Copy Link available;
- keep Open Email available if email exists.

---

## Non-Goals And Deferred Work

Deferred from V1:

- server-side SMS sending;
- carrier registration, delivery receipts, proof-of-send, and SMS compliance ledgers;
- Google Voice integration or automation;
- mobile app push-to-phone handoff;
- backend automated customer SMS/email notification preferences;
- saved message templates;
- storing edited outbound share copy in request history;
- including the business new request link in this drawer.

Google Voice remains owner-managed and is indirectly supported through **Copy Message**.

---

## Acceptance Criteria

- A request with `needsShare == true` exposes **Share Link** from list and detail.
- Drawer opens with customer contact, editable message, privacy notice, QR, channel actions, and
  **Mark as Shared**.
- Scan to Text QR encodes only a compact handoff URL.
- Editing the message stales/blocks the QR until **Update Text Link to Match Edits** is clicked.
- Expired QR state requires creating a new text link.
- Mobile handoff page opens valid SMS handoff and provides fallback buttons.
- Expired/invalid mobile handoff token exposes no payload.
- Email and WhatsApp launch directly from the current edited message.
- Copy Message copies the full current message in one action.
- Copy Link copies only the raw customer request page URL.
- **Mark as Shared** clears `needsShare`, records `sharedVia`, writes honest manual-share history,
  closes the drawer, and leaves **Share Link** available for reshare.
- No UI says **Sent** for external handoffs.

---

## Test Plan

Backend/API:

- authenticated token creation succeeds for accessible request with customer phone;
- inaccessible request fails closed;
- missing/empty message fails;
- missing phone fails or returns an explicit disabled reason;
- raw token is never stored;
- expired token returns no payload;
- invalid token returns no payload;
- Mark as Shared writes manual-share metadata/history and clears `needsShare`;
- token creation/resolve does not write request history.

Frontend/PWA:

- drawer renders from list and detail;
- email missing disables Open Email only;
- editing message triggers QR stale guard;
- refreshing QR restores scannable state and expiry;
- Copy Message uses edited text;
- Copy Link uses only URL;
- prepared states do not close the drawer;
- Mark as Shared closes drawer and updates row/detail.

Public mobile handoff:

- valid token shows/attempts SMS launch and fallback controls;
- expired token shows expired copy with no message or phone;
- fallback **Copy Message** works only while valid.

Manual QA:

- scan QR from desktop screen with iPhone and Android if available;
- confirm email draft pre-addresses/prefills;
- confirm WhatsApp Web opens with message body;
- confirm no double-copy path is required for normal channels.
