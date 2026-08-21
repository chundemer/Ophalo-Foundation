# Build Log 129 — Direct Actual Work and Accounting Handoff: Product Preflight

**Status:** Product decisions locked; mechanical implementation preflight required before code  
**Date:** 2026-08-18  
**Related:** Build Logs 104, 126, 127, 128; ADR-487

## Purpose

Keep must support the ordinary field-service outcome: a staff member completes routine work during
a visit and records what actually happened, price-blind, without inventing a Proposed Scope. This
record is factual source data for office closeout, the Day-1 accounting CSV/reconciliation loop,
and future optional equipment history.

Keep is not a task-routing, payment-processing, accounting, or QuickBooks-sync product. This
preflight uses the existing responsibility/ownership language; it introduces no separate routing
state, board, or route model.

## Native field outcomes

```text
Direct repair / service  → Direct Actual Work
Recommendation needed    → Proposed Scope → office review/commercial path → Actual Work later
Diagnostic / advice only → zero-line Actual Work visit with a truthful outcome and completion note
```

No request is required to have a proposal, actual-work lines, a quote, or an accounting export.

## Direct Actual Work record boundary

- `ActualWork` is a distinct request-bound record; it is never a status change on `ProposedScope`
  or a commercial record.
- One finalized Actual Work record represents one field visit/execution event. A complex request may
  retain multiple immutable visit records.
- A staff member starts a Draft record through a **Record completed work** action that reuses the
  price-blind Unified Scope Composer interaction patterns. It does not expose customer price, cost,
  margin, discount, or quote controls.
- Submitted line records retain item/description snapshot, unit, actual quantity, optional field
  note, recorder identity, and recorded time. A line may optionally link to an approved commercial
  baseline source; direct work has no required upstream source.

## Zero-line diagnostic/service visits

An Actual Work visit may be submitted with zero lines only when it includes a required completion
note and one truthful structured outcome:

```text
DiagnosticOnly | NoWorkAuthorized | NoAccess
```

This prevents invented material/labor lines while preserving what occurred. A zero-line visit is
not automatically eligible for accounting export as a $0 job. If a diagnostic/trip charge applies,
it must be represented by a real actual-work line; otherwise Owner/Admin explicitly closes it as
no-charge.

## Office closeout and accounting handoff

- Office closeout selects finalized, not-previously-exported Actual Work visits for a request.
- The resulting accounting handoff is an immutable snapshot. A later visit or correction never
  changes a prior export; it becomes a later reviewed snapshot/handoff.
- The Day-1 handoff is a server-authoritative batch of `jobs.csv` and `work-lines.csv` for manual
  QuickBooks entry. It is not a QuickBooks import, API integration, invoice creator, or ledger.
- Both files include `RequestReferenceCode` (for example `REQ-1042`) prominently for human/Excel
  matching, plus `RequestId` and `AccountingExportId` for technical and batch traceability.
  `work-lines.csv` also carries its source Actual Work visit/line id.
- After export, Owner/Admin records the accounting-system invoice/reference number and reconciles
  the externally confirmed outcome: `PaidInFull`, `VoidedOrNoCharge`, or `Other` (required note).
  Keep does not store payment amount, payment method, balance, partial payment, credit, or
  collection activity.
- Margin is an Owner/Admin closeout view, not an accounting-export column by default.

## Controlled parallel-pilot implementation addendum — 2026-08-19

The next-week release implements the complete technician-to-office loop: price-blind factual field
capture, an Owner/Admin Actual Work Review queue, and Owner/Admin financial review sourced from
the Price Book. It must be an end-to-end vertical batch, not an isolated schema foundation.

### Locked pilot guardrails

- The request's active **Responsible** user is the sole field recorder for the pilot. That user
  may be an Owner, Admin, or Operator; an Owner/Admin assigned as Responsible uses the same
  price-blind capture surface as an Operator, and role does not expose financial fields there.
- There is one open Draft visit per request, owned by its Responsible recorder. The recorder may
  create, edit, or discard it. Submitted visits and their factual lines are immutable.
- Multi-technician work is supported without parallel drafts: the active Responsible user records
  the single visit and remains accountable for its job details; other technicians do not create
  their own Actual Work record for that request in this pilot.
- No cross-user Draft **Take over** action, cross-user Draft edit, linked **Correct prior visit**
  workflow, silent submitted-record edit, closeout, or export is in this pilot. These are
  explicitly deferred product options, not missing implementation.
- Submitting a visit raises an additive Actual Work needs office review signal. Owner/Admin sees
  it in an Actual Work Review queue and can mark the submitted visit reviewed, recording reviewer,
  time, and an optional internal note. The signal resolves only when no submitted visit on the
  request remains unreviewed. Review is not an invoice, customer approval, export, or payment fact.
- The field capture surface is Request Detail's **Record completed work** action. The Owner/Admin
  review surface shows immutable visit history plus Price Book-backed sales price,
  Standard/Expected Direct Cost, margin, totals, and clear incomplete-financial-data cues. No new
  top-level navigation is added; the queue is an Owner/Admin-only **Actual Work Review** tab in the
  existing Requests workspace and is the office's actionable entry point.
