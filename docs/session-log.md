# Session Log — OpHalo Foundation

**Last updated:** 2026-08-23
**Deployment posture:** Not pilot-ready.
**Purpose:** active handoff only. Completed implementation narratives belong in Git history and the relevant build log, not here.

## Authoritative sources

- Acceptance status and release priority: [pilot-readiness-bug-tracker.md](pilot-readiness-bug-tracker.md)
- Product decisions: [decision index](decisions/README.md) and individual ADRs
- Durable implementation evidence: [build logs](build-log/)
- Request Detail interaction contract: [Workbench signoff specification](ux-design/v2/request-detail-workbench-signoff-spec.md)
- Effective-attention contract and precedence: [Request Detail API preflight](ux-design/v2/request-detail-workbench-api-preflight.md)

## Current production focus — Request Detail action clarity

The Workbench shell and three visual slices are complete. The remaining issue is action discovery:
an operator must scan multiple regions to identify and reach the correct next action. The next
change must make one server-authorized resolution route obvious while preserving the difference
between communication, externally handled contact, and formal attestation.

### Next presentation pass — Request Detail workbench refinement (2026-08-23)

**Status:** approved visual direction; implementation in progress as independently compiling
vertical slices (batch-size gate). This is a presentation/layout pass over the existing authorized
workbench, not a workflow or API-contract redesign.

**Slice progress:**
1. Global border-temperature tokens — done, committed `4c05cdc`. `--ophalo-border` neutralized to
   `#e2e8f0`; added `--ophalo-border-subtle`, `--ophalo-surface-muted`, `--ophalo-shadow-card` to
   `app.css` and `ophalo-tokens.css`. `ophalo-web/src/app/globals.css` deliberately left out of
   scope (separate public frontend; token decision deferred). Cross-route visual check passed.
2. Hero attention banner consolidation — done. `AttentionGuidanceCard` + `NextStepCard` merged into
   one `HeroAttentionBanner` in `DetailPanels.tsx`; routing logic unchanged (extracted to
   `resolveNextStep`). `RequestDetailContent.tsx` wired to the merged component;
   `NextStepCard.test.tsx` renamed/adapted to `HeroAttentionBanner.test.tsx`. 193/193 request-detail
   tests pass, `pnpm typecheck` clean, visual check passed.
3. Actual Work compact strip — done. `ActualWorkCard.tsx` collapsed the stacked label/description/
   full-width-button layout into a horizontal strip (label+summary left, compact `ClipboardList`-
   icon Record/Resume trigger right); quiet-degradation (loading/hidden/error → null) and secondary-
   not-teal button convention preserved. `ActualWorkHistoryCard.tsx` header padding tightened
   (`py-4` → `py-3.5`) to register as the same strip module; visit-entry list and price-blind/empty-
   state behavior unchanged. 193/193 request-detail tests pass, `pnpm typecheck` clean,
   `pnpm check:tokens` clean, visual check passed.
4. Owner "Change" quiet trigger + canvas width narrowing — not started.
5. Activity collapse + reorder below Record details — not started.

**Target:** the supplied Request Detail mockup is the visual reference. Move away from a wireframe
of stacked/nested boxes toward a calm, single-plane operational workbench: consistent Inter-based
operational typography, restrained borders, whitespace/alignment-led grouping, sentence-case
labels, and bold reserved for scanning anchors, important values, and permitted primary actions.
A controlled serif face may remain only for the request title and a verbatim customer quotation.

**Approved visual changes**

1. Use the existing Lucide icon set consistently for interactive controls, status/risk cues, mode
   tabs, section anchors, and disclosure affordances. Icons stay small outline glyphs aligned to
   text; do not paste one-off inline SVG path sets. Do not add decorative icons to static metadata
   labels (Customer contact, Service location, Assigned owner, etc.).
2. Narrow and center the desktop Work Canvas toward the mockup's reading measure (evaluate
   `max-w-4xl` versus the current `max-w-6xl`), retaining the protected two-pane desktop shell and
   its one canvas scroll owner.
3. Retain the compact three-row Request Anchor and its data/action wiring. Refine it visually;
   owner assignment may become a quiet **Change** trigger that opens the existing authorized
   reassignment workflow outside the metadata ledger. Do not alter authorization or mutation flow.
4. When active attention exists, compose its current guidance, verbatim customer context, and
   server-routed next action into one quiet amber `HeroAttentionBanner`. It must preserve the
   server-authored Why/Resolve-by explanation when meaningful and may only offer Clear attention
   when `canAcknowledgeAttention` authorizes it. Never infer a clearance route.
5. Turn Actual Work into a compact factual execution strip with its existing authorized
   Record/Resume action. Do **not** introduce Proposed Scope, pricing, or an **Edit Scope** action:
   Proposed Scope remains outside the controlled-pilot Workbench.
