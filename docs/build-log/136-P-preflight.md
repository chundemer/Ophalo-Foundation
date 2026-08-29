# BL136 P — Actual Work Paper-Compatible Pilot Upgrade: workflow & mechanical preflight

**Status:** Complete. Decisions locked in [ADR-494](../decisions/ADR-494-actual-work-paper-compatible-pilot-upgrade.md);
this document is the durable code-grounded support for that ADR — the gap analysis, file-level slice
gates, migration list, and regression cases the ADR summarizes rather than repeats. Consult it
alongside ADR-494 when implementing BL136 4c–4g. Where the two differ, ADR-494 governs.
**Date:** 2026-08-29 (P complete)
**Inputs read:** session-log handoff; BL136; BL129 (boundary, zero-line, locked pilot guardrails, GAP-055,
Batch 6/7 locks); ADR-487 (record boundaries, controlled-pilot foundation, *required later domain-preflight
constraints*); ADR-493 (§1–6 + 2026-08-29 amendment); current code — `ActualWork`, `ActualWorkLine`,
`ActualWorkDraftRecorderTransfer`, enums, `ActualWorkErrors`, `ActualWorkDraftApiService`,
`ActualWorkReviewApiService`, `ActualWorkHistoryReadApiService`, `ActualWorkFinancialReadApiService`,
`IActualWorkPersistence`, `EfActualWorkSubmissionPersistence`, `EfActualWorkReviewPersistence`,
`ActualWorkConfiguration`, `KeepRequestActionPolicy`, `KeepRequest.ChangeStatus`, `RequestDetailContent.tsx`
wiring + `request-detail/` file inventory.

---

## Part 1 — Gaps and conflicts in the current direction

### G1 (blocking, first) — a superseded submitted visit strands the aggregate review signal forever
`EfActualWorkReviewPersistence.ResolveWorkSignalIfClearAsync` clears `ActualWorkNeedsOfficeReview` only when
**no** row exists with `status = 'Submitted' AND reviewed_at_utc IS NULL` for the request. BL129 Batch 6 and
ADR-493 §2 both lock the signal as "resolves only when every submitted visit is reviewed."
4e marks the erroneous submitted source *excluded/superseded* and it is **never reviewed**. Unless the
supersede transaction either (a) also runs the resolve-if-clear check, or (b) the predicate is narrowed to
`... AND superseded_at_utc IS NULL`, every corrected request stays queued indefinitely. This decision (D4)
must be locked before 4e, and the same predicate change ripples to the review queue, queue count, financial
detail, and the 4g close gate (see D8 call-site list).

### G2 — ADR-487 already locked per-line attribution, under a different name
ADR-487 *Required later domain-preflight constraints* → "Per-line field attribution. Every `ActualWorkLine`
records `RecordedByAccountUserId` and its record time independently of optional
`DerivedFromCommercialRevisionLineId`." Today `ActualWorkLine` carries only `CreatedByUserId` (BaseEntity) and
`CreatedAtUtc`. BL136 §3 names the field `PerformedByAccountUserId` and defines it as *who performed the work*,
explicitly distinct from creator/recorder/reviewer — a different concept from "who recorded the line."
Conflict to resolve (D2): is ADR-487's constraint satisfied by `CreatedByUserId`, or is `PerformedByAccountUserId`
a second column? Either way ADR-487's constraint text needs an amendment line so we do not ship two ADRs
naming different columns for overlapping intent.

### G3 — 4d "office Draft entry and handoff" is already largely possible at the API layer
`ActualWorkDraftApiService.AuthorizeAsync` gates create/mutate/submit on `RequestsOperate` +
`PriceBookQuotesMaterials` entitlement + `ActualWorkCapture` — **not** on active-Responsible participation
(GAP-055). Any qualified office user can already create, transcribe, and submit the one Draft.
`TransferRecorderAsync` already provides reason-required Owner/Admin handoff to any target holding
`RequestsOperate` + `ActualWorkCapture`. So 4d is **not new domain**; it is (i) a *stated authority rule*
(does "office staff" mean an existing Operator/Admin with `ActualWorkCapture`, or a new office capability?),
(ii) a handoff-*to-recorder* direction currently missing (transfer is Owner/Admin-initiated only — a field
tech cannot "hand my Draft to the office"), and (iii) UI. The preflight should say this plainly rather than
plan a domain slice. **Decision D3.**

### G4 — three notes now coexist; interaction unspecified
`CompletionNote` (zero-line submit, required, no max enforced today), `ReviewNote` (office, ≤2000, Draft
never), and the new `VisitNote` (Draft-only, ≤2000, BL136 §4). BL136 §4 says `VisitNote` is "separate from
the existing zero-line completion outcome." Must lock (D5): does a zero-line visit carry *both*
`CompletionNote` and `VisitNote`? Is `VisitNote` frozen at submit (readable forever) or Draft-only-visible?
Who can edit it — recorder only, via a Draft-guarded mutation path parallel to `UpdateLine`?

### G5 — ADR-493 §4 Replacement presupposes a Billing Revision; 4e is the earlier case
ADR-493 §4: "Replacement… Before billing handoff, it voids the affected Draft/Ready revision with a reason."
Pre-Batch-5 there is no `BillingRevision`, so that clause is a no-op. 4e is the *pre-review, pre-revision*
correction. The preflight must state how the two unify: 4e's supersede-link lifecycle is the substrate that
ADR-493 §4's post-handoff "later adjustment revision" will later consume; 4e itself only needs
source-retention + successor-Draft + exclusion-from-live-queries. BL136 already amends ADR-493 for sequencing;
this is the semantic bridge to add to that amendment.

### G6 — "excluded/superseded" needs a persisted marker; enum-value vs nullable-columns is constrained
BL129 locks: the open-Draft partial unique index predicate "matches the persisted lifecycle exactly" and
"must not invent a redundant `IsDiscarded` state." Adding an `ActualWorkStatus.Superseded` value forces a
review of the `status = 'Draft'` index filter and every exhaustive status switch. Nullable
`SupersededAtUtc` / `SupersededByActualWorkId` / `SupersessionReason` keeps `Status = Submitted` intact but
pushes `superseded_at_utc IS NULL` into every "live submitted visit" query. **Decision D4** — see the
call-site enumeration in D8; the cost is not asserted small, it is listed.

### G7 — 4f Workspace vs ADR-493 §5 price-blindness hard rule
ADR-493 §5: financial-resolution controls "must never be added to the price-blind `ActualWorkComposer`
field surface" and belong to Owner/Admin office surfaces "beginning with `ActualWorkReviewCard`." If the
Ticket Workspace (4f) is a single shared route rendering both the field Draft path and the office
resolution path, the preflight must name exactly how price blindness is preserved (separate components
gated by capability within one route, never a shared line renderer that conditionally hides money). This
constraint — not layout preference — is what decides route-vs-sheet. **Decision D7.**

