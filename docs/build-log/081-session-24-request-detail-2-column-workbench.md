# Build Log 081 — Session 24: Request Workbench 2-Column Layout And List Quick Actions

**Started:** 2026-07-11
**Status:** Draft — S24 paused for GAP-007 list quick-action contract
**Session name:** S24 request workbench 2-column layout and list quick actions
**Related ADRs:** ADR-377, ADR-380, ADR-382, ADR-383, ADR-433, ADR-434, ADR-435
**Next free ADR before this log:** ADR-435
**Next free ADR after ADR-435:** ADR-436

---

## Purpose

This build doc converts the authenticated PWA request list/detail experience from a desktop layout
that feels like three columns into a dedicated request workbench.

The current desktop composition effectively spends horizontal space on:

```text
global left nav | request main content | request action/context rail
```

That makes the active request feel compressed, especially now that list and detail contain richer
lifecycle, attention, contact, service-location, priority, team, timing, internal-note, and closeout
workflows.

The target state is:

```text
top app nav
request list or request header
70% primary work area | 30% context/actions panel where useful
```

The request list remains the triage and common-action surface. Request detail remains the careful
full-context surface. Staff should not have to open detail for routine, confident actions that the
server already exposes as list-safe quick actions.

This is primarily a PWA layout/workflow change. It should not change permissions, concurrency rules,
request statuses, or mobile/native app behavior. Backend contract changes are allowed only where a
list quick action cannot be safely completed from existing list metadata.

---

## Product Decisions To Lock Before Coding

### Decision 1 — Request workbench uses a shared desktop shell

On desktop request-list and request-detail routes, hide or replace the persistent left sidebar with a
horizontal top navigation bar.

Rationale:

- Keeping the left sidebar visible means request detail still feels like three columns even if the
  detail component itself uses a 70/30 grid.
- Keeping the left sidebar on list while detail uses top nav makes the two request surfaces feel like
  different products.
- The request list and detail pages are the operator/admin workbench. They need width for the active
  job and the active queue.
- The app only has a small number of top-level destinations, so the sidebar is low-value on detail.

Expected desktop top nav contents:

- OpHalo Keep logo/brand.
- Requests navigation.
- Getting Started navigation only when it is still operationally relevant, or tucked behind a menu.
- Settings navigation when visible for the current role, preferably as a compact nav item or account
  menu entry.
- New Request action when allowed.
- Quiet role/account identity if currently shown in the sidebar.

Mobile remains unchanged at the shell level unless a separate mobile-nav cleanup is explicitly
approved.

Settings and Getting Started can keep their existing shell in the first pass if that minimizes blast
radius, but Requests and Request Detail should match.

### Decision 2 — Detail body uses a 70/30 split on desktop

Use a responsive desktop grid:

```text
minmax(0, 7fr) minmax(320px, 3fr)
```

or an equivalent 70/30 implementation that protects the side panel from becoming too narrow.

The right column may need an upper bound on very wide screens if the page becomes visually stretched,
but the first implementation should favor reclaiming detail workspace width rather than creating a
small fixed-width rail.

### Decision 2b — Request list should match the workbench layout language

The request list should use the same top nav, spacing, typography, and restrained operational styling
as request detail.

The list page does not have to force the exact same 70/30 split if row-level actions are the better
workflow, but it should avoid the current directory-like feel. Acceptable list patterns:

- main queue list with inline row quick actions;
- main queue list plus a 30% right panel for queue guidance, selected-row preview, or bulk/queue
  context;
- a hybrid where row actions are visible on desktop and collapsed behind an action menu on narrow
  widths.

The list should stay dense enough for scanning. Do not turn rows into large marketing cards.

### Decision 3 — Main column owns the work loop

The 70% main column owns:

- request identity/status header content when not in the global header;
- customer description/original request;
- active attention guidance when present;
- send customer update composer;
- activity feed and filters;
- primary lifecycle action only when it is part of the immediate work loop.

The send-update composer should sit directly above activity so staff can read recent communication and
write the next customer update without jumping across columns.

### Decision 4 — Side column owns context and secondary controls

The 30% context panel owns:

- customer contact details;
- service location;
- customer intake signals and contact preference;
- editable internal business priority;
- responsible user;
- watchers;
- follow-up/planned timing;
- internal comments/instructions;
- utilities and secondary actions.

