# Pilot Readiness Bug And Gap Tracker

**Purpose:** The live, forward-looking backlog for unresolved pilot-readiness work.

**Last triaged:** 2026-09-01

Historical findings, resolved work, and superseded implementation notes were removed from this document. They remain in [the session log](session-log.md) and the relevant `docs/build-log/` records. A tracker item belongs here only while it has remaining work or an unresolved decision.

## Status Legend

- **Open:** Decision is sufficiently clear; implementation has not started.
- **In progress:** A bounded implementation is underway; the stated remainder is still required.
- **Reopened:** A prior remedy was incomplete or superseded.
- **Needs decision:** Product direction must be locked before implementation.

## Tomorrow's Launch Gates

These are not an instruction to ship every remaining item before a supervised pilot. They define the conditions that must be satisfied before enabling the relevant workflow.

| Item | Gate condition |
| --- | --- |
| GAP-039 | Required before any customer-facing production pilot. |
| GAP-033 | Required before enabling public customer intake. |
| GAP-048 | Required before using email to share private customer request pages. |
| GAP-047 | Required if staff relies on Internal priority for operational triage. |
| GAP-016 / GAP-021 | Required before native use or accepting common `+1` phone entry in Quick Capture. |
| GAP-049 | Required before relying on follow-up creation from closed requests. |

## Implementation Order

Complete each numbered slice with focused automated coverage and a production-candidate/manual check where applicable. Do not start a dependent slice before its prerequisite is accepted.

1. **Release safety and truthful public entry:** GAP-039, GAP-033, then GAP-040. Establish safe production observability and configuration validation first; make the public request journey and published claims truthful second. (GAP-056 customer SMS/QR handoff sender/business context — resolved, commit `0fc7a2a`.)
2. **Field-work correctness:** No active item. (GAP-055 Actual Work recorder ownership — resolved across Batches A–D: migration/ownership `b3b3d41`, recorder authorization `d26b955` and `72ce6a5`, audited transfer `c7ce822`, and Owner/Admin recovery UI `de40491`.)
3. **Phone and capture integrity:** GAP-016, GAP-021, GAP-051, then GAP-025. Consolidate the ADR-444 normalization path before extending fallback customer recognition.
4. **Request Detail foundation and correctness:** GAP-019, GAP-058, GAP-059, then GAP-047, GAP-048, GAP-049, and GAP-063. First establish shared responsive seams without behavior change; then make Owner/Admin review, lifecycle, attention, and timing actions unmistakable. See [BL137](build-log/137-request-detail-and-queue-usability-handoff.md) for the bounded execution order.
5. **Request workspace next:** GAP-027 and GAP-045 are resolved. Implement GAP-067 first as the presentation-only Request List/Detail foundation, after a brief read-only GAP-042 business-identity placement preflight; then implement GAP-042, GAP-041, GAP-046, GAP-043, GAP-044, GAP-026, and GAP-053. The row grammar remains locked: one lifecycle cue, one server-ranked exception cue, and one next-action line. GAP-067 must preserve that grammar, server ranking, and existing behavior; do not merge a broad queue redesign into it. (GAP-057 empty-Attention fallback and truthful state; GAP-060 Views-menu off-screen clipping; GAP-061 queue/detail synchronization — resolved in `0cfb335`.)
6. **Pilot operating loop and final usability review:** GAP-064 (after GAP-039), GAP-037, GAP-038, and GAP-054. Establish a reliable new-customer-request alert path before relying on intake to create live work, then deliver the founder's evidence/reporting loop, a fail-soft feedback route, and a final role/device navigation review.

## Active Work

### GAP-039 — Production failures and pilot health are not observable enough to earn trust

**Status:** Open
**Severity:** P0
**Area:** Production reliability and internal product operations

**Locked decision:** Use **Sentry** for redacted server and browser error capture, with release/environment identity and **founder email** as the initial alert destination. Do not enable session replay, broad behavioral analytics, a data warehouse, or an owner-facing analytics dashboard in this pilot slice.

**Implementation order:**

1. Define and test the telemetry boundary: permit only release/environment, server-generated correlation ID, safe route/status/error metadata, and narrowly justified account-level identifiers. Scrub or reject capability URLs/tokens, request text, service addresses, phones, emails, authorization headers, cookies, sessions, and broad request bodies.
2. Add the Sentry ASP.NET integration in production configuration. Attach the existing `ReleaseIdentity` and correlation ID; capture unhandled server failures without changing safe ProblemDetails responses. Retain `/health/live` and `/health/ready` as opaque availability/readiness signals.
3. Add the Sentry React integration to the authenticated PWA with release/environment identity. Validate `VITE_PUBLIC_BASE_URL` at startup/build time and make missing or malformed configuration fail safely rather than allowing request-detail `.replace()` calls to throw. Do not add session replay.
4. Configure Railway health checking against `/health/ready`, Sentry environment/DSN/release configuration, and the founder-email alert rule. Record a short runbook: inspect release/correlation ID, check health and Railway logs, decide mitigation versus rollback, and record the incident.
5. Verify a production candidate with controlled server and browser failures, normal/unhealthy health responses, an invalid public-base URL, alert delivery, release identity, and automated redaction checks for representative PII and public-token paths.

