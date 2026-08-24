# Session Log — OpHalo Foundation

**Last updated:** 2026-08-24
**Deployment posture:** Not pilot-ready.
**Purpose:** active handoff only. Completed implementation narratives belong in Git history and the relevant build log, not here.

## Authoritative sources

- Acceptance status and release priority: [pilot-readiness-bug-tracker.md](pilot-readiness-bug-tracker.md)
- Product decisions: [decision index](decisions/README.md) and individual ADRs
- Durable implementation evidence: [build logs](build-log/)
- Request Detail interaction contract: [Workbench signoff specification](ux-design/v2/request-detail-workbench-signoff-spec.md)
- Effective-attention contract and precedence: [Request Detail API preflight](ux-design/v2/request-detail-workbench-api-preflight.md)

## Current production focus — Request Detail action clarity

The visual workbench refinement is committed (`99cba09`): neutral borders, consolidated attention,
compact Actual Work with collapsed visit history, teal inline triggers, and quiet history
disclosures. Actual Work draft/submitted CTA clarity is also committed (`985eba5`): "Add actual
work" / "Continue draft" by factual draft state, and a lock icon + "Submitted · locked" on every
submitted visit row — presentation-only, per ADR-487 (correction/reversal/reconciliation remains
deferred domain work, not opened by this change).

Implementation sequence steps 1-6 (below) are now complete — the Request Detail action-surface
redesign is done. The action-first planning and queue-signals slice (below) is also complete. The
queue-header consolidation batch (below) is also complete and committed. The Customer Need /
attention-rail decoupling batch (below) is also complete and committed.
**Next batch: resume 8B (Owner/Admin Actual Work financial review UI).**

### Request Detail — permanent Customer Need + compact attention rail — complete (2026-08-24)