The side panel is not a dumping ground. If an action is the recommended next step for active
attention, it should appear in the main work path or be promoted visually from the side panel.

### Decision 5 — Do not merge customer urgency and internal priority

Keep ADR-433 semantics:

- Customer urgency/contact preference is a read-only customer signal from intake.
- Business priority is an internal editable triage decision.

The UI should clarify their relationship instead of collapsing them into one field.

Recommended labels:

```text
Customer signal
Internal priority
```

### Decision 6 — Lifecycle actions are contextual

Preserve ADR-434:

- Show **Mark work done** only when server action metadata allows transition to `resolved` and the UI
  state is eligible for the normal no-active-attention path.
- Show **Close request** only when server action metadata allows transition to `closed`.
- Active attention wins the hierarchy. Do not let a lifecycle button imply that attention is handled.

The request header may host the current primary lifecycle action, but only when that action is truly
the next valid step.

Recommended placement:

- **Close request**: header action when eligible.
- **Mark work done**: header or main work path only for the normal no-active-attention path.
- Active-attention resolution actions: main column, near the attention guidance and work loop.
- Demoted/admin utilities: side panel.

### Decision 7 — Private and customer-visible writing surfaces must be visually distinct

Request detail has several text-entry surfaces with different visibility and workflow effects:

- **Send customer update**: customer-visible; can optionally pair with status changes where allowed.
- **Internal note**: internal-only team memory/instructions; never customer-visible.
- **External contact summary**: internal audit/team memory for calls, texts, email, or in-person
  contact; may have server-owned attention effects depending on contact details.
- **Feedback review note**: internal-only Owner/Admin review note.
- **Attention clear reason**: internal-only explanation for acknowledging attention without a
  customer update/contact log.

The UI must not make these feel interchangeable. Internal note surfaces should say:

```text
Internal note
Not visible to customer
```

Customer update surfaces should continue to say when text is visible on the customer page. Contact
logs should be clearly framed as durable internal records of outside contact, not customer messages.

### Decision 8 — Contact logging is normal team memory, not only attention cleanup

`Log contact` should be available whenever `detail.availableActions.canLogExternalContact` allows it.

When attention guidance says contact logging is the recommended resolution path, promote it in the
main work loop. Otherwise keep it in the side-panel customer/contact or utilities area.

The contact panel should include practical owner/admin affordances where data exists:

- call link;
- email link;
- copy phone/email;
- log contact.

### Decision 9 — Follow-up and planned-date controls are in scope

The backend already supports staff-owned timing:

- `PUT /keep/requests/{requestId}/follow-up-on`;
- `DELETE /keep/requests/{requestId}/follow-up-on`;
- `PUT /keep/requests/{requestId}/planned-for`;
- `DELETE /keep/requests/{requestId}/planned-for`.

The PWA detail page should not only display timing. It should allow eligible staff to set/clear:

- Follow Up On date, reason, and optional note, respecting `validation.followUpNoteMaxLength`;
- Planned For date.

Use server action metadata:

- `availableActions.canSetFollowUpOn`;
- `availableActions.canSetPlannedFor`.

These timing controls are internal/staff-owned. They do not automatically notify the customer.

### Decision 10 — Activity filters must not blur customer-visible and internal history

After internal notes return, the default activity filter must not imply that internal notes were
customer communication.

Recommended filter labels:

```text
Conversation & notes
All activity
```

or an equivalent label that makes internal team memory legible. If a stricter customer-only
conversation filter is introduced later, it should exclude internal notes.

### Decision 11 — Admin status/classification paths must be explicit

Cancellation, spam, and test/classification actions should not be accidentally buried in the customer
update composer.

For S24, either:

- expose them in a compact side-panel **Admin actions** section when server metadata allows them; or
- explicitly defer them in the implementation notes if no safe UI exists yet.

Do not create new client-side policy. The client must continue to rely on server metadata and allowed
status/action lists.

### Decision 12 — Request list quick actions are in scope

The request list should render server-provided `row.actions.quickActions` as actual quick actions,
not only as a `Next:` text prompt.

ADR-435 clarifies the product boundary:

