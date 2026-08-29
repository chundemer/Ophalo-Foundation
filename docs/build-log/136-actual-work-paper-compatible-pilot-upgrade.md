# Build Log 136 — Actual Work Paper-Compatible Pilot Upgrade

**Status:** Direction locked; workflow and mechanical preflight required before implementation  
**Date:** 2026-08-29  
**Related:** Build Logs 129 and 135; ADR-487; ADR-493

## Why this changes the immediate sequence

The pilot business currently records much field work on paper, then has an office administrator enter it later. The product must help that operation move progressively into Keep; it must not require every technician to adopt a complex field workflow on day one.

The completed Actual Work financial foundation remains valid: submitted field facts are immutable, office financial resolutions and zero-line dispositions are append-only, and review remains a hard financial gate. What is incomplete is the capture and office-review experience around those safeguards.

Accordingly, BL135 Batch 5 (Billing Revision) is paused. It resumes only after the work in this build log has been preflighted and delivered. This is a sequencing change, not a relaxation of financial controls.

## Locked operating model

1. **Flexible capture; hard financial lock.** A Draft can be captured by a technician or transcribed by office staff. Submission makes the factual record immutable. Financial resolution and disposition evidence remain append-only; submitted facts are never silently edited or deleted.
2. **One active Draft; explicit handoff.** Keep retains the existing exclusive Draft recorder model. Office participation must use a deliberate authority/handoff path, not shared concurrent editing.
3. **Per-line performer attribution.** Each Actual Work line needs a `PerformedByAccountUserId`, distinct from creator, current recorder, and reviewer. The ticket may offer a header-level default for new lines, but attribution belongs on the line so multi-technician work is representable. The preflight must lock historic and inactive-user behavior.
4. **Draft-only visit note.** A ticket needs an optional `VisitNote` (maximum 2,000 characters) for field context, uncertainty, and office follow-up. It is separate from the existing zero-line completion outcome and cannot alter a submitted record.
5. **Corrections preserve history.** A pre-review factual error is corrected by an atomic replacement-copy flow: retain the erroneous submitted source and its financial evidence, mark it excluded/superseded, and create a linked successor Draft containing the factual capture, performers, and note but no financial resolutions. Post-review corrections continue to follow the controlled addendum/replacement path. Exact lifecycle, uniqueness, signal, billing-eligibility, and zero-line semantics require preflight.
6. **Usable office UI is pilot scope.** The office needs a full Actual Work Ticket Workspace, not a narrow inline card or side drawer: ticket context, notes, lines, line-adjacent missing-financial actions, totals, review state, and safe actions in one working view. It must use the established Request List and Request Detail visual language, and needs focus, dirty-close, concurrency, and mobile fallback behavior. `EntrySource`/`InitiatedVia` are intentionally omitted unless a real reporting need is established; existing creator, recorder, performer, and reviewer audit data answer the current question.

## Required pilot walkthroughs

The design is not ready until it can be walked through end-to-end for:

1. An office administrator transcribing a paper ticket into a controlled Draft and handing it off or submitting it.
2. A technician recording a simple job quickly, leaving a helpful visit note, and handing the Draft to the office when needed.
3. An owner/admin completing the whole ticket from the desktop workspace, resolving missing cost or price beside the affected line, and reviewing it without scrolling between disconnected surfaces.
4. Correcting a submitted-but-unreviewed factual omission (for example, missed labor hours) without editing/deleting the submitted source or its finance evidence.
5. A reviewed ticket remaining immutable, with the appropriate later correction path.
6. Request close being blocked by a relevant open Draft or unreviewed submitted Actual Work ticket, while excluded/superseded records do not create a false block.

## Delivery sequence

### P — workflow and mechanical preflight (no code)

Lock the authority rules for office Draft transcription and handoff; performer cardinality, historical attribution, and inactive users; Draft-note validation and visibility; replacement-copy lifecycle, signals, financial/billing exclusion, and retry/concurrency behavior; workspace interaction details (including route versus sheet, Request List/Request Detail styling, focus, dirty close, and narrow-screen fallback); and request-close eligibility. Reconcile the decisions with ADR-487 and ADR-493 before file-level implementation plans are approved.

### 4c — attribution and Draft note foundation

Introduce the model, validation, persistence, API, and audit needed for per-line performer attribution and Draft-only visit notes. Preserve current submitted immutability and recorder authorization.

### 4d — office Draft entry and handoff

Provide the controlled office path to create, transcribe, continue, transfer, and submit a Draft without converting it into shared concurrent editing.

### 4e — pre-review replacement-copy correction

Implement the atomic correction flow locked in preflight. It must retain the original, establish linkage and exclusion, create the successor Draft, and leave no ambiguous billing/review signal state.

### 4f — Actual Work Ticket Workspace

Deliver the desktop-first workspace and a safe narrow/mobile fallback. Financial blockers and their resolution/disposition actions must be visible in the ticket context, beside the work they concern.

### 4g — request-close eligibility gate

Make request close reflect outstanding relevant Actual Work. Define and test how Draft, submitted-unreviewed, reviewed, superseded/excluded, and replacement successor tickets affect eligibility.

### Resume BL135 Batch 5 — Billing Revision

Billing Revision starts only after 4c–4g have passed their individual gates. The Billing Revision design must consume the resulting correction lifecycle rather than competing with it.
