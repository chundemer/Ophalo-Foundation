# Pilot Readiness Bug And Gap Tracker

**Created:** 2026-07-02
**Purpose:** Live tracker for pilot-blocking or pilot-relevant bugs/gaps discovered during Session 14.
**Source:** Promoted from the Pre-S14e bug register in `docs/build-log/068-session-14-ophalo-web-front-door.md`.
**Current active items:** GAP-016 through GAP-035 — New Request launch blockers, public-intake
trust/continuity work, account-start and auth-entry conversion work, Request Detail launch findings,
and frontend robustness/consistency findings from the 2026-07-17 launch verification review. Build
087 is paused until they are triaged and the selected fixes are complete.
**Recently resolved:** GAP-015 — feedback review operational loop and accountability trail (commit `315b231`); GAP-004 — durable PWA request-detail routing (commit `3ebdc57`).
**Previously resolved:** GAP-010 — Ready to Close rows leaked communication next-actions (S24j).

This document is the current working tracker. Historical discovery notes stay in the build logs, but
triage, status, and next-session ordering should happen here.

As of S15c, all earlier active pilot-readiness bugs/gaps in this tracker were resolved. The active
New Request items below were found during the subsequent V1 public-use audit and supersede the
previous assumption that deployment smoke testing was the next step.
Detailed historical discovery notes and their resolved status remain below for traceability; only the
Active Launch Blockers section controls current work.

## Verification Findings — 2026-07-17

The current frontend passed its baseline typecheck and existing automated tests. The review that
produced the items below also found that those checks do not systematically validate CSS-token
references, shared presentation consistency, modal keyboard containment, async UI cleanup, or
render-failure recovery. These are validation-gate gaps, not evidence that ordinary build/test
validation was skipped.

The corrective work must add focused tests or automated checks with each relevant resolution, so
the same classes of defect are not rediscovered only by a later manual review. Do not combine all
items below into one implementation session; use the bounded slices listed in the proposed
sequencing at the end of this section.

### GAP-028 — Undefined CSS tokens silently break intended visual states

**Status:** Open
**Severity:** P1
**Area:** `ophalo-app` token use and CI validation

**Verified cause:** `BusinessSection.tsx` uses undefined `--ophalo-teal` for the customer-visible
composer cue, and `ShareLinkModal.tsx` uses undefined `--muted` for disabled share-channel rows.
CSS custom-property failures are not caught by TypeScript; each declaration is discarded by the
browser, losing the intended teal cue or muted disabled fill.

**Required resolution:** Replace `--ophalo-teal` with `--keep-accent`; replace `--muted` with the
approved neutral fill `--ophalo-canvas`. Add a repository check that compares every component
`var(--...)` reference with the token definitions and fails CI for undefined references. Keep the
inlined app token block synchronized with `web/shared/styles/ophalo-tokens.css` as part of that
check or its existing synchronization verification.

### GAP-029 — Request-status labels and badge variants are inconsistent by surface

**Status:** Open
**Severity:** P1
**Area:** `ophalo-app` request list, request detail, and Quick Capture

**Verified cause:** Status formatting/variant logic is independently implemented in
`pages/request-detail/helpers.ts`, `components/RequestRow.tsx`, and
`components/quick-capture/utils.ts`. As a result, `resolved` is `Work completed` in list/detail but
`Resolved` in Quick Capture; `pending_customer` is `Waiting on Customer` in Quick Capture but
`Pending Customer` in detail; and `received`/`scheduled` use an info badge in the list but a
default badge in detail. The detail helper also relies on substring heuristics for some variants.

**Required resolution:** Create one explicit `lib/requestStatus.ts` mapping every supported
server-status slug to its display label and `KeepBadge` variant. Import it from all three surfaces;
do not retain fallback substring heuristics. Lock the desired terminology and variants in focused
unit tests, including received, scheduled, active (`in_progress`), waiting on customer,
work completed, closed, cancelled, spam, and test.

### GAP-030 — Transient copy/success UI can reject or update after disposal

**Status:** Open
**Severity:** P1
**Area:** `ophalo-app` clipboard actions and Request Detail success feedback

**Verified cause:** Clipboard writes in Request Detail, customer panels, share links, and public
link settings are not consistently caught; permission/security denial can create an unhandled
promise rejection. Six independent copied-state timeout patterns also lack unmount cleanup. In
addition, `RequestDetail` does not clear its four-second feedback-review success timer on unmount.

**Required resolution:** Introduce a small shared copy-feedback hook that catches clipboard
failure, presents non-disruptive failure copy where appropriate, replaces/cleans its timer on reuse,
and clears it on unmount. Add equivalent cleanup for the feedback-review timer. Test successful and
rejected clipboard paths plus timer disposal; do not claim React's removed unmounted-state warning
is the defect—the defect is stale asynchronous UI work and missing failure handling.

### GAP-031 — A render exception can blank the authenticated workbench

**Status:** Open
**Severity:** P1
**Area:** `ophalo-app` application bootstrap

**Verified cause:** No React error boundary wraps `App` at the application root. A rendering error
in any authenticated-workbench panel can therefore unmount the visible application with no recovery
card. This finding applies to `ophalo-app`, not the separate public/customer `ophalo-web` surface.

**Required resolution:** Add a root error boundary with a plain recovery card and Reload action;
retain normal query/API error states inside their existing components. Add a focused render-throw
test that verifies the fallback and reload affordance.

### GAP-032 — Shared modal/focus behavior and Request Detail seams remain incomplete

**Status:** Open
**Severity:** P1
**Area:** `ophalo-app` form modals and Request Detail presentation architecture

**Verified cause:** The contact and service-location modals close from backdrop click, Escape,
close, and Cancel without dirty-form protection; a stray close can discard entered form data.
Neither traps keyboard focus. The same overlay/Escape/ARIA/click-stop pattern is hand-written across
Log Contact, Service Location, Share Link, Request Row Action, Follow-up Resolution, and Quick
Capture. `RequestDetail.tsx` remains 830 lines, including both form modal implementations and the
US state table, making these shared behavior fixes harder to apply safely.

**Required resolution:** Build a shared `KeepModal` primitive that provides dialog semantics,
initial focus, focus trap, focus restoration, Escape handling, and an explicit backdrop-close
policy. Form wrappers must use a dirty-close confirmation or disable backdrop-close; apply the same
policy to contact and service-location forms. Then extract `LogContactModal`,
`ServiceLocationModal`, and the US-state data into `pages/request-detail/`, leaving
`RequestDetail.tsx` as controller/orchestration code. Cover keyboard containment, trigger-focus
restoration, and dirty-close behavior with tests.

**Proposed bounded sequencing:**

1. GAP-028 and focus-ring consolidation: correct the two tokens, move the shared focus-ring class
   to a common UI constant, and add the CSS-variable CI check.
2. GAP-029: introduce the canonical status module with mapping tests.
3. GAP-032 and existing GAP-024/GAP-019: introduce `KeepModal`, protect dirty forms, then perform
   the Request Detail modal extraction without changing request mutation behavior.
4. GAP-030 and GAP-031: shared copy feedback/timer cleanup and the root error boundary.
5. In a separate tooling slice, introduce ESLint with React hooks and JSX-a11y rules. Establish the
   initial error/warning policy explicitly rather than allowing a configuration rollout to become an
   unbounded cleanup session.

**Mock-parity note:** `mockApiClient.ts` currently mutates the imported `api` object. Assignments
already receive signature checking, but simply annotating the mutable object as `typeof api` would
not prove every endpoint was mocked. When mock parity is selected, define a complete
`const mockApi: typeof api` and install it with `Object.assign(api, mockApi)` so a newly added
client method becomes a compile-time omission.

## Status Legend

- **Open:** Not fixed.
- **Resolved:** Fixed in a committed slice.
- **Deferred:** Known issue, not currently selected for the next implementation slice.
- **Needs decision:** Requires a product/architecture decision before implementation.

## Locked Responsive-PWA Strategy

Keep has one authenticated PWA, not separate desktop and native-mobile products. It shares one
backend contract, action policy, mutation behavior, visibility boundary, and React page controller.
Desktop and mobile PWA layouts may differ when that removes real-world friction for a busy owner.

