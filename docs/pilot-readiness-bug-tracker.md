# Pilot Readiness Bug And Gap Tracker

**Created:** 2026-07-02
**Purpose:** Live tracker for pilot-blocking or pilot-relevant bugs/gaps discovered during Session 14.
**Source:** Promoted from the Pre-S14e bug register in `docs/build-log/068-session-14-ophalo-web-front-door.md`.
**Current active items:** GAP-016 through GAP-019 — New Request launch blockers and a Request
Detail maintainability seam found during
Build 086 manual verification on 2026-07-16. Build 087 is paused until they are triaged and the
selected fixes are complete.
**Recently resolved:** GAP-015 — feedback review operational loop and accountability trail (commit `315b231`); GAP-004 — durable PWA request-detail routing (commit `3ebdc57`).
**Previously resolved:** GAP-010 — Ready to Close rows leaked communication next-actions (S24j).

This document is the current working tracker. Historical discovery notes stay in the build logs, but
triage, status, and next-session ordering should happen here.

As of S15c, all earlier active pilot-readiness bugs/gaps in this tracker were resolved. The active
New Request items below were found during the subsequent V1 public-use audit and supersede the
previous assumption that deployment smoke testing was the next step.
Detailed historical discovery notes and their resolved status remain below for traceability; only the
Active Launch Blockers section controls current work.

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

## Active Launch Blockers — New Request

### GAP-016 — New Request accepts invalid phone numbers and traps correction

**Status:** Needs decision
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

**Decision required:** Confirm North American 10-digit phone validation for launch, or define the
international phone/country-selection model before implementation.

### GAP-017 — Staff New Request cannot capture service location at creation

**Status:** Open
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

**Status:** Needs decision
**Severity:** P1
**Area:** `ophalo-app` Quick Capture; public-intake link setup; secure SMS handoff

**Verified cause:** A durable public customer-request page already exists in Settings, but it is not
available from New Request. The current drawer begins with internal phone lookup and staff capture,
even though staff entry should be the fallback when the customer cannot submit the request.

**Locked hierarchy:** Customer self-service is the default New Request path. **Enter request for
customer** is the clear secondary, last-resort path.

**Proposed solution:**

- For Owner/Admin, lead New Request with **Let the customer submit their request** and expose the
  existing durable business public-intake link through **Copy link**, an in-person **Show customer QR**, and
  **Text customer from your phone**.
- The desktop text action uses an opaque, short-lived SMS-handoff URL in a QR code. The owner scans
  it with their phone; the phone opens a pre-addressed SMS composer containing the public intake
  link. Mobile opens that composer directly. Keep does not send SMS, and neither recipient phone nor
  message appears in the QR payload.
- The in-person QR may encode the durable public intake slug because it carries no customer data.
- The public-intake link is not the private customer request page. A private page is minted only
  after a request exists; it cannot be the pre-capture handoff. Operators retain the staff-entry
  fallback and do not receive link-management controls.
- Do not present the public form as a staff-entry action: same-account authenticated staff are
  deliberately blocked from posting public intake. It is a customer handoff/preview only.

**Decision:** ADR-442 locks the public-intake handoff as the primary New Request route.

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
