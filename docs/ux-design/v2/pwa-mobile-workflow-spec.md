# PWA Mobile Workflow Specification

**Status:** Draft — proposed cross-app mobile contract; requires decision review before implementation  
**Purpose:** Define how Keep behaves as a coherent, safe, touch-first PWA across staff and customer journeys. This document owns mobile workflow hierarchy and containment; it does not create new server permissions, lifecycle transitions, pricing authority, or delivery guarantees.  
**Companion authority:** [V2 Decision Register](keep-ui-production-decision-register.md), [Design Model V2](keep-ui-design-model-v2.md), and [Request Detail / Workbench specification](request-detail-workbench-signoff-spec.md) remain authoritative where they already define a request, action, data, or recovery rule.

## 1. Product position

Mobile Keep is not a compressed desktop console and it is not a role-switching product. It is the same authorized product expressed through a task-first, single-column shell.

The mobile experience must help a person:

1. find the work that needs attention;
2. understand one record without losing critical customer context;
3. perform the next safe, authorized action with a thumb-reachable control;
4. enter focused work without accidental navigation or data loss; and
5. resume reliably after interruption, refresh, poor connectivity, or a server conflict.

Device type never grants, removes, or guesses authority. The server supplies record visibility, available actions, current version, lifecycle state, and any attention/clearance effect. The client uses that response to choose presentation and emphasis only.

## 2. One application, three mobile journeys

| Journey | Intended outcome | Mobile posture | Must not become |
| --- | --- | --- | --- |
| Staff operations | Triage, communicate, capture work, and complete authorized records | Fast queue-to-detail drill-down with one current primary action | A shrunken desktop admin console |
| Field execution | Arrive prepared, contact the customer, record factual work, and resume after interruption | Contact and original need immediately reachable; focused capture workspace | A price, quote, scheduling, inventory, or accounting surface |
| Customer journey | Submit a need or understand an existing request | Business-first, trust-forward, capability-link page | A staff portal, live chat, or account system |

An individual may move between office and field responsibilities during the day. The application does not expose an **Office mode** or **Field mode** toggle. The current route, request state, and server-authorized actions determine what is prominent.

## 3. Mobile information architecture

### 3.1 Persistent destinations

The authenticated shell should reserve persistent thumb access for only the highest-frequency, cross-record destinations:

| Destination | Job | Notes |
| --- | --- | --- |
| **Requests** | Find, triage, and resume request work | Default operational destination; preserves selected queue context when returning from a request. |
| **My Work** | Return to the current user's authorized active work | A scoped request list, not a separate record type or local task system. |
| **Capture** | Start an authorized new request or quick capture flow | Opens focused containment; it does not compete with a selected request's primary action. |
| **More** | Lower-frequency account, help, and authorized configuration destinations | Shows only server-authorized destinations. |

**Decision required:** Validate the final persistent-navigation labels and whether **My Work** is a top-level tab or a saved Requests scope. Until that decision is locked, the shell must not assume that desktop navigation maps one-for-one to a mobile tab bar.

Public intake and customer request pages have their own intentionally limited navigation. They never mount authenticated staff navigation.

### 3.2 Drill-down and return

`#/request/{id}` is the durable selected-request route on every viewport. On mobile it presents a focused single request with a clear **Back to Requests** affordance. Browser Back/Forward, refresh, deep links, and push links must preserve the authorized destination and must not replace it with a local-only detail state.

Returning to Requests restores the applicable list scope, filter context, and scroll position when an active browser session can safely retain them. A deep link with no prior list context returns to the authorized default Requests scope.

## 4. The request is the mobile work canvas

### 4.1 Required order

A mobile request is one scroll surface beneath a compact sticky Request Anchor. At rest, its order is:

1. identity and current lifecycle state;
2. active attention, if any;
3. the customer/contact and service-location strip;
4. the original **Customer need** in verbatim, readable form;
5. current authorized work context and **Actual Work** entry point;
6. customer communication or internal note composition when authorized;
7. activity/history; and
8. lower-frequency record context and utilities.

The anchor keeps the customer, request reference, status, active attention when present, and the current server-authorized primary action immediately findable. Phone, service location, and responsible owner remain reachable without making the header a dashboard.

### 4.2 One primary action rail

The sticky bottom action rail is the priority engine, not a shortcut tray. It shows exactly one enabled local-task primary action at rest, chosen from the server-authorized action metadata.

Priority is:

1. active attention that requires an authorized response or clearance;
2. the next blocking, authorized work-state action;
3. an authorized lifecycle action such as **Mark Work Done**; then
4. an authorized terminal **Close Request** action, always with confirmation.

Customer-visible composition temporarily owns primary emphasis while text is being written. The rail hides or unpins while a text input is focused so the keyboard does not obscure the field or submit action. It reappears safely when focus ends.

The client must never invent a recommended action, permission, state transition, clearance effect, or completion condition. If no action is authorized, it shows clear read-only or waiting context rather than a disabled imitation of a privileged control.

### 4.3 Contextual layers, not modes

The same request exposes different information first according to its state:

| Current need | Prominent layer | Typical authorized interaction |
| --- | --- | --- |
| First response or other attention is due | Attention rail + primary action | Respond/resolve, then use Details for follow-up or reassignment when allowed. |
| Work is being performed | Contact strip + Customer need + Actual Work | Call, text, map, and record factual line items without leaving the request context. |
| Work is ready for review or completion | Lifecycle action rail | Mark work done or, when authorized and confirmed, close the request. |
| No change is permitted | Read-only request context | Review safe details, activity, and any public/customer communication allowed by the server. |