- A catalog-backed Actual Work line snapshots its selected Price Book version-line identity,
  sell price, and Standard/Expected Direct Cost when the field fact is recorded. Owner/Admin review
  calculates from those immutable snapshots, never from the catalog's then-current price. A custom
  or otherwise unsnapshotted line renders an explicit incomplete-financial-data cue; it never
  produces invented totals or margin.
- Actual Work mutations require the Price Book account entitlement, `RequestsOperate`, the distinct
  `ActualWorkCapture` permission (`keep.pricebook.actualwork.capture`), and an active-Responsible
  row-authorization check. The new permission is granted through `OperatorBase`; Owner/Admin
  inherit it through role composition. The Responsible check is exposed as one reusable
  participation read primitive, not duplicated by callers.
- The domain and API boundary both reject a zero-line submit unless its completion note is
  non-whitespace and its outcome is exactly `DiagnosticOnly`, `NoWorkAuthorized`, or `NoAccess`.
- The database enforces one active Draft per request with a partial unique index whose predicate
  matches the persisted lifecycle exactly. It must not invent a redundant `IsDiscarded` state.
- Marking a visit reviewed and resolving the aggregate Actual Work review signal run in one
  database transaction; a request remains queued while any submitted visit is unreviewed.

### Draft recorder ownership correction — 2026-08-20

**GAP-055 resolves this pilot-blocking workflow defect:** dispatch assignment (`Responsible`) is
useful routing context but is not authority to record factual Actual Work. The prior requirement
that a technician call the office for reassignment before recording work is superseded.

Locked pilot policy:

- Any active account member with the existing `RequestsOperate` and `ActualWorkCapture` permissions
  may create the one open Actual Work Draft for a request; active-Responsible participation is not a
  creation precondition.
- Creation preserves immutable `CreatedByUserId` authorship and sets explicit
  `RecorderAccountUserId` current ownership. The recorder alone may edit lines, expand assemblies,
  read Draft-bound nudges, discard, or submit the unsubmitted Draft.
- The existing one-open-Draft-per-request database constraint and the Draft concurrency token remain
  mandatory. A concurrent starter must reconcile to the existing Draft rather than create a second
  field record.
- An Owner/Admin may transfer an **unsubmitted** Draft recorder only through an explicit,
  reason-required, immutable `ActualWorkDraftRecorderTransferred` audit event containing actor,
  prior recorder, new recorder, and time. It changes `RecorderAccountUserId`, never
  `CreatedByUserId`. Silent takeover and a shared mutable Draft are not pilot behavior.
- Submitted visits remain immutable. Request assignment continues to support dispatch and does not
  change automatically when a technician starts or receives a Draft.

This correction pauses 5d-ii-d. Before resuming it, perform a mechanical ownership-remediation
preflight covering the domain/migration, all current active-Responsible authorization seams
(create/edit/discard/submit/expand/nudge/history), transfer API/audit, field UI copy, and the
concurrency/authorization regression matrix. Keep the work in bounded batches; do not fold this
cross-cutting correction into the nudge frontend slice.

### Business-completeness correction — 2026-08-20

The implemented 5b field composer permits catalog-item search and custom/off-catalog lines, but
does not offer assembly expansion or contextual nudges. That omission is not acceptable as the
complete pilot field workflow: technicians need help finding the full set of parts and work that
actually belongs in a factual Actual Work visit. Repeated individual searches or vague custom
lines would make complete, trustworthy field recording unnecessarily difficult.

Assembly expansion and field nudges are therefore required pilot field-assist capabilities, not
optional polish. They remain price-blind and factual: neither may automatically add work, expose
financial information, or convert Actual Work into a Proposed Scope recommendation flow. A new
**5d — Actual Work field assist: assembly expansion and nudges** batch must follow 5c and receive
its own mechanical preflight before implementation. That preflight must lock the expansion result
and immutable-snapshot behavior, duplicate-component handling, nudge eligibility/deduplication and
dismissal, explicit add/no-action behavior, Draft concurrency, and focused regression coverage.
Do not reuse Proposed Scope behavior blindly merely because its interaction pattern is related.

### 5d preflight — locked decisions — 2026-08-20

