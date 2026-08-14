# Session Log — OpHalo Foundation

**Last updated:** 2026-08-14 (3.4f-1)
**Deployment posture:** Not pilot-ready.
**Source of truth for acceptance criteria:** `docs/pilot-readiness-bug-tracker.md`.

This log records current operational blockers and the active work queue. Historical implementation
evidence belongs in `docs/build-log/`; locked decisions belong in
`docs/pilot-readiness-decision-questions.md` and the decision index.

## Product-Direction Context — Launch Planning, Not Blanket Implementation Authority

The first HVAC-contractor pilot discussion identified an emerging **asset-aware continuity**
direction for Keep: future contractor workflows may link a known equipment asset, QR-based service
intake, quote/approval, and retained work history. The complete discovery record is
`docs/build-log/091-pilot-discussion-contractor-asset-workflow.md`.

This is a staged product-direction decision, not authorization to expand the current implementation
queue. The first contractor now has a Fleetmatics-retirement-driven, mixed public/B2B launch target
in roughly four weeks with one-week contingency. Build 104 controls the launch lanes; Build 105
controls photo evidence; Build 106 controls reliability/release evidence. Do not implement equipment
assets, QR tagging, quotes, accounting/fleet replacement, property-manager subscriptions, sensor
telemetry, or any customer-specific shortcut unless a separately scoped session explicitly promotes
it through these records.

**Price Book, Quotes & Materials — entitlement foundation and operator enrollment path complete;
founder live access verified.** Sessions 1a–1c (ADR-462) delivered the
`AccountCapabilityPackageEnrollment` entity/persistence, the account-aware
`AccountFeatureAccessResolver`, and a generic Owner/Admin `GET /accounts/me/capability-packages`
status read. The founder's live account is now enrolled for
`keep.price_book_quotes_materials` and production Price Book access is confirmed. Do not add dummy
catalog/assembly data to that account: run full mutable acceptance locally against a disposable
database, and use production only for entitlement, navigation, and real-business-data checks.

**Session 2 progress:**

- **2e — Catalog workspace UI: complete (2026-08-09).** Build 112 locked the product/UI boundary;
  Build 113 supplied the bounded implementation and completion-verification sequence. Image storage
  is paused; do not reopen 2e for image work, an empty Offerings & Packages tab, or deferred catalog
  refinements.

- **Session 3.0 — Continuation preflight and reconciliation: complete (2026-08-09, commit
  `3ef30d9`).** ADR-478 (quote direct-cost snapshots and Owner/Admin margin visibility) and ADR-479
  (live `OfferingAssembly` lifecycle and computed operational eligibility) are locked; ADR-477
  (field-presentable pricing) moved to Deferred pending pilot evidence (DEF-093). Full reconciliation
  record and sequencing in [Build Log 117](build-log/117-price-book-continuation-coding-plan.md).

- **Internal capability-package enrollment operator path: complete (2026-08-13, commit
  `6ea62a5`).** `InternalCapabilityPackageEnrollmentService`/`InternalCapabilityPackageEnrollmentApiService`/
  `InternalEntitlementsEndpoints` deliver `GET`/`POST .../enroll`/`.../disable`/`.../reenable` under
  `/internal/accounts/{accountId}/capability-packages/{featureKey}`, gated on
  `internal.entitlements.manage` (authenticated → caller's own Internal-purpose account + role →
  target account exists; target's commercial state is deliberately not consulted — this is an
  operator tool, not a request/service-delivery action). Enroll only ever creates the first row for
  an (AccountId, FeatureKey) pair; Disable/Reenable require the caller's expected concurrency token
  and never force-transition. The two real database races (concurrent Enroll on the same pair; a
  stale Disable/Reenable commit) are translated at the persistence seam
  (`AccountCapabilityPackageEnrollmentCommitResult`) into 409s (`EnrollmentAlreadyExists` /
  `VersionMismatch`) instead of leaking as 500s. 42 unit tests, 21 integration tests (including 2
  real-PostgreSQL persistence-race tests), 14/14 architecture tests, `git diff --check` clean.
  Founder live enrollment and Price Book access were verified on 2026-08-13. The temporary
  Railway-query bootstrap procedure and the future internal-operator API procedure are recorded in
  `docs/runbook/capability-package-enrollment.md`.

