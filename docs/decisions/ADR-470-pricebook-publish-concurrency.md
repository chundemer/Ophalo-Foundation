# ADR-470 — Price-Book Publish Concurrency

**Status:** Locked
**Date:** 2026-07-31
**Related:** build-log/108, build-log/110, ADR-458, ADR-467

## Decision

Publishing a `PriceBookImport` (creating a new `PriceBookVersion` + `PriceBookVersionLine` rows and
repointing every affected `CatalogItem.CurrentPriceBookVersionLineId`) and recording a manual-only
`CatalogItem` price override (ADR-458's single-line `PriceBookVersion` with `SourceImportId = null`)
both execute inside one serializable database transaction, holding an account-scoped publish lock
version on a new module-owned `PriceBookAccountState` row (`AccountId` unique, `PublishLockVersion` —
an optimistic token bumped by every publish/manual-override transaction for that account; created
lazily on first publish/override, never on account creation). This stays a Price Book, Quotes &
Materials-owned row keyed by `AccountId`, not a new field on Foundation's `Account` entity — the
module must not add product-specific concurrency state to a shared identity entity. A competing
publish or manual override that started against a stale lock version fails the transaction with a
concurrency conflict — never a partial write, never last-write-wins — and the caller must re-read the
current state and retry against the new version. No row-level `CatalogItem.ConcurrencyVersion` check
is required for a bulk-pointer-repoint transaction already serialized by the account-level lock;
`CatalogItem.ConcurrencyVersion` remains authoritative for its own non-publish mutations (rename,
category change, activate/inactivate) that are not part of a publish transaction.

## Rationale

ADR-458 requires atomic publish but Build 108 never specified the concurrency-control mechanism for a
transaction that touches many `CatalogItem` rows at once, nor the conflict rule when a manual
override races a full-catalog publish. Per-row optimistic concurrency across an unbounded, variable
set of `CatalogItem` rows would require collecting and checking every affected row's token up front,
which is fragile against rows added mid-transaction by a concurrent import; a single account-scoped
lock version gives one deterministic conflict point that already matches the "atomic, all rows or
none" contract instead of inventing a second one. Fail-closed on conflict (retry, never silently
merge) matches the module's existing "financially consequential, server-authoritative" posture
(ADR-458) and the rest of the codebase's stable-error/no-silent-partial-write conventions.
