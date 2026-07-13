# Build Log 078 — Customer Tracker Link Email And Resend Configuration

**Started:** 2026-07-10
**Status:** Complete — S78a–S78e landed 2026-07-13
**Session name:** S29 customer tracker link delivery / Resend configuration / confirmation flow
**Next free ADR before this log:** ADR-432
**Next free ADR after this log:** ADR-433

---

## Purpose

This session captures the corrected email boundary and the follow-up slice for making customer tracker
links harder to lose after public intake submission.

The previous shorthand "no backend SMS/email to customers in V1" was too broad. The corrected
boundary is:

- platform email via `IEmailSender`/Resend is in scope for auth/member flows and production
  configuration;
- a narrow, fail-soft transactional tracker-link email is allowed after customer-initiated public
  intake when the customer supplied an email address;
- backend customer SMS, SMS reply ingestion, broad automated customer email/SMS notification
  workflows, notification preferences, quiet hours, delivery ledgers, proof-of-send, and campaigns
  remain deferred.

ADR-432 locks this distinction.

---

## Problem

Current public intake success behavior can leave non-technical customers without an easy way back to
their private request page:

- the reference code is visually prominent even though operators usually find requests by phone/name;
- "bookmark this page" assumes browser behavior many customers do not know or use;
- if the customer never opens the request page, the operator may see a misleading "not yet viewed"
  confidence signal even though the customer successfully submitted intake.

SMS would be the most familiar recovery path for many customers, but backend customer SMS remains out
of V1. Email is available through the existing Resend-backed platform email seam when the customer
chooses to provide an email.

---

## Locked Direction

Use a layered link-retention model:

1. **Email present:** send one transactional tracker-link email after successful public intake.
2. **No email:** confirmation UI should not depend on bookmarking; use auto-redirect, copy/share
   affordances, and clear customer-safe copy.
3. **Later operator contact:** operator-initiated `mailto:`, `sms:`, and native share handoffs should
   include/prefill the customer page link where supported.
4. **Recovery net:** if the customer loses the link, the business can find the request by phone/name
   and re-share the customer page from the operator surface.

Do not use phone numbers as customer-page URLs. Phone numbers are identifiers, not secrets. Customer
page access remains a high-entropy capability URL/token.

## Locked Decisions From 2026-07-10 Discussion

1. **The success screen is a transition, not the destination.** After public intake succeeds, the
   customer should be moved toward the private request page automatically instead of being asked to
   understand and preserve an intermediate confirmation page.
2. **The dedicated customer request page is the product moment.** The flow should make it very likely
   that customers see their private Keep page immediately, which also prevents a misleading operator
   "not yet viewed" signal caused only by an intermediate screen.
3. **The reference code is quiet support metadata.** Operators normally recover requests by
   phone/name. The reference code may remain available for support/debugging, but it should not be
   visually treated as the customer's primary key or something they must write down.
4. **Email is optional but benefit-led.** When the public intake form asks for email, helper copy
   should explain the direct customer value: if they add email, Keep can send them a secure link to
   track the request.
5. **Email-present means send the tracker link.** If a customer supplies email during public intake,
   send a single transactional tracker-link email after the durable request commit/page-token
   creation. Delivery failure must not fail the intake and must not be disclosed in the public API
   response.
6. **No-email customers still need a practical path.** Confirmation UX should use auto-open,
   copy-link/share affordances, and plain reassurance. "Bookmark this page" is not acceptable as the
   primary recovery instruction.
7. **Operator handoffs should carry the link.** Future `mailto:`, `sms:`, and native-share handoffs
   from the operator surface should prefill the private customer page link where supported, without
   treating composer launch as proof of contact.
8. **Phone number is lookup and recovery, not access.** A URL such as `/keep/9013333333` is rejected
   because phone numbers are PII, guessable, reusable across requests, and not unique to one service
   event. Customer page access remains token/capability-link based.
9. **This remains inside the V1 communication boundary.** Resend-backed platform email and this one
   narrow customer tracker-link email are in scope. Backend customer SMS, SMS replies, broad
   automated customer notifications, notification preferences, quiet hours, delivery ledgers, and
   proof-of-send semantics remain deferred.

---

## Implementation Slice Candidates

### S78a — Resend Production Configuration Check

Goal: ensure the existing email seam is deployment-ready.

Scope:

- confirm `Resend:ApiKey` and `Resend:FromAddress` deployment settings;
- verify `IEmailSender` uses `ConsoleEmailSender` only when Resend config is absent in development;
- document production secret requirements and local/dev terminal-output behavior;
- add or update a small smoke/runbook note if missing.

### S78b — Public Intake Tracker-Link Email

Goal: if a public-intake customer supplies email, send the private request-page link by email.

Scope:

