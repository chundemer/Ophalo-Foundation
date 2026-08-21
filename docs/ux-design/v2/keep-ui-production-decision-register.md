# OpHalo Keep UI Production Decision Register

**Status:** Working decision register — not implementation authorization by itself  
**Date:** 2026-08-21  
**Purpose:** Lock the user-facing decisions required to move Keep's UI from a functional application to a production-ready operational product. This document precedes the UI implementation build guide.

## 1. Scope and posture

This is a user-workflow document, not a component inventory or a visual mood board. It answers what
each user must understand, do, recover from, and trust before a UI implementation chooses layouts or
components.

The current release focus is the authenticated Requests experience:

```text
Desktop office work -> Request Queue + selected Request Workbench
Narrow/mobile work  -> focused request view and field-first actions
```

It also includes the public customer submission and existing customer-request journeys because they
are trust and continuity surfaces, not optional polish.

Out of scope for this UI release unless separately promoted by a product decision:

- voice transcription;
- automatic end-of-day sweeps;
- customer detail-request links, photo upload, or new SMS delivery contracts;
- asset/equipment history without the Asset Operations identity and authorization model;
- inventory, invoices, payments, accounting, scheduling, or route optimization.

These are retained as **Future Product Pressures**, not implied build requirements.

## 2. Authority and status vocabulary

This register does not replace existing server, security, route, or product decisions. In particular,
request actions remain server-authorized and versioned; client layout cannot infer permissions or
state transitions.

| Status | Meaning |
|---|---|
| **Locked** | Existing authoritative decision or explicitly approved UI rule. Implementation may follow it. |
| **Decision required** | A user-facing choice that must be resolved before dependent implementation begins. |
| **Deferred** | Worth recording, but deliberately outside this UI release. |

## 3. User-workflow principles

1. **One shared request record.** Office and field surfaces show the same authoritative request,
   with different density and ergonomics—not separate “office” and “on-site” modes.
2. **Authorization decides access.** Role, account, row visibility, and available actions come from
   the server. The client never guesses from device type or a role label.
3. **Context decides the task surface.** A person opens a request to understand it, contact the
   customer, send an update, record a fact, capture field work, or review history. UI containment
   follows that task.
4. **A request must make the next safe action clear.** Attention resolution, work completion, and
   request closeout are separate outcomes; no generic “handled” action may blur them.
5. **State and recovery are visible.** Loading, filters, failures, conflicts, draft preservation,
   and read-only access must be understandable without inference.
6. **Customer trust is a product requirement.** A customer must recognize the business, understand
   the page purpose and data request, and retain a safe recovery path before acting.

## 4. Surface map

| Surface | Primary users | Primary job | Not its job |
|---|---|---|---|
| Desktop Request Queue + Workbench | Owners, Admins, office staff | scan risk, assign/reroute, contact/update, retain continuity | full scheduling/dispatch board or accounting system |
| Focused request detail | All authorized staff, adapted by viewport | understand one request deeply and make careful, permitted changes | a generic chat thread or drawer stack |
| Field/mobile workspace | Operators and field-working staff | My Work, customer contact, updates, field capture, resume after interruption | business-wide admin console or price/quote authority |
| Customer intake | Homeowner/property contact | submit a service need to a known business | account management or internal operations |
| Customer request page | Homeowner/property contact | understand current status, receive updates, add permitted context | staff data, private notes, staff actions, internal history |

## 5. Decision register

### UI-001 — Desktop Request Queue + Workbench shell

**Status:** Locked — 2026-08-21  
**Decision:** The authenticated desktop Requests surface uses a two-pane master-detail workbench: a
focused operational Request Queue on the left and the selected request workbench on the right. The
queue is not an inbox or generic global navigation; it uses Requests, Needs Attention, My Work/Assigned,
ownership, and continuity language.

**User acceptance:** An office user can scan and move between requests without losing their place or
opening/closing a standalone detail page repeatedly.

**Layout rules:**

- The Request Queue uses a bounded 320–360 CSS-pixel width.
- Two panes render only when the available application workspace also protects a usable selected
  workbench minimum. This is a container/minimum-width rule, not a device label or a fixed viewport
  breakpoint; it must be reviewed at 100%, 125%, and 150% browser zoom with realistic populated data.