**Done when:** Controlled server and browser failures arrive in Sentry with useful release/correlation context and no protected data; founder email alerting works; readiness/availability monitoring is verified; invalid required public configuration fails safely; and the runbook is usable by the founder.

### GAP-033 — Public intake does not establish sufficient customer trust or return continuity

**Status:** Open
**Severity:** P1
**Area:** `ophalo-web` public intake

Before asking for customer address/contact data, show business identity and configured public contact information when available; place factual privacy/use disclosures before relevant fields; keep email visible and optional; take a successful submission directly to its private tracker; and provide a real privacy-policy link. Public copy must not promise automatic tracker-link email, verification, or unsupported security properties.

### GAP-040 — Marketing site does not accurately represent the current product or launch posture

**Status:** Open
**Severity:** P1
**Area:** `ophalo-web` marketing and legal/support routes

Audit public routes, copy, images, links, metadata, and deployment behavior against shipped V1. Remove unsupported automatic-email, SMS, verification, response-time, revenue, or security claims; use representative non-private visuals; and verify desktop/mobile, keyboard, and production-host behavior.

### GAP-062 — Assembly editor drifts from the Price Book workspace and hides item identity

**Status:** Resolved — commit `07b7ea8` (clarity copy `f34292c`)
**Severity:** P1
**Area:** Price Book Offering/Assembly detail and edit form

The Offering/Assembly editor uses a narrow, one-off `max-w-2xl` layout while the wider Price Book workspace is available, making it feel unlike the rest of the product's operational forms. Its associated-item rows truncate catalog item names to protect quantity, optionality, and remove controls; an Owner/Admin cannot reliably see which item they are editing.

**Locked resolution:** Recompose Assembly Detail as a Price Book workspace form using the established page shell and an intentional wide content region. At desktop widths, associated-item rows must use a stable grid/column layout that gives the item identity the dominant flexible column and keeps quantity, optionality, and destructive action legible. At narrow widths, controls may stack below the identity, but the complete catalog item display name must wrap and remain visible. Remove name truncation from editable associated-item rows; do not replace it with hover-only title text or an overflow/ellipsis workaround.

**Acceptance criteria:**

- The Assembly Detail page aligns visually with the application's established workspace/form hierarchy rather than occupying an arbitrary narrow strip of canvas.
- Every associated catalog item name is fully readable at supported desktop, narrow PWA, and browser-zoom widths, including long names.
- Quantity, the Optional control, and Remove remain visibly associated with the correct item, keyboard reachable, and do not cause horizontal overflow.
- The optional-component explanatory copy and base-price note remain readable without becoming the dominant visual element.
- Focused frontend coverage includes a long item name and layout/accessible-name regressions; TypeScript, build, and CSS-token checks remain clean.

**Resolution:** `OfferingAssemblyDetail` now renders in the shared `mx-auto w-full max-w-[1440px] px-4 sm:px-6` workspace wrapper with a `max-w-4xl` content column. Associated-item rows use a `sm:grid` with a wrapping `minmax(0,1fr)` identity column and intrinsic-width Qty/Optional/Remove columns, stacking name-above-controls below `sm`; name truncation and title-tooltip fallback removed. Regression tests cover a long item name and the workspace width. TypeScript, build, and CSS-token checks clean.

### GAP-016 — New Request phone validation and correction path remains incomplete

**Status:** In progress
**Severity:** P0
**Area:** Quick Capture, authenticated request API, and native parity

Finish the ADR-444 normalized ten-digit North American policy across native and all client paths, including leading `1`/`+1`, correction from capture, and consistent actionable validation.

### GAP-021 — Quick Capture rejects valid country-code input

**Status:** Open
**Severity:** P1
**Area:** `ophalo-app` Quick Capture lookup

Normalize an 11-digit value beginning with `1` to its final ten digits before the UI gate, lookup, and return-to-draft path.

### GAP-051 — Phone formatting remains incomplete outside the authenticated PWA

**Status:** In progress
**Severity:** P1
**Area:** Native and public phone input/display

Authenticated PWA staff-facing formatting is complete. Finish native parity and the public-web audit, including tolerant `1`/`+1` input and formatted configured business-phone display, while preserving canonical stored values and `tel:`/API behavior.

### GAP-025 — Quick Capture hides request-phone-only customer continuity

**Status:** Reopened
**Severity:** P1
**Area:** Quick Capture identity lookup

Implement ADR-492: retain canonical-customer matches, but render a request-phone-only hit as an explicit possible existing customer with up to three active-request cards and a clear choice to open, reuse, or create new. Never auto-select, link, navigate, or silently backfill; preserve exact, account-scoped normalized lookup and the no-candidate-cap protections.

### GAP-019 — Request Detail needs durable shared responsive seams before further behavior changes