- request list is the speed/action cockpit;
- request detail is the depth/accountability workbench;
- S24 deep-link/focus shortcuts are only a temporary safety fallback while GAP-007 is open.

List quick actions should cover routine confident work:

- open detail;
- contact customer / log contact;
- send customer update;
- add internal note;
- mark handled / clear attention when server says it is allowed;
- assign/self-assign/watch where the current list surface is specifically about ownership.

List quick actions may cover only with stricter server metadata and explicit confirmation:

- close request from the Ready to Close queue when server metadata safely supports it;
- mark work done only for no-active-attention cases when server metadata says it is safe.

Actions with meaningful context risk should remain detail-first unless a safe compact flow exists:

- cancellation;
- spam/test classification;
- feedback review / mark feedback reviewed;
- service-location edits;
- status changes requiring customer-visible explanation;
- generic status changes;
- timing changes if the row does not have enough context.

List quick actions must communicate their effect:

- customer-visible;
- internal-only;
- logs outside contact;
- clears attention;
- changes lifecycle/status;
- opens detail for careful handling.

If a list quick action requires the latest concurrency version and the list row does not expose it,
the PWA should either:

- use a temporary focus/deep-link fallback; or
- explicitly stop and fix the list summary contract before treating the action as complete.

Do not guess versions or bypass server-owned concurrency. Do not treat background detail fetches as
the preferred V1 workflow; the preferred fix is GAP-007: make the request list payload carry the
safe mutation metadata it needs.

### Decision 13 — Request list row hierarchy should mirror detail hierarchy

List rows should be organized around the same mental model as detail:

- identity and status first;
- attention / customer signal / internal priority second;
- next safe action third;
- context metadata fourth.

Rows should expose enough context for an owner/admin to decide whether the quick action is safe:

- customer name and reference;
- status;
- attention reason/due state;
- responsible/unassigned;
- last touch;
- source and service city/state when present;
- customer urgency and internal priority distinction;
- tracker share state when relevant.

The row should not require opening detail just to discover that the customer prefers phone, the job is
unassigned, or the request is ready to close.

### Decision 14 — Mobile remains single-column

This project does not introduce a mobile 70/30 equivalent.

Mobile should keep a stacked detail page, with the order adjusted only where needed to match the new
work model:

1. request header;
2. attention guidance when present;
3. customer description/original request;
4. primary next action / send update;
5. activity feed;
6. customer contact and service location;
7. practical contact actions;
8. priority, team, timing controls, and utilities;
9. internal comments/instructions where server policy allows them.

Do not regress existing mobile affordances. If a component is moved for desktop, confirm mobile order
explicitly.

---

## Target Request Detail Desktop Layout

```text
+--------------------------------------------------------------------------------+
| OpHalo Keep        Requests                    New Request      Settings/Menu   |
+--------------------------------------------------------------------------------+
| < Requests   Kelley S   CK8DDC3WD   Work completed   Viewed 18h ago   [Close] |
+--------------------------------------------------------------------------------+
|                                                                                |
|  MAIN WORK COLUMN - 70%                    | CONTEXT PANEL - 30%               |
|                                            |                                    |
|  Customer description                      | Customer                          |
|  "Our sink in the kitchen..."              | Phone, email, preference          |
|                                            |                                    |
|  Needs attention guidance, if present      | Service location                  |
|                                            | Address, add/edit                 |
|  Send update to customer                   |                                    |
|  [ composer ]                              | Triage                            |
|  [ Send update ]                           | Customer signal                   |
|                                            | Internal priority dropdown        |
|  Activity                                  |                                    |
|  [ Conversation & notes | All activity ]   | Team                              |
|  timeline events                           | Responsible, watchers             |
|                                            |                                    |
|                                            | Contact actions                   |
|                                            | Call, email, copy, log contact    |
|                                            |                                    |
|                                            | Timing                            |
|                                            | Set/clear follow-up, planned date |
|                                            |                                    |
|                                            | Internal / Utilities              |
|                                            | Internal note, feedback review,   |
|                                            | admin actions when allowed        |
+--------------------------------------------------------------------------------+
```

## Target Request List Desktop Layout