This applies equally to a person with broad permissions and a narrowly scoped operator. Broad access does not justify presenting every administrative control at once.

### 4.4 Details is the quiet administrative layer

**Details** is a labelled disclosure for permitted lower-frequency record utilities: assignment, watchers, service location, internal planning, and other server-authorized administration. It is one tap away but never displaces customer context, attention, field capture, or the current primary action.

The disclosure may not conceal a safety-critical warning, the original customer need, an active primary action, or required mutation feedback.

## 5. Focused work containment

Complex work opens in a full-height workspace or route with a visible **Back to Request** control. Only one overlay/workspace is open at a time; mobile drawers are full-height and never stack.

| Work | Containment | Contract |
| --- | --- | --- |
| New Request / Quick Capture | Focused drawer or route | Preserve safe return; show validation and save state. |
| Log Contact | Focused containment | A contact launch is intent only; durable logging is explicit. |
| Actual Work | Full-screen workspace | Factual and price-blind; draft status is explicit; submitted visits are locked. |
| Customer update / internal note | Inline composer | Mode and visibility are always explicit: customer-visible or internal-only. |
| Close Request / discard / conflict | Dialog | Requires explicit confirmation or recovery choice. |

Actual Work must be usable at a job site: large touch controls, one clear recorder task, draft visibility, and an explicit submission state. It must not expose price, proposal, payment, inventory, scheduling, or accounting controls merely because a user also has administrative permissions elsewhere.

## 6. Authorization and role adaptation

Roles describe possible capabilities; they do not prescribe separate mobile applications. For each record, the server controls visibility and action availability. The mobile client adapts as follows:

| Capability outcome | Mobile behavior |
| --- | --- |
| Can view only | Show request context and safe history; do not show unavailable actions as disabled promises. |
| Can communicate | Expose the appropriate composer with durable visibility disclosure. |
| Can record work | Expose **Add Work** and the focused Actual Work workspace. |
| Can assign or administer | Expose those tools inside Details when server-authorized. |
| Can resolve attention | Allow the authorized attention action to take priority in the action rail. |
| Can complete or close | Surface the valid lifecycle action only when its server preconditions are met; close requires confirmation. |

A user whose permissions change, whose assignment changes, or whose request becomes unavailable must receive the returned server state. The client removes invalid actions and provides a concise explanation without pretending that the previous local layout remains authoritative.

## 7. Queue, search, and empty states

Mobile Requests is an operational list, not a dashboard. Each row prioritizes the highest actionable exception, customer/request identity, concise customer context, and the next relevant metadata. It must not turn every row into a stack of badges or action buttons.

Search and filters are explicit, visible in their applied state, and recoverable. Empty results say whether the current scope/filter has no records or whether no authorized records exist, and provide only a relevant authorized next step. A mobile user must be able to return from search, filtered lists, and a request drill-down without losing orientation.

## 8. Customer-facing mobile journeys

Public intake is mobile-first and business-first. Before data entry it identifies the business, states why requested information is needed, offers applicable help/privacy/terms routes, and asks only for the established minimum useful request and contact information. Submission creates the same request accountability loop as staff-created work.

The customer Request page is a capability-link view of one request. It shows only server-authorized public lifecycle context, public updates, safe original-request context, and permitted customer actions. It never exposes staff identity beyond what is authorized, private notes, internal history, staff actions, or invented delivery/read/status claims.

## 9. Reliability, accessibility, and safety

- Use one selected-request data, mutation, version, and conflict engine across wide and mobile shells.
- Every mutation carries the authoritative request version and adopts the returned detail state.
- Preserve unsaved text/work where safe; otherwise require explicit discard before navigating away.
- On conflict, retain the local draft where possible, explain the changed server state, and require an explicit reapply/review decision before resubmission.
- Make general controls at least 44 CSS px; persistent field primary actions at least 48 CSS px.
- Use at least 16 CSS px editable text on iOS-facing mobile/public forms to prevent auto-zoom.
- Do not rely on hover, color alone, hidden swipe gestures, or device orientation for essential actions.
- Support keyboard, screen reader, zoom, safe-area insets, loading, offline/interrupted, empty, error, permission-denied, and stale-data states.

## 10. Mobile acceptance criteria

This specification is ready to implement only when the following can be demonstrated:

1. Any authorized user can open a deep-linked request on a phone, understand its customer need and current safe action, act if permitted, and return to the correct request context.
2. A person with both office and field permissions sees contextual priority rather than an Office/Field toggle or a screen full of simultaneous controls.
3. Active attention takes precedence over ordinary lifecycle actions, while customer composition safely owns the action area during writing.
4. A field user can call/text/map, capture Actual Work, submit it, and return to the request without losing context or seeing price authority.
5. Administrative utilities are reachable through Details only when authorized and do not bury urgent attention, customer need, or the primary action.
6. Read-only, denied, conflict, loading, empty, and interrupted states remain clear and recoverable.
7. Customer-facing mobile pages remain separate from staff navigation and expose only authorized public information and actions.

## 11. Decisions to lock before build

1. Final authenticated mobile persistent-navigation destinations and labels.
2. Whether My Work is a persistent destination or an always-visible Requests scope.
3. The exact server action metadata and ordering contract consumed by the mobile action rail.
4. Which non-request product areas are available on mobile in the first release, especially Price Book, settings, and account administration.
5. Offline scope: read-only cache, local drafts, queued mutations, or an explicitly online-only contract.
6. Push/deep-link entry behavior and notification preference surface.

Until these are locked, implementation may build the shared request detail and focused-work patterns already authorized by the V2 documents, but must not invent navigation, offline, or cross-product capabilities.