- When that condition is not met, Keep renders the focused one-pane Queue → request drill-down
  presentation. It does not squeeze both panes into an operationally unusable layout.
- No manual collapsible-queue control is included in this first redesign release.
- In the bounded wide-desktop shell, Queue and Workbench may scroll independently. The implementation
  must avoid nested scroll traps and keep the selected request's current header/primary action reachable.

### UI-002 — Durable request selection and routes

**Status:** Locked  
**Rule:** `#/request/{id}` remains the durable, direct, refresh-safe route for a selected request.
On wide desktop it renders inside the Request Workbench; on narrow screens it renders as a focused
one-column request view. Do not introduce a competing `?id=` selection contract.

**User acceptance:** Copying/opening a request URL, refreshing, browser Back/Forward, and a push/deep
link all preserve an authorized request destination.

### UI-003 — Unselected workbench state

**Status:** Locked — 2026-08-21  
**Decision:** On a wide `#/requests` route with no request explicitly selected, Pane 2 renders a
read-only, non-mutating **Priority Preview**. It follows the applied queue view, filters, and search
context; it does not change the URL, mark a request viewed, or create an activity/audit event.

**Branches:**

- With attention in the active result set: show the top server-ranked request, its authoritative
  attention reason, and **Open request**.
- With active results but no attention: state only the provable fact that no request in this view
  currently needs attention; show the next server-ranked active request when one exists. Do not infer
  that every promise is on track.
- With a filter/search result of zero: show a filtered-empty recovery state and **Reset filters**.
- With no active requests: show a calm zero state and an authorized **New Request** action.

Only an explicit row selection or **Open request** navigates to `#/request/{id}` and mounts the full,
interactive workbench. Do not ship a blank canvas, generic “Select an item” placeholder, silent
auto-selection, or hidden route mutation.

### UI-004 — Queue views, defaults, and selection continuity

**Status:** Locked — 2026-08-21; **amended 2026-08-21** (Owner/Admin Office Review).
**Decision:** The visible primary queue tabs are intentionally constrained to three.

| User context | Primary tabs | Secondary views in **Views** |
|---|---|---|
| Owner/Admin | Needs Attention, All Work, My Work | Watching |
| Operator | My Work, Needs Attention, Available | Watching |

**My Work** is the standard visible label; do not use longer alternatives such as “Assigned to Me” or
“My Promises” in the primary tab bar. The secondary-views control is labeled **Views**, not "More
Views" — it holds only Watching for either role, and "More" overstates a single-item control. When a
secondary view is active, **Views** visibly names it (e.g. the control reads "Watching").
History remains a quiet footer/non-peer entry using existing dates, not a new “Completed Today” queue.

On a new browser session, Owner/Admin starts in Needs Attention and Operator starts in My Work. During
an active browser session, return navigation between `#/requests` and `#/request/{id}` restores the
permitted active view, applied filter/search context, and queue scroll position. This is a behavioral
contract; a particular browser-storage mechanism is not mandated here.

Server ranking and paging remain authoritative. Incoming changes must not silently reorder a list under
an actively scanning user; show a quiet refresh/update-available affordance instead. A filtered-empty
view is always distinct from a truly empty queue and provides a clear Reset filters recovery.

#### UI-004 amendment — Owner/Admin Office Review strip (2026-08-21)

**Supersedes** the original UI-004 table's placement of Ready to Close, Feedback Review, and Actual
Work Review inside Owner/Admin's generic **More Views**. Those three are Owner/Admin office
obligations, not quiet secondary views, and must not be hidden behind an un-badged overflow menu.

**Presentation variants (2026-08-21):** the role, count, disclosure, and action rules below apply
to both Requests presentations. Today's full-width `#/requests` page uses a normal horizontal
three-tab row and a compact, intrinsic-width Office Review control directly below it; it must not
stretch Office Review across the workspace or imitate a full-width input. Views and History remain
a compact, visually associated utility group on the tab row where space permits, or wrap together
before search/filter when space does not. UI-001's future 320–360 CSS-px Queue pane reuses the same
component as a pane-width strip, places Views/History in their own utility row, and uses the
two-row primary grid. The Queue-pane target composition:

```
Needs Attention 13
All Work 16 | My Work 4
Office Review · 1 pending ▾
Views ▾                                      History
Search / filter
```

- Owner/Admin retains exactly three primary queue controls: Needs Attention, All Work, My Work.
  Unchanged from the base decision above.
- A conditional **Office Review** control renders directly below the primary controls and above
  search/filter for Owner/Admin only. It is distinct from customer-promise risk (Needs Attention)
  and uses navy/neutral treatment, never amber by default. Collapsed is the default scan state, not
  the expanded list. It is compact and content-width on the current full-width page; it is full width
  within the future bounded Queue pane.
  - Its aggregate count is Ready to Close count + Feedback Review count + Actual Work Review count.
    Ready to Close and Feedback Review use their existing server view counts; Actual Work Review uses
    the authoritative `GET /keep/pricebook/actual-work/review-queue/count` endpoint (`{ count: int }`,
    Slice A-1, commit `1e35335`). The aggregate must never be a guessed zero and must never be derived
    from the full review-queue list's `.length`.
  - The strip is shown only when the authoritative aggregate is greater than zero. While the
    authoritative inputs are loading, the Queue reserves a compact loading placeholder in the strip's
    position, shaped like the eventual strip rather than a blank bar, so the Queue does not shift when
    the result resolves; an incomplete aggregate is never displayed as final.
  - Collapsed, the strip reads "Office Review · {aggregate} pending" — not a leading digit juxtaposed
    with the label. Once a member view (Ready to Close/Feedback Review/Actual Work Review) is active,
    it instead names that destination — "Office Review: Feedback Review" — the same active-naming
    pattern **Views** uses for Watching, so the Owner never loses scan context after selecting one.
  - Opening Office Review reveals Ready to Close, Feedback Review, and Actual Work Review, each with
    its own authoritative count, prioritizing actionable (non-zero) members as normal rows; members
    with nothing to review collapse into one quiet line (e.g. "No Ready to Close") rather than
    standing as equal-weight zero-badge rows — a single real item must not be buried among two empty
    ones.
- **Watching** is not office-review work. For Owner/Admin it is a quiet **Views** utility, kept
  separate from Office Review. Operator is unchanged: Watching remains its only secondary view behind
  **Views**, and Operator has no Office Review strip.
- **Views** and the demoted **History** entry point form a compact, visually associated utility
  group. On the current full-width page they may share the tab row; if they wrap, they wrap together
  before search/filter. In the future bounded Queue pane they live in their own row below Office
  Review, with Views left and History right. The Views/Office Review disclosures are a plain
  disclosure/group (not an ARIA `menu`/`menuitem`, since neither implements full menu keyboard
  traversal): Escape and an outside pointerdown both dismiss and return focus to the trigger;
  selecting a view does the same after navigating.
- Owner/Admin's default-session view remains Needs Attention, and Needs Attention remains the amber
  customer-promise-risk surface. Office Review work must never be merged into Needs Attention or imply
  that every review item is urgent.
- At the UI-001 320–360 CSS-px Queue-pane width, Owner/Admin's three primary controls use a two-row
  grid rather than a horizontal tab row: Row 1 is Needs Attention with its authoritative count, full
  width; Row 2 is All Work with its count and My Work with its count, side by side. Operator's
  narrow-pane grid is unaffected by this amendment: Row 1 is My Work full-width; Row 2 is Needs
  Attention and Available. Neither role's narrow-pane grid may scroll horizontally, clip, abbreviate a
  locked label, or squeeze a control below its usable target size.

### UI-005 — Request information hierarchy

**Status:** Locked — 2026-08-21  
**Decision:** A selected request uses a compact, sticky **Request Anchor** above a scrollable Work
Canvas. It is not a large permanently fixed header.

**Request Anchor:** customer name as the Source Serif 4 H1 anchor; request reference; source and
submission context; lifecycle/status; authoritative attention reason; current server-valid primary
action; and a compact 2–4-column adaptive context strip for phone, service location, and responsible
owner. An operator can find customer phone, service location, and responsible owner without scrolling.
Do not permanently place a QR code in this header; a supported Call action may offer a QR/modal handoff
where applicable.