**Status:** Resolved — RD-019A (`ophalo-app`), behavior-preserving composition-seam extraction.
`RequestDetailContent` is now a coordinator delegating to `useRequestDetailLayout` (both width
rules + rail focus), `RequestDetailWorkCanvas` (layout-only canvas structure/order),
`RequestDetailActualWorkSection` (Actual Work region from injected state + callbacks), and
`RecordDetailsSection`. No API/DTO/authorization/mutation-policy/lifecycle/attention change.
**Severity:** P1
**Area:** `ophalo-app` Request Detail architecture

**Locked resolution:** Keep one **page-level coordinator** for authoritative request-detail state,
cache synchronization, navigation, overlays, and cross-feature policy. Shared feature controllers
may own bounded local form state, mutations, retry snapshots, and conflict handling, provided that
they consume the authoritative detail/version and return the authoritative replacement detail to the
page coordinator. Desktop and mobile composition must never implement business behavior separately.

Extract thin desktop and narrow/mobile composition wrappers plus coherent shared canvas regions.
Preserve the distinct viewport-width Actual Work workspace-route rule and container-width Request
Detail layout rule; they serve different purposes and must not be collapsed into one heuristic.
This slice is behavior-preserving: no visual redesign, API/DTO change, authorization change,
mutation-policy change, or changed lifecycle/attention semantics.

### GAP-058 — Actual Work review and request-completion actions compete on Request Detail

**Status:** Resolved — RD-058A (`c5796e0`), RD-058B-1 (`2ae07d5`), RD-058B-2 (`8e3127d`, confirm-dialog fix `85a1a57`)
**Severity:** P1
**Area:** Request Detail Actual Work review and lifecycle action hierarchy