6. Keep the customer-update/internal-note composer as the substantive writing surface; flatten
   routine enclosing chrome rather than adding cards within cards. Preserve its visibility
   disclosures, drafts, error recovery, and server-authorized status behavior.
7. Collapse **Activity History & Communication Log** at rest and move that disclosure **below
   Record details**. The collapsed summary must retain an entry count and keyboard-accessible
   disclosure semantics. Record details therefore precedes Activity in the visual/canvas order;
   do not create another scroll owner or duplicate timeline data.

**Guardrails / validation before implementation**

- `RequestDetailContent.tsx` currently renders `AttentionGuidanceCard`, `NextStepCard`, and
  `OriginalRequestCard` independently; consolidate presentation conditionally without assuming
  that attention guidance or `detail.description` exists.
- `NextStepCard` already safely routes by `guidanceKey`; retain its server availability checks and
  target behavior (composer focus, Contact customer sheet, Follow Up resolution, or Clear
  attention sheet). A teal visual CTA must not become a client-authored workflow decision.
- Existing `ActualWorkCard` / `ActualWorkHistoryCard` intentionally hide absent data and protect
  price-blind Actual Work. Preserve both properties in the strip/disclosure treatment.
- Use existing CSS tokens in `src/styles/app.css`, `KeepButton`, and Lucide imports. Any global
  token adjustment needs a cross-app visual check; prefer request-detail-local composition first.
- Update the relevant Request Detail rendering/accessibility tests for the reordered collapsed
  Activity disclosure, then run focused Vitest, `pnpm typecheck`, `pnpm check:tokens`, and visual
  checks at desktop 100%/125%/150% plus narrow layout before sign-off.

**Global styling-token guidance — approved, but deliberately narrow**

- The existing global foundation is aligned with the mockup and should remain: canvas
  `#f8f6f1`, white surfaces, navy, Keep teal, Inter for operational UI, and Source Serif only for
  display roles/customer quotations.
- The worthwhile global refinement is border temperature: evaluate changing
  `--ophalo-border` from warm `#ddd6c8` to a neutral/slate value around `#e2e8f0` (or a slightly
  softer `#e7edf3`). This should reduce the application's wireframe/outlined-card feeling without
  changing information hierarchy. Perform a cross-route visual review before accepting it.
- Add semantic tokens rather than scattering new literal colors: `--ophalo-border-subtle` for
  internal dividers, `--ophalo-surface-muted` for quiet grouped controls, and
  `--ophalo-shadow-card` for the restrained surface elevation used by the upgraded workbench.
  Use the standard border for interactive boundaries/meaningful surfaces, and the subtle border
  for internal division.
- Keep `KeepButton`'s existing semantic variants, 42px minimum height, and focus behavior intact.
  Any visually tighter desktop controls belong in a Request-Detail-scoped compact treatment; do
  not globally reduce touch targets as part of this presentation pass.
- Preserve Source Serif for page titles and verbatim customer quotes only. Migrate operational row
  titles/metadata to the existing Inter/sans role where the current shared type utility uses serif.
- `src/styles/app.css` explicitly mirrors `web/shared/styles/ophalo-tokens.css`; any accepted
  global token adjustment must update both sources. Validate Requests, Price Book, Settings, and
  Request Detail after the token change, then run `pnpm check:tokens`.

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

### 4. Move structured actions into sheets without changing domain meaning

- Clear attention: move the existing required-reason form into the sheet; submit only through the
  acknowledgement endpoint and replace detail with its response.
- Log contact: move the existing workflow into the sheet; preserve outcome and attention-effect disclosures.
- Resolve Follow Up On: use its dedicated resolution path. Preserve date-only display; never model
  it as generic acknowledgement.
- Edit service location: move only if its current form and authorization make the sheet appropriate;
  do not bundle unrelated location changes into the attention slice.

### 5. Simplify the canvas and protect truthful completion behavior

- Remove standalone structured-action form cards only after their sheet destination is live and keyboard-accessible.
- Keep Customer Update inline but collapsed by default and expanded from its explicit destination.
  Preserve customer-visible disclosure, status behavior, validation, and draft/error recovery.
- Keep Log contact reachable from the Anchor as a compact trigger, but route it to the sheet.
- Render **Mark work done** as demoted when effective attention remains, with clear nearby consequence text.

### 6. Verify the full resolution matrix

For persisted attention, due/overdue Follow Up On, and overdue first response, verify:

- Needs Attention row admission matches visible detail guidance.
- Next step label matches `guidanceKey`.
- The named target opens and has matching available-action authorization.
- Update, contact logging, follow-up resolution, and acknowledgement retain distinct server-owned effects.
- Desktop 100%/125%/150% zoom, keyboard-only operation, narrow-screen sheet behavior, focus return,
  dirty-draft close/reopen, 409 recovery, and unavailable/403 states work.

Run focused Vitest coverage, `pnpm typecheck`, `pnpm check:tokens`, and the relevant full frontend
suite before visual sign-off.

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
