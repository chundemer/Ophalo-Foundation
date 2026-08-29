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

### P — workflow and mechanical preflight (no code) — COMPLETE (2026-08-29)

All P questions are locked in **[ADR-494](../decisions/ADR-494-actual-work-paper-compatible-pilot-upgrade.md)** (D1–D12): performer semantics and selection rules, `VisitNote`, office Draft authority/handoff, the pre-review replacement/supersession lifecycle and chain rules, review-signal reconciliation, superseded-work inertness, the Ticket Workspace route, the authoritative close gate, and the developer-only local reset/seed boundary. ADR-487 and ADR-493 carry pointer amendments. The per-slice implementation split for 4c–4g is below.

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

### Per-slice implementation split (from the P preflight)

Each slice is independently compiling and sized against the CLAUDE.md batch gate (≤3 mutation
families / ≤8 production files / ≤12 total). 4c, 4e, and 4f are split; 4d and 4g are single slices.

- **4c-i** — performer attribution, delivered as **one deployable vertical slice** across eight
  gate-compliant commits, none deployed until all merge. Rollout seam
  (ADR-494 D1–D2): a strict non-null `PerformedByAccountUserId` must never sit behind an old API/UI
  that would fall back to a recorder default — the false office attribution this upgrade eliminates.
  An `ActualWorkLine` is created by **three routes** — direct `AddLine`, HTTP `POST …/lines`,
  assembly expansion — all covered. Exact impacted-file inventory (13 test files) and proven
  per-commit file counts are in the [P preflight → Slice 4c-i](136-P-preflight.md).
  - **4c-i-r** — developer reset/seed tool (D12) in its **own gated commit before the migration**:
    a checked-in script/seeder + usage note (2 files); no production code, no DI, no
    migration/startup/deploy wiring.
  - **4c-i-0a / 4c-i-0b** — test-construction seam (tests only, no production code, no behaviour
    change): one `ActualWorkTestData` helper per test project (the projects do not cross-reference)
    wrapping `ActualWork.Create` + `AddLine`, migrating the **11 direct-`AddLine` test files / 34
    call sites**. 0a = unit (3 files), 0b = integration (10). Forked.
  - **4c-i-a-1** (Core + Infrastructure) — `ActualWorkLine.PerformedByAccountUserId` (non-null)
    threaded through `Create`; `ActualWork.DefaultPerformedByAccountUserId` (nullable) + Draft-only
    recorder-only `SetDefaultPerformer` + optional default arg on `Create`; `AddLine` takes an
    optional explicit performer, seeds from the ticket default, returns `ActualWork.PerformerRequired`
    when both absent (**no creator/recorder fallback**); EF config; `ActualWorkErrors.PerformerRequired`;
    the two existing `AddLine` callers thread the ticket default (compile-level — the assembly path's
    proper outcome is `4c-i-a-2`); both `ActualWorkTestData` helpers gain the default. 7 prod + 4
    test. *(No `CompletionNote` change — that guard is out of 4c-i, see below.)*
  - **4c-i-mig** — `AddActualWorkPerformer` EF migration, **authored and committed by Christian**
    (`dotnet ef migrations add … --startup-project src/OpHalo.Keep.Infrastructure`, ADR-049) once
    4c-i-a-1's config merges. 3 generated files (migration + `.Designer` + `OpHaloDbContextModelSnapshot`
    diff), 0 logic, 0 test. No backfill. Rollout: validated locally after the explicit 4c-i-r
    reset, then deployed through the **normal production migration path** with the slice; production
    holds zero Actual Work rows, so the strict non-null migration succeeds there without a backfill;
    the reset tool is local-only and is never invoked by the migration or the deployment.
  - **4c-i-a-2** (Application + Infrastructure) — assembly expansion uses the **persisted ticket
    default** for every line; a Draft with no default returns an explicit
    `ActualWorkExpandAssemblyResult.PerformerRequired` (**never `NotDraft`**) with **no partial
    writes**; a genuine non-`Draft` visit still returns `NotDraft`. `IActualWorkAssemblyExpansionPersistence`
    (enum value) + `EfActualWorkAssemblyExpansionPersistence` (up-front default check, rollback) +
    `ActualWorkDraftApiService` (map to `ActualWorkErrors.PerformerRequired`). 3 prod + 1 test.
  - **4c-i-b** (Application + Api) — new `GetActualWorkPerformerCandidatesService` **callable by any
    active `RequestsOperate` + `ActualWorkCapture` holder** (an Operator office transcriber), **not**
    the Owner/Admin-only `GetActualWorkRecorderCandidatesService`; new
    `/actual-work/performer-candidates` route + DI registration; new **Draft-only recorder-only
    `PUT …/default-performer` (`SetDefaultPerformer`)** route that persists / clears the ticket
    default, using the **existing Actual Work concurrency protocol** — `X-Keep-ActualWork-Version`
    request header via `ParseActualWorkVersion`, no body version, `ActualWorkConcurrencyVersionResponse`
    on success; `ActualWorkDraftApiService` create-draft accepts the explicit optional ticket
    default and add-line the explicit optional performer, all server-validated for the same
    eligibility; `ActualWorkErrors.PerformerIneligible` (422). 6 prod + 6 test — the three per-file
    HTTP `CreateDraftAsync` helpers (`ActualWorkDraftApiTests`, `ActualWorkNudgeFieldReadApiTests`,
    `ActualWorkFinancialResolutionApiTests`) each start sending a default; plus the new candidate
    tests and `SetDefaultPerformer` cases (set / replace / clear / recorder-only / stale-version /
    ineligible). Splits to `4c-i-b-1` + `4c-i-b-2` if review wants headroom.
  - **4c-i-c** — **minimum functional frontend** (`web/ophalo-app`), part of the deployable slice,
    not deferred. An explicit **UI-only** entry-intent choice *before* Draft creation (not a
    persisted `EntrySource`): **"Record my work"** → sends the current user as the visible ticket
    default; **"Transcribe work"** → sends no default and requires a technician selection (from the
    new performer-candidate read) **persisted via `SetDefaultPerformer`** before line entry, so it
    survives reload and later lines inherit it. **Gates the whole add region — line, assembly
    (`expandAssemblyMutation`), nudge-accept — until the default is persisted.** Add-line sends the
    explicit performer or the persisted default. 6 prod + 3 test. Rich performer UI + history
    display remain 4c-iii.
  - **Deferred, not in 4c-i:** the `CompletionNote` trimmed-to-null / ≤2000 `Submit` guard (D3
    intent). It is a separate note-validation behaviour that changes existing submit tests and is
    unrelated to the performer seam; it needs its own bounded slice/preflight (may pair with 4c-ii's
    `VisitNote` work, but only under its own preflight).