```text
+--------------------------------------------------------------------------------+
| OpHalo Keep        Requests                    New Request      Settings/Menu   |
+--------------------------------------------------------------------------------+
| Requests        Active requests that may need ownership, follow-up, or closeout |
| [Needs attention] [Ready to close] [Assigned to me]                             |
+--------------------------------------------------------------------------------+
| Queue tabs / filters / search                                                   |
+--------------------------------------------------------------------------------+
|                                                                                |
|  MAIN QUEUE AREA                                      | OPTIONAL QUEUE PANEL    |
|                                                       |                         |
|  [ Row: Kelley S ]                                    | Queue context / filters |
|  CK8DC3WD  Work completed  Customer: urgent           | Selected-row preview    |
|  Unassigned  Last touch 1h ago  Brighton, TN          | or compact guidance     |
|  [Close request] [Log contact] [Open detail]          |                         |
|                                                       |                         |
|  [ Row: Kelley ]                                      |                         |
|  965D8R8T  Pending customer                           |                         |
|  Christian Hundemer  Last touch 1h ago                |                         |
|  [Send update] [Log contact] [Open detail]            |                         |
|                                                                                |
+--------------------------------------------------------------------------------+
```

The optional queue panel should be used only if it helps. If row-level actions are clearer, keep the
list as a strong full-width queue with rows that match the detail visual language.

---

## Current Implementation Notes For Claude

Primary files expected to change:

- `web/ophalo-app/src/App.tsx`
- `web/ophalo-app/src/pages/RequestDetail.tsx`
- potentially `web/ophalo-app/src/styles/app.css` if shared layout classes are preferred

Current relevant behavior:

- `App.tsx` renders a persistent desktop sidebar around all app routes.
- `Requests.tsx` currently renders inside that sidebar shell with a centered `max-w-6xl` queue area,
  tabs, search, status filter, and full-width row cards.
- `RequestRow.tsx` already receives `row.actions.quickActions` and derives a `Next:` prompt, but it
  does not render those actions as executable row controls.
- `KeepRequestSummary` includes list-safe identity, status, attention, priority, service-location,
  participation, preview, and quick-action metadata, but does not expose a request concurrency
  version.
- `RequestDetail.tsx` renders:
  - a breadcrumb/queue-navigation bar;
  - main detail content;
  - a desktop-only fixed-width right action rail;
  - mobile-only stacked actions and team context.
- `OriginalRequestCard` currently mixes customer description, contact, intake signals, priority, and
  service location in one card.
- `BusinessUpdateSection` currently lives in the action rail on desktop and before activity on mobile.
- Backend/API support for internal notes already exists at
  `POST /keep/requests/{requestId}/internal-notes`, exposed through
  `availableActions.canAddInternalNote` and `validation.internalNoteMaxLength`.
- The PWA detail page currently displays `internal_note_added` timeline events but does not appear to
  expose a standalone internal note composer/action.
- Backend/API support for timing already exists through follow-up and planned-date endpoints, exposed
  through `availableActions.canSetFollowUpOn`, `availableActions.canSetPlannedFor`, and
  `validation.followUpNoteMaxLength`. The PWA detail currently appears to display timing without
  exposing set/clear controls.
- Current `LogContactCard` is attention-gated. It should become generally available when
  `canLogExternalContact` is true, then visually promoted only when attention guidance recommends it.
- Current activity filter labels should be reconsidered once internal notes return, because
  `Communication` can sound customer-visible.

Claude should inspect the current file before coding because recent sessions may have changed the
exact component boundaries.

---

## Codeable Sessions For Claude

### S24a — Lock shell behavior and top navigation

Goal: Make request list and request detail use the same desktop request-workbench shell by replacing
the persistent left sidebar with a top navigation shell on request routes.

Scope:

- Update `App.tsx` shell behavior for `route.page === "requests"` and `route.page === "detail"`.
- Preserve the existing sidebar for settings and getting-started routes unless a top-nav reuse is
  simpler and visually safe.
- If the sidebar is retained anywhere, retain it only outside the request workbench.
- Add a desktop top nav for request routes with equivalent navigation destinations and role gating.
- Keep detail top nav compact. Getting Started should not consume primary detail-nav space after it
  stops being operationally relevant.
- Keep `New Request` available where it is currently available.
- Preserve mobile behavior.

Acceptance criteria:

- Desktop request list and request detail no longer show the left sidebar.
- Settings/getting-started pages still have working navigation.
- Requests, Getting Started, Settings, and New Request routes/actions still work according to role.
- No backend/API behavior changes.
- TypeScript passes.

Suggested verification:

```text
cd web/ophalo-app
pnpm typecheck
```

### S24b — Convert request detail body to 70/30 desktop grid

Goal: Replace the fixed-width desktop action rail with a true responsive 70/30 layout.

Scope:

- In `RequestDetail.tsx`, convert the detail body wrapper from flex/fixed rail to desktop grid.
- Use a 70/30 ratio with a protected side-panel minimum width.
- Keep mobile stacked rendering.
- Preserve independent scrolling if the current UX depends on it, but avoid scroll traps.
- Ensure timeline and cards do not overflow the main column.

Acceptance criteria:

- Desktop body visually reads as two columns.
- Main column gets roughly 70% of the request-detail width.
- Context/action panel gets roughly 30% and remains usable at tablet/desktop breakpoints.
- Mobile remains one column.
- TypeScript passes.

### S24c — Split original request/context ownership

Goal: Move mixed metadata out of `OriginalRequestCard` so the main column contains the customer story
and the side panel contains context.

Scope:

- Keep customer description/original request in the main column.
- Move contact details, service location, customer signal, internal priority, and source/submitted
  metadata into side-panel sections.
- Preserve service-location add/edit behavior and optimistic priority behavior.
- Preserve contact launch tracking behavior.
- Include practical customer contact affordances where data exists: copy phone/email, call link,
  email link, and log contact.
- Preserve ADR-433 distinction between customer urgency and business priority.

Acceptance criteria:

- Customer description is no longer visually competing with contact/location metadata.
- Side panel clearly separates:
  - Customer;
  - Service location;
  - Triage / Customer signal / Internal priority.
- Add/edit location still updates detail state.
- Priority dropdown still updates detail state.
- Email/contact launch still opens the contact-log flow where applicable.
- Contact/location context is usable for an owner/admin who wants to call or email immediately.
- TypeScript passes.

### S24d — Move send-update composer into the main work loop

Goal: Put customer update composition directly above activity on desktop.

Scope:

- Render `BusinessUpdateSection` in the main column above the Activity card on desktop.
- Keep it before Activity on mobile.
- Avoid duplicate composers at a single breakpoint.
- Preserve draft state while navigating within the same detail render.
- Preserve active-attention highlighting semantics from existing `highlights.sendUpdate`.
- Rename or adjust the default Activity filter so internal notes are not implied to be
  customer-visible communication. Preferred default label: **Conversation & notes**.

Acceptance criteria:

- Staff can read/write customer communication in one vertical flow.
- Active attention still promotes send-update when appropriate.
- The right panel no longer uses the update composer as a generic rail item.
- Activity filtering remains understandable after internal notes are restored.
- TypeScript passes.

### S24e — Rebalance actions, lifecycle, and utilities

Goal: Make the right panel context-first while keeping valid actions discoverable.

Scope:

- Keep ADR-434 lifecycle behavior:
  - `Mark work done` for eligible no-attention completion path.
  - `Close request` only when `canClose` allows it.
  - active attention remains primary.
- Decide whether `WorkDoneCard` / `CloseRequestCard` belongs in the request header, main column, or
  top of side panel based on current state.
- Keep `LogContactCard`, `MarkHandledCard`, `FeedbackReviewSection`, admin actions, and team controls
  available without making the panel feel like a random stack of cards.
- Make `Log contact` available whenever `detail.availableActions.canLogExternalContact` is true.
  Promote it only when `highlights.logContact` says it is the recommended attention-resolution action.
- Restore a standalone internal comments/instructions composer when
  `detail.availableActions.canAddInternalNote` is true.
- Add or confirm an API client helper for `POST /keep/requests/{requestId}/internal-notes` using
  `X-Keep-Request-Version: {detail.version}` and body `{ note }`.
- Use `detail.validation.internalNoteMaxLength`; preserve unsaved note text on `409
  KeepRequest.RequestChanged` the same way other stale forms preserve user-entered text.
- Internal notes are internal-only, must not send customer-visible messages, and must appear in the
  operator timeline as `internal_note_added`.
