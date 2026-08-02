# Build Log 110 — Price Book, Quotes & Materials: Session 2 Mechanical Preflight

**Status:** Session 2a.1 implemented and migrated; Session 2a.2 authorized to begin
**Date:** 2026-07-31
**Scope:** Mechanical implementation preflight for Build 108's "Catalog and import" coding-session
slice; confirms no price-book code exists yet, checks the proposed slice against the repository's
batch-size gate, and surfaces two implementation-level decisions the ERD preflight left unspecified
**Related:** Build 108; build-log/109; ADR-453 through ADR-471

## Correction (2026-07-31, same day)

The first pass at this preflight undercounted Session 2a's gate: it excluded the three edited files
(`PermissionKeys.cs`, `RolePermissions.cs`, `Program.cs`) from the 8-production-file ceiling and
treated the migration as a non-counting extra. Edited files count fully against the production-file
gate; only tests and the EF-generated migration sit outside it. As originally scoped (entity +
persistence + endpoints + permission wiring in one session), 2a was 12 production files — over gate.
Corrected by splitting 2a itself into two batches, below (Christian's recommendation, adopted as-is):

- **2a.1 — CatalogItem foundation** (8 production files, exactly at gate): entity, two enums, errors,
  persistence interface, lifecycle service, EF persistence, EF configuration, plus its migration.
  Unit tests only (entity + service) — no API surface yet.
- **2a.2 — CatalogItem API delivery** (4 production files): endpoints, permission key, role grant,
  `Program.cs` registration/mapping. API integration tests here, including cross-account isolation and
  stale-`ConcurrencyVersion` 409.

This keeps the two batches independently compiling and independently reviewable, and avoids a
false-economy exception on the very first slice of the module.

## Finding: the named slice breaks the batch gate

Build 108's "Catalog and import" coding-session slice (`CatalogCategory`, `CatalogItem`,
`CatalogItemAlias`, `PriceBookImport`/`PriceBookImportRow`, `PriceBookVersion`/`PriceBookVersionLine`,
`ManualPriceOverride`, staged validation, atomic publish) exceeds the repository's hard batch gate
(eight production files / three independent mutation-handler families) as a single session, whichever
way its entities are grouped. It is split into three independently-verifiable coding sessions instead:

1. **Session 2a.1 — CatalogItem foundation.** Entity, enums, errors, persistence
   interface/implementation/configuration, lifecycle service, migration, unit tests only.
   **Authorized to begin.** Followed by **2a.2 — CatalogItem API delivery** (endpoints, permission
   key, role grant, DI/route registration, integration tests: account-isolation and stale-version
   409) — see "Correction" below for the gate math that produced this split.
2. **Session 2b — Categories and aliases.** `CatalogCategory`/`CatalogItemAlias`, as a separate
   session following 2a. `CatalogCategory` uses its own two-value `Active`/`Inactive` state enum,
   distinct from `CatalogItem`'s `Draft`/`Active`/`Inactive` — a category is never `Draft`.
3. **Session 2c — Import staging/validation and Session 2d — versioned atomic publish** remain
   blocked until the two decisions below are recorded (now done — ADR-469, ADR-470) and until each is
   itself checked against the batch gate at its own preflight.

## Decisions locked

- **Import object storage — ADR-469.** A private, module-owned `IPriceBookImportFileStorage`
  abstraction backs `PriceBookImport.SourceFileObjectKey`; no reuse of a nonexistent generic blob
  service, no public URL, retained for the life of the import row.