| Capability | Desktop PWA | Mobile PWA |
|---|---|---|
| Call customer | QR handoff to the owner’s phone; no desktop `tel:` launch | Direct `tel:` action |
| Text customer/request link | Short-lived opaque QR handoff that opens the owner’s phone SMS draft | Direct SMS action |
| Email | `mailto:` action | `mailto:` action |
| Focused form/action | Centered modal | Bottom sheet |
| Request Detail composition | Desktop work area plus action/context sidebar | Mobile stack using the same shared panels and callbacks |

Rules:

- Adapt the presentation by responsive PWA layout/capability; do not fork business logic by device
  or create separate desktop/mobile request implementations.
- QR payloads never expose raw customer phone numbers or message bodies. Keep prepares a handoff;
  the owner’s device launches the external app. Keep does not send SMS.
- Direct call, text, and email launch external channels only. Logging the contact outcome remains a
  separate, explicit Keep action.
- A customer-facing action must be present in the relevant layout: do not tell an owner to share a
  page while exposing only a copy fallback.

## Active Launch Blockers — New Request, Public Intake, And Account Start

### GAP-033 — Public intake does not establish sufficient customer trust or return continuity

**Status:** Open
**Severity:** P1
**Area:** `ophalo-web` public intake identity, privacy disclosure, and post-submit customer journey
**Decision:** ADR-446

The public business request page asks a first-time customer to provide a service address, name, and
phone number before it gives enough concrete evidence that the page belongs to the business they
intended to contact or explains how they can reliably return to their request later. The current
fallback business-initial badge and business name are clean but can resemble an anonymous form
template when no logo is present. The key address-privacy disclosure appears only after the address
fields, email is hidden behind an optional disclosure despite being the clearest durable return-link
channel, and the page uses imprecise/competing language about a business following up with a private
request link versus the customer receiving a private request page after submission.

This is a conversion and trust risk, not a decorative-polish concern: a cautious customer who opens
the public link from a text message may reasonably leave rather than provide their home address and
contact information. The launch verification plan currently checks public-intake form behavior but
does not explicitly test first-visit business recognition, pre-entry privacy comprehension, or
customer return access.

**Required resolution:**

- Add a public-safe business identity block before form entry: custom logo when available; otherwise
  retain a polished initials fallback; business display name; and configured public website and phone
  contact when available. For this pilot, a website is an input-validated absolute HTTPS URL set by
  Owner/Admin, not a DNS/ownership-verified domain; do not label it "verified" or imply OpHalo has
  independently verified a business. Do not require social-network links.
- Rewrite the form introduction to state the actual customer outcome consistently, for example:
  `Submit your request, then manage updates on your private request page.` After a successful
  submission, take the customer directly to that request-specific tracker. The business also
  includes the same tracker link in its later customer email/text communications.
- Move the factual address privacy disclosure directly below the `Where is the service needed?`
  heading, before the street-address field. It must plainly identify what is shared, with whom, and
  what is not visible on the private request page. Add concise, equally factual contact-use copy
  before name/phone entry.
- Keep email visible and optional for the business to contact the customer. Do not describe it as an
  automatic tracker-link delivery mechanism. Preserve the existing customer preference model and do
  not treat a request-update channel as marketing consent.
- Replace the separate post-submit receipt/handoff page with direct navigation to the new private
  request tracker. On that first tracker visit, show a one-time, dismissible welcome banner with the
  business identity and safe request reference. It explains that the page keeps the request,
  updates, and next steps in one place, and that the customer can add details, ask a question, or
  request a call there. Do not claim a tracker link was emailed or make the customer save a
  capability URL as their only return method. The configured phone is a secondary recovery/contact
  route, not a replacement for the tracker.
- Add a real public privacy-policy link beside the submission/identity context. Any marketing use of
  phone or email requires separate explicit consent and appropriate legal review; do not use vague
  `secure`, `verified`, or `end-to-end encrypted` badges unless those claims are technically,
  operationally, and legally substantiated.
- Apply the same business-first/OpHalo-secondary identity and recovery rules to public terminal
  states where the request/link is known: post-submit confirmation, expired tracker, OffSeason or
  known-link unavailable, and safe error states. An unknown/invalid token must remain
  non-enumerating and must not reveal business identity. Known-business terminal states must not
  strand the customer in an anonymous shell; provide the safe business contact/recovery route that
  the available public contract permits.
- Do not use a timer-based or interstitial redirect. Successful submission navigates directly to the
  newly created tracker, where the one-time welcome banner provides the receipt/next-step context.
  The banner must be dismissible and must not reappear on ordinary later tracker visits.
- Make safe public browser/document titles identify the known business and outcome, for example
  `Request service from {businessName} — OpHalo Keep` and `{businessName} request updates — OpHalo
  Keep`. Retain `noindex` and restrictive referrer behavior for capability-link pages; do not put a
  page token, address, customer name, or other private request data in title/metadata.
- Simplify the OpHalo footer to factual platform attribution and privacy access. Do not use a fake
  security seal, decorative encryption claim, or a progress indicator that does not reduce a real
  customer uncertainty.
- Expand the public-intake launch verification gate with a skeptical-first-time-customer review on
  desktop and a real phone: business recognition from an inbound link, data-sharing comprehension
  before address entry, no-payment expectation, direct arrival at the private tracker with its
  one-time welcome, later business-message link continuity, and the unbranded-logo fallback.

**Deferred deployment decision — explicit OffSeason banner:**

R90b-3 may render the configured business identity and recovery/contact route while the existing
OffSeason policy reduces customer actions. It does **not** add a customer-facing `OffSeason` banner
in that slice: the current public customer-page contract has no `IsOffSeason` field. Before app
deployment, review real known-business OffSeason screenshots/behavior and decide whether the reduced
actions plus identity/recovery are sufficiently clear. If not, create a bounded follow-on to expose
a public-safe `IsOffSeason` signal and render explicit, non-alarming availability copy. Do not infer
or expose OffSeason state for unknown/invalid tokens.

**Acceptance criteria:**

- Before entering personal information, a customer can identify the intended business through its
  name and configured real-world identity/contact anchors, or sees a deliberate safe fallback when
  none are configured.
- Before entering an address or phone number, a customer can understand who receives that data and
  how it is used without relying on unsupported security claims.
- Public intake copy and submit behavior accurately state that successful submission opens the
  private tracker; later business communications repeat that same tracker link.
- After submission, the customer lands on the tracker—not a separate receipt page—and sees a
  one-time, dismissible welcome banner with the request reference and clear tracker-use guidance.
  The banner does not claim a tracker email was sent or tell the customer the capability link is
  their only way back.
- Privacy and marketing-consent boundaries are visible and accurately implemented; service-location
  and internal-only data remain absent from the public tracker.
- Known-business success, expired, unavailable, and OffSeason/error states retain safe identity and
  a usable recovery/contact path; unknown-token states do not reveal account identity. Explicit
  OffSeason banner/copy remains a required pre-deployment review decision.
- Post-submit continuity is readable and controllable: direct tracker navigation occurs only after a
  successful submission; no timer-based/interstitial redirect occurs, and the welcome banner can be
  dismissed and remains absent on ordinary later visits.
- Public browser titles identify the known business/outcome without leaking a capability token or
  private request information.
- Screenshot/manual verification passes for known-logo, no-logo, desktop, and real-phone inbound
  link flows; relevant public-intake tests and TypeScript checks pass.

### GAP-034 — Business account-start page looks unfinished and obscures the pilot value proposition

**Status:** Open
**Severity:** P1
**Area:** `ophalo-web` `/start` business account creation / pilot conversion
**Decision:** ADR-446

The `/start` page is the first product interaction for a prospective business owner, but its current
desktop presentation is a narrow, left-aligned unframed form in a wide canvas. The page has no
visible OpHalo/Keep identity beyond a small `Back to OpHalo` link, does not use the available space
to explain the operating value of Keep, and makes a production pilot-registration flow resemble an
internal development form. Its primary copy is also ambiguous: `Request access` implies a manual
approval queue even though the flow sends a sign-in link so the owner can finish setting up Keep.

