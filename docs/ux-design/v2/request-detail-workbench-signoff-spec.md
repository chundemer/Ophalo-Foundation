# Request Detail / Workbench — Production Interaction Specification

**Status:** Locked — approved 2026-08-22; amended 2026-08-22 for Actual Work-only pilot scope  
**Purpose:** One implementation-facing specification of the already locked Request Detail decisions.
**Authority:** UI-001 through UI-013 in the [V2 Decision Register](keep-ui-production-decision-register.md), plus ADR-380, ADR-434 through ADR-441, ADR-482, ADR-487, and server-authored detail/action metadata. Where a server response does not authorize an action, this specification requires the UI to omit it.

## Ratified interaction decisions

The product owner approved the following reconciled interaction model on 2026-08-22:

1. Two-pane desktop Queue + Workbench shell, with no third desktop detail column.
2. One Workbench Work Canvas scroll surface beneath a sticky Request Anchor; the Queue scrolls independently.
3. An adaptive two-to-three-row Request Anchor that keeps customer, reference, status, attention,
   phone, email, service location, responsible owner, and one server-authorized primary action
   immediately findable without clipping boundary-valid content.
4. Distinct action lanes: one outcome primary, quick contact, explicit durable contact logging,
   communication, contextual work/timing, and quiet utilities.
5. Contact launches/copy are intent only; desktop Call/Text use the authorized opaque QR handoff;
   durable contact logging is explicit.
6. Update customer, View customer page, and Share customer page are separate actions with separate
   effects. Sharing uses only the authorized copy/native/manual intent contract and never proves
   customer receipt.
7. One inline communication composer with explicit Customer update/Internal note mode and persistent
   visibility disclosure.
8. The controlled-pilot Workbench includes Actual Work only, as a conditional focused workspace;
   it is factual and price-blind. Proposed Scope is explicitly deferred from this go-live surface.
9. Exactly one server-authored primary at rest; active attention precedes work completion; Keep teal
   is reserved here for customer-visible update action and active customer-visible composition.
10. Follow Up On is promise protection with Complete/Move/Keep active resolution; Planned For is
    internal timing context, not a scheduling system; Close request is confirmed, Owner/Admin-only,
    and server-authorized.
11. Narrow/mobile uses a focused single-column Request presentation with one dominant permitted
    action and a full focused workspace for Actual Work.

## 1. Product decisions

1. Desktop is a two-pane workbench only when the application workspace is at least 1001 CSS px: a 320–360 px Request Queue, a 1 px divider, and a protected 640 px Workbench. Otherwise use one-pane Queue → Request drill-down. There is no manual Queue collapse control in this release.
2. The Queue and the Workbench may each have one independent page-level scroll surface. Inside the Workbench, there is exactly one vertical scroll surface: the Work Canvas. Do not introduce a scrollable desktop right rail, stacked pane, or card body.
3. A selected Request has a compact sticky Request Anchor above the Work Canvas. It is neither a dashboard nor a permanently oversized header.
4. Without scrolling, the Anchor exposes: customer name, request reference, status, active attention/risk (when present), the current server-valid primary action, phone, service location, and responsible owner.
5. The Work Canvas order is fixed by operational urgency: active attention guidance; original customer need; authorized active work context; customer communication/private-note composition; activity/history; lower-frequency record context.
6. At rest there is one enabled local-task primary. The server determines whether it is an attention-resolution or lifecycle action. A valid customer-update composer temporarily owns primary emphasis. The client never fabricates a recommended action, transition, clearance effect, or permission.
7. Customer updates and internal notes share a composition region but never share meaning: updates say **Visible on the customer page**; notes say **Internal only**. Actual Work is factual and price-blind. Proposed Scope is outside this controlled-pilot Workbench scope.

## 2. Users and top workflows

| User | Primary job | Top Workbench flows | Never implied by the UI |
|---|---|---|---|
| Office staff / Operator | Maintain the customer promise on Requests they may operate | assess attention; contact/update; record private context; assign/self-assign where authorized; set follow-up/planned timing; mark work done | closeout authority, pricing visibility, or a status transition not sent by the server |
| Owner/Admin | Triage account work and own business accountability | everything above where authorized; reassign; edit location/priority; close completed calm work; review authorized Actual Work signals | automatic clearance of attention, customer delivery/read confirmation, or a customer acceptance decision |
| Field technician | Execute one assigned promise safely in the field | open My Work; call/text/email or map; return and truthfully log contact; send update/add note; capture Actual Work in a focused workspace; mark work done | office quote/pricing/cost/margin access, dispatch-board behavior, Proposed Scope capture in this pilot surface, or closing Requests |
| Viewer / read-only user | Understand permitted work | inspect permitted Request and history | every mutation; unavailable controls must not be shown as disabled invitations |

## 3. Action placement model