**Implemented as specified below.** `HeroAttentionBanner` (`DetailPanels.tsx`) is now a single
compact amber rail — badge, label, an on-demand accessible disclosure (`AttentionGuidanceDisclosure`,
same outside-click/Escape/focus-return pattern as `RequestListToolbar`'s `ViewsPopover`) holding
Why/Resolve by/after-handled/timeline-evidence, the server-routed primary Next step CTA, and a
secondary **Clear attention** entry point shown whenever acknowledgement is separately authorized
(suppressed only when the primary CTA already routes there, i.e. `guidanceKey ===
"acknowledge_attention"`). `OriginalRequestCard` is now a single always-mounted **Customer need**
module (`RequestDetailContent.tsx` renders it unconditionally; `hasTimelineQuote` suppression
removed) — no longer coupled to attention state. Presentation-only; no API/query/domain changes.
Verified: typecheck clean, `pnpm check:tokens` passed, `git diff --check` clean, full
`src/pages/request-detail` suite 206/206 passing. Zoom/narrow-viewport visual pass (acceptance
check 6) not run in this session — flag if a dedicated visual QA pass is still wanted before this
ships to pilot.

**Original spec (as implemented):**

**Problem:** the current `HeroAttentionBanner` is a tall amber card that combines three different
things: temporary operational guidance (**Why**, **Resolve by**, and Next step), the customer's
durable original request, and the next action. This hides the core customer context when attention
clears, makes routine work visually heavy, and forces experienced operators to scan SOP prose on
every flagged request.

**Locked product decision:** Customer Need is durable request context; Needs Attention is a
conditional operational state. They must not be coupled in the DOM or layout.

**Target canvas order:** directly below the existing Zone A Request Detail Anchor / Internal
Planning row, render (1) the conditional Attention rail, (2) the always-mounted Customer Need
module, (3) Actual Work, then (4) the existing communication workspace. When there is no active
effective-attention guidance, do not render any attention surface: no amber background, warning
icon, rail border, placeholder, or residual vertical gap. Customer Need then sits immediately
under the Anchor.

**Attention rail (active guidance only):** replace the current multi-section hero card with a
single compact amber rail. It contains the warning icon/badge, the server-authored attention
label (for example, **First response due**), an accessible on-demand information trigger, the
single server-routed primary Next step CTA, and a secondary **Clear attention** entry point only
when acknowledgement is an authorized, applicable action. On ordinary desktop widths it should be
one shallow horizontal row; it may wrap into a usable compact multi-row layout at narrow widths.
Do not force a brittle literal 48px height at the expense of target size, readable labels, zoom, or
mobile layout.

**On-demand guidance:** move the existing `guidance.why`, `guidance.resolveBy`, and optional
`guidance.afterHandled` content out of the rail into a click/tap accessible popover or disclosure
anchored to the information trigger. It must be keyboard reachable, expose an accessible name and
expanded state, close with Escape/outside interaction as appropriate, and remain usable without
hover. This is presentation relocation only: retain the exact server-owned guidance copy and do
not invent client-side SLA rules.

**Permanent Customer Need:** always render one quiet, compact module showing the request's
original description, labeled **Customer need**. It is not amber and has no alert styling. It must
remain visible both while attention is active and after it is cleared. Do not replace it with a
timeline-sourced quote: attention evidence may be surfaced in the on-demand guidance, but the
permanent module is specifically the original request description. Apply the established empty
content behavior if the API has no description; do not render a fake customer quote.

**Preserve domain/workflow truth:** CTA selection remains entirely server-driven through
`effectiveAttention.guidanceKey` and existing available-action authorization. Do not hard-code
“Respond to customer” into the rail: valid routes include Respond to customer, Log contact,
Resolve follow-up, and Go to Clear attention. Keep the existing resolution destinations and
effects intact. In particular, Clear attention must continue to open the required-reason sheet;
it must never silently clear the flag. A customer-page update and a logged direct contact remain
distinct domains and must retain their existing disclosures/audit semantics.

**Implementation map:**

- `web/ophalo-app/src/pages/request-detail/RequestDetailContent.tsx` owns canvas ordering and
  must render `OriginalRequestCard` unconditionally as the permanent Customer Need module.
  Remove the current `hasTimelineQuote` conditional/suppression behavior.
- `web/ophalo-app/src/pages/request-detail/DetailPanels.tsx` owns `HeroAttentionBanner` and
  `OriginalRequestCard`. Refactor the former into the conditional compact rail; preserve
  `resolveNextStep`, `scrollAndFocusWithinWorkCanvas`, authorization checks, and sheet callbacks.
  Adapt the latter to the quiet permanent Customer Need presentation.
- `web/ophalo-app/src/pages/request-detail/helpers.ts` remains the authoritative UI mapping for
  `buildAttentionGuidance`; do not alter effective-attention precedence or API contracts for this
  presentation slice.
- Update/add focused tests under
  `web/ophalo-app/src/pages/request-detail/__tests__/`, especially
  `HeroAttentionBanner.test.tsx` and RequestDetailContent coverage.

**Acceptance checks:**

1. With each active `guidanceKey`, the rail renders, its label and server-routed CTA are correct,
   and the CTA retains its current destination behavior/authorization fallback.
2. With no active guidance, the rail is absent and Customer Need remains directly below the
   Anchor; no alert visual residue is rendered.
3. With active guidance, Customer Need still renders exactly once and displays the original
   description rather than a timeline quote.
4. Why / Resolve by / after-handled guidance is available on demand and works with keyboard and
   touch; it is not permanently consuming canvas height.
5. Clear attention opens its existing sheet and preserves required-attestation behavior.
6. Verify desktop at 100%/125%/150% zoom and a narrow/mobile viewport: no clipping, overlap,
   inaccessible action, or unintended horizontal scroll.
7. Run the focused Request Detail tests, `pnpm typecheck`, `pnpm check:tokens`, and the relevant
   frontend suite before handoff. Keep this as one presentation-only reviewable change set; do not
   combine it with 8B financial-review work.

**Deferred next:** resume 8B (Owner/Admin Actual Work financial review UI) after this slice is
implemented and verified.

### Request Queue header consolidation — complete (2026-08-24)

**Implemented as specified below.** `RequestQueueNavigation.tsx` now owns only the primary-tabs row
(Row 1) and the history sub-header. `RequestListToolbar.tsx` owns Row 2: search plus one **Views**
popover bundling Saved views (Watching, then Owner/Admin Office Review destinations), **History
Log**, and single-select status filtering (radio semantics, draft-copies-applied on open, Apply
commits once and returns focus to Views, Escape/outside-click discards unapplied changes, **Reset
filters** commits an immediate clear and updates the `Views · N` badge — locked in review; the
first pass left Reset as draft-only, corrected before commit). `Requests.tsx` rewired accordingly;
no query/API/domain changes. Verified: typecheck clean, full suite 628/628 passing (plus the
Reset-filters regression test rewritten for the corrected behavior), `pnpm check:tokens` passed,
`git diff --check` clean.

**Problem to resolve:** the current queue header has five competing vertical control layers before
the first request row. It mixes a native browser `<select>` status filter with custom React
disclosures, and exposes the same scope through primary tabs, secondary links/disclosures, and the
status filter. This is control overload, not a new data/domain requirement.

**Locked interaction model:** make the normal operational queue header two compact rows, following
the approved reference:

```text
[ Attention · N ] [ All · N ] [ Mine · N ]
[ Search queue…                              ] [ Views v ]
                                             └ Saved views + Filter by status
```

- Row 1 is the sole primary scope chooser. Use the existing role-aware primary tab definitions and
  their server-authoritative counts: Owner/Admin remains **Needs Attention, All Work, My Work**;
  Operator remains **My Work, Needs Attention, Available Work**. Compact display labels may be
  **Attention / All / Mine** only where their accessible names retain the full labels. The selected
  tab must have a clear selected container/state; counts are subordinate badges, never separately
  clickable controls. Keep the existing roving-tab keyboard behavior and authoritative
  `onSelectTab` semantics.
- Row 2 contains search and one custom **Views** button. It replaces the native status `<select>`
  entirely. Do not retain a visible `Filter` control or any native OS option menu.
- The Views popover is a normal accessible disclosure/group (not an ARIA menu unless full menu
  keyboard semantics are implemented). It contains, in order: a **Saved views** section (Watching;
  then Owner/Admin Office Review destinations when applicable), a separator, then **Filter by
  status** controls using the existing `STATUS_OPTIONS`, followed by **Reset filters** and
  **Apply**. The current API/query contract carries one status value, so these are a custom
  single-select control (radio semantics/checkmark treatment), not a multi-status checkbox filter.
  Do not fabricate multi-select query behavior. The UI may use the agreed icon set for saved-view
  rows; icons are decorative and the text label remains the accessible name.
- `Office Review` is no longer a peer header link/control. Its existing aggregate/count/error
  contract stays intact, but its members move into the Saved views portion of Views for Owner/Admin
  only. Preserve the rule that counts are server-authoritative; no guessed zero or client-derived
  membership. Watching remains a saved view, not a fourth primary tab.
- `History` moves into Views as **History Log**. Entering/exiting history and the history
  scope/date controls retain their existing behavior; do not merge history into a status filter or
  invent history counts. It is a result-set mode, not an operational queue scope.
- Status filtering remains operational-only; it must not be shown or applied in history mode.
  Preserve submitted-search semantics, clear-search behavior, applied-criteria/status messaging,
  first-load stability (GAP-041), authorization, query keys, and server ownership of membership,
  ranking, and counts. The Views trigger should visibly indicate active non-default status filters
  (for example `Views · 2` or a count badge) and provide a one-action reset.
- Do not change request-row content, API contracts, tab IDs/views, query behavior, or the
  customer-facing product domain. This is an interaction/layout consolidation only.

**Current implementation map:** `Requests.tsx` owns selected tab, search, status filter, history
mode, queries, and mutation/reset semantics. `RequestQueueNavigation.tsx` owns primary tabs plus
the current Office Review/Views disclosures. `RequestListToolbar.tsx` owns search and the native
status `<select>` that must be removed/replaced. `RequestsWorkspaceHeader.tsx` owns the compact
pane queue identity. `requestsWorkspace.ts` owns tab definitions, status options, counts, and
history helpers. Update focused coverage in `components/requests/__tests__/RequestQueueNavigation.test.tsx`
and the Requests/workspace tests; add coverage for no native `combobox`, popover open/close and
focus return/Escape, saved-view navigation, filter Apply/Reset, active-filter indication, history
entry, role gating, and primary-tab keyboard navigation.

**Visual intent:** two rows, one component vocabulary, shallow white/light canvas popover,
existing navy/teal/accent tokens, and the previously agreed icons. No five-row stack, raw text-link
navigation, dark OS-native picker, or color-only state. Validate at the real narrow queue-pane
width as well as full-width/narrow-page layouts; preserve 44px minimum interactive targets where
the existing layout supports them.

### Action-first planning and queue signals — complete (2026-08-24)

Presentation-only, existing request/detail contracts supplied all data; no new API/domain work.

#### Detail header: Internal Planning row

Built as a locked correction to the original one-line-strip brief: the terse pill format
(`Priority: Routine · Not planned · No follow-up`) read as passive metadata, not an actionable
control, and was rejected in review. Final design is a labeled, bordered select-style control row
at the bottom of the Request Detail Anchor (Zone A), below customer/contact/location/owner facts,
one subtle top separator above it:

```text
Internal priority        Planned work date          Set internal follow-up
[ Routine          v ]   [ Set planned work date… ]  [ Set internal follow-up… ]
```

- Locked order: **Internal priority -> Planned work date -> Set internal follow-up**. Desktop:
  one three-column grid row (`RequestDetailAnchor.tsx` Row 4). Narrow: the same labeled controls
  stack (`grid-cols-1` -> `sm:grid-cols-3`).
- Each field has a persistent visible label, a bordered select-like control, and a
  dropdown/chevron affordance; reuses `TriagePanel`/`TimingPanel`'s existing mutation handlers,
  date-only formatting, and conflict/error behavior via a new `strip` prop on both — no invented
  state or endpoints.
- Exact empty-state copy: Priority `Routine`, Planned `Set planned work date…`, Follow-up
  `Set internal follow-up…`. Never `Not planned` / `No follow-up`.
- Authorization: if the viewer can edit a field, it renders as an active control; if not but a
  value exists, it renders read-only (labeled, bordered, no chevron) — existing planning data is
  never hidden because editing is unavailable; if unauthorized and unset, the field is omitted
  entirely rather than rendering a dead control. Priority always renders (`Routine` is a real
  value, not an absence).
- Accessibility correction found in testing: `<label for>` pointing at a trigger button replaces
  its accessible name entirely, which would hide the date value from screen readers. Fixed by
  keeping the label purely visual and giving each trigger an explicit `aria-label` that includes
  the field name and current value (e.g. `"Planned work date: Aug 29, 2026"`).
- Old duplicated Timing/Triage card removed from `RequestDetailContent.tsx` now that the Anchor
  row carries full parity.

Owners: `RequestDetailAnchor.tsx` (Row 4 composition), `DetailPanels.tsx` (`TriagePanel` `strip`),
`TimingPanel.tsx` (`strip`), `RequestDetailContent.tsx` (old card removed).

#### Request queue: conditional combined action-signal line

Removed service **city/state** from both the default `RequestRow` card and the `paneMode` (actual
day-to-day queue surface) row; service location stays in detail/filters. Both now render one
conditional, capped, compact signal line in place of it:

```text
Urgent · Planned Aug 29 · Prefers text
```

- Shows internal priority only above Routine (`urgent`/`soon`); planned date only when set;
  contact preference only when explicitly `phone_call`/`text_message`/`email` — never
  `No preference`. Capped at three signals, unmounted entirely when no eligible signal exists.
- Deliberately excludes follow-up: a due/overdue follow-up is already carried by the existing
  attention/status cue, so it is not duplicated into the signal line.
- Existing attention ranking, badges, promoted row action, and `Next:` action semantics are
  unchanged.

Owner: `web/ophalo-app/src/components/RequestRow.tsx`.

Verified: 623/623 frontend tests pass (`pnpm test`), `pnpm typecheck` clean, `pnpm check:tokens`
passed. Focused coverage added for locked label order, exact empty-state copy, set-value
rendering, authorized interaction, read-only existing-value, omitted-when-unauthorized-and-unset,
and the queue signal line (default row, pane row, and unmount-when-empty).

### Non-negotiable product rules

- `EffectiveAttention` is authoritative for Request Detail attention presentation and gating. Do
  not derive an active attention result from legacy `attentionLevel`, `attentionReason`, dates, or status.
- `guidanceKey` selects the resolution route. It is not prose and must not be replaced by a client-side guess:

  | `guidanceKey` | Meaning | Resolution route |
  |---|---|---|
  | `acknowledge_attention` | A future server-authored acknowledgement-only condition. | Explicit **Clear attention** attestation, with a required reason. It is not the recommended route for current customer-originated attention reasons. |
  | `resolve_follow_up` | A customer Follow Up On promise is due or overdue. | Complete, move, or retain the follow-up through the dedicated resolution flow. |
  | `respond_to_customer` | The first response is overdue. | Send a customer update or log an actual external contact, as currently authorized. |
  | `log_external_contact` | A customer explicitly requested a call, or asked to coordinate timing. | Open **Contact customer** and log the completed external contact. Timing coordination must not rely on a passive customer-page update; a requested call still requires live phone contact. |

- A customer update does not automatically clear attention or prove delivery, receipt, or resolution.
  Clear attention is not a substitute for doing the customer work.
- Marking work done must continue to state that attention remains when it does. It may be visually
  compact, but the consequence cannot be hidden.
- Follow Up On is date-only. Render `effectiveAttention.dueOnDate` with `formatDateOnly`; never
  synthesize UTC midnight or apply a timezone conversion.
- Render only mutations returned as available by the current server detail. Returned authoritative
  detail replaces local state after every mutation.

### EffectiveAttention migration — complete (2026-08-23)

`BusinessSection.tsx` (`WorkDoneCard` line 35, `CloseRequestCard` line 310) now gates on
`detail.effectiveAttention.level` instead of legacy `detail.attentionLevel`. No remaining
non-test Request Detail consumer reads `attentionLevel`/`attentionReason`.

Verified: `pnpm typecheck` clean; `BusinessSection.compactPrimary`, `RequestDetailAnchor`, and
`NeedsAttentionDetailGuidance.matrix` (11/11) pass, including the required regression case —
`mock-req-001` with legacy `attentionLevel: "normal"`/`attentionReason: null` while
`effectiveAttention` is overridden active for each `guidanceKey`. `git diff --check` clean.

**Next batch:** the drawer/sheet primitive and structured-action migration; the server-routed Next
step module now uses reason-specific effective-attention guidance and canvas-owned scrolling.

### Step 4 — structured-action migration to `ResponsiveSheet` (locked 2026-08-23)

**Status:** implementation-ready after mechanical preflight. `ResponsiveSheet` (step 3, `c5d59b6`)
already requires an accessible name at the type level (`label`/`labelledBy` union) and has test
coverage for both — no outstanding accessibility gap to close before adding consumers.

Preflight found four current surfaces, none using `KeepModal` or `ResponsiveSheet`:

| Workflow | Current implementation | File |
|---|---|---|
| Log external contact | `LogContactModal` — hand-rolled centered dialog | `RequestDetail.tsx` |
| Resolve Follow Up On | `FollowUpResolutionPanel` — hand-rolled `fixed inset-0` dialog | `request-detail/FollowUpResolutionPanel.tsx` |
| Edit service location | `ServiceLocationModal` — hand-rolled centered dialog, manual Escape listener | `RequestDetail.tsx` |
| Clear attention | `MarkHandledCard` — not a dialog; always-mounted inline card reached via `scrollAndFocusWithinWorkCanvas("clear-attention-card")` | `request-detail/DetailPanels.tsx` |

Rules for this batch:

1. Real replacement, not a chrome swap: all four converge on `ResponsiveSheet`. Keep existing
   mutation handlers/API calls (`api.acknowledgeAttention`, `api.updateServiceLocation`, the
   follow-up resolution call, the contact-log call) unchanged.
2. Do not extract `LogContactModal` or `ServiceLocationModal` out of `RequestDetail.tsx` in this
   step. Replace their dialog chrome in place — `RequestDetail.tsx` already owns their open state,
   returned-detail cache updates, and focus restoration. Extraction is a separate structural
   refactor and is out of scope here.
3. Clear attention's primary trigger becomes the Next Step CTA (`respond_to_customer` /
   `acknowledge_attention` routing already resolves to it) — consistent with the other three
   workflows and avoiding a second duplicate-action surface. Remove the always-visible inline
   `MarkHandledCard` entirely; its form becomes sheet content opened via `onOpenClearAttention`.
   A separate non-primary access point, if ever needed, is a later decision — not part of this
   migration.
