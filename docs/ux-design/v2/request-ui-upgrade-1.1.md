# Keep Request UI Upgrade 1.1 — Three-Column Operational Workbench

**Status:** Locked — product-owner approved 2026-09-03  
**Scope:** Authenticated Keep Request Queue + selected Request experience  
**Implementation state:** Production implementation completed 2026-09-03; product-owner visual acceptance pending
**Primary outcome:** An office user can find a Request, perform the next customer or work action, and understand the complete communication and operational history without losing usable vertical workspace.

## 1. Decision summary

At a sufficiently wide application workspace, Keep uses one three-column operational workbench:

```text
┌────────────────────┬────────────────────────────────────────────┬──────────────────────────────┐
│ 1. REQUEST QUEUE   │ 2. ACTIVE REQUEST WORK                    │ 3. REQUEST MEMORY            │
│ 320–360 px         │ fluid; protected minimum 620 px           │ 300–340 px                   │
│                    │                                            │                              │
│ Find and switch    │ Communicate, resolve attention,           │ Communications, request      │
│ work without       │ capture Actual Work, review financials,   │ history, customer/context,   │
│ losing queue state │ and perform the current authorized action │ planning, owner, visits      │
└────────────────────┴────────────────────────────────────────────┴──────────────────────────────┘
```

The columns are not equal:

- The Queue is a bounded navigation and prioritization surface.
- Active Request Work is the dominant flexible column.
- Request Memory is a quiet supporting rail, not a second action dashboard.

The selected Request's identity and frequent-action toolbar stay compact and reachable. The previous large fixed Anchor does not remain pinned. Context that is not required for request identity moves into the right rail or normal scrolling content.

## 2. Relationship to existing locked decisions

Upgrade 1.1 amends only the conflicting desktop composition rules in:

- `request-detail-workbench-signoff-spec.md`, ratified decision 1 and sections 1.1, 1.2, 1.3, 5, 7.1, and related wording that prohibits a right rail;
- `keep-ui-production-decision-register.md`, UI-001's two-pane desktop composition; and
- any earlier visual guidance that treats the entire Request Anchor as the fixed band above the only Work Canvas.

Upgrade 1.1 replaces those rules with the three-column and scroll contracts in this document. It does **not** supersede:

- server-authored action availability and authorization;
- request-version concurrency and stale-write recovery;
- the distinction between launching contact and durably logging contact;
- the distinction between customer-visible updates and internal notes;
- the distinction between sharing a page and proving customer receipt;
- attention precedence and the one-primary-action-at-rest rule;
- Actual Work's factual, price-blind field-capture contract;
- locked submitted visits and internal financial-review authority; or
- the focused single-column mobile Request experience.

No client role, status, or entitlement guess may replace server authority while implementing this layout.

## 3. Product and operational principles

1. **Communication is core work.** Contact launch, durable contact logging, customer updates, internal notes, communication history, and share actions are first-class Request functions.
2. **The next action owns the center.** The main column prioritizes active attention and the work required now. It is not a complete record dump.
3. **The right rail is Request memory.** It answers “what has been said?”, “what has happened?”, and “what context do I need?” without displacing the active task.
4. **Frequent actions remain visible.** Contact and sharing controls do not move into a generic overflow menu merely to make the header visually sparse.
5. **Visual density must not erase meaning.** Use icon-plus-label controls for important actions. Icon-only controls are reserved for conventional, low-risk utilities with accessible names and tooltips.
6. **A launch is not an outcome.** Opening a phone, SMS, or email app does not write a contact event. The user explicitly confirms and saves what occurred.
7. **Work facts and money authority remain distinct.** Actual Work may use a Price Book-associated icon, but field capture does not reveal sell price, cost, margin, tax, discount, quote, or billing data.
8. **History is operational evidence.** Communications, internal notes, request events, and submitted work remain attributable, timestamped, and permission-aware.
9. **Customer Need is permanent operating context.** The reason for the Request is never hidden behind a right-rail tab and remains readable while the user works or audits the Request.

## 4. Desktop layout and scroll contract

### 4.1 Wide-workbench eligibility

Render all three columns only when the available application workspace can protect:

- Queue: 320–360 CSS px;
- Active Request Work: at least 620 CSS px;
- Request Memory: 300–340 CSS px;
- dividers and gutters without horizontal page scrolling.

This is a usable-container rule, not a device-name assumption. Verify it at 100%, 125%, and 150% browser zoom. When the protected widths do not fit, use the responsive adaptations in section 12.

### 4.2 Scroll ownership

- The global application navigation does not scroll with Request content.
- The Queue has one independent vertical scroll surface below its filter/search controls.
- The selected Request workspace has one vertical scroll surface shared by the Active Request Work and Request Memory columns.
- The compact Request identity/action strip may be sticky within the selected Request workspace.
- The right rail must not create a competing page-height nested scroll surface.
- Drawers, dialogs, disclosures, long select lists, and textareas may manage their own bounded content when open; they do not alter page-level scroll ownership.

### 4.3 Compact sticky strip

The sticky strip contains only:

- Back/queue continuity and previous/next controls where already supported;
- request reference, customer/request name, lifecycle status, and concise active-attention cue;
- a compact Customer Need summary that remains present regardless of the selected Request Memory tab;
- customer-page viewed state where available;
- the server-authorized current primary action; and
- the frequent Request toolbar defined in section 6.

The Customer Need summary uses a deliberate one- or two-line presentation in the sticky strip. When the source text exceeds that space, the full value is available through an accessible expand/disclosure without changing tabs. Contact data, service location, owner/team, planning controls, visit history, and full attention explanation do not make the sticky strip taller. They live in the right rail or the active module that gives them meaning.

Target the normal sticky-strip height at approximately 120–160 px after safe wrapping, including the compact Customer Need summary. The frequent toolbar may use no more than two control rows at the protected 620 px center-column minimum. The strip must not consume roughly half of a 768–900 px-tall office viewport.

## 5. Desktop information architecture

### 5.1 Column 1 — Request Queue

The Queue retains its complete operational controls and existing role-aware behavior:

- the three primary queue scopes and their authoritative counts;
- Owner/Admin Office Review and its authoritative aggregate/member counts where applicable;
- Views, including active-view naming;
- History entry point;
- search and filters;
- filtered-empty versus truly empty recovery;
- server ordering, paging/loading, update-available behavior;
- selected-row indication; and
- selection, filter, search, and scroll continuity during request navigation.

For Owner/Admin, the primary scopes remain **Needs Attention**, **All Work**, and **My Work**. For other roles, retain the role-specific locked labels and ordering. Upgrade 1.1 does not reduce the Queue to the abbreviated controls shown in a visual mockup.

The Queue's controls stay fixed above its scrolling results. Queue and selected Request scroll independently so an office user does not lose their place while reviewing a long Request.

### 5.2 Column 2 — Active Request Work

The center column owns work that can change the Request or move the customer promise forward:

1. active attention and its authorized resolution action;
2. active customer response/update composition, integrated with the attention surface when the attention reason is a customer message;
3. Actual Work draft state and capture/resume entry;
4. pending internal financial review for authorized Owner/Admin users;
5. contextual lifecycle action, including Mark work done or Close when server-authorized;
6. follow-up resolution when it is the current work; and
7. scoped success, conflict, error, and recovery states.

Modules that do not exist or are unauthorized are omitted; do not render empty placeholders to preserve symmetry.

When active attention is a customer message, the attention header and response composer form one composed surface. **Respond now** expands and focuses the composer directly inside or immediately attached to that surface; it does not send the operator to a separate card. The textarea and submit action must remain reachable through the selected Request's normal scroll surface at the acceptance viewports. Other attention reasons continue to use the server-authorized action and containment appropriate to their meaning.

### 5.3 Column 3 — Request Memory

The right rail uses three top-level tabs:

1. **Communications** — default when the Request has communication or note history, or when active attention is communication-driven;
2. **Request history** — complete operational/audit sequence available to the current user; and
3. **Details** — customer/context, planning, team, and visit reference information.

Tab selection is local presentation state and does not mutate the Request. The rail keeps the user's selected tab during ordinary Request mutations and while the user moves between Requests in the Queue during the same session. This supports batch communication, history, and detail review without repeated tab selection. A new session starts with the context-appropriate default, and a tab the current user cannot access falls back to the first permitted tab.

