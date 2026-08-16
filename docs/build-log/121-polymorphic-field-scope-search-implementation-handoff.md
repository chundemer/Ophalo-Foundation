# Build Log 121 — Polymorphic Field Scope Search Implementation Handoff

**Status:** Approved handoff — ready to implement
**Date:** 2026-08-16
**Authority:** ADR-486
**Scope:** Replace the composer's Common-Item-only text search with one price-free polymorphic
field search across Active catalog items and operationally eligible assemblies. Keep Quick actions
as persistent accelerators. Reuse all existing scope mutations.

## Product contract

For a non-empty query such as `furn`, the composer shows, in order:

1. **Matching assemblies:** e.g. `Furnace Tune-Up` with an `Assembly` badge and optional
   `Expands 4 items` metadata. Selecting it calls the existing `expand-assembly` endpoint.
2. **Matching catalog items:** e.g. `Furnace Inspection` (`Service`), `Furnace Tune-Up Labor`
   (`Labor`). Selecting one calls the existing `field-select` endpoint.
3. **Custom item:** `Add “furn” as custom item`, always after real matches. If neither first
   group has a row, show `No Price Book matches` directly above the custom action.

Quick scope actions remain above the input as their current persistent, zero-to-six touch targets.
They are accelerators, not a cap on discoverability. Do not render a duplicate search-result row
merely because a matching result is already a Quick action.

## Server/API work

1. Add a dedicated, authorized, price-free endpoint, recommended shape:

   `GET /keep/pricebook/field/scope-search?search={query}&limit={limit}&cursor={cursor}`

   Return one typed sequence, not two browser-merged pages:

   ```json
   {
     "items": [
       { "kind": "OfferingAssembly", "id": "…", "displayName": "Furnace Tune-Up", "defaultItemCount": 4 },
       { "kind": "CatalogItem", "id": "…", "displayName": "Furnace Inspection", "catalogItemType": "Service", "externalKey": null }
     ],
     "limit": 20,
     "hasMore": false,
     "nextCursor": null
   }
   ```

2. Use ADR-480's existing field-read authority: account access (read posture), Price Book
   entitlement, `RequestsOperate`, and `ScopeCapture`. Return no price/cost/margin/tax/inventory/
   formula/quote fields.
3. Catalog candidates are every account-owned `Active` catalog item matching deterministic
   name/SKU/alias search — **do not filter `IsCommonItem`**. Assembly candidates are every `Active`
   operationally eligible assembly matching name.
4. API-side ranking is deterministic across kinds: exact match, then prefix match, then remaining
   supported deterministic match; tie-break with normalized display name then id. Define the cursor
   against that complete ordering so pages cannot duplicate or skip rows.
5. Do not alter `POST .../field-select` or `POST .../expand-assembly`. They already validate an
   Active catalog item and an eligible assembly respectively at mutation time.
6. Retain the existing Common-Items endpoint for any other consumer; migrate only the composer.

## Web work

1. Add typed client types/method for `scope-search`; replace
   `api.getFieldCatalogItems` in `ComposerSearchAndAdd` with it.
2. Render the returned sequence as **Matching assemblies** then **Matching catalog items**. Badge
   assemblies `Assembly`; badge catalog rows with `catalogItemType`. Do not show price-like data.
3. A catalog selection keeps the existing quantity/note confirmation and `fieldSelectProposedScopeLine`
   flow. An assembly selection dispatches `expandProposedScopeAssembly` immediately with
   `excludedOptionalItemIds: []`, then performs the existing authoritative scope reload.
4. Keep `ComposerQuickActions` above search, unchanged in its configuration/read contract.
5. Render a visible accessible search-error state; do not silently turn a failed request into an
   empty result set. Render the explicit no-match state only after a successful empty response.
6. Keep debounce, touch targets, focus behavior, full-screen phone dialog, conflict reconciliation,
   submitted/read-only behavior, and custom-input preservation unchanged.

## Verification gate

- API integration tests: mixed query, catalog-only, assembly-only, no match, inactive catalog
  exclusion, inactive/ineligible assembly exclusion, SKU and alias matching, ordering/cursor
  correctness, authorization, and absence of price fields.
- Web tests: grouped render order; custom action last; explicit no-match and error states; catalog
  selection uses `field-select`; assembly selection uses `expand-assembly`; Quick actions remain
  available and do not duplicate a matching row.
- Run targeted backend integration tests, frontend composer tests, frontend typecheck, full frontend
  suite, and `git diff --check`.

## Phase-completion boundary

This is **Phase 1: scope-composer correction**. It is complete only after the verification gate and
manual acceptance with realistic disposable Price Book data prove that a technician can find and
select a non-Common active catalog item and an eligible assembly, instead of being funnelled into a
custom item. Record that evidence before declaring the composer field-ready.

**Phase 2: Paired Nudges is deferred.** It is not part of this implementation. Do not add trigger
relationships, recommendation chips, settings UI, or inferred suggestions here. Begin it only after
Phase 1 is committed and manually accepted, through a separate preflight and decision.