The page already detects a browser time zone, but renders the stored technical IANA identifier (for
example, `America/Chicago`) as a primary form field. Removing time-zone confirmation entirely would
be unsafe: account time zone drives operational dates and response-policy timing, and a browser
guess can be wrong. The problem is presentation and unnecessary cognitive load, not the existence
of the setting.

**Required resolution:**

- Replace the unframed desktop form with a deliberate, responsive account-start composition. At
  desktop widths, use a centered, contained two-panel frame (approximately 40/60 rather than an
  obligatory equal split): a restrained navy Keep identity/value panel beside a white form panel.
  At mobile widths, stack a compact identity/value header above the form; do not preserve a cramped
  split panel.
- Give the identity panel a real OpHalo Keep mark/wordmark and a concise, factual value proposition
  for service businesses. Use product-supported benefits such as one place for service requests,
  clear customer updates/private request pages, and dependable follow-through. Do not add generic
  enterprise claims, unsubstantiated security claims, or inaccurate claims that Keep sends native
  SMS itself.
- Rewrite the form heading, supporting copy, and primary action to match the actual flow. Prefer
  `Start your Keep pilot` or `Set up Keep for your business` over `Get access to Keep`; state that
  the pilot is free only if universally true; and explain that an emailed sign-in link finishes
  setup. Do not say `Request access` unless a manual approval step truly exists.
- Retain browser time-zone detection and server validation, but present the selected value in
  customer language (for example, `Central Time — Chicago`) with concise `Detected from your device`
  context and an explicit Change control. Persist the canonical IANA identifier; never silently
  remove the customer's ability to correct it.
- Integrate existing sign-in and questions/support affordances into the form-panel/footer hierarchy
  so they are discoverable but do not compete with account start. Preserve accessible labels, errors,
  keyboard flow, and the existing pilot-full/email-in-use states.
- Add visible Privacy Policy and Terms links beside the account-start submit/support context. The
  links must describe actual policy resources and must not substitute for a separate marketing
  consent decision where one is required.
- Add screenshot/manual conversion review at wide desktop, common laptop, and mobile widths. It must
  review first-visit identity, value comprehension, CTA expectation, timezone correction, magic-link
  success/error states, and real long business/name/email data.

**Acceptance criteria:**

- A first-time service-business owner can identify OpHalo Keep, understand a factual reason to try
  it, and understand what will happen after submitting before entering form data.
- Desktop composition uses its available space intentionally; it no longer appears as a left-drifting
  development form. Mobile has a clear, compact stacked equivalent.
- Form copy and the primary action accurately describe the access/sign-in-link flow and pilot-cost
  posture.
- Time zone defaults from the browser where available, is shown in understandable local language,
  remains editable, and reaches the API as a valid canonical IANA identifier.
- Existing validation, duplicate-email, pilot-full, keyboard, and magic-link checks continue to
  pass; errors are programmatically associated with their fields or announced through an accessible
  summary/live region, and keyboard focus has a visible `:focus-visible` treatment.
- Privacy/Terms and support/recovery routes are visible without competing with the primary account
  start action; focused visual/manual checks cover desktop and phone layouts.

### GAP-035 — Auth, invite, and recovery states still present as anonymous development shells

**Status:** Open
**Severity:** P1
**Area:** `ophalo-web` sign-in, check-email, magic-link exchange/error, invite acceptance/error, and
mobile authorization entry states
**Decision:** ADR-446; ADR-390

GAP-034 identified the `/start` account-creation page as a weak first impression, but the same raw
left-aligned `.auth-page` shell is currently reused by sign-in, check-email, magic-link exchange and
failure states, invite acceptance and failures, and mobile authorization. Most of these pages show
no OpHalo Keep mark/wordmark and do not give an intentional visual hierarchy for success, recovery,
or support. A prospective owner can therefore begin on a redesigned page and immediately land on an
anonymous-looking intermediate/error state after providing their email; an invited team member sees
the same inconsistency.

The current auth forms also render errors as unassociated plain paragraphs and remove default
outlines while relying primarily on a border-color change for focus. This leaves keyboard and
assistive-technology feedback weaker exactly where a user is trying to enter/recover account access.

**Required resolution:**

- Build one shared, responsive auth-entry shell using the approved OpHalo Keep mark/wordmark,
  shared type/color tokens, intentional desktop/mobile composition, and a slot for contextual
  heading, outcome, recovery, and optional support/legal content. Migrate `/start`, `/signin`,
  `/auth/check-email`, browser magic-link exchange/error, and invite accept/error states to it.
- Give each state truthful, specific context: whether an email was sent, what to do if it does not
  arrive, whether an account/invite/link is expired, and the precise available recovery action. Do
  not imply that an email was delivered, an account was approved, or a support path exists when the
  underlying flow cannot provide it.
- Preserve ADR-390's sterile mobile authorization posture: it may use the OpHalo Keep identity asset,
  but must not add third-party scripts, analytics, external links, credentials, or raw-token
  exposure to the mobile handoff page.
- Add shared accessible field-error and status patterns: labels/inputs/errors are associated,
  submit failures are announced, focus treatment is clearly visible with `:focus-visible`, and
  keyboard focus is preserved or moved meaningfully after state transitions.
- Apply appropriate Privacy/Terms/support links to normal browser auth/account-entry surfaces. Keep
  such external links out of the sterile mobile-handoff surface required by ADR-390.
- Add page-specific titles for auth/recovery outcomes without placing a magic-link/invite token,
  email address, or other sensitive data into metadata. Test titles, keyboard flow, error
  announcement, and success/error/recovery states at desktop and phone widths.

**Acceptance criteria:**

- A business owner or invited member recognizes OpHalo Keep on every normal web auth/invite entry,
  loading, success, and failure state—not only on `/start`.
- Each state gives a factual next step or safe recovery route and does not regress existing
  authentication, invite, rate-limit, or session behavior.
- Normal browser auth pages have visible focus, associated/announced validation errors, and policy/
  support access where appropriate; the mobile authorization handoff remains ADR-390 sterile.
- Screenshot/manual verification passes for start, sign-in, check-email, expired/invalid magic link,
  invite success/failure, and mobile authorization on their relevant desktop/phone contexts.

### GAP-036 — Public Link & Profile settings do not complete the public-trust workflow

**Status:** Implemented (GAP-036a `8085971`, GAP-036b `014bae5`) — pending manual desktop/mobile
keyboard verification with a live authenticated session before this can be marked resolved.
**Severity:** P1
**Area:** `ophalo-app` Settings / Public Link & Profile
**Decision:** ADR-446; GAP-033 public-safe identity rules

R90b added optional hosted logo and website configuration and projects the approved identity to
known-business public surfaces, but the owner-facing settings form does not yet expose those fields.
An owner therefore cannot complete the branding configuration that the public request/tracker pages
are designed to display. The customer preview is also static: it does not reflect a business-name
edit while the owner is deciding how the public page will appear.

The same screen exposes `Replace link (breaks old shared links)` as a low-friction text action.
Replacing a durable public link can invalidate printed QR codes, website links, email signatures, and
text templates. The operation needs an explicit, server-enforced destructive confirmation rather
than relying on link styling or a client-only warning.

**Required resolution:**

- Add a clear `Branding & trust anchors` subsection to the existing company/profile settings. Expose
  optional **Logo URL** and **Website URL** fields that use the existing R90b-1 contracts and
  validation. This V1 uses externally hosted, absolute HTTPS URLs; it does not add an image upload
  drop-zone, blob storage, Facebook/social fields, or domain-ownership verification.
- Explain in concise factual copy that the logo, website, and existing customer-facing phone may
  appear on customer request/tracker pages. Never label the business or website `verified`.
- Bind the customer preview to unsaved profile draft values at least for business name, and include
  configured logo/fallback identity where the existing preview can do so without duplicating the
  public page implementation. A Save action remains required; live preview does not persist a draft.
- Replace the bare `Replace link` action with a destructive confirmation dialog that states the old
  public link will stop working. Require the owner to type `REPLACE` exactly before the final action
  becomes available. The server-side replacement command/endpoint must require and validate the
  explicit confirmation too; a client-only typed guard is not sufficient.