**Work Canvas order:** active attention guidance first when active; customer’s original need exactly as
entered; real and authorized active scope/work context only; the customer update/internal-note work
area with explicit visibility; then activity/history and lower-frequency record context. Omit empty
scope placeholders. Never render a generic “Mark Handled” or client-invented “Resolve” header action;
state transitions appear only when server-authorized.

Customer-reported urgency/contact preference remains distinct from internal priority. “Visible on the
customer page” is the approved customer-visibility helper; do not claim a “live” customer page.

### UI-006 — Action semantics and hierarchy

**Status:** Locked — 2026-08-21

**Locked rules:**

- **Send update** is customer-visible and must say so.
- **Internal note** is internal-only and must say so.
- **Log contact** records outside contact only after explicit staff confirmation; launching a phone,
  mail, message, or maps app proves intent, not completed contact.
- **Resolve/acknowledge attention**, **Mark work done**, and **Close request** are distinct,
  server-authorized actions. Active attention must not be made to look resolved by a completion action.
- Every versioned mutation uses the authoritative request version and adopts the returned detail state.

**Visual mapping:**

| Operational meaning | Treatment | Examples / boundary |
|---|---|---|
| Current workflow primary | Navy filled | New Request, Open request, a currently valid work-completion or attention-resolution action |
| Customer communication | Keep teal filled | Send update, with clear customer visibility disclosure |
| Contextual secondary | Navy outline | Log contact, assign, call/email/customer-page actions, set follow-up |
| Quiet utility | text/subdued | Copy, watch, related low-risk utilities |
| Destructive terminal action | Red filled + confirmation | Close request only |
| Attention/risk | badge, header, or alert surface | Amber is never a filled action button |

At rest, the selected Workbench has only one enabled local-task primary. When a user begins a valid
customer update, its composer submit becomes the contextual primary and any header lifecycle action
demotes. Log Contact or Assign normally stays outline and may promote only when the authoritative
server action/attention contract identifies it as the recommended next step. The client does not invent
that recommendation. A global New Request control must defer visually to the selected-request primary.

### UI-007 — Office, field, and responsive work surfaces

**Status:** Locked — 2026-08-21  
**Decision:** Keep has no global “Office Mode / On-Site Mode” toggle. Authorization decides available
data/actions; default queue decides the starting point; available workspace and input decide containment;
the task decides the action surface.

- Wide PWA uses UI-001’s Queue + Workbench. Narrow/touch-constrained PWA uses focused Queue → request
  drill-down on the same durable route. Field users default to My Work.
- The focused field request keeps customer, phone, service location, original need, and the currently
  safe action immediate. Persistent mobile actions are 48 CSS pixels or larger and appear only for
  actual permitted tasks such as Call, Maps, Send Update, Log Contact, or capture; one remains dominant.
- Proposed Scope and Actual Work open as full focused workspaces, not squeezed cards/drawers. They are
  price-blind for every field recorder, including an Owner/Admin: no sell price, cost, margin, tax,
  discount, quote, or billing authority.
- Phone, message, and mail launches record intent only. On return, one non-blocking, single-slot banner
  may ask factually, e.g. “You opened a call to Charles. Log what happened?” with Log contact/Dismiss.
  It never auto-logs; a new relevant external intent replaces the prior banner. Maps does not imply a
  contact resumption prompt.
- A general mobile bottom action bar hides/unpins while a text input is focused. A composer may retain
  its own keyboard-safe sticky submit footer where its existing contract permits it.
- Leaving a field composer preserves a resumable server draft by default. Explicit discard is destructive
  and confirmed; do not use a generic “discard unsaved scope” prompt.

There is no silent offline mutation queue. Local draft preservation beyond existing server drafts needs
an explicit privacy/retention decision before it is promised. Native field products follow these
principles later; this decision does not authorize a native redesign.

### UI-008 — Form containment and overlay contract

**Status:** Locked — 2026-08-21  
**Decision:** Form containment is selected by task, not by implementation convenience.

