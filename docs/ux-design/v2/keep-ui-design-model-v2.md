# Keep UI Design Model V2

**Status:** Working model — locked sections are implementation authority; sections marked
**Decision required** are not.  
**Scope:** OpHalo Keep production UI upgrade.

## 1. Product stance

Keep is operational continuity software for small service businesses. The UI must help a person
understand what customer promise is at risk, what they can safely do now, and whether the record
changed successfully.

It must feel calm, sturdy, practical, and field-ready—not like a generic SaaS dashboard, helpdesk,
or full field-service operating system.

## 2. One request record, multiple work surfaces

| Surface | User job | UI posture |
|---|---|---|
| Wide office PWA | scan risk, route work, update customers, retain continuity | Request Queue + selected Request Workbench |
| Narrow/touch-constrained PWA | understand and act on one request | focused request detail with a clear return to Requests |
| Native/mobile field surface | My Work, contact, factual field capture, resume after interruption | action-oriented and price-blind during field capture |
| Public intake/customer request pages | submit a need or understand an existing request | business-first, limited, mobile-first trust surface |

Authorization, not screen width or mode, decides what a person may see or do. Layout changes density
and containment; it does not create a separate source of truth.

## 3. Brand, typography, and color

The current token and typography contract is retained from V1:

- **Source Serif 4** for headings and anchors; **Inter** for UI, forms, and operational data.
- `--ophalo-navy` `#10243E`: parent identity, navigation, structural anchors.
- `--keep-accent` `#168A9A`: Keep identity and customer-communication cues.
- `--ophalo-attention` `#C8741A`: continuity risk and attention.
- `--keep-info` `#244C95`: active/new/supporting state.
- `--ophalo-canvas` `#F8F6F1`: warm canvas; cards visibly lift from it.

Text on pale teal, amber, or blue surfaces uses `--ophalo-ink` or another explicitly audited
high-contrast foreground. Semantic color reinforces meaning; labels and content communicate it even
when color is unavailable.

## 4. Production richness and field readiness

Each primary surface has one clear type anchor, one elevated filled surface, a deliberate saturated
Keep-teal moment, unequal visual rhythm, and visible card/canvas separation.

On phones, the richness floor must not displace the current primary touch action below the first
useful viewport. Ordinary controls target at least 44 CSS pixels; persistent field action bars and
primary field actions target at least 48 CSS pixels.

## 5. Queue, selection, and workbench hierarchy

On wide `#/requests`, an unselected workbench uses a read-only Priority Preview rather than silently
opening a request. It follows the applied queue context and gives a factual, server-ranked next record
where one exists. Selecting a row or **Open request** alone changes the route to `#/request/{id}`.

Primary queues are deliberately limited to three: Owner/Admin sees Needs Attention, All Work, My Work;
Operator sees My Work, Needs Attention, Available. Watching is a quiet secondary view in a visibly
labelled **Views** control for both roles (not "More Views" — it only ever holds Watching). New
sessions begin at Needs Attention for Owner/Admin and My Work for Operators; active-session returns
preserve the applied queue context and scroll position. Background data does not silently reorder an
actively scanned list.

