# OpHalo Keep UI Production Decision Register

**Status:** Working decision register — not implementation authorization by itself  
**Date:** 2026-08-20  
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

**Status:** Decision required  
**Decision to make:** Does the authenticated desktop Requests surface use a fixed two-pane master-detail
workbench: a compact Request Queue on the left and the selected request workbench on the right?

**Proposed direction:** Yes. The queue is operational—not an inbox—and uses Requests, Needs Attention,
My Work/Assigned, ownership, and continuity language. It is not generic global navigation.

**User acceptance:** An office user can scan and move between requests without losing their place or
opening/closing a standalone detail page repeatedly.

**Must define before build:** queue width/minimum workbench width, allowed manual queue collapse,
wide-layout threshold based on usable workspace, and queue scrolling versus workbench scrolling.

### UI-002 — Durable request selection and routes

**Status:** Locked  
**Rule:** `#/request/{id}` remains the durable, direct, refresh-safe route for a selected request.
On wide desktop it renders inside the Request Workbench; on narrow screens it renders as a focused
one-column request view. Do not introduce a competing `?id=` selection contract.

**User acceptance:** Copying/opening a request URL, refreshing, browser Back/Forward, and a push/deep
link all preserve an authorized request destination.

### UI-003 — Unselected workbench state

**Status:** Decision required  
**Decision to make:** What does `#/requests` show in Pane 2 before a request is explicitly selected?

**Options to evaluate:**

- a purposeful queue welcome with a server-ranked priority preview and **Open request** action;
- auto-select the highest-priority request only if it creates no false viewed/activity side effect and
  replaces the URL with the selected durable route;
- a neutral empty state only when the current queue is actually empty.

**Do not ship:** a blank canvas or a generic “Select an item” placeholder as the normal populated state.

### UI-004 — Queue views, defaults, and selection continuity

**Status:** Decision required  
**Decision to make:** Which queue views are primary, what is the initial/default view by user context,
and what persists across refresh and return?

**Proposed starting posture:**

- Owner/Admin: restore the last permitted queue; first visit defaults to Needs Attention.
- Operator: My Work is primary; Available stays a distinct privacy-limited surface.
- A future personal default-view preference may be added only if pilot evidence justifies it.

**Must define:** sorting/ranking source, filters/search persistence, selected-row treatment, page/cursor
behavior, filtered-empty copy, refresh behavior, and whether queue updates may reorder visible rows
while a user is working.

### UI-005 — Request information hierarchy

**Status:** Decision required  
**Decision to make:** What must be visible in the selected request’s first viewport, and what is
progressive disclosure?

**Proposed first-viewport order:**

1. request identity, customer, status/attention reason, and current next safe action;
2. customer’s original need/description;
3. customer contact and service location needed to act now;
4. active scope/work-review signal only when it is real and actionable;
5. customer update/contact action area;
6. activity/history and lower-frequency context.

**Must preserve:** customer-entered data is not cosmetically rewritten; internal priority remains
distinct from customer-reported urgency/contact preference.

### UI-006 — Action semantics and hierarchy

**Status:** Locked for action truth; decision required for full visual mapping.

**Locked rules:**

- **Send update** is customer-visible and must say so.
- **Internal note** is internal-only and must say so.
- **Log contact** records outside contact only after explicit staff confirmation; launching a phone,
  mail, message, or maps app proves intent, not completed contact.
- **Resolve/acknowledge attention**, **Mark work done**, and **Close request** are distinct,
  server-authorized actions. Active attention must not be made to look resolved by a completion action.
- Every versioned mutation uses the authoritative request version and adopts the returned detail state.

**Decision to make:** lock exact visual hierarchy for normal, active-attention, read-only, and terminal
states. Color reinforces meaning but never replaces explicit labels or visible customer/internal scope.

### UI-007 — Office, field, and responsive work surfaces

**Status:** Decision required  
**Rule already agreed:** no global “Office Mode / On-Site Mode” toggle.

**Decision to make:** lock responsive containment, default entry points, and field ergonomics.

**Proposed posture:**

- wide PWA workspace: Request Queue + Workbench;
- narrow/touch-constrained workspace: focused request view;
- native mobile: field-first My Work and Available surfaces;
- opening external contact/maps action: on focus/resume, show a non-blocking factual prompt until
  staff explicitly logs contact or dismisses it.

**Field privacy rule:** proposed/actual field-work capture is price-blind, including for an Owner/Admin
acting as the field recorder. No sell price, cost, margin, tax, discount, quote, or billing authority
appears in that capture surface.