## 6. Frequent Request toolbar

The toolbar is a compact action/link row, not a labelled “Request shortcuts” card. It uses real icons from Keep's existing icon system and visible labels. Related actions use tight, co-located control clusters so the toolbar never exceeds two control rows at the protected 620 px center-column minimum. It may wrap within that limit without overlapping, clipping, or shrinking targets below their usable size.

### 6.1 Communication group

| Control | Visual treatment | Operational behavior |
|---|---|---|
| **Contact customer** | Strong persistent secondary action; contact/conversation icon + label | Opens the durable **Contact customer** logging flow. It remains available when authorized even if another action is the single current primary. |
| **Call** | Compact icon + text link | Uses the authorized contact launch/QR flow. Launch is intent only. On return, Keep may offer a non-blocking Log contact prompt. |
| **Text/SMS** | Compact icon + text link | Uses the authorized SMS/QR flow. Include the appropriate share link only when the existing flow does so. Launch is intent only. |
| **Email** | Compact icon + text link | Opens the authorized email draft/launch. Launch is intent only. |

The toolbar must not remove **Contact customer** merely because Call/Text/Email are present. They solve different jobs: launch a channel versus record what happened.

Call, Text/SMS, and Email render as one compact segmented channel-launch cluster beside **Contact customer**. The cluster retains visible text labels and distinct focus/hover states; it is not reduced to unexplained icons.

### 6.2 Sharing group

Keep both distinct share destinations visible when authorized:

- **Share business page** — shares the business's public/general page using the existing authorized destination and share contract.
- **Share customer request page** — shares the customer-facing page for this Request using the authorized token/intent flow.

These controls use one tight, co-located share cluster with share/external-link iconography and visible destination labels. **Business page** and **Customer request page** remain directly identifiable at rest; Upgrade 1.1 does not hide either frequent destination behind a generic Share dropdown. Labels must identify the destination; a generic **Share link** label is insufficient when both destinations exist. Sharing does not prove receipt, delivery, or attention clearance unless the server separately confirms that effect.

### 6.3 Price Book work group

- **Record Actual Work** or **Continue Actual Work** uses a recognizable work-record icon paired with a small Price Book cue such as `BadgeDollarSign`, `CircleDollarSign`, or the established Price Book tag/receipt glyph.
- The text label remains **Actual Work** unless product copy is separately changed. An icon may show that the work items originate in Price Book; it must not imply that the recorder can view pricing.
- **Review financials (N)** uses a dollar/receipt/calculator icon, the authoritative pending count, and internal-review language. It is shown only to users authorized to open that review.
- When attention owns the single visual primary, Actual Work and financial review remain visible but secondary. They must not compete with the attention-resolution control.

### 6.4 Toolbar overflow

The toolbar may place truly low-frequency actions in a labelled **More actions** disclosure. The following must not be hidden there in the normal authorized desktop state:

- Contact customer;
- Call, Text/SMS, and Email when the channel exists;
- Share business page;
- Share customer request page;
- an open Actual Work draft or authorized Record Actual Work entry; and
- a pending authorized financial review.

Unavailable actions are omitted, not rendered as disabled invitations. Loading authorization does not flash speculative actions.

## 7. Contact customer and durable communication logging

The existing Contact customer sheet remains the canonical way to record external communication that occurred in or outside Keep.

Required fields and behavior:

- Direction: **We contacted them** or **They contacted us**.
- Channel: Phone call, Text/SMS, Email, or another server-supported channel.
- Optional/required follow-up state according to the existing contract.
- A meaningful summary, with current validation limits and accessible error messages.
- Explicit **Log contact** submission; Cancel/Close performs no write.
- Call/Text/Email launch affordances may appear in the sheet, including the authorized QR handoff on desktop.
- The sheet clearly states that opening an external draft/application does not update Keep.
- On successful logging, authoritative Request detail/version replaces stale client state and the new event appears in Communications and Request history as appropriate.
- On concurrency conflict, preserve entered summary and selections, stop stale resubmission, and provide refresh/retry recovery.
- Double submission is prevented; pending, success, and failure states are explicit.