| Pattern | Intended use |
|---|---|
| Inline | customer update, internal note, priority, follow-up, and simple assignment |
| Side drawer | New Request/Quick Capture, Log Contact, and other focused single-record creation/editing that retains workbench context |
| Modal/dialog | Close request, explicit discard, required attention reason, and blocking conflict recovery |
| Full route/workspace | selected Request detail, Proposed/Actual Work capture, and complex Price Book work |

Only one overlay may be open. On mobile, a drawer becomes full-height; drawers never stack. Customer-
visible writing remains inline. Destructive action requires a confirmation dialog; ordinary remote errors
remain inline with the affected work.

Opening containment moves focus to its title or first useful field. Dialogs trap focus; closing restores
the invoker focus. Escape, backdrop, Cancel, and browser Back respect local unsaved input with an explicit
Keep/Discard decision. Persisted server drafts retain their default resumable behavior. Full field
workspaces include a clear Back to Request control and visible save/error/conflict state.

### UI-009 — Cross-product state and recovery contract

**Status:** Locked — 2026-08-21  
**Decision:** Keep shows stable truth, preserves active-session effort, and requires an explicit
re-commit after uncertainty. Initial loads use layout-preserving skeletons and expose no actions until
authorized data arrives. Background refresh retains usable content, never silently reorders an actively
scanned queue or overwrites an active form, and offers a quiet update-available/refresh affordance.

A true empty queue is distinct from a filtered-empty queue: the former calmly states that no active
Requests exist and offers only an authorized New Request action; the latter names the active search or
filter context and offers Reset filters. Remote failure is localized to the affected region with Retry
and a safe return path; it does not turn a single panel failure into a generic application crash.
Read-only access says that the Request can be viewed but not changed and does not expose unavailable
write affordances.

During a mutation, disable the submitting control and prevent duplicate submission while preserving the
entered values. Adopt returned authoritative state on success and confirm it beside the changed work
with an accessible announcement. On failure, keep input and show the safe server error inline; retry is
explicit and only offered where repeating the authorized operation is safe.

A `409` never discards typed input. The affected form becomes blocking recovery: Keep preserves local
input, re-fetches authoritative state, blocks stale submission, and requires the user to review the
current Request before explicitly reapplying and submitting their text against the new version. Where a
safe, meaningful comparison is possible, show authoritative content beside local input; otherwise retain
the text while the user reviews the refreshed record. Another actor is named only when authorized server
data proves it. There is no automatic merge or automatic resubmission.

Only server-persisted work is called a **Draft**. In-memory input is labeled **Unsaved changes**, is
session-only, and receives Keep/Discard protection for in-app navigation, Back, Cancel, and containment
close; browser/tab termination warnings are best-effort. On connection loss, say that unsaved changes
remain only in this session, keep reads explicitly retryable, and disable submits. No mutation silently
queues or transmits later; a customer-visible update always needs a fresh explicit submit.

### UI-010 — Customer-submitted request journey

**Status:** Locked — 2026-08-21  
**Decision:** Public intake is a business-first, mobile-first request entry point, separate from the
existing customer Request page. Before data entry, it identifies the known business, explains why
information is requested and what happens next, exposes applicable Privacy/Terms/help routes, and uses
only supported claims.

The minimum useful fields remain name, phone, service-address line 1, city, required US state, request
details, optional service-address line 2/ZIP, and optional email. Address fields accept ordinary,
free-form rural or unmapped text; autocomplete is neither required nor a submission gate. Service
location is shared with the business only and never shown on the customer Request page.

Submit disables immediately and shows local progress to prevent repeat taps. Any retry-safe public-write
idempotency protocol requires a separate server-contract decision; a client-generated value alone is not
a duplicate-prevention promise. Existing public-intake rate limiting and pilot spam posture remain in
force; honeypot, adaptive challenges, and broader duplicate detection are not added by this UI decision.

After a durable submission, show the Request-page path and clear recovery: with supplied email, the
existing fail-soft transactional tracker-link email is sent; without email, use the customer Request page
with copy/share affordances and practical recovery copy. Do not claim email delivery, realtime tracking,
or account security. Invalid, expired, unknown, or unavailable intake links use a calm non-enumerating
state: “Service intake is unavailable. Please contact your service provider directly.”