4. Clear-attention sheet open state lives in `RequestDetail.tsx` alongside the contact/location/
   follow-up sheet state. Thread `onOpenClearAttention` through `RequestDetailContent` →
   `NextStepCard` as a callback; `NextStepCard` must not manipulate sheet state or DOM anchors
   itself. This removes the `scrollAndFocusWithinWorkCanvas("clear-attention-card")` path.

**Correction (2026-08-23):** the first implementation pass wired routing but missed the ResponsiveSheet
doc comment's own requirement — deferred to step 4, not optional — that each consumer own dirty-close
confirmation so Escape/backdrop/Close/Cancel cannot silently destroy an in-progress form. Fixed by
following the codebase's existing convention (`CatalogItemDrawer.tsx`, `OfferingAssemblyDrawer.tsx`):
a local `isDirty`/`attemptClose`/`showDiscardConfirm` triple per consumer (duplicated, not shared —
matches the existing precedent and "differ materially in discard rules and draft shape"), gating
`ResponsiveSheet`'s `onClose` and every in-panel Close/Cancel button, with a nested `alertdialog`
overlay (Keep editing / Discard) that traps focus and marks the background `inert`. `ResponsiveSheet`
gained two additive presentation-only props to support this — `overlay?: ReactNode` (rendered last,
absolute over the full panel) and `contentInert?: boolean` (marks header/body/footer inert while the
overlay is shown) — no draft/dirty logic added to the primitive itself. `ExternalContactForm` gained
an optional `onDirtyChange` callback since its field state isn't otherwise visible to `LogContactModal`.