- Label internal note surfaces clearly with **Not visible to customer**.
- Keep customer-visible update, internal note, external contact summary, feedback review note, and
  attention clear reason visually distinct.
- For cancellation/spam/test/classification actions, either expose them in a compact **Admin actions**
  side-panel section when server metadata allows them, or explicitly defer them in implementation
  notes if no safe UI exists yet.
- Prefer grouped side-panel sections with compact headings.

Acceptance criteria:

- The next valid primary action is obvious.
- Attention-resolution actions remain visually ahead of work completion when attention exists.
- Closeout does not appear for ineligible requests.
- Staff have an obvious way to record internal comments/instructions on a request when allowed.
- Internal note creation updates local detail state with the returned DTO and shows the new timeline
  event.
- Internal note submission is unavailable when `canAddInternalNote` is false.
- Staff can log a normal outside contact even when the request does not currently need attention,
  provided server metadata allows it.
- Private/internal writing surfaces cannot be mistaken for customer-visible updates.
- Admin-only terminal/classification actions are either intentionally exposed or explicitly deferred,
  not accidentally hidden in the customer composer.
- Utilities are grouped and scannable.
- TypeScript passes.

### S24f — Restore timing controls

Goal: Let eligible staff set and clear Follow Up On and Planned For from request detail.

Scope:

- Add or confirm API client helpers for:
  - `PUT /keep/requests/{requestId}/follow-up-on`;
  - `DELETE /keep/requests/{requestId}/follow-up-on`;
  - `PUT /keep/requests/{requestId}/planned-for`;
  - `DELETE /keep/requests/{requestId}/planned-for`.
- Use `X-Keep-Request-Version: {detail.version}`.
- Render timing controls only from server action metadata:
  - `detail.availableActions.canSetFollowUpOn`;
  - `detail.availableActions.canSetPlannedFor`.
- Follow Up On control supports date, reason, optional note, clearing, and
  `detail.validation.followUpNoteMaxLength`.
- Planned For control supports date and clearing.
- Make the UI explicit that timing is internal/staff-owned and does not automatically notify the
  customer.
- Preserve unsaved timing form input on `409 KeepRequest.RequestChanged`.

Acceptance criteria:

- Eligible staff can set/clear follow-up timing from detail.
- Eligible staff can set/clear planned date from detail.
- Ineligible users do not see editable timing controls.
- Successful timing writes update local detail state and timeline.
- Timing controls remain compact in the side panel and coherent in mobile stacking.
- TypeScript passes.

### S24g — Request list workbench layout and quick actions

Goal: Make the request list match the request-detail workbench and expose safe quick actions so staff
do not have to open detail for routine confident work.

Current corrective note:

- The S24g deep-link/focus implementation is a temporary safety fallback only.
- Do not mark S24g product-complete until S24g2/S24g3 resolve GAP-007 or explicitly defer true list
  mutations with Owner/Admin workflow sign-off.

Scope:

- Update the request list desktop shell/layout to visually match request detail:
  - same top nav;
  - same content width language;
  - same typography and operational density;
  - no persistent left sidebar on request-list routes if detail no longer uses it.
- Keep tabs, search, status filter, summary pills, pagination, and empty/error states working.
- Redesign `RequestRow` so row hierarchy mirrors detail:
  - identity/status;
  - attention and triage signals;
  - next safe action;
  - context metadata;
  - quick actions.
- Render server-provided `row.actions.quickActions` as executable row controls where safe.
- Preserve `Open detail` as the safe fallback.
- For quick actions requiring a versioned mutation, either fetch detail before submitting/showing the
  modal or open detail focused on the action. Do not invent a client version.
- Keep customer-visible, internal-only, contact-log, clear-attention, and status-changing effects
  visually distinct.
- On mobile/narrow widths, collapse row quick actions into a compact action menu or stacked action
  area that does not break row scanning.

Expected row quick-action behavior:

- `open_detail`: opens detail.
- `contact_customer`: launches/logs contact flow or opens a compact contact action surface.
- `post_customer_update`: opens a customer-visible update composer/modal, with clear visibility copy.
- `acknowledge_attention`: opens a clear-attention confirmation/note flow when server says allowed.
- `review_feedback`: opens detail or a focused review flow; do not mark reviewed without context.
- `close_request`: only if server metadata safely exposes it for Ready to Close; otherwise keep
  closeout detail-first.

