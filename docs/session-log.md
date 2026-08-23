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

### 3. Establish the responsive sheet primitive and draft rules

- Desktop: right slide-over, full viewport height, one normal scroll owner, focus trapped while
  open, Escape and close supported, and focus returned to the trigger.
- Mobile/tablet: bottom-sheet presentation with keyboard-safe sizing and an accessible close control.
- Preserve an in-memory draft for a sheet closed during the same request session. Explicit **Discard**
  clears it. Do not persist customer-sensitive drafts to local storage by default.
- Warn before closing only when a dirty draft would be lost; do not turn routine close/reopen into a confirmation loop.

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