### G8 — 4g close gate must be domain-authoritative, not policy metadata
There is **no** Actual-Work close gate today. `KeepRequest.ChangeStatus` has no Actual Work awareness;
`KeepRequestActionPolicy` is advisory UI metadata (CLAUDE.md). The authoritative Resolved→Closed block
must live in the status-change domain/service path (a predicate the service evaluates against the request's
Actual Work state), with `KeepRequestActionPolicy` carrying only the derived hint. **Decision D8.**

### G9 — historic data, migration, and inactive-user behavior for performer attribution
**Resolved by fact (Christian, 2026-08-29): there is no live Actual Work data in production — all local
Actual Work is disposable demo data.** This removes the legacy-data constraint entirely:
- `PerformedByAccountUserId` is **non-null** for every line in the new model — no permanent "unknown historic
  performer" exception is carried into production.
- The migration performs **no backfill of any kind** — it never manufactures performer attribution. Local
  demo data must be reset before the migration is applied; on a local database that still holds Actual Work
  rows the migration **fails loudly** (intended), and the developer resets first (see "Local reset/seed
  plan" below). Production has no Actual Work rows, so the strict non-null migration is safe there.
- **Do not blindly default the performer to the current Draft recorder.** In the office-transcription
  workflow the recorder is the admin entering a paper ticket while a technician performed the work. The
  ticket-level "Performed by" default must be visible and editable *before lines are added*:
  - Technician-created Draft → default to that technician.
  - Office-transcribed Draft → the office user picks the technician first; new lines inherit that selection.
  - Existing lines keep their captured performer when the Draft is handed off.
- Inactive user as performer: allowed (they did the work). The performer picker offers active members ∪
  {current line value}, so a now-inactive technician stays selectable/displayed.
**Decision D2b** (now a UI-default rule, not a backfill rule).

---

## Part 2 — Decisions requiring owner approval

| # | Decision | Recommendation |
|---|---|---|
| **D1** | Office Draft-entry authority model. | **Reuse existing capabilities.** "Office staff" = an account member holding `RequestsOperate` + `ActualWorkCapture` (Operators + Owner/Admin already have it). Do **not** mint a new office-transcription permission for the pilot; the pilot business's office admin is given an Operator/Admin seat. Revisit only if a genuine "office-only, cannot do field capture" role emerges. |
| **D2** | Performer field: name + relationship to ADR-487's `RecordedByAccountUserId`. | Add **one** new column `PerformedByAccountUserId` (**non-null**) on `ActualWorkLine`, meaning *who performed this line's work*. **`PerformedByAccountUserId` replaces ADR-487's future-facing `RecordedByAccountUserId` concept** — office transcription makes recorder and performer materially different, so "who performed" is the concept worth persisting; "who authored/recorded" is already covered by `CreatedByUserId`. ADR-494 states this supersession; ADR-487 gets a one-line amendment pointer. A nullable ticket-level `DefaultPerformedByAccountUserId` on the Draft seeds new lines (see D2b); the per-line value is authoritative and always non-null once a line exists. |
| **D2b** | Migration population + performer-default UX (no live data). | Migration: non-null column, **no backfill**; fails loudly on a local DB that still holds Actual Work rows (developer resets first). New lines: capture the performer deliberately — a new performer must be an active, account-scoped, eligible staff user (server-validated); an inactive former user stays valid on a line already attributed to them. Ticket-level "Performed by" default, editable before lines are added — technician Draft → self; office-transcribed Draft → office picks the technician first, lines inherit; handoff never rewrites already-captured line performers. Performer picker = active eligible members ∪ {current value}. **Never** silently default the line performer to the current recorder. |
| **D3** | 4d scope. | **No new domain aggregate/state.** 4d = (a) D1's stated authority rule; (b) a new *recorder-initiated* handoff action ("send this Draft to the office" — sets recorder to a chosen office user OR to a nullable "office queue" holder — recommend: transfer to a specific chosen user, reusing the existing transfer audit event with `Reason` defaulted to a system string, **relaxing** the Owner/Admin-only gate to also allow the current recorder to transfer *their own* Draft); (c) UI. Enumerate this as a small API-permission change + UI, not a domain slice. |
| **D4** | Superseded marker representation + signal fix. | Nullable columns on `ActualWork`: `SupersededAtUtc`, `SupersededByActualWorkId` (FK self, composite `(account_id, …)`), `SupersededByAccountUserId`, `SupersessionReason` (required when superseded, ≤2000). **Keep `Status = Submitted`.** Domain method `Supersede(bySuccessorId, byUser, reason, atUtc)` — allowed only on a `Submitted`, not-yet-`Reviewed`, not-already-superseded visit; fail-closed otherwise. Signal raise + resolve SQL is **extracted into one reusable reconciliation seam**: `IActualWorkReviewSignalReconciliation` in Application declared with domain scalars only (`accountId`, `requestId`, `nowUtc`, `ct`) — **no `DbContext`, `DatabaseFacade`, or transaction in the interface**; one Infra impl taking the request-scoped `OpHaloDbContext` via DI, so EF auto-enlists in an open transaction. `RaiseAsync` = the existing idempotent upsert/reopen (does **not** use the review predicate); `ResolveIfClearAsync` solely owns the shared "open outstanding review" predicate constant (also used by the D8 operational reads). Submission (raise), review (resolve-if-clear), and the supersession transaction (resolve-if-clear, in its own commit) all call that one implementation — the SQL is never duplicated in a second persistence class. |
| **D5** | The three note types + one validation convention. | `VisitNote`, `CompletionNote`, `ReviewNote` are all trimmed-to-null, ≤2000. `VisitNote` — new nullable `ActualWork` column, Draft-only + recorder-only via a new `SetVisitNote(note)` domain method + Draft-guarded route mirroring `UpdateLine`; frozen at submit; readable on every downstream read; independent of `CompletionNote` (a zero-line visit may carry both). `CompletionNote` — the trimmed-to-null / ≤2000 rule (previously unbounded) is the D3 *intent* but its `Submit`-guard implementation is **deferred to its own note-validation slice/preflight, explicitly out of 4c-i** (see "Deferred — `CompletionNote` note-validation guard"); still required only for a zero-line submit; also add a Draft-only recorder-only `SetZeroLineDisposition(outcome, completionNote)` persisted setter + read projection so a replacement Draft's copied zero-line values are editable and survive reload (D6). `ReviewNote` — unchanged. |
| **D6** | Replacement-copy lifecycle + transaction ownership. | **Application service** (`ActualWorkReplacementApiService`, Owner/Admin-gated) composes auth, checks the no-open-Draft precondition, loads the source, and **constructs the successor aggregate from it**: `Status = Draft`, acting user as recorder + author, deep copy of every line's factual + performer + snapshot fields + `VisitNote`, **no** financial-resolution / disposition / review rows. **Zero-line source:** the successor also carries **editable copied `Outcome` + `CompletionNote`** (replacement *copy* — starts from what was recorded, not blank); both stay editable while Draft and are re-validated by the normal zero-line submit rules at submit. **Persistence seam** owns one transaction: concurrency-check source → `source.Supersede(...)` (Core, guards only) → add the provided successor → re-evaluate signal → save → commit. Precondition: **no open Draft on the request** (partial unique index — return `ActualWork.DraftAlreadyOpenForRequest`). Retry: source concurrency-token mismatch → `VersionMismatch`; second supersede on an already-superseded source → `AlreadySuperseded`. Authority: **Owner/Admin-only for the pilot**; widening to the source recorder deferred. |
| **D6b** | Replacement-chain rules. | **One direct successor per source:** unique index on `ActualWork.SupersededByActualWorkId` (a given successor supersedes exactly one source) **and** the supersede guard rejects a source that already has a non-null `SupersededByActualWorkId` (`AlreadySuperseded`) — together these forbid **sibling replacements** (a source cannot be replaced twice). **A successor may itself be superseded before review:** yes — it is an ordinary `Submitted`-unreviewed visit once submitted; correcting it again forms a chain `v1 → v2 → v3`, each link one-to-one. A successor that is still `Draft` is discarded via the normal discard path, not superseded. **Chain never loses audit history:** superseding sets marker columns only; no row is deleted, no `Status` changes; `supersedes` / `supersededBy` links are walkable in both directions for the history view. **Every operational read/list/signal/billing/close query excludes rows with `superseded_at_utc IS NOT NULL`** (D8 enumerates the call sites) — but the **history read (`GetSubmittedVisitsForRequestAsync` / `ActualWorkHistoryReadApiService`) stays unfiltered** and returns them, flagged, with lineage links, so the audit trail stays visible while the operational surfaces show only the live head of each chain. Do **not** filter the history source. |
| **D7** | Workspace: route vs sheet + price-blindness. | **Dedicated desktop route** `/requests/:id/actual-work/:visitId` (and `/…/actual-work/new` / `/…/draft`), reachable from Request Detail. Within it, **two capability-gated regions never share a line renderer**: the field/Draft region (price-blind, `ActualWorkCapture`) and the office region (`AccountingManage` — resolution, disposition, review, totals, blockers). On narrow screens the route degrades to the existing stacked cards on Request Detail (no new mobile workspace). `ActualWorkComposer` stays exactly as price-blind as today; the workspace *hosts* it, does not modify it. |
| **D6c** | Superseded-work inertness + stable error. | New `ActualWorkErrors.Superseded` (code `"ActualWork.Superseded"`, → 409, reconcilable). **Mutations** on a superseded source fail closed *after* the existing version-mismatch check (a stale client is told to reload for the more general reason first; a current client against a superseded source then gets `ActualWork.Superseded`): mark-reviewed, line financial resolution, zero-line no-charge disposition, and a second direct replacement. **Direct live reads** of a superseded visit (single-visit financial detail, any "open this ticket" read) return the same reconcilable `ActualWork.Superseded` outcome, not a normal live surface — a stale deep link routes to "reload / go to the replacement." **Billing eligibility is exclusion, not a mutation error** — a superseded visit simply never appears in eligible-visit / queue / revision-selection results. |
| **D8** | Close-eligibility gate (4g). | Authoritative predicate in the status-change path (domain method arg or a `KeepRequestActualWorkCloseGate` service the change-status orchestration calls), blocking Resolved→Closed when the request has **either** an open Draft **or** a `Submitted`, not-`Reviewed`, not-`Superseded` visit. Superseded and reviewed visits never block. `KeepRequestActionPolicy` gets a derived `CanClose`-suppressing hint + a stable reason code for the UI. New error `KeepRequestErrors.CloseBlockedByOutstandingActualWork`. **Call sites that must adopt the `superseded_at_utc IS NULL` filter:** review-queue list, review-queue count, single-visit financial-detail read, eligible-visit reads, the `ResolveIfClearAsync` predicate (D4), and this new close gate. **Explicitly NOT filtered:** `GetSubmittedVisitsForRequestAsync` / `ActualWorkHistoryReadApiService` (history must show superseded rows + lineage). `GetOpenDraftForRequestAsync` is unaffected (a Draft can't be superseded). |
| **D9** | `EntrySource` / `InitiatedVia` columns. | **Omit** (BL136 §6 already leans this way). Creator + recorder + performer + reviewer + transfer audit answer "who/how" for the pilot. Add only against a real reporting requirement. |

---

## Part 3 — Ordered implementation slices

Each slice is sized against the CLAUDE.md gate (≤3 mutation families, ≤8 prod files, ≤12 total incl. tests).
Slices are independently compiling. `4c`/`4e`/`4f` as written in BL136 each exceed the gate and are split
below. Migrations are authored by Christian on approval of each slice's preflight.

### Slice 4c-i — performer attribution (one deployable vertical slice, eight commits + Christian's migration)

**Rollout seam.** `AddLine` must produce a non-null `PerformedByAccountUserId` from day one. A strict
schema behind the old API/UI would force a `createdByUserId` / recorder fallback — the false office
attribution ADR-494 eliminates. The schema, the performer-input API, **and a minimum functional
frontend** must reach production together. Commits — `4c-i-r` (dev tool), `4c-i-0a`, `4c-i-0b` (test
seam), `4c-i-a-1` (Core + Infra), `4c-i-mig` (Christian-authored EF migration), `4c-i-a-2`
(assembly-expansion outcome contract), `4c-i-b` (API), `4c-i-c` (frontend) — split only for
review/compile isolation and the CLAUDE.md file gate; none deploys until all merge.

#### Exact impacted-file inventory — every path that creates an `ActualWorkLine` (verified 2026-08-29)

An `ActualWorkLine` is created by **three** routes, not one: a direct `ActualWork.AddLine` domain
call; the HTTP `POST /keep/pricebook/actual-work/{id}/lines` endpoint; and assembly expansion
(`EfActualWorkAssemblyExpansionPersistence`, which calls `AddLine` in a loop and today collapses
**every** failure to `NotDraft` — `:174`). The earlier `\.AddLine(` grep found only route 1.

**Production callers of `ActualWork.AddLine` (2):** `ActualWorkDraftApiService.AddLineAsync:157`,
`EfActualWorkAssemblyExpansionPersistence.cs:170`. **`ActualWork.Create` callers:** the new default
arg is optional (`Guid? = null`) → no compile impact.

**Test files that cause an `ActualWorkLine` to exist — 13 files** (`D` = direct `AddLine` sites,
`H` = HTTP `POST …/lines`, `X` = expand-assembly):

| File | D | H | X | Breaks at |
|---|--:|--:|--:|---|
| `UnitTests/Keep/ActualWorkTests.cs` | 10 | – | – | 4c-i-0a seam |
| `UnitTests/Keep/ActualWorkFinancialProjectionTests.cs` | 6 | – | – | 4c-i-0a seam |
| `IntegrationTests/Persistence/ActualWorkPersistenceTests.cs` | 6 | – | – | 4c-i-0b seam |
| `IntegrationTests/Persistence/ActualWorkReviewPersistenceTests.cs` | 3 | – | – | 4c-i-0b seam |
| `IntegrationTests/Persistence/ActualWorkSubmissionTests.cs` | 1 | – | – | 4c-i-0b seam |
| `IntegrationTests/Persistence/ActualWorkFinancialResolutionPersistenceTests.cs` | 1 | – | – | 4c-i-0b seam |
| `IntegrationTests/Persistence/ActualWorkAssemblyExpansionPersistenceTests.cs` | – | – | 8 | **4c-i-a-2** |
| `IntegrationTests/Api/ActualWorkHistoryApiTests.cs` | 3 | – | – | 4c-i-0b seam |
| `IntegrationTests/Api/ActualWorkReviewApiTests.cs` | 1 | – | – | 4c-i-0b seam |
| `IntegrationTests/Api/ActualWorkFinancialReadApiTests.cs` | 1 | – | – | 4c-i-0b seam |
| `IntegrationTests/Api/ActualWorkDispositionApiTests.cs` | 1 | – | – | 4c-i-0b seam |
| `IntegrationTests/Api/ActualWorkFinancialResolutionApiTests.cs` | 1 | 4 | – | 4c-i-0b seam (D) **+ 4c-i-b** (H) |
| `IntegrationTests/Api/ActualWorkDraftApiTests.cs` | – | 12 | 7 | **4c-i-b** |
| `IntegrationTests/Api/ActualWorkNudgeFieldReadApiTests.cs` | – | 1 | 1 | **4c-i-b** |

The direct-`AddLine` churn (11 files, 34 sites) is isolated into the no-behaviour-change seam
(`4c-i-0a`/`4c-i-0b`); each per-file private `CreateDraftAsync` HTTP helper is updated in `4c-i-b`;
assembly-expansion tests move to `4c-i-a-2`. `OpHalo.UnitTests` and `OpHalo.IntegrationTests` have no
shared test-support project, so the seam helper is one static class per project.

#### Slice 4c-i-r — developer reset/seed tool (own gated commit, before the migration)
The local demo-data reset/seed (ADR-494 D12) lands in its **own commit ahead of the migration**, not
folded into 4c-i-a-1. **2 files:** a checked-in `scripts/reset-local-actual-work.*` (or a dev-only
seeder class under `tools/`) + a one-line README/usage note. **No production code, no DI, no
migration/startup/deploy wiring.** Gate: the tool is inert to the running application; running it
against a local DB clears Actual Work rows only.

#### Slice 4c-i-0a — test seam (unit project, tests only, no behaviour change)
New `OpHalo.UnitTests/Keep/ActualWorkTestData.cs` (wraps `ActualWork.Create` + `AddLine` with
explicit defaults) + migrate `ActualWorkTests.cs`, `ActualWorkFinancialProjectionTests.cs` to it.
**3 files.** Forked. Gate: unit suite green, count unchanged.

#### Slice 4c-i-0b — test seam (integration project, tests only, no behaviour change)
New `OpHalo.IntegrationTests/Support/ActualWorkTestData.cs` + migrate the 9 direct-`AddLine`
integration files (4 persistence + 5 API — the `D` rows above, excluding the expand-only file).
**10 files.** Forked; split persistence vs API if a reviewer prefers ≤6. Gate: integration suite
green, count unchanged.

#### Slice 4c-i-a-1 — domain + persistence (no migration; assembly-expansion contract deferred to a-2)
**Layer:** Core + Infrastructure. **Families:** 0 (factory/constructor change).
**Prod (7):** `ActualWorkLine.cs` (non-null `PerformedByAccountUserId` through `Create`),
`ActualWork.cs` (nullable `DefaultPerformedByAccountUserId`; optional arg on `Create`; Draft-only
recorder-only `SetDefaultPerformer` guard; `AddLine` takes an optional explicit performer, seeds from
the ticket default, returns `PerformerRequired` when both absent — **no creator/recorder fallback**),
`ActualWorkLineConfiguration.cs`, `ActualWorkConfiguration.cs` (column + index + default column),
`ActualWorkErrors.cs` (`PerformerRequired`), `ActualWorkDraftApiService.cs` +
`EfActualWorkAssemblyExpansionPersistence.cs` (compile-level: thread `actualWork.DefaultPerformedByAccountUserId`
into the loop's `AddLine`; failure still collapses to `NotDraft` here — the proper outcome is a-2).
**Tests (4):** both `ActualWorkTestData` helpers gain the default arg; new `ActualWorkTests` cases
(empty performer rejected; default seeds; explicit overrides default; `PerformerRequired` when
neither; inactive-user id accepted at domain; `SetDefaultPerformer` Draft-only + recorder-only);
`ActualWorkPersistenceTests` round-trip. **Total 11 (7 prod + 4 test).**
**Gate:** compiles and green; **not deployed alone**; no line without a performer; server never
derives one.

#### Slice 4c-i-a-2 — assembly expansion: explicit `PerformerRequired`, no partial writes
**Layer:** Application + Infrastructure. **Families:** 0 (outcome-enum widening).
Locked behaviour (Christian, 2026-08-29): assembly expansion uses the **persisted ticket default**
for every line it creates; a Draft with no default returns an explicit `PerformerRequired` outcome
(**never `NotDraft`**) and makes **no partial changes**; a genuinely non-`Draft` visit still returns
`NotDraft`.
**Prod (3):**
- `IActualWorkAssemblyExpansionPersistence.cs` — add `ActualWorkExpandAssemblyResult.PerformerRequired`.
- `EfActualWorkAssemblyExpansionPersistence.cs` — after the row-locked Draft load and status check,
  **before any `AddLine`/write**: if `DefaultPerformedByAccountUserId is null` → return
  `PerformerRequired`, transaction rolled back, zero lines written. Otherwise pass that default to
  every `AddLine`; a `NotDraft` from the aggregate still maps to `NotDraft`.
- `ActualWorkDraftApiService.cs` — map `ActualWorkExpandAssemblyResult.PerformerRequired` →
  `ActualWorkErrors.PerformerRequired` in the outcome switch (~`:196`–`:211`).
**Tests (1):** `ActualWorkAssemblyExpansionPersistenceTests.cs` — (a) no default → `PerformerRequired`
and **zero `ActualWorkLine` rows written**; (b) valid default → every expanded line carries it;
(c) `Submitted` visit → still `NotDraft`.
**Total 4 (3 prod + 1 test).**
**Gate:** the office transcriber's assembly expansion behaves exactly as "every line has a real
performer" requires; no partial write on the no-default path.

#### Commit 4c-i-mig — `AddActualWorkPerformer` migration (Christian-authored, explicitly gated)
**Author:** Christian, `dotnet ef migrations add AddActualWorkPerformer --startup-project
src/OpHalo.Keep.Infrastructure` (ADR-049 / memory), after `4c-i-a-1`'s EF config merges
(independent of `4c-i-a-2`, which touches no schema).
**Files (3, all EF-generated, no hand-written logic):**
`src/OpHalo.Foundation.Infrastructure/Migrations/<ts>_AddActualWorkPerformer.cs`,
`<ts>_AddActualWorkPerformer.Designer.cs`,
`src/OpHalo.Foundation.Infrastructure/Migrations/OpHaloDbContextModelSnapshot.cs` (diff only).
0 production-logic files, 0 tests. Non-null `PerformedByAccountUserId` column on
`actual_work_lines`, nullable `DefaultPerformedByAccountUserId` on `actual_work`, **no backfill**
(D1/D12).
**Rollout:** (1) validated locally first, after the explicit `4c-i-r` local reset; (2) then
deployed through the **normal production migration path** with the rest of the slice — production
receives the columns like any other migration; (3) production holds **zero Actual Work rows**, so
the strict non-null migration succeeds there with no backfill; (4) the `4c-i-r` reset tool is
local-only and is **never invoked by the migration or the deployment**.
**Gate:** migration `Up`/`Down` reviewed; local apply against a reset DB succeeds; a non-empty
local DB fails loudly as designed.

#### Slice 4c-i-b — performer-input API + dedicated performer-candidate read
**Layer:** Application + Api. **Families:** 1 (create-draft + add-line command shape).
**Prod (6):**
- `GetActualWorkPerformerCandidatesService.cs` **(new)** — gates 1–3 as
  `GetActualWorkRecorderCandidatesService` (account access → entitlement → `RequestsOperate`) but
  **no Owner/Admin gate**; additionally requires `ActualWorkCapture`; returns active,
  account-scoped, performer-eligible members. Does **not** reuse the recorder-candidate service,
  which is Owner/Admin-only (`GetActualWorkRecorderCandidatesService.cs:81` `Role is not (Owner or
  Admin)` → 403 for an Operator transcriber).
- `KeepEndpoints.cs` — new `MapGet /keep/pricebook/actual-work/performer-candidates`; new
  **`PUT /keep/pricebook/actual-work/{id}/default-performer`** (`SetDefaultPerformer` — Draft-only,
  recorder-only). **Concurrency uses the existing Actual Work protocol, not a new one:** the
  `X-Keep-ActualWork-Version` request header parsed by the existing `ParseActualWorkVersion`; **no
  version in the body**; success returns the existing `ActualWorkConcurrencyVersionResponse` (the
  rotated version — the client re-reads the Draft for the stored default value, as the other
  mutations do). Body carries only the target performer id or `null` (clear). Create + add-line
  body records gain the performer / ticket-default fields.
- `KeepServiceCollectionExtensions.cs` — `AddScoped<GetActualWorkPerformerCandidatesService>()`.
- `ActualWorkDraftApiService.cs` — create-draft accepts the explicit optional ticket default;
  add-line accepts the explicit optional performer; new `SetDefaultPerformerAsync` (loads Draft,
  recorder gate, version check, `ActualWork.SetDefaultPerformer` from `4c-i-a-1`, revalidates
  eligibility for a non-null value, commits, returns the rotated `ConcurrencyVersion`); all
  validate the same eligibility server-side against the membership snapshot.
- `ActualWorkErrors.cs` — `PerformerIneligible` (422; no membership enumeration, mirrors
  `RecorderTransferTargetIneligible`).
- a small `ActualWorkPerformerEligibility` predicate helper shared by the candidate service and the
  draft service (fold into the service if it stays one call site).
**Tests (6):**
- `ActualWorkPerformerCandidatesApiTests` **(new)** — Operator caller succeeds, Viewer/unauth 403,
  inactive excluded, current-value union;
- `ActualWorkDraftApiTests` — its `CreateDraftAsync` helper now sends a default (self); create
  with/without default; add-line with explicit performer / inheriting default / `PerformerRequired`
  with neither; `SetDefaultPerformer` set / replace / clear / recorder-only (non-recorder 404/403) /
  stale-version 409 / inactive + cross-account rejected (422) / existing lines keep their own performer;
- `ActualWorkNudgeFieldReadApiTests` — its `CreateDraftAsync` helper sends a default so its HTTP
  add-line + expand cases still pass;
- `ActualWorkFinancialResolutionApiTests` — same `CreateDraftAsync` fix for its 4 HTTP add-line cases;
- a resolver unit test.
**Total 12 (6 prod + 6 test) — at the gate.** If review prefers headroom, split the
`SetDefaultPerformer` route + its cases into `4c-i-b-2` (`KeepEndpoints` + `ActualWorkDraftApiService`
+ `ActualWorkDefaultPerformerApiTests` = 3 files), leaving `4c-i-b-1` at 5 prod + 5 test.
**Gate:** an Operator office transcriber can list candidates, persist a ticket default (and reload
it), and add a line that inherits it; every persisted line has a real validated performer.

#### Slice 4c-i-c — minimum functional frontend (part of the deployable slice)
**Layer:** `web/ophalo-app`. **Prod (6):**
- `apiClient.types.ts` — `ActualWorkCreateBody` + `defaultPerformedByAccountUserId`;
  `ActualWorkAddLineBody` + `performedByAccountUserId`; `ActualWorkPerformerCandidatesResult`.
- `apiClient.ts` — create/add-line bodies; new `getActualWorkPerformerCandidates(requestId?)`;
  new `setActualWorkDefaultPerformer(id, performerId | null, version)` — sends the
  `X-Keep-ActualWork-Version` header, returns the existing `ActualWorkConcurrencyVersionResult`.
- `useActualWorkCapture.ts` — entry-intent parameter on the start-capture path; create payload
  (`defaultPerformedByAccountUserId` = self for "Record my work", omitted for "Transcribe work");
  transcribe path calls `setActualWorkDefaultPerformer`, applies the rotated version and refetches
  the Draft (for the stored default) before enabling the add region; add-line payload carries the
  explicit performer or the persisted default; fetch candidates.
- `ActualWorkCard.tsx` — the explicit **UI-only** entry-intent choice *before* Draft creation:
  **"Record my work"** (current user as visible default) vs **"Transcribe work"** (no default). Not
  a persisted `EntrySource` — only the interaction branch.
- `ActualWorkComposer.tsx` — "Transcribe work" path: technician selector that **persists via
  `SetDefaultPerformer`**; **gates the entire add region — add-line *and* `expandAssemblyMutation`
  (`:391`) *and* nudge-accept — until the default is persisted**; "Record my work" path shows the
  preset default; later lines inherit it.
- `ComposerSearchAndAdd.tsx` — receives the `disabled`/gated prop so the assembly-pick and
  add-item affordances it renders are inert until the default is set (`ComposerQuickActions` /
  `ComposerNudgePanel` dispatch through the composer's gated mutations, so no separate change).
**Tests (3):** `ActualWorkCard.test.tsx` (both entry choices; payload differs); `ActualWorkComposer.test.tsx`
(transcribe path blocks **both** add-line and expand-assembly until the default persists; then both
inherit it); `useActualWorkCapture.test.ts` (rotated-version handling on `SetDefaultPerformer`).
**Total 9 (6 prod + 3 test).** Split into `4c-i-c-1` (api client + hook) / `4c-i-c-2` (card +
composer) if a reviewer prefers.
**Gate:** `check:tokens`, `tsc --noEmit`, full frontend suite; no money field on the field surface;
no add-region affordance (line, assembly, nudge) is live before a performer/default exists.

#### Deployment-sequence proof (4c-i)
1. **What must land together.** `4c-i-a-1` (schema) + `4c-i-mig` + `4c-i-a-2` (assembly-expansion
   outcome) + `4c-i-b` (API) + `4c-i-c` (minimum frontend) are one production deployment. `4c-i-r`
   and `4c-i-0a/0b` are prerequisite commits with no runtime effect. Without `4c-i-c` the live
   composer would create a Draft with no default and its next add-line or assembly expansion would
   fail `PerformerRequired` — the frontend cannot be deferred.
2. **Three line-creation routes, one rule.** Direct add-line, HTTP `POST …/lines`, and assembly
   expansion all produce a line with a real performer or fail with `PerformerRequired`; expansion
   makes no partial writes on the no-default path (`4c-i-a-2`).
3. **Explicit entry intent (no silent classification).** `4c-i-c` presents "Record my work" vs
   "Transcribe work" before Draft creation, replacing the generic single "Record completed work"
   entry point that cannot tell a technician from an office transcriber. UI-only, not persisted.
4. **Technician Draft visible self default.** "Record my work" sends the current user as
   `defaultPerformedByAccountUserId`; shown and editable before any line is added.
5. **Office transcription requires a technician first, persisted.** "Transcribe work" sends no
   default; `4c-i-c` gates the **whole add region — line, assembly, nudge** — behind a technician
   selection (from the new performer-candidate read, callable by the Operator transcriber)
   **persisted via `SetDefaultPerformer`** (`4c-i-b`) — survives reload, inherited by later lines.
   The server returns `ActualWork.PerformerRequired` on every route as the backstop.
6. **Existing lines across handoff.** A recorder transfer changes only edit ownership; every
   already-recorded line keeps its captured performer (domain — `4c-i-a-1`; no rewrite path exists).
7. **Migration rollout.** `4c-i-r`'s local-only tool wipes local Actual Work rows first, so the
   migration is **validated locally**; it then **deploys through the normal production migration
   path** with the slice; production holds zero Actual Work rows, so the strict non-null migration
   succeeds there with no backfill. The reset tool is never invoked by the migration or the deployment.
8. **File gate proven, not estimated** (inventory re-derived across all three line-creation routes):

   | Commit | Files | Author |
   |---|---|---|
   | `4c-i-r` | 2 (script + note) | Claude |
   | `4c-i-0a` | 3 (helper + 2 unit files) | Claude, forked |
   | `4c-i-0b` | 10 (helper + 9 direct-`AddLine` integration files) | Claude, forked |
   | `4c-i-a-1` | 7 prod + 4 test = 11 | Claude |
   | `4c-i-mig` | 3 EF-generated (migration + designer + snapshot diff), 0 logic, 0 test | Christian |
   | `4c-i-a-2` | 3 prod + 1 test = 4 | Claude |
   | `4c-i-b` | 6 prod + 6 test = 12 (splits to 5+5 / 3 if review wants headroom) | Claude |
   | `4c-i-c` | 6 prod + 3 test = 9 | Claude |

   Every commit ≤ 12 total, ≤ 8 production, ≤ 1 mutation family. **The `CompletionNote` ≤2000 /
   trimmed-to-null guard (ADR-494 D3) is *not* in 4c-i** — see the deferred note below.

#### Deferred — `CompletionNote` note-validation guard (out of 4c-i scope)
D3 records the *intent* that `CompletionNote` become trimmed-to-null / ≤2,000 (it is currently
unbounded). Implementing that guard in `Submit` is a **separate validation behaviour** — it changes
stored values for existing submit tests and has nothing to do with the performer seam. It is
**explicitly excluded from every 4c-i commit** and needs its own bounded note-validation
slice/preflight (it may pair naturally with `4c-ii`'s `VisitNote` work, but only under its own
preflight, not folded in silently).

### Slice 4c-ii — VisitNote API + read-model projections
**Layer:** Application + Api. **Families:** 1 (new `SetVisitNote` route). **Prod (≈5):**
`ActualWorkDraftApiService` (`SetVisitNoteAsync` — Draft+recorder+concurrency); `KeepEndpoints.cs`
(route + body records); `ActualWorkHistoryReadApiService` (project performer display name +
`VisitNote`); `ActualWorkFinancialReadApiService` (project performer + `VisitNote` on line/visit
DTOs); `ActualWorkErrors` additions.
**Tests (≈5):** draft-API integration (set/clear VisitNote, Draft-only, recorder-only, concurrency),
history + financial read include performer/VisitNote.
**Gate:** VisitNote settable only on own Draft; frozen after submit; performer name in both reads.

### Slice 4c-iii — rich performer + VisitNote field UI
**Layer:** `web/ophalo-app` only. Builds on the minimum selector shipped in 4c-i-c.
**Prod (≈5):** `ActualWorkComposer.tsx` (per-line performer override beyond the ticket default +
`VisitNote` textarea, all price-blind), `useActualWorkCapture.ts` (VisitNote mutation),
`apiClient` + `apiClient.types.ts` (VisitNote), `ActualWorkHistoryCard.tsx` (show performer +
VisitNote read-only).
**Tests (≈4):** composer per-line performer + VisitNote interaction; history card render.
**Gate:** `check:tokens`, `tsc --noEmit`, full frontend suite; no money field appears on the field surface.

### Slice 4d — recorder-initiated handoff (authority relax + UI)
**Layer:** Application + Api + frontend. **Families:** 1 (transfer-recorder gate change).
**Prod (≈5):** `ActualWorkDraftApiService.TransferRecorderAsync` (allow the current recorder to transfer
their own unsubmitted Draft; keep Owner/Admin path; system-default `Reason` when recorder-initiated),
`KeepEndpoints` (no new route — same endpoint, relaxed auth), `ActualWorkRecoveryDrawer.tsx` /
`ActualWorkComposer.tsx` ("Hand off to office" action), `useActualWorkHistory.ts` / `apiClient`.
**Tests (≈4):** recorder can transfer own Draft; non-recorder non-Owner still 404/forbidden; audit event
written with system reason; target still must hold `RequestsOperate` + `ActualWorkCapture`.
**Gate:** transfer audit invariants preserved; no shared concurrent editing introduced.

### Slice 4e-0 — extract the signal-reconciliation seam (prep, no behaviour change)
**Layer:** Application + Infrastructure. **Families:** 0. **Prod (≈4):** new
`IActualWorkReviewSignalReconciliation` (Application) — `RaiseAsync(accountId, requestId, nowUtc, ct)`
and `ResolveIfClearAsync(accountId, requestId, nowUtc, ct)`, **domain scalars only, no `DbContext` /
transaction on the interface**; one Infra impl receiving the request-scoped `OpHaloDbContext` via DI
(EF auto-enlists in an open transaction); `ResolveIfClearAsync` owns the single shared "open
outstanding review" predicate constant (`status = 'Submitted' AND reviewed_at_utc IS NULL`);
`RaiseAsync` keeps the existing idempotent upsert/reopen. Repoint `EfActualWorkSubmissionPersistence`
and `EfActualWorkReviewPersistence` at it (delete their private copies).
**Tests:** existing submit/review signal integration tests pass unchanged (no new behaviour).
**Gate:** no predicate/behaviour change; the SQL now lives in exactly one place; the interface names
no Infrastructure type.

### Slice 4e-i — supersession marker + zero-line Draft setter + predicate widen
**Layer:** Core + Infrastructure + migration. **Families:** 1 (`Supersede`).
**Prod (≈7):** `ActualWork.cs` (marker columns + `Supersede(...)` guards + successor factory +
`SetZeroLineDisposition(outcome, completionNote)` Draft-only recorder-only setter),
`ActualWorkConfiguration.cs` (columns, self-FK, unique index on `SupersededByActualWorkId`),
`ActualWorkErrors.cs` (`AlreadySuperseded`, `SupersessionReasonRequired`, **`Superseded`**), widen the
predicate used by **`ResolveIfClearAsync` only** to `… AND superseded_at_utc IS NULL` (one edit;
`RaiseAsync` remains the idempotent upsert/reopen and never evaluates this predicate),
a new `IActualWorkSupersessionPersistence` + `EfActualWorkSupersessionPersistence` owning the **one
transaction** (concurrency-check source → mark superseded → add the caller-provided successor → call
the 4e-0 resolve-if-clear → save/commit), migration `AddActualWorkSupersession`.
**Tests (≈6):** domain guards; `SetZeroLineDisposition` Draft-only + persists + reload; persistence —
atomic supersede+successor, signal resolves when the last unreviewed live visit is superseded, stays
active when another remains.
**Gate:** superseded visit never blocks the signal; source concurrency-token mismatch → `VersionMismatch`.

### Slice 4e-ii — replacement-copy: application + API + operational filters
**Layer:** Application + Api. **Families:** 1 (`CreateReplacementAsync`). **Prod (≈6):**
new `ActualWorkReplacementApiService` (Owner/Admin gate mirroring `ActualWorkReviewApiService`; precondition
no open Draft; **builds the successor aggregate from the loaded source** — lines + performers + VisitNote,
and for a zero-line source copies `Outcome`/`CompletionNote` into the successor Draft via
`SetZeroLineDisposition`; hands it to the 4e-i seam),
`KeepEndpoints` (routes `POST .../{id}/replace` and a Draft `SetZeroLineDisposition` route,
recorder-only + concurrency-checked), `ActualWorkFinancialReadApiService`
(add `superseded_at_utc IS NULL` to the review queue, count, single-visit financial detail, eligible-visit
reads; a superseded-visit detail read returns the `ActualWork.Superseded` reconcilable outcome),
`ActualWorkHistoryReadApiService` (**unfiltered**; add a `superseded` / `supersededBy` / `supersedes` marker
to history entries), superseded-source mutation rejection on the review/resolution/disposition/replace
paths (after their existing version check).
**Tests (≈6):** replacement creates a Draft successor with copied facts (incl. zero-line Outcome/note) and no
financial rows; source drops out of review queue/count/eligible reads and its detail read returns
`ActualWork.Superseded`, but it **remains in the history read** flagged with lineage; mutations on the source
return `ActualWork.Superseded` after a version check; blocked when an open Draft exists; Owner/Admin-only.
**Gate:** no financial evidence deleted; source fully retained and linked; history source never filtered.

### Slice 4e-iii — replacement-copy UI
**Layer:** `web/ophalo-app`. **Prod (≈5):** `ActualWorkReviewCard.tsx` / new `ReplaceVisitForm.tsx`
(reason-required "Correct this visit" action on an unreviewed submitted visit), `useActualWorkFinancialReview.ts`
(mutation + outcome family), `ActualWorkComposer.tsx` (successor Draft's zero-line disposition fields
prefilled from source, editable, persisted — survive reload), `ActualWorkHistoryCard.tsx`
(superseded/successor lineage badges), `apiClient` + types. **Tests (≈5):** action visible only for
unreviewed non-superseded submitted visit; prefilled zero-line values editable + reload-persistent;
lineage badges; outcome mapping (409 concurrency, 409 open-draft-exists, 403).
**Gate:** field surface unaffected; existing 799+ suite green.

### Slice 4f-i — Actual Work Ticket Workspace: route shell + field region
**Layer:** `web/ophalo-app` only. **Prod (≈6):** new route + `ActualWorkWorkspacePage.tsx`
(ticket context header, lines, VisitNote, performer, totals placeholder), router wiring, a
`useActualWorkWorkspace` composition hook over the existing capture/history hooks, Request Detail
entry-point link, narrow-screen fallback to the existing stacked cards.
**Tests (≈5):** route renders for a Draft and a submitted visit; focus management; dirty-close guard;
narrow viewport falls back.
**Gate:** Request List / Request Detail visual language (tokens, `rounded-xl`, `KeepButton`); no money on
the field region.

### Slice 4f-ii — workspace office region
**Layer:** `web/ophalo-app` only. **Prod (≈5):** office panel composing the **existing**
`FinancialResolutionForm` / `NoChargeDispositionForm` / review action + blocker list + totals, gated on
`canReviewActualWork`; line-adjacent placement of missing-financial actions; `useActualWorkFinancialReview`
reuse. **Tests (≈4):** office controls hidden without capability; resolution/disposition/review reachable
line-adjacent; concurrency reconcile.
**Gate:** ADR-493 §5 — resolution controls never rendered in the field region; reviewed visit shows
read-only.

### Slice 4g — request-close eligibility gate
**Layer:** Core + Application + Api + frontend. **Families:** 1 (`ChangeStatus` gate). **Prod (≈7):**
`KeepRequest.cs` or the change-status orchestration service (evaluate an injected
`ActualWorkCloseEligibility` predicate on Resolved→Closed), new
`IActualWorkCloseEligibilityPersistence` + EF impl (`EXISTS` open Draft OR submitted∧¬reviewed∧¬superseded),
`KeepRequestErrors.CloseBlockedByOutstandingActualWork`, `KeepRequestActionPolicy` (derived hint + reason
code), API error mapping, frontend `PrimaryActionControl` / status control (disable Close + reason copy).
**Tests (≈6):** close blocked by open Draft; blocked by unreviewed submitted; **allowed** when the only
blocker was superseded; allowed when all reviewed; allowed when no Actual Work exists; policy hint matches.
**Gate:** full unit + architecture + focused integration; gate is domain-authoritative, policy is advisory.

### Then — resume BL135 Batch 5 (Billing Revision)
Batch 5's `Replacement` correction (ADR-493 §4) consumes the 4e supersede-link lifecycle rather than
introducing a parallel one. No change to Batch 5's file list from this preflight beyond that dependency note.

---

## Migrations summary (Christian authors, `--startup-project src/OpHalo.Keep.Infrastructure`)
1. `AddActualWorkPerformer` — `actual_work_lines.performed_by_account_user_id` (**non-null**),
   `actual_works.default_performed_by_account_user_id` (nullable), index. **No backfill step.** Production
   has no Actual Work rows, so it is safe there; on a local DB the developer runs the reset (below) first,
   and the migration is expected to fail loudly if rows still exist. The migration never manufactures a
   performer value.
2. `AddActualWorkVisitNote` — `actual_works.visit_note` (nullable, ≤2000). *(Can fold into #1 if approved
   together — still one slice's worth of review.)*
3. `AddActualWorkSupersession` — four nullable columns + self-FK + unique index on
   `superseded_by_actual_work_id`.
4. No schema change for 4g (read-only `EXISTS`).

## Local reset/seed plan (first implementation slice, 4c-i — Christian runs)
Actual Work has dependents: `ActualWorkLine`, `ActualWorkLineFinancialResolution`,
`ActualWorkOfficeFinancialDisposition`, `ActualWorkDraftRecorderTransfer`, `ActualWorkNudgeSuggestion`, and
the `keep_request_work_signals` rows keyed `ActualWorkNeedsOfficeReview`. Deleting only parent rows leaves
orphans / stranded signals. Safe order (all local dev DB only; production untouched — it has no Actual Work
rows):
1. `DELETE FROM keep_actual_work_line_financial_resolutions;`
2. `DELETE FROM keep_actual_work_office_financial_dispositions;`
3. `DELETE FROM keep_actual_work_draft_recorder_transfers;`
4. `DELETE FROM keep_actual_work_nudge_suggestions;` *(rules may stay)*
5. `DELETE FROM keep_actual_work_lines;`
6. `DELETE FROM keep_actual_works;`
7. `DELETE FROM keep_request_work_signals WHERE signal_key = 'ActualWorkNeedsOfficeReview';`
(Exact table names to be confirmed against the EF configs during 4c-i preflight; `ClientCascade` on
`ActualWorkLine` means step 6 may cascade 5 in EF but a raw SQL wipe should be explicit.) Provide this as a
checked-in `scripts/` snippet or an EF-based dev seeder reset, not ad-hoc SQL. After the wipe, run the
`AddActualWorkPerformer` migration against the clean schema, then reseed demo tickets through the normal
capture flow so every seeded line has a deliberately chosen performer.

## Test-surface implications
- Architecture tests: new `IActualWork*Persistence` seams live in Application, EF impls in Infrastructure —
  no boundary change.
- Regression matrix for the `superseded_at_utc IS NULL` filter: every live-visit read listed in D8.
- Signal-stranding regression: explicit test that a fully-superseded request clears
  `ActualWorkNeedsOfficeReview`.
- Frontend token/`tsc`/full-suite gates on every `web/ophalo-app` slice.

## Outcome (2026-08-29)

The cross-slice contracts this preflight surfaced — performer meaning, the replacement/supersession
lifecycle and chain rules, Billing Revision's relationship to pre-review replacement, the
signal-reconciliation fix, the dedicated workspace route, the authoritative close gate — are locked
in **[ADR-494](../decisions/ADR-494-actual-work-paper-compatible-pilot-upgrade.md)** (D1–D12), with
pointer amendments in ADR-487 and ADR-493 and the per-slice 4c–4g split mirrored into
[BL136](136-actual-work-paper-compatible-pilot-upgrade.md). The 2026-08-29 review corrections folded
in: zero-line successor carries editable copied `Outcome`/`CompletionNote` via a persisted
`SetZeroLineDisposition` Draft setter; the history read stays unfiltered while operational reads
filter superseded rows; the stable error is `ActualWork.Superseded` (raised after the existing
version-mismatch check); no migration backfill (the strict non-null migration fails loudly on an
un-reset local DB); the persistence seam owns the replacement transaction while the application
service builds the successor; the signal-reconciliation seam is `IActualWorkReviewSignalReconciliation`
declared with domain scalars only — no `DbContext`/transaction on the interface — with
`ResolveIfClearAsync` the sole owner of the shared "open outstanding review" predicate.

Implement 4c–4g against ADR-494 for the locked decisions and against Part 3 here for the file-level
slice gates, migrations, and regression cases.

**2026-08-29 slice-boundary correction (rev. 4).** `4c-i` is one deployable vertical slice across
**eight commits**: `4c-i-r` (dev reset/seed tool, own commit before the migration), `4c-i-0a` /
`4c-i-0b` (tests-only construction seam, forked), `4c-i-a-1` (Core + Infra), `4c-i-mig`
(Christian-authored EF migration, 3 generated files), `4c-i-a-2` (assembly-expansion outcome
contract — expansion uses the persisted ticket default; no default → explicit `PerformerRequired`,
never `NotDraft`, no partial writes; genuine non-`Draft` → still `NotDraft`), `4c-i-b`
(performer-input API — dedicated `RequestsOperate` + `ActualWorkCapture` performer-candidate read,
**not** the Owner/Admin-only recorder-candidate service — **plus a Draft-only recorder-only
`SetDefaultPerformer` route using the existing Actual Work concurrency protocol**:
`X-Keep-ActualWork-Version` header + `ParseActualWorkVersion`, no body version,
`ActualWorkConcurrencyVersionResponse`), `4c-i-c` (minimum functional frontend). None deployed until
all merge. An `ActualWorkLine` is created by **three routes** (direct `AddLine`, HTTP `POST …/lines`,
assembly expansion); the impacted-test inventory is re-derived across all three — **13 test files**,
with each commit's exact budget in "Slice 4c-i" above. The frontend adds an explicit **UI-only**
entry-intent choice before Draft creation ("Record my work" → self as default; "Transcribe work" →
no default, technician selection persisted via `SetDefaultPerformer`), gating the **whole add region
— line, assembly, nudge** — until the default exists, so an office admin is never silently classified
as a performer. No server-side performer derivation on any route. **The `CompletionNote`
≤2000/trimmed-to-null guard (D3 intent) is removed from 4c-i** — it is a separate note-validation
behaviour needing its own bounded slice/preflight. See ADR-494 D1–D3.