Acceptance criteria:

- Request list and request detail feel like the same workbench system.
- Staff can complete routine safe actions from list rows when server metadata supports them.
- Staff can still open detail for careful work.
- List rows remain scannable and do not become oversized cards.
- Quick actions do not rely on client-inferred permissions or stale concurrency assumptions.
- Quick-action copy communicates whether the action is customer-visible, internal-only, contact-log,
  attention-clearing, or status-changing.
- TypeScript passes.

### S24g2 — Request list action contract

Goal: Add the list summary metadata required for safe inline request-list mutations.

Scope:

- Add `version` to the request list summary DTO/read model so list mutations can send
  `X-Keep-Request-Version`.
- Add or confirm server-authored quick-action metadata:
  - `requiresVersion`;
  - `executionMode` (`inline`, `modal`, or `detail`);
  - `customerVisible`;
  - `internalOnly`;
  - `clearsAttention`;
  - `changesStatus`.
- Keep server action metadata authoritative; do not create client-only eligibility rules.
- Update backend mapping/tests for list summaries.
- Update `ophalo-app` TypeScript types and mocks.
- Preserve existing list behavior for roles/views that do not receive a quick action.

Acceptance criteria:

- Request list rows expose a concurrency version suitable for versioned row-level mutations.
- Quick actions state whether they are inline/modal/detail flows and communicate their effects.
- No inline list mutation uses inferred eligibility.
- Backend/API tests cover the new summary contract.
- PWA typecheck passes.

### S24g3 — True inline request-list actions

Goal: Convert eligible request-list quick actions from temporary deep links into compact row-level
controls/modals.

Scope:

- Implement row-level or overlay-modal flows for:
  - send customer update;
  - log external contact;
  - add internal note;
  - assign/self-assign/watch where the server exposes it;
  - clear/acknowledge simple attention where server metadata says it is safe;
  - close request only from Ready to Close with explicit confirmation.
- Preserve detail/focus navigation for:
  - feedback review;
  - cancellation;
  - spam/test/classification;
  - service-location edits;
  - timing controls;
  - generic status changes.
- Use `row.version` or server action token metadata for every versioned mutation.
- On success, refresh or reconcile the affected list row so the queue remains trustworthy.
- Preserve drafts on `409 KeepRequest.RequestChanged` and tell the user to refresh/open detail.
- Keep visibility copy explicit: customer-visible update, internal-only note, external contact log,
  attention-clearing action, or lifecycle/status change.

Acceptance criteria:

- Owners/Admins can perform routine quick actions from the list without open-detail/back loops.
- Detail remains available and preferred for context-heavy or accountability-heavy work.
- Row actions do not bypass concurrency, role policy, or server action metadata.
- List rows remain scannable at desktop and mobile widths.
- TypeScript passes.

### S24h — Responsive QA and polish

Goal: Verify the new layout at realistic widths and clean up visual regressions.

Scope:

- Test desktop, narrow desktop/tablet, and mobile widths across request list and detail.
- Check long customer names, long emails, long addresses, and long descriptions.
- Check requests with:
  - active attention;
  - no attention;
  - work completed / ready to close;
  - missing service location;
  - customer urgency and business priority both present;
  - no customer email or no phone;
  - active timing values and no timing values;
  - internal notes present in activity;
  - contact logging available without active attention.
- Check request list rows with:
  - many badges;
  - no quick actions except open detail;
  - multiple quick actions;
  - unassigned and assigned states;
  - ready-to-close state;
  - missing phone/email/service location.
- Confirm no text overlap or clipped action controls.

Acceptance criteria:

- No incoherent overlapping UI.
- Main column and side panel keep stable widths.
- Mobile order is coherent and single-column.
- TypeScript passes.
- If a local browser/dev server is available, capture manual screenshots or note inspected viewport
  sizes in this build log.

---

## Suggested Implementation Order

Claude should complete these sessions in order:

1. S24a shell/top-nav behavior.
2. S24b 70/30 grid.
3. S24c context split.
4. S24d composer/activity proximity.
5. S24e action rebalance.
6. S24f timing controls.
7. S24g request list layout and temporary quick-action focus fallback.
8. S24g2 request list action contract / GAP-007.
9. S24g3 true inline request-list actions.
10. S24h QA/polish.