## Locked Request Detail action-surface contract (2026-08-23)

**Status:** approved for implementation after the required mechanical preflight. This is the
interaction allocation for the Request Workbench; it supersedes no domain ADR. Reconcile the
implementation with the signoff specification and current server authorization during preflight.

### Surface and routing matrix

| Server route / workflow | Surface | Trigger and constraint |
|---|---|---|
| `respond_to_customer` | Inline Customer Update composer | The Next step CTA expands it when `canSendBusinessUpdate` is true. If that action is unavailable but contact logging is authorized, route to the Log Contact sheet instead; never expand a disabled composer as the resolution target. |
| `acknowledge_attention` | Right slide-over / mobile bottom sheet | Explicit secondary route for a server-authored acknowledgement-only condition. Requires the existing formal attestation reason. |
| `resolve_follow_up` | Right slide-over / mobile bottom sheet | Opened when the customer Follow Up On promise is due or overdue. It offers Complete, Reschedule, or Keep active; it is not acknowledgement or generic messaging. |
| `log_external_contact` / Log contact | Right slide-over / mobile bottom sheet | Opened from the Anchor or Next step when it is the authorized contact resolution route. |
| Mark work done | Persistent Anchor macro action | Retains an explicit “attention remains” consequence whenever effective attention is active. |
| Destructive action, dirty-draft discard, 409 recovery | Centered modal | Blocking/binary interruption only. |