- Preserve the current public-link/slug authorization, old-link invalidation semantics, aliases, and
  non-enumeration behavior. Do not silently replace a link, rotate a token, or invalidate a printed
  link as part of an unrelated profile save.
- If the settings identity form/preview and destructive replacement guard exceed the slice gate,
  implement them as two independently tested, committed slices; do not weaken the confirmation to
  combine them.

**Acceptance criteria:**

- Owner/Admin can view, edit, validate, save, and revisit Logo URL and Website URL using the existing
  settings contract; Operators/Viewers cannot mutate them.
- Invalid/non-HTTPS URLs show the existing actionable validation behavior, and no upload or social
  integration has been added.
- The preview reflects the draft business name immediately and does not claim unsaved changes are
  live.
- Link replacement cannot execute until `REPLACE` is entered and the server validates the explicit
  confirmation; cancel/escape leaves the existing link unchanged.
- Successful replacement keeps the intended existing invalidation/alias behavior; failed, stale,
  unauthorized, and canceled operations leave the old link usable.
- Focus management, keyboard operation, and announced validation/destructive-dialog errors are
  verified, alongside relevant API/PWA checks.

### GAP-016 — New Request accepts invalid phone numbers and traps correction

**Status:** In progress — ADR-444 and authenticated-web work are committed; native parity and
leading-country-code client normalization remain open
**Severity:** P0
**Area:** `ophalo-app` Quick Capture lookup/capture; authenticated business-request API; shared
request-input validation

**Verified cause:** The lookup button permits 7–15 digits, the lookup service accepts 7–15 digits,
and `KeepRequestInputValidator` accepts the same range. A nine-digit number can therefore reach
the capture stage. That stage displays the number in a read-only field with no change path, so the
operator cannot correct it without abandoning the request.

**Proposed solution:**

- Lock launch phone policy to a normalized 10-digit North American number, accepting a leading
  country-code `1` and normalizing it before lookup/create. Do not imply international support with
  an ambiguous 7–15 digit field; add a country selector in a later dedicated internationalization
  slice if needed.
- Apply the identical rule in the lookup UI, authenticated create API, and public intake validation.
- Disable lookup/capture until valid and show an inline, associated error: **Enter a 10-digit phone
  number.**
- Replace the capture-stage read-only phone field with **Change**. Returning to lookup preserves and
  focuses the number, re-runs duplicate lookup as appropriate, and preserves entered name, email,
  description, source, and address draft.

**Decision:** ADR-444 locks normalized 10-digit North American validation. The remaining work must
accept a leading `+1`/`1` in the authenticated lookup UI and align native capture before this gap is
closed.

### GAP-017 — Staff New Request cannot capture service location at creation

**Status:** In progress — backend contract and disclosure are committed; incomplete-address client
handling and native parity remain open
**Severity:** P1
**Area:** `ophalo-app` Quick Capture; PWA create-request contract; Keep business-request command,
service, and entity creation
**Existing decision:** GAP-006 remains valid: staff may intentionally create a request without a
known service location and add it later; public customer intake continues to require location.

**Verified cause:** The authenticated create body currently contains only customer name, phone,
email, description, and source. The public intake command supports service-address fields, but the
staff-create command and `KeepRequest.CreateByBusiness` path do not.

**Proposed solution:** Add an **Add service address (optional)** disclosure to staff capture. When
opened, require a complete valid address—line 1, city, and two-letter US state—with line 2 and ZIP
optional. Carry it through the authenticated request-create contract and persist it as part of the
initial request creation. The omission path remains explicit and safe.

### GAP-018 — New Request leads with staff entry instead of customer self-service handoff

**Status:** In progress — ADR-442/445 locked; R88f-a/b require correction before the PWA entry
surface is implemented
**Severity:** P1
**Area:** `ophalo-app` Quick Capture; public-intake link setup; secure SMS handoff

**Verified cause:** A durable public customer-request page already exists in Settings, but it is not
available from New Request. The current drawer begins with internal phone lookup and staff capture,
even though staff entry should be the fallback when the customer cannot submit the request.

**Locked hierarchy:** Customer self-service is the default New Request path. **Enter request for
customer** is the clear secondary, last-resort path.

**Proposed solution:**

- For Owner/Admin, lead New Request with **Let the customer submit their request** and expose the
  remote-caller **Text customer from your phone** flow plus the explicit staff-entry fallback. Do
  not render a durable customer QR in this active panel; businesses manage any printed/counter QR
  separately from Public Link settings.
- For the remote-caller text action, the Owner/Admin confirms the caller's mobile number. Desktop
  renders an opaque, short-lived QR handoff that the Owner/Admin scans with their phone; it opens a
  pre-addressed SMS composer containing the public intake link. Mobile opens that same
  pre-addressed composer directly. Keep does not send SMS. The raw phone number and message body
  are stored only in the short-lived server-side handoff and never appear in the QR payload.
- A durable in-person QR may encode the public intake slug because it carries no customer data, but
  it belongs on a separately prepared customer-facing counter/print display, not beside the staff
  QR in New Request.
- The public-intake link is not the private customer request page. A private page is minted only
  after a request exists; it cannot be the pre-capture handoff. Operators retain the staff-entry
  fallback and do not receive link-management controls.
- Do not present the public form as a staff-entry action: same-account authenticated staff are
  deliberately blocked from posting public intake. It is a customer handoff/preview only.

**Decision:** ADR-442 locks the public-intake handoff as the primary New Request route. ADR-445
locks the exact caller-text, desktop QR, mobile-direct, URL, and server-side token contract.

### GAP-019 — Request Detail needs layout decomposition before further launch changes

**Status:** Open
**Severity:** P1
**Area:** `ophalo-app` Request Detail presentation architecture

**Verified cause:** `RequestDetail.tsx` combines page query/state ownership, mutation handlers,
modal orchestration, desktop sidebar composition, mobile-stack composition, and repeated placement
of shared panels. This makes the desktop/mobile contact, sharing, timing, accessibility, and
location changes unnecessarily risky: a behavioral change can be applied to one placement and missed
in the other.

**Proposed solution:**

- Retain `RequestDetail.tsx` as the single controller for data, mutations, navigation, modal state,
  and shared callbacks.
- Extract `RequestDetailDesktopLayout.tsx` and `RequestDetailMobileLayout.tsx` for composition
  only. Both receive the same detail data, permissions, and callbacks; neither fetches data or owns
  business rules.
- Continue extracting shared panels where their behavior is used in both layouts, beginning with a
  header-level `CustomerContactStrip` and the existing timing, service-location, sharing, composer,
  and activity components.
- Do not create two independent device-specific Request Detail implementations. Presentation order
  may differ; action policy, state transitions, contact logging, accessibility behavior, and error
  handling must remain shared.
- Apply the Locked Responsive-PWA Strategy: desktop contact/share actions use QR handoffs where a
  desktop cannot perform the physical phone action; mobile PWA actions launch the permitted native
  `tel:`, `sms:`, or `mailto:` channel directly.

**Acceptance criteria:** A Request Detail contact/share or accessibility change has one shared
behavioral implementation and explicit desktop/mobile composition call sites; TypeScript remains
clean; no lifecycle, permission, or optimistic-concurrency behavior changes as part of the refactor.

### GAP-020 — Desktop call QR exposes customer phone number

**Status:** Open
**Severity:** P0
**Area:** `ophalo-app` Request Detail contact strip and external-contact modal

**Verified cause:** Desktop call QR codes currently encode `tel:{customerPhone}` directly. This
puts raw customer phone data into the QR image, contradicting the Locked Responsive-PWA Strategy.

**Required resolution:** Replace the direct payload with an opaque, short-lived call-handoff URL
that opens the owner device's phone action, or explicitly amend the locked privacy rule before
launch. Do not ship the current direct-phone QR payload.

### GAP-021 — Authenticated Quick Capture rejects ADR-444 country-code input

**Status:** Open
**Severity:** P1
**Area:** `ophalo-app` Quick Capture lookup