- **Publish concurrency — ADR-470.** Atomic publish and manual-only override run inside one
  serializable transaction holding an account-scoped optimistic lock version on a new
  `PriceBookAccountState` row (module-owned, not a new field on Foundation's `Account`). A competing
  publish/override against a stale lock version fails closed with a concurrency conflict and must
  retry.

## Storage and 2c.1 scope clarification (2026-08-01)

ADR-471 supersedes ADR-469's module-specific storage-seam direction: production business documents
will use a **private Cloudflare R2 bucket** through an application-owned, S3-compatible .NET
`IBusinessDocumentStorage` seam. Local filesystem storage is not a pilot/production fallback;
Vercel hosts the web frontend and is not this API's document-storage backend. The inherited ADR-469
rules remain: opaque DB key only, no DB blob, no public URL, and import source retained for the row's
lifetime.

Session **2c.1** is deliberately limited to `PriceBookImport`/`PriceBookImportRow` staging,
validation, lifecycle, and exception-resolution behavior. It does **not** implement an upload route,
R2 adapter/provisioning, CSV parsing, XLSX support, or presigned URLs. Upload/parsing is a subsequent
preflighted slice. `SourceFileObjectKey` remains required/non-null in 2c.1; tests use opaque test
keys, never a nullable schema or a production placeholder. The later V1 upload contract is UTF-8 CSV
(optional BOM) only, rejecting unsupported legacy encodings rather than silently corrupting values.
V1 exports stream incrementally from paged/streamed authoritative data through the .NET API, never
from a fully materialized CSV buffer. The R2 provisioning slice must also establish narrow
origin/method/header CORS rules for later presigned browser uploads; wildcard origins are prohibited.

## 2c.1a implementation decisions (2026-08-02)

The following choices close the remaining import-row implementation questions and are binding for
the 2c.1a domain/schema batch:

- `PriceBookImportRow` is a `PriceBookImport` aggregate-owned child, analogous to
  `CatalogItemAlias` for creation and lifecycle ownership, but validation is expressly permitted to
  load and persist a row directly through row-domain transition methods. Never load thousands of rows
  merely to change one validation outcome. A row has no independent `ConcurrencyVersion`; import
  lifecycle/publish remains parent-governed.
- Configure an explicit `PriceBookImportId` FK from row to import and `WithMany(i => i.Rows)`;
  index `PriceBookImportId` and `(PriceBookImportId, ValidationStatus)`; retain the ERD's unique
  `(ImportId, RowNumber)` constraint and account-scoped FK/isolation protections. The owned
  parent/row relationship uses `DeleteBehavior.Cascade` (account relationships remain restricted).
- Raw import values are staging-tolerant: `ProposedType` is nullable raw `string?`, never
  `CatalogItemType`; all proposed monetary/labor values are nullable `decimal?`
  (`ProposedCost`, `ProposedSellPrice`, `ProposedSourceLaborHours`,
  `ProposedSourceConsumablesAllowance`, `ProposedSourceTaxAmount`).
- Model `ValidationMessages` as an entity-owned collection of strings, exposed read-only and backed
  by a private list. Map it to one PostgreSQL `jsonb` column with an EF Core JSON value converter and
  a `ValueComparer` for reliable change tracking. Validation messages must preserve offending raw
  input for parsing failures; a null parsed value alone is not sufficient exception-review context.

This does not authorize validation rules, parser behavior, or the validation/exception-resolution
application service; those remain 2c.1b.

## Outcome

Session 2a.1 — CatalogItem foundation is **complete**: entity, `CatalogItemType`/`CatalogItemActiveState`
enums, `CatalogItemErrors`, `ICatalogItemPersistence`/`EfCatalogItemPersistence`,
`CatalogItemConfiguration` (table `keep_pricebook_catalog_items`), `CatalogItemLifecycleService`,
migration generated and applied. Review before migration generation found and fixed two issues: the
table name (`catalog_items` → locked `keep_pricebook_catalog_items`) and an unhandled external-key
race (concurrent unique-index violation on `AddAsync` now caught and translated to
`CatalogItemErrors.ExternalKeyAlreadyExists`, matching `EfAuthCodePersistence`'s existing
`IsUniqueConstraintViolation` pattern, with a regression test). 31/31 focused unit tests pass, full
solution build clean, architecture tests 14/14 pass, `git diff --check` clean.

**Session 2a.2 — CatalogItem API delivery is complete**, with one recorded gate exception. The
mechanical preflight approved a 6-file plan (`PriceBookEndpoints.cs`, a new `CatalogItemApiService`
owning the ADR-462 auth-stack composition, the new `keep.pricebook.catalog.manage` permission key
and its Admin+ role grant, DI registration, `Program.cs` route mapping), corrected to 7 files when
review caught a missing `IAccountFeatureAccessResolver` gate and a missing `ErrorHttpMapper` 409
mapping for `CatalogItem.VersionMismatch`/`ExternalKeyAlreadyExists`. A second review pass then
caught that activate/inactivate were implemented as `POST` with a body-carried `expectedVersion`,
conflicting with this document's own locked `X-Keep-*-Version` optimistic-concurrency header
contract (line 40 above). The fix — `PATCH` + a new `CatalogItemVersionHeader` parser (mirroring
`KeepRequestVersionHeader`) + two new `CatalogItemErrors` codes it returns — added 2 more production
files, landing at **9 production files against the locked 8-file gate**.

**Gate exception, recorded and approved:** the overage is accepted as a single-file exception
rather than split into a follow-up session. Rationale: the two added files
(`CatalogItemVersionHeader.cs`; the two new constants on the already-touched `CatalogItemErrors.cs`)
are a direct, tightly-coupled fix for a locked-contract violation surfaced by review, not new
scope — `PriceBookEndpoints.cs` cannot be contract-correct without them, so a follow-up split would
mean committing a batch that knowingly uses the wrong concurrency-token transport now and patching
it later, which is worse than a one-file gate overage.

Final state: `PriceBookEndpoints.cs`, `CatalogItemApiService.cs` (new), `PermissionKeys.cs`,
`RolePermissions.cs`, `KeepServiceCollectionExtensions.cs`, `Program.cs`, `ErrorHttpMapper.cs`,
`CatalogItemVersionHeader.cs` (new), `CatalogItemErrors.cs` — 9 production files (1-file exception
recorded above). Plus `CatalogItemApiTests.cs` and `CatalogItemVersionHeaderTests.cs` (new,
integration tests — account-isolation 404, stale/missing/malformed-header 409/400/400, entitlement-gate
403, correct-path 200/204). 15/15 CatalogItem integration tests pass, 31/31 focused 2a.1 unit tests
unchanged, 14/14 architecture tests pass, full solution build clean, `git diff --check` clean.

Sessions 2b–2d remain scoped above but not yet preflighted individually; each requires its own
mechanical preflight before implementation begins, per the Session and Scope Protocol.