### Interaction model

1. Add a compact **Next step** module directly below Attention Guidance. It names the exact action
   selected by the server and presents one explicit destination button. Do not say “use the
   highlighted action” or rely on a visual highlight elsewhere on the page.
2. Keep the customer’s original request immediately after this module, so an operator can read the
   problem before acting.
3. Keep the customer-update composer inline and collapsed by default. `respond_to_customer`
   auto-expands it only when a customer update is currently authorized. This retains a comfortable
   writing surface and visible request context for routine work.
4. Use a responsive **drawer / sheet** for structured, deliberate side workflows:

   - Clear attention
   - Log external contact
   - Resolve Follow Up On
   - Edit service location

   On wide screens, it is a right-side slide-over that preserves line-of-sight to the request and
   history. On narrow screens, it becomes a bottom sheet. Do not introduce a centered modal for
   these workflows.
5. Reserve centered modals for blocking or binary decisions: destructive confirmation,
   dirty-draft discard, and version-conflict recovery.

**Locked time-sensitive communication rule:** `ScheduleChangeRequest` and
`TimingChangeRequested` return `log_external_contact`. Their Next step CTA is **Contact customer**;
it opens the durable contact workflow, where call/text/email launch utilities support the contact
but do not themselves resolve attention. A customer-page update remains an available secondary
action and must disclose that it does not notify the customer.