- **4c-ii** — `VisitNote` API + read projections: new `SetVisitNote` Draft-guarded route (≤2000, trimmed-to-null, recorder-only); history + financial reads project performer display name and `VisitNote`. Application + Api.
- **4c-iii** — rich field UI: composer per-line performer refinement + `VisitNote` textarea (price-blind); history card shows performer + `VisitNote` read-only. `web/ophalo-app`.
- **4d** — recorder-initiated handoff: relax `TransferRecorderAsync` to allow the current recorder to transfer their own unsubmitted Draft (system reason); "hand off to office" UI. Application + Api + frontend.
- **4e-0** — extract the signal-reconciliation seam (prep, no behaviour change): new `IActualWorkReviewSignalReconciliation` (Application) declared with domain scalars only (`accountId`, `requestId`, `nowUtc`, `ct`) — no `DbContext` / transaction in the interface; one Infrastructure impl taking the request-scoped `OpHaloDbContext` via DI (EF auto-enlists in an open transaction). `RaiseAsync` = the existing idempotent upsert/reopen; `ResolveIfClearAsync` owns the single shared "open outstanding review" predicate constant. Repoint `EfActualWorkSubmissionPersistence` (raise) and `EfActualWorkReviewPersistence` (resolve) at it; existing tests green unchanged. Application + Infrastructure.
- **4e-i** — supersession marker + signal predicate + transaction seam: `ActualWork` marker columns + `ActualWork.Supersede(...)` (guards only) + `SetZeroLineDisposition(outcome, completionNote)` Draft-only recorder-only setter (D5) + successor factory + unique index + `ActualWorkErrors.Superseded` + widen the `ResolveIfClearAsync` predicate (the one shared with the D8 operational reads) to include `AND superseded_at_utc IS NULL` — one place; `RaiseAsync` unchanged + a persistence seam owning one transaction (concurrency-check source → mark superseded → add the provided successor → call 4e-0 resolve-if-clear → save/commit) + `AddActualWorkSupersession` migration. Core + Infrastructure.
- **4e-ii** — replacement-copy application/API + operational filters: `ActualWorkReplacementApiService` (Owner/Admin gate; no-open-Draft precondition; **builds the successor aggregate from the loaded source**, copying zero-line `Outcome`/`CompletionNote` via `SetZeroLineDisposition`; hands it to the 4e-i seam), `POST .../replace`, new `SetZeroLineDisposition` Draft route (recorder-only, concurrency-checked), add `superseded_at_utc IS NULL` to the review-queue list + count + single-visit financial-detail read + eligible-visit reads (**leave `GetSubmittedVisitsForRequestAsync` unfiltered**), superseded-source mutation + live-read rejection returning `ActualWork.Superseded` after the version-mismatch check, history lineage flags on the history DTO. Application + Api.
- **4e-iii** — replacement-copy UI: reason-required "Correct this visit" on an unreviewed submitted visit; the successor Draft's zero-line disposition fields prefilled from the source and editable (persisted, survive reload); history lineage badges; outcome mapping. `web/ophalo-app`.
- **4f-i** — workspace route shell + field region: dedicated route, ticket context, lines, `VisitNote`, performer, narrow-screen fallback. `web/ophalo-app`.
- **4f-ii** — workspace office region: composes the existing `FinancialResolutionForm` / `NoChargeDispositionForm` / review controls + blocker list + totals, capability-gated, line-adjacent. `web/ophalo-app`.
- **4g** — close gate: authoritative predicate in the status-change path (`EXISTS` open Draft OR `Submitted ∧ ¬Reviewed ∧ ¬Superseded`); `KeepRequestErrors.CloseBlockedByOutstandingActualWork`; `KeepRequestActionPolicy` derived hint. Core + Application + Api + frontend.

Migrations (Christian authors, `--startup-project src/OpHalo.Keep.Infrastructure`):
`AddActualWorkPerformer`, `AddActualWorkVisitNote` (may fold into the first), `AddActualWorkSupersession`.
The local reset/seed tool is developer-only and never runs from a migration or deploy path (ADR-494 D12).

### Resume BL135 Batch 5 — Billing Revision

Billing Revision starts only after 4c–4g have passed their individual gates. The Billing Revision design must consume the resulting correction lifecycle rather than competing with it.