Evaluated against Proposed Scope's existing assembly-expansion (`EditProposedScopeService
.ExpandAssemblyAsync`) and nudge (`ScopeNudgeRule`/`ScopeNudgeSuggestion`,
`ScopeNudgeFieldReadApiService`) prior art. Locked:

- **Duplicate handling — skip and report.** Applies only to automated assembly-expansion and
  nudge-add actions, not today's manual `AddLineAsync`, which keeps its existing no-guard
  behavior. If the Draft already contains a line for a given catalog item, the generated line for
  that component is skipped and the result reports which components were skipped so the UI can
  tell the technician; the action still succeeds for the remaining components.
- **Snapshot behavior — identical to a manual add.** Every generated catalog line resolves its
  display name, sell price, and Standard/Expected Direct Cost snapshot exactly as a manually added
  line does, inside the single atomic expansion transaction (one `ConcurrencyVersion` bump, not
  one per line).
- **Nudge source — separate Actual Work rule set, same technical shape.** Actual Work nudges reuse
  the price-blind association mechanics (trigger → suggested targets, no stored price data) but do
  not read Proposed Scope's `ScopeNudgeRule` rows. Proposed Scope nudges express commercial/upsell
  intent; Actual Work nudges must express factual-completion pairing. A new, separately
  Owner/Admin-configured rule set keeps that editorial ownership from blurring together later.
- **Nudge eligibility/dismissal — stateless, matching Proposed Scope's pattern.** No persisted
  dismiss state; the read service filters out any suggestion already on the current Draft or no
  longer eligible, on every call.
- **Explicit add — ordinary single-line add.** Tapping a nudge suggestion calls the same
  `AddLineAsync` path as a manual add; there is no separate "suggested line" staging state.
- **Draft concurrency — single token, no parallel path.** Expansion and nudge-add both go through
  the Draft's one `ConcurrencyVersion`, the same optimistic-concurrency model as every other Draft
  mutation.
- **Batch split.** 5d splits into **5d-i — assembly expansion** (atomic expansion, per-item
  snapshot resolution, skip-and-report result, concurrency/authorization tests) and **5d-ii —
  Actual Work nudges** (separate configuration/read/add flow, stateless eligibility filtering, no
  persisted dismissal), as two independently gated sessions rather than one batch, to stay inside
  the hard batch-size gate.

### 5d-ii preflight — locked decisions — 2026-08-20

Mechanical preflight against the prior-art seam (`ScopeNudgeRule`/`ScopeNudgeSuggestion`,
`IScopeNudgeRulePersistence`, `ScopeNudgeRuleConfigApiService`, `ScopeNudgeFieldReadApiService`,
`ScopeNudgeRuleEndpoints`) confirmed every named symbol exists as described. A literal single-session
5d-ii would require a new domain pair (`ActualWorkNudgeRule`/`ActualWorkNudgeSuggestion` +
set validator + errors), a new persistence contract/EF implementation/two EF configurations/one
migration, an Owner/Admin config API service (Create/Update/Delete — 3 mutation families alone),
a technician field-read API service, endpoints, DI registration, `ErrorHttpMapper` additions, and
frontend wiring — ~15 production files and 3+ mutation families, over the hard batch-size gate.
Locked:

- **Five-way split**, not the two-way 5d-i pattern, because 5d-ii-a alone (domain + persistence
  contract + EF persistence + two EF configurations + migration artifacts) already exceeds the
  eight-production-file gate as one batch:
  1. **5d-ii-a1 — domain + application persistence contract.** `ActualWorkNudgeRule`,
     `ActualWorkNudgeSuggestion`, `ActualWorkNudgeSuggestionSetValidator`,
     `ActualWorkNudgeRuleErrors`, `IActualWorkNudgeRulePersistence`. No EF, no migration, no API.
  2. **5d-ii-a2 — EF persistence, mappings, migration, persistence tests.**
     `EfActualWorkNudgeRulePersistence`, `ActualWorkNudgeRuleConfiguration`,
     `ActualWorkNudgeSuggestionConfiguration`, the migration (Christian runs `dotnet ef`), and
     persistence tests.
  3. **5d-ii-b — Owner/Admin config API.** Create/Update/Delete + list, mirroring
     `ScopeNudgeRuleConfigApiService`/`ScopeNudgeRuleEndpoints` exactly.
  4. **5d-ii-c — technician field-read API.** Mirrors `ScopeNudgeFieldReadApiService`; explicit add
     reuses `ActualWorkDraftApiService.AddLineAsync`, no new mutation handler here.
  5. **5d-ii-d — frontend.** Fetch nudges in `ActualWorkComposer`, render suggestion chips, tap-to-add
     via the existing add-line path.
- **Config CRUD gate — `PriceBookCatalogManage`.** Same permission as
  `ScopeNudgeRuleConfigApiService`: this is catalog/editorial configuration, not technician capture.
  No distinct permission key unless a future authority boundary requires one.

### 5d-ii-a1 implementation notes — 2026-08-20

Domain pair and application persistence contract implemented and tested (5 production files,
matching the locked estimate): `ActualWorkNudgeRule`, `ActualWorkNudgeSuggestion`,
`ActualWorkNudgeSuggestionSetValidator`, `ActualWorkNudgeRuleErrors` (all mirroring
`ScopeNudgeRule`'s shape, distinct table/entities), `IActualWorkNudgeRulePersistence`
(`ActualWorkNudgeRuleCommitResult` + per-rule CRUD contract). No EF, migration, or API surface in
this batch. 2 new unit test files (`ActualWorkNudgeRuleTests`, `ActualWorkNudgeSuggestionSetValidatorTests`),
15/15 passing.

### 5d-ii-a2 implementation notes — 2026-08-20

EF persistence, mappings, and migration implemented and tested (3 production files, matching the
locked estimate): `ActualWorkNudgeRuleConfiguration`/`ActualWorkNudgeSuggestionConfiguration`
(mirroring `ScopeNudgeRuleConfiguration`/`ScopeNudgeSuggestionConfiguration` exactly — same
exclusive-trigger/exclusive-target check constraints, composite tenant-scoped FKs, database-level
Cascade delete from suggestion to rule), `EfActualWorkNudgeRulePersistence`. Migration
`20260820191413_ActualWorkNudgeRule` creates `keep_pricebook_actual_work_nudge_rules` and
`keep_pricebook_actual_work_nudge_suggestions`; validated against the entity/configuration shape
and confirmed both projects build clean with no pending-model-changes drift.
`ActualWorkNudgeRulePersistenceTests` (1 new file, 9/9 passing against real PostgreSQL) covers
duplicate-trigger uniqueness (both trigger types), the `ReplaceSuggestions`/`SaveAsync` round-trip,
cascade delete of suggestion rows, account-scoped listing, the composite-parent-FK cross-account
rejection, and both database check constraints.

### 5d-ii-b implementation notes — 2026-08-20

Owner/Admin config API implemented and tested (5 production files, matching the locked estimate):
`ActualWorkNudgeRuleConfigApiService` (List/Create/Update/Delete, mirroring
`ScopeNudgeRuleConfigApiService`'s gate composition, existence-only write-time target checks, and
per-rule CRUD shape exactly) and `ActualWorkNudgeRuleEndpoints` (thin route mapping under
`/keep/pricebook/actual-work-nudge-rules`), plus DI registration
(`IActualWorkNudgeRulePersistence` → `EfActualWorkNudgeRulePersistence`, first consumer) and two
new explicit `ErrorHttpMapper` entries (`ActualWorkNudgeRule.TargetNotFound` → 404,
`ActualWorkNudgeRule.DuplicateTrigger` → 409; `.NotFound` already covered by the generic suffix
rule, remaining domain-shape errors fall through to the existing 400 default). 1 new test file
(`ActualWorkNudgeRuleApiTests`, mirroring `ScopeNudgeRuleApiTests`), 17/17 passing.

### 5d-ii-c implementation notes — 2026-08-20

Technician field-read API implemented and tested (4 production files, matching the locked estimate):
`ActualWorkNudgeFieldReadApiService` (mirrors `ScopeNudgeFieldReadApiService`'s trigger-parse/
rule-lookup/eligibility-filter shape) and `ActualWorkNudgeFieldReadEndpoints` (thin route mapping,
`GET /keep/pricebook/actual-work/{actualWorkId}/nudge-suggestions`), plus route registration in
`Program.cs` and DI registration in `KeepServiceCollectionExtensions.cs`. No new mutation handler —
explicit add reuses `ActualWorkDraftApiService.AddLineAsync` directly (frontend wiring is 5d-ii-d).
No new `ErrorHttpMapper` entries — `ActualWork.NotDraft` and the generic `.NotFound` suffix rule
already cover every failure path.

Two decisions locked during preflight, since the mirror target's model didn't resolve them cleanly:

1. **Authorization mirrors `ActualWorkDraftApiService`, not ScopeNudge's row-visibility read.** The
   gate composition and row authorization reuse `AuthorizeAndLoadDraftAsync`'s pattern exactly
   (`RequestsOperate` + Price Book entitlement + `ActualWorkCapture`, then
   `IActiveResponsibleCheck.IsActiveResponsibleAsync` against the Draft's `RequestId`) because an
   Actual Work Draft is exclusive to its active Responsible participant, unlike a ProposedScope's
   broader row-visibility read. A non-Responsible caller gets `KeepRequestErrors.NotFound`; a
   non-Draft visit gets `ActualWorkErrors.NotDraft` — both indistinguishable from every other Draft
   mutation's failure shape.
2. **Dedupe suppresses only catalog-item suggestions already on the Draft.** `ActualWorkLine` retains
   no `OfferingAssemblyId` (assembly provenance is discarded after expansion), so suggestion dedupe
   matches only `SuggestedCatalogItemId` against the Draft's existing lines' `CatalogItemId`.
   Assembly-targeted suggestions are never suppressed by this endpoint; partial/full prior expansion
   is reported by the existing expand-assembly endpoint's skip-and-report result instead.
3. **Account posture is Blocked-only**, matching `ScopeNudgeFieldReadApiService`'s read gate (not
   `ActualWorkDraftApiService`'s mutation Blocked||ReadOnly posture) — price-blind, non-mutating
   availability data; a read-only account may see suggestions even though the later add action stays
   unavailable until the account leaves read-only.

11 new HTTP integration tests (`ActualWorkNudgeFieldReadApiTests`), 11/11 passing against real
PostgreSQL: happy path, ineligible suggestion omitted, catalog-item dedupe, assembly suggestion never
suppressed, ineligible trigger, no rule configured, missing/combined trigger parameters, non-
Responsible caller, submitted (non-Draft) visit, and missing entitlement.

### 5d-ii-d implementation preflight — 2026-08-20

Mechanical preflight only (5d-ii's locked split already fixed this batch's scope). Every named
symbol confirmed present, no drift from the locked estimate:

- `GET /keep/pricebook/actual-work/{actualWorkId}/nudge-suggestions`
  (`ActualWorkNudgeFieldReadEndpoints`, 5d-ii-c) — query params
  `triggerCatalogItemId`/`triggerOfferingAssemblyId`, response
  `{ ruleId, triggerCatalogItemId, triggerOfferingAssemblyId, suggestions: [{ id, order,
  catalogItemId, offeringAssemblyId, displayName }] }`. No `targetKind` field — unlike
  `ScopeNudgeSuggestionFieldRowResponse`, this is not a literal type copy.
- Commit points that must fire the trigger: `addMutation` (catalog line, existing) and
  `expandAssemblyMutation` (assembly expand, 5d-i-b) inside `ActualWorkComposer.tsx`'s
  `ActualWorkSearchAndAdd`. Off-catalog custom lines carry no `catalogItemId`/`offeringAssemblyId`
  and cannot fire a trigger, matching the backend contract.
- Accept dispatch targets: `api.addActualWorkLine` (catalog suggestion, quantity 1, no note) and
  `api.expandActualWorkAssembly` (assembly suggestion, `includedOptionalItemIds: []` — ActualWork's
  inclusion-list shape from 5d-i-a, not ProposedScope's `excludedOptionalItemIds`). Both already
  exist in `apiClient.ts`; a 409 defers to the existing `onConflict()` reconciliation path already
  wired for every other composer mutation.
- `ComposerNudgePanel.tsx`/`useProposedScopeCapture.ts`'s nudge state (Session 5, build-log/125) is
  the mirror target for UX (session-only "Often added together" chip panel, client-side Dismiss,
  no persistence) but not a literal reuse target: ActualWork keeps its mutation logic inline in
  `ActualWorkComposer.tsx` (established in 5b/5d-i-b) rather than ProposedScope's extracted
  `ComposerSearchAndAdd.tsx`/`ComposerNudgePanel.tsx`/hook-owned-state split, so nudge state/fetch
  belongs inline in `ActualWorkComposer.tsx`, not in `useActualWorkCapture.ts` (which only owns
  probe/draft/modal state, no line mutations) or a new standalone panel file.
- `ActualWorkComposer.tsx`'s own doc comment (line 48) currently reads "no assemblies, no nudges,
  no Undo" — stale since 5d-i-b shipped assembly expansion; needs correcting in this batch too.

**Exact file-level gate (0 new mutation families — reuses `addActualWorkLine`/
`expandActualWorkAssembly` directly, no new handler):**

1. `web/ophalo-app/src/lib/apiClient.types.ts` — add `ActualWorkNudgeSuggestionFieldRowResponse`,
   `ActualWorkNudgeFieldResultResponse`.
2. `web/ophalo-app/src/lib/apiClient.ts` — add `getActualWorkNudgeFieldSuggestions`.
3. `web/ophalo-app/src/pages/request-detail/ActualWorkComposer.tsx` — nudge state/fetch/chip panel
   inline in `ActualWorkSearchAndAdd`; correct the stale doc comment.
4. `web/ophalo-app/src/pages/request-detail/__tests__/ActualWorkComposer.test.tsx` — fetch-after-
   commit (both trigger kinds), render, tap-to-add success, dismiss, 409-reconcile cases.

No unresolved decisions block implementation; the inline-vs-extracted-file choice above is a
preflight finding, not an open product/architecture decision, since it follows the file's own
established pattern. Awaiting Christian's go-ahead to implement.

### 5d-i-a implementation notes — 2026-08-20

Backend expansion seam implemented and tested (7 production files, matching the locked estimate):
`IActualWorkAssemblyExpansionPersistence`/`EfActualWorkAssemblyExpansionPersistence` (new),
`ActualWorkDraftApiService.ExpandAssemblyAsync`, `ActualWorkErrors` (two new codes), the
`POST /keep/pricebook/actual-work/{actualWorkId}/expand-assembly` route in `KeepEndpoints.cs`, its
DI registration in `KeepServiceCollectionExtensions.cs`, and two new `ErrorHttpMapper` lines.

Two corrections made during the preflight review before implementation, both preserved here since
they change the locked design:

- **No pre-transaction tracked load.** `ActualWorkDraftApiService.ExpandAssemblyAsync` performs
  only the account-level `AuthorizeAsync()` gate — no row read. The active-Responsible check moved
  inside `EfActualWorkAssemblyExpansionPersistence.ExpandAsync`, run immediately after the
  transaction's own `FOR UPDATE` lock on the Draft (the first tracked load of that aggregate
  anywhere in the call path). This avoids handing an already-tracked `ActualWork` to the locked
  seam, which would have let EF's identity map silently reuse the pre-lock entity instead of the
  transaction's authoritative locked read. A `NotResponsible` outcome maps to the same
  indistinguishable `KeepRequestErrors.NotFound` (404) `AuthorizeAndLoadDraftAsync` already used.
- **Optional-item inclusion validated by `OfferingAssemblyItem.Id`**, not `CatalogItemId` — mirrors
  `IOfferingAssemblyExpansionPersistence`'s `excludedOptionalItemIds` convention exactly (just
  inverted to an inclusion list per the locked contract).

Tests: `ActualWorkAssemblyExpansionPersistenceTests` (new, 1 test — the two-transaction eligibility-
recheck race proof, the one guarantee a full-stack HTTP test cannot express) plus 7 new
`ActualWorkDraftApiTests` HTTP tests (happy path, optional-default-out, explicit optional inclusion,
skip-and-report, invalid-inclusion 400, stale-version 409, not-responsible 404, viewer-denied 403).
66/66 Actual Work integration tests and 25/25 unit/architecture tests passing.

### 5d-i-b implementation notes — 2026-08-20

Frontend assembly expansion is implemented. `ActualWorkComposer` now renders an explicit
“Add assembly” affordance for an `OfferingAssembly` field-search result and calls the direct
Actual Work expansion endpoint with an empty optional-inclusion list, preserving the locked
optional-default-out behavior. Its result remains price-blind: the UI reports only generated-line
and skipped-component counts, then awaits the normal Draft refresh before another mutation can
use the refreshed concurrency version. A 409 follows the existing Actual Work reconcile path.

The client uses separate `ExpandActualWorkAssemblyBody`/`ExpandActualWorkAssemblyResult` types,
because the direct Actual Work response adds `skippedCatalogItemIds` and names its token
`actualWorkConcurrencyVersion`; Proposed Scope's shape is not reusable without hiding that
contract difference.

`FieldScopeSearchApiService` now permits `RequestsOperate` plus either `ScopeCapture` or
`ActualWorkCapture`, reopening ADR-480's exact composition as locked. The production role matrix
currently grants both capture permissions together to every permitted role, so a literal
ActualWorkCapture-only HTTP principal cannot be seeded without changing that unrelated matrix.
The focused HTTP regression covers the supported Actual Work operator path and the existing
ScopeCapture callers remain covered by the field-search suite.

The preflight's eight-file estimate narrowed to six modified files after inspection: the existing
Actual Work composer, not `useActualWorkCapture`, owns its line mutations and can await the same
`onCommitted` Draft refresh directly; no hook or hook-test change is necessary. Tests: 26 focused
frontend tests passing, TypeScript check passing, and 44 focused Field Scope Search/Actual Work
HTTP integration tests passing. `git diff --check` clean.

### 6 preflight — locked decisions — 2026-08-20

Mechanical preflight against the submit/signal seam (`IActualWorkSubmissionPersistence`/
`EfActualWorkSubmissionPersistence`, `ActualWork.Submit`, `KeepRequestWorkSignalKeys.Signals
.ActualWorkNeedsOfficeReview`) and the cross-module Owner/Admin mutation analog
(`MarkFeedbackReviewedService`) confirmed every named symbol exists as described. Locked:

- **Authorization.** Owner/Admin role check, `RequestsOperate` permission, and the existing Price
  Book entitlement (`CapabilityPackageFeatureKeys.PriceBookQuotesMaterials`). No new
  `ActualWorkReview` permission key — this is an office-only role capability, not a separately
  delegable technician capability, unlike `ActualWorkCapture`.
- **New `ActualWork` fields.** Nullable `ReviewedAtUtc`, `ReviewedByAccountUserId`, `ReviewNote`
  (optional, trimmed to null, max 2,000 chars — matches the feedback-review note convention).
  `ActualWorkStatus` does not gain a value; `Status` stays `Submitted` per its existing doc comment.
- **Single-shot.** Only a `Submitted`, not-yet-reviewed visit (`ReviewedAtUtc IS NULL`) can be
  marked reviewed. A repeat review is a conflict error; it never overwrites reviewer, timestamp, or
  note — mirrors ADR-275's feedback-review precedent.
- **Signal resolution.** Per-request aggregate across all `ActualWork` rows, atomic with the
  triggering review: resolve `ActualWorkNeedsOfficeReview` only when no `Submitted` visit remains
  with `ReviewedAtUtc IS NULL` for that request. Mirrors `EfActualWorkSubmissionPersistence`'s raise/
  reopen upsert, inverted to a conditional resolve, in the same transaction as the review write.
- **Terminal-request posture — confirmed 2026-08-20.** Not blocked. A submitted visit must remain
  reviewable so its aggregate review signal cannot be stranded after the request closes; unlike
  ADR-488's ProposedScope review, no terminal-request lock applies here. No `SELECT ... FOR UPDATE`
  terminal check in the review transaction.
- **Two-way split — corrected 2026-08-20.** The original single-session estimate undercounted
  `KeepEndpoints.cs` and `KeepServiceCollectionExtensions.cs` as one file pair; they are two
  separate production files, putting a single session at 9 production files, over the eight-file
  hard gate. Split by layer, matching the 5d-i two-way precedent:
  1. **6A — domain, persistence, migration.** `ActualWork.cs`, `ActualWorkErrors.cs`,
     `ActualWorkConfiguration.cs`, migration, `IActualWorkReviewPersistence.cs`,
     `EfActualWorkReviewPersistence.cs`. No API, no DI registration.
  2. **6B — review API service, endpoints, DI.** `ActualWorkReviewApiService.cs`,
     `KeepEndpoints.cs`, `KeepServiceCollectionExtensions.cs`, API tests. Depends on 6A's merged
     persistence contract.

**6A file/test-count gate** (6 production files, within the eight-file/one-mutation-family gate;
domain + persistence layer only, no mutation handler exposed yet):
1. `Core/Entities/ActualWork.cs` — `ReviewedAtUtc`/`ReviewedByAccountUserId`/`ReviewNote` fields,
   `MarkReviewed` domain method.
2. `Core/Errors/ActualWorkErrors.cs` — `NotSubmitted` (Draft, not yet submitted),
   `AlreadyReviewed` (conflict), `ReviewNoteTooLong`.
3. `Infrastructure/Persistence/Configurations/ActualWorkConfiguration.cs` — map the three new
   columns.
4. Migration (Christian runs `dotnet ef`, `--startup-project src/OpHalo.Keep.Infrastructure`) +
   Designer + model-snapshot update.
5. `Application/PriceBook/IActualWorkReviewPersistence.cs` — new interface, result enum, outcome
   record, mirroring `IActualWorkSubmissionPersistence`'s shape.
6. `Infrastructure/Persistence/EfActualWorkReviewPersistence.cs` — atomic transaction: tracked load
   + version check + `MarkReviewed` domain transition + conditional signal resolve (no terminal
   check), one commit.

6A test files (2, new/modified):
1. `tests/OpHalo.UnitTests/Keep/ActualWorkTests.cs` — `MarkReviewed` domain cases: success, not-
   submitted, already-reviewed, note trimming/length.
2. `tests/OpHalo.IntegrationTests/Persistence/ActualWorkReviewPersistenceTests.cs` — new, mirrors
   `ActualWorkSubmissionTests.cs`: commit + signal resolves (last unreviewed visit), commit + signal
   stays active (another unreviewed submitted visit remains on the request), not found, version
   mismatch, not-submitted, already-reviewed conflict.

6A totals: 6 production files, 2 test files, 8 total files, zero mutation handler families
(persistence/domain only). Under the hard gate.

### 6B preflight — mechanical drift found — 2026-08-20

Mechanical preflight against the merged 6A persistence contract and `ErrorHttpMapper.cs`'s
existing Actual Work entries confirmed every symbol named in the original 6B gate still exists,
with one gap: `ErrorHttpMapper.cs` was not in the original file count. `ActualWork.NotFound` is
already covered by the generic `.NotFound` suffix fallback (404), but `ActualWork.NotSubmitted`
and `ActualWork.AlreadyReviewed` need explicit 409 entries — matching the existing
`ActualWork.NotDraft`/`VersionMismatch`/`DraftAlreadyOpenForRequest` state-conflict precedent,
not the default 400 fallback — and `ActualWork.ReviewNoteTooLong` needs an explicit 400 entry for
consistency with the file's existing pattern (`ExpandInclusionItemInvalid` gets one despite 400
already being the default). Corrected gate below: 4 production files, still under the eight-file
hard gate.

**6B file/test-count gate** (4 production files):
1. `Application/PriceBook/ActualWorkReviewApiService.cs` — new service owning the Owner/Admin auth
   stack (RequestsOperate + Price Book entitlement + Owner/Admin role check, no `ActualWorkCapture`)
   and mapping the persistence outcome to a `Result`.
2. `Api/Keep/KeepEndpoints.cs` — new `POST /keep/pricebook/actual-work/{actualWorkId}/review` route
   (reuses the existing `X-Keep-ActualWork-Version` header).
3. `Api/Keep/KeepServiceCollectionExtensions.cs` — DI registration for
   `IActualWorkReviewPersistence`/`EfActualWorkReviewPersistence` and
   `ActualWorkReviewApiService`.
4. `ErrorHttpMapper.cs` — explicit `ActualWork.NotSubmitted`/`AlreadyReviewed` (409) and
   `ReviewNoteTooLong` (400) entries.

6B test files (1, new):
1. `tests/OpHalo.IntegrationTests/Api/ActualWorkReviewApiTests.cs` — new, endpoint-level
   200/403 (non-Owner/Admin, missing entitlement)/404/409 cases.

### 6B implementation notes — 2026-08-20

Implemented as gated (4 production files, 1 test file). `ActualWorkReviewApiService` composes its
own auth stack rather than reusing `ActualWorkDraftApiService.AuthorizeAsync` — that helper's gate
3 requires `ActualWorkCapture`, which office review deliberately does not check. Endpoint follows
the existing `POST .../actual-work/{id}/...` + `X-Keep-ActualWork-Version` header convention
exactly. `ErrorHttpMapper.cs` gained explicit `ActualWork.NotSubmitted`/`AlreadyReviewed` (409) and
`ReviewNoteTooLong` (400) entries; `ActualWork.NotFound` needed no entry (generic `.NotFound`
suffix fallback already covers it).

8/8 new `ActualWorkReviewApiTests` passing (Owner 200 + review fields set, Admin 200, Operator 403,
missing-entitlement 403, unknown-visit 404, stale-version 409, Draft-visit 409/`NotSubmitted`,
reviewed-twice 409/`AlreadyReviewed` with no field overwrite). Full regression: 130/130 Actual Work
integration tests, 49/49 Actual Work unit tests, no regressions. `git diff --check` clean.

Slice 6 (6A + 6B) is complete.

6B totals: 3 production files, 1 test file, 4 total files, one mutation handler family (mark
reviewed). Under the hard gate.

### 6A implementation notes — 2026-08-20

Domain/persistence/migration layer is implemented as gated. `ActualWork.MarkReviewed` adds the
three nullable fields and the single-shot domain transition (no `Status` change, per
`ActualWorkStatus`'s existing doc comment). `IActualWorkReviewPersistence`/
`EfActualWorkReviewPersistence` own the atomic transaction: tracked load, version check, domain
transition, `SaveChangesAsync`, then a conditional `UPDATE ... WHERE resolved_at_utc IS NULL AND
NOT EXISTS (...)` resolve of `ActualWorkNeedsOfficeReview` scoped to the request — no terminal
check, confirming the locked "not blocked" decision above. Migration `AddActualWorkReview`
(`20260820235826`) adds `review_note`/`reviewed_at_utc`/`reviewed_by_account_user_id` to
`keep_actual_works`; verified against the entity/EF configuration and the model snapshot diff
before tests were written — no drift, no unrelated snapshot changes.

6 production files as gated, 2 test files as gated. 34/34 `ActualWorkTests` unit tests passing
(6 new `MarkReviewed` cases), 8/8 new `ActualWorkReviewPersistenceTests` integration tests passing
against real PostgreSQL (commit-resolves, commit-leaves-active-with-another-unreviewed-visit,
last-unreviewed-then-resolves, not-found, wrong-account, version-mismatch, not-submitted,
already-reviewed-no-overwrite). `git diff --check` clean.

### Pilot draft-concurrency decision

The pilot locks **one open Draft visit per request**. If multiple technicians are present, the
assigned Responsible user remains accountable for the job details and records the one field visit.
The pilot does not introduce independent second-technician drafts, a shared draft, or takeover.

All remaining items are mechanical-preflight choices constrained by this document and ADR-487:
aggregate/table names, API/DTO shape, version header, persistence transaction, exact read
visibility query, and focused authorization/concurrency/failure tests.

### Approved implementation sequence

Each session must publish its exact file/test count for the hard batch gate before edits. A
foundation session is not feature completion; the next named session follows immediately.

1. **Actual Work domain.** Visit/line aggregate, immutable financial snapshots, draft lifecycle,
   zero-line outcome invariant, and domain tests.
2. **Persistence and migration.** EF mappings, exact active-Draft index, persistence contract,
   migration/designer/model-snapshot files counted individually, and persistence tests.
3. **Draft API and authorization.** Create/edit/discard service and endpoints, `ActualWorkCapture`,
   reusable active-Responsible check, and authorization/concurrency contracts.
4. **Submission and review signal.** Atomic submit, zero-line boundary validation, and additive
   Actual Work review-signal raise/reopen behavior.
5. **Field capture UI.** Request Detail composer, client API/types, retry/error behavior, and
   read-only submitted visit history.
5d. **Actual Work field assist: assembly expansion and nudges.** Price-blind technician support
   for finding and recording complete factual work; separate preflight required before code.
6. **Owner/Admin review mutation.** Mark reviewed, reviewer/time/note, and atomic aggregate-signal
   resolution.
7. **Owner/Admin financial read.** Immutable-snapshot totals, Standard/Expected Direct Cost,
   margin, and incomplete-data projection/API.
8. **Owner/Admin review UI.** Existing Requests-workspace Actual Work Review tab plus request-detail
   review card; review action updates the queue and history.

## Required later closeout/handoff decisions

The pilot implementation sequence above resolves Actual Work aggregate, Draft, submission,
review, snapshot, and visibility behavior. The later closeout/export preflight must still lock:

1. Exact closeout eligibility over reviewed visits, including the hard block on a line lacking a
   valid sales-price or Standard/Expected Direct Cost snapshot.
2. Exact CSV schemas, export audit event, retry/idempotency behavior, and later-correction rule.
3. `PermissionKeys.Keep.AccountingManage` is the accounting mutation/export seam and maps to
   Owner/Admin for the first pilot. No separate Accountant role or accounting-user UI is in the
   launch scope. A later role may receive this permission without changing accounting APIs, but
   role/membership and UI work remains a deliberate later slice.
4. Exact invoice/reference and `Other`-note validation.

## Non-goals

- Customer quote delivery, acceptance, signatures, invoices, payment collection, QuickBooks sync,
  tax engine, inventory, payroll, routes, or a task-routing board.
- Asset/equipment identity and equipment history UI; a later Asset Operations package may read
  factual Actual Work records only.
- Converting, editing, or deleting submitted Proposed Scope records.