The Contact customer toolbar control is not synonymous with “send a customer-page update.” Durable external-contact records and customer-visible Keep updates remain separate event types.

## 8. Communications tab

The Communications tab is the fast, readable history of human communication around the Request.

### 8.1 Included entries

Subject to authorization, include:

- customer messages received through the customer Request page;
- business customer-page updates;
- explicitly logged inbound/outbound calls;
- explicitly logged inbound/outbound Text/SMS;
- explicitly logged inbound/outbound email;
- share-intent events only where useful and supported by the event contract;
- internal notes, clearly marked **Internal only**; and
- communication-related follow-up creation/resolution where it helps explain the sequence.

Each item shows channel/type, direction or visibility, actor/source, timestamp, summary/body preview, and follow-up state when applicable. Unknown future event types use a neutral fallback and never disappear silently.

### 8.2 Filters and actions

Provide lightweight filters:

- **All**;
- **Customer**; and
- **Internal**.

The default is All. Filters do not alter server state. The tab provides visible entry points for **Contact customer** and **Add internal note** when authorized. Do not require an administrator to leave the Request or open a generic overflow menu to add a note.

Long histories use the existing authoritative paging/loading approach. Avoid placing a second free-scrolling timeline inside the rail; the Request workspace remains the scroll owner.

## 9. Request history tab

Request history is the operational/audit narrative rather than a duplicate communication thread. Subject to visibility rules, it includes:

- Request creation and intake source;
- status and lifecycle transitions;
- attention raised, changed, acknowledged, or resolved;
- assignment, owner, watcher, and participation changes;
- internal priority, Planned Work Date, and Internal Follow-up changes;
- service-location changes;
- Actual Work draft/submission/replacement/discard facts that are valid history events;
- internal financial-review completion or correction facts;
- closeout and feedback-review events; and
- communication events where needed to preserve a complete chronological audit.

Entries show who/what performed the action, when it occurred, and a concise description. History must not expose private, financial, or tenant data beyond the current user's permission.

The tab may offer event-type filtering later, but Upgrade 1.1 does not require a new backend audit taxonomy if the unified authoritative timeline already supports the necessary display.

## 10. Details tab

The Details tab holds supporting context and lower-frequency edits:

- customer contact details and communication preference when one exists;
- service location and authorized edit action;
- responsible owner, Watch/Watching, watchers, and authorized assignment/team actions;
- Internal Priority;
- Planned Work Date;
- Internal Follow-up;
- submitted visit history and locked-record indicators;
- customer-page viewed state and appropriate navigation; and
- lower-frequency Request metadata.

Customer Need is intentionally absent from the tab-owned list because its compact summary is permanently visible in the sticky identity strip. The Details tab may provide an additional full-text presentation for deep reference, but that is supplementary and must not be the only way to read the need.

Planning controls remain clearly labelled form controls, not ambiguous chips. Moving them to Details changes placement, not meaning or server behavior. Due/overdue Follow-up can promote an actionable resolution module into the center column while its stored value remains visible in Details.

The right rail shows one tab's primary content at a time. It must not display full Communications plus duplicated Details cards beneath the tabs merely to fill vertical space.

## 11. Visual system and interaction styling

### 11.1 Hierarchy

- Center-column active work receives the strongest card hierarchy and available width.
- The right rail uses quieter surfaces, smaller headings, restrained shadows, and compact spacing.
- The Queue retains clear selected, attention, hover, and keyboard-focus states.
- Teal continues to communicate Keep action/brand emphasis; amber communicates attention/risk; red is reserved for danger/terminal destructive confirmation.
- Financial review uses neutral/navy structure with an amber count or status cue where pending; it is not presented as customer-promise risk.

### 11.2 Controls

- Use buttons for mutations, disclosures, drawers, and stateful client actions.
- Use links for true route navigation and external destinations.
- Icon-plus-label is the default for frequent toolbar actions.
- Icon-only controls require a familiar symbol, at least a 44 px effective target where practical, an accessible name, visible focus, and a tooltip on hover/focus.
- Links must remain recognizable without relying on color alone.
- All controls support keyboard navigation and visible focus; tab interfaces follow the appropriate tab semantics and arrow-key behavior.

### 11.3 Density