**Verified cause:** `LookupGate` strips formatting but requires the resulting value to contain
exactly 10 digits. A valid ADR-444 input such as `+1 (555) 123-4567` therefore becomes 11 digits
and cannot be looked up, even though shared backend normalization accepts it.

**Required resolution:** Normalize an 11-digit value beginning with `1` to its final 10 digits
before applying the UI gate, lookup request, and draft return path.

### GAP-022 — Optional service-address disclosure can silently discard entered data

**Status:** Open
**Severity:** P1
**Area:** `ophalo-app` Quick Capture create form

**Verified cause:** The form sends address properties only when line 1 is populated. If staff opens
the disclosure and enters city, state, ZIP, or line 2 without line 1, request creation succeeds
without an address rather than surfacing the required-if-open condition.

**Required resolution:** When the disclosure is open, require line 1, city, and state client-side;
show associated field errors and submit all supplied address fields so server validation remains a
backstop. Line 2 and ZIP remain optional.

### GAP-023 — Change-phone draft can conflict with the newly selected customer

**Status:** Open
**Severity:** P1
**Area:** `ophalo-app` Quick Capture draft restoration

**Verified cause:** After **Change**, a newly looked-up customer can supply a new name/email prefill,
but the preserved draft takes precedence. The old name/email can then be displayed as read-only for
the new phone/customer.

**Required resolution:** On a changed-phone lookup that resolves to a different customer, reset or
explicitly confirm the identity fields while preserving request-specific draft fields such as
description, source, and address.

### GAP-024 — Modal accessibility is incomplete for Quick Capture and desktop call handoff

**Status:** Open
**Severity:** P1
**Area:** `ophalo-app` Quick Capture and Request Detail contact modal

**Verified cause:** The overlays set `aria-modal` and handle Escape, but keyboard focus is not
trapped inside them. The desktop call QR modal also does not restore focus to its launching control
when closed.

**Required resolution:** Add a focus trap to both overlays and restore focus to the original trigger
on every close path; retain Escape and initial-focus behavior.

### GAP-025 — Quick Capture does not recognize a customer found by request-list phone search

**Status:** Open
**Severity:** P1
**Area:** `ophalo-app` Quick Capture lookup; Keep customer/request identity data

**Verified cause:** Request-list search matches `KeepRequest.CustomerPhone`, while Quick Capture
looks up only `KeepCustomer.CanonicalPhone`. A request can therefore be found in the list by phone
while Quick Capture returns `customer: null` and opens a blank capture form. This affects legacy,
seeded, or otherwise unbackfilled request/customer data.

**Required resolution:** When the account-scoped customer lookup misses, safely fall back to a
normalized request-phone lookup and return existing name/email for Quick Capture prefill. Decide
whether the successful fallback should also backfill/link the `KeepCustomer` row; preserve account
isolation and test both prefilled legacy data and ordinary current customer matches.

### GAP-026 — Request-list search has no clear affordance

**Status:** Open
**Severity:** P2
**Area:** `ophalo-app` Requests list search

**Verified cause:** After entering a phone number, name, reference code, or other query, the request
list offers no visible one-click way to clear the search and return to the current queue view.

**Required resolution:** Add an accessible clear control when a search value is present. It must
reset the query, restore the selected queue's unfiltered list, and support both pointer and keyboard
use without requiring the user to manually select and delete the text.

### GAP-027 — Request list has competing alerts and no lifecycle scan cue

**Status:** Needs decision
**Severity:** P1
**Area:** `ophalo-app` Requests list row hierarchy, queue-count contract, and lifecycle presentation

**Verified cause:** The request-list screenshot review on 2026-07-16 shows multiple independently
colored signals on the same row: lifecycle status, response-overdue, attention reason, unshared
customer page, follow-up timing, planned timing, internal/customer priority, and a separate due
label. This makes routine future planning appear as visually important as an actionable breach.
It also exposes a trust problem: the page-level and tab counts can say **1 Needs attention** while
several default-queue rows visibly say **Response overdue**. A closed request can additionally
retain an overdue-response presentation even though terminal requests must suppress ordinary SLA
and follow-up attention; unresolved negative feedback remains the explicit closed-request exception
under GAP-015 / Build 085.

The list also has no concise indication of where a request is in its real lifecycle. Staff must
infer that from a status chip among unrelated alerts, which slows scanning of a mixed queue.

**Required product decision:** Lock a compact, truthful lifecycle representation for list rows.
It must describe the request's actual current state rather than imply that every request moves
linearly through every stage. A candidate is a small, muted milestone strip or header:
`Received → Scheduled → Active → Work completed → Closed`, with the current valid stage highlighted
and skipped/inapplicable stages left neutral. `Pending customer` is a waiting state, not a fabricated
completion step. The server-owned status and allowed transitions remain authoritative; do not add a
client-only lifecycle state machine.

**Required resolution after the decision:**

- Render one quiet lifecycle/status cue and at most one prominent, actionable exception per row.
  Use a deterministic priority order (for example: cancellation/customer urgent; response overdue;
  overdue follow-up; unresolved negative feedback; customer page unshared). Preserve other signals
  in detail or behind a compact, accessible `More signals` affordance rather than additional alert
  pills.
- Merge the relevant deadline into the selected exception, for example **Response overdue · Jul 13**;
  do not display a separate red due label for the same condition.
- Render future planned/follow-up dates as quiet metadata, not bordered or attention-colored badges.
- Suppress response-SLA and follow-up-overdue signals for closed requests. Retain only the approved
  unresolved-negative-feedback path for a closed request, including its Feedback Review queue
  behavior.
- Reconcile summary-pill/tab counts with the same canonical conditions that receive prominent
  attention treatment. A request shown as response-overdue must not make **Needs attention** look
  inaccurate; if it belongs to a different queue, the row treatment and label must make that
  distinction explicit.
- Retain the request list as the speed/action cockpit: keep one clearly visible contextual primary
  action where the server permits it, but move secondary actions into detail or an accessible overflow.
  Do not rely on hover-only controls, since the PWA must remain usable on touch devices.

**Acceptance criteria:**

- At normal desktop and mobile widths, a row communicates identity, current lifecycle state, and its
  single most important next intervention without a cluster of competing colored pills.
- Future scheduling is visually calmer than an overdue customer-facing commitment.
- Closed rows have no zombie SLA/follow-up alarm; unreviewed negative feedback remains visible and
  actionable through its dedicated workflow.
- Queue tabs and summary counts agree with the urgency the list visibly communicates.
- Lifecycle rendering follows server-returned state/transition policy and is covered by focused UI
  tests for received, active, waiting-on-customer, work-completed, closed, and closed-with-unresolved-
  feedback cases.

## P0/P1 Pilot Flow Bugs

### BUG-001 — Share-intent leaves stale detail version

**Status:** Resolved in S15a
**Severity:** P1
**Area:** `ophalo-app` request detail / Keep backend concurrency

`POST /share-intent` rotates `ConcurrencyVersion`, but the UI only sets a local `shareCleared` flag
and keeps using cached `detail.version`. The next staff action can produce a false `409` conflict.

Expected fix: after successful share-intent `204`, invalidate/refetch request detail, in addition to
or instead of the local flag.

### BUG-002 — `ApiError.extensions` parsing misses flattened ProblemDetails fields

**Status:** Resolved in S15a
**Severity:** P1
**Area:** `ophalo-app` API client / member management errors

ASP.NET Core flattens `ProblemDetails.Extensions` to top-level JSON. `apiClient.ts` reads
`problem["extensions"]`, so extension-driven copy such as `suggestedAction=reactivate|resend_invite`
is unreachable.

Expected fix: in one place in `apiClient.ts`, treat the whole problem object as the extension source
while preserving the existing fallback behavior.

### BUG-003 — Quick Capture navigation uses nonexistent URL routing

**Status:** Resolved in S15b
**Severity:** P1
**Area:** `ophalo-app` Quick Capture / app navigation

`QuickCapture.tsx` navigates with `window.location.href = "/keep/requests/{id}"`, but
`ophalo-app` routing is React state in `App.tsx`, not URL routing. Mobile post-success, "View Request
Workbench", and active-request card tap flows land on the default list after reload or can 404 on a
static host.