Do not combine S24a through S24g into one large patch unless explicitly directed. Each session should
end with a working, typechecked app state.

---

## Non-Goals

- Do not change backend request status names or enums.
- Do not change API contracts unless a request-list quick action cannot be made safe from existing
  metadata.
- Do not merge customer urgency and internal business priority.
- Do not redesign native mobile request detail.
- Do not add new permission logic in the client.
- Do not remove existing server-driven action gating.
- Do not create a marketing/landing-page style hero.

---

## Open Questions

These should be resolved before or during S24a/S24b:

1. Should the top nav be request-workbench-only for now, or should it become the global desktop shell
   across the PWA?
   - Recommendation: request list + request detail first, because those surfaces need to match and it
     minimizes settings/getting-started blast radius.

2. Should primary lifecycle actions live in the request header or as the first item in the main work
   column?
   - Recommendation: header when the action is a clean state transition such as **Close request**;
     main column/attention area when the action needs explanatory context.

3. Where should `Log contact` live when there is no active attention?
   - Recommendation: make it available from customer/contact context or utilities whenever server
     metadata allows it; promote it into the main work path only when attention guidance says contact
     logging is the primary resolution path.

4. Where should internal comments/instructions live?
   - Recommendation: in the side-panel Utilities/Internal section for normal use, with timeline
     events visible in Activity. If the note is part of active attention resolution, do not pretend it
     clears customer-facing attention unless the server says so.

5. How should cancellation/spam/test/classification actions appear?
   - Recommendation: compact **Admin actions** in the side panel when server metadata allows them, or
     an explicit implementation deferral if no safe V1 UI exists yet.

6. Should side-panel sections be sticky?
   - Recommendation: not in the first pass. Stabilize the layout first, then consider sticky context
     after viewport testing.

7. Which list quick actions can be completed without opening detail?
   - Locked by ADR-435: routine low-risk actions belong on the list once the list row exposes safe
     mutation metadata. Until GAP-007 is fixed, focus/deep-link shortcuts are temporary only.
     Feedback review, cancellation, classification, service-location edits, timing controls, and
     generic status changes stay detail-owned for V1.

8. Should request list use a right-side queue panel?
   - Recommendation: only if it helps scanning or selected-row preview. Do not add a decorative or
     low-value panel just to mimic detail.

---

## Claude Handoff Prompt

Use this prompt when starting implementation:

```text
You are implementing Build Log 081, Session 24, for the OpHalo Keep PWA.

Read docs/build-log/081-session-24-request-detail-2-column-workbench.md first.
Then implement only the named sub-session I give you.

Respect these locked decisions:
- Desktop request list and request detail should become one request workbench system.
- Request list/detail desktop should not keep the persistent left sidebar if that leaves a
  three-column or mismatched feel.
- ADR-435: request list is the speed/action cockpit; request detail is the depth/accountability
  workbench.
- GAP-007: temporary quick-action deep links do not satisfy the final list-action requirement; true
  inline row actions require row-level concurrency/action metadata first.
- Main column owns customer story, attention guidance, update composer, and activity.
- Side panel owns contact, location, customer signal, internal priority, team, timing, and utilities.
- Request list should expose safe server-provided quick actions so routine work does not require
  opening detail.
- List quick actions must not bypass server metadata or concurrency/version safety.
- Restore internal comments/instructions where `canAddInternalNote` allows them; internal notes are
  never customer-visible and do not clear customer-facing attention by themselves.
- Log contact is available whenever server metadata allows it; active attention only changes its
  prominence.
- Restore Follow Up On and Planned For controls where server metadata allows them; timing is
  internal/staff-owned and does not automatically notify the customer.
- Keep customer-visible update, internal note, external contact summary, feedback review note, and
  attention clear reason visually distinct.
- Admin terminal/classification actions must be intentionally exposed or intentionally deferred.
- Do not merge customer urgency and business priority.
- Preserve ADR-434 work-completed/closeout behavior.
- Mobile remains single-column.

Do not make backend/API changes unless the sub-session explicitly asks for them.
Run web/ophalo-app typecheck before finishing when dependencies are available.
```