**Placement terms:** Primary means the single server-authorized current-workflow button in the Anchor. Persistent secondary means visible in the Anchor context strip or the first canvas screen, not in a catch-all menu. Contextual means visible only inside the module that gives it safe meaning. Utility means subdued and discoverable, not a routine-work hiding place. “Unavailable” means omitted, not disabled, until the server explicitly permits it.

| Supported action | Placement | Containment / rule |
|---|---|---|
| Call | Persistent visible secondary in Anchor phone context | Launch is intent only; return may offer one non-blocking **Log contact** prompt. Desktop may use authorized QR handoff. |
| Text | Persistent visible secondary in Anchor phone context when a valid number/affordance exists | Same intent-only/logging rule as Call. |
| Email | Persistent visible secondary in Anchor contact context when a valid address/affordance exists | Same intent-only/logging rule as Call. |
| Log external contact | Persistent visible secondary; may be the server-authorized primary when attention metadata explicitly recommends it | Drawer (full-height on mobile); durable log only after explicit confirmation. |
| Send customer update | Visible first-canvas composition action; teal submit is primary only while its composer is active | Inline; label **Visible on the customer page**. Do not claim delivery/read/realtime. |
| Add internal note | Visible first-canvas composition action, adjacent to update but visually secondary | Inline; label **Internal only**. |
| Assign / reassign responsible owner | Persistent visible secondary in Anchor owner context | Inline assignment control. Omit when unauthorized. |
| Watch / unwatch | Low-frequency utility near Anchor participation context | Quiet control; not hidden among routine work. |
| Mute / unmute | Low-frequency utility near Watch state | Quiet control; only where the server permits the current participant. |
| Set Follow Up On | Persistent visible secondary in first-canvas timing context | Inline; requires date + reason; note required for `other`. |
| Complete or move Follow Up On | Relevant contextual module: active/due/overdue Follow Up card | Narrow resolution flow: complete (reason), move, or retain after activity. No silent clear. |
| Set/change/remove Planned For | Persistent visible secondary in first-canvas timing context | Inline timing control; past date shows **Planned date passed**, not a missed customer promise. |
| Clear / acknowledge authorized attention | Primary only when server-authorized as the current attention-resolution action; otherwise its explicit attention-guidance module | Attention guidance explains **Why** and **Resolve by**. Never a generic client “Resolve” action. |
| Capture/view Actual Work | Relevant contextual module only when entitled and authorized; capture opens as focused workspace | Price-blind factual capture. No right-rail card/composer. Show actual-work review/history only when authorized. |
| Mark work done | Anchor primary when server authorizes completion and no active attention | If completion is authorized during attention, it may appear only as an explicit demoted warning action: **Mark work done, attention remains**. |
| Close request | Relevant closeout module / Anchor primary only when server authorizes `canClose` | Owner/Admin only; red filled action plus confirmation. Never a routine “More” action. |
| Edit service location | Relevant contextual record-details module | Inline edit. Detail-owned; no generic Queue mutation. |
| Set internal priority | Relevant contextual record-details module | Inline edit. Must remain visually and semantically distinct from customer urgency. |
| Share customer-page link | Low-frequency utility in Anchor | Use server-authorized share intent/page token. Never show raw token; sharing does not prove customer receipt or clear attention unless confirmed by the server’s separate flow. |
| Generic status change | Relevant contextual lifecycle module only when server exposes it | Detail-owned; do not turn status into a default action menu. |

## 4. Lifecycle and attention matrix

| Request state | Immediate hierarchy | Primary-action rule | Timing / attention variants | Prohibited implication |
|---|---|---|---|---|
| Received | Anchor facts then original need; attention guidance first if present | Server-valid attention action if active; otherwise server-valid current work action, if any | Future Follow Up quiet; due/overdue Follow Up is active attention; past Planned For is a timing/status-check cue | Receipt is not scheduling, contact, or ownership. |
| Scheduled | Same, with Planned For visible as internal timing context | Server-valid action only | Future Planned For quiet; today visible; past shows **Planned date passed** and prompts mark done/move/remove | Scheduled is not a customer commitment display unless separately communicated. |
| Active (backend `InProgress`/other active state) | Attention first; then original need and active work context | Server-valid attention action takes priority; otherwise current authorized work action | Same Follow Up / Planned For rules; show Actual Work only if real and authorized | Work activity does not prove customer informed or attention resolved. |
| Work completed (backend `Resolved`) | Completion fact plus any active attention; activity/history remain available | **Close request** only when Owner/Admin and server says `canClose`; otherwise no invented next state | Active attention blocks closeout and remains primary. Follow Up/Planned For are active-request-only controls and therefore unavailable. | Resolved is not Closed and does not open customer feedback. |
| Closed | Read-only completed record; show unresolved feedback review only where authorized | No lifecycle primary. Owner/Admin may see authorized feedback-review action. | Terminal transition clears normal active attention. Closed unresolved feedback is a distinct Owner/Admin review state, not reopened work. | Closed does not mean the customer received a particular update, and feedback review does not reopen it. |

