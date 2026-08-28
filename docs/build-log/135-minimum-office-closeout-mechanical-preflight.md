# Build Log 135 — Minimum Office Closeout: Mechanical Preflight (Batch 0, no code)

**Status:** Mechanical preflight complete (rev. 4 — eleven review corrections applied over three
review rounds) — implementation batches gated, no code written. Batch 1 is a separate coding
session.
**Date:** 2026-08-27
**Related:** ADR-493, ADR-487, ADR-467, ADR-468, ADR-462, ADR-463, ADR-480; Build Log 129
("Minimum Office Closeout implementation sequence — locked"); session-log "Next after the release
gate — Minimum Office Closeout Batch 0 (no code)"

## Scope of this document

Batch 0 of the ADR-493 / Build Log 129 closeout sequence. This is a mechanical map only: it names
exact target files, DTOs, endpoints, error mapping, permission/entitlement gates,
transaction/concurrency boundaries, database constraints, and focused tests for the later batches,
plus per-batch production/test file counts and mutation-handler families against the CLAUDE.md
batch gate. It writes no production code, migration, API type, or UI.

Rev. 2 applies the five corrections from Christian's review of rev. 1:

1. **Billing Revision must freeze financial facts** — `BillingRevisionVisit`/`BillingRevisionLine`
   snapshot rows, written and frozen at `ReadyForBilling`; summary/CSV read the snapshot, never
   live Actual Work + resolutions.
2. **Three-column line FK** — `ActualWorkLine` gains a `(AccountId, ActualWorkId, Id)` alternate
   key; the resolution FK uses all three columns so a resolution provably belongs to that exact
   visit's line.
3. **Resolution/disposition blocked after review** — a reviewed visit (`ReviewedAtUtc != null`,
   still `Status == Submitted`) rejects new resolution/disposition rows; post-review changes go
   through the deferred correction/adjustment path.
4. **Batch counts corrected and batches split now** — Batch 2 defers DI; Batch 3a splits into
   3a-i / 3a-ii; Batch 3b splits into 3b-i / 3b-ii; Batch 6 defers DI.