Expected fix: pass an `onSelectRequest` callback from `App.tsx` into `QuickCapture`.

### BUG-004 — Magic-link exchange checks obsolete duplicate-email error code

**Status:** Resolved in S15a
**Severity:** P2
**Area:** `ophalo-web` magic-link exchange

`ExchangeClient.tsx` checks `Account.NewAccountEmailAlreadyRegistered`, but the backend emits
`Account.EmailAlreadyInUse`. The duplicate start-to-exchange race falls into generic invalid-link
copy.

Expected fix: check `Account.EmailAlreadyInUse`.

### BUG-005 — Post-capture Copy Tracker Link does not record share intent

**Status:** Resolved in S15a
**Severity:** P1
**Area:** `ophalo-app` Quick Capture / tracker sharing

S13e says successful copy/share should record share intent. `SuccessPanel` copies the tracker link
without recording it, so Needs Share can continue nagging after the operator already shared.

Expected fix: after successful post-capture copy, call share-intent and refresh/align request detail
state as needed.

## Locked-Scope Gaps

### GAP-001 — Assign/clear responsible and watcher management missing

**Status:** Resolved in S15b
**Severity:** P1
**Area:** `ophalo-app` participation controls

S13d locked assign/clear responsible and watcher management. Backend contracts exist:
`PUT /responsible` and `PUT|DELETE /watchers/{id}`, with `canAssignResponsible` and
`canSelfAssignResponsible` computed. The client has no methods and `ParticipationSection` only
supports watch/mute.

Consequence: Operator "Available Work" is a dead end, and Owner/Admin cannot assign work.

### GAP-002 — Customer tracker page `/keep/r/{pageToken}` is missing

**Status:** Resolved in S15c
**Severity:** P1
**Area:** `ophalo-web` customer tracker

Staff share/copy paths build `{PublicBaseUrl}/keep/r/{token}`, but `ophalo-web` has no customer
tracker route. The API endpoint `/keep/r/{pageToken}` is JSON only.

Consequence: tracker links shared with customers 404 until the page exists.

Expected fix: add an `ophalo-web` customer tracker route at `/keep/r/{pageToken}` that fetches the
existing API JSON from `GET /keep/r/{pageToken}` and renders a customer-facing status/tracker page
without leaking the raw page token.

### GAP-003 — Public intake link setup does not appear to expose the usable S14g URL

**Status:** Resolved in S15a
**Severity:** P1
**Area:** `ophalo-app` Settings / public intake setup

S14g added `/keep/intake/{token}` in `ophalo-web`. The existing Settings intake section displays the
active `publicSlug` and calls ensure/replace, but the UI does not appear to construct/copy the full
customer-facing intake URL from the one-time raw token returned by setup.

Expected next step: verify the committed S14g setup flow and either expose the one-time
`{PublicBaseUrl}/keep/intake/{rawToken}` URL after ensure/replace or adjust the route/link contract
deliberately.

### GAP-004 — Browser back / refresh does not preserve app location

**Status:** Resolved in commit `3ebdc57` (2026-07-08)
**Severity:** P2
**Area:** `ophalo-app` navigation

State routing pushes no browser history. S13d assumed standard browser back for detail-to-list; on
mobile PWA the back gesture can exit the app, and refresh on detail loses place.

Decision: ADR-427 locks this as pre-pilot PWA navigation behavior. Browser refresh and direct URL
open must preserve authorized request detail; Requests breadcrumb/back returns to the request list;
the OpHalo Keep logo returns to the request list/home workbench.

Fix: hash-based routing (`#/request/{id}`) with `pushState` on navigation and `popstate` listener
for back/forward; `getRouteFromLocation()` runs on mount so hard refresh restores authorized detail;
logo and Requests nav both navigate to the requests list; malformed hash and unauthorized/missing
requests remain fail-closed with user-friendly error copy.

### GAP-006 — Staff-created requests cannot add missing service location after creation

**Status:** Resolved (S23)
**Severity:** P1
**Area:** `OpHalo.Keep.Application` request operations; `ophalo-app` request detail / Quick Capture

Service companies may receive requests from external channels where the staff member can create the
request before they know the exact service address. Public customer intake should continue to require
service location, but authenticated business/staff-created requests need an explicit internal
`Service location unknown` path and a post-creation way for permitted staff to add or correct the
service location.

Current gap: once a customer or business-created request exists without a usable service location,
there is no clear operator/admin/owner workflow to add it later. This leaves externally sourced
service requests stuck without required operational context.

Expected fix:
- Keep public intake service-location requirements intact.
- Allow business/staff-created requests to intentionally omit service location via an explicit
  `Service location unknown` / `Add later` path, if that path is not already present.
- Show a request-detail service-location section to authenticated staff. When missing, show an
  internal `Service location needed` cue with an `Add location` action; when present, show the full
  address with an `Edit` action.
- Persist service-location add/edit through an authenticated operation guarded by the same request
  row/action authorization model used for operational mutations.
- Audit service-location changes with actor, timestamp, and a safe changed-field summary.
- Clear the internal `Service location needed` cue when a usable location is saved; allow server
  policy to contribute the missing-location cue to Needs Attention for service businesses.
- Keep service location internal-only: do not show it on unauthenticated customer tracker pages,
  public metadata, or customer-facing share previews.

### GAP-007 — Request-list quick actions lack complete row-level action contract

**Status:** Resolved in S24g2 (2026-07-11)
**Severity:** P1
**Area:** `OpHalo.Keep.Application` request list DTO/API; `ophalo-app` request list quick actions
**Decision:** ADR-435

S24 temporarily converted request-list quick actions into navigation/focus links because
the end-to-end list/action contract is incomplete for executable row actions.

Current finding:

- Backend `KeepRequestSummary` already includes `Version` from `KeepRequest.ConcurrencyVersion`.
- The PWA `KeepRequestSummary` type/mocks must still be checked and wired to consume `version`.
- `KeepQuickAction` currently lacks explicit execution metadata such as `requiresVersion` and
  `executionMode`.

That fallback protects database safety but does not satisfy the request-list product goal. The list
is an action cockpit for high-frequency, low-risk work; forcing owners/admins through
open-detail/action/back loops for every customer update, contact log, internal note, or simple
attention action is a pilot workflow regression.

Expected fix:

- Confirm `version` is serialized by the list API and consumed by the PWA list summary type/mocks so
  list actions can send `X-Keep-Request-Version`.
- Add server-authored action metadata:
  - `requiresVersion`;
  - `executionMode` (`inline`, `modal`, or `detail`);
  - `customerVisible`;
  - `internalOnly`;
  - `clearsAttention`;
  - `changesStatus`.
- Keep the server authoritative for action availability, effects, permissions, and execution mode.
- Convert list-eligible actions from deep links into row-level controls or compact modals after the
  contract is available:
  - send customer update;
  - log external contact;
  - add internal note;
  - assign/self-assign/watch where server metadata allows;
  - clear/acknowledge simple attention where server metadata says it is safe;
  - close request only from Ready to Close with explicit confirmation.
- Keep detail-owned actions as navigation/focus flows:
  - feedback review;
  - cancellation;
  - spam/test/classification;
  - service-location edits;
  - timing controls;
  - generic status changes.

Do not implement inline list mutations by fetching detail in the background as the primary V1
workflow unless the row contract remains blocked after explicit review. The preferred fix is to make
the list payload carry the mutation metadata it needs.

### GAP-008 — Request-list urgency/priority pills lack source and next-action context

**Status:** Resolved in S24h (2026-07-12)
**Severity:** P2
**Area:** `ophalo-app` request list row hierarchy / triage copy
**Decision:** ADR-433, ADR-435

Request rows can show a generic **Urgent** pill when customer intake urgency is urgent and no
internal business priority has been set. In context, this can be confusing for owners/admins because
the row does not answer:

- why this row is urgent;
- whether urgency came from the customer or the business;
- what the next expected staff action is.

This is especially risky because ADR-433 deliberately separates customer-reported intake urgency
from internal business priority. A generic urgent pill can blur that boundary and make customer
urgency look like staff-owned priority.