**Attention precedence:** An active attention reason always precedes ordinary completion in visual hierarchy. Server-authored guidance controls the reason and resolution effects. Stronger attention may supersede due/overdue Follow Up. Customer-reported urgency and internal priority are signals, not attention by themselves. Amber communicates risk; it is never a filled action.

## 5. Desktop Workbench wireframe

```text
┌──────────────────── Request Queue (320–360 px) ───────────────────┬──────────── Selected Request Workbench (min 640 px) ────────────┐
│ Needs Attention                                                     │ STICKY REQUEST ANCHOR (not independently scrollable)               │
│ All Work | My Work                                                  │ Customer name · Request ref · source/submission · Status            │
│ Office Review (Owner/Admin, conditional)                            │ Active attention/risk, if any       [server-valid primary action]  │
│ Views | History · Search/filter · ranked Request rows               │ Phone: Call / Text | Service location: Maps | Owner: Assign         │
│                                                                      ├───────────────────────────────────────────────────────────────────┤
│ [Queue has its own scroll surface]                                  │ WORK CANVAS — the Workbench's only vertical scroll surface          │
│                                                                      │  1. Needs Attention: Why / Resolve by / authorized action           │
│                                                                      │  2. Customer's original need (verbatim)                             │
│                                                                      │  3. Authorized, real Actual Work context                             │
│                                                                      │     (omit when absent; capture opens focused workspace)              │
│                                                                      │  4. Communicate: [Customer update] [Internal note]                  │
│                                                                      │     explicit customer-visible/internal disclosure                    │
│                                                                      │  5. Timing: Follow Up On / Planned For (when permitted)              │
│                                                                      │  6. Activity and history                                              │
│                                                                      │  7. Record details: location, internal priority, watch/mute, share  │
└────────────────────────────────────────────────────────────────────┴───────────────────────────────────────────────────────────────────┘
```

No desktop sidebar, right rail, or action-card dashboard is permitted within the Workbench. The Anchor's compact context strip replaces the screenshot’s scrollable secondary column. Routine actions remain visible where their context is first needed; low-frequency utilities are quiet, not a hiding place for normal operations.

## 6. Responsive and mobile adaptation

1. Below the protected desktop workspace, use a focused request route with a clear return to Requests; retain the same detail/action/version engine.
2. Keep customer, phone, service location, original need, and one safe currently permitted action in the first useful viewport.
3. Use a persistent 48 px+ mobile action area only for actual permitted work (for example Call, Maps, Send update, Log contact, or capture). One action remains dominant. Hide/unpin the bar while a text input is focused.
4. Customer update and internal note remain inline. Log Contact and similar focused edits use a full-height drawer. Close, discard, required attention reason, and conflict recovery use a dialog. Actual Work uses a full focused workspace with Back to Request.
5. Phone/text/email launches remain intent only. On return, one replaceable, non-blocking prompt may offer **Log contact** or **Dismiss**. Maps never prompts to log contact.
6. Authorization changes available content/actions, not the truth of the layout. Field capture remains price-blind for every recorder, including Owner/Admin.

## 7. Acceptance criteria for frontend implementation

1. The Workbench has one and only one vertical canvas scroll surface; no scrollable internal desktop action/details rail exists.
2. At normal zoom and 100%, 125%, and 150%, customer, status, attention, phone, location, owner, and the current authorized primary action are discoverable without scrolling.
3. Every rendered mutation is present only when the latest server detail/action metadata permits it; returned authoritative detail replaces stale local state after mutation.
4. The UI never equates a launch with contact, work completion with attention clearance, `Resolved` with `Closed`, or customer update with delivery. Actual Work remains factual and price-blind.
5. The screen shows at most one enabled local-task primary at rest. Customer update submit gains primary emphasis only during valid composition; terminal Close is red and confirmed.
6. Active attention appears before completion and explains Why / Resolve by. Completion during attention is not a normal primary card.
7. Empty Actual Work context does not render as a placeholder. Authorized capture opens its dedicated focused workspace.
8. Customer-visible and internal writing have explicit, persistent disclosure. Price/cost/margin/tax/discount/quote/billing data never appears in field capture.
9. Follow-up set requires date/reason; completion/move uses the defined resolution flow. Planned For has no completion flow and uses past-date wording specified above.
10. Unauthorized/read-only actions are omitted. Loading suppresses actions until authorization is known. Errors, pending writes, stale-version conflicts, and unsaved input follow UI-008/UI-009 recovery rules.
11. Mobile targets meet 44 px generally and 48 px for persistent actions; native launch resumption never auto-logs or auto-mutates.

## 8. Remaining implementation-contract checks (not product-design questions)

Before coding, verify that the detail API supplies, per action where applicable: availability, execution mode/containment, customer-visible/internal effect, attention-clear effect, lifecycle effect, validation hints, and current version. Missing metadata fails closed: omit or present a conservative review path; do not infer from role, status, or local heuristics.

This specification does not authorize implementation by itself. Implementation begins only after the product owner confirms this consolidation accurately represents the locked decisions.