**Progress:** The read-only Actual Work Review queue projection carries the factual request
lifecycle status; a row states both **Request: {lifecycle state}** and **Submitted visit awaiting
internal financial review** (RD-058A, commit `c5796e0`). RD-058B-1 reframed the review card as
**Internal financial review** with the persistent sub-line "Reviews the submitted visit's financial
details. Does not change the customer request.", renamed the action to **Complete internal financial
review**, relabelled per-visit state as **Financial review pending** / **Financial review
completed**, and made both success surfaces (Request Detail canvas banner and the wide-viewport
Actual Work workspace route) announce "Internal financial review completed. The customer request
status is unchanged." RD-058B-2 completed the action hierarchy: during active attention the
server-authored attention-resolution action is the only dominant action; the standalone Anchor
**Contact customer** action is removed unconditionally (contact stays in Customer Contact / a
server-routed contact-sheet primary); the non-primary alternate reads **Resolve another way…** and
opens the Why/Resolve-by guidance disclosure; **Mark work done** moved from the Anchor to a quiet
"Request lifecycle" block in the Work Canvas after Actual Work and before the composer (desktop and
mobile), still gated on the server-provided secondary authorization; both **Mark work done**
controls (and **Close request**) confirm through one focused `MutationConfirmDialog` — title
"Mark request as Work completed?", the full advisory in a constrained body ("Work completed · no
customer notification · no internal-review completion · attention/open draft unresolved"), Cancel
focused on open, Escape restores focus to the trigger, page not re-laid-out — replacing the inline
row that had expanded the Anchor and displaced the request identity; and the Anchor inner card is
bounded to `max-w-4xl mx-auto` to share the Work Canvas reading frame.

When a request is in **Actual Work Review**, the page simultaneously presents the request-level **Mark work done** action and the review-card **Mark visit reviewed** action. The request can still show an early lifecycle state such as **Received**, making it unclear whether the operator is reviewing recorded work, completing the customer request, or expected to do both. A mistaken completion can change the customer-facing lifecycle before the required financial review is complete.

**Locked resolution:** Make the two facts visually and semantically separate.

- Extend the read-only Actual Work Review queue projection with the factual request lifecycle status.
  A row states both **Request: {lifecycle state}** and **Submitted visit awaiting internal financial
  review**; a `Received` request must never imply that it has advanced simply because a visit awaits
  review.
- Rename the card action to **Complete internal financial review** and place persistent copy on the
  card: it reviews the submitted visit's financial details and **does not change the customer
  request**. On success, announce that internal financial review completed and request status is
  unchanged.
- Retain server-authored **Mark work done** for the request lifecycle. With active attention, it is
  a quiet, contextual lifecycle action below the attention and Actual Work/communication context,
  not a competing anchor action. Its confirmation must state that it marks the request as Work
  completed, does not notify the customer, does not complete internal financial review, and, where
  applicable, leaves attention or an open Actual Work draft unresolved.
- The attention-resolution action is the sole visually dominant action while attention is active.
  Channel-specific Call/Text/Email/Share actions remain in Customer Contact; do not duplicate a
  large `Contact customer` action in the anchor. A non-primary authorized alternate path is labelled
  **Resolve another way…**, not `Clear attention`, and must expose the server-authorized guidance.
- Align the Request Anchor and Work Canvas to one shared horizontal content boundary; keep the
  compact planning row in the anchor.

Do not hard-block request completion, couple completion to review, invent a client lifecycle policy,
or change server lifecycle authority as a presentation fix.

**Acceptance criteria:**

- An Owner/Admin can distinguish financial-review completion from customer-request completion before acting.
- A visit-review action cannot be mistaken for, or silently cause, a request status change; a request-completion action cannot be mistaken for review.
- Desktop/mobile, keyboard focus order, permission variants, and the `Received` plus actual-work-review state have focused regression coverage.
- The review queue, Request Detail, and confirmation copy distinguish request lifecycle, submitted
  visit, internal review, customer notification, active attention, and open-draft facts without
  implying that one action changes another.

### GAP-059 — Planned-work and internal-follow-up controls look disabled or unreadable

**Status:** Resolved — RD-059A (`cf9adaf`)
**Severity:** P1
**Area:** Request Detail schedule and follow-up controls

RD-059A applied the locked resolution to `TimingPanel` (Anchor `strip` row plus the
full-card and `bare` variants). Persistent labels are **Internal priority**, **Planned work date**,
and **Internal follow-up (optional)**. Enabled empty controls now read **Set planned date** and
**Set follow-up date** in normal-contrast ink with a leading calendar cue and no placeholder
ellipsis (previously low-contrast `Set planned work date…` / `Set internal follow-up…`). The
restrained configuration checkmark shows only for the current Internal priority selection
(including default Routine) and a persisted Planned work date; it never appears for an empty planned
date or the optional follow-up. Read-only values drop chevron/hover/button semantics and carry a
visible muted **Read only** caption. Keyboard: Enter/Space opens an editor and focus moves to its
first field; Escape (`preventDefault` + `stopPropagation`) and Cancel close it and restore focus to
the trigger; one-open-editor behavior is preserved; save and 409-conflict errors stay in the
relevant editor with `role="alert"` and the conflict path keeps the editor open with the field
disabled. Existing date/reason validation and mutation/version/conflict policy are unchanged; no
server or policy change. Coverage: new `TimingPanel.strip.test.tsx` (strip + full-card keyboard,
error, conflict, loading, empty-copy/contrast, checkmark, read-only), extended
`DetailPanels.priority.test.tsx` and `RequestDetailAnchor.test.tsx`. Full frontend suite 977
passed; tsc / `check:tokens` / `vite build` / `git diff --check` clean; desktop, narrow PWA,
keyboard, and browser-zoom evidence captured.

The custom disclosure buttons that open the **Planned work date** and **Internal follow-up** date
editors use placeholder-like low-contrast text and a weak affordance. In the observed Request
Detail state, they visually read as unavailable rather than actionable controls, making a core
scheduling/follow-up path easy to miss.

**Locked resolution:** Preserve the compact three-field planning row and existing mutation policy,
but distinguish an enabled disclosure button from a read-only value without relying on color.

- Persistent labels are **Internal priority**, **Planned work date**, and **Internal follow-up
  (optional)**. A checkmark is a restrained configuration cue, not a request-completion signal:
  show it for the current Internal priority selection (including the default Routine) and for a
  persisted Planned work date; do not show it for an empty planned date or optional follow-up.
- Enabled empty controls read **Set planned date** and **Set follow-up date** in normal-contrast text,
  with calendar/disclosure cues and no placeholder ellipses.
- Read-only values have no chevron/hover behavior and expose a visible **Read only** cue.
- Enter/Space opens an editor and focuses its first field; Escape closes it and restores focus to its
  trigger; normal Tab order reaches every form action. Errors remain associated with the relevant
  editor and are announced.

**Acceptance criteria:**

- At normal desktop and mobile widths, an Owner/Admin can identify both controls as available,
  distinguish configured priority/planned work from an empty value, and understand that follow-up
  is optional before opening an editor.
- Empty text, selected values, focus, hover, read-only, validation, loading, and mutation-error
  states meet the established contrast and accessibility treatment.
- Focused PWA coverage verifies keyboard open/focus/Escape/restore behavior and that enabled empty
  controls are not rendered with disabled semantics or appearance.

### GAP-047 — Internal-priority updates can fail silently on Request Detail

**Status:** Open
**Severity:** P1
**Area:** Request Detail triage mutation

Surface associated failure feedback for transport/API failures and stale-version conflicts. A failed or conflicted priority change must not appear saved; require refresh before further stale mutations.

### GAP-048 — Emailing a private request page bypasses deliberate share intent

**Status:** Open
**Severity:** P1
**Area:** Request Detail customer email/share path

Route email containing a private tracker through the explicit share workflow. Opening `mailto:` is not proof of delivery; only an informed owner confirmation records sharing. Preserve token secrecy, plain-email capability, and truthful `Needs Share` state.

### GAP-049 — Closed-request follow-up prefill can exceed the description limit

**Status:** Open
**Severity:** P1
**Area:** Request Detail follow-up creation

Reserve space for the provenance prefix and safely truncate copied source text so maximum-length closed requests can start a valid follow-up without changing the original record.

### GAP-063 — Owners and Admins cannot classify a request as Spam or Test in Request Detail

**Status:** Open
**Severity:** P1
**Area:** Request Detail Owner/Admin lifecycle controls

The server already supports an auditable, terminal Spam/Test classification through
`POST /keep/requests/{id}/classify` and returns the authoritative `availableActions.canClassify`
permission flag. Request Detail does not expose that authorized action, so staff cannot remove a
known spam submission or intentional test request through the product.

**Locked resolution:** For an Owner or Admin on an active request with `canClassify`, expose a
secondary lifecycle action offering **Mark as spam** and **Mark as test**. Require a clear,
accessible confirmation before submit because the classification is terminal; allow an optional
internal reason (maximum 500 characters). Replace authoritative detail with the response and show
the resulting terminal status and existing internal timeline event. Do not expose the action to
Operators/Viewers, send customer notification, alter the server authorization/state policy, or
provide unclassification/reopen from the client.

**Acceptance criteria:**

- Owner/Admin users can classify an eligible request as Spam or Test after confirmation; the UI
  refreshes to the server-returned terminal state.
- The action is absent for ineligible roles and terminal requests, including when a stale client
  view says it is available.
- The reason field, confirmation, API/transport failure, and stale-version conflict states are
  keyboard accessible and provide clear feedback without implying a successful mutation.

### GAP-027 — Request-list alerts compete and lifecycle state is hard to scan

**Status:** Resolved (Q-027A, `8ced025`) — the locked row grammar (one quiet lifecycle cue, one
server-ranked exception cue, one next-action line) was already in place; the remaining defect was
that non-overdue priority/urgent work rendered red. `RequestRow.severityToTone` now reserves the
red tone for server severity `"danger"` (genuine overdue/high-risk) and renders `"priority"` amber.
Server ranking/severity, the one-exception limit, terminal suppression, and quiet planned/future
timing are unchanged; selection stays visually distinct from severity; Office Review remains a
separate surface.
**Severity:** P1
**Area:** Request-list row hierarchy and lifecycle presentation

**Locked resolution:** Every Request row uses one compact grammar: one quiet lifecycle cue, at most
one server-ranked exception/attention cue, and one factual next-action line. Selection state is
independent of severity; do not make selected blue and alert red compete as equal row borders.
Reserve red for genuine overdue/high-risk work, keep planned/future timing quiet, and suppress
ordinary SLA/follow-up alarms for terminal work while retaining the approved unresolved-feedback
exception. The queue count and visible row urgency remain server-authoritative.

The Owner/Admin primary queue controls remain Attention, All work, and Mine; Office Review stays
separate from customer-promise risk. Implement this after GAP-019/058/059, with no client-side
re-ranking or broad queue redesign folded into the Request Detail slices.

### GAP-045 — Default Queue language does not explain work scope or prioritization

**Status:** Resolved (documentation-only) — the shipped UI already satisfies the substance. The
Owner/Admin primary tab is titled **All Work** with the supporting subtitle "Open requests and
feedback requiring review, ranked with customer promises needing attention first." Server
queue/ranking authority is unchanged. This landed with the attention-first landing work (GAP-057);
no further UI change is warranted.
**Severity:** P1
**Area:** Request-list orientation

Intent: replace implementation language with the locked Owner/Admin label **All Work** and clear
supporting copy explaining that open requests and review work are ranked with customer promises
needing attention first, with server queue/ranking authority unchanged.

The controlling decision is **UI-004 (production, 2026-08-21)**: title-case **All Work** plus that
exact subtitle. The earlier ADR-449 (2026-07-25) used lowercase "All work"; the tracker inherited
the stale casing — it is not a product gap. Do not change labels, copy placement, server ranking,
or navigation. UI-004's Office Review discoverability requirement is out of scope here and stays
with GAP-065.

### GAP-042 — Authenticated request work lacks visible business identity

**Status:** Open
**Severity:** P1
**Area:** Request List and Request Detail context

Add restrained, fresh business-name context to authenticated list/detail views without competing with the request/customer, duplicating stale labels, or exposing account identity publicly.

### GAP-041 — First queue selection blanks the work area

**Status:** Open
**Severity:** P1
**Area:** Request-list loading and queue tabs

Keep queue context and list geometry stable during first fetch, use an appropriate loading treatment, and complete tab keyboard behavior without showing prior-queue rows under a new label.

### GAP-046 — Request search and filters lack visible applied-state and recovery

**Status:** Open
**Severity:** P2
**Area:** Request-list search/filter accessibility

Show applied criteria, an accessible result/status announcement, and a clear/reset path. Preserve deliberate-submit search, cursor/query binding, and accurate cursor-page count language.

### GAP-043 — Request-list scale behavior is not a verified operating experience

**Status:** Open
**Severity:** P1
**Area:** Cursor pagination and scale UX

Make and document a V1 scale decision from representative pilot data. If retaining the cursor model, make page transitions, older/newer work, focus, end state, and result context clear without adding misleading offset/numbered pagination or infinite scroll.

### GAP-044 — Completed and cancelled work is not discoverable in the PWA

**Status:** Open
**Severity:** P1
**Area:** Request history access

Expose the existing authorized closed/cancelled/all-history API views through a clear PWA path. Keep active and terminal contexts distinct and preserve roles, protected cursors, filters, and detail-back navigation.

### GAP-026 — Request-list search has no clear affordance

**Status:** Open
**Severity:** P2
**Area:** Request-list search

Add an accessible clear control that restores the selected queue's unfiltered list by keyboard or pointer. Deliver with GAP-046 rather than as a separate interaction pattern.

### GAP-053 — Needs Attention reverses canonical row communication action order

**Status:** Open
**Severity:** P2
**Area:** Request-list row actions

Render **Update customer** before **Log contact** whenever both actions are allowed, including Needs Attention, Open Work, and narrow layouts. Share the ordering rule and cover visual plus focus order.

### GAP-065A — An active Actual Work Draft no longer hides prior submitted visits (UI slice)

**Status:** Resolved (`4fbda15`)
**Severity:** P1 (narrow UI slice of GAP-065)
**Area:** Request Detail — Actual Work section

`RequestDetailActualWorkSection` now renders the submitted `ActualWorkHistoryCard` whenever visit
history has content or errored, even while the current Actual Work capture state is an editable
Draft. The no-filler Draft behavior is preserved (empty history still renders nothing). Review
routing is unchanged: on a wide viewport each prior submitted visit still exposes **Open in
workspace** and routes with that exact visit ID; narrow screens keep the inline review card and add
no workspace route. Owner/Admin financial-review authorization is not broadened. Coverage:
`RequestDetailActualWorkSection.test.tsx` (6 focused tests); `src/pages/request-detail` suite 425
passed; tsc / `check:tokens` / `vite build` / `git diff --check` clean.

The broader GAP-065 queue **Internal review pending** cue, the persistent Office Review navigation
affordance, and the server-authoritative projection remain **Needs decision** below.

### GAP-064 — A new customer request can arrive without reliably alerting accountable staff

**Status:** Needs decision
**Severity:** P1
**Area:** Public intake and staff notification reliability

An authenticated business-created request is already known to the staff member who entered it, but a
customer-originated public-intake request can be created without a reliable, timely alert to an
accountable Owner/Admin. The current desktop QR and mobile `sms:` patterns are **manual customer
contact handoffs**: they open the submitting operator's phone/Messages app and neither send nor
prove delivery to a staff recipient. They cannot be the primary safeguard against an unseen job.

**Decision required:** Define the smallest reliable staff-alert policy before implementation:

- Which customer-originated events require an immediate alert (at minimum, a newly created public
  request), which role or responsible person is the accountable recipient, and how Owner/Admin
  fallback works when that person is unavailable.
- Whether the pilot's primary channel is real device push, provider-delivered internal SMS, or a
  deliberately configured combination; define delivery failure, retry, and escalation rather than
  treating a launched native app as delivery.
- If automated internal SMS is selected, establish verified staff phone enrollment, explicit opt-in
  and opt-out handling, recipient de-duplication, after-hours/quiet-hours policy, message content
  minimization, provider cost/credentials, durable delivery attempts, and a safe fallback channel.
- Whether a desktop QR/mobile SMS-compose action is retained only as an optional **manual
  escalation** after the request is saved. It must identify the actual sender and recipients, require
  a deliberate send, and never claim that all Owners/Admins were notified.

**Done when:** A public-intake submission has a durable, privacy-safe routed-alert record and a
verified pilot path that reaches its accountable staff recipient or produces an actionable failure/
escalation state. The request list/badge remains the authoritative backlog; a manual QR or native
SMS launch is supplementary only. Coverage proves recipient selection, actor exclusion,
mute/eligibility/off-season behavior where applicable, duplicate suppression, failure handling,
and that no customer data beyond the minimum notification payload is exposed.

### GAP-065 — Owner/Admin internal financial-review work is hard to discover from requests

**Status:** Open
**Severity:** P1
**Area:** Request List, Office Review navigation, and Actual Work review context

**Implementation contract:** [BL138](build-log/138-gap-065-owner-admin-financial-review-discovery-and-delivery-plan.md)

**Locked product boundary:** A submitted Actual Work visit is an immutable, dated field record;
it is never merged with another visit merely because both belong to the same request or service
account. Financial review is also per visit, preserving the submitted price/cost snapshots,
performer attribution, visit notes, correction/supersession history, and audit trail. A future
customer charge may group one or more reviewed visits, but that billing/invoice grouping is not a
current Keep entity or scope.

**Locked workflow rule:** **submitted and unreviewed is an active office task; reviewed is
history.** A current Actual Work Draft is not review work, but it must never hide an earlier
submitted/unreviewed visit.

**Phased resolution:**

1. **Request Detail direct entry (first implementation slice).** For Owner/Admin only, promote
   every submitted, unreviewed visit into a factual **Pending financial reviews (N)** task card in
   the Actual Work region, ahead of passive Visit history. Each visit gets its own row with
   submitted timestamp, technician/recorder where truthfully available, line count, and a direct
   **Review financials** route to that exact workspace visit. Use **Ready to review** for
   financially complete-but-unreviewed work, **Needs cost/price resolution** when applicable, and
   reserve **Review complete** for a visit with `reviewedAtUtc`. Keep reviewed and superseded
   records in passive history. The completed GAP-065A history fix remains in force.
2. **Request-scoped workspace continuation (second slice).** Preserve the exact-visit deep link,
   but add a compact Owner/Admin pending-visit switcher in the wide financial-review workspace.
   It lists the outstanding submitted visits for this request, with the selected visit explicit.
   After a successful review, show confirmation and **Review next pending visit** plus **Back to
   request**; never auto-navigate. Switching with a dirty reviewer note or open financial
   resolution input requires a discard confirmation. Reviewed visits may be available in a
   secondary audit section, but pending visits are dominant.
3. **Queue discoverability (separate preflight/implementation slices).** Add a quiet,
   server-authoritative Owner/Admin request-row count cue such as **2 visits need review**. A
   normal request-row click continues to open Request Detail; it must not unexpectedly jump to a
   financial workspace. The existing account-wide Office/Actual Work Review destination should
   become a clearly named, persistent destination with a truthful empty state. A later dedicated
   cross-request review queue may offer one row per pending visit and a direct Review action, but
   requires its own query, authorization, ranking, and empty-state preflight.

**Guardrails:** Do not show review work for a Draft, reviewed or superseded visit, an
Operator/Viewer, or a terminal request with no unreviewed submitted visit. Do not change request
ranking, queue counts, attention severity, lifecycle status, financial-review authorization, or
the server's review gate as a presentation fix. Any request-row cue/count must come from a
server-authoritative projection, never client inference from lifecycle, Draft, or history data.
Do not introduce a generic “Review all” action until its eligibility, per-visit reviewer-note
semantics, resolution handling, and audit evidence are separately locked.

**Done when:** An Owner/Admin can open Request Detail once, enter any outstanding visit directly,
review one visit, and deliberately continue to the next outstanding visit without returning to the
request list. The request-level review signal remains active until every submitted, unsuperseded
visit is reviewed. Queue discovery is clear but remains distinct from customer-promise attention.
Focused coverage proves multiple pending visits, reviewed/superseded visits, draft-plus-prior
visit, role, dirty-switch, success continuation, deep-link, empty-state, and server-authoritative
cue/count behavior.

### GAP-066 — Catalog Item detail is not yet a usable financial and operational-impact workspace

**Status:** Open
**Severity:** P1
**Area:** Price Book Catalog Item detail

The existing Catalog Item page has the correct item, price/cost, profitability, alias, edit, and
inactivation behavior, but presents them as a narrow sequence of raw fields on the canvas. It does
not match the financial-review workspace's clear hierarchy, and it gives an Owner/Admin no
at-a-glance answer to the operational question: where will changing or inactivating this item have
an effect?

**Locked direction:** Treat Catalog Item detail as a financial workspace. The visual order is
**item identity → economics → discoverability → operational impact**. Price Book uses the same
cool financial-workspace canvas as Actual Work financial review; request communication/data keeps
its distinct request canvas. Preserve existing mutation, price-version, alias, inactivation,
conflict, entitlement, and authorization behavior.

**Phased resolution:**

1. **Existing-data presentation slice.** Recompose the Owner/Admin page into a wide responsive
   workspace: item identity/status and action hierarchy in the header; an **Economics &
   profitability** card with Sell price, Direct cost, Gross profit, Margin %, and secondary Markup
   %; and a dedicated **Search aliases** card. `Update pricing & cost` is the primary financial
   action; Edit is secondary; Inactivate remains quiet/destructive. Use semantic margin tone:
   healthy positive margin green, thin/non-negative margin amber, negative margin red, and missing
   data neutral/unavailable. Do not create a fake “standard catalog rate” concept.
2. **Operational-impact slice (new read-model preflight required).** Add Owner/Admin-only,
   server-authoritative reverse relationships for active assemblies/offers and relevant nudge
   rules. Each section must provide truthful counts, useful empty states, and deep links to the
   affected records. Do not infer relationships in the client, expose unavailable relationships,
   or show decorative empty cards. The existing inactivation dependency check is not by itself a
   general impact projection.

**Guardrails:** Do not alter price/cost snapshots, reuse mutable current catalog values as history,
broaden Price Book entitlement/role access, or imply invoices, billing, inventory, or accounting
behavior. Association/nudge display must not delay, replace, or weaken the existing safe
inactivation dependency check.

**Done when:** An Owner/Admin can scan the economic decision, update pricing/cost safely, manage
field-search aliases, and—once the second slice is delivered—understand every live operational
relationship before changing an item. Desktop, narrow layout, missing-price/cost, zero-cost,
thin/negative-margin, conflict, and alias-management coverage remain correct.

### GAP-067 — Request workspace presentation lacks a coherent operational visual system

**Status:** Open
**Severity:** P2
**Area:** Request List and Request Detail presentation

The Request workspace carries inconsistent canvas warmth, card edges and spacing, metadata density,
queue alert treatment, and button hierarchy. In particular, repeated red row accents and badges
make routine work look urgent, while the selected row and actual SLA breach do not have sufficiently
distinct meanings. The detached Customer Need module also makes it possible to encounter an
attention action before the user has scanned what the customer needs.

**Locked direction:** Treat this as a presentation-only Request workspace coherence pass, using the
revised Request reference page retained by Christian for the implementation session. It is the
desktop visual source of truth for this gap; narrow behavior still requires responsive verification.
The exact implementation values are locked in the [Request Workspace Visual Token
Specification](ux-design/v2/request-workspace-visual-spec.md); do not substitute discretionary
palette, spacing, or hierarchy choices during implementation.
Use a clean, operational Slate-50 canvas (`#f8fafc` or the equivalent established token), distinct
from but not semantically dependent on the cool financial Price Book canvas. The app shell and
cards remain white with Slate-200 borders, rounded-xl geometry, and only a restrained shadow where
elevation is meaningful. Do not reintroduce the prior cream/amber page canvas.

- **Queue state grammar:** preserve GAP-027's one lifecycle cue, one server-ranked exception cue,
  and one next-action line. Teal identifies selection; red is reserved for genuine active,
  unacknowledged overdue/high-risk work; amber covers customer replies and routine follow-ups;
  completed work stays quiet. Do not add client-side ranking, duplicate alert badges, or colored
  rails to every row. The one sanctioned exception to the amber reservation is the BL138 Slice 3b
  Owner/Admin financial-review metadata dot — a tiny, non-alert amber dot preceding muted
  `text-slate-600` "{N} visit(s) need financial review" text, rendered in both the default row and
  the compact pane row (beneath the `Next:` / action-signal line). It is server-authoritative and
  must remain metadata: never a badge, rail, ranked exception cue, attention treatment, or
  interactive control.
- **Request anchor:** use compact micro-labels and a responsive identity/contact/location/owner
  grid, followed by a distinct planning row for Internal priority, Planned work date, and Internal
  follow-up. Customer Need follows inside the anchor as a clearly bounded *neutral* Slate-50/
  Slate-200 summary—not an attention banner. Keep all existing detail fields, controls,
  permissions, and semantics available.
- **Action hierarchy:** each context has one primary operational action (for example, respond to a
  customer message) in teal-600/700 with white text. A consequential internal action, such as
  Review Visit Financials, may use restrained dark-slate emphasis in its own card; continuation,
  edit, and navigation actions are white outlined controls. Destructive actions remain visually
  distinct and require their existing protections. Do not let a financial-review action visually
  outrank the active customer-promise response merely through styling.
- **Attention and spacing:** the single active customer-message attention card uses a warm amber
  surface/border; Customer Need and ordinary content stay neutral. Keep 20–24 px separation
  between major cards, 16–20 px card padding, 12 px internal control gaps, and one consistent
  divider/border tone. Avoid a stack of heavy shadows or multiple competing colored panels.

**Guardrails:** This gap does not authorize changed request lifecycle, attention/ranking logic,
financial-review behavior, API contracts, permissions, data model, or a generic queue redesign.
Preserve responsive and keyboard behavior. Do not flatten the queue so far that a dispatcher loses
the factual next-action context required for fast triage.

**Done when:** Request List and Request Detail use a calm, consistent operational visual system;
selection, customer attention, genuine breach, completion, and primary action are distinguishable
at a glance; Customer Need is present in the request anchor; and browser verification confirms the
desktop and narrow layouts retain their locked behavior.

### GAP-037 — Pilot has no weekly, evidence-based value report

**Status:** Open
**Severity:** P1
**Area:** Founder/pilot operations

Provide a founder-only, account-timezone, copy-pasteable weekly summary of safe request-level signals. Exclude Spam/Test and demo/internal accounts; do not turn it into owner analytics, automated email, staff scoring, or unsupported business-outcome claims.

### GAP-038 — Pilot businesses lack an in-product feedback and help loop

**Status:** Open
**Severity:** P1
**Area:** Authenticated PWA pilot support

Add an authenticated, rate-limited, fail-soft feedback route to a private founder channel and a maintained Help & Updates page. Do not automatically attach customer PII, broad logs, or create a ticketing/CMS system.

### GAP-054 — Authenticated app-shell navigation and action hierarchy needs review

**Status:** Open
**Severity:** P2
**Area:** Authenticated desktop/mobile shell

Perform a role- and entitlement-aware desktop/mobile review of global versus page-local actions, profile grouping, active-route treatment, discoverability, keyboard behavior, and narrow layouts. Make only evidence-backed shared-shell changes and record browser verification.