### Why this is the recommended split

- Clear-attention attestation, contact log, and follow-up resolution often require reference to the
  original request, contact information, and prior activity while writing. A drawer/sheet preserves context.
- These flows need room for server disclosures, required reason text, contact method/outcome
  controls, and normal vertical scrolling. A fixed centered modal is a poor fit and creates nested scroll risk.
- Customer updates are daily core work. Hiding their writing surface in a drawer by default adds
  friction without improving truthfulness.

### Explicit non-recommendations

- Do not make every action a drawer or introduce a permanent floating bottom command dock in the
  first release. It adds a competing visual zone, can obstruct mobile content, and is unnecessary
  once Next step provides a single destination.
- Do not put Clear attention in customer-update or internal-note tabs. It is a different,
  server-authorized attestation with a different audit meaning.
- Do not make a due Follow Up route to Clear attention or Send customer update by default. Its
  `resolve_follow_up` flow is distinct.
- Do not split the timeline into permanent Transcript/Audit tabs before pilot evidence shows current
  filters are inadequate. A customer-facing filter is the lower-risk first move.

## Approved implementation sequence

### 0. Mechanical preflight — no code

Read the current Request Detail composition, mutation controllers, drawer/modal primitives,
responsive behavior, and dirty-draft handling. Produce exact files, ownership, accessibility
behavior, and a test plan. Confirm that a sheet preserves request context without becoming a third
permanent pane.