- Favor 12–16 px internal spacing in dense desktop cards and 16–20 px between major modules.
- Avoid stacked card-inside-alert-inside-card treatments when one composed surface can communicate the same boundary.
- Toolbar clustering and a controlled second row are preferable to truncating action labels or shrinking targets below usable size. A third toolbar row at the protected center-column minimum is a layout failure and must trigger a more compact approved composition or the responsive presentation.
- Long customer names, email addresses, locations, Customer Need text, and translated copy must wrap or truncate with a discoverable full value.

## 12. Responsive behavior

### 12.1 Intermediate desktop/tablet

When Queue + center + right rail cannot all meet their protected widths:

- retain Queue + Active Request Work when that pair remains usable;
- replace the right rail with a clearly labelled **Request memory** drawer or sheet containing Communications, Request history, and Details tabs;
- keep an unread/new-communication or actionable-history indicator on the drawer trigger when supported by authoritative data; and
- do not squeeze the center below its usable minimum.

When Queue + center cannot both fit, use the existing focused Queue → Request drill-down.

### 12.2 Mobile/focused Request

- Preserve the focused single-column Request route and clear Back to Requests navigation.
- Present the current authorized primary action first.
- Keep Contact customer and available Call/Text/Email actions easy to reach.
- Place sharing and Actual Work entry in the first relevant action group without creating a horizontally scrolling toolbar.
- Use a full-height sheet/drawer for Contact customer and Request memory.
- Actual Work remains a focused workspace with Back to Request.
- Hide or reposition sticky actions while the software keyboard/text input would otherwise obscure content.

Responsive adaptation changes composition, not permissions, event meaning, or save behavior.

## 13. Loading, empty, error, and concurrency states

- Reserve compact, shape-matched loading states for the Queue and rail to reduce layout shift.
- Distinguish no communication yet from communication hidden by permissions or a load failure.
- Distinguish no Request history from a history load failure.
- A rail failure does not block unrelated center-column work unless the failed data is required for that action.
- A selected Request change cancels or safely ignores stale in-flight reads from the previously selected Request.
- All versioned mutations continue to send the latest Request version and adopt the authoritative returned state.
- Typed customer updates, internal notes, and contact summaries survive recoverable validation and concurrency errors.
- Success feedback identifies what was actually saved; it does not claim external delivery or receipt.

## 14. Role and permission expectations

| User | Center work | Request Memory | Financial information |
|---|---|---|---|
| Owner/Admin | All server-authorized communication, lifecycle, Actual Work, and review actions | Authorized communications, internal notes, full permitted Request history and details | May open internal financial review only when server-authorized |
| Office staff/Operator | Authorized communication, assignment/timing, lifecycle, and work actions | Authorized communication/history/detail subset | No inferred financial access |
| Field technician | Customer contact, updates/notes, assigned work, price-blind Actual Work | Only permitted communication/history/detail subset | No sell price, cost, margin, tax, discount, quote, or billing data |
| Viewer/read-only | No mutations | Permitted read-only memory and details | Only explicitly authorized data |

Layout presence never grants authority. Unauthorized controls are omitted.

## 15. Implementation slices

Implement and verify in this order so each slice leaves a coherent product:

1. **Shell and scroll ownership** — introduce three-column eligibility, shared center/right workspace scroll, compact sticky identity strip, and responsive rail fallback. Preserve Queue continuity.
2. **Right-rail information architecture** — Communications, Request history, and Details tabs using existing authoritative data; move context without duplicating it.
3. **Frequent Request toolbar** — Contact customer, clustered channel launches, the directly identified share destinations, Actual Work, and financial-review actions with correct semantics, density, and server gates.
4. **Communication completeness** — integrated customer-message attention/composer, durable external-contact logging entry, internal-note entry, communication filtering, mutation refresh, and conflict recovery.
5. **Visual refinement** — icon system, hierarchy, wrapping, dense states, focus behavior, and removal of obsolete duplicated modules.
6. **Responsive and accessibility verification** — drawer/focused variants, keyboard/tabs, zoom, screen-reader naming, and realistic-data overflow.

Each slice updates tests and relevant decision/build logs. Do not keep old and new placements mounted simultaneously beyond a short implementation transition.