5. **The three open confirmations are locked** (see [§6](#6-locked-decisions)).

Rev. 3 applies four further corrections from the review of rev. 2:

6. **Freeze complete resolution/disposition provenance** — `BillingRevisionLine` freezes, per
   resolved component, the resolution id, basis, reason, resolved-by name + id, and resolved-at;
   `BillingRevisionVisit` freezes the zero-line `NoCharge` disposition kind/reason/actor/time
   (§3). ADR-493 §5 requires the summary to show this audit data.
7. **`BillingRevisionVisit` alternate key** — Batch 6 adds `HasAlternateKey(AccountId, Id)` so
   `BillingRevisionLine`'s composite FK has a principal key to target, mirroring the D2 fix.
8. **Proof 5 test corrected** — new resolutions are blocked after review, so the proof is a
   persistence fixture that mutates live source rows after freeze + a read-shape assertion, not a
   user-level "later resolution".
9. **Batch 3a-ii file list de-duplicated** — the endpoint file and the DI file are now separate
   numbered items; items 4 + 5 collapse to one file (`ActualWorkFinancialReadApiService.cs` holds
   both the projection and the DTO records), keeping the count at exactly 8.

Rev. 4 applies two final consistency corrections from the review of rev. 3:

10. **`Draft`-voided revision read shape** — a `Draft` revision voided before it ever froze has no
    snapshot rows. `Voided` reads split on `FrozenAtUtc`: non-null → frozen rows; null → void
    audit + released-membership history only, no financial section (locked — option 1). §3, Batch
    7a/7b/8, proof 5.
11. **Batch 7b test-file count** — two integration test files
    (`BillingRevisionLifecycleApiTests`, `BillingRevisionFreezeTests`), so 6 prod / 2 test, total
    8 (was mislabelled 1 test). Still within the gate.

Locked product contract is ADR-493 and BL129 "Minimum Office Closeout foundation". Nothing here
reopens it. No locked contract was found to be un-implementable truthfully.

## UI boundary — read this before Batch 4 or Batch 8

Financial-resolution inputs, the no-charge disposition control, blocker explanations, and the
Billing Revision summary live **only** on the Owner/Admin office-review surface —
`ActualWorkReviewCard.tsx` and its own hooks. They must **never** be added to the price-blind field
surface `ActualWorkComposer.tsx`, nor to any Operator/field read. ADR-493 §6 and ADR-487 make price
blindness a property of field capture, not of a role. Queue expansion of the review card into a
standalone Office Review UI is a separately bounded slice, not part of Batch 4.

## 1. Existing surface — confirmed present (mechanical preflight)

All symbols the later batches build on still exist at the paths below.

### Domain (`src/OpHalo.Keep.Core`)

| Symbol | File | Notes |
|---|---|---|
| `ActualWork` | `Entities/ActualWork.cs` | Aggregate. `Status`, `Outcome`, `CompletionNote`, `SubmittedAtUtc`, `ReviewedAtUtc`, `ReviewedByAccountUserId`, `ReviewNote`, `RecorderAccountUserId`, `ConcurrencyVersion` (app-managed `Guid`, `IsConcurrencyToken`, `ValueGeneratedNever`). Owned `Lines`. `MarkReviewed(reviewedBy, note, atUtc)` is single-shot; **a reviewed visit stays `Status == Submitted`** (this is why correction #3 is needed). |
| `ActualWorkLine` | `Entities/ActualWorkLine.cs` | Immutable after submit. `CatalogItemId?`, `PriceBookVersionLineId?`, `SellPriceSnapshot?`, `StandardExpectedDirectCostSnapshot?`, `ActualQuantity`, `DisplayNameSnapshot`, `UnitOfMeasureSnapshot?`. Three linkage states. Config has index `(AccountId, ActualWorkId)` but **no alternate key** — correction #2 adds `(AccountId, ActualWorkId, Id)`. |
| `ActualWorkStatus` | `Entities/Enums/ActualWorkStatus.cs` | `Draft`, `Submitted` only. Stored `HasConversion<string>`, `maxLength(50)`. |
| `ActualWorkOutcome` | `Entities/Enums/ActualWorkOutcome.cs` | `DiagnosticOnly`, `NoWorkAuthorized`, `NoAccess`. Required on a zero-line submit. |
| `ActualWorkErrors` | `Errors/ActualWorkErrors.cs` | `NotFound`, `NotDraft`, `NotSubmitted`, `AlreadyReviewed`, `VersionMismatch`, `ReviewNoteTooLong`, `ExpectedVersionRequired/Invalid`, GAP-055 recorder-transfer errors. |
| `ActualWorkDraftRecorderTransfer` | `Entities/ActualWorkDraftRecorderTransfer.cs` | Append-only audit-record pattern to copy: `BaseEntity`, private ctor + static `Create`, required trimmed `Reason`, actor + timestamp. |
| `KeepRequestWorkSignal` | `Entities/KeepRequestWorkSignal.cs` | `Modules.PriceBookQuotesMaterials`, `Signals.ActualWorkNeedsOfficeReview`. Closeout adds **no** new signal (ADR-493 §2). |

### Application (`src/OpHalo.Keep.Application/PriceBook`)

| Symbol | File | Notes |
|---|---|---|
| `IActualWorkPersistence` | `IActualWorkPersistence.cs` | `GetByIdAsync(accountId, id, ct)` (tracked, `Lines` included). `ActualWorkCommitResult { Committed, ConcurrencyConflict, DraftAlreadyOpenForRequest }`. |
| `IActualWorkReviewPersistence` | `IActualWorkReviewPersistence.cs` | Atomic mark-reviewed + signal-resolve. `ActualWorkReviewResult { Committed, NotFound, NotSubmitted, AlreadyReviewed, ReviewNoteTooLong, VersionMismatch }`, `ActualWorkReviewOutcome(Result, ConcurrencyVersion?)`. |
| `ActualWorkReviewApiService` | `ActualWorkReviewApiService.cs` | `MarkReviewedAsync(id, note, expectedVersion, ct)`. Auth today: authenticated → account-access policy (not blocked/read-only) → `CapabilityPackageFeatureKeys.PriceBookQuotesMaterials` entitlement → **explicit Owner/Admin role check** → `PermissionKeys.Keep.RequestsOperate`. **No dedicated closeout permission key** — correction #5 / Batch 3a-i adds `AccountingManage` and retrofits this copy. |
| `ActualWorkFinancialReadApiService` | `ActualWorkFinancialReadApiService.cs` | `GetReviewQueueAsync`, `GetReviewQueueCountAsync`, `GetFinancialDetailAsync(id, ct)`. Same auth composition. DTOs `ActualWorkReviewQueueEntry`, `ActualWorkFinancialLineEntry`, `ActualWorkFinancialDetailResult` (carries `ConcurrencyVersion`). |
| `ActualWorkFinancialProjection` | same file, `internal static` | `IsLineComplete` (both snapshots non-null), `ComputeVisitTotals`, `ToLineEntry`. **Unrounded** decimal arithmetic today — correction folded into Batch 3a-ii (ADR-467). |
| `IActualWorkFinancialReviewPersistence` | `IActualWorkFinancialReviewPersistence.cs` | `GetUnreviewedQueueAsync`, `CountUnreviewedAsync`. `ActualWorkReviewQueueSourceRow(Visit, ReferenceCode, CustomerName)`. |

### Infrastructure (`src/OpHalo.Keep.Infrastructure/Persistence`)

| Symbol | File | Notes |
|---|---|---|
| `EfActualWorkReviewPersistence` | `EfActualWorkReviewPersistence.cs` | `BeginTransactionAsync` (Read Committed) → load by `(AccountId, Id)` → version check → domain `MarkReviewed` → `SaveChangesAsync` (catch `DbUpdateConcurrencyException`) → conditional `UPDATE keep_request_work_signals … WHERE NOT EXISTS (submitted && reviewed_at_utc IS NULL)` → commit. **The extension point for the Batch 3b-ii hard review gate.** |
| `ActualWorkConfiguration` | `Configurations/ActualWorkConfiguration.cs` | `keep_actual_works`. `HasAlternateKey(AccountId, Id)` = `ak_keep_actual_works_account_id`. Partial unique `ux_keep_actual_works_open_draft` (`filter: "status = 'Draft'"`) — **the pattern to copy for every closeout partial-unique invariant.** Composite FK to `keep_requests(account_id, id)`. `ConcurrencyVersion` `ValueGeneratedNever` token — copy for `BillingRevision`. |
| `ActualWorkLineConfiguration` | `Configurations/ActualWorkLineConfiguration.cs` | `keep_actual_work_lines`. `HasCheckConstraint("ck_keep_actual_work_lines_three_state_linkage", …)` — the pattern to copy for money/reason check constraints. Money columns `HasPrecision(19, 4)`. Composite FKs to `keep_actual_works`, `keep_pricebook_catalog_items`, `keep_pricebook_version_lines`, all via `(account_id, …)` principal keys. |
| Config discovery / DI | `OpHaloDbContext.OnModelCreating` `ApplyConfigurationsFromAssembly` loop; `KeepServiceCollectionExtensions.cs` (~lines 220–230) | New `IEntityTypeConfiguration<>` classes auto-register. New persistence/service classes need an explicit `services.AddScoped<>` line — **deferred to the batch that first consumes them** (matches the "no startup crash / register-when-consumed" convention). Existing Actual Work code reads via `dbContext.Set<T>()`, so no `DbSet<>` property is required for the new aggregates. |
| EF migrations | `src/OpHalo.Foundation.Infrastructure/Migrations` | Migrations assembly is **Foundation.Infrastructure**; startup project `src/OpHalo.Keep.Infrastructure` (ADR-049). **Christian runs `dotnet ef`, not Claude.** Snake-case names, `numeric(19,4)`, `timestamp with time zone`, string enums `character varying(50)`. |

### API (`src/OpHalo.Api`)

| Symbol | File | Notes |
|---|---|---|
| Actual Work endpoints | `Keep/KeepEndpoints.cs` (~778–990) | `POST …/actual-work/{id}/review` (907), `GET …/{id}/financial-detail` (967), `GET …/review-queue` (944). `.RequireAuthorization()` only; per-action auth in the app service. |
| `ParseActualWorkVersion` | `Keep/KeepEndpoints.cs:1268` | Header `X-Keep-ActualWork-Version`, `Guid.TryParseExact(…, "D")`, non-empty. Returns `Result<Guid>`. **Pattern to copy for `X-Keep-BillingRevision-Version`.** |
| `ErrorHttpMapper` | `Helpers/ErrorHttpMapper.cs` | `ActualWork.*` block at ~209–224. Convention: state-guard conflicts → `409`; malformed/oversize input → `400`; semantically-invalid target (`RecorderTransferTargetIneligible`, `KeepRequest.ParticipationTargetIneligible`) → `422`. Add explicit entries for the `ActualWork.`/`BillingRevision.` prefixes. |
| Response mappers | `Keep/KeepEndpoints.cs` (~1204–1245) | `ToActualWorkFinancialDetailResponse`, `ToFinancialLineResponse`, `ToActualWorkReviewQueueEntryResponse`. `ActualWorkConcurrencyVersionResponse`, `ActualWorkReviewBody(string? ReviewNote)` file-scoped record ~1304. |

### Permissions / entitlement / money

- `PermissionKeys.Keep` — `RequestsOperate`, `ScopeCapture`, `ActualWorkCapture`,
  `PriceBookCatalogManage`. **No `AccountingManage` key exists yet** (BL129 §3 locks it as the
  seam).
- `RolePermissions` — explicit set composition `Owner ⊇ Admin ⊇ Operator ⊇ Viewer`. `ScopeCapture`
  + `ActualWorkCapture` in `OperatorBase`; `PriceBookCatalogManage` in `AdminBase`.
- Entitlement: `CapabilityPackageFeatureKeys.PriceBookQuotesMaterials` via
  `IAccountFeatureAccessResolver.IsEnabledAsync` (ADR-462).
- Money/currency: ADR-468 — one currency per account, **USD**, no server-owned account-currency
  source; `ActualWorkLine` snapshots carry **no currency column**. Closeout money columns follow
  the same convention (bare `numeric(19,4)`, no currency field).
- Rounding: ADR-467 — round-half-up, each line total rounded independently, visit/revision total =
  **sum of already-rounded line totals**. `ActualWorkFinancialProjection` does not round today.
  Batch 3a-ii introduces line rounding in the recalculated projection; Batch 7b reuses it when
  writing frozen snapshot totals. Look for a reusable quote-line rounding helper first; if none is
  usable from Keep.Core without a layering breach, add a small `internal static` money-rounding
  helper beside `ActualWorkFinancialProjection`.

## 2. Mechanical drift from the brief

| # | Drift | Resolution |
|---|---|---|
| D1 | `PermissionKeys.Keep.AccountingManage` does not exist; BL129 §3 names it as the closeout mutation seam, Owner/Admin. | Batch **3a-i**: add key + `RolePermissions.AdminBase` entry (Owner inherits) + retrofit the two existing review/financial-read auth copies. |
| D2 | `keep_actual_work_lines` has no alternate key; the per-line resolution FK must prove the line belongs to that **exact visit** — separate `(account_id, actual_work_id)` and `(account_id, actual_work_line_id)` FKs still admit a same-account line from a different visit. | Batch **2**: add `ak_keep_actual_work_lines_account_id_actual_work_id_id` on `(AccountId, ActualWorkId, Id)`; the resolution FK is the full three-column key. |
| D3 | `ActualWorkFinancialProjection` arithmetic is unrounded; ADR-467. | Batch **3a-ii**: line rounding in the recalculated projection; reused by Batch 7b snapshot writer. |
| D4 | ADR-493 §2 billing-eligibility includes "not already reserved by an active Billing Revision", but the Batch 3b-ii review gate must **not** check revision membership (a visit cannot be in a revision before review). | Documented so nobody adds a revision join to `MarkReviewed`. Membership/eligibility recheck is transactional in Batch 7a assembly and again in Batch 7b freeze. |
| D5 | A reviewed Actual Work visit stays `Status == Submitted`; Batch 3a-ii's resolution API would otherwise mutate effective financial facts **after** the Owner/Admin's financial approval. | Batch **3a-ii** / **3b-i**: resolution and disposition mutations reject `ReviewedAtUtc != null` (`…VisitAlreadyReviewed`, 409). Post-review changes are the deferred correction/adjustment path (BL129 §9). |

## 3. Immutable Billing Revision snapshot model (correction #1)

ADR-493 §3: "`ReadyForBilling` freezes the revision's membership and financial contents." The
Billing Revision therefore stores its own copy of every financial fact, written once, at the
`Draft → ReadyForBilling` transition. A later financial-resolution correction on an underlying
visit can never change an already-ready revision's totals or context.

Freeze status is carried by `FrozenAtUtc` — non-null exactly when the revision reached
`ReadyForBilling` at least once. A `Draft` revision can be voided before it ever freezes, so
`Voided` splits on `FrozenAtUtc` (locked — option 1: a never-ready draft was never an approved
billing package, so it has no financial summary to show).

| Read state | Source of truth for the detail/summary read |
|---|---|
| `Draft` | **Live provisional** — recomputed from current Actual Work lines + current effective resolutions + ADR-467 rounding. Labelled provisional in the DTO; not exportable. |
| `ReadyForBilling`, `HandedOffToBilling`, `Voided` **with `FrozenAtUtc != null`** | **Frozen snapshot rows only** — `BillingRevision` context columns + `BillingRevisionVisit` visit columns + `BillingRevisionLine` rows. Never re-joined to live Actual Work / resolution records. |
| `Voided` **with `FrozenAtUtc == null`** (a Draft voided before it was ever Ready) | **No financial summary.** The detail read returns void audit (`VoidedAtUtc`, `VoidedByAccountUserId`, `VoidReason`) + retained membership history (`BillingRevisionVisit` rows, all released) only. No provisional recompute, no frozen rows. |

Frozen data (all written in the Batch 7b `MarkReadyForBilling` transaction):

- **`BillingRevision`** context columns: `CustomerNameSnapshot`, `ServiceLocationSnapshot`,
  `RequestReferenceCodeSnapshot`, `FrozenAtUtc`, `TotalSalesPriceSnapshot`,
  `TotalStandardExpectedDirectCostSnapshot`, `TotalMarginSnapshot`, `IsFinanciallyCompleteSnapshot`.
- **`BillingRevisionVisit`** per-visit columns: `RecorderAccountUserIdSnapshot`,
  `RecorderDisplayNameSnapshot`, `SubmittedAtUtcSnapshot`, `OutcomeSnapshot`,
  `CompletionNoteSnapshot`, `ReviewedAtUtcSnapshot`, `ReviewedByDisplayNameSnapshot`,
  `VisitTotalSalesPriceSnapshot`, `VisitTotalDirectCostSnapshot`. **Zero-line `NoCharge`
  disposition provenance** (null for a lined visit): `DispositionKindSnapshot?`,
  `DispositionReasonSnapshot?`, `DispositionByAccountUserIdSnapshot?`,
  `DispositionByDisplayNameSnapshot?`, `DispositionAtUtcSnapshot?` — copied from the effective
  disposition so a zero-line `NoCharge` revision summary can show the full disposition audit
  without re-joining the live record.
- **`BillingRevisionLine`** (new table, one row per included line at freeze):
  `BillingRevisionVisitId`, source `ActualWorkLineId` (reference only, no FK cascade semantics
  needed — it is historical), `DisplayNameSnapshot`, `UnitOfMeasureSnapshot`, `QuantitySnapshot`,
  `EffectiveUnitSellPrice`, `EffectiveUnitStandardExpectedDirectCost`, `LineSalesTotal` (rounded),
  `LineStandardExpectedDirectCostTotal` (rounded), `LineMargin`. **Full per-component
  financial-resolution provenance** (null when the component came straight from the captured
  snapshot, i.e. no resolution was needed): `SellPriceResolutionId?`, `SellPriceResolutionBasis?`,
  `SellPriceResolutionReasonSnapshot?`, `SellPriceResolvedByAccountUserIdSnapshot?`,
  `SellPriceResolvedByDisplayNameSnapshot?`, `SellPriceResolvedAtUtcSnapshot?`, and the identical
  five `DirectCostResolution*` columns. ADR-493 §5 requires the summary to show
  financial-resolution audit data; freezing it here is the only way it survives a later resolution
  correction on the underlying visit.

A `Void` of a `ReadyForBilling` (or `HandedOffToBilling` — not permitted; only unhanded voids)
revision keeps its frozen rows as the historical record; it only releases memberships.

## 4. Per-batch implementation map

Counts are production files / test files / **new** mutation-handler families, against the CLAUDE.md
gate (≤3 families, ≤8 production files, ≤12 total). Every batch is an independently compiling
vertical slice; start a fresh Claude session per approved commit.

### Batch 1 — Financial-resolution + zero-line-disposition domain foundation

**Layer:** Core only. **Families:** 0. **Files:** 5 prod / 2 test.

1. `Entities/ActualWorkLineFinancialResolution.cs` — immutable append-only record (`BaseEntity`,
   private ctor + `Create`). `AccountId`, `ActualWorkId`, `ActualWorkLineId`,
   `ResolvedUnitSellPrice` (`decimal?`), `ResolvedUnitStandardExpectedDirectCost` (`decimal?`),
   `Basis` (`FinancialResolutionBasis`), trimmed non-empty `Reason` (maxlen 2000),
   `ResolvedByAccountUserId`, `ResolvedAtUtc`. `Create` rejects: both values null
   (`FinancialResolutionValueRequired`); a negative value (`FinancialResolutionValueNegative`);
   undefined `Basis` (`FinancialResolutionInvalidBasis`); empty reason
   (`FinancialResolutionReasonRequired`). It cannot see snapshot/review state — "fills only a
   *missing* component, only before review" is enforced in Batch 3a-ii against the loaded visit.
2. `Entities/ActualWorkOfficeFinancialDisposition.cs` — immutable append-only **visit-level**
   record. `AccountId`, `ActualWorkId`, `Kind` (`OfficeFinancialDispositionKind`), trimmed
   non-empty `Reason`, `DisposedByAccountUserId`, `DisposedAtUtc`. Attaches to the visit, not a
   line — the shape that lets a zero-line visit reach billing eligibility (proof 1).
3. `Entities/Enums/FinancialResolutionBasis.cs` — `SupplierReceipt`, `OwnerSetPrice`,
   `FixedAgreement`, `Other`.
4. `Entities/Enums/OfficeFinancialDispositionKind.cs` — `NoCharge` only (locked §6.2). Real enum
   so a later kind is additive + exhaustively switched.
5. `Errors/ActualWorkFinancialResolutionErrors.cs` — new `static class`
   (`ActualWork.FinancialResolution*`, `ActualWork.Disposition*`).

Tests: `tests/OpHalo.UnitTests/Keep/ActualWorkLineFinancialResolutionTests.cs`,
`tests/OpHalo.UnitTests/Keep/ActualWorkOfficeFinancialDispositionTests.cs`.

### Batch 2 — Financial-resolution / disposition persistence (no DI)

**Layer:** Infrastructure + Foundation migration + Application read seam. **Families:** 0.
**Files:** 5 prod / 1 test (+ migration + `.Designer` + model-snapshot generated by Christian).

1. `Persistence/Configurations/ActualWorkLineFinancialResolutionConfiguration.cs` — table
   `keep_actual_work_line_financial_resolutions`. Composite FKs:
   `(account_id, actual_work_id)` → `keep_actual_works(account_id, id)` (Restrict);
   **`(account_id, actual_work_id, actual_work_line_id)` → `keep_actual_work_lines(account_id, actual_work_id, id)`** (Restrict) — the three-column key from D2, proving the line belongs to that
   exact visit. Check constraints (copy `ck_…_three_state_linkage` style):
   `ck_…_value_present` = at least one resolved value non-null;
   `ck_…_non_negative` = each resolved value null or `>= 0`;
   `ck_…_reason_present` = `length(btrim(reason)) > 0`. **No unique index** — supersession is
   allowed (proof 2). Index `(account_id, actual_work_line_id, resolved_at_utc)` for the
   effective-row read.
2. `Persistence/Configurations/ActualWorkOfficeFinancialDispositionConfiguration.cs` — table
   `keep_actual_work_office_financial_dispositions`. Composite FK `(account_id, actual_work_id)` →
   `keep_actual_works`. `ck_…_reason_present`. **No unique index** (corrections additive; effective
   = latest by `disposed_at_utc`). `Kind` string-converted `maxLength(50)`.
3. `Persistence/Configurations/ActualWorkLineConfiguration.cs` — **edit**: add
   `HasAlternateKey(x => new { x.AccountId, x.ActualWorkId, x.Id })` (D2). Additive migration; the
   `(account_id, actual_work_id, id)` tuple is already unique via the PK, so no data risk.
4. `src/OpHalo.Keep.Application/PriceBook/IActualWorkFinancialResolutionPersistence.cs` — **read
   seam only** this batch: `GetResolutionsForVisitAsync(accountId, actualWorkId, ct)`,
   `GetDispositionsForVisitAsync(accountId, actualWorkId, ct)`. Mutation methods are added by
   3a-ii / 3b-i.
5. `src/OpHalo.Keep.Infrastructure/Persistence/EfActualWorkFinancialResolutionPersistence.cs` —
   `account_id`-filtered reads. Class lands here; **DI registration is deferred to Batch 3a-ii**
   (first consumer).

Tests: `tests/OpHalo.IntegrationTests/Persistence/ActualWorkFinancialResolutionPersistenceTests.cs`
— real-database proof of every check constraint; the three-column FK (a resolution naming a
same-account line from a *different* visit is rejected); and **effective-resolution/supersession**
(two rows same line+component → the read selects the newer, the older row is retained).

### Batch 3a-i — `AccountingManage` permission seam (authorization only)

**Layer:** Foundation authorization + two existing Application auth copies. **Families:** 0 (no new
mutation; tightens existing auth). **Files:** 4 prod / 2 test.

1. `src/OpHalo.Foundation.Application/Accounts/Authorization/PermissionKeys.cs` — **edit**:
   `public const string AccountingManage = "keep.accounting.manage";` in `Keep`.
2. `src/OpHalo.Foundation.Application/Accounts/Authorization/RolePermissions.cs` — **edit**: add
   `PermissionKeys.Keep.AccountingManage` to `AdminBase` (Owner inherits via composition; Operator
   / Viewer do not hold it) — locked §6.1.
3. `ActualWorkReviewApiService.cs` — **edit**: `AuthorizeAsync` adds an `AccountingManage`
   permission check (alongside the retained explicit Owner/Admin role check, defense-in-depth).
4. `ActualWorkFinancialReadApiService.cs` — **edit**: same auth addition.

Behaviour is unchanged for Owner/Admin today; this only future-proofs a narrower accounting role
without touching the closeout APIs later.

Tests: `tests/OpHalo.UnitTests/Foundation/RolePermissionsTests.cs` (or the existing role-permission
test — Admin/Owner hold `AccountingManage`, Operator/Viewer do not); extend
`ActualWorkReviewApiTests.cs` + `ActualWorkFinancialReadApiTests.cs` auth matrix.

### Batch 3a-ii — Financial-resolution mutation API + read projection fold

**Layer:** Application + API. **Families:** 1 (create financial resolution). **Files:** 8 prod /
3 test.

1. `PriceBook/ActualWorkFinancialResolutionApiService.cs` — new. Auth: copy the (now
   `AccountingManage`-gated) `ActualWorkFinancialReadApiService.AuthorizeAsync`.
   `CreateResolutionAsync(actualWorkId, lineId, command, expectedVisitVersion, ct)`.
2. `IActualWorkFinancialResolutionPersistence.cs` — **edit**: add
   `CreateResolutionAsync(...)` + a result enum
   (`Committed, VisitNotFound, VersionMismatch, VisitNotSubmitted, VisitAlreadyReviewed, LineNotFoundOnVisit, SnapshotComponentAlreadyValid`).
3. `EfActualWorkFinancialResolutionPersistence.cs` — **edit**: transactional mutation. `BeginTransaction`
   → load visit tracked by `(accountId, id)` with `Lines` → guards **in this order**: not found →
   `visit.ConcurrencyVersion != expectedVisitVersion` → `Status != Submitted` →
   **`ReviewedAtUtc != null` → `VisitAlreadyReviewed`** (D5) → line not found on visit → the
   targeted component's snapshot on the line is already non-null → insert resolution row →
   bump `visit.ConcurrencyVersion` (keeps the review card's expected version coherent) →
   `SaveChanges` (catch `DbUpdateConcurrencyException` → `VersionMismatch`) → commit. Returns the
   new visit `ConcurrencyVersion`.
4. `ActualWorkFinancialReadApiService.cs` — **edit** (one file: it holds `ActualWorkFinancialProjection`
   **and** the `ActualWorkFinancialLineEntry` / `ActualWorkFinancialDetailResult` records). Load the
   visit's resolutions, compute the **effective** value per missing component
   (`ResolvedAtUtc DESC, Id DESC`), fold into `ComputeVisitTotals` / `ToLineEntry`; a line is
   complete when snapshot-or-effective-resolution covers **both** components. Apply ADR-467 line
   rounding (D3). Add to the DTOs: per-component `IsResolved` + resolved value + basis, and a
   `Blockers` list of every unresolved line/component.
5. `ActualWorkFinancialResolutionErrors.cs` — **edit**: add the `FinancialResolution*` codes.
6. `Helpers/ErrorHttpMapper.cs` — **edit**: `ActualWork.FinancialResolutionLineNotFound` → 404;
   `…SnapshotComponentAlreadyValid` → 409; `…VisitAlreadyReviewed` → 409;
   `…ValueRequired`/`…ValueNegative`/`…InvalidBasis`/`…ReasonRequired` → 400.
7. `Keep/KeepEndpoints.cs` — **edit**:
   `POST /keep/pricebook/actual-work/{actualWorkId:guid}/lines/{lineId:guid}/financial-resolution`,
   body `(decimal? ResolvedUnitSellPrice, decimal? ResolvedUnitStandardExpectedDirectCost, string Basis, string Reason)`,
   `X-Keep-ActualWork-Version` via `ParseActualWorkVersion`, returns
   `ActualWorkConcurrencyVersionResponse`. Extend `ToActualWorkFinancialDetailResponse` /
   `ToFinancialLineResponse`.
8. `Keep/KeepServiceCollectionExtensions.cs` — **edit**: `AddScoped` for
   `IActualWorkFinancialResolutionPersistence` (deferred from Batch 2) + the new API service.

*8 production files exactly. If preflight of the real diff pushes past 8, split the read-projection
fold (items 4, response mappers in 7) into a 3a-iii slice and land the resolution mutation first.*

Tests: `tests/OpHalo.IntegrationTests/Api/ActualWorkFinancialResolutionApiTests.cs` (new; auth
matrix, each guard incl. `VisitAlreadyReviewed`, version echo, supersession through the API);
extend `ActualWorkFinancialProjectionTests.cs` (rounding + effective-resolution folding);
extend `ActualWorkFinancialReadApiTests.cs` (detail carries resolutions + blockers).

### Batch 3b-i — Zero-line no-charge disposition API + persistence

**Layer:** Application + API. **Families:** 1 (record disposition). **Files:** 6 prod / 2 test.

1. `PriceBook/ActualWorkOfficeFinancialDispositionApiService.cs` — new (a dedicated class, not a
   method on the resolution service — keeps each closeout auth copy small and matches the existing
   one-service-per-Actual-Work-action pattern). Auth: same `AccountingManage`-gated composition.
   `RecordDispositionAsync(actualWorkId, command, expectedVisitVersion, ct)`.
2. `IActualWorkFinancialResolutionPersistence.cs` — **edit**: add `RecordDispositionAsync(...)` +
   result enum values (`Committed, VisitNotFound, VersionMismatch, VisitNotSubmitted, VisitAlreadyReviewed, VisitHasLines`).
3. `EfActualWorkFinancialResolutionPersistence.cs` — **edit**: same transactional shape as 3a-ii —
   guards not-found → version → `Status != Submitted` → `ReviewedAtUtc != null` (`VisitAlreadyReviewed`,
   D5) → **visit has ≥1 line → `VisitHasLines`** (locked §6.2 — `NoCharge` disposition is
   zero-line only) → insert row → bump visit `ConcurrencyVersion` → commit.
4. `ActualWorkFinancialResolutionErrors.cs` — **edit**: `Disposition*` codes.
5. `Helpers/ErrorHttpMapper.cs` — **edit**: `ActualWork.DispositionReasonRequired` → 400;
   `ActualWork.DispositionVisitHasLines` → 409; `ActualWork.DispositionVisitAlreadyReviewed` → 409.
6. `Keep/KeepEndpoints.cs` — **edit**:
   `POST /keep/pricebook/actual-work/{actualWorkId:guid}/financial-disposition`, body
   `(string Kind, string Reason)`, `X-Keep-ActualWork-Version`. DI: one `AddScoped` for the new
   service in `KeepServiceCollectionExtensions.cs` (counts within the 6).

Tests: `tests/OpHalo.IntegrationTests/Api/ActualWorkDispositionApiTests.cs` (new; auth matrix, each
guard, a lined visit rejected, a reviewed zero-line visit rejected); extend
`ActualWorkOfficeFinancialDispositionTests.cs` if a domain branch changed.

### Batch 3b-ii — Hard `MarkReviewed` gate + review transaction/read integration

**Layer:** Domain + Application. **Families:** 1 (modified `MarkReviewed`). **Files:** 6 prod /
2 test.

1. `Entities/ActualWork.cs` — **edit**: `MarkReviewed` gains two precondition inputs supplied by
   the orchestration (it stays pure — loads nothing): `bool financialDataComplete`,
   `bool zeroLineDispositionSatisfied`. New guards ordered **after** `NotSubmitted` /
   `AlreadyReviewed` / note-length and **before** the state write: `!financialDataComplete` →
   `ReviewBlockedIncompleteFinancials`; zero-line && `!zeroLineDispositionSatisfied` →
   `ReviewBlockedZeroLineDispositionRequired`.
2. `IActualWorkReviewPersistence.cs` — **edit**: two new `ActualWorkReviewResult` values
   (`BlockedIncompleteFinancials`, `BlockedZeroLineDisposition`).
3. `ActualWorkReviewApiService.cs` — **edit**: map the two new results to the new errors.
4. `EfActualWorkReviewPersistence.cs` — **edit**: inside the existing transaction, after the
   version check and before `MarkReviewed`, load the visit's `Lines` + effective resolutions +
   dispositions (reuse the Batch 3a-ii projection completeness logic) and compute the two booleans;
   pass them in. **No revision-membership check** (D4).
5. `Errors/ActualWorkErrors.cs` — **edit**: `ReviewBlockedIncompleteFinancials`,
   `ReviewBlockedZeroLineDispositionRequired`.
6. `Helpers/ErrorHttpMapper.cs` — **edit**: both → 409.

Tests: extend `ActualWorkTests.cs` (the two domain gate branches);
extend `ActualWorkReviewApiTests.cs` + `ActualWorkReviewPersistenceTests.cs` (review blocked on an
incomplete line; blocked on an un-disposed zero-line visit; passes once resolved / disposed).

### Batch 4 — Office financial-resolution UI

**Layer:** `web/ophalo-app` only. **Families:** 0. **Files:** ~5 prod / ~4 test.

`src/pages/request-detail/ActualWorkReviewCard.tsx` (resolution inputs + blocker explanation),
`useActualWorkFinancialReview.ts` (resolution + disposition mutations; version handling; 409/403
reconcile per the Slice 2 pattern), new `FinancialResolutionForm.tsx` / `NoChargeDispositionForm.tsx`,
`lib/apiClient.ts` + `lib/apiClient.types.ts`. **Never `ActualWorkComposer.tsx`** (see the UI
boundary callout above). Queue expansion is out of scope. Manual acceptance: incomplete line
resolved → totals appear; zero-line visit → no-charge disposition unblocks review; a reviewed visit
shows resolution controls **disabled**; 403 hides the controls; stale version reconciles.

### Batch 5 — Billing Revision domain foundation

**Layer:** Core only. **Families:** 0. **Files:** 5 prod / 2 test.

1. `Entities/BillingRevision.cs` — aggregate (`BaseEntity`). `AccountId`, `RequestId`, `Status`
   (`BillingRevisionStatus`), `CreatedByAccountUserId`, `ConcurrencyVersion` (app-managed `Guid`
   token, `ValueGeneratedNever` — copy `ActualWork`). Audit: `ReadyAtUtc?`/`ReadyByAccountUserId?`,
   `HandedOffAtUtc?`/`HandedOffByAccountUserId?`/`ExternalBillingReference?` (maxlen 100),
   `VoidedAtUtc?`/`VoidedByAccountUserId?`/`VoidReason?`. **Frozen context snapshot columns** (null
   until `MarkReadyForBilling`): `FrozenAtUtc?`, `CustomerNameSnapshot?`, `ServiceLocationSnapshot?`,
   `RequestReferenceCodeSnapshot?`, `TotalSalesPriceSnapshot?`, `TotalStandardExpectedDirectCostSnapshot?`,
   `TotalMarginSnapshot?`, `IsFinanciallyCompleteSnapshot?`. Owned `Visits`. Lifecycle methods
   return `Result`, bump `ConcurrencyVersion`, fail-closed on the wrong source status:
   `MarkReadyForBilling(confirm, by, atUtc, snapshot)` (Draft→Ready; requires `confirm`; accepts
   the assembled snapshot payload and writes the frozen columns/rows; `NotDraft` otherwise),
   `Void(reason, by, atUtc)` (Draft|Ready→Voided; reason required; `AlreadyHandedOff` if handed
   off), `HandOff(by, atUtc, extRef?)` (Ready→HandedOff; single-shot; `NotReadyForBilling` /
   `AlreadyHandedOff`).
2. `Entities/BillingRevisionVisit.cs` — membership (`BaseEntity`). `AccountId`, `BillingRevisionId`,
   `ActualWorkId`, `AddedAtUtc`, `ReleasedAtUtc?`, `ReleasedReason?`, plus the **frozen per-visit
   snapshot columns** listed in §3 (null until freeze). Released **in place** — row retained.
3. `Entities/BillingRevisionLine.cs` — **new** frozen line snapshot (`BaseEntity`). Fields per §3.
   Created only by the freeze; no update path.
4. `Entities/Enums/BillingRevisionStatus.cs` — `Draft`, `ReadyForBilling`, `HandedOffToBilling`,
   `Voided`. String-converted, `maxLength(50)`.
5. `Errors/BillingRevisionErrors.cs` — `NotFound`, `VersionMismatch`, `NotDraft`,
   `NotReadyForBilling`, `AlreadyHandedOff`, `VoidReasonRequired`, `ReadyRequiresConfirmation`,
   `EmptyVisitSelection`, `VisitNotEligible`, `VisitAlreadyReserved`, `ActiveRevisionExistsForRequest`,
   `ExternalReferenceTooLong`.

Tests: `tests/OpHalo.UnitTests/Keep/BillingRevisionTests.cs` (lifecycle transitions incl.
fail-closed source-status branches; freeze writes the snapshot columns),
`tests/OpHalo.UnitTests/Keep/BillingRevisionVisitTests.cs` (release retains the row).

### Batch 6 — Billing Revision persistence (no DI)

**Layer:** Infrastructure + Foundation migration + Application read seam. **Families:** 0.
**Files:** 5 prod / 1 test (+ migration artifacts by Christian).

1. `Persistence/Configurations/BillingRevisionConfiguration.cs` — table `keep_billing_revisions`.
   `HasAlternateKey(AccountId, Id)`. `ConcurrencyVersion` token `ValueGeneratedNever`. Composite FK
   `(account_id, request_id)` → `keep_requests(account_id, id)` (Restrict). Snapshot money columns
   `HasPrecision(19, 4)`, nullable. **Proof 4 — partial unique index**
   `ux_keep_billing_revisions_active_per_request` on `(account_id, request_id)`
   `HasFilter("status IN ('Draft', 'ReadyForBilling')")` — immutable text-literal predicate against
   the string-converted enum column, same mechanism as `ux_keep_actual_works_open_draft`.
   `HandedOffToBilling` / `Voided` fall outside → unlimited history, exactly one active.
2. `Persistence/Configurations/BillingRevisionVisitConfiguration.cs` — table
   `keep_billing_revision_visits`. `HasAlternateKey(x => new { x.AccountId, x.Id })` =
   `ak_keep_billing_revision_visits_account_id` — the principal key `BillingRevisionLine`'s
   composite FK targets (same pattern as the D2 correction on `ActualWorkLine`). Composite FKs
   `(account_id, billing_revision_id)` → `keep_billing_revisions` (`ClientCascade` — mirrors
   `ActualWorkLine`), `(account_id, actual_work_id)` → `keep_actual_works` (Restrict). Snapshot
   columns nullable. **Proof 3 — partial unique index**
   `ux_keep_billing_revision_visits_unreleased` on `(account_id, actual_work_id)`
   `HasFilter("released_at_utc IS NULL")`. A handed-off revision's memberships are never released →
   permanent reservation; a voided pre-handoff revision sets `released_at_utc` in place → visit
   freed, row retained. Column-level partial unique, **not** a cross-table status join (ADR-493 §6).
3. `Persistence/Configurations/BillingRevisionLineConfiguration.cs` — **new** table
   `keep_billing_revision_lines`. Composite FK `(account_id, billing_revision_visit_id)` →
   `keep_billing_revision_visits(account_id, id)` via the item-2 alternate key (Restrict) — proves
   a line belongs to that exact account's revision-visit. `source_actual_work_line_id` and each
   `*_resolution_id` are plain indexed columns (no FK — historical references that must survive
   even if a source row is later soft-deleted). Money columns `HasPrecision(19, 4)`.
   `ck_…_quantity_positive`.
4. `src/OpHalo.Keep.Application/PriceBook/IBillingRevisionPersistence.cs` — **read seam only**:
   `GetByIdAsync(accountId, id, ct)` (tracked, `Visits` + frozen lines included),
   `GetForRequestAsync(accountId, requestId, ct)`,
   `GetEligibleVisitsForRequestAsync(accountId, requestId, ct)`. `BillingRevisionCommitResult`
   enum defined here (used by 7a–7c).
5. `src/OpHalo.Keep.Infrastructure/Persistence/EfBillingRevisionPersistence.cs` — `account_id`
   filtered reads. **DI deferred to Batch 7a.**

Tests: `tests/OpHalo.IntegrationTests/Persistence/BillingRevisionPersistenceTests.cs` — DB-level
proof of **both** partial-unique invariants: (a) a second active revision for the same request
raises a unique violation; a handed-off + a new Draft both persist; (b) a second unreleased
membership for the same visit raises a unique violation; releasing the first then admits a second;
a handed-off revision's membership blocks a new one permanently. Plus: a `BillingRevisionLine`
naming a `billing_revision_visit_id` from a different account is rejected by the composite FK.

### Batch 7a — Billing Revision Draft assembly + provisional detail read API

**Layer:** Application + API. **Families:** 1 (assemble Draft revision). **Files:** 7 prod /
2 test.

1. `PriceBook/BillingRevisionApiService.cs` — new. Closeout auth composition (same `AccountingManage`-gated
   stack). `CreateDraftAsync(requestId, actualWorkIds[], ct)`, `GetDetailAsync(revisionId, ct)`,
   `GetEligibleVisitsAsync(requestId, ct)`.
2. `IBillingRevisionPersistence.cs` — **edit**: add `AddDraftAsync(...)`.
3. `EfBillingRevisionPersistence.cs` — **edit**: `AddDraftAsync` in a **serializable** transaction
   (mirrors `EfActualWorkPersistence.AddAsync` race handling). For each selected visit re-verify
   transactionally: `Submitted`, `ReviewedAtUtc` set, no financial blocker (reuse the Batch 3a-ii
   projection), no unreleased membership. Insert the revision + one membership per visit; the two
   partial-unique indexes are the race backstop — catch the unique violation and map to
   `ActiveRevisionExistsForRequest` / `VisitAlreadyReserved`. Empty selection → `EmptyVisitSelection`;
   an ineligible visit → `VisitNotEligible`. **No snapshot written at Draft** (§3).
4. `PriceBook/BillingRevisionReadModels.cs` — new. `BillingRevisionDetailResult` (id, status,
   `ConcurrencyVersion`, audit fields, `IsProvisional` flag, request/customer/service-location
   context, `IReadOnlyList<BillingRevisionVisitDetail>` each with line breakdown + rounded totals +
   completeness). Three read shapes per §3: `Draft` → **live provisional** projection; frozen
   revision (`FrozenAtUtc != null`, i.e. `Ready`/`HandedOff`/`Voided-after-ready`) → frozen
   `BillingRevisionVisit` + `BillingRevisionLine` rows; `Draft`-voided (`FrozenAtUtc == null`) →
   void audit + released membership history, no financial section. `BillingRevisionEligibleVisit`.
5. `Helpers/ErrorHttpMapper.cs` — **edit**: `BillingRevision.NotFound` → 404; `VersionMismatch` →
   409; `ActiveRevisionExistsForRequest` → 409; `VisitAlreadyReserved` → 409; `VisitNotEligible` →
   422 (semantically-invalid selection, mirrors `RecorderTransferTargetIneligible`);
   `EmptyVisitSelection` → 400.
6. `Keep/KeepEndpoints.cs` — **edit**: `POST /keep/pricebook/billing-revisions` (body
   `(Guid RequestId, Guid[] ActualWorkIds)`), `GET /keep/pricebook/billing-revisions/{revisionId:guid}`,
   `GET /keep/pricebook/billing-revisions/request/{requestId:guid}/eligible-visits`. New
   `ParseBillingRevisionVersion` helper (header `X-Keep-BillingRevision-Version`, copy
   `ParseActualWorkVersion`).
7. `Keep/KeepServiceCollectionExtensions.cs` — **edit**: DI for `IBillingRevisionPersistence` +
   `BillingRevisionApiService` (deferred from Batch 6).

Tests: `tests/OpHalo.IntegrationTests/Api/BillingRevisionApiTests.cs` (new; auth matrix, assembly
with an ineligible visit rejected, provisional detail read shape, eligible-visits read); extend the
persistence test for the transactional eligibility recheck.

### Batch 7b — Ready for Billing (freeze snapshot) + Void API

**Layer:** Application + API. **Families:** 2 (mark ready; void). **Files:** 6 prod / 2 test.

1. `PriceBook/BillingRevisionApiService.cs` — **edit**: `MarkReadyAsync(revisionId, confirm, expectedVersion, ct)`,
   `VoidAsync(revisionId, reason, expectedVersion, ct)`.
2. `PriceBook/BillingRevisionSnapshotAssembler.cs` — **new** `internal static` pure helper: given
   the loaded revision, its member visits' live lines, effective resolutions (with basis, reason,
   resolver id/time), effective zero-line dispositions, request/customer context, and resolver /
   reviewer / recorder display names, build the frozen `BillingRevision` context columns +
   `BillingRevisionVisit` columns (incl. disposition provenance) + `BillingRevisionLine` rows
   (incl. full per-component resolution provenance), applying ADR-467 rounding. No I/O — the
   persistence method resolves the display names and passes them in.
3. `IBillingRevisionPersistence.cs` — **edit**: add `MarkReadyAsync`, `VoidAsync`.
4. `EfBillingRevisionPersistence.cs` — **edit**: `MarkReadyAsync` — serializable transaction: load
   revision tracked with `Visits` → version check → **re-verify every member visit still eligible**
   (reviewed, no blocker, still the sole reservation) → call `BillingRevisionSnapshotAssembler` →
   `revision.MarkReadyForBilling(confirm, …, snapshot)` (writes frozen columns incl. `FrozenAtUtc`;
   adds `BillingRevisionLine` rows) → `SaveChanges` → commit. `VoidAsync` — load tracked with
   `Visits` → version check → `revision.Void` (Draft **or** Ready; `FrozenAtUtc` is untouched, so a
   Draft void leaves it null and writes no snapshot) → set `released_at_utc` + reason on every
   non-released membership in the same `SaveChanges` → commit. Both catch
   `DbUpdateConcurrencyException` → `VersionMismatch`.
5. `Helpers/ErrorHttpMapper.cs` — **edit**: `BillingRevision.NotDraft` → 409; `NotReadyForBilling`
   → 409; `VoidReasonRequired` → 400; `ReadyRequiresConfirmation` → 400.
6. `Keep/KeepEndpoints.cs` — **edit**: `POST …/billing-revisions/{revisionId:guid}/ready` (body
   `(bool Confirm)`), `POST …/{revisionId:guid}/void` (body `(string Reason)`), both with
   `X-Keep-BillingRevision-Version`.

Tests: `tests/OpHalo.IntegrationTests/Api/BillingRevisionLifecycleApiTests.cs` (new; ready writes
the frozen snapshot columns/rows incl. full resolution + disposition provenance; void-after-ready
releases memberships and retains the frozen rows; **Draft void leaves `FrozenAtUtc` null and the
detail read returns void audit + membership history with no financial section**; wrong-status and
stale-version paths).
`tests/OpHalo.IntegrationTests/Persistence/BillingRevisionFreezeTests.cs` (new; a fixture that
directly rewrites a frozen visit's live `keep_actual_work_lines` / resolution rows after freeze and
asserts the `Ready` detail read is byte-identical; a read-shape assertion that the `Ready` /
`HandedOff` detail query joins only `keep_billing_revision_*` tables — proof 5).

### Batch 7c — Handed Off to Billing API (single-shot)

**Layer:** Application + API. **Families:** 1 (hand off). **Files:** 5 prod / 1 test.

1. `PriceBook/BillingRevisionApiService.cs` — **edit**: `HandOffAsync(revisionId, externalReference?, expectedVersion, ct)`.
2. `IBillingRevisionPersistence.cs` — **edit**: add `HandOffAsync`.
3. `EfBillingRevisionPersistence.cs` — **edit**: `HandOffAsync` — load tracked → version check →
   `revision.HandOff` → save. **Single-shot** (locked §6.3): a second call on an already-handed
   revision → `AlreadyHandedOff` (409); concurrency resolves via the token (one wins, the other
   `VersionMismatch`). `ExternalBillingReference` trimmed, maxlen 100 → `ExternalReferenceTooLong`.
4. `Helpers/ErrorHttpMapper.cs` — **edit**: `BillingRevision.AlreadyHandedOff` → 409;
   `ExternalReferenceTooLong` → 400.
5. `Keep/KeepEndpoints.cs` — **edit**: `POST …/{revisionId:guid}/handoff` (body
   `(string? ExternalBillingReference)`), `X-Keep-BillingRevision-Version`.

Tests: `tests/OpHalo.IntegrationTests/Api/BillingRevisionHandoffApiTests.cs` (new; records
actor/time/ref; second handoff → 409; concurrent handoff — one wins, one `VersionMismatch`).

### Batch 8 — Billing Revision summary UI

**Layer:** `web/ophalo-app` only. **Families:** 0. **Files:** ~5 prod / ~4 test.

Owner/Admin-only, copyable/printable summary of **one selected revision** — never an unreserved
list of all reviewed visits (ADR-493 §6). It reads the frozen snapshot for a frozen revision
(`FrozenAtUtc != null`), the provisional projection for a `Draft` (clearly marked provisional), and
void audit + membership history only for a `Draft`-voided revision (no financial section). New
`BillingRevisionSummary.tsx` + `useBillingRevision.ts` + a print stylesheet + `apiClient` types +
an entry from `ActualWorkReviewCard`. Shows customer, service location, request reference, included
visits/dates/recorders, rounded totals, completeness, and — per ADR-493 §5 — the full frozen
financial-resolution audit per resolved component (basis, reason, resolved-by name, resolved-at)
and, for a zero-line `NoCharge` revision, the frozen disposition audit (kind, reason, actor, time).
All of it comes from the frozen `keep_billing_revision_*` rows for a frozen revision (`FrozenAtUtc != null`).

## 5. The required proofs

| Proof | Mechanism | Batches |
|---|---|---|
| **1. Visit-level zero-line no-charge disposition shape** | `ActualWorkOfficeFinancialDisposition` — immutable append-only record whose FK target is `keep_actual_works(account_id, id)`, not a line. `Kind` enum (`NoCharge`), required non-empty `Reason` (`ck_…_reason_present`), actor + timestamp. Batch 3b-ii treats a zero-line visit as review-eligible only when ≥1 disposition exists; Batch 3b-i rejects a disposition on a lined visit. | 1 / 2 / 3b-i / 3b-ii |
| **2. Effective financial-resolution supersession** | `ActualWorkLineFinancialResolution` — append-only, **no unique constraint**. Read model selects, per missing component, the most-recent row (`resolved_at_utc DESC, id DESC`) that supplies it; older rows retained. A component with no supplying resolution stays a blocker. New rows are rejected once `ReviewedAtUtc != null` (D5). DB proof: two rows same line+component → newer effective, older present. | 1 / 2 / 3a-ii |
| **3. One unreleased Billing Revision membership per visit** | `keep_billing_revision_visits` partial unique index on `(account_id, actual_work_id)` `WHERE released_at_utc IS NULL`. Handed-off memberships stay NULL forever; voiding a pre-handoff revision sets `released_at_utc` in place (row retained). Column-level partial unique, not a cross-table status join. | 5 / 6 |
| **4. One Draft/ReadyForBilling revision per request** | `keep_billing_revisions` partial unique index on `(account_id, request_id)` `WHERE status IN ('Draft', 'ReadyForBilling')`. `HandedOffToBilling`/`Voided` fall outside → unlimited history, exactly one active. Same mechanism as `ux_keep_actual_works_open_draft`. | 5 / 6 |
| **5. Ready revision financial contents are immutable** | `BillingRevision` context columns + `BillingRevisionVisit` columns + `BillingRevisionLine` rows written once, transactionally, at `Draft → ReadyForBilling`, after a re-verification of eligibility. Every read of a frozen revision (`FrozenAtUtc != null` — `Ready`, `HandedOff`, or voided-after-ready) uses those rows; they are never re-joined to live Actual Work / resolution records. (A `Draft`-voided revision, `FrozenAtUtc == null`, has no snapshot and shows only void audit + membership history — §3.) Because new resolutions are blocked after review (D5), the proof is not a user action: a Batch 7b persistence fixture directly mutates a frozen visit's live line/resolution rows post-freeze and asserts the `Ready` detail read is unchanged; plus a read-shape assertion that the `Ready`/`HandedOff` detail query touches only `keep_billing_revision_*` tables. | 5 / 6 / 7b |

## 6. Locked decisions

Locked with Christian's review of rev. 1; no further confirmation needed before Batch 1.

1. **`AccountingManage`.** New key `keep.accounting.manage`, added to `RolePermissions.AdminBase`
   (Owner inherits, Operator/Viewer do not). Batch 3a-i retrofits the existing
   `ActualWorkReviewApiService` and `ActualWorkFinancialReadApiService` auth copies so the entire
   office-financial surface (reads, review mutation, resolution, disposition, all Billing Revision
   transitions) shares one permission seam. The explicit Owner/Admin role check is retained
   alongside it for defense-in-depth, matching the existing pattern.
2. **Disposition scope.** `OfficeFinancialDispositionKind` = `NoCharge` only, for **zero-line
   visits only**, this phase. A lined no-charge / warranty workflow is separate commercial policy,
   not in this sequence. The disposition endpoint rejects a lined visit (`DispositionVisitHasLines`,
   409).
3. **Handoff behaviour.** Single-shot. A duplicate `POST …/handoff` on an already-handed revision
   returns `BillingRevision.AlreadyHandedOff` (409); the UI refetches the revision. Matches
   existing `MarkReviewed` semantics and keeps one clean handoff audit event.

## 7. Batch-gate check

| Batch | Prod files | Total (incl. tests / migration) | New mutation families | Within gate? |
|---|---|---|---|---|
| 1 | 5 | 7 | 0 | yes |
| 2 | 5 | ~9 | 0 | yes |
| 3a-i | 4 | 6 | 0 | yes |
| 3a-ii | 8 | 11 | 1 | yes (exactly 8 — split off a 3a-iii read-projection slice if the real diff grows) |
| 3b-i | 6 | 8 | 1 | yes |
| 3b-ii | 6 | 8 | 1 | yes |
| 4 | ~5 | ~9 | 0 | yes |
| 5 | 5 | 7 | 0 | yes |
| 6 | 5 | ~9 | 0 | yes |
| 7a | 7 | 9 | 1 | yes |
| 7b | 6 | 8 | 2 | yes |
| 7c | 5 | 6 | 1 | yes |
| 8 | ~5 | ~9 | 0 | yes |

## Non-goals (unchanged from ADR-493 / BL129)

CSV generation, QuickBooks/API integration, invoice creation, payments, tax, inventory,
reconciliation, customer acceptance/e-signature, asset identity, and `AdjustmentBillingRevision` /
post-handoff Addendum/Replacement mechanics (BL129 §9, a later preflight — this is also the home
for any post-review correction of financial facts, per D5). Queue expansion of the review card into
a standalone Office Review UI is a separately bounded slice, not part of Batch 4.