### 1. Finish EffectiveAttention correctness — complete

### 2. Introduce a server-routed Next step module — complete (2026-08-23)

- Add one small Request Detail component immediately after Attention Guidance.
- Map `guidanceKey` to the locked matrix above. `respond_to_customer` expands the inline Customer
  Update composer only when it is authorized; otherwise it routes to an authorized Log Contact sheet.
- If a server-selected route is unavailable, show factual guidance without inventing a fallback
  mutation; record this as a contract discrepancy for review.
- Remove “highlighted panel/action” recommendation copy.
- Timing and schedule-change reasons route to **Contact customer**, not a passive page update.

### 3. Establish the responsive sheet primitive and draft rules — complete (2026-08-23)

- Desktop: right slide-over, full viewport height, one normal scroll owner, focus trapped while
  open, Escape and close supported, and focus returned to the trigger.
- Mobile/tablet: bottom-sheet presentation with keyboard-safe sizing and an accessible close control.
- `ResponsiveSheet` (`web/ophalo-app/src/components/keep/ResponsiveSheet.tsx`) is presentation-only:
  layout, focus, and Escape/backdrop close plumbing, built on `KeepModal`. `label`/`labelledBy` is a
  mandatory, TypeScript-enforced accessible name.
- Preserve an in-memory draft for a sheet closed during the same request session. Explicit **Discard**
  clears it. Do not persist customer-sensitive drafts to local storage by default.
- Warn before closing only when a dirty draft would be lost; do not turn routine close/reopen into a confirmation loop.
- Draft state and dirty-close confirmation are owned by each step-4 workflow, not the primitive —
  discard rules and draft shape differ materially across contact, follow-up, attention, and location.

### 4. Move structured actions into sheets without changing domain meaning — complete (`2f67476`, `f293a06`)

- Clear attention: move the existing required-reason form into the sheet; submit only through the
  acknowledgement endpoint and replace detail with its response.
- Log contact: move the existing workflow into the sheet; preserve outcome and attention-effect disclosures.
- Resolve Follow Up On: use its dedicated resolution path. Preserve date-only display; never model
  it as generic acknowledgement.
- Edit service location: move only if its current form and authorization make the sheet appropriate;
  do not bundle unrelated location changes into the attention slice.

### 5. Simplify the canvas and protect truthful completion behavior — complete (2026-08-23)

- Remove standalone structured-action form cards only after their sheet destination is live and keyboard-accessible.
- Keep Customer Update inline but collapsed by default and expanded from its explicit destination.
  Preserve customer-visible disclosure, status behavior, validation, and draft/error recovery.
