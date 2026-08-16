# Build Log 119 — Unified Scope Composer: Session 1 Server Contract Preflight

**Status:** Complete — decisions locked; no production code written  
**Date:** 2026-08-15  
**Scope:** Reconcile the ADR-482–485 unified-composer contract against the existing Session 3.4
surface and lock the server work required before Session 2 implementation.  
**Related:** Build 118; ADR-479 through ADR-485

## Outcome

Three ADR-485 requirements are already satisfied without new server work:

- `FieldCatalogReadApiService.ListItemsAsync` supplies price-free, deterministic Common-Item and
  Active-only field search, including `Search`, `CategoryId`, `Limit`, `Cursor`, `MatchRank`, and
  `MatchReason`.
- `expand-assembly` already atomically rechecks eligibility and creates all assembly default lines
  as editable Draft lines. The unified composer uses that default-only behavior directly.
- The established new-endpoint gate is explicit: authenticate, account access, Price Book
  entitlement, and `RequestsOperate` plus `ScopeCapture`, followed by row visibility where the
  endpoint is request/scope-bound. New APIs must restate this stack rather than inherit it by
  reference.

The following three server changes remain required: persisted versioned Undo-delete, server
rejection of empty submission, and Quick scope action configuration/field reads.

## Locked decisions

### 1. Server-authoritative Undo expiry

Deleting a Draft line continues to remove the active `ProposedScopeLine` row; it is not converted
to a soft delete. In the same transaction, the server persists a short-lived removed-line snapshot
keyed by `(ProposedScopeId, LineId)`. The snapshot retains the complete line field set and
provenance needed to recreate the exact line, its original `DisplayOrder`, and `RemovedAtUtc`.

`restore` is a versioned server mutation. It re-inserts the original line at its original display
order and bumps the scope concurrency version. Delete likewise bumps the version. A restore is
accepted only when server time is no more than five seconds after `RemovedAtUtc`; otherwise it
returns a typed expiry error. The client-side toast timer is presentation only and cannot extend the
window. A restore also fails if the original line id is already present, preserving idempotence and
preventing duplicate reconstruction.

Expired records may be cleaned up asynchronously, but cleanup is not correctness enforcement: the
restore operation always checks `RemovedAtUtc` itself. This preserves the five-second rule even if
the cleanup job is delayed.

**Retention operational requirement.** Removed-line snapshots are transient recovery state, not an
audit log. A scheduled cleanup must delete rows whose `RemovedAtUtc` is older than five minutes,
running at least hourly. The five-minute buffer is operational slack only; it never extends the
five-second restore window. Successful restore already deletes its consumed snapshot in the same
transaction. Cleanup must be bounded/batched and emit its deleted-row count and failure outcome so
retention failures are discoverable. It is a separate maintenance slice and must be delivered before
the Undo-delete feature is treated as production-complete; it must not be placed on the field
mutation hot path or used to determine whether a restore is expired.

### 2. Empty scopes cannot submit

`ProposedScope.Submit()` gains the domain invariant that `_lines.Count` must be greater than zero.
The domain exposes a dedicated `ProposedScopeErrors.EmptySubmit` error. This complements, rather
than relies on, the existing client-side disabled-submit hint.

### 3. One polymorphic Quick scope action slot

Quick actions use one account-owned ordered-slot entity, not parallel catalog-item and assembly
tables. Each row has exactly one target: `CatalogItemId` or `OfferingAssemblyId`. A database check
constraint enforces exclusive presence; account-scoped ordering and the zero-to-six maximum are
enforced by the aggregate/service and database constraints where applicable.

Only an Active Common `CatalogItem` or an operationally eligible `OfferingAssembly` may be selected
when an Owner/Admin writes configuration. Owner/Admin configuration reads expose all configured
slots in order, including a clear ineligible state when a previously configured target has later
become inactive or ineligible. The price-free field read returns only currently eligible configured
actions, in stored order. The server never silently auto-drops an invalid configuration: correction
is an explicit Owner/Admin action and remains auditable.

Each endpoint spells out its authorization composition. Owner/Admin configuration reads and writes
follow the existing PriceBookCatalogManage-adjacent configuration pattern: authenticate, account
access, Price Book entitlement, and `PriceBookCatalogManage`, then act. The technician field read
uses the field-access pattern: authenticate, account access, Price Book entitlement,
`RequestsOperate` plus `ScopeCapture`, then act. These rules are written in each API service; they
are not inherited merely by citing an existing service.

## Operational clarifications

### Restore ordering

Restore preserves the removed line's original `DisplayOrder`. It does not renumber the other Draft
lines: an Undo operation must not create unrelated concurrent edits. Reads remain deterministically
ordered by `DisplayOrder`, then `LineId`, so an unexpected duplicate display-order value is stable
across clients without a restore-time re-normalization pass.

### Quick action invalidation while the composer is open

A field read is only a point-in-time eligibility view. A target can become unavailable before the
technician taps it. The subsequent authoritative selection operation remains the decision point:
the existing catalog-item-unavailable result or
`ProposedScope.ExpandAssemblyNotOperationallyEligible` is returned, followed by normal
reconciliation. The PWA maps those known results to a contextual notice that the office recently
updated the item and it is no longer available. Do not introduce a separate
`QuickActionIneligible` server error while field actions submit resolved catalog-item/assembly ids,
not a Quick-action id.

### Keyboard-safe composer behavior

The frontend acceptance gate includes real keyboard-open verification in iOS Safari and Android
Chrome. Search focus, edited line inputs, validation feedback, and the submit control must remain
usable and unobscured. `100dvh`/flex layouts and the `visualViewport` API are permissible tools;
the locked contract is the observable keyboard-safe behavior, not a required implementation
mechanism.

### Decimal quantities

All Draft line types accept positive decimal quantities at the domain/API layer. Unit conventions
may guide field labels and defaults but must not create integer-only or unit-specific submission
validation that blocks a technician's fractional quantity.

## Session 2 implementation boundary

Session 2 may add the removed-line snapshot table, Quick action entity/migration, domain services,
Owner/Admin configuration reads/writes, field read, restore endpoint, and tests. It must not reopen
deterministic field search or assembly expansion. It must prove expiry, restore/version conflicts,
empty-submit rejection, polymorphic-target validity, six-slot/order boundaries, field price-key
absence, and the invalid-config split (visible to Owner/Admin; absent from field read).
