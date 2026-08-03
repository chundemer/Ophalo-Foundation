# Build Log 111 — Price Book, Quotes & Materials: Session 2d Preflight (Direct Price Entry and Versioned Publish)

**Status:** 2d.1c complete — 2d.2 mechanical preflight next
**Date:** 2026-08-02
**Scope:** Turns ADR-472's pivot (direct catalog entry, no CSV import) and ADR-470's locked publish-concurrency
mechanism into a buildable plan for the office direct price-entry/publish flow. Not an implementation session.
**Related:** Build 108 (ERD); Build 110 (Session 2 preflight); ADR-458; ADR-462; ADR-467; ADR-469; ADR-470;
ADR-471; ADR-472; ADR-473; DEF-087

## What changed since the Build 108 ERD

Build 108 designed `PriceBookVersion`/`PriceBookVersionLine` as import-publish's output, with a *manual
override* as the exception path (`SourceImportId = null`, described there as "a hypothetical manual-only
republish with no new import"). ADR-472 removed CSV import from MVP entirely (Session 2c.cleanup, complete).
That makes the former exception path the **only** path for V1: every `PriceBookVersion` in this batch has
`SourceImportId = null`. `PriceBookImport`/`PriceBookImportRow` do not exist in code and are out of scope here.

Import-only `PriceBookVersionLine` fields from the ERD (`SourceWorkbookTab`, `SourceRowNumber`,
`SourceLaborHoursSnapshot`, `SourceConsumablesAllowanceSnapshot`, and `SourceTaxAmountSnapshot`) are dropped.
There is no source workbook, and V1 direct entry captures Cost and SellPrice only; labor is a normal Service
catalog item and V1 does not calculate tax (ADR-473).

## Locked inputs this preflight builds on

- **ADR-470** — publish and manual `CatalogItem` override both run inside one serializable transaction holding
  an account-scoped optimistic lock (`PriceBookAccountState.PublishLockVersion`, module-owned, created lazily).
  A stale-lock competitor fails closed with a conflict; no silent merge, no partial write.
- **ADR-467** — round-half-up per line; a revision/version total is the sum of already-rounded line totals.
- **ADR-462** — API-facing service (not the domain/lifecycle service) owns the full auth-stack composition:
  account access gate → account-aware feature resolver → user permission → domain operation. Confirmed pattern:
  `CatalogItemApiService` (`src/OpHalo.Keep.Application/PriceBook/CatalogItemApiService.cs`).
- **Existing permission key** — `keep.pricebook.catalog.manage` already covers "import/publish/manual
  override/assemblies" per Build 108's authorization table; no new permission key is needed for this batch.
- **Existing header contract** — `X-Keep-CatalogItem-Version` (`CatalogItemVersionHeader.cs`) remains the
  locked optimistic-concurrency transport for `CatalogItem` lifecycle/header mutations. It is not a precondition
  for price publish: ADR-470's account-scoped publish lock is the concurrency contract for that operation.
- **`CatalogItem.CurrentPriceBookVersionLineId`** already exists as an unconstrained nullable FK placeholder
  (`src/OpHalo.Keep.Core/Entities/CatalogItem.cs`), explicitly reserved for this session.

## Proposed entities (this batch only)

- **`PriceBookAccountState`** — `Id`, `AccountId` (unique), `PublishLockVersion` (`Guid`, bumped every
  publish/override transaction). Created lazily on first publish/override, never on account creation (ADR-470).
- **`PriceBookVersion`** — `Id`, `AccountId`, `VersionNumber` (sequential per account, unique with `AccountId`),
  `SourceImportId` (nullable, always `null` in this batch — column retained for schema continuity with the ERD
  rather than re-adding it later), `PublishedAtUtc`, `PublishedByAccountUserId`, `Status`
  (`Published`/`Superseded`).
- **`PriceBookVersionLine`** — `Id`, `PriceBookVersionId`, `AccountId`, `CatalogItemId`, `DisplayNameSnapshot`,
  `TypeSnapshot`, `UnitOfMeasureSnapshot`, `CurrencySnapshot`, `CostSnapshot` (nullable money),
  `SellPriceSnapshot` (nullable money — a package-only/reference item may still carry no independent price).
  Unique on `(PriceBookVersionId, CatalogItemId)`.
- **`ManualPriceOverride`** — `Id`, `AccountId`, `TargetType` (`CatalogItem` only this batch; `QuoteLine`
  target is added when `OfficeQuote`/`QuoteLine` exist in a later session), `CatalogItemId`,
  `ActorAccountUserId`, `OccurredAtUtc`, `Reason` (required), `OldSellPrice`, `NewSellPrice`, `OldCost`
  (nullable), `NewCost` (nullable).

Each entity gets its own `keep_pricebook_*`-prefixed table (matching `keep_pricebook_catalog_items`/
`keep_pricebook_catalog_categories`), account-scoped FK/isolation, and no independent `ConcurrencyVersion` on
`PriceBookVersion`/`PriceBookVersionLine`/`ManualPriceOverride` — they are insert-only snapshots, never edited
in place, so there is nothing to version.

## Direct price-entry/publish flow

An Owner/Admin submits a price edit for **one `CatalogItem`** (Cost and/or SellPrice only).
The API service composes the ADR-462 gate order, then a domain-level `PriceBookPublishService` executes, inside
one serializable transaction:

1. Read (or lazily create) `PriceBookAccountState`, compare `PublishLockVersion` against the caller's expected
   value; conflict → fail closed, caller re-reads and retries (ADR-470).
2. Mark the account's prior `Published` `PriceBookVersion` as `Superseded`, increment
   `PriceBookVersion.VersionNumber`, and insert the new `Published` `PriceBookVersion` (`SourceImportId = null`)
   with its single `PriceBookVersionLine`, snapshotting the item's current header fields plus the new
   Cost/SellPrice.
3. Repoint `CatalogItem.CurrentPriceBookVersionLineId` to the new line.
4. Insert the `ManualPriceOverride` audit row (old/new values, actor, reason).
5. Bump `PriceBookAccountState.PublishLockVersion`.

No staging/draft entity is introduced — "draft" in the session-log framing means the in-flight request payload
before submission, not a persisted intermediate row. This matches ADR-470's manual-override shape directly;
it does not require inventing a new mechanism.

## Batch breakdown (gate: ≤3 mutation-handler families / ≤8 production files / ≤12 total per session)

1. **2d.1a — Account lock and override foundation.** `PriceBookAccountState` and `ManualPriceOverride`, their
   enum, shared errors, EF configurations, and unit tests only. No persistence, migration, service, or endpoint.
   This is six production files, within the session gate.
2. **2d.1b — Version aggregate foundation.** `PriceBookVersion` and `PriceBookVersionLine`, their status enum,
   shared errors, EF configurations, and unit tests only. No persistence, migration, service, or endpoint. This
   mirrors the `PriceBookImport`/`PriceBookImportRow` aggregate-foundation precedent and is six production files.
3. **2d.1c — Persistence and schema delivery.** Persistence interfaces/implementations for the settled entity
   shapes, one migration covering all four price-book tables, and the account-scoped composite FK from the
   existing `CatalogItem.CurrentPriceBookVersionLineId` placeholder to `PriceBookVersionLine`.
   No publish service, endpoint, or integration tests. Keep this as a separate mechanical preflight; it must
   demonstrate that its production and total file counts fit the session gate before implementation.
4. **2d.2 — Publish service and API delivery.** `PriceBookPublishService` (domain-level, ADR-470 transaction),
   `CatalogItemPriceApiService` (or an added method on the existing `CatalogItemApiService` — mechanical
   preflight decides which keeps the family count lowest), endpoint, `Program.cs` registration, `ErrorHttpMapper`
   entries for the new stable lock-conflict error. Integration tests: account isolation, competing-publish
   conflict (ADR-470 regression), and correct-path 200.

Each session gets its own mechanical preflight per the Session and Scope Protocol before implementation starts.

## Decisions confirmed for 2d.1

1. Price publish does **not** require the caller's `CatalogItem.ConcurrencyVersion` or
   `X-Keep-CatalogItem-Version`; the ADR-470 account-scoped publish lock is its optimistic-concurrency
   precondition. The header remains required for catalog lifecycle/header mutations.
2. V1 direct entry covers Cost and SellPrice only. Labor, consumables, and tax inputs/snapshots are out of scope.
3. V1 supports one `CatalogItem` per publish transaction. Bulk edit-and-publish is a later explicit workflow.
4. `CatalogItem.Activate()` remains price-independent: an active item may have
   `CurrentPriceBookVersionLineId = null` for catalog preparation or reference use.
5. `SourceWorkbookTab`, `SourceRowNumber`, `SourceLaborHoursSnapshot`,
   `SourceConsumablesAllowanceSnapshot`, and `SourceTaxAmountSnapshot` are out of scope and must not be
   retained as always-null columns.