- Keep Log contact reachable from the Anchor as a compact trigger, but route it to the sheet.
- Render **Mark work done** as demoted when effective attention remains, with clear nearby consequence text.

Delivered across `ad49157`, `3da292a`, `4ef0352`, `04dbf9b`, `99cba09`: HeroAttentionBanner
consolidation, Actual Work compact strip with collapsed visit history, quiet owner-reassignment
trigger, and Activity collapsed below Record details.

### 6. Verify the full resolution matrix — complete (2026-08-24)

For persisted attention, due/overdue Follow Up On, and overdue first response, verified:

- Needs Attention row admission matches visible detail guidance.
- Next step label matches `guidanceKey`.
- The named target opens and has matching available-action authorization.
- Update, contact logging, follow-up resolution, and acknowledgement retain distinct server-owned effects.
- Desktop 100%/125%/150% zoom, keyboard-only operation, narrow-screen sheet behavior, focus return,
  dirty-draft close/reopen, 409 recovery, and unavailable/403 states — confirmed by Christian.

Automated verification: full frontend suite 615/615 tests passed (68 files, including all
`src/pages/request-detail` coverage), `pnpm typecheck` clean, `pnpm check:tokens` passed. This
closes the approved Request Detail action-surface implementation sequence (steps 0-6).

## Other active work

### Two-domain customer communication (ADR-491) — complete (2026-08-23)

Locked scope from `ADR-491`: Post customer-page update vs. Contact customer / Log direct contact
are distinct communication domains; call/text QR handoffs are utilities within Contact customer,
never standalone workflows or evidence of contact.

Implemented (`ef0bc96`): consolidated Request Detail customer contact/handoff into the unified
Contact customer drawer; added an SMS handoff QR alongside the existing call handoff; removed dead
props on `CustomerContactStrip` and fixed a vacuous test assertion surfaced in review.

Follow-up dedup (post-commit, same day): extracted a shared `useHandoffMint` hook (mint/loading/
error/retry state machine) used by `useCallHandoff`, `RequestDetail.tsx`'s SMS handoff QR, and
`NotifyCustomerPanel`'s SMS handoff QR — same state machine, presentation stays per-caller. Added a
request-generation guard so a stale/overlapping mint or a post-unmount resolution can never write
state (regression risk identified in review). New tests: `useHandoffMint.test.ts` (stale-overlap,
post-unmount) plus retry-regression coverage for both SMS sites. Verified: `tsc --noEmit` clean,
`git diff --check` clean, 193/193 `src/pages/request-detail` tests pass.

### Owner/Admin Actual Work financial review UI (8B)

**Status:** preflight complete; implementation paused pending the Request Detail action-surface
redesign. Revalidate before code; do not revive the old layout plan unchanged.

- Backend financial-detail and review endpoints exist; financial detail includes the concurrency version.
- The future Request Detail card must be Owner/Admin-only, quiet-hide 403/entitlement-denied states,
  fetch per submitted visit, submit the exact returned concurrency version, and recover
  `ActualWork.AlreadyReviewed` by refreshing to show the real reviewer/note.
- The card stays separate from price-blind operator/field workflows.

## Pilot and release constraints

- The staff PWA is the active field surface; native/mobile work is not implied.
- Do not infer authority for quotes, prices, invoicing, payments, QuickBooks, inventory, fleet, or
  Proposed Scope from Request Detail work.
- Price Book access requires the account capability package. Use disposable local data for mutable
  acceptance; never seed test catalog data into the founder’s production account.
- Before a production candidate, run repository checks and the controlled production smoke test;
  verify health/readiness, release identity, error capture, alert routing, and telemetry redaction.
- Do not invite a pilot customer until selected P0/P1 tracker items and the end-to-end pilot checklist
  are complete or explicitly deferred.

## Working-session rules

1. Preflight before implementation: inspect the controlling ADR/build log/tracker and current code;
   report exact files, data flow, open decisions, tests, and verification commands.
2. Implement one reviewable change set at a time. Do not combine EffectiveAttention completion,
   action-surface redesign, and financial-review UI merely because they touch Request Detail.
3. Stop for an explicit product decision if current server data/action metadata cannot truthfully
   support the proposed UI.