- **Session 3.4 preflight: locked (2026-08-13), implementation not started.** Technician
  proposed-scope field capture (price-blind, web/PWA). Full preflight, locked decisions, retired
  endpoint, assembly-expansion locking protocol, and the 3.4a–3.4g session map are in
  [Build Log 118](build-log/118-proposed-scope-field-capture-preflight.md). Headline decisions:
  a new by-request `ProposedScope` read doubles as the client's entitlement probe (no new
  `AvailableActionsMetadata` flag); two new server-authoritative endpoints
  (`field-select`, `expand-assembly`) replace client-supplied catalog/assembly snapshots, and raw
  `POST .../lines` is retired from the technician-reachable surface rather than re-gated; assembly
  expansion is atomic with an explicit row-locking protocol (scope → assembly → catalog items,
  ascending id order, eligibility re-checked under lock); display order is always server-computed
  (`max+10`); off-catalog text is trimmed/control-char-rejected/200-char-truncated server-side,
  Unicode preserved; scope display shows the open Draft or, if none, only the single most recent
  submitted/reviewed scope — no history (that's Session 3.5).

- **3.4a — `ProposedScope` read API: complete (2026-08-14, commit `ced8b6b`).**
  `ProposedScopeReadApiService` delivers `GET .../proposed-scopes/by-request/{requestId}` (200,
  `{state, scope}` — `state` is `"NoScopeYet"` with `scope: null`, or the scope's own `Status`; the
  open `Draft` takes precedence over any older `SubmittedToOffice`/`OfficeReviewed` row, otherwise
  the single most recent one by `CreatedAtUtc` — no history) and `GET .../proposed-scopes/{id}`
  (404 folds together missing/cross-account scope and MyWork-invisible request, never a 403 that
  would confirm the row exists). Read-only account-access gate (Blocked-only denies; `ReadOnly`
  e.g. OffSeason may still read) — this doubles as the capture entry point's entitlement probe per
  build-log/118 decision 1, no `AvailableActionsMetadata` flag added. Response lines are ordered by
  `DisplayOrder` then `Id`; `GetCurrentForRequestAsync`'s most-recent-only tiebreak is
  `CreatedAtUtc` then `Id` descending for determinism. 5 production files, zero mutation families,
  6 total changed files. 9 new integration tests (NoScopeYet state tag, Draft precedence,
  most-recent-only, entitlement 403, MyWork 404 both endpoints, unknown-id 404, display-order
  round-trip with a wire-level price-key-absence sweep, Operator-role regression 403 against the
  existing Admin-gated catalog/assembly reads) — 41/41 `ProposedScope*` integration tests, 14/14
  architecture tests, `git diff --check` clean.

- **3.4b — Field-safe catalog read API: complete (2026-08-14).** New
  `FieldCatalogReadApiService` delivers `GET /keep/pricebook/field/catalog-items{,/{id}}` and
  `/field/catalog-categories` beside the existing Admin-gated `CatalogReadApiService`/
  `PriceBookEndpoints` surface, not inside a loosened version of it. Gate 3 is `RequestsOperate`
  AND `ScopeCapture` (ADR-480), not `PriceBookCatalogManage`; gate 1/2 (Blocked-only account
  access, then Price Book entitlement) match `CatalogReadApiService` exactly — catalog data is
  account-wide, so there is no row-visibility step. Reads are forced to `IsCommonItem = true` and
  `ActiveState.Active`; `ICatalogReadPersistence.CatalogItemListFilters` gained an optional
  `IsCommonItem` filter (shared EF implementation, additive for the existing Admin path), and the
  keyset-cursor fingerprint now includes it so a cursor from one query shape can't validate against
  the other. Every field response type (`FieldCatalogItemResponse`, etc.) structurally omits
  price/margin fields — a leak would be a compile error, not a runtime discipline. 5 production
  files, zero mutation families, 6 total changed files. 10 new integration tests (`IsCommonItem`/
  Active-only scope, price-free wire sweep on list and detail, non-common-item detail 404, Gate 1
  Blocked-account 403, Gate 3 Viewer-role 403 — Operator holds `RequestsOperate` and `ScopeCapture`
  together, so Viewer, which holds neither, is the reachable proof the check denies — Operator-role
  regression 403 against the existing Admin-gated catalog endpoints) — 14/14 architecture tests,
  `git diff --check` clean.

- **3.4c — Field-safe assembly read API: complete (2026-08-14).** New
  `FieldOfferingAssemblyReadApiService` delivers `GET /keep/pricebook/field/offering-assemblies{,/{id}}`
  beside the existing Admin-gated `OfferingAssemblyReadApiService`/`OfferingAssemblyEndpoints`
  surface. Gate 3 is `RequestsOperate` AND `ScopeCapture`, matching 3.4b; reads are scoped to
  `ActiveState.Active` and `OfferingAssemblyListRow.IsOperationallyEligible` (ADR-479). Two
  pagination-correctness points, called out explicitly because build-log/118 had already flagged
  this bug class twice: (1) `IsOperationallyEligible` is computed in-memory after the SQL page
  fetch (joined catalog-item price lookups), so `ListAsync` reuses
  `IOfferingAssemblyPersistence.ListAsync` unmodified and computes `HasMore`/`NextCursor` from the
  raw fetched page *before* filtering to eligible rows for the returned `Items` — a page can be
  sparse or empty with `HasMore: true` rather than ever skipping an eligible row further down the
  sequence; (2) an Admin `?status=Active` cursor and this list's cursor would otherwise carry an
  identical fingerprint (same raw filter shape), so `OfferingAssemblyListCursor.ComputeFingerprint`
  gained a `fieldOperationallyEligible` discriminator parameter (default `false`, Admin call site
  unchanged) making the two surfaces' cursors mutually rejected. Field DTOs omit `PriceTreatment`
  and the whole pricing summary; detail 404s for Inactive/ineligible assemblies. 5 production
  files, zero mutation families, 6 total changed files. 12 new integration tests (eligible/
  Active-only list scope, price-free wire sweep on list and detail, ineligible/Inactive detail
  404, sparse-page cursor walk proving `HasMore: true` with an empty page still reaches every
  eligible assembly, cross-surface cursor rejection both directions, Gate 1 Blocked-account 403,
  Gate 3 Viewer-role 403, Operator-role regression 403 against both the existing Admin-gated list
  and detail endpoints) — 112/112 across the full offering-assembly/catalog/proposed-scope
  regression set, 14/14 architecture tests, `git diff --check` clean.

- **3.4d — Server-authoritative `field-select` + retirement of raw `AddLine`: complete
  (2026-08-14).** New `FieldProposedScopeSelectionApiService`/`FieldSelectProposedScopeLineApiCommand`
  deliver `POST /keep/pricebook/proposed-scopes/{id}/field-select` (`KnownCatalogItem` or
  `OffCatalogItem` only); the raw, caller-trusted `POST .../{id}/lines` and
  `ProposedScopeApiService.AddLineAsync`/`AddProposedScopeLineApiCommand` are removed in the same
  commit, not re-gated. Gate composition restates `ProposedScopeApiService`'s three-gate mutation
  stack exactly (not inherited by reference, per build-log/118); row visibility
  (`EditProposedScopeService.VerifyRequestVisibleAsync`) is checked before any catalog-item
  resolution, so an invisible scope 404s before a referenced item's existence/active state is ever
  evaluated. `KnownCatalogItem` resolves the account-owned, Active `CatalogItem` server-side
  (`ICatalogReadPersistence.GetItemDetailAsync`) and builds `DisplayNameSnapshot`/
  `UnitOfMeasureSnapshot` itself; an unknown/cross-account/inactive id folds into one
  `ProposedScope.LineCatalogItemNotFound` 404. `OffCatalogDescription` is stored full/unchanged (up
  to the existing 500-char limit); only `DisplayNameSnapshot` is server-derived from it (trim → reject
  any C0/C1 control character → truncate to 200 chars). New `EditProposedScopeService.
  AppendFieldLineAsync`/`AppendProposedScopeLineCommand` computes `MAX(DisplayOrder)+10` (10 if none)
  from the same loaded/tracked scope inside the visibility/version-checked load, before commit — the
  field command never accepts a client-supplied `DisplayOrder`. 6 production files (1 new), 1
  mutation family, 8 total changed files. 8 new/repointed integration tests (server-resolved
  snapshot + computed display-order round-trip, off-catalog full-description preservation with
  truncated-snapshot derivation, unknown-catalog-item 404, control-character rejection, disallowed
  `LineType` 400, gates → visibility → act ordering for an invisible scope, missing-version-header
  400, retired-route 404 pinning the no-reachable-window requirement) — 18/18 `ProposedScopeApiTests`,
  154/154 across the full `ProposedScope`/`CatalogItem`/`OfferingAssembly` regression set, 14/14
  architecture tests, `git diff --check` clean.

- **3.4e — Atomic `expand-assembly`: complete (2026-08-14).** New
  `FieldExpandAssemblyApiService`/`IOfferingAssemblyExpansionPersistence`/
  `EfOfferingAssemblyExpansionPersistence` deliver `POST /keep/pricebook/proposed-scopes/{id}/
  expand-assembly`, the sole path for `PrimaryOffering`/`AssociatedItem` lines (build-log/118
  "Assembly-expansion locking protocol"). One atomic transaction, corrected during preflight to a
  dedicated persistence seam (not composed locks) so lock/recheck/append/commit share one
  `DbContext`: (1) `SELECT ... FOR UPDATE` locks the `ProposedScope` row, version/status-checked;
  (2) `SELECT ... FOR UPDATE` locks the `OfferingAssembly` row, then every referenced `CatalogItem`
  (primary + associated items) in ascending id order; (3) ADR-479 eligibility is recomputed from
  those locked rows (reuses `IOfferingAssemblyPersistence.IsOperationallyEligibleAsync` against the
  same scoped `DbContext`, so it reads the just-locked state, not a pre-transaction snapshot); (4)
  every submitted exclusion id is validated as a current *optional* associated-item id — unknown or
  required-item ids reject as `ProposedScope.ExpandExclusionItemInvalid` with zero lines written;
  (5) only then are `PrimaryOffering` + non-excluded `AssociatedItem` lines appended at
  `MAX(DisplayOrder)+10, +20, ...` and the scope's version bumped once. Ineligible-at-recheck rejects
  as `ProposedScope.ExpandAssemblyNotOperationallyEligible` (409), always with zero writes.
  `EditProposedScopeService.ExpandAssemblyAsync` is a thin passthrough mapping the seam's outcome
  enum, matching `SubmitProposedScopeService`'s relationship to
  `IProposedScopeSubmissionPersistence`. Gate composition/row-visibility ordering restates
  `FieldProposedScopeSelectionApiService`'s exactly. 8 production files (3 new), 1 mutation family,
  10 total changed files. 14 new integration tests: 8 persistence-level (happy path, display-order
  continuation, optional-item exclusion, unknown/required-item invalid exclusion, ineligible-at-lock
  rejection, stale-version conflict, and the two-transaction race proof — a
  `PostScopeLockHook` test seam pauses the transaction right after the scope lock to deactivate the
  primary item on a second connection before the assembly/catalog-item locks are taken, proving the
  recheck reads that just-committed change) plus 6 API-level (happy path, unknown-assembly 404,
  ineligible-assembly 409, no-entitlement 403, Viewer-role 403, gates → visibility → act ordering for
  an invisible scope, missing-version-header 400) — 25/25 `ProposedScopeApiTests`, 8/8 new
  `OfferingAssemblyExpansionPersistenceTests`, 168/169 across the full `ProposedScope`/`CatalogItem`/
  `OfferingAssembly` regression set (1 unrelated pre-existing flake, confirmed passing in isolation),
  14/14 architecture tests, `git diff --check` clean. Next: 3.4f (frontend entry point + ladder
  selection) on explicit go-ahead.

- **3.4f preflight (2026-08-14): split into 3.4f-1/3.4f-2, locked.** Backend contract (3.4a–3.4e)
  confirmed stable; no frontend Price Book client code exists yet (`apiClient.ts`/`.types.ts` have no
  `proposed-scope` references). Preflight confirmed `RequestDetailContent.tsx` mounts both
  `RequestDetailDesktopLayout` and the mobile actions/context blocks simultaneously (CSS-toggled via
  `md:hidden`/`hidden md:flex`, not conditional mounts), so probe/draft/modal state must be hoisted
  into one hook shared by both, not owned per-layout. Selection/expansion endpoints return only
  `{id, status, version}` (no lines), so the ladder must re-fetch `GET .../proposed-scopes/{id}` after
  every commit rather than optimistically append. ADR-461's five-rung ladder is fixed order
  (Primary Offering → Common Items → Categories → Search → Off-Catalog), progressive with an explicit
  "not here" advance, never free-jump tabs or AI/fuzzy matched. The originally proposed single-batch
  file list (13 touched files) exceeded the hard batch-size gate (8 production / 12 total), so 3.4f is
  split:
  - **3.4f-1 — Entry point + draft-lifecycle wiring: complete (2026-08-14).** `apiClient.ts`/
    `.types.ts` gained `getCurrentProposedScopeForRequest`/`getProposedScope`/`createProposedScope`
    (field-select/expand-assembly/field-catalog/field-assembly client methods deferred to 3.4f-2,
    where the rungs that call them are built). `useProposedScopeCapture.ts` is hoisted once in
    `RequestDetailContent.tsx` and passed into both `RequestDetailDesktopLayout` and
    `RequestDetailMobileActions` — confirmed those two trees mount simultaneously (CSS-toggled via
    `md:hidden`/`hidden md:flex`, not conditional), so a per-layout hook would have double-probed.
    States: `hidden` (403, renders nothing), `no-scope` ("Capture proposed scope" CTA → creates
    draft, opens modal), `draft` ("Resume proposed scope" CTA → opens modal, no create call),
    `submitted` (`SubmittedToOffice`/`OfficeReviewed` → read-only status + "Capture new proposed
    scope"). `refetchScope()` is exposed for 3.4f-2 to call after every ladder mutation/409/timeout.
    `ProposedScopeCard.tsx` renders in both action stacks, near `WorkDoneCard`, before
    `CloseRequestCard`. `ProposedScopeCaptureModal.tsx` is a stub (lists existing lines, no ladder) —
    3.4f-2 fills it in. 8 production files (3 new), 1 test file, 9 total changed files (+session-log).
    7 new `useProposedScopeCapture` tests (403 hide, no-scope, draft-resume, submitted, create-draft-
    then-open, resume-without-create, refetch-replaces-state) — 394/394 full frontend suite, `tsc
    --noEmit` clean, `git diff --check` clean. Next: 3.4f-2 (`ProposedScopeCaptureModal` + the 5 rung
    components) on explicit go-ahead.
  - **3.4f-2 (not started):** `ProposedScopeCaptureModal.tsx` + the 5 rung components
    (`PrimaryOfferingRung`, `CommonItemsRung`, `CategorySearchRung`, `GlobalSearchRung`,
    `OffCatalogRung`), filling in the modal 3.4f-1 stubbed. Immediate-commit-per-pick, re-fetch on
    success, narrow 409/timeout reconciliation (re-fetch + non-blocking "scope refreshed" notice, no
    auto-retry). Line edit/remove, submit, and full recovery UI stay out of scope — deferred to 3.4g.

- **3.1 — Offering/Assembly domain foundation: complete (2026-08-10, commit `6f7047e`).**
  `OfferingAssembly`/`OfferingAssemblyItem` entities, persistence, EF configuration, and the
  ADR-479 computed-eligibility read are migrated (`20260810075949_OfferingAssembly`). 22 domain
  unit tests, 12 integration tests against real PostgreSQL, 14/14 architecture tests, `git diff
  --check` clean. No API/service layer or technician workflow yet — that was 3.2.

- **3.2a.1 — Offering/Assembly create/activate/inactivate API: complete (2026-08-10, commit
  `3d67c1e`).** `OfferingAssemblyLifecycleService`/`OfferingAssemblyApiService`/
  `OfferingAssemblyEndpoints` deliver atomic `POST .../create-with-items` (one aggregate build,
  one `AddAsync`; existence-checks referenced catalog items; no eligibility check at create time
  per ADR-479) and `PATCH .../activate` / `.../inactivate` behind the strict
  `X-Keep-OfferingAssembly-Version` header contract (dedicated parser + test file, matching
  `CatalogItemVersionHeader`). Same ADR-462 mutation gate as `CatalogItemApiService`. 8 production
  files (at the batch-size cap), 1 mutation family, 11 total changed files. 9 unit + 29
  integration (7 header + 10 API + 12 existing 3.1 eligibility) + 14/14 architecture tests, `git
  diff --check` clean.

- **3.2a.2 — Offering/Assembly bounded reads: complete (2026-08-10, commit `165cc3f`).**
  `OfferingAssemblyReadApiService` delivers cursor-paged `GET .../offering-assemblies` (signed,
  status-bound fingerprint, name-then-id order, batched `isOperationallyEligible` per row — one
  projection query per page, never per row) and `GET .../offering-assemblies/{id}` (full item
  lines plus `eligibilityReasons`: `AssemblyInactive` short-circuits to the sole reason;
  otherwise `PrimaryItemInactive`/`PrimaryItemMissingStandalonePrice` then per-component
  `ComponentInactive`/`ComponentMissingStandalonePrice` — the latter only under `Summed`, never
  `AllInclusive`). `IsOperationallyEligibleAsync` (3.1, locked-tested) is untouched; the new
  `GetEligibilityAsync` is additive. Same read-only ADR-462 gate as `CatalogReadApiService`
  (Blocked-only denial). 6 production files, zero mutation families, 8 total changed files. 42/42
  focused OfferingAssembly integration tests (13 read incl. cursor status-fingerprint binding,
  no-status-param-returns-all default, and the full eligibility-reason taxonomy), 31 unit, 14/14
  architecture tests, `git diff --check` clean.

  3.2a is now fully complete (create, activate/inactivate, list, detail — no live editing yet).

- **3.2b — Offering/Assembly live editing: complete (2026-08-10).** Adds `PATCH
  .../offering-assemblies/{id}` (header/primary/price-treatment update, existence-checks the new
  primary), `POST .../items` (add — returns `{itemId, concurrencyVersion}` so a client can chain
  the next sequential edit without a detail read), `PATCH .../items/{itemId}` (update — also how a
  reorder step is expressed; no bulk reorder endpoint), and `DELETE .../items/{itemId}` (remove),
  all behind the existing `X-Keep-OfferingAssembly-Version` contract. Conflict recovery is
  client-side only (409 → re-fetch `GET .../{id}` → retry) — no dedicated repair endpoint or
  custom 409 diff payload.

  Fixed two real defects surfaced during this batch, not anticipated in the preflight: (1)
  `OfferingAssemblyCommitResult` collapsed a stale-version race and the ADR-466 active-primary-
  item collision into one generic `Conflict`, so reactivating an assembly or re-pointing its
  primary into another assembly's claimed primary was misreported as `VersionMismatch` — split
  into `Committed` / `ConcurrencyConflict` / `PrimaryCatalogItemAlreadyClaimed`, proven against
  real Postgres. (2) `OfferingAssemblyItem`'s FK to its parent was `DeleteBehavior.Restrict`,
  which never mattered until `RemoveItem` — the first genuine removal path for that owned
  collection — made it unreachable (EF tried to null a required FK instead of deleting the
  orphan); changed to `ClientCascade`
  (migration `20260810104220_OfferingAssemblyItemClientCascadeDelete`: a real FK-action change,
  `RESTRICT` to Postgres's default `NO ACTION` — both remain non-cascading at the database level;
  `ClientCascade` only changes EF's own change-tracker behavior for the in-app removal path).

  7 production files (existing 3.2a files plus the FK config) + 2 migration files, 1 mutation
  family, comfortably within the batch-size cap. 19 unit + 33 focused API/persistence integration
  + 14/14 architecture tests (independently re-run and confirmed), `git diff --check` clean.

  3.2's API/persistence layer (create, activate/inactivate, list, detail with eligibility reasons,
  live header/item editing) is complete. **Correction (2026-08-10):** build-log/117 defines 3.2 as
  "Authorized Owner/Admin API and workbench surface" — the Owner/Admin workbench UI was never
  built; `web/ophalo-app` has no route, API-client wrapper, list, detail, or editor for
  offerings/assemblies (only catalog items). Session-log previously mismarked 3.2 "fully complete"
  without checking the promised UI against the frontend; this entry corrects that. The workbench UI
  is tracked as **3.2c** and is a hard dependency for 3.4 (per build-log/117's own `3.2, 3.3 → 3.4`
  dependency line) — do not start 3.4 until 3.2c is complete.

  **3.2c/3.2d preflight locked (2026-08-10), no code yet.** Nav: `PriceBook.tsx` gains tabs
  ("Catalog Items" / "Offerings & Assemblies") — one Owner/Admin price-book workspace, not a
  separate top-level route. Build-log/112's deferred usage-count/link and the quote-block
  behavior stay out of scope; the catalog-item-inactivation hazard (build-log/112: "inactivation
  must warn about active offering usage") is in scope but split by the file-count gate:
  - **3.2c — workbench UI.** New `OfferingAssemblyDrawer.tsx` (create), `OfferingAssemblyDetail.tsx`
    (view/edit/activate/inactivate/items), `CatalogItemPicker.tsx` (shared primary/associated-item
    search, reused by both); modified `PriceBook.tsx` (tabs), `apiClient.ts`/`apiClient.types.ts`
    (assembly CRUD calls), `App.tsx` (assembly detail route). Wires the existing 3.2a/3.2b API only
    — no backend changes. 8 production files.
  - **3.2d — catalog-item inactivation dependency check, immediately next.** New account-scoped,
    Owner/Admin-gated, active-assemblies-only read covering both primary and associated-item
    references (`IOfferingAssemblyPersistence.ListActiveAssembliesReferencingCatalogItemAsync`,
    `EfOfferingAssemblyPersistence`, `OfferingAssemblyReadApiService`, new
    `GET /keep/pricebook/catalog-items/{catalogItemId}/active-assembly-dependencies` on
    `OfferingAssemblyEndpoints.cs`) plus `CatalogItemDetail.tsx`'s pre-inactivation confirmation
    (not `CatalogItemDrawer.tsx`, which is create-only) naming the consequence — those assemblies
    become unavailable for new selection — before the existing inactivate call. Purpose-built to
    the inactivation path only; not a general usage-count/link feature.
  - **Guardrail: 3.2c and 3.2d are one functional delivery.** Do not treat 3.2c as released,
    manually accepted, or pilot-used on its own — no deployment between the two sessions.

  **3.2c — workbench UI: complete (2026-08-10), not released.** New `OfferingAssemblyDrawer.tsx`
  (create), `OfferingAssemblyDetail.tsx` (view/edit/activate/inactivate/items, eligibility-reasons
  display), `CatalogItemPicker.tsx` (shared server-search picker, reused by both — the catalog is
  bounded/cursor-paged so it searches on typed input rather than holding it in memory, unlike
  `CategoryCombobox`'s in-memory list). `PriceBook.tsx` gains the locked "Catalog Items" /
  "Offerings & Assemblies" tabs; `App.tsx` gains the `pricebook-assembly` route
  (`#/pricebook/assembly/:id`, checked before the item-detail route since its own pattern is a
  superset). `apiClient.ts`/`apiClient.types.ts` gain the full assembly CRUD surface. Wires only
  the existing 3.2a/3.2b API — no backend changes in this slice.

  Independent review of the first pass found four real gaps, all fixed before this entry: (1) the
  assemblies list queried `status: "Active"` only, so an inactivated assembly had no UI path back
  to reactivation — added an Active/Inactive status filter mirroring the catalog-items toggle;
  (2) header/item mutations in `OfferingAssemblyDetail.tsx` invalidated only the detail query, so
  the list could show a stale name/primary/price-treatment/eligibility after an edit — folded list
  invalidation into the shared `invalidateDetail()` used by every mutation; (3) the creation
  drawer's per-row associated-item exclusion list only ever contained the primary item, so a user
  could pick the same component twice and hit a raw backend error — replaced the static exclusion
  list with a per-row `excludeIdsForRow()` that also excludes every other row's current selection;
  (4) the only test change was a required-prop fixup, no real coverage of the new surface — added
  three focused suites: `PriceBook.assemblies.test.tsx` (tab isolation, list render, row
  navigation, eligibility badge, the Active/Inactive filter reaching an inactivated assembly,
  filter-scoped empty states), `OfferingAssemblyDrawer.test.tsx` (primary-exclusion, the
  duplicate-prevention regression case, submit payload shape, required-field validation),
  `OfferingAssemblyDetail.test.tsx` (render, eligibility-reasons display, header edit invalidates
  both detail and list queries, version-conflict recovery, activate/inactivate incl. the inline
  confirm gate, item removal invalidates the list).

  A second review pass found two more real gaps, both fixed: (5) the assemblies list never
  requested a second page — no cursor/page state and no Previous/Next controls, so an account with
  more than a page of active or inactive assemblies had no way to reach the rest — added
  independent per-status cursor/page state (`assemblyPagination`, keyed by `Active`/`Inactive`, so
  switching the status filter doesn't lose the other status's page position, unlike the
  catalog-items list which resets pagination on any filter change) plus Prev/Next controls mirroring
  the catalog list's; (6) the desktop assembly rows were bare clickable `<tr>` elements, unreachable
  by keyboard — replaced with the catalog table's own pattern, a `<button>` around the name cell.
  Two new regression tests cover both (`PriceBook.assemblies.test.tsx`: cross-status page-position
  independence via Prev/Next, and keyboard Enter-to-navigate on the name button).

  7 production files + 4 test files. `tsc --noEmit` clean; full frontend suite 355/355 (39 files,
  18 new), no regressions; `git diff --check` clean. **Not released per the guardrail above —
  3.2d (catalog-item inactivation dependency check) is the immediate next session, no deployment
  before it lands.**

- **3.2d — catalog-item inactivation dependency check: complete (2026-08-10).** New
  `IOfferingAssemblyPersistence.ListActiveAssembliesReferencingCatalogItemAsync` (EF: account-scoped,
  `Active`-only, matches both `PrimaryCatalogItemId` and associated `Items[].CatalogItemId`),
  `OfferingAssemblyReadApiService.GetActiveAssemblyDependenciesAsync` (reuses the existing Owner/
  Admin `AuthorizeAsync` gate, no new gate logic), and
  `GET /keep/pricebook/catalog-items/{catalogItemId}/active-assembly-dependencies` on
  `OfferingAssemblyEndpoints.cs`, returning `{ count, assemblies: [{ id, name }] }`.
  `CatalogItemDetail.tsx`'s inline pre-inactivation confirmation now names the affected active
  assemblies and states they become unavailable for new selection; `Confirm inactivate` stays
  disabled while the dependency read is loading, refetching (including a reopened confirmation
  serving stale cached data in the background — gated on `isFetching`, not `isLoading`, to close a
  fail-closed gap caught in review), or has failed, so a failed read can never allow a blind
  inactivation.

  7 production files (`IOfferingAssemblyPersistence.cs`, `EfOfferingAssemblyPersistence.cs`,
  `OfferingAssemblyReadApiService.cs`, `OfferingAssemblyEndpoints.cs`, `apiClient.ts`,
  `apiClient.types.ts`, `CatalogItemDetail.tsx`) + 3 test files. `dotnet build` and `tsc --noEmit`
  clean; `git diff --check` clean. Backend: 18/18 focused read-suite integration tests (5 new,
  covering active-only filtering, primary + associated-item references, account scoping, the
  Owner/Admin gate, and the `count` field), 41/41 unit, 14/14 architecture. Frontend: 359/359 (39
  files), including a regression test proving the mutation cannot fire during a background refetch
  of stale cached dependency data.

  **Guardrail closed: 3.2c and 3.2d together are the complete Offering/Assembly office-management
  delivery.** Ready for Christian's normal manual acceptance; this record does not itself authorize
  deployment.

- **3.3 pre-work — two authority/snapshot decisions locked (2026-08-10), no code yet.** ADR-480:
  new `keep.pricebook.scope.capture` permission in `RolePermissions.OperatorBase` (Admin/Owner
  hold it automatically via the existing role composition); every `ProposedScope` mutation
  requires three independent gates — `RequestsOperate`, Price Book entitlement (ADR-462), and the
  new capture permission — not one combined key. ADR-481: extends ADR-479 down to
  `ProposedScopeLine` — its snapshot fields and initial `Quantity` are captured once at line
  creation and never live-recomputed from the catalog/assembly on a later Draft read/edit,
  correcting build-log/108's "recomputed live" ERD text, which predates and conflicts with
  ADR-479. Full reconciliation record in [Build Log 117](build-log/117-price-book-continuation-coding-plan.md).

- **3.3a.1 — ProposedScope/ProposedScopeLine domain/schema: complete (2026-08-10).**
  `ProposedScope` (`Draft`/`SubmittedToOffice`/`OfficeReviewed`, one open `Draft` per request via a
  partial unique index on `RequestId`, composite FK to `KeepRequest(AccountId, Id)` — account-safe
  at the database level, no post-load tenant check) and `ProposedScopeLine`
  (`PrimaryOffering`/`AssociatedItem`/`KnownCatalogItem`/`OffCatalogItem`, ADR-481 snapshot fields
  captured once at line creation) plus their EF configuration and migration
  (`20260810144838_ProposedScopeAndLine`). `ProposedScopeLine`'s parent FK uses `ClientCascade`
  from the start — applying the 3.2b `OfferingAssemblyItem` lesson upfront rather than
  rediscovering it. `Submit()` is a pure status transition only, no `KeepRequestWorkSignal` side
  effect — that coordination is a separate atomic persistence concern, Session 3.3a.2. No
  persistence interface, no API, and no terminal-request precondition yet — all deferred to
  3.3a.2/3.3b per explicit correction during preflight.

  Three line-level data invariants corrected during implementation, not independently ADR'd (same
  granularity as `OfferingAssembly`'s own item-level rules): (1) `Quantity`/`OffCatalogQuantity`
  are the same logical value for an off-catalog line, never independently caller-managed — required
  equal at creation, kept in sync by `Update`; (2) `UnitOfMeasureSnapshot` is required for every
  catalog-referencing line type and empty for `OffCatalogItem`; (3) `IsException`/
  `DefaultQuantitySnapshot` are `AssociatedItem`-only — `PrimaryOffering` (the original design had
  wrongly grouped it with `AssociatedItem`) gets neither.

  7 production files, 2 migration files, 1 test file. 36 unit tests, full unit suite 1456/1456,
  14/14 architecture tests, 53/53 focused integration tests (proving the migration applies cleanly
  against real PostgreSQL with no pending-model-changes warning), `git diff --check` clean.

- **3.3a.2 — KeepRequestWorkSignal foundation and atomic submit/signal: complete (2026-08-10).**
  `KeepRequestWorkSignal` (ADR-463, Core-owned entity + `SourceModuleKey`/`SignalKey` registry —
  no public mutation method yet, since its only writer this batch is a native upsert, not tracked
  mutation) plus `IProposedScopePersistence`/`EfProposedScopePersistence` (ordinary create/edit,
  mirrors `IOfferingAssemblyPersistence`'s shape) and the dedicated
  `IProposedScopeSubmissionPersistence`/`EfProposedScopeSubmissionPersistence` owning the entire
  submit transaction directly against `OpHaloDbContext` — never an Application-layer
  `IDbContextTransaction` across two ordinary adapters, matching
  `EfCatalogItemCreateAndActivatePersistence`'s pattern. `SubmitProposedScopeService` stays thin:
  no auth (ADR-480's three-gate wiring is 3.3b), no transaction, just maps the persistence
  outcome. Migration `20260810153619_KeepRequestWorkSignal`.

  Inside the one transaction: `SELECT ... FOR UPDATE` locks the `KeepRequest` row before the
  terminal-state check (`Closed`/`Cancelled`/`Spam`/`Test` reject submission), the tracked
  `ProposedScope`'s version/status gate the pure `Submit()` domain transition, then a native
  Postgres upsert (`INSERT ... ON CONFLICT ... DO UPDATE ... WHERE resolved_at_utc IS NOT NULL`)
  raises or reopens the ADR-463 signal in one round trip — no application-level retry loop, and
  an already-active row is left completely untouched (not even `ConcurrencyVersion`/
  `UpdatedAtUtc` bump) rather than merely preserving `RaisedAtUtc`. Review caught that first cut
  only preserved `RaisedAtUtc` while still bumping the rest, and that the terminal check's
  `AsNoTracking` read had no protection against a concurrent terminal transition landing between
  the check and commit — fixed with the `WHERE` clause and the `FOR UPDATE` lock respectively,
  both proven by new tests (full-row-immutability assertion; a real two-transaction race proving
  `SubmitAsync` observes a terminal transition that commits while it waits on the lock).

  One intentional divergence from the interface shape sketched at preflight: `SubmitAsync` derives
  `RequestId` from the loaded `ProposedScope` rather than taking it as a separate caller-supplied
  parameter — safer (no caller-supplied id can mismatch the scope's actual request) and simpler,
  approved as a sound simplification during review.

  7 production files, 2 migration files, 1 test file, one mutation family. 1456/1456 full unit
  suite, 66/66 focused integration tests (13 `ProposedScope`/signal, including the row-lock race,
  stable across repeated runs), 14/14 architecture tests, `git diff --check` clean.

  3.3a (`ProposedScope`/`ProposedScopeLine`/`KeepRequestWorkSignal` domain and persistence
  foundation) is now fully complete. The next code preflight is **Session 3.3b** (the ADR-480
  three-gate API surface: create/edit/submit endpoints, permission registration, and the
  terminal-request precondition extended to create/edit, not just submit).

- **3.3b: complete (2026-08-10).** ADR-480 three-gate API surface implemented: create/edit/submit
  endpoints, `keep.pricebook.scope.capture` permission registration, and the terminal-request
  precondition extended to create/edit.

  Post-implementation review found two authorization gaps, both fixed:
  - `EditProposedScopeService.LoadForEditAsync` checked version before request visibility/terminal
    state, so a stale token on a same-account scope an Operator can't see under MyWork leaked
    existence via 409 instead of 404. Reordered: visibility/terminal now checked first.
  - `ProposedScopeApiService.SubmitAsync` computed the MyWork/AccountWide visibility gate and then
    discarded it before delegating to `SubmitProposedScopeService`, so any account member could
    submit any scope in the account regardless of request participation. Fixed by adding
    `EditProposedScopeService.VerifyRequestVisibleAsync` (visibility-only, no version/terminal —
    those stay owned by the atomic submit persistence) and calling it before delegating to submit.

  Two new regression tests (real Postgres, seeded Operator with no participation on the request):
  `Submit_ForAScopeOnARequestTheOperatorCannotSee_Returns404`,
  `UpdateLine_ForAScopeOnARequestTheOperatorCannotSee_WithAStaleVersion_Returns404NotConflict`
  (sends a wrong version too, to prove visibility is checked before version/conflict).

  Verification: build 0 errors; 1456/1456 unit; 14/14 architecture; 19/19 new/updated integration
  (`ProposedScopeApiTests` + `ProposedScopeVersionHeaderTests`); `git diff --check` clean. Shared
  regression sweep (OfferingAssembly/CatalogItem/CatalogCategory/ProposedScope, 131 tests) run
  twice: 130/131 both times, same single failure —
  `CatalogItemCreateAndActivateApiTests.Create_TwoConcurrentCreatesInSameAccount_ExactlyOneWins`,
  a pre-existing timing-sensitive concurrency race test untouched by this change. Passes in
  isolation; documented as existing flake, not a regression.

  Decisions locked for this batch:
  - `ProposedScopeApiService` is the **single** ADR-480 three-gate owner (`RequestsOperate` +
    Price Book entitlement + `scope.capture`) for every mutation, **including submit**.
    `SubmitProposedScopeService` stays exactly as 3.3a.2 left it — thin, auth-free, unmodified —
    so gates are never evaluated twice.
  - Terminal-request check for create/edit is a plain account-scoped `KeepRequest.IsTerminal`
    read (no `FOR UPDATE` lock — unlike submit, create/edit aren't racing a second write), owned
    by the new `CreateProposedScopeService`/`EditProposedScopeService` (Application layer).
    `IProposedScopePersistence` stays request-unaware, ordinary aggregate persistence only.
  - **Finding, resolved:** unlike `OfferingAssembly`, `ProposedScope` has no header-level mutable
    field to PATCH (`RequestId` fixed at creation, `Status` only changes via `Submit`) — so there
    is no header-PATCH endpoint in this batch, only line add/update/remove. Endpoint set: `POST
    .../create`, `POST .../{id}/lines`, `PATCH .../{id}/lines/{lineId}`, `DELETE
    .../{id}/lines/{lineId}`, `POST .../{id}/submit`.

  File-level gate (8 production files, 1 mutation family, within cap):
  - New: `ProposedScopeVersionHeader.cs` (`X-Keep-ProposedScope-Version`, mirrors
    `OfferingAssemblyVersionHeader`; needs new `ProposedScopeErrors.ExpectedVersionRequired`/
    `ExpectedVersionInvalid`), `ProposedScopeEndpoints.cs`, `ProposedScopeApiService.cs`,
    `CreateProposedScopeService.cs`, `EditProposedScopeService.cs`.
  - Modified: `PermissionKeys.cs` (add `ScopeCapture`), `RolePermissions.cs` (add to
    `OperatorBase`), DI registration file. `SubmitProposedScopeService.cs` is NOT modified.

  Reference templates identified: `OfferingAssemblyApiService.cs` (gate-composition shape,
  `AuthorizeAsync` helper — note it's a 2-gate example, ProposedScope's is 3-gate, no exact
  sibling to copy verbatim), `OfferingAssemblyVersionHeader.cs`, `EfProposedScopePersistence.cs`
  (existing, unmodified), `IKeepRequestOperatePersistence`-style account-scoped read for the
  terminal check (exact read method still to be selected at implementation time — not yet
  resolved which existing seam or new method supplies the account-scoped `KeepRequest` read).

  This completed implementation is the API foundation for the later field-facing Session 3.4
  proposed-scope capture workflow; it did not include a technician UI.

  The completed 2e record follows. Build 113 broke the
  work into bounded implementation slices. 2e.0 preflight split 2e.1 into 2e.1a (canonical SKU
  foundation) and 2e.1b (pricing-mode foundation) because 2e.1b's only caller,
  `EfPriceBookPublishPersistence`, pushed the combined slice past the 8-production-file gate.
  Both are complete: `CatalogItem.NormalizedExternalKey` (ASCII-normalized, unique per account,
  rejects an all-punctuation key) and `PriceBookVersionLine.PricingMode`
  (`StandalonePrice`/`NoStandalonePrice`, invariant-enforced against Sell Price) are migrated and
  backfilled, with domain, API, and migration-backfill test coverage.

  **2e.2 — Atomic creation API: complete.** `POST /keep/pricebook/catalog-items/create-and-activate`
  is now the sole item-creation path; the prior draft-create and draft-activate routes and their
  `CatalogItemApiService` methods were removed, not just left unused, so a direct API caller can no
  longer reach a Draft outcome. The transaction needs an intentional two-phase save: `CatalogItem`
  and its own `PriceBookVersionLine` each hold a FK back to the other (the item's current-price
  pointer; the line's composite FK to the item), so EF cannot insert both in one `SaveChanges` call.
  It inserts with a null price pointer first, then repoints and saves again inside the same
  serializable transaction — matching the existing Account/AccountUser ADR-019 pattern. Full
  regression is clean (1,364 unit tests, 14 architecture tests, both integration suites, `git diff
  --check`). Reported honestly: the new concurrent-create race test was improved (thread-pool
  scheduling plus pre-warmed connections) from ~50% to ~19/20 passing locally, but retains the same
  class of real-Postgres-timing flakiness the existing `PriceBookPublishApiTests` concurrency tests
  already carry — not fully eliminated without test-only synchronization hooks in production code,
  which was out of this batch's scope.

  **2e.3 — Catalog read contract: complete.** `GET /keep/pricebook/catalog-items` (list/search),
  `GET /keep/pricebook/catalog-items/{id}` (detail), and `GET /keep/pricebook/catalog-categories`
  (choices) are read-only, account-scoped, and cursor-paged. Search matches DisplayName,
  canonical-normalized SKU, and active aliases; each result reports why it matched
  (DisplayName/ExternalKey/Alias precedence) and rows are ordered (MatchRank, DisplayName, Id) —
  the locked total order that the signed keyset cursor carries end to end, with a fingerprint over
  search/type/category/status (excluding limit/cursor) so a cursor from a different filter shape is
  rejected. The auth gate reuses `CatalogItemApiService`'s 3-gate composition (ADR-462) except gate
  1 denies only `IsBlocked`, not `IsReadOnly` — matching every other pure-read service in this
  codebase, so an OffSeason account can still browse its price book. Query-shape validation
  (limit/type/status/cursor/unknown-param) is handled as API-layer `ValidationProblem` responses
  rather than named Core errors — a deliberate, reviewed choice since this is transport-layer
  parsing, not a domain error contract. New: `ICatalogReadPersistence`/`EfCatalogReadPersistence`,
  `CatalogItemListCursor` (reuses the existing `IKeepRequestListCursorProtector` — its HMAC signing
  carries no KeepRequest-specific payload knowledge), `CatalogReadApiService`,
  `PriceBookCatalogQueryBinding`. 15 new integration tests cover canonical-SKU search, shared-alias
  multi-match, active/inactive filtering, match rank/reason, browse- and search-mode cursor walks
  (including a same-DisplayName Id tie-break), cursor/fingerprint-mismatch rejection, cross-account
  404, and entitlement/role gate denial. Full regression clean (14 architecture tests, `git diff
  --check`); the one pre-existing 2e.2 concurrent-create flake is outside this read-only slice.

  **2e.4 — Workspace shell and navigation: complete.** `web/ophalo-app` (the authenticated
  operator dashboard — `web/ophalo-web` is public-only and untouched) gains a `pricebook` route,
  entitled desktop nav entries (sidebar and workbench top-bar), and a `GET
  /accounts/me/capability-packages`-backed entitlement check scoped to Owner/Admin only (avoids a
  guaranteed 403 for other roles). Closed a real pre-existing gap found during preflight: no mobile
  overflow/hamburger navigation existed anywhere in the app — Settings and Getting Started weren't
  reachable on mobile either, outside contextual links — so build-log/112's "mobile hamburger/
  overflow navigation alongside Requests" requirement had no mechanism to hang off. Added one
  shared `MobileNavMenu` (reuses the existing `KeepModal` primitive for focus-trap/Escape/focus
  restoration) plus a new mobile-only top bar (logo + hamburger, present on every route since none
  existed before) — this is a visible layout change to the existing Requests/Detail mobile views,
  not scope creep: it's what "shared App-level" mechanism means. Caught and fixed a real layout bug
  before commit: the shell container wasn't `flex-col` for non-workbench routes, so the new mobile
  bar and content would have rendered side-by-side rather than stacked.
  `pages/PriceBook.tsx` renders the list shell only (default active items — search/filter/paging
  are 2e.7): role-denied, unentitled-plan, entitlement-check-loading, and a **distinct retryable
  entitlement-check-error state** (added after review — a failed `capability-packages` fetch must
  not be reported as "not included in your plan"), plus catalog loading/error/empty/list states,
  rendering `NoStandalonePrice` as "No standalone price" per build-log/112. No creation drawer, no
  item detail, no actions column. New: `MobileNavMenu`, `PriceBook`, catalog/capability types and
  client functions in `apiClient.ts`/`apiClient.types.ts`. 19 new frontend tests (nav-item
  role/entitlement matrix, mobile menu rendering/dismissal, Price Book states including the
  entitlement-error/plan-denied distinction). `tsc --noEmit`, the CSS-token check, the production
  build, and the full vitest suite (234 tests) are all clean. Not yet visually verified in a live
  browser session against a running backend — flagged, not silently skipped. The next action is
  2e.5 — Create-and-activate drawer (build-log/113).

  **2e.5 — Create-and-activate drawer: correction/refinement pass applied; not accepted yet.**
  `CatalogItemDrawer.tsx` now matches Build 112's owner-friendly direction: visual order is Name;
  Type + Category (paired desktop row); UOM (defaulted to `each`, with literal-value quick-fill
  chips each/hour/ft/sq ft/gal/lb/box/lot replacing the prior datalist); a fieldset-grouped, visible
  "Codes & search (optional)" section holding SKU and the renamed "Search keyword / shorthand" alias
  field; and a plain-language "This item doesn't have its own sell price" checkbox replacing the
  abstract pricing-mode buttons (checked → `NoStandalonePrice` with `sellPrice: null`, never `0`).
  Save & add another retention, Ctrl/Cmd+Enter, first-invalid-field focus with value preservation,
  the accessible category input label, the contained discard-confirm focus trap, and the retryable
  category-refetch-failure recovery were already correct in the prior pass and are unchanged. 18
  frontend tests cover the above plus both keyboard shortcuts and null-serialization.
  **Currency:** Christian explicitly chose the USD-only pilot posture over the alternatives (a
  server-owned currency source, or blocking completion) — recorded as an ADR-468 amendment. The
  drawer's hard-coded `"USD"` is now a deliberate, documented decision rather than an unresolved
  gap; a server-owned account-currency setting remains required before non-USD pilot accounts are
  supported, out of 2e.5 scope. Per Christian's follow-up, the dedicated read-only Currency field
  was removed in favor of a quiet "Prices in USD" note plus `$` prefixes on Cost and Sell Price —
  de-emphasized while staying honest about the approved USD-only pilot posture. 19 frontend tests.
  The first real-browser pass also found and corrected a drawer-layout defect: its form body must
  scroll independently while a non-shrinking footer stays reachable and pinned to the viewport on
  both desktop drawer and mobile full-screen presentations. The next refinement is now locked in
  Build 112: a successful zero-item catalog view hides the duplicate page-header action and shows
  one contextual **Add your first catalog item** onboarding action; populated views use the header's
  **Add catalog item** action. This is deliberately local page behavior, not authorization to
  redesign the global app shell. `tsc --noEmit`, the CSS-token check, and `git diff --check` are
  clean. **Session handoff (2026-08-06):** 2e.5 implementation is complete, but its real-app
  browser verification remains a required manual acceptance checkpoint: use an entitled Owner/Admin
  account to verify empty and populated states on desktop and mobile, the reachable drawer footer,
  create-and-refresh behavior, and keyboard/error paths. Do not use a temporary mocked preview as
  the acceptance substitute. That checkpoint may run alongside the next mechanical preflight; the
  next implementation session is **2e.6 — Active-item maintenance**.

  **Price Book model alignment (2026-08-06):** the pilot furnace-install workbook was verified
  against the locked decision index and Sessions 2e/Build 112–113 delivery status. No new
  client-specific pricing architecture was adopted: the one catalog-item form remains correct;
  ADR-457's already-decided, not-yet-built static associated-item assemblies own standard
  consumables and component breakdown; and the workbook's purchase-side tax does not authorize a
  sales-tax engine. Dynamic pricing formulas remain deferred. Build Log 114 adds one bounded
  2e.6 follow-up: render owner/admin-only, read-only gross profit, margin %, and markup % from
  existing Cost/Sell Price snapshots when derivable. It introduces no schema field, persisted
  calculation, automatic price, or field-role cost/margin exposure.

  **Category governance clarification (2026-08-06):** categories remain optional account-owned
  browse labels under ADR-461, with no seeded trade taxonomy. The current API can create and
  activate/inactivate categories but does not yet provide a category-management UI, rename, item
  counts, or safe unassignment on inactivation. Build Log 114/DEF-091 lock a later dedicated
  Owner/Admin maintenance slice: category filtering remains in 2e.7; rename, assigned-item count,
  and confirmed inactivation follow separately. Inactivation must atomically clear `CategoryId` on
  all currently assigned catalog items, hide the inactive category from future assignment, preserve
  immutable history, and never restore assignments automatically on reactivation. Do not add merge
  or bulk reassignment without pilot evidence.

  **Scalable category selection (2026-08-06):** pilot testing found the native category dropdown
  adequate for a short list but cumbersome for a business with 10–20 or more categories. Build Log
  114 now schedules a single accessible searchable category combobox in 2e.7, shared between
  catalog entry/edit and the category filter. It keeps No category explicit and creates a new
  category only after a normalized exact-match check. This is a workflow refinement, not a seeded
  trade taxonomy or new pricing capability.

  **2e.5 acceptance and 2e.7 drawer refinement (2026-08-07):** manual browser acceptance is now
  complete for the committed 2e.5 drawer batch (`7603430`); the next implementation session remains
  **2e.6 — Active-item maintenance**. Review of the real desktop drawer locked follow-up work in
  Build Logs 112–114 for 2e.7: replace the category select/reveal flow with a stable-layout,
  searchable creatable combobox; disable every item-save path while category creation or race
  recovery is pending; pair Cost and Sell Price on desktop while stacking them on mobile; and keep
  `Common item` truthful (no invented quick-add label) and UOM quick-fill non-disruptive (no focus
  auto-advance). These are frontend workflow refinements, not new catalog, pricing, or taxonomy
  capabilities.

  **2e.6 split and 2e.6a detail delivery (2026-08-07):** the original Active-item maintenance row
  is intentionally split to preserve the 8-production-file / 3-mutation-family gate: **2e.6a** is
  read-only item detail and derived profitability; **2e.6b** is the one new header-update family;
  **2e.6c** is existing alias-management UI plus thin Reactivate API wiring; and **2e.6d** is the
  existing later-price publish UI and its ADR-470 conflict handling. Do not recombine these slices.
  2e.6a is implemented and verified, awaiting its commit: it adds the `#/pricebook/:id` detail
  route, keyboard-accessible list-to-detail navigation, header/category/current price/alias display,
  and Owner/Admin-only gross profit, margin, and markup from immutable current Cost/Sell snapshots.
  The preflight found and closed a read-contract omission: detail now returns `CurrentCost` alongside
  Current Sell Price and pricing mode; no persistence/service/mutation work was needed. Direct URLs
  use the same role, entitlement loading/error, and plan-denied guards as the Price Book list before
  requesting data. Zero rules remain locked: show gross profit when both values exist; margin is
  unavailable at zero Sell Price; markup is unavailable at zero Cost; and no standalone price leaves
  profitability unavailable. Focused frontend/integration checks, `tsc --noEmit`, CSS-token check,
  production build, and review are clean. 2e.6a is committed (6167f53).

  **2e.6b — header update: complete (2026-08-07, commit 19e49de).** Adds the single
  `PATCH`-style header-update mutation (`updateCatalogItemHeader`) for display name, external
  key/SKU, category, and common-item flag, gated by the existing optimistic
  `concurrencyVersion` header contract. Domain, application, and API layers enforce the same
  validation already locked for creation (`DisplayNameRequired`/`TooLong`,
  `InvalidExternalKey`/`ExternalKeyAlreadyExists`, `CatalogCategory.NotFound`/`NotActive`), plus
  `CatalogItem.VersionMismatch` on a stale `concurrencyVersion`. On the frontend, a version
  conflict unmounts the edit form, refreshes the read-only view to the concurrent editor's latest
  values via query invalidation, and disables Edit (`conflictRefreshPending`, showing
  "Refreshing…") for the window between conflict detection and the refetch landing, so a fast
  double-click can't reopen the form and resave against the still-stale version. Re-entering Edit
  after the refresh restores the user's unsaved draft rather than re-seeding from the refreshed
  item. Focused unit (75), integration (26 passing; the pre-existing `ExactlyOneWins` concurrency
  test from 2e.2 remains its documented flaky self and is unrelated to this slice), and frontend
  (15, including a deferred-promise regression test proving the disabled/"Refreshing…" window and
  that a click during it does not re-trigger the mutation) suites are clean; `tsc --noEmit` is
  clean. 12 files changed (within the batch gate: 1 mutation family, 8 production files, 12 total
  including tests).

  **2e.6c — alias management, Reactivate, and Inactivate: complete (2026-08-07, commit
  6e72ba7).** Adds `CatalogItemApiService.ActivateAsync` and `PATCH
  /keep/pricebook/catalog-items/{id}/activate` (thin wrapper over the already-existing domain
  `CatalogItem.Activate()`/`CatalogItemLifecycleService.ActivateAsync`, mirroring `/inactivate`
  exactly — no new domain rule or error contract). On the frontend, wires up the alias
  add/activate/inactivate endpoints that already existed server-side since 2a.2 but had no client
  caller, adds a Reactivate action for a non-Active item, and — after review caught that Reactivate
  had no way to ever be reachable — adds an Inactivate action for an Active item, gated by an
  inline "Confirm inactivate"/"Cancel" step (mirrors `TeamSection`'s suspend/remove pattern) since
  it is a one-click action with real consequence for anyone currently searching the catalog.
  Reactivate/Inactivate/alias-add/alias-activate/alias-inactivate share one `itemBusy` pending
  gate with the existing `conflictRefreshPending`: every one of them disables Edit and every
  alias/lifecycle control until its triggered refetch actually lands, including on
  `VersionMismatch`, `AlreadyActive`, and `NotActive` conflicts — not just until the mutation call
  resolves, which was a bug caught and fixed mid-session via a failing regression test. Alias-add
  failures preserve the typed alias text rather than clearing it. Reactivate/Inactivate success
  also invalidates the `catalogItems` list query so its Status column stays in sync (the list has
  no active-only filter yet; that is 2e.7's job). No backend change was needed for Inactivate — it
  reuses the existing 2a.2 `/inactivate` endpoint, so this batch stays at 1 new mutation family
  (Reactivate) and 7 files changed (well within the 8-production-file / 12-total gate). Verified:
  `tsc --noEmit`, CSS-token check, and the production build are clean; the frontend suite is clean
  (288 tests, including 9 new/extended tests covering alias add/activate/inactivate,
  Reactivate/Inactivate success, `AlreadyActive`/`NotActive`/`VersionMismatch` conflicts held
  through their refetch, and an inactivate-then-reactivate round trip proving each step uses the
  freshly refreshed `concurrencyVersion`); the backend unit suite is clean (1,379 tests); the
  2 new backend integration tests for `/activate` pass (13/13 in the file); `git diff --check` is
  clean.

  **2e.6d — later price update (server: publish) and its ADR-470 conflict handling: complete
  (2026-08-07).** Wires the `POST /keep/pricebook/catalog-items/{id}/publish-price` endpoint
  (backend-complete since Build 111/2e.1b, previously had no frontend caller) into a dedicated
  `CatalogItemPricePublishForm` component. Review added one backend rule the original endpoint
  lacked: publish now requires an Active item, rejecting an Inactive one with the new
  `PriceBookVersion.CatalogItemNotActive` (409) from `EfPriceBookPublishPersistence`, mapped
  explicitly in `ErrorHttpMapper`. Existing `PriceBookPublishApiTests` seeds were updated to an
  Active item to match (Draft has been unreachable through the public API since 2e.2 anyway); a
  new `Publish_WhenCatalogItemInactive_Returns409` test proves the rejection leaves no
  `PriceBookVersion`/`ManualPriceOverride` row behind. No version header on this endpoint — ADR-470's
  lock is account-scoped (`PriceBookAccountState`), not `CatalogItem.ConcurrencyVersion`, so a
  manual retry is always safe.

  A second review pass reworked the UI for a small-business owner rather than a price-book
  operator: the user-facing action is "Update price" throughout (internal names — the API method,
  mutation, `PublishLockConflict` — stay publish-oriented since that's what the server actually
  does); Sell Price is the first, visually primary field; Cost is "Internal cost (optional)" with
  "customers do not see this" helper text; the "no standalone price" toggle moved into a collapsed
  Advanced options section; the free-text audit reason became a required guided picker (Supplier
  cost changed / Correcting a price / Promotion or seasonal pricing / Other, the last revealing a
  required, 500-char-limited text input); and a no-op guard compares the proposed Cost/Sell
  Price/pricing mode against the values loaded when the form opened, disabling Update price with an
  explanatory message unless one of those three actually changed (a reason change alone can never
  submit a duplicate immutable price version). The below-cost confirmation gate, currency-aware
  `$`-style prefixes (via `Intl.NumberFormat`, respecting the item's actual currency instead of a
  hardcoded USD — `ProfitabilityPanel` and the read-only Sell price/Cost display were also
  corrected to use it), server error-code mapping, conflict-triggers-a-refetch-with-the-draft-
  preserved behavior, and the no-auto-resubmit rule are all unchanged; the conflict message was
  reworded to "Someone else updated pricing a moment ago. We refreshed the latest price—review your
  changes and try again." with no mention of locks, versions, or replay. The extraction also cut
  `CatalogItemDetail.tsx` from 1,073 to 812 lines — it now owns only the "Update price" trigger and
  open/closed state, passing the item's current price data and id into the form component.
  8 files changed (6 production, 2 test — 1 new mutation-family rule tightened on an existing
  endpoint, no new family; well within the batch gate). Verified: `tsc --noEmit`, CSS-token check,
  and the production build are clean; the frontend suite is clean (294 tests, 30 of them in
  `CatalogItemDetail.test.tsx` covering the guided reason picker, Other requiring typed text, the
  Advanced-options toggle, the no-op disable/enable transition, below-cost confirmation, and the
  conflict/draft-preservation guarantee); the backend unit suite is clean (1,379 tests); the 2
  new/updated `PriceBookPublishApiTests` pass (6/6 in the file); `git diff --check` is clean.

  ADR-474, ADR-475, and the `keep-product-positioning.md`/`deferred-topics.md` changes alongside
  this work are Christian's, made outside this implementation session and left untouched.

  **2e.7 split and 2e.7a delivery (2026-08-08, commit 9ce75be).** 2e.7 — Lifecycle and
  operating-speed polish — is split into **2e.7a** (list search/filter/pagination), **2e.7b**
  (shared searchable/creatable category combobox, replacing the drawer's native select and reused
  read-only in the list filter), and **2e.7c** (drawer Cost/Sell Price desktop pairing, keyboard-
  shortcuts help, accessibility polish); do not recombine these slices. 2e.7a is frontend-only: the
  search/categoryId/status/cursor query params were already fully supported server-side
  (`CatalogReadApiService`, `PriceBookCatalogQueryBinding`) but unused by `PriceBook.tsx`. Adds
  debounced (300ms) search, an active-categories-only filter dropdown, an Active/Inactive status
  toggle, and Prev/Next pagination via a client cursor stack; any filter change resets to page one.
  Review caught that the unfiltered list defaults to `status=Active` server-side, so a catalog
  holding only inactive items would render the misleading "Your catalog is empty" onboarding
  zero-state; fixed with a conditional zero-item-limit probe against `status=Inactive`, fired only
  when the unfiltered active list comes back empty, distinguishing that case into its own
  "No active items" state (header CTA stays visible, one-click switch to the Inactive filter). 2
  files changed (both frontend, no new mutation family). Verified: `tsc --noEmit`, CSS-token check,
  and the production build are clean; the frontend suite is clean (301 tests, 22 in
  `PriceBook.test.tsx`); `git diff --check` is clean. Not yet visually verified in a live browser
  session — flagged, not silently skipped. The next implementation preflight is **2e.7b — shared
  category combobox**.

  **Catalog-entry review follow-up (2026-08-08):** owner/admin workflow review confirms the 2e.7
  sequence without expansion: 2e.7b owns the stable-layout searchable/creatable category combobox;
  2e.7c owns the desktop Cost/Sell Price pairing and shortcuts/accessibility polish. UOM quick-fill
  remains a literal-value fill with no automatic focus jump, because surprise focus movement risks
  fast-entry mistakes. A future advisory **Similar catalog items** assist is recorded as DEF-092,
  not added to either next slice: it must use a bounded server search rather than the visible page
  or an unbounded local catalog, be debounced and capped, link to inspection, and never block save
  or present a similarity as a duplicate.

  **2e.7b category-combobox UX correction — locked before acceptance (2026-08-08):** the initial
  shared combobox implementation technically supports creation after typing, but its empty
  `No category` presentation makes that path undiscoverable to a busy Owner/Admin. Correct it as a
  guided searchable combobox, without a second form, separate create button, layout shift, or
  automatic category creation:

  - With no committed category, the input placeholder is **`Search or create category…`**. The
    create/edit forms retain the visible `Category (optional)` label; the list filter retains its
    distinct browse meaning (`All categories`) and remains select-only.
  - On focus with an empty query, retain the normal actionable options (`No category` first, then
    existing categories), and append a visually quiet, **non-selectable** footer separated from
    those options: **`💡 Type a new name to create category`**. It is discovery guidance, not a
    second action or an option keyboard navigation can accidentally choose. Equivalent concise
    instruction must be exposed to assistive technology.
  - When the typed normalized value is not an exact existing category, replace ordinary no-match
    presentation with one clear, visually prominent option: **`+ Create "{entered name}"`**. It is
    the default highlighted option, uses the Price Book accent/primary-action treatment rather than
    looking like an ordinary category row, and is created only after an explicit click or Enter.
  - Enter invokes the highlighted create option (or selects an existing/no-category option when
    that is highlighted). **Tab never creates or changes a category**: it follows normal combobox
    focus traversal, because a fast data-entry Tab must not persist an accidental typo. Escape
    closes the popup and restores the last committed selection without creating or changing
    anything.
  - Preserve the existing exact-normalized-match reuse, 409 refetch-and-select race recovery, and
    pending gate. From the start of a create attempt until it resolves, every item-save path
    (including Ctrl/Cmd+Enter) remains disabled; an error/conflict keeps the item draft and intended
    category recoverable rather than silently omitting it.

  This is a correction within 2e.7b's existing frontend-only scope, not a new mutation family or
  authorization to add category management. Validate it in focused component and create/edit
  consumer tests, then visually verify the empty, exact-match, no-match/create, pending, error,
  Escape, Enter, and Tab paths on desktop and mobile before accepting/committing the batch.

  **2e.7b scale and ordering acceptance correction — required before commit (2026-08-08):** do
  not accept the combobox merely because its small-list tests pass. Owner/Admin category choice
  must remain predictable with 15–50 account categories. The current category-choice read orders
  by persisted `DisplayOrder` then Name; because the direct-entry MVP assigns new categories the
  next display-order value and provides no ordering-management UI, this presents effective creation
  order rather than the A–Z ordering an owner expects. Correct this at the authoritative read
  contract, **not** with divergent frontend sorts: change the active-category query to stable,
  case-insensitive alphabetical Name ordering with a deterministic tie-breaker, and add the focused
  persistence/API proof. This is a bounded backend read-path correction (no mutation family),
  expressly approved as necessary to complete the 2e.7b owner workflow.

  The popup must also be structurally bounded rather than relying on one scrolling list:

  - Pin the actionable **No category** choice at the top of the popup.
  - Put only ordinary existing-category rows in a vertically scrollable region capped at roughly
    240–256px (`max-h-60`/`max-h-64`), so a long account list cannot overrun the drawer/mobile
    viewport or obscure its action footer.
  - Keep the non-selectable `💡 Type a new name to create category` discovery footer fixed below
    that scroll region. When a typed name has no exact normalized match, replace that footer with
    the prominent **`+ Create "{entered name}"`** action so it remains visible without scrolling;
    it must not be appended after potentially many partial-match rows.
  - Preserve the locked keyboard rule: the fixed create action is default-highlighted and Enter or
    click invokes it; Tab only traverses focus and never creates/changes a category; Escape restores
    the last committed selection. Keep a semantically valid WAI-ARIA combobox/listbox structure as
    the visual regions are separated.
  - Verify popover placement and clipping when the control is near the drawer/mobile viewport's
    lower edge; the popup must not be hidden behind or push the pinned form footer.

  Required proof before 2e.7b acceptance: focused tests for mixed-case A–Z category ordering, the
  pinned No-category/create-footer behavior with a 15+ (preferably 50) category fixture, exact/
  partial/no-match filtering, and keyboard selection; live entitled-app desktop and mobile checks
  at empty, one, 15, and 50 categories, including the lower-edge popup placement. Do not commit or
  describe 2e.7b as complete until this correction and visual checkpoint are recorded.

  **2e.7b complete (2026-08-08).** `CategoryCombobox` (`components/keep/CategoryCombobox.tsx`) is
  the shared searchable combobox: `CatalogItemDrawer` (create) and `CatalogItemDetail`'s header-edit
  form both use it in creatable mode with the identical exact-match/409-race-recovery/pending-gate
  contract; `PriceBook`'s category filter uses it select-only. Guided-discovery correction applied:
  `Search or create category…` placeholder when creatable, a non-selectable `💡 Type a new name to
  create category` hint on empty-query focus (always exposed to AT via `aria-describedby`, visible
  footer or `sr-only` fallback), the create action as the default-highlighted option even over
  partial category matches, and Tab/blur reverting to the last committed selection without ever
  creating or changing anything (Escape does the same).

  Scale/ordering correction applied: `EfCatalogReadPersistence.GetActiveCategoriesAsync` now orders
  by `NormalizedName` (case-insensitive, already unique per account) then `Id`, replacing the old
  `DisplayOrder`-then-Name read that effectively showed creation order; `CatalogReadApiTests`'
  ordering test was rewritten (`Categories_ReturnsActiveOnlyOrderedByNameCaseInsensitive`) with
  names deliberately out of both display-order and byte-case order to actually prove it. The popup
  is now three structurally distinct regions rather than one scrolling list: "No category" pinned
  above, ordinary category rows in a `max-h-60` (~240px) scrollable middle, and the create action
  pinned below — proven with a 50-category fixture (`CategoryCombobox.test.tsx`'s "2e.7b scale
  correction" suite: pinned rows structurally outside the scroll region, height cap present, create
  action reachable and default-highlighted regardless of partial-match count, both No category and
  Create reachable by keyboard alone).

  Two additional correctness issues were caught and fixed during this pass, before any browser
  check: (1) the create option's highlight used `bg-[var(--keep-accent)]/10`, a Tailwind opacity
  modifier on a `var()`-based arbitrary color that Tailwind 3.4 cannot resolve — confirmed via the
  compiled CSS output that it emitted no rule at all, so the highlight silently never rendered;
  replaced with the existing `--keep-accent-bg` token. (2) the draft-text sync effect depended only
  on `currentCategoryId`, so a `categories` query resolving after `currentCategoryId` was already
  set (e.g. opening Edit before the categories fetch lands) left the field showing blank forever
  despite a real category being selected; it now also depends on `selectedCategory?.name`.

  Manual browser acceptance (desktop and mobile, entitled app) confirmed at 0, 1, 15, and 50
  categories: pinned No category/create-action reachability, the scrollable middle region, and
  popover placement/clipping at the drawer/mobile viewport's lower edge all behave per the
  correction above.

  Verified: frontend `tsc --noEmit`, CSS-token check, full vitest suite (328/328), and the
  production build are clean. Backend `dotnet build` is clean; `CatalogReadApiTests` (15/15) and
  the broader `Catalog*` integration/unit suites are clean aside from the pre-existing, separately
  documented flaky `CatalogItemCreateAndActivateApiTests.Create_TwoConcurrentCreatesInSameAccount_
  ExactlyOneWins` (confirmed passing in isolation; unrelated to this batch, not a 2e.7b regression).
  `git diff --check` is clean. Batch: `CategoryCombobox.tsx` + test (new), `CatalogItemDrawer.tsx`,
  `CatalogItemDetail.tsx`, `PriceBook.tsx` + their tests, plus the two pre-approved backend files
  (`EfCatalogReadPersistence.cs`, `CatalogReadApiTests.cs`) — no new mutation family.

  **2e.7c — Cost/Sell Price desktop pairing, keyboard-shortcuts help, accessibility polish:
  complete (2026-08-08).** In `CatalogItemDrawer.tsx`, Cost and Sell Price now share the same
  `grid-cols-1 sm:grid-cols-2` pairing already used for Type/Category (stacked on mobile, paired
  on desktop); the "no standalone price" checkbox moved above the pair since it gates Sell
  Price's visibility. All 6 field-error spans now have an `id`, with their inputs wired to
  `aria-describedby` only when the error is present; every other interactive control already used
  the shared `FOCUS_RING` class, so no other focus-visible gaps were found. `PriceBook.tsx` adds a
  page-level "Keyboard shortcuts" button opening an accessible dialog (built on `KeepModal`, so
  focus-trap/Escape/focus-restoration are inherited, not reimplemented) documenting all four
  locked shortcuts (Cmd/Ctrl+Enter, Escape, `/`, `n`), plus the `/` (focus search) and `n` (new
  item) list-level handlers themselves — both were previously undocumented-and-unimplemented, not
  just undocumented. `/` uses a narrow `isTypingTarget` guard (input/textarea/select/
  contenteditable only) so it still fires from a focused button or link — e.g. right after closing
  the shortcuts dialog, a bug caught and fixed mid-session; `n` uses a stricter
  `isEditableOrInteractiveTarget` guard that also backs off from any button/link/interactive-role
  element, so a stray `n` can never accidentally create an item. Both are silenced while the
  drawer or the shortcuts dialog is open.

  Manual mobile review (428px) surfaced three corrections folded into this same slice: the
  6-column catalog table was unreadable at mobile widths (values wrapping into fragments), so
  mobile now gets a compact card list (`<ul>`/`<li>`, one full-width tappable card per item with
  an explicit `aria-label="View {displayName}"` rather than relying on its concatenated child
  text) prioritizing Name, Type/UOM, Sell price or "No standalone price", and Status — SKU is
  dropped as low-value on the compact card; desktop table is unchanged, now wrapped in
  `hidden sm:block` alongside the `sm:hidden` card list. The global sticky "New Request"
  quick-capture FAB (`App.tsx`) was unconditionally shown except on the request-detail route,
  which put it directly next to Price Book's own "Add catalog item" action with an unrelated
  label — it's now also hidden on `pricebook`/`pricebook-item` routes. The page header reflows to
  `flex-col` below `sm` so the title/description read at full width before the action row (now
  including the shortcuts button, with a hover/keyboard-focus tooltip since a bare icon isn't
  self-explanatory), instead of competing side-by-side and wrapping awkwardly.

  3 production files changed (`App.tsx`, `CatalogItemDrawer.tsx`, `PriceBook.tsx`), 2 test files —
  no new mutation family, frontend-only. Verified: `tsc --noEmit`, CSS-token check, and the
  production build are clean; the frontend suite is clean (337 tests, up from 328). `git diff
  --check` is clean. Not independently re-confirmed in a live browser after the last two fixes
  (the `/`-from-a-button correction and the accessible-name/tooltip additions) — flagged, not
  silently skipped.

  **2e.8 — Completion verification and handoff: complete (2026-08-09).** The outstanding live
  browser re-verification was completed after the final 2e.7c fixes: the `/` shortcut works from a
  focused button after closing the shortcuts dialog without stealing text-entry keystrokes, and the
  shortcuts control has a clear accessible name and hover/keyboard-focus tooltip. The entitled
  Owner/Admin Price Book workflow was rechecked on desktop and mobile, including the final
  interaction/accessibility polish and the required happy-path and conflict/error coverage.
  Proportionate automated checks, the frontend production build, and `git diff --check` were also
  completed. Build 112/113 boundaries and the deferred topics remain unchanged: 2e is closed with
  no image-storage scope added; image storage remains paused until the Session 3 Price Book
  foundation completes (see Session 3.0/3.1 status above).

- **2a.1 — CatalogItem foundation:** complete and migrated. `CatalogItem`, its lifecycle/persistence
  stack, and `keep_pricebook_catalog_items` are in place. Review corrected the table name and ensured
  a concurrent SKU collision maps to `CatalogItemErrors.ExternalKeyAlreadyExists`.
- **2a.2 — CatalogItem API delivery:** complete. The feature/permission/user gate is enforced before
  catalog mutations; create, activate, and inactivate routes use the locked version-header contract.
  A tightly coupled one-file gate exception (9 rather than 8 production files) is recorded in
  [Build Log 110](build-log/110-price-book-quotes-materials-session-2-preflight.md).
- **2b.1 — CatalogCategory foundation + CatalogItem FK:** complete and migrated. Categories use the
  shared `CatalogActiveState`, have independent optimistic concurrency, and the nullable composite
  `(AccountId, CategoryId)` FK prevents cross-account category assignment.
- **2b.2 — CatalogItemAlias foundation:** complete and migrated. Aliases are owned children of
  `CatalogItem` (own table, no independent `ConcurrencyVersion`), created/activated/inactivated only
  through the parent aggregate, which rotates its own token on every alias mutation.
- **2b.3 — Category and alias API delivery:** complete. `CatalogCategoryApiService` and
  `CatalogItemApiService`'s new alias methods share the existing `PriceBookCatalogManage` gate — one
  entitlement, not per-entity. Alias routes nest under the catalog item. Review found two blockers,
  both fixed: `CatalogItem.Alias*` errors needed explicit `ErrorHttpMapper` entries (the `Alias`
  segment breaks the generic `.NotFound`/`.AlreadyActive`/`.NotActive` suffix matches — the new
  generic `.NotActive` rule also corrects `CatalogItem.NotActive`, shipped in 2a.2 as an unmapped
  400, to 409); and every activate/inactivate route now returns 200 with the rotated
  `ConcurrencyVersion` instead of 204, since a client had no way to make a second versioned mutation
  without a separate read.
- **2c — Import staging/validation: mandatory coding sequence.** Each sub-slice requires the normal
  Claude mechanical preflight and Codex validation before implementation. Preserve the hard gate of
  at most eight hand-written production files and three independent mutation-handler families per
  slice; tests and the generated migration are outside that count. Do not merge these slices merely
  because they touch the same aggregate.
  - **2c.1a — Import/row schema and domain foundation.** Add `PriceBookImport` and
    `PriceBookImportRow`; their three explicit enums (`PriceBookImportStatus`, row validation status,
    and exception resolution); EF configurations/migration; and aggregate-owned factory/transition
    methods only. Persist every ERD field from Build 108, including `(ImportId, RowNumber)` unique
    enforcement and account-scoped FKs. `SourceFileObjectKey` is required/non-null from the first
    migration and test fixtures use valid opaque test keys. This slice owns only staging/row-domain
    invariants and the `Staged → Validated` / pre-publish `Discarded` mechanics; it does not implement
    publish mutations, API routes, storage, parsing, or an upload-created import.
    - **Implementation decisions locked (2026-08-02).** `PriceBookImportRow` is an aggregate-owned
      child: the import creates rows and governs import lifecycle/publish boundaries, while a later
      validation service may query and persist individual rows through row-domain transition methods
      without loading the complete import. Rows have no independent `ConcurrencyVersion`. The EF
      relationship uses an explicit `PriceBookImportId` FK, an index on that FK, and a composite
      `(PriceBookImportId, ValidationStatus)` index for exception-review queries. The import-to-row
      FK cascades on deletion; preserve account isolation with account-scoped relationships/FKs.
    - Proposed monetary/labor source values (`ProposedCost`, `ProposedSellPrice`,
      `ProposedSourceLaborHours`, `ProposedSourceConsumablesAllowance`, and
      `ProposedSourceTaxAmount`) are nullable `decimal?` staging values. `ProposedType` is nullable
      raw `string?`, not `CatalogItemType`, so unsupported or misspelled source values can be staged
      and corrected before catalog mapping.
    - `ValidationMessages` is a domain-facing, strongly typed collection of strings (expose an
      `IReadOnlyCollection<string>` backed by a private list; do not expose a publicly mutable list).
      Persist it as one PostgreSQL `jsonb` column with an EF JSON value converter **and a
      `ValueComparer`** so in-place collection changes are detected. Validation messages for parse
      failures must retain the offending raw input (for example, the raw sell-price text), since a
      null parsed decimal cannot explain the failure to the exception-review user.
  - **2c.1b — Validation and exception-resolution service: complete.** `PriceBookImportValidationService`
    (commit `84d89b0`) implements the per-field validation rule engine (type/name/UOM/external-key/
    sell-price/currency/mapped-item), row-scale-safe persistence (single-row loads, count/projection
    queries, never the parent's full row set), and the locked Warning-only-`Accepted` /
    Warning-or-Error-`Skipped` / revalidated-`Corrected` exception-resolution policy.
    `PriceBookImportRow.ApplyCorrection` replaces the prior bare `ResolveCorrected()` flip with an
    atomic, caller-revalidated correction. Row mutations check the parent import's lifecycle (`Staged`
    for validation; `Staged`/`Validated` for resolution/correction, rejecting `Discarded`/`Published`/
    `PublishFailed` with a dedicated error), and `ApplyCorrection` rejects any revalidated status other
    than `Valid`/`Warning` (including out-of-range enum casts) and rejects a `Valid` result carrying
    messages.
  - **2c.2a — `IBusinessDocumentStorage` seam and R2 adapter: complete.** `IBusinessDocumentStorage`
    (Foundation.Application) plus its R2 production adapter and a Development-only local-disk fake
    (Foundation.Infrastructure) are in place (commit `e3eb142`). Opaque key generation/validation is
    centralized in `BusinessDocumentObjectKey` so `DeleteBestEffortAsync` can never act on a key not
    generated for the given account/purpose. DI registration is fail-closed: local disk is wired only
    in Development when R2 config is absent; every other environment throws at startup without real
    R2 configuration. `AWSSDK.S3` added to Foundation.Infrastructure. Locked for the next batch:
    5 MiB max source file size, 5,000 max data rows (streamed enforcement, not trusted
    `Content-Length`); R2 bucket CORS restricted to `https://app.ophalo.com` (prod) and
    `http://localhost:5173` / `http://localhost:3000` (local dev), no wildcards, `www.ophalo.com`
    excluded; V1 upload is an authenticated multipart POST to the .NET API which streams to R2 (no
    presigned URLs/callback in this batch, Vercel not in the data path); upload stages `Pending` rows
    without synchronously invoking 2c.1b validation; `DocumentPurpose` defines only
    `PriceBookImport`.
  - **2c.2b / 2c.3 — CSV upload, parser, and import review: deferred from MVP (2026-08-02).**
    ADR-472 records the product pivot: price-book data is entered and curated directly in Keep;
    generic contractor CSV ingestion is not a pilot capability. Do not implement the parser,
    upload endpoint, import-review UI, `OpenReadAsync` solely for CSV, CsvHelper, import limits, or
    CSV-specific R2 retention/cleanup behavior. A bounded cleanup session now removes the existing
    unexposed import entities, validation/persistence surface, tests, and schema before pilot, using
    the deployment-history-safe migration strategy in ADR-472. A later evidence-led revisit starts
    with one documented Ophalo template and review, never arbitrary spreadsheet ingestion.
  - **2c.cleanup — remove deferred import foundation: complete (2026-08-02).** Deployment audit
    confirmed `20260802101257_PriceBookImport` (committed in `65feb71`) was unshared — production
    Postgres `__OpHaloMigrationsHistory` had `0` rows for it and both import tables were absent — so
    the migration, its designer, and the matching `OpHaloDbContextModelSnapshot.cs` blocks were
    removed outright rather than forward-dropped. Removed all import-specific entities/enums/errors,
    EF configuration, persistence interfaces/implementations, `PriceBookImportValidationService`, DI
    registrations, and the `DocumentPurpose.PriceBookImport` branch (`DocumentPurpose` is now an
    empty enum pending the image slice's own purpose value). `IBusinessDocumentStorage`, the R2
    adapter, and the development storage fake are preserved unchanged for 2d/image work.
    `LocalDiskBusinessDocumentStorageTests.cs` was also removed — it only exercised the generic seam
    using `DocumentPurpose.PriceBookImport` as a stand-in value and could not compile against an
    empty enum; the image-storage session should add fresh seam tests once a real purpose value
    exists. No import tables, purpose branch, or public import route remain. Full suite green: 1309
    unit, 14 architecture, 983 integration; build and `git diff --check` clean. See ADR-472 and
    DEF-087.
  - **2d — Direct price entry and versioned atomic publish: complete** (`b5fcf3a`). Re-scoped
    the planned publish slice around Owner/Admin direct price entry rather than an import. It owns
    version/line creation, catalog pointer updates, and ADR-470's serializable account publish lock.
    ADR-473 locks the V1
    workflow: an existing Keep request is the quote boundary; a technician may capture proposed
    scope and an Owner/Admin may start the quote from that request; office alone owns price edits and
    internal approval; quotes are single-option and tax-included; labor is a normal Service catalog
    item; and off-catalog entries are single-use. Preserve the existing internal `Draft →
    SubmittedForApproval → Approved` lifecycle—there is no V1 customer `Sent`/`Accepted`/`Declined`
    state, signature, delivery link, tax calculation, or free-standing quote. It must not revive CSV
    upload as an implementation shortcut. Build Log 111 records the completed 2d.1–2d.2 delivery.
  - **2e — Price Book catalog workspace UI: complete (2026-08-09).** The entitled Owner/Admin
    workspace now includes top-level Price Book navigation, bounded catalog reads, a responsive
    Catalog Items list and item drawer, direct price entry, categories, aliases, lifecycle controls,
    and final desktop/mobile accessibility and interaction verification. Build Logs 112 and 113
    remain the locked decision and execution records. Offerings & Packages remains a separate
    functional slice; do not ship an empty/disabled tab.
  - **Price Book continuation (next preflight).** Build Log 117 breaks the unimplemented
    Offering/Assembly foundation, technician proposed scope and off-catalog capture, office
    review/catalog curation, internal quote foundation, actual-work/material records, and entitled
    web/mobile workflows into independently gated Session 3 slices. This completes the locked
    internal Price Book capability; it does not authorize customer quote delivery, dynamic pricing,
    or technician pricing authority.
  - **Price Book delivery-status clarification (2026-08-12).** The initial **Catalog Items**
    workspace (2e) is complete and closed. The later **Offerings & Assemblies**
    office-management delivery (3.2c + 3.2d) is also complete and ready for normal manual
    acceptance; it is not an unfinished part of the initial catalog workspace. The broader
    Price Book continuation—Proposed Scope, request-bound internal quote work, actual-work
    records, and related workflows—remains separately scoped future capability. The PWA UI
    quality program is therefore a presentation/usability correction of complete Catalog Items
    and Assemblies workflows, not completion work for missing Price Book fundamentals.
  - **PWA UI-quality correction — collaborative workflow begins (2026-08-12).** Christian and
    Codex first decide the bounded desktop/PWA change and review its visual effect with realistic
    data. Claude implements the approved code change. Codex then validates implementation against
    the agreed decision, existing behavior, tests, accessibility, and the UI-quality contract;
    Christian reviews the resulting real screen before the next change. The first surface is
    **Price Book: Catalog Items**, selected as the calibration workspace because it contains a
    clear page CTA, tabs, filters, data grid, and differentiated states with less workflow
    complexity than Requests. First candidate pass: desktop page-shell rhythm, filter-state
    clarity, table/row scan affordances, and CTA hierarchy; verify visually before applying the
    shared data-workspace pattern to Offerings & Assemblies or beginning Requests work. The
    correction is presentation/usability only: no catalog behavior, server contract, or mobile
    redesign is authorized by this pass.
  - **Price Book Catalog Items — approved first correction baseline (2026-08-12).** Visual
    review approved the single contextual catalog CTA, global-New-Request suppression on Price
    Book routes, bounded filter/table surfaces, truthful current-page result count, visible
    applied-filter/reset state, and one custom search-clear affordance. Long catalogs revealed
    that a CTA-only sticky control would be incomplete: users also need search, filters, status,
    and Price Book section navigation while browsing. The approved follow-up is one CSS-native
    sticky workspace bar containing tabs, filters, active-filter context/reset, and the
    contextual create action; no scroll-triggered JavaScript pop-in bar. A table-footer “Add
    another catalog item” affordance is intentionally deferred until the sticky pattern is
    reviewed with paginated data. Full rule: `docs/ux-design/pwa-ui-quality-system.md` D6.
  - **Price Book Assemblies — Step 2 pricing-summary preflight authorized (2026-08-13, no code
    yet).** Step 1 URL-addressable Price Book tabs/contextual assembly return is complete and
    deployed: `#/pricebook` is the canonical Catalog Items route;
    `#/pricebook?tab=assemblies` is the Assemblies route; Assembly Detail returns through
    **Back to Assemblies**. Step 2 is a backend/API-first preflight for an authoritative
    Owner/Admin Assembly Detail pricing summary. It must propose a read-model extension (preferred)
    versus a dedicated endpoint, exact DTOs, query/projection approach, test plan, and migration
    requirement; do not implement code in the preflight.

    Locked pricing rules: (1) **Summed** price is the current standalone sell price of the primary
    item plus each required associated item’s current standalone sell price multiplied by its
    quantity; (2) a missing standalone sell price on the primary or any required Summed associated
    item yields **Price needs review**, never a silent $0 total; (3) **All-inclusive** package price
    is the primary item’s current standalone sell price—there is no separate assembly override-price
    field; (4) a missing standalone price on that primary yields **Price needs review**; (5) pricing
    is server calculated from authoritative current price data, never recomputed by the frontend.

    Locked cost posture: catalog business cost remains optional and never blocks catalog/assembly
    creation, activation, or price completeness. Missing cost is a separate Owner/Admin
    profitability signal—e.g. **Margin needs cost review**—and must not be represented as a
    price failure. The preflight must identify required versus optional line treatment, authoritative
    missing-cost count/read shape, and whether phase one shows only readiness or also gross-profit,
    margin, and markup values. Optional-line presentation, including whether to show a separate
    optional-add-ons total, remains an explicit unresolved product decision; it must not be assumed
    or silently included in the base calculated sell price.
  - **Assemblies editor-containment direction (2026-08-13, implementation deferred).** Create
    remains a list-context-preserving slide-over drawer; existing assembly management remains the
    Assembly Detail page because it also owns lifecycle, eligibility, items, and the planned
    pricing/profitability summary. These are containment choices for one workflow—not separate
    editor concepts. After Step 2 establishes the final detail hierarchy, Edit must converge with
    creation on the same anatomy: Name → Primary catalog item → Price treatment → Associated items
    → Save/cancel and validation. Inactivate stays outside edit mode. Do not convert management to
    a drawer merely for visual symmetry. Full rule: `docs/ux-design/pwa-ui-quality-system.md` D6.
  - **Assemblies Step 2 pricing-summary contract review (2026-08-13).** Approved the preflight's
    existing-Assembly-Detail-endpoint extension, server-authoritative calculation, nested summary
    object, and no-migration posture. Corrected one material margin rule: all-inclusive package
    *customer price* is primary-only, but margin readiness/cost basis includes primary plus every
    required associated item for both price treatments. Project `PricingMode`, `SellPriceSnapshot`,
    and `CostSnapshot` independently for every current referenced line—NoStandalonePrice may still
    carry business cost. Optional items are excluded from phase-one totals/readiness. The summary
    returns structured, catalog-item-linked review reasons so lifecycle eligibility, price
    completeness, and margin completeness remain distinct and actionable. Repair navigation uses
    the existing hash router (`#/pricebook/{itemId}?returnToAssembly={assemblyId}`) rather than a
    new path scheme; no inline catalog editing on Assembly Detail. Delivery is split: backend/read
    contract + types/tests first, then a separately reviewed Assembly Detail repair UI batch. Full
    contract: `docs/ux-design/pwa-ui-quality-system.md` D6.
  - **Assemblies Step 2 pricing-summary — Batch 1 and Batch 2 complete (2026-08-13).** Batch 1
    (backend/read contract): `OfferingAssemblyDetail` gained a server-computed `Pricing` summary
    (`AssemblyPriceStatus`, `AssemblyMarginStatus`, structured item-linked `AssemblyPricingReason`s)
    on the existing Assembly Detail endpoint; no migration; `LoadCatalogItemLookupAsync` now
    projects `SellPriceSnapshot`/`CostSnapshot` independently of `PricingMode`, and
    `HasStandalonePrice` requires a non-null `SellPriceSnapshot`. Summed price reasons are
    exhaustive (primary and every required component checked independently; a missing primary
    price no longer short-circuits component checks). 66 integration tests pass. Batch 2 (repair
    UI): Assembly Detail renders the pricing/margin summary and separate Lifecycle/Price/Margin
    issue groups with catalog-item-linked review links (**Review price** / **Review cost** —
    explicitly cost- vs. price-oriented, not generic); the repair loop uses the existing hash
    router (`#/pricebook/{catalogItemId}?returnToAssembly={assemblyId}&returnToAssemblyReason=...`)
    with `Back to assembly` returning via a real remount/refetch, never cache invalidation from the
    Catalog Item mutation. Catalog Item Detail's price-publish CTA is renamed **Update pricing &
    cost** (it edits both fields); a contextual banner appears only when arrived via a review link.
    Follow-up UX fix (2026-08-13): the publish form's save action was below the fold — the form now
    renders directly after the item header/summary (before Profitability/Aliases) and its
    Cancel/Update actions sit in a sticky bottom-of-viewport bar that is the form's last child, so
    normal document flow keeps it from ever covering the reason selector, error text, or below-cost
    confirmation. No new mutation path; existing audited-publish semantics (required reason,
    validation, below-cost confirmation, query invalidation) are unchanged. 387 frontend tests pass;
    `tsc --noEmit` and `git diff --check` clean. No backend files touched in the UX fix. The work
    is committed and pushed, but live acceptance remains blocked until the internal entitlement
    operator path can enable Price Book for the founder account.
  - **Pilot-required image storage (paused until Price Book live acceptance).** R2 remains required: price-book import is
    deferred, not private document storage. The next storage slice defines image metadata,
    account authorization, bounded direct API multipart upload, image type/size validation, purpose
    keys, retrieval/display, and pilot retention. Equipment/work images—not CSV—are the first
    production use of the R2 seam.

  ADR-471 locks the production document backend as private Cloudflare R2 through the shared .NET
  `IBusinessDocumentStorage` seam; local disk is not a pilot/production option. V1 exports are a
  later capability and must incrementally stream authoritative data rather than buffer a full CSV;
  they are not persisted.

## Immediate Production Access And Reliability Blockers

- **GAP-039b (P0): error capture and safe customer references.** Use Sentry's free errors-only
  offering for the browser and API. It is the selected pilot diagnostic tool because it provides
  grouped, release-aware browser/API crash capture and founder email alerts without a recurring
  vendor cost. Do **not** build a generic application `Errors`/exception database table: it would
  duplicate monitoring work, risk retaining PII/capability tokens, and is not a reliable record
  during a database outage. Health/configuration checks and the smoke-test tool are complete; no
  paid observability, replay, performance tracing, broad telemetry, or persistent staging
  environment before revenue.
- **Verify deployed routing and release configuration.** The QR-handoff host correction (OPS-009,
  commit `ce1ec40`) is deployed and Scan to call has been confirmed on a real device. Continue to
  validate the intended public/deep-link contract, Vercel environment variables, DNS/domain/cookie
  topology, and API deployment configuration before pilot access. **Confirmed domain contract:**
  `app.ophalo.com` = authenticated `ophalo-app` staff application; `www.ophalo.com` = public
  `ophalo-web` tracker and QR-resolver pages (`/keep/share-call`, `/keep/share-sms`, `/keep/r`);
  `api.ophalo.com` = API. See OPS-009 below — the API's `App:PublicBaseUrl` binding (not
  `AppBaseUrl`) is now the single source for these public-resolver URLs.
- **GAP-020 (P0): opaque desktop call-handoff.** The production QR now resolves on a real device.
  Remaining release evidence is cross-device verification: dialer/fallback, expiry/invalid-token
  behavior, cache headers, iOS Safari, Android Chrome, and a non-`localhost` phone-reachable
  environment.
- **GAP-016 (P0): complete phone-validation parity.** Native parity and the remaining manual
  browser/device verification are still required for the ADR-444 phone-input contract.
- **Requests onboarding-banner progression.** The Owner/Admin banner now identifies setup work,
  but its primary CTA remains “Set up request page” after that step is complete. Advance the CTA to
  the next incomplete core action (Quick Capture for the first customer request), then hide the
  banner after the public request page and first request are complete. Team remains optional.
- **GAP-052 / customer-update notification integrity (P0) — Complete.** The safety gate, durable
  prepare/confirm contract, responsive PWA flow, and production migration are implemented and
  committed (`94f61af`, `7c2f9e5`, `a1d9221`). Deployed workflow verification performed manually
  in production 2026-07-28: page-only update and detailed-voicemail no-clear behavior, voicemail
  `NextAttentionAtUtc` advancement, `call_requested` text/email non-clear vs. completed-call clear,
  and the full prepare→confirm flow (desktop QR, mobile direct `sms:`/`mailto:`, resume-after-reload)
  all passed. Confirmer-mismatch (cross-teammate fail-closed) was not verified — only one teammate
  account exists on the business; the server-side same-actor rule remains covered by the 0.11b
  domain/integration test suite.
- **Request List operating-contract recovery (P1).** ADR-449 and ADR-450 lock truthful Owner/Admin
  queue language/sections and row context. The existing safe activity-preview retrieval remains
  useful, but activity must supplement—not replace—the original request summary; internal-note
  presence requires a server-authorized cue without exposing note text.

## Open Product And Pilot-Readiness Work

### Quick Capture, Input, And Modal Safety

- GAP-016 / GAP-021: finish the remaining authenticated country-code parity and later native parity.
- GAP-019: complete the Request Detail presentation decomposition before further detail changes.
- GAP-026 / GAP-027: add a recoverable search-clear affordance and complete the remaining lifecycle
  row decision. GAP-017, GAP-018, GAP-022, GAP-023, and GAP-025 are complete for the PWA.
- GAP-028 through GAP-031 and the scoped GAP-024 modal-accessibility work are complete. GAP-032's
  shared modal/focus foundation is complete for Quick Capture and desktop call handoff; dirty-close
  policy and Request Detail modal extraction remain deliberately deferred.

### Public Trust, Pilot Support, And Go-Live Gates

- GAP-033: collect remaining deployed public-intake/tracker evidence, including actual browser
  intake submission, expired-tracker presentation, and the known-business OffSeason decision.
- GAP-037: deliver the founder/internal weekly value-report path.
- GAP-038: deliver authenticated Pilot Feedback plus Help & Updates, with required native parity
  before store submission.
- GAP-040: complete marketing accuracy, assets, legal/support links, and deployment-readiness work.

### Authenticated Workspace And Request Operations

- GAP-041 through GAP-046: fix first-load queue transition; add business context; decide/verify
  paging at real-work scale; expose history; clarify queue orientation; and make search/filter state
  visible and recoverable.
- GAP-047 through GAP-051: make priority failures visible; preserve deliberate tracker-share intent;
  bound follow-up prefill; add same-customer related-work context; and finish consistent North
  American phone formatting.

## Validated Work-Session Queue

Each remaining code session follows this gate before implementation:

1. **Claude preflight:** read the named tracker/ADR/build-log material and current implementation;
   return the precise files/data flow, proposed bounded change set, open decisions, regression plan,
   verification commands, and any stale-document or scope conflict. Do not implement during this step.
2. **Codex validation:** independently check the preflight against the repository and controlling
   decisions. The outcome is **validated**, **correct and resubmit**, or an explicitly framed product
   question for Christian. No code begins until the preflight is validated.
3. **Implementation and review:** keep the validated scope to one reviewable change set, add focused
   regression coverage, and run proportionate checks. Do not combine later sessions merely because
   files overlap. If new production scope appears, record it and stop for a decision.

Completed/manual rows below are historical evidence and do not need a new preflight. Every future
code row marked **Claude preflight required** does.

### Phase 0 — Restore a Safe Validation Loop

| Order | Session | Scope and completion gate |
|---|---|---|
| 0.1 | First production smoke account and sign-in baseline | **Complete (manual/provider task).** Railway PostgreSQL-URL support, explicit startup migration switch, and runtime port binding are committed (`6a63d86`, `79aee3f`, `de1a8b9`). Dedicated internal smoke account created through `/start`; email delivery, link exchange, `/auth/me`, and authenticated request-list load all verified. A missing production cursor-signing secret caused Requests-workbench polling failures during verification (OPS-007) and has been resolved. Normal Sign in with the same account confirmed working; the earlier generic Sign in error did not reproduce. |
| 0.2 | GAP-039a — API readiness and safe diagnostics | **Complete** (`c8dd1e8`, `d7d0ee2`, `8b165b2`). Server-generated correlation IDs (`X-Correlation-Id` + log scope), minimal `/health/live` and `/health/ready` (no dependency/config detail in the public body; DB outage logged internally), fail-fast startup validation for required production config, and release identity (`RAILWAY_GIT_COMMIT_SHA`) in the log scope. Also fixed a live diagnosability gap: Resend delivery failures were silently discarded — now logged (status code in `ResendEmailSender`, auth-code ID in `StartAuthService`/`SignInAuthService`) without exposing PII. 896/896 integration tests pass. **Deployment note:** startup now fails fast if required config is missing — before the next deploy, confirm Railway sets `ConnectionStrings__DefaultConnection` (not only Railway's own `DATABASE_URL`, which this code does not read directly), `App__PublicBaseUrl`, `Resend__ApiKey`, and `Resend__FromAddress` (must be an address on the verified `mail.ophalo.com` domain, e.g. `OpHalo <no-reply@mail.ophalo.com>`). |
| 0.3 | Email trust template foundation | **Complete** (`027cfdf`). Shared `AccountEmailLayout` (table-based HTML, retina logo + text fallback, single CTA, locked ADR-431 motto, Privacy/Terms/Contact footer, no tracking pixel/click tracking) applied to account-start, sign-in, and invite emails, each with distinct truthful intro copy (ADR-446). `IEmailSender.SendAsync` gained a `textBody` parameter so every account email now ships a real plain-text alternative, threaded through `ResendEmailSender`, `ConsoleEmailSender`, and all callers. Logo asset hosted at `https://www.ophalo.com/brand/ophalo-lockup-color.png`. 898/898 integration tests pass. Future customer-facing messages remain business-primary with OpHalo only as a quiet footer; out of scope here. |
| 0.4 | GAP-039b — Error capture and safe customer references | **Claude preflight required after the external prerequisite.** First provision two free Sentry projects (browser/PWA and API) and place only their DSNs in the respective deployment environment variables; do not put DSNs in source control. Then wire only unhandled errors: browser render/async failures and API unhandled exceptions. Attach the existing release identity and server correlation ID when available. Before send, remove authorization headers, cookies, magic-link codes, public-intake/page tokens and capability URLs, customer request text, names, phone numbers, emails, and free-text form/request data. Disable session replay, tracing/performance, profiling, logs/telemetry, and user-identifying context. Configure actionable new-issue/regression email to the founder, with a conservative free-tier quota/spend alert. Keep Railway/Vercel logs as the correlated investigation source. Return a safe opaque error reference (never exception text) only on unexpected user-visible failures where support needs one; preserve existing expected `ProblemDetails` contracts. Add focused tests proving scrubbers remove sentinel secrets/tokens/PII and that release/correlation metadata is attached. Do not create an application error/exception table, a dashboard, a paid Sentry plan, or a persistent staging environment. **External prerequisite:** the project DSNs and the founder alert destination must be supplied before wiring/deploy verification. |
| 0.5 | GAP-039c — Deploy smoke checks and runbook | **Complete** (`fd34af3`, corrected by `8b6f392`). Added the dependency-free Node smoke script, regression tests, and runbook. Routine mode checks health, sign-in trigger, `/auth/me`, and request-list load with a local-only smoke-session cookie; full mode uses a separately obtained email code and deliberately skips a new sign-in trigger so it cannot invalidate that code. Local mock coverage passes. First live script execution remains a non-blocking operational check; manual deployed-app smoke testing is complete. |
| 0.6 | GAP-020 deployment verification | **Complete.** Manual device verification found scanning the desktop call QR 404'd. **Root cause (OPS-009):** `KeepEndpoints.cs` built SMS/call handoff URLs from `App:AppBaseUrl` (resolving to `app.ophalo.com`, the authenticated `ophalo-app` staff host), but the public resolver pages (`/keep/share-call`, `/keep/share-sms`) are served by the separate `ophalo-web` deployment at `www.ophalo.com`. **Fixed** (`ce1ec40`): both handoff URL builders now use the existing `IOptions<MagicLinkSettings>.PublicBaseUrl` binding (`App:PublicBaseUrl`/`App__PublicBaseUrl`), matching the pattern already used for intake-sms handoffs and magic links; removed the dead `App:AppBaseUrl` config key and its `app.ophalo.com` fallback. New assertions in `KeepCallHandoffApiTests.cs` prove both handoff URLs use the configured public base URL and never the app host. 8/8 focused, 626/626 Keep-scoped integration tests pass; `git diff --check` clean. Christian confirmed the desktop QR now scans and resolves correctly in production, 2026-07-27. |
| 0.6b | Scan to text contact handoff | **Complete** (`3acb19e`). Added `Scan to text` (desktop QR) / `Text` (mobile direct `sms:`) beside `Scan to call`/`Call` in `CustomerContactStrip.tsx`, reusing the existing opaque SMS-handoff endpoint (`api.createSmsHandoff`) and the `CallQrModal` QR-modal pattern via a new `TextQrModal`. Desktop shows an editable SMS draft (defaulted to the customer-page link) with a QR minted from it; editing marks the QR stale and requires an explicit "Update QR" remint rather than silently reusing a QR for changed text. "Done — record this text" routes into the existing `onContactLaunched("outbound", "sms")` → Log External Contact flow; scanning/launching never itself records contact, clears attention, or claims delivery, and the QR token security model is unchanged. **Review caught and fixed a real race:** the initial automatic mint (unlike the button-gated remint) isn't blocked by `isMinting`, so it could resolve after an edit and silently redisplay a stale-message QR as current; fixed with an edit-version guard (`messageVersionRef`) that discards any mint response superseded by a later edit while still clearing the minting state. 6 new focused tests (`TextQrModal.test.tsx`) cover mint-on-open, opaque-URL-only QR content, stale/remint behavior, the race-condition regression (deferred-promise initial mint resolving after an edit), and mobile `sms:` href construction; full frontend suite 140/140, `tsc --noEmit`, CSS-token check, and `git diff --check` all pass. |
| 0.7 | Authenticated Sign in redirect consistency | **Complete** (`b81cbb6`). `/start` and `/signin` now share the `/auth/me` redirect logic; an authenticated visitor to `/signin` goes to the app, while an unauthenticated visitor sees the sign-in form. Production browser verification on 2026-07-24 confirmed sign-in email delivery, authenticated redirect to Requests, and unauthenticated redirect from the app to Sign in. `ophalo-web` still has no test runner, so this retains manual regression coverage. |
| 0.8 | Requests onboarding-banner next action | **Complete.** `RequestsOnboardingBanner`'s primary CTA now reflects the next incomplete core step: "Set up request page" → Settings `public-profile` while the request page isn't ready, then "Add your first request" → Quick Capture once it is. Banner-hide gating in `Requests.tsx` (both core steps complete, team optional) was already correct and untouched. `reviewCustomerPageComplete`/`shareIntakePageComplete` remain unused as completion signals. 6/6 focused tests pass (`Requests.onboarding.test.tsx`, updated fixtures + new CTA-progression test). |
| 0.9 | Customer-page intent and hierarchy | **Narrowed after verification, complete.** Preflight found the core start-new-request vs. existing-request-tracking hierarchy (explicit headlines, copy, layout, primary action) was already implemented on both `IntakeForm.tsx` and the tracker (`TrackerStatusCard.tsx`/`TrackerActionCard.tsx`): "Send update or question" primary, share demoted, cancellation visually separated. Only gap: added intake-page reassurance copy — "Already have a request? Check the private link {Business} sent you." — placed low, below the submit CTA; does not imply a public recovery mechanism. **Deferred:** the reciprocal "start another request" link on the tracker page requires exposing a business intake slug/URL in the `/keep/r/{pageToken}` API response, which `CustomerPageData` does not currently carry; not added this session (no backend contract change without an explicit decision) and `websiteUrl` was not substituted. Revisit only after a deliberate API/privacy decision on exposing that field. |
| 0.10 | Post-go-live workbench navigation UX | **Deferred as DEF-084.** Top-level app navigation currently remounts Requests, Getting Started, and Settings; this can look like a refresh and resets local page state. Requests queries have their own first-visit/loading and cached-return behavior. Do not change this during go-live stabilization. Revisit only with pilot evidence, preserving authentication/failure visibility and making an explicit decision before retaining unsaved Settings drafts. |
| 0.11 | GAP-052a — Customer-update notification integrity: safety gate | **Safety gate complete** (`94f61af`). A page-only update (`AddBusinessUpdate`/`AddBusinessUpdateWithStatus`) no longer sets first response or clears business-waiting attention. Detailed voicemail (`LogOutboundExternalContact`) no longer counts first response or clears attention; it now advances `NextAttentionAtUtc` to the next business day in the account's timezone, never pulling an already-later commitment earlier. Supersedes the voicemail effects in ADR-169/198/213/214 globally. The durable obligation/attestation continuation is complete in 0.11b. |
| 0.11b | GAP-052a (cont.) — Notification obligation/attestation domain/API | **Complete** (`7c2f9e5`). First pass shipped only an unlinked `ConfirmUpdateNotification` attestation and was rejected in review: it never validated the client-supplied `relatedUpdateEventId`, and had no durable prepared-by-actor record to enforce ADR-451's same-actor rule. Corrected to a real two-phase obligation model: `KeepRequest.PrepareUpdateNotification` (Sms/Email only) records a durable pending obligation — `PendingNotificationRelatedEventId`/`Channel`/`PreparedByAccountUserId`/`PreparedAtUtc` (new columns on `keep_requests`, mirroring the `NeedsShare` flat-field pattern) — plus a `NotificationPrepared` audit event; `ConfirmUpdateNotification` now requires an exact-matching unconfirmed pending obligation (same related event, same channel, same preparing actor) or fails closed (`NotificationNotPrepared` / `NotificationConfirmerMismatch`), and clears the pending pointer on success (blocking replay). New `IKeepRequestOperatePersistence.IsCustomerVisibleBusinessUpdateEventAsync` enforces server-side, at prepare time, that the related event is a same-request/same-account/customer-visible (`Visibility=All`, `MessageIntent=BusinessUpdate`) event — confirm trusts the already-validated pending pointer rather than re-querying. Also fixed a real pre-existing gap in `LogOutboundExternalContact` where `CallRequested` attention was clearable by text/email (now only a completed live call satisfies it). New `PrepareUpdateNotificationService` + `ConfirmUpdateNotificationService`, `POST /keep/requests/{requestId}/notification-preparation` + `.../notification-confirmation`. **Batch-size exception (Christian-approved):** 3 mutation-handler families (CallRequested fix, Prepare, Confirm — at the hard cap) across 15 hand-written production files (was 7 quoted initially) + migration artifacts + 5 test-fake interface-stub updates, forced by (a) the exhaustive `KeepRequestEventType` switch invariant in `KeepRequestDetailMapper`/`KeepCustomerPageMapper`, and (b) the corrected durable-obligation design Christian required after reviewing the first pass. 22 new domain unit tests (Prepare guards/overwrite, Confirm no-prep/wrong-event/wrong-channel/wrong-actor/replay/happy-path) + 14 new integration tests (random/wrong-request/wrong-event-type related-event rejection, cross-actor confirmer-mismatch, replay, full prepare→confirm happy paths) added to the existing `KeepRequestExternalContactTests.cs`/`KeepRequestExternalContactApiTests.cs` (no new test files). Keep-scoped suites: 962/962 unit, 626/626 integration passing; `git diff --check` clean. **OPS-008:** the `20260726213949_AddNotificationPrepareConfirmObligation` migration (adds `pending_notification_*` columns) shipped in code but was not applied to the production database before deploy, causing every request-list query to fail (`42703: column k.pending_notification_channel does not exist`) starting 2026-07-27. The migration has since been applied to production and the outage is resolved. |
| 0.12 | GAP-052b — Customer-update notification integrity: responsive PWA | **Complete** (`a1d9221`). Built the Owner/Admin post → prepare → confirm flow on `NotifyCustomerPanel.tsx`: desktop opaque SMS QR (reuses the existing `createSmsHandoff` primitive, not the ShareLinkModal ceremony), direct mobile `sms:`, `mailto:` for email, preference-guided but not hard-blocked channel choice, and an explicit "I sent it — Confirm" step — opening the draft never itself confirms. The panel only appears when a submission actually created a customer-visible event (`messageIntent: business_update`, `visibility: all`), wired from `BusinessSection.tsx`. Added a small, Christian-approved read-shape addition on top of the 0.11b contract: `KeepRequestDetailResult.PendingNotification` (`relatedUpdateEventId`, `channel`, `preparedAtUtc`, `canConfirmAsCurrentUser`, no raw preparer ID) so the panel resumes truthfully after reload/navigation and shows a correct non-confirmable state when another teammate holds the pending obligation — same-actor confirmation was already enforced server-side in 0.11b. Queue refresh is inherited for free from existing per-visit refetch (no separate cache-invalidation work needed). Out of scope, deferred: the historical "Notified via SMS/email" timeline badge (the underlying `relatedEventId` is already exposed on events for that future follow-up), templates, and multi-pending-notification design. 12 new focused frontend tests (`NotifyCustomerPanel.test.tsx`, `BusinessSection.notify.test.tsx`) plus 3 new assertions in the existing `KeepRequestExternalContactApiTests.cs` for the new field; full suites 962/962 unit, 626/626 integration, 134/134 frontend passing; `tsc --noEmit` and `git diff --check` clean. |

### Phase 1 — Shared UI Safety Foundations

| Order | Session | Scope and completion gate |
|---|---|---|
| 1.1 | GAP-028 — CSS token validation | **Complete** (`5dd45c7`). `BusinessSection.tsx` (`--ophalo-teal`) and `ShareLinkModal.tsx` (`--muted`) referenced undefined tokens; replaced with the approved `--keep-accent`/`--ophalo-canvas`. Added `web/ophalo-app/scripts/check-css-tokens.mjs`, wired into `build`, which fails on any undefined `var(--...)` reference in `ophalo-app/src` and on drift between `app.css`'s inlined `:root` block and `web/shared/styles/ophalo-tokens.css`. 6/6 new focused tests pass (`check-css-tokens.test.mjs`); confirmed the guard catches a reintroduced undefined-token regression. |
| 1.2 | GAP-029 — Status language and badges | **Complete** (`b1e67a4`). Added `web/ophalo-app/src/lib/requestStatus.ts` as the single status label/badge-variant source, imported by `RequestRow.tsx`, `request-detail/helpers.ts` (re-exported for `DetailHero.tsx`/`TimelineEvent.tsx`/`BusinessSection.tsx`), and `quick-capture/LookupResultView.tsx` (now uses `KeepBadge` instead of a fixed slate span). Retired all three per-surface duplicates, including detail's broken substring-based badge-variant heuristic. Locked labels preserved (`in_progress`→Active, `resolved`→Work completed, ADR-425/434); `pending_customer`→"Pending Customer" and `closed`→success variant (matches `resolved`, per ADR-050) were this session's terminology decisions, confirmed with Christian. 20 new focused tests (`requestStatus.test.ts`) lock all 9 statuses' label/variant plus fallback behavior; full suite 97/97 passing, `tsc --noEmit` clean. |
| 1.3 | GAP-030 / GAP-031 — Transient UI and error boundary | **Complete** (`101e9e9`). Added `web/ophalo-app/src/hooks/useCopyFeedback.ts` — catches clipboard rejection, tracks copied/failed id, replaces its timer on reuse, clears on unmount, and guards against a clipboard promise settling after unmount via `isMountedRef` (no state/timer work once gone). Refactored all six copy-timeout call sites onto it (`ShareLinkModal.tsx`, `RequestDetail.tsx` phone copy, `PublicLinkSection.tsx` raw/slug URL, `DetailPanels.tsx` phone/email), fixing two prior unhandled-rejection risks; added unmount cleanup for `RequestDetail.tsx`'s separate review-success timer; `CustomerPanel`'s icon-only copy buttons gained a dynamic `aria-label` plus an `aria-live="polite"` failure status line. Added root `ErrorBoundary.tsx` wrapping `<App />` in `main.tsx` — plain recovery card, Reload-only action, no exception message/data/stack trace ever rendered. 9 new focused tests (hook timer-reuse/unmount/post-unmount-settle cases; boundary render-throw/no-leaked-text/single-Reload-action cases); full suite 105/105 passing, `tsc --noEmit` clean. |
| 1.4 | GAP-032 / GAP-024 — Modal and focus contract | **Complete** (`d8574f2`). Added `web/ophalo-app/src/components/keep/KeepModal.tsx` — dialog semantics, initial focus (falls back to the panel itself, `tabIndex={-1}`, when there's no focusable child), a Tab/Shift+Tab focus trap, Escape-to-close, an explicit `backdropClosable` policy, and focus restoration to the pre-open trigger only when it's still connected and focusable. Applied only to `QuickCapture.tsx` and `CustomerContactStrip.tsx`'s `CallQrModal`, per this session's narrowed scope — GAP-032's dirty-close confirmation and the `RequestDetail.tsx` modal extraction remain deferred. Caught and fixed a real a11y bug along the way: an early draft nested the dialog panel inside the `aria-hidden` backdrop, hiding the whole dialog from the accessibility tree (surfaced by the existing Quick Capture regression suite); the decorative dim layer is now a sibling of the panel, not an ancestor. 11 new focused tests (`KeepModal.test.tsx`) cover initial focus, no-focusable-child fallback, Tab-trap wrap both directions, Escape, backdrop-click policy, and safe no-op focus-restoration when the prior trigger was removed or disabled; full suite 116/116 passing, `tsc --noEmit` clean. |

### Phase 2 — Quick Capture Reliability

| Order | Session | Scope and completion gate |
|---|---|---|
| 2.1 | GAP-016 / GAP-021 — Phone-entry contract | **Partially advanced (GAP-051 slice only).** Authenticated `ophalo-app` staff-facing phone entry/display now formats as-you-type and on read-only summaries as `(555) 555-5555`, matching the public intake form's readability, via shared `normalizeNaPhoneInput`/`formatNaPhone` utilities (`web/ophalo-app/src/components/quick-capture/utils.ts`) applied across `HandoffPanel`, `LookupGate`, `CaptureForm`, `LookupResultView`, `ShareLinkModal`, `RequestDetail`, `DetailPanels`, and (added in a follow-up fix) the business's own Customer-facing phone field in `CompanySection.tsx`. Canonical 10-digit values, API payloads, lookup, `tel:`/`sms:` targets, and copy actions are unchanged. See `docs/pilot-readiness-bug-tracker.md` GAP-051 for full detail. **Not done:** native parity and full country-code lookup compatibility remain open — this session does not close GAP-016, GAP-021, or Session 2.1. |
| 2.1b | GAP-016 — `ophalo-web` public-form leading-`1`/`+1` normalization parity | **Complete.** `ophalo-web`'s intake `formatPhoneAsYouType` (`IntakeForm.tsx`) silently truncated the real last digit of an 11-digit `+1`-prefixed typed/pasted number instead of dropping the country code, because it sliced to 10 digits before stripping a leading `1`. Fixed to strip a leading `1` first, mirroring the rule already locked and shipped in `ophalo-app`'s `normalizeNaPhoneInput`. Backend `PhoneNormalizer`/`KeepRequestInputValidator` was already correct and unaffected — this was a client-side data-integrity bug, not a validation gap. `ophalo-web` has no test runner; verified manually by Christian. Remaining GAP-016 work: native parity (blocked on the native app not yet existing) and the manual device-verification pass (2.4). |
| 2.2 | GAP-017 / GAP-022 / GAP-023 — Service location and draft safety | **Complete.** `CaptureForm.tsx`: (1) the address disclosure now requires line 1, city, and state client-side — an open disclosure with only some fields filled shows inline field errors and blocks submit instead of silently discarding the address (GAP-022, closing GAP-017's remaining client-handling gap; server-side validation was already in place). All supplied fields submit unconditionally once valid. (2) Identity fields (name/email) now default to a resolved customer's `prefill` ahead of a preserved draft, and a non-null prefill is fully authoritative for both fields — including when the newly resolved customer has no email on file, which previously left a prior customer's stale email populated (GAP-023). Non-identity draft fields (description, source, address) are unaffected and still restore from the draft. One existing unit test that encoded the old draft-over-prefill precedence as intended was corrected; 4 new focused tests added (`draft-preservation.test.tsx`, now 10/10). Full frontend suite 143/143, `tsc --noEmit` and `git diff --check` clean. Native parity remains open per GAP-017/GAP-021, tracked separately. |
| 2.3 | GAP-018 / GAP-025 — Self-service handoff and customer recognition | **Complete.** GAP-018 was already shipped (R88f-a/b/c, `a834cbe`/`5d5f502`/`7c2917a`) — this session corrected the stale tracker/build-log status to match. GAP-025: `LookupKeepRequestByPhoneService` now falls back to an account-scoped, exact-match `KeepRequest.CustomerPhone` query when the canonical customer lookup misses, returning a read-only `Prefill` (name/email) into Quick Capture's capture form with `customer` still `null` — no `KeepCustomer` backfill/link. The match query normalizes via a raw-SQL `regexp_replace(customer_phone, '[^0-9]', '', 'g')` predicate — digit-for-digit equivalent to `PhoneNormalizer` — with no candidate cap. Two earlier drafts were caught in review and replaced: a `Contains`+`Take(50)` prefilter that could miss a valid match behind enough decoy rows, and a chained `string.Replace` punctuation strip that only handled the write-path's allowed characters and would miss an unanticipated legacy separator. 4 new service-level tests plus 4 new persistence-level tests against real PostgreSQL (60-decoy and unanticipated-separator regressions); full backend Keep suite 960/960, Keep persistence proof suite 34/34, frontend suite 144/144, `tsc --noEmit` and `git diff --check` clean. See GAP-025 in `pilot-readiness-bug-tracker.md` for detail. |
| 2.4 | GAP-016 phone-input verification | **Complete (core cases).** Christian manually verified the ADR-444 contract in `ophalo-app` Quick Capture: bare 10-digit entry, leading-`1`/`+1` stripping (typed and pasted), and the Change-phone action preserving the full capture draft (name/email/description/source/address) on return from lookup. The GAP-025 legacy-phone-prefill path was intentionally not exercised — no legacy `KeepRequest.CustomerPhone`-without-`KeepCustomer` data exists yet in any environment (pre-production, no seeded/migrated legacy data). Verify that path once real or seeded legacy data exists. |

### Phase 3 — Request List Operating Experience

| Order | Session | Scope and completion gate |
|---|---|---|
| 3.0 | GAP-007a — Request List routine action recovery | **Complete** (`de9a0c2`). Routine non-terminal rows with no attention-driven promotion now retain no fabricated `Next:` cue but render up to two server-authoritative modal actions: **Update customer**, then **Log contact**. Existing attention/closeout/feedback promotion, server permission/concurrency authority, and the two-button cap are unchanged. Focused row tests cover both-action ordering and one-action/no-invention behavior; `tsc --noEmit`, full PWA tests, token check, and `git diff --check` pass. `add_internal_note` remains detail-accessible until a separately designed compact-menu pass. |
| 3.0b | GAP-007b — Request List safe latest-activity retrieval | **Complete** (`9c35dec`). The bounded, server-selected activity-preview retrieval is available for each list page. ADR-450 supersedes only its prior display rule: original request context must remain stable; safe latest activity is secondary; note presence is a separate server-authorized cue. |
| 3.0c | ADR-450 — Request List row-context contract | **Complete** (`61a99ac`). [Build Log 092](build-log/092-request-list-row-context-handoff.md) implemented as validated, with an approved 13-file exception to the 12-file batch gate recorded there. `originalSummary` (mapped from `KeepRequest.Description`, no new persistence read), a nullable `latestActivity`, and a server-authorized `hasInternalNote` presence cue (new account-scoped, content-free EXISTS batch read gated by `Keep.InternalNotesAdd`) replace the prior description-fallback preview. `RequestRow` uses sibling real `<button>`s (not a `div[role=button]` wrapping a nested button) and `Requests.tsx` uses a composite row key so local expansion state resets on tab/filter/search/page changes. 968 Keep unit tests, 636 Keep integration tests, 155 PWA tests, `git diff --check` pass. |
| 3.0d | ADR-449 — Owner/Admin work-queue hierarchy | **Complete.** `Requests.tsx`: `Default Queue` renamed `All work`; Owner/Admin page heading is `Requests for {Business name}` (sourced from the existing `getSetup` contract, gated on `role === "owner" \|\| "admin"`; Operator/Viewer keep the plain `Requests` heading and never fetch setup); the locked ADR-449 subtitle renders only on the `All work` tab, blank elsewhere. Within `All work`, rows split into a quiet `Needs attention` header (rendered only when a matching row exists) followed by `Open work`, using the server-authoritative `rowContext`/`RankingOrder` already on the wire — the client partitions the existing order, it never resorts or fabricates membership. **Review caught a real contract gap and required a backend fix:** `GetKeepRequestListService.ComputeRowContext` classified an overdue first response as `"first_response"` instead of `"needs_attention"`, so an overdue (red/urgent) row would have silently landed in the quieter Open work bucket, contradicting `ComputeRankingGroup`'s own `overdue_business_waiting`/order-1 bucket and the Build 087 red-overdue-equals-Needs-Attention contract; fixed so `firstResponseOverdue` returns `needs_attention`, non-overdue pending first response still returns `first_response`. New regression test proves the overdue case now lands in `needs_attention`; the existing pending-response test continues to prove the non-overdue case stays `first_response`. 9 files (5 production: `apiClient.types.ts`, `mockApiClient.ts`, `fixtures.ts`, `Requests.tsx`, `GetKeepRequestListService.cs`; 4 test: `RequestRow.test.tsx`, `Requests.onboarding.test.tsx`, new `Requests.sections.test.tsx`, `KeepRequestListServiceTests.cs`) — within the batch gate, no exception needed. 180/180 `KeepRequestListServiceTests`, 969/969 Keep unit, 636/636 Keep integration, 160/160 PWA, `tsc --noEmit`, CSS-token check, `git diff --check` all pass. |
| 3.0e | Customer-update template strategy | **Deferred decision.** Do not build starter, custom, or business-managed message templates while Request List recovery and GAP-052 notification integrity remain open. Revisit only after the Request List is working as locked and pilot evidence shows repeated owner-authored update language. |
| 3.1 | GAP-041 / GAP-026 — First-load queue and search affordance | **Complete.** `Requests.tsx`: first selection of an unvisited queue (including Available) now renders a fixed 5-row queue-agnostic skeleton (`RequestRowSkeleton`, mirroring `RequestDetail.tsx`'s existing pulse pattern) instead of collapsing the region to a `Loading…` blob; header, tab bar, and search row stay mounted throughout, and an `sr-only` announcement carries the loading state since the skeleton itself has no text. Cached-tab revisits remain immediate (unchanged `isLoading`-only gating). Tab bar gained a real roving-tabindex keyboard pattern — `tabIndex={isActive ? 0 : -1}`, Left/Right (wrapping) and Home/End move focus and selection together, Enter/Space keep native `<button>` activation — locked to the narrow scope (no `aria-controls`/`tabpanel` wiring, since the page has one dynamically replaced content region, not persistent per-tab panels). GAP-026: added a visible, keyboard-usable clear (`X`) button inside the search input, shown whenever `draftQ.length > 0`, resetting `draftQ`/`q`/`cursor`/`cursorStack` in one action and returning focus to the input. 2 files (1 production, 1 new test file `Requests.queueTransition.test.tsx` — 5 new tests covering first-load skeleton, cached-tab immediacy, arrow/Home/End navigation, Enter/Space activation, and clear-button behavior). Full frontend suite 165/165, `tsc --noEmit`, CSS-token check, `git diff --check` all pass. No backend changes. |
| 3.2 | GAP-043 / GAP-044 — Paging and history | **Complete.** GAP-043: retained the existing 50-row cursor model; `Requests.tsx` adds a truthful numbered range ("Showing 1–50", never "of N" — computed from `cursorStack` depth × `limit`, valid under the existing fixed-limit/short-final-page contract), an explicit "End of results" state, and post-page-change scroll+focus placement. `goNextPage`/`goPrevPage` scroll the actual list-region container (not the window) to top immediately, then a `pendingPageFocusRef` + `useEffect` defers moving focus to the range heading until the new page has actually rendered (`!isLoading && !isError`) — an earlier draft focused immediately and was corrected in review, since that landed on the stale prior range and then stranded focus on an empty/loading heading. The heading is conditionally rendered only when it carries a meaningful label (loading/range/empty-state text — never a blank node in the outline) but stays the same stable DOM node across loading→loaded. GAP-044: added a demoted, non-competing Owner/Admin "History" entry point (not a peer tab) into the already-implemented `closed_history`/`cancelled_history`/`all_history` contract, with Closed/Cancelled/All scope and Today/Yesterday/This week/All time date scope — Today is sent as explicit `closedFrom`/`closedTo` (UTC midnight, exclusive upper bound, matching the server's own `ResolveClosedShortcut` convention) since the backend shortcuts only cover yesterday/this_week; no backend change. Search and pagination retain the selected history view/date scope and never silently return to active queues. **Corrected in review:** presentation (context label, subtitle, empty state, `Needs attention`/`Open work` split, feedback-review focus, search placeholder) now derives from the server's own `listContext.isHistory` once a response has loaded (`presentAsHistory`), falling back to the client's `historyMode` only before the first response arrives or as loading/navigation intent (which chrome to render, which view/date params to request) — an earlier draft drove all presentation from `historyMode` alone. Narrow navigation-round-trip behavior: returning from Request Detail resets to history page one/default scope, exactly as every other queue tab already does; full list-state/URL preservation across detail navigation is deferred to its own later session, not attempted here. 6 files (3 production: `apiClient.types.ts`, `apiClient.ts`, `Requests.tsx`; 3 test: updated `Requests.sections.test.tsx`, new `Requests.pagination.test.tsx` (4 tests), new `Requests.history.test.tsx` (7 tests, including a contrived-mismatch test proving presentation follows the server signal, not client state)). Full frontend suite 176/176, `tsc --noEmit`, CSS-token check, `git diff --check` all pass. No backend changes. |
| 3.2a | Requests workspace decomposition | **Complete.** [Build Log 093](build-log/093-requests-workspace-decomposition-handoff.md) was implemented as a no-behavior, frontend-only refactor: `Requests.tsx` was reduced from 912 to 432 lines while remaining the sole query/state/navigation controller. Pure list configuration/history helpers moved to `requestsWorkspace.ts`; the static skeleton, content region/pager, queue/history navigation, toolbar, and header/onboarding/summary-pill presentation moved to five bounded `components/requests/` files. Query keys, cache/polling, API shapes, permissions, routing, exact list/history/paging/accessibility behavior, and visual copy/classes are unchanged; no reducer/state-model redesign was introduced. 7 production files, within the batch gate; full frontend suite 176/176, `tsc --noEmit`, CSS-token check, and `git diff --check` pass. |
| 3.3 | GAP-046 — Filter-state visibility and recovery | **Complete.** [Build Log 094](build-log/094-request-list-filter-state-recovery-handoff.md) implemented the locked frontend-only slice: a quiet conditional `Applied:` line reports submitted—not merely drafted—criteria; filtered/history empty states distinguish no matches from a truly empty queue; the existing polite result heading carries truthful range/empty + submitted criteria wording; and `Clear filters` appears only in a filtered empty state. Normal search X/status controls remain independent. Operational recovery clears submitted/draft search, status, and paging; history recovery clears search/paging while retaining its selected scope/date. 4 production files plus 1 focused test file (9 new tests), within the batch gate; full frontend suite 185/185, `tsc --noEmit`, CSS-token check, and `git diff --check` pass. No backend/API/query-key/cursor behavior changed. |
| 3.4 | GAP-027 — Lifecycle scan cue | **Complete.** [ADR-452](decisions/ADR-452-request-list-lifecycle-cue-is-the-status-chip.md) locks the existing single status chip + at most one deterministic exception pill (Build 087) as the truthful lifecycle cue — no milestone strip; a stepper would add scan noise, imply a false linear pipeline for non-linear service work, and carry pre-launch regression risk without answering "current state"/"needs attention" faster. [Build Log 095](build-log/095-gap-027-lifecycle-cue-verification.md) verified all three required criteria (single cue/exception, server-authoritative priority, count/row reconciliation) are already implemented (Build 087; session 3.0d) and already covered by `RequestRow.test.tsx`/`KeepRequestListServiceTests.cs` — no new production files, no `RequestRow.tsx` changes. Full-suite verification also surfaced two integration tests asserting pre-ADR-451 behavior (unrelated to GAP-027): `AddBusinessUpdateTests`/`AcknowledgeAttentionTests` asserted a page-only business update sets first response / clears business-waiting attention; ADR-451 already locked the opposite, and the production domain code was already correct — only the tests were stale. Both corrected to assert preservation, matching the existing `SilentStatusChange_DoesNotClearBusinessWaitingAttention` shape. Verified: frontend 185/185, `tsc --noEmit`, CSS-token check; backend unit 1,207/1,207; architecture 14/14; integration 933/933 (was 931/933 before the fix). |
| 3.5 | Request List queue context and first-visit chrome continuity | **Complete.** `requestsWorkspace.ts` adds a pure `getQueueSubtitle(tabId, role)` lookup rendering a locked, truthful subtitle for each operational queue in the existing `pageSubtitle` slot (`Requests.tsx`) — Assigned to Me: "Requests currently assigned to you."; Operator's My Promises: "Your active customer promises — the requests assigned to you."; Needs Attention: "Requests with customer promises needing attention now." (no "your" — Owner/Admin/Viewer see it account-wide while Operator sees only their own MyWork scope, same tab/label, different server scope by role); Watching: "Requests you're watching."; Ready to Close: "Resolved work ready for owner/admin closeout."; Feedback Review: "Closed requests with customer feedback awaiting review." All work and history keep their existing locked subtitles unchanged. Count continuity: the `latestCounts` effect that propagates `listQuery.data?.viewCounts` up to `App.tsx`'s universal `viewCounts` state now only fires `onViewCountsUpdate` when `latestCounts` is truthy — previously it unconditionally propagated `null` while an unvisited tab's query was loading (React Query resets `data` to `undefined` on a new queryKey with no `placeholderData`), briefly wiping tab-bar/summary-pill counts before the new queue's first response arrived. 2 production files, 1 new test file (`Requests.queueContext.test.tsx`, 3 tests: full Owner/Admin subtitle sweep across all 5 tabs plus All work, the Operator label variant, and a deferred-response test proving a known non-null count is never replaced with null mid-load). No query-key, ranking, count-computation, or `RequestRow` changes. Full frontend suite 188/188, `tsc --noEmit`, CSS-token check, `git diff --check` all pass. No backend changes. |
| 3.5a | Request List empty-state duplication fix | **Complete.** On an empty operational queue, the small quiet list-region heading (`pageHeadingText`, e.g. "Nothing assigned to you") rendered identically above the centered visible empty-state heading + detail, a visible duplicate. `RequestListContent.tsx`'s `<h2>` now gets `sr-only` applied only when `!isLoading && !isError && rows.itemCount === 0` — exactly the case where its text duplicates the visible empty-state heading below it; loading and non-empty range text stay visible as before. The element's `ref`/`tabIndex={-1}` and `aria-live="polite"` containment are unchanged, so the post-page-change focus target and truthful polite announcement contract are preserved even while visually hidden. The 3.5 subtitle-exclusivity contract (`pageSubtitle`'s existing All-work-vs-`getQueueSubtitle` ternary) was already correct — no fix needed there, only regression coverage. 1 production file, 1 test file extended (`Requests.queueContext.test.tsx`, +3 tests: an All-work-subtitle-never-appears-elsewhere sweep, and two no-duplication tests — Assigned to Me and Feedback Review — each asserting exactly one visible + one `sr-only` heading match). Full frontend suite 191/191, `tsc --noEmit`, CSS-token check, `git diff --check` all pass. No backend/API/query-key/ranking/count-computation/`RequestRow` changes. |

### Phase 4 — Request Detail Reliability And Continuity

| Order | Session | Scope and completion gate |
|---|---|---|
| 4.1 | GAP-019 — Request Detail decomposition | **Complete** (Codex implementation, 2026-07-28). Frontend-only controller/presentation extraction: `RequestDetail.tsx` remains the sole query, mutation, state, navigation, focus/scroll-policy, and overlay controller. Added `RequestDetailHeader` (breadcrumb/queue callback forwarding), `RequestDetailStates` (loading/error/retry), `RequestDetailActivity` (filter/timeline callback forwarding), and `RequestDetailContent` (main column + desktop sidebar composition). The two Request Detail form modals and `US_STATES` deliberately remain controller-owned; their eventual extraction follows the deferred GAP-032 dirty-close/modal-policy work, so this is not the final `RequestDetail.tsx` shape. No backend, DTO, route, query-key, cache, permission, or behavioral changes. Frontend `tsc --noEmit`, 25-test-file / 191-test suite, and `git diff --check` pass. **Process note:** implementation began after a preflight supplied for approval rather than an explicit go-ahead; the resulting scoped change was retained and recorded at the user's direction. |
| 4.2 | GAP-047 / GAP-048 / GAP-049 — Mutations, sharing, and follow-up bounds | **Complete** (2026-07-28). GAP-047: `TriagePanel`'s internal-priority `<select>` (`DetailPanels.tsx`) now carries a full submitting/conflict-disabled/error state — disabled while saving and permanently after a `409`, showing a dedicated conflict message (`role="alert"`) and reverting to the server value on any other failure, matching the established `UnifiedComposer` note pattern. GAP-048: `CustomerContactStrip`'s Email quick action is now a bare `mailto:{email}` with no prefilled subject/body — the private tracker link is shared exclusively through `ShareLinkModal`'s explicit prepare/confirm ceremony; SMS/QR handoff paths are unchanged. GAP-049: new `buildFollowUpDescription` helper (`request-detail/helpers.ts`) truncates only the copied original text — never the `Follow-up to closed request {ref}: ` prefix — to fit the shared `DESCRIPTION_MAX_LENGTH` (4000, matching the .NET validator), preferring the last whitespace boundary and reserving room for an ellipsis; `QuickCapture`/`CaptureForm` thread a `wasTruncated` flag through to show "The prior request description was shortened to fit. Please review before creating this follow-up." below the field, and the textarea now enforces `maxLength={4000}`. 7 production files, 4 test files (11 total, within the 8-production/12-total gate); no backend/DTO/route/query-key changes. **Process note:** the first GAP-047 draft momentarily stopped resetting `pendingPriority` on the success path, which would have pinned the display to the optimistic value instead of following live server updates — caught and fixed before commit. Frontend suite 28 files / 201 tests passing (was 191), `tsc --noEmit`, CSS-token check, `vite build`, and `git diff --check` all pass. |
| 4.3a | GAP-051 close-out — remaining raw phone captions | **Complete** (2026-07-28). Full-authenticated-surface audit found 3 remaining raw, unformatted `customerPhone` display spots the prior GAP-051 session missed: `CustomerContactStrip.tsx`'s `CallQrModal` and `TextQrModal` captions, and `NotifyCustomerPanel.tsx`'s SMS-notify QR caption. Each file now imports and applies the existing `formatNaPhone` helper to its caption only — no `tel:`/`sms:`/handoff-URL value changed. 2 production files, 2 test files (existing `TextQrModal.test.tsx` and `NotifyCustomerPanel.test.tsx` extended with 3 new formatted-caption assertions). Frontend suite 28 files / 204 tests passing (was 201), `tsc --noEmit`, CSS-token check, `vite build`, `git diff --check` all pass. |
| 4.3b | GAP-050 — Related-work backend read path | **Complete.** Dedicated `GET /keep/requests/{requestId}/related-work` endpoint (not a `KeepRequestDetailResult` field) — deliberately decoupled from the 18 existing mutation call sites into `KeepRequestDetailMapper.ToDetailResult`, since folding it into the detail response would make every mutation's response silently overwrite the frontend's cached related-work with `null` (`onDetailUpdated`'s `queryClient.setQueryData`). New `GetKeepRequestRelatedWorkService` mirrors `GetKeepRequestDetailService`'s auth/scope preamble exactly (same not-found boundary for cross-account/row-inaccessible requests, same Owner/Admin/Viewer=AccountWide vs Operator=MyWork mapping) and reuses `IKeepRequestDetailPersistence.GetRequestAsync` to resolve the anchor request's `KeepCustomerId` before querying `GetOtherCustomerRequestsAsync` (new method, reuses `KeepRequestRowQueryFactory.Apply` for scope, excludes the anchor request and Cancelled/Spam/Test — Spam/Test exclusion Christian-approved as classified/non-operational records). **Corrected in review:** the first pass loaded every eligible related request into memory and ranked/capped/counted in the service — a scalability risk for a prolific customer. Ranking (`Max(CreatedAtUtc, LastBusinessActivityAt, LastCustomerActivityAt)`, expressed as nested comparisons rather than `Enumerable.Max` so it translates to SQL — not a `??` coalesce, so a later customer touch correctly outranks an older business touch), the deterministic `ThenBy(Id)` tie-break, and `Take(3)` now all execute in the database query itself; a separate `CountAsync` over the same filtered `IQueryable` gives an exact total without materializing the capped rows. The persistence method now returns a `KeepRequestRelatedWorkQueryResult(TotalCount, Items)`; the service only maps status/forwards fields, no in-memory ranking left. 5 production files (`IKeepRequestDetailPersistence.cs`, `EfKeepRequestDetailPersistence.cs`, new `GetKeepRequestRelatedWorkService.cs`, `KeepEndpoints.cs`, `KeepServiceCollectionExtensions.cs`) + 6 test files (4 existing per-file fakes updated with a signature-matching `NotImplementedException` stub — mechanical, no behavior change; new `GetKeepRequestRelatedWorkServiceTests.cs` covering not-found passthrough, role→scope selection, the `take=3` parameter, and result-mapping pass-through; new `KeepRequestRelatedWorkApiTests.cs` proving the full contract against real PostgreSQL — including that the SQL-translated ranking expression actually works: cross-account/unknown-id 404, status inclusion/exclusion, Operator MyWork exclusion of a sibling the Operator doesn't participate in, cap-at-3 with total reflecting all 7 eligible, later-customer-outranks-older-business ranking, and tie-break). Caught and fixed two test-authoring bugs during verification, not production defects: (1) a tie-break assertion wrongly assumed `Guid.CreateVersion7()`'s creation-time ordering survives `Guid.CompareTo` — .NET's default comparer uses the internal little-endian field layout, not the RFC 4122 byte string, so it isn't chronological; fixed to assert against the same ordering the production code actually uses. (2) the tie-break fixture assumed two separately-saved rows would tie on `CreatedAtUtc` — true only under `KeepPersistenceProofTests`' frozen fake clock, not `KeepApiWebFactory`'s real clock; fixed by forcing an identical explicit `LastBusinessActivityAt` on both rows. 1,214/1,214 backend unit tests, 643/643 Keep-scoped integration tests, 14/14 architecture tests, `git diff --check` all pass. No frontend changes — the panel/navigation consumer is 4.3c. |
| 4.3c | GAP-050 — Related-work panel and navigation | **Complete.** Consumes the 4.3b endpoint via a new `apiClient.getRelatedWork` method and `KeepRequestRelatedWorkResult`/`Item` types (camelCase, matching the API's JSON casing). New `RelatedWorkPanel` in `DetailPanels.tsx` self-fetches with its own `useQuery` (independent of the detail-query cache, matching 4.3b's decoupling rationale), renders nothing for single-request customers or query errors, and displays each row's status through the existing `statusLabel`/`statusBadgeVariant` helpers rather than raw backend codes. `onNavigate` was added only to `RequestDetailContentProps` (not the shared `RequestDetailLayoutProps` used by desktop/mobile layouts) and is optional end-to-end: without it the panel issues no query and renders nothing, matching the header's existing no-callback behavior. Rendered once in the shared main column (`RequestDetailContent.tsx`), not duplicated across layouts. 5 production files (`apiClient.types.ts`, `apiClient.ts`, `DetailPanels.tsx`, `RequestDetailContent.tsx`, `RequestDetail.tsx`) + 1 new test file (`DetailPanels.relatedWork.test.tsx`: no-callback/no-query, zero-related, capped display with label assertions, click navigation, pending-query, and rejected-query quiet-degradation). Frontend suite 29 files / 210 tests passing, `tsc --noEmit`, `vite build`, `git diff --check` all pass. GAP-050 is now fully delivered (4.3b backend + 4.3c frontend). |
| 4.4 | GAP-042 — Authenticated workspace identity bootstrap | **Complete** (2026-07-29, committed `e7fe9f8`, pushed to `origin/main`). Final entry of Phase 4 — no 4.5. Supersedes the prior `KeepRequestListContext.BusinessName` design (session 4.4 first pass): that approach coupled the title to the first List response and broke the locked GAP-041 first-load skeleton contract (title must render before list data resolves). The uncommitted `KeepRequestList*`/`Requests.tsx` list-context hunks, their tests, and the detail-header `detail.businessName` wiring were removed via a named stash (`4.4-abandoned-list-context-businessname`, recoverable, not deleted) before this pass began. **Corrected architecture:** `/auth/me` is now the sole authenticated workspace-bootstrap source for `businessName`, independent of any List/Detail data query. Backend: `IMemberManagementPersistence`/`EfMemberManagementPersistence.GetAuthenticatedWorkspaceIdentityAsync(accountUserId, accountId, ct)` replaces the old role-only `GetAccountUserRoleAsync`, matching both IDs (returns null — never another account's data — on any mismatch) and returning `(Role, BusinessName)` in one read; `AuthEndpoints.Me` adds `businessName` (nullable, no substituted `""`) to the existing `/auth/me` response, all other fields unchanged. Frontend: `MeResponse.businessName: string | null`; `Requests.tsx` drops the Owner/Admin-gated `["setup"]`/`api.getSetup` title query in favor of `useQuery(["me"])`, extended to Owner/Admin/Operator (Viewer still routes to `AccessLimited`, never mounts this page); `RequestDetail.tsx` queries the same `["me"]` cache and passes `businessName` to `RequestDetailHeader` (every role that can reach Detail, Viewer included via direct link) — `detail.businessName` is preserved in the DTO for its other consumers but no longer sources header chrome; `RequestDetailHeader.tsx` renders it as truncating chrome next to `referenceCode`, quiet when absent, Prev/Next unaffected; `CompanySection.tsx`'s `updateProfile` success path keeps its existing `["setup"]` cache set, immediately patches `["me"]`'s cached `businessName`, then invalidates `["me"]` to reconfirm server authority (no title flicker). Because `["me"]` is independent of the list query, the GAP-041 first-load skeleton contract (`Requests.queueTransition.test.tsx`) now passes without modification to its assertion — the title resolves from `["me"]` while the list response is still deferred. 8 production files (3 backend + 5 frontend) + 6 test files (`AuthApiTests.cs` — extended `MeBody`/assertion plus a new cross-account `GetAuthenticatedWorkspaceIdentityAsync` null-leak proof; `Requests.sections.test.tsx` and `Requests.queueTransition.test.tsx` — added `getMe` mocks, Operator now asserted to see the business-name heading same as Owner/Admin; `CompanySection.phone.test.tsx` — extended with a `["setup"]`/`["me"]` cache-sync-on-save proof (patches in place, then invalidates); new `RequestDetailHeader.businessName.test.tsx`; `fixtures.ts` — `mockMeByRole` gets `businessName`), 14 total changed files — two over the 12-file gate: this session-log entry (required documentation, Christian-approved exception) and `RequestDetailHeader.businessName.test.tsx` (no existing test file covered that component, and the header-rendering proof was an explicit required deliverable — flagging per Christian's direction rather than dropping the test to fit). Backend: 33/33 `AuthApiTests`, 32/32 `MemberManagementTests`, full solution builds clean. Frontend: 30 test files / 215 tests passing (was 210), `tsc --noEmit`, CSS-token check, `vite build`, `git diff --check` all pass. |

### Phase 5 — Pilot Operations And Launch Evidence

| Order | Session | Scope and completion gate |
|---|---|---|
| 5.1 | GAP-033 — Public-trust deployment evidence | **Manual/review task.** Capture the required real-browser intake, expired-tracker, and OffSeason evidence; implement only defects or the explicit banner decision that the review uncovers. |
| 5.2 | GAP-037 — Weekly value report | **Claude preflight required; Codex validation required before implementation.** Build the founder/internal account report endpoint/read path and manual-share output; do not build a business analytics dashboard or automated report delivery. |
| 5.3 | GAP-038 — Pilot feedback and help | **Claude preflight required; Codex validation required before implementation.** Add authenticated Report Friction and Help & Updates, its private founder route, and the required native parity work; preserve PII boundaries. |
| 5.4 | GAP-040 — Marketing and launch accuracy | **Claude preflight required; Codex validation required before implementation.** Bring public marketing copy/assets/legal/support links into alignment with the deployed product; verify deployment-facing claims and links. |
| 5.5 | Production-candidate release gate | **Manual/release task.** Run the full end-to-end checklist, validate alert routing/error capture/health/release identity, review known limitations, and decide whether pilot onboarding may begin. |

## Release Rules

- Finish or explicitly defer each selected P0/P1 tracker item before pilot invitation. A broken
  required-persona core flow, including authentication, is a pilot blocker.
- Before every production candidate, run the repository checks and the controlled smoke test
  (`scripts/production-smoke-test.mjs`, see `docs/runbook/production-smoke-test.md`); verify
  health/readiness, release identity, error capture, alert routing, and telemetry redaction.
- Do not onboard the excited pilot client until the production sign-in flow and the required
  end-to-end pilot checklist are verified.