**Required outcome:** customer-created and business-created requests enter the same accountability loop.
Customer urgency is a reported signal, not automatic operational attention; contact preference is not a
delivery guarantee.

**Customer acceptance:** before entering data, a person can identify the business, understand why the
information is requested and what occurs next, access applicable Privacy/Terms/help routes, and recover
from validation, network, or known-business unavailable states. Invalid/unknown capability links remain
non-enumerating.

### UI-011 — Customer request page trust and collaboration

**Status:** Locked — 2026-08-21  
**Decision:** The customer Request page is a business-first, mobile-first capability-link view of one
Request—not an account portal, live-chat inbox, or staff workbench. Business display name leads; an
explicitly configured customer-facing contact is offered where available; OpHalo Keep is quiet secondary
attribution. Logo upload is not part of V1.

Show only server-authorized public status, explicit public business updates, customer-safe original
Request context, and permitted customer actions. The latest public business update is prominent, with
public updates in chronological order. **Add details for {Business}** is one bounded contribution, not
chat: no speech bubbles, avatars, presence, typing indicators, or expectation of an immediate reply.

Never expose service location, staff identity or assignment, internal notes/activity, prices/costs,
office workflow, or delivery/read claims. Do not invent scheduling/progress states; render only the
server-authorized lifecycle and explicit public updates. Closed Requests remain readable and offer the
existing one-time feedback action while unexpired. Cancelled Requests remain readable but accept no new
customer input. Closed and Cancelled pages expire 30 days after their terminal transition. Invalid,
expired, or unavailable links use the same calm, non-enumerating recovery state and show business
contact only if safely configured.

### UI-012 — Accessibility, content, and visual production gate

**Status:** Locked — 2026-08-21  
**Decision:** No V2 surface ships without evidence for realistic and boundary-valid content; authorized,
loading, empty, filtered-empty, read-only, error, conflict, and recovery states; keyboard-only and
screen-reader operation; visible focus and managed dialog focus; direct-route, refresh, and Back
behavior; and wide, narrow, touch, and 100%/125%/150% zoom review.

Review maximum server-accepted names, addresses, notes, updates, error strings, long unbroken words,
missing optional data, multiple badges, and realistic history depth—not ideal placeholder content.
Editable text controls on iOS-facing public/mobile forms use at least 16 CSS pixels to avoid browser
auto-zoom. General controls target at least 44 CSS pixels; persistent field/mobile primary actions at
least 48 CSS pixels. Text and non-text contrast, including focus indicators, must be audited against
their adjacent surfaces. Source Serif 4 carries meaningful page/section anchors; Inter carries
operational UI, forms, and data.

Validation errors and blocking conflict entry receive appropriate immediate screen-reader announcement;
success and non-urgent changes use polite announcements. Quiet background-refresh availability must not
repeatedly interrupt reading. Labels must state only supported facts: customer-visible work says
“Visible on the customer page,” never “live,” and customer/internal distinction is explicit. Reject
placeholder/developer copy, raw identifiers, clipped or overlapping controls, weak hierarchy, false
delivery/realtime/security claims, and layouts that work only with ideal data.

Release evidence includes populated visual captures, keyboard/focus traversal results, screen-reader
verification for validation/mutation/conflict announcements, contrast audit, and a regression proof that
a `409` preserves local input and requires explicit re-review before resubmission.

### UI-013 — Migration, fallback, and release gate

**Status:** Locked — 2026-08-21  
**Decision:** Keep has one durable selected-Request route, one authorization/data/mutation/version/
conflict engine, and one UI-009 recovery contract. Wide and narrow shells are presentation mounts over
that same engine; no fallback may create another route, action policy, request cache, or write path.

Selecting another Request, navigating, resizing between eligible layouts, refreshing, or using
Back/Forward must preserve the correct authorized destination and must never leak state, version,
pending mutation, or unsaved input between Requests. When local input would be displaced, use UI-008’s
explicit discard dialog before navigation proceeds.

Rollout may use a presentation-only feature flag. Begin with behavior-preserving extraction, mount the
new shell behind the flag, validate direct routes and recovery with realistic data and roles, and retire
fallback presentation only after UI-012 evidence and a defined pilot observation period. The two-pane
shell is selected by UI-001’s usable-workspace rule, never a fixed viewport breakpoint.