Expected fix:

- Replace generic urgent/soon row labels with source-aware labels:
  - `Customer signal: Urgent`;
  - `Customer signal: Soon`;
  - `Internal priority: Urgent`;
  - `Internal priority: Soon`.
- When both customer signal and internal priority are present and differ, show both without implying
  they are the same field.
- Keep the row dense; prefer concise labeled chips/text over a large explanatory block.
- Add a compact next-action cue when server quick actions make the next action clear, for example:
  - `Next: update customer or log contact`;
  - `Next: assign owner`;
  - `Next: close request`;
  - avoid inventing next actions when server metadata is ambiguous.
- Preserve the stronger visual treatment for truly actionable states such as `Unassigned`, overdue
  attention, and active attention reason.
- Do not merge customer urgency and business priority.

Acceptance criteria:

- Owners/admins can tell why a row has an urgent/soon signal without opening detail.
- Customer-reported urgency and internal priority remain visually and semantically distinct.
- Rows remain scannable on desktop and mobile widths.
- TypeScript passes.

### GAP-009 — Staff operational signals need why/next-action audit

**Status:** Resolved in S24i (2026-07-12)
**Severity:** P2
**Area:** `ophalo-app` request detail/list signal copy; future native signal copy
**Decision:** ADR-436

GAP-008 resolved the immediate request-list generic urgency pill issue, but ADR-436 now locks the
broader rule: staff-facing operational alerts, urgency signals, priority cues, and needs-attention
states must explain both:

- why the signal is shown;
- what staff should do next.

Request detail already has a strong Needs Attention guidance card with `Why` and `Resolve by`.
However, non-attention detail surfaces still need an audit, especially the side-panel `Triage`
section where customer intake urgency may appear as `Urgent request` without a plain-language source
or next-step explanation.

Expected fix:

- Audit request detail for staff-facing signals that are not active attention:
  - customer signal / intake urgency;
  - internal business priority;
  - service-location missing/needed cues;
  - timing/follow-up cues when they appear as attention-like operational prompts;
  - closeout/readiness cues;
  - feedback review cues outside the main attention card.
- Preserve existing Needs Attention `Why` / `Resolve by` structure.
- For customer intake urgency on detail, prefer copy such as:
  - `Customer marked urgent during intake`;
  - `Customer asked for soon follow-up during intake`;
  - plus concise next-step guidance when server actions make it clear.
- Keep request-list `Next:` cue verbs aligned with visible quick-action button labels.
- Do not invent next actions locally when server metadata is ambiguous; use conservative review copy
  or omit the cue.
- Do not merge customer urgency and internal priority.

Acceptance criteria:

- Request detail non-attention signals do not appear as unexplained alert/urgency pills.
- Request list and detail both preserve ADR-433 source boundaries.
- Existing attention guidance still shows `Why` and `Resolve by`.
- TypeScript passes for any PWA changes.

### GAP-010 — Ready to Close rows leaked communication next-actions

**Status:** Resolved in S24j (2026-07-12)
**Severity:** P1
**Area:** `ophalo-app` request list row actions; `GetKeepRequestListService` quick-action metadata
**Decision:** ADR-434, ADR-435, ADR-436

A screenshot review found a **Work completed** row with no active attention flags still showing:

- `Next: Log contact or Update customer`;
- quick-action buttons for `Log contact` and `Update customer`.

That violated the closeout contract: a resolved/no-attention row in Ready to Close must clearly
surface closeout as the next administrative step instead of making routine communication look like
the primary next action.

S24j fixed the closeout cue and added a neutral `Review closeout` shortcut, but it over-pruned
post-work communication/admin actions. GAP-011 tracks that follow-up correction.

Expected fix:

- For policy-closeable rows, server quick-action metadata should expose `close_request`.
- List `Next:` copy must be derived from the server action metadata and read `Next: Close request`
  when closeout is the available lifecycle action.
- The visible row shortcut should be `Review closeout` / `Open closeout` and use neutral secondary
  styling while `close_request` remains a detail/focus action.
- The destructive `Close request` button belongs on request detail unless a dedicated list-level
  confirmation flow is explicitly implemented.

Acceptance criteria:

- `Next:` cue reads `Close request`, while the row shortcut is clearly phrased as review/navigation.
- Unit and B5 integration tests cover the ready-to-close action payload.
- PWA typecheck passes.

### GAP-011 — External contact logging duplicated and closeout rows over-prune communication actions

**Status:** Resolved in S24k (2026-07-12)
**Severity:** P1
**Area:** `ophalo-app` shared action modals; request list row actions; request action metadata/policy
**Decision:** ADR-434, ADR-435, ADR-436

The request-list **Log external contact** modal and request-detail **Log external contact** modal are
two different experiences. The request-list modal is the preferred product UX:

- title `Log external contact`;
- customer/reference context;
- `Internal record — not visible to customer` badge;
- segmented direction control: `We contacted them` / `They contacted us`;
- channel select;
- outbound-phone outcome select;
- optional note/summary;
- footer actions `Cancel` / `Log contact`.

Request detail still uses a separate older form with different copy, layout, controls, and submit
language. This creates training friction and makes future contact-log behavior drift likely.

The same review also found a product nuance in S24j: **Work completed** with no active attention is
ready for closeout, but it is not communication-dead. A business may still need to log a final call,
send a customer-visible update, or add an internal note before closing the request. The row should
therefore guide closeout while preserving server-allowed post-work communication/admin actions.

Expected fix:

- Extract one shared external-contact log component/form and use it from both request list and
  request detail.
- Use the request-list modal as the source of truth for layout/copy.
- If request detail needs phone copy/call utilities, add them as optional adornments around the shared
  form rather than forking the form.
- For calm work-completed rows:
  - keep `Next: Close request`;
  - preserve allowed `Log contact`, `Update customer`, and `Add note` actions where server metadata
    allows them;
  - include a neutral `Review closeout` shortcut that navigates/focuses request detail closeout;
  - do not show a red/destructive `Close request` list button unless an explicit confirmed list-close
    flow is implemented.
- Review backend action policy before changing behavior. If domain/application rules currently block
  post-work customer updates or external contact on `Resolved`, decide whether to allow them before
  final `Closed`. Keep `Closed`, `Cancelled`, `Spam`, and `Test` protections intact.

Acceptance criteria:

- Request list and request detail use one shared external-contact logging component/form.
- The shared component matches the request-list UX.
- Calm work-completed rows do not lose legitimate post-work communication/admin actions.
- The closeout triage signal remains clear: `Next: Close request` plus neutral `Review closeout`.
- Destructive close execution remains request-detail-owned unless a confirmed list-close flow is
  explicitly implemented.
- Targeted action-policy/list metadata tests are updated if backend policy changes.
- PWA typecheck passes.

### GAP-012 — Closed requests need follow-up-request path, not reopen

**Status:** Fixed — Session 30 / Build 084
**Severity:** P2
**Area:** `ophalo-app` request detail / closed request actions; future Quick Capture/create request flow
**Decision:** ADR-089, ADR-148, ADR-434

Pilot testing raised the closed-request recovery question: when a customer or staff member identifies
more work after a request is already `Closed`, should Owner/Admin reopen the request or copy its
details into a new follow-up request?

Current locked direction says reopen is deferred and `Closed` is a meaningful terminal lifecycle
boundary. Reopen would create semantic churn around close timestamps, customer page state, feedback,
closeout review, metrics, and whether post-close customer feedback becomes part of the old work or
new work.

Expected decision/fix:

- Do not add general `Reopen` in V1 unless a later ADR deliberately changes the lifecycle model.
- Add or plan a `Create follow-up request` action from Closed request detail for Owner/Admin.
- The action should prefill/copy safe customer/request context into the normal business-created
  request flow.
- The new request should link back to the original through an internal note or future relation field.
- Keep the original request Closed and preserve its feedback/closeout history.
- A lighter `Copy request info` utility may be acceptable before a full prefilled-create flow if the
  full flow is too large for the pilot slice.

Acceptance criteria:

- Closed request detail gives Owner/Admin a clear path for new work without reopening the original.
- Original Closed request lifecycle, feedback, and closeout history remain intact.
- Customer-visible pages do not imply the old Closed request has reopened.
- Any copied/prefilled data respects existing public-token and visibility boundaries.

### GAP-013 — Customer feedback submission lacks clear submitted state

**Status:** Fixed — Session 30 / Build 084
**Severity:** P2
**Area:** `ophalo-web` customer tracker feedback form
**Decision:** ADR-135, ADR-136, ADR-139

Pilot testing found that after a customer submits closed-request feedback, the feedback form appears
to disappear without a strong confirmation. The customer should receive an explicit submitted state
so they know the action worked.

Existing product model is binary resolution feedback:

```text
wasResolved=true  -> positive/resolved feedback
wasResolved=false -> negative/unresolved feedback
```

V1 should not add ratings, stars, CSAT/NPS, public reviews, or testimonials. The customer UI should
make the binary choice plain and human:

- `Yes, this was resolved`
- `No, I still need help`

Expected fix:

- Replace the feedback form with a durable submitted state after success.
- Use clear copy such as `Feedback submitted. Thank you.`.
- Include safe supporting copy such as `Your feedback has been shared with {businessName}.`.
- If the returned customer page result includes feedback fields, render the submitted state from
  server state so refresh/direct revisit remains consistent.
- Do not reveal internal attention/review state to the customer.

Acceptance criteria:

- After feedback submit, the customer sees an explicit confirmation instead of an empty/disappearing
  area.
- Refreshing the closed customer page after feedback still shows that feedback was submitted.
- The feedback choice is binary and resolution-oriented, not a rating/review system.
- Error/duplicate/rate-limit states remain safe and customer-friendly.

### GAP-014 — Authenticated request detail does not clearly show submitted feedback

**Status:** Fixed — Session 30 / Build 084
**Severity:** P1
**Area:** `ophalo-app` request detail / feedback review visibility
**Decision:** ADR-151, ADR-263, ADR-271, ADR-384

Pilot testing found that customer feedback is not clearly appearing on authenticated request detail,
or there is no visual indication that feedback has been submitted. Staff need to see the closed
request's feedback state according to role visibility rules.

Expected fix:

- Add or correct a request-detail Feedback card/state.
- Show whether feedback was submitted.
- Show positive/resolved feedback as a quiet completed signal.
- Show negative/unresolved feedback prominently for Owner/Admin review.
- Show `FeedbackComment` only where existing visibility rules allow it.
- Show submitted timestamp when available.
- Show reviewed metadata for reviewed negative feedback.
- Render `Mark feedback reviewed` only when server metadata allows it.
- Preserve the distinction that negative feedback does not reopen the request automatically.

Suggested card states:

| State | Staff display |
|---|---|
| No feedback yet | Quiet `No feedback submitted yet` or omit unless useful. |
| Positive feedback | `Customer marked request resolved` plus optional visible comment where allowed. |
| Negative feedback | `Customer said this was not resolved` plus comment/review action where allowed. |
| Reviewed negative feedback | Reviewed metadata and retained feedback context. |

Acceptance criteria:

- Owner/Admin can see submitted feedback and review state on request detail.
- Negative feedback is visually hard to miss and routes to the existing review action.
- Positive feedback is visible as feedback submitted, without creating false active work.
- Operators/Viewers receive only the feedback metadata/comment visibility allowed by ADR-151/ADR-263.
- PWA typecheck passes.

### GAP-015 — Feedback review lacks a complete operational loop

**Status:** Resolved — commit `315b231` (2026-07-14); verified 2026-07-14
**Severity:** P1
**Area:** `ophalo-app` request list/detail, `ophalo-web` customer tracker, Keep feedback activity
**Decision:** Build 085 locked direction; preserve ADR-135, ADR-263, ADR-264, and ADR-269 behavior

Build 084 made customer feedback visible and preserved the underlying review mutation, but pilot
operation still lacks a consistent review journey: opening an item from Feedback Review does not make
feedback the main task, reviewed negative feedback remains in Utilities, and All Activity has a
review event without a corresponding customer-feedback-received event.

Required fix:

- Opening a row from Feedback Review promotes unreviewed negative feedback above Activity; normal
  request navigation keeps it as a subtly highlighted Utility.
- Owner/Admin acknowledgement remains one-click and removes active negative feedback from both
  surfaces, queue, and count, with a short confirmation.
- Positive feedback remains display-only in Utilities.
- Persist an internal `feedback_received` activity event separately from `feedback_reviewed`.
- Return the saved public feedback comment in the immediate successful submission response as well as
  on a later revisit.

Acceptance criteria:

- Feedback Review opens directly into the focused review state.
- Review clears active feedback presentation without deleting original feedback or reopening work.
- Authenticated All Activity shows separate received/reviewed accountability events.
- Customer recap remains consistent immediately after submission and on revisit.
- Existing authorization, optimistic concurrency, visibility, and public-token boundaries remain
  intact.

## Resolved During Session 22

### GAP-005 — Same-account staff can submit through the public intake form

**Status:** Resolved in S22 (2026-07-09)
**Severity:** P1
**Area:** `OpHalo.Keep.Application` public intake service; `ophalo-web` IntakeForm

Authenticated users who belong to the account that owns a public intake link were able to POST to
`/keep/public-intake/slug/{slug}` and `/keep/public-intake/token/{token}` and create requests
tagged as `public_intake` source. This breaks source integrity and audit semantics — public intake
must represent only customer-submitted requests.

**Fix:**
- `CreateKeepPublicIntakeService.ExecuteWithLinkAsync` now checks `currentUser.IsAuthenticated &&
  currentUser.AccountId == link.AccountId` and returns `keep.public_intake.staff_not_permitted`
  (422) before any database writes. Anonymous users and members of other accounts are unaffected.
- `ErrorHttpMapper` registers the new error code → 422 UnprocessableEntity.
- `IntakeForm.tsx` maps `keep.public_intake.staff_not_permitted` to a staff notice stage: "You're
  signed in to this account — use Quick Capture or Create Request in the app."
- 4 unit tests + 3 integration tests added; all existing tests remain green.

## Resolved During Session 14

### RES-001 — `ophalo-app` AuthGuard redirected to stale `/auth/signin?return_to=...`

**Status:** Resolved in S14f
**Area:** `ophalo-app` auth redirect

S14f changed unauthenticated redirect to `{PublicBaseUrl}/signin` with no `return_to`.

### RES-002 — Homepage customer-start copy became valid after S14g

**Status:** Resolved in S14g
**Area:** `ophalo-web` marketing copy / public intake

Before S14g, "Customers can start a request..." was premature. S14g delivered the public intake page,
so the claim is now backed by a route.

## Minor / Polish / Hardening

### POL-001 — `dev-auth.html` ships in production bundle

**Status:** Resolved in S15b
**Severity:** P2
**Area:** `ophalo-app` production artifact cleanup

`web/ophalo-app/public/dev-auth.html` is copied into every Vite deploy. It adds no new backend
capability, but it is a prototype artifact on a public origin.

Expected fix: exclude it from production builds or remove it after local runbooks no longer need it.

### POL-002 — Settings timezone selector is hardcoded and incomplete

**Status:** Resolved in S15b
**Severity:** P3
**Area:** `ophalo-app` Settings

Settings has a 22-zone hardcoded list that misses common zones such as Phoenix, Honolulu, and UTC,
while `/start` uses the full IANA list. A start-selected zone can later show as un-reselectable
`(custom)`.

Expected fix: reuse the `Intl.supportedValuesOf` approach.

### POL-003 — Manual-share invite URL and clipboard-copy polish

**Status:** Resolved in S15b
**Severity:** P3
**Area:** `ophalo-app` Settings / share sections

Manual-share invite URL stays rendered until unmount, though S13g said show once. Clipboard-copy
failures in share sections are silently swallowed in the `DOMException` branch.

### POL-004 — Composer draft is split across duplicated mobile/desktop mounts

**Status:** Resolved in S15b
**Severity:** P3
**Area:** `ophalo-app` request detail composer

Duplicated mobile/desktop mounts mean a draft typed at one viewport width may disappear after
resizing.