Owner/Admin additionally carries a conditional **Office Review** control below the primary tabs,
above search/filter: it aggregates Ready to Close, Feedback Review, and Actual Work Review under an
authoritative count, using navy/neutral treatment (never amber — amber
stays reserved for Needs Attention's customer-promise risk). Collapsed — reading "Office Review · N
pending" — is the default scan state; opening it prioritizes actionable (non-zero) members and
collapses empty ones into one quiet line. It renders only once its aggregate is authoritatively known,
never as a guessed zero, with a structured loading placeholder (shaped like the eventual strip, not a
blank bar) so the Queue does not shift under an actively scanning user. Office Review is Owner/Admin
office obligations, not a quiet view, and is distinct from Watching. Operator has no Office Review
strip.

**Presentation:** the current full-width `#/requests` page uses a normal horizontal primary-tab row
and an intrinsic-width Office Review control directly beneath it. It must feel like a compact
operational control, never a workspace-wide faux input. Views and History remain a compact,
associated utility group on that tab row where space permits, or wrap together before search/filter.
The future UI-001 bounded 320–360 CSS-px Queue pane reuses the same behavior with a pane-width Office
Review strip, a dedicated Views/History utility row, and the two-row primary grid defined in section
13. Presentation changes density and containment only; it does not create different queue rules.

A selected request uses a compact sticky Request Anchor over a scrollable Work Canvas. The anchor keeps
the customer, request/status/attention identity, current authorized action, and phone/location/owner
context immediately available. The canvas starts with active attention guidance where relevant, preserves
the customer’s original need exactly, then shows real authorized work context, composition, and history.

## 6. Action truth

These rules are locked:

- **Send update** is customer-visible and says so.
- **Internal note** is internal-only and says so.
- **Log contact** records outside contact only after explicit staff confirmation.
- Opening a phone, messaging, mail, or maps app records intent only; resumption may offer a
  non-blocking prompt to log what happened.
- Attention acknowledgement, work completion, and request closeout remain separate,
  server-authorized outcomes.
- Color never replaces explicit labels or customer/internal visibility disclosure.

The visual hierarchy is semantic: the current workflow primary is navy filled; customer communication
is Keep teal filled; contextual actions are navy outline; quiet utilities are subdued; terminal close is
red and confirmed; and amber is information/risk only, never a filled action. At rest there is one
enabled local-task primary in the selected Workbench. A valid active composer temporarily owns that
primary emphasis. Do not infer a universal “Mark handled” treatment.

## 7. Form containment

Form containment is task-based:

| Containment | Intended job |
|---|---|
| Inline | customer updates, notes, priority/follow-up, simple assignment |
| Drawer | focused single-record creation/editing while retaining workbench context |
| Modal/dialog | confirmation, explicit discard, required short outcome/reason, blocking recovery |
| Full route/workspace | substantial, durable, multi-step, or field-first work |

There is one overlay at a time. Mobile drawers are full-height, customer-visible writing stays inline,
normal remote errors are inline, destructive actions are confirmed, and focus/draft/Back behavior is
intentional and recoverable.

## 8. Office and field adaptation

Keep does not have an Office/On-Site mode switch. Authorization controls data/actions, queue defaults
control entry, and viewport/input/task control layout. Field defaults to My Work. Field capture opens
as a focused workspace and is price-blind for every recorder. A persistent mobile action area is 48 CSS
pixels or larger when applicable and hides/unpins during text entry. Phone, message, and mail launches
may offer one factual, non-blocking contact-log prompt after return; they never auto-log contact.

## 9. State and recovery

Every surface makes loading, empty, filtered-empty, permission, stale data, mutation pending, success,
failure, retry, and connection-loss states visible and recoverable. Initial loads preserve layout and
hide actions until authorized data arrives. Background refresh does not overwrite active input or silently
reorder a scanned queue; it offers a quiet update-available affordance.

Only server-persisted work is a Draft. Local input is Unsaved changes: it is session-only, protected for
in-app navigation where possible, and never silently queued for later transmission. A version conflict
never discards typed input. Keep refetches authority, blocks stale submission, preserves local input, and
requires explicit review and re-submit against the new version. A remote actor is identified only when
authorized response data proves it.

## 10. Public intake and customer Request page

Public intake leads with the known business and collects only the established useful request/contact and
service-location fields. It accepts rural/free-form address text; autocomplete is not a gate. Its submit
state prevents repeat taps, but public-write idempotency needs a server contract before it is promised.
Success leads toward the customer Request page, with the narrow fail-soft tracker-link email only when
email was supplied. Unknown or unavailable intake links are calm and non-enumerating.

The customer Request page is a business-first capability-link view, not a portal or chat. It shows only
authorized public status, public updates, safe original-request context, and permitted customer actions.
It never exposes service location, staff/office data, financial data, or delivery/read claims. Closed
pages offer the existing one-time feedback action while unexpired; Cancelled pages are read-only; both
expire 30 days after terminal transition.

## 11. Production quality gate

Release review uses realistic and boundary-valid data, keyboard-only and screen-reader operation,
contrast/focus audit, mobile text-entry safety, wide/narrow/touch layouts, and 100%/125%/150% zoom.
General controls are at least 44 CSS pixels and persistent field actions at least 48. Editable text
controls on iOS-facing public/mobile forms use at least 16 CSS pixels. Labels state only supported
facts, including “Visible on the customer page” for customer-visible writing.

## 12. Migration

Wide and narrow layouts mount one selected-Request data/mutation/version/conflict engine on the one
durable route. A temporary presentation fallback may exist behind a feature flag, but it cannot fork
request state or action behavior. Dirty navigation uses the containment discard dialog; legacy
presentation retires only after the production gate and pilot observation evidence are complete.

## 13. Desktop workbench contract

**Locked under UI-001:** the wide office PWA uses a Request Queue + selected Request Workbench. The
queue is bounded to 320–360 CSS pixels; the layout appears only when the available application
workspace protects a usable selected-workbench minimum. This is not a fixed device/viewport breakpoint.
When space is insufficient, Keep uses the focused one-pane Queue → request drill-down presentation.
There is no manual collapsible queue in the first redesign release.

At this 320–360 CSS-px width, primary tabs use a two-row grid rather than a horizontal strip, per the
UI-004 amendment: Owner/Admin Row 1 is Needs Attention full-width, Row 2 is All Work | My Work;
Operator Row 1 is My Work full-width, Row 2 is Needs Attention | Available. Neither role's grid may
scroll horizontally, clip, abbreviate a locked label, or squeeze a control below its usable target
size.

UI-002 through UI-005 lock the durable selected route, non-mutating Priority Preview, role queue
defaults, refresh posture, and first-viewport detail hierarchy. The exact protected workbench minimum
remains an implementation measurement validated at 100%, 125%, and 150% zoom with populated data.

## 14. Non-goals

V2 does not authorize voice transcription, a silent offline mutation queue, photo upload, customer
detail-request links, asset-history UI without Asset Operations, scheduling, payments, inventory,
or accounting workflows.