### UI-008 — Form containment and overlay contract

**Status:** Decision required  
**Decision to make:** adopt a product-wide placement rule for inline forms, drawers, modals, and full
routes.

| Pattern | Intended use |
|---|---|
| Inline | small, contextual, low-containment changes that need surrounding request context |
| Side drawer | focused create/edit task that benefits from retaining the underlying queue/workbench |
| Modal/dialog | short confirmation, acknowledgement, short outcome selection, or blocking recovery |
| Full route/workspace | substantial, durable, frequently resumed, field-first, or multi-step work |

**Non-negotiable rules to lock:** one active overlay at a time; deliberate Escape/backdrop/Back/Cancel
behavior; unsaved-change protection; focus movement and restoration; scroll behavior; visible save/error/
conflict feedback; mobile drawer-to-full-screen containment.

### UI-009 — Cross-product state and recovery contract

**Status:** Decision required  
**Decision to make:** define the required UI treatment for:

- initial loading and background refresh;
- no requests and filtered-empty queues;
- remote failure and retry;
- permission/read-only state;
- stale data and version conflict;
- mutation pending, success, and failure;
- local unsent drafts; and
- offline/connection-loss.

**Locked conflict rule:** a `409` conflict never discards typed input. Keep re-fetches authoritative
state, preserves the local draft, blocks the stale form from submitting, and explains how to review
and retry. The UI may identify another actor only if authorized server data proves it.

**Proposed pilot offline posture:** drafts may be preserved only after a privacy/retention decision;
customer-visible messages and other mutations must not silently queue and transmit later.

### UI-010 — Customer-submitted request journey

**Status:** Decision required  
**Decision to make:** lock the public intake’s minimum useful fields, confirmation/recovery behavior,
and visual trust contract separately from the existing customer request page.

**Required outcome:** customer-created and business-created requests enter the same accountability loop.
Customer urgency is a reported signal, not automatic operational attention; contact preference is not a
delivery guarantee.

**Customer acceptance:** before entering data, a person can identify the business, understand why the
information is requested and what occurs next, access applicable Privacy/Terms/help routes, and recover
from validation, network, or known-business unavailable states. Invalid/unknown capability links remain
non-enumerating.

### UI-011 — Customer request page trust and collaboration

**Status:** Decision required  
**Decision to make:** lock the existing-request customer page hierarchy, allowed actions, and terminal
states.

**Required hierarchy:** known business identity first; OpHalo Keep is a truthful secondary platform
endorsement. Do not use unsupported verified/security/delivery claims. Customer pages expose only their
intentionally limited public contract.

### UI-012 — Accessibility, content, and visual production gate

**Status:** Decision required  
**Decision to make:** create shared acceptance criteria for keyboard/focus, touch targets, contrast,
zoom, semantic labels, error announcement, typography/hierarchy, responsive layout, and populated-data
review.

**Required visual outcome:** each primary surface has a clear hierarchy, one dominant safe action,
intentional workspace surfaces, visible filter/recovery state, real brand identity, and no
placeholder/wireframe appearance.

### UI-013 — Migration, fallback, and release gate

**Status:** Decision required  
**Decision to make:** define how the new shell is introduced without duplicating request logic or
silently changing behavior.

**Proposed rule:** reuse one request-detail data/mutation engine and mount it in desktop workbench or
narrow focused layout. A temporary fallback may retain the current detail presentation, but no second
route contract or duplicate action policy is introduced.

**Release evidence:** realistic populated data, desktop/tablet/phone widths, Owner/Admin/Operator/
Viewer access posture, direct route/refresh/Back behavior, mutation/conflict recovery, and customer
public-flow review.

## 6. Required workflow scenarios

The following scenarios must be reviewed with realistic data before implementation approval. For each,
record starting state, required information, primary safe action, success, failure/recovery, and server
authority boundary.

1. Owner/Admin morning triage of attention, ownership, and stale work.
2. Office staff captures a request while answering an inbound phone call.
3. Customer submits a new request from a known business’s public intake link.
4. Customer opens an existing request/status page from an inbound link.
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

The build guide may be written when UI-001, UI-003 through UI-005, UI-007 through UI-012, and UI-013
are either **Locked** or explicitly **Deferred** with no dependent UI work authorized. It must then
translate these decisions into component boundaries, route/selection behavior, migration sequence,
test matrix, and release acceptance criteria; it must not reopen the decisions by implication.