- add a customer tracker-link email template;
- send after successful request commit/page-token generation;
- fail soft: intake succeeds even if email delivery fails;
- do not reveal email delivery success/failure in the public response;
- use configured public web base URL to build `/keep/r/{pageToken}`;
- add focused tests for email-present send, email-absent no-send, and delivery failure still returns
  success.

Out of scope:

- backend customer SMS;
- notification preference/opt-out platform;
- durable notification ledger/outbox/retry/dead-letter;
- proof-of-send semantics.

### S78c — Confirmation Flow And Link Retention UX

Goal: stop treating the success screen as the final destination.

Scope:

- demote or remove the prominent reference-code treatment;
- auto-redirect to the customer request page after a short confirmation state;
- keep a manual `Open request page` action;
- provide copy/share fallback for users who do not provide email;
- avoid "bookmark this page" as the primary instruction.

### S78d — Operator Correspondence Prefill

Goal: make every operator-written contact an opportunity to re-deliver the customer page link.

Scope:

- web `mailto:` handoffs include subject/body with the customer page link;
- mobile `sms:`/`mailto:` handoffs include/prefill the customer page link where supported;
- use configured public web base URL, never `window.location.origin`;
- opening a composer does not create proof of contact or mutate request state without explicit
  existing share/contact logging semantics.

---

## Guardrails

- Customer page URL remains token/capability-link based, not phone-number based.
- Email delivery is best effort and fail-soft after durable intake commit.
- Public API responses must not reveal whether an email was sent.
- No customer SMS is introduced in this session.
- No notification preference center, quiet hours, opt-out routes, or campaign-like behavior is added.
- Link tokens may necessarily appear in delivered email and provider dashboards/logs; this is the
  accepted capability-link delivery posture, consistent with magic-link style delivery.

---

## Open Questions

- Should the tracker-link email `Reply-To` use the business customer-facing email when configured, or
  a product-controlled support/no-reply address for V1?
- Should email-field helper copy on public intake say "Add your email and we'll send you a link to
  track your request" even when email is not required?
- Should auto-redirect happen immediately after the success response or after a short visible
  confirmation delay?

---

## Completion Notes (2026-07-13)

### Decisions locked during S29

- **Reply-To**: None for V1. Send from `Resend:FromAddress` only. `IEmailSender` interface unchanged.
  Email copy clarifies replies may not be monitored.
- **Email helper copy**: Benefit-led — "Add your email — we'll send you a link to track this request."
- **Auto-redirect**: ~2s delay with visible confirmation, manual "Open now" fallback.

### S78a — Resend configuration verified (no code change)

Wiring in `Program.cs` is deployment-ready:
- `ConsoleEmailSender` in development when `Resend:ApiKey` is absent.
- `ResendEmailSender` with Bearer token otherwise.
- Required production secrets: `Resend:ApiKey`, `Resend:FromAddress`, `App:PublicBaseUrl` (all in
  `appsettings.json` as empty strings — populate per environment).
- `MagicLinkSettings.PublicBaseUrl` is the correct injection point for building outbound URLs.

### S78b — Public intake tracker-link email ✓

- `CreateKeepPublicIntakeService`: `IEmailSender` + `IOptions<MagicLinkSettings>` injected.
  `TrySendTrackerLinkEmailAsync` sends after durable commit when `TrimmedEmail` is non-null.
  Tracker URL built as `publicBaseUrl.TrimEnd('/') + "/keep/r/" + pageToken`.
  Delivery failure swallowed — intake result unaffected. Public response unchanged.
- 3 new unit tests: email sent when present, not sent when absent, delivery failure → success.
- 2 new integration tests using `CapturingEmailSender`: email captured when present, empty when absent.
- Baseline: 1,064/1,064 unit tests.

### S78c — Confirmation flow and link-retention UX ✓

- `IntakeForm.tsx` (`ophalo-web`): email helper copy updated to benefit-led text.
  Success screen: reference code demoted to `Reference: {code}` footer metadata.
  Auto-redirect via `setTimeout(..., 2000)` to `/keep/r/{pageToken}`.
  When email was provided, confirmation copy explains link was sent and replies may not be monitored.
  Manual "Open request page now" button remains throughout.

### S78d — Operator correspondence prefill ✓

- `RequestDetail.tsx` `CustomerPanel` mailto: prefilled with subject "Your request page link"
  and body containing the customer page URL. URL built from `VITE_PUBLIC_BASE_URL` (public/customer
  web origin, not `window.location.origin`). Opening the composer does not mutate request state.

### Deferred

- Reply-To when Keep has a deliberate business-facing contact email setting.
- Delivery ledger / proof-of-send / retry / dead-letter queue.
- Notification preferences, quiet hours, opt-out.
- Backend customer SMS.