## 16. Acceptance criteria

### Layout and usability

1. At a qualifying wide workspace, the visible structure is Queue | Active Request Work | Request Memory, with the center visually dominant.
2. At 1366×768 and 1440×900 at 100% zoom, activating the customer-response composer allows both its input and submit action to be reached within the selected Request's normal scroll surface.
3. At 1920×1080, the layout uses available width instead of stacking all context below the center work while leaving a large empty canvas.
4. At 100%, 125%, and 150% zoom, the layout switches presentation before any column becomes operationally unusable or creates horizontal page scrolling.
5. The Queue filter/search controls remain reachable while Queue results scroll independently.
6. The compact Customer Need summary remains readable without selecting a Request Memory tab, and long text has an accessible full-value path.
7. At the protected 620 px center-column minimum, the complete authorized frequent toolbar occupies no more than two control rows.

### Queue preservation

8. The role-appropriate primary queue scopes, authoritative counts, Office Review, Views, History, search, filters, and continuity behavior remain intact.
9. Switching Requests preserves the applicable Queue view, filters/search, scroll position, and selected Request Memory tab for the session.

### Communication

10. An authorized business user can start Contact customer from the toolbar, record direction/channel/follow-up/summary, save once, and see the resulting event without leaving the Request.
11. Call, Text/SMS, and Email launches remain visible as a compact channel cluster when available and never auto-log contact.
12. Share business page and Share customer request page are both directly identifiable, distinctly labelled, permission-gated, and use their existing destinations/contracts.
13. Communications shows permitted customer messages, business updates, logged external contact, and internal notes with visible type/visibility, actor/source, and timestamp.
14. Add internal note remains easy to reach and always discloses **Internal only**.
15. Customer-message attention expands the response composer within the same composed surface and focuses it without navigating to a detached card.

### Work and financial review

16. Actual Work entry is visibly associated with Price Book work through iconography while field capture remains price-blind.
17. Pending financial review shows the authoritative count and is available only to authorized users.
18. Active attention retains visual and action precedence over Actual Work, financial review, and ordinary lifecycle completion.

### History and details

19. Owner/Admin can inspect a coherent Request history containing permitted lifecycle, ownership, planning, communication, work, and review events.
20. Customer Need, service location, owner/team, planning, and submitted visit history remain available after leaving the former fixed Anchor.
21. Communications, Request history, and Details do not render duplicate full-content stacks beneath the selected tab.

### Resilience and accessibility

22. Authorization loading/changes never expose speculative actions.
23. Stale-version conflicts preserve unsaved text and selections and offer explicit recovery.
24. Keyboard users can operate the Queue, toolbar, tabs, work modules, drawers, and dialogs with visible focus.
25. Important toolbar actions have visible text; any icon-only utility has an accessible name and tooltip.
26. Empty, loading, hidden, and failed history/communication states are distinguishable and do not block unrelated permitted work.

## 17. Pre-code verification checklist

Before the first production edit:

- map each toolbar action to its existing route, drawer, mutation, authorization flag, and test coverage;
- verify the exact existing contract/destination for **Share business page** separately from **Share customer request page**;
- inventory the event types currently available to Communications and Request history;
- identify whether the right-rail filters can be derived safely from current event metadata;
- confirm that financial-review counts and navigation remain authoritative;
- confirm container-width behavior with the real global navigation and Queue rather than a standalone mock;
- capture baseline screenshots at 1366×768, 1440×900, and 1920×1080; and
- record any backend/data gap before designing a client-side approximation.

## 18. Explicit non-goals

Upgrade 1.1 does not introduce:

- a dispatch calendar or scheduling board;
- automatic logging of phone, SMS, or email launches;
- customer-delivery/read claims not supported by the server;
- a client-authored permission or status engine;
- pricing in field Actual Work capture;
- a second generic activity store;
- a permanently visible fourth detail column;
- horizontally scrolling action toolbars; or
- removal or simplification of existing Queue operations.

## 19. Signoff

Product-owner approval on 2026-09-03 locks the three-column desktop direction and authorizes implementation discovery/slicing. Any discovered API gap that changes action meaning, auditability, privacy, or authorization returns to product review rather than being filled by a visual-only client assumption.