## 6. Required workflow scenarios

The following scenarios must be reviewed with realistic data before implementation approval. For each,
record starting state, required information, primary safe action, success, failure/recovery, and server
authority boundary.

1. Owner/Admin morning triage of attention, ownership, and stale work.
2. Office staff captures a request while answering an inbound phone call.
3. Customer submits a new request from a known business’s public intake link.
4. Customer opens an existing customer Request page from an inbound link.
5. Field technician opens My Work, launches an external call/contact action, returns, and logs a
   truthful outcome.
6. Field technician captures price-blind proposed or actual work where that capability is available.
7. Owner/Admin makes a customer update and sets a valid internal follow-up/planned date.
8. Two authorized staff members make conflicting edits; the second user preserves their draft and
   recovers from the version conflict.
9. Viewer/read-only user opens a permitted request and sees no unavailable write affordances.
10. No-data, filtered-empty, loading, remote-error, and unavailable/permission states across Requests
    and public customer surfaces.

## 7. Future Product Pressures

These are valid discovery signals but are not UI-release requirements. A later product decision must
provide evidence, dependencies, data/permission contract, and user acceptance before promotion:

- customer “request more details” links, photo upload, and channel delivery;
- promise-safe quick replies, waiting-until, and end-of-day open-promise review;
- asset/property/equipment identity and permitted service-history context;
- voice-assisted field capture;
- offline mutation queue/synchronization.

## 8. Exit criteria before the implementation build guide

The build guide may be written when UI-001 through UI-013 are either **Locked** or explicitly
**Deferred** with no dependent UI work authorized. It must then
translate these decisions into component boundaries, route/selection behavior, migration sequence,
test matrix, and release acceptance criteria; it must not reopen the decisions by implication.

## 9. Documentation alignment before implementation

The following sources remain authoritative in their stated domains, but contain prior scope or layout
posture that must be reconciled explicitly rather than silently overwritten.

| Source | Current posture | Required action before dependent implementation |
|---|---|---|
| `pwa-ui-quality-system.md` | D1 excludes Request Detail from its first correction phase; D15 defers mobile adaptation. | V2 UI-001/UI-007 extend that program to Queue + Workbench and focused PWA detail. Retain D1/D15 as historical phase boundaries; do not apply them to V2 implementation. |
| `build-log/081-session-24-request-detail-2-column-workbench.md` | Earlier desktop direction removes the request sidebar and uses a top-nav/70–30 detail composition. | Superseded for the new authenticated Requests shell by UI-001 through UI-005. Its request-data and component-reuse observations remain useful, not its layout direction. |
| `ux-design-model-v1.md` | Defines current Request List and Request Detail contracts. | V2 UI-001 through UI-013 supersede V1 where their rules conflict; V1 tokens, typography, and retained primitive guidance remain in force. |
| `keep-component-spec.md` | Recipes are customer-surface-first; operator appendix is only a token/action note. | Retained as primitive source. V2 Component Spec now owns Queue row, Request Anchor, workbench, action, field-bar, and containment recipes. |
| `keep-review-rubric.md` | Binary checks and detailed gates are heavily tuned to the current customer request page and standalone list/detail treatment. | Retained for covered customer/list/detail checks. V2 Review Rubric owns Queue + Workbench and focused-mobile checks; do not apply customer-page class-count checks mechanically. |
| `ux-design-decisions.md` | Existing button table calls “Mark handled” quiet bookkeeping. | Superseded where it conflicts with UI-006: attention acknowledgement, work completion, and request closeout are separate server-authorized actions; no universal “Mark handled” button. |

### Hardening rules already promoted

- Semantic-wash contrast is audited at WCAG 2.1 AA; ordinary text on pale teal, amber, or blue uses
  an audited high-contrast foreground.
- Source Serif 4 headings and Inter UI/body remain the type contract.
- General controls target at least 44 CSS pixels; persistent field/mobile primary actions target at
  least 48 CSS pixels.
- Repeated muted uppercase labels must not substitute for hierarchy; page anchors, type ramp, and
  surface volume carry the primary hierarchy.
