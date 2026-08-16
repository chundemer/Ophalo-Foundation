# ADR-486 — Polymorphic Field Scope Search

**Status:** Locked
**Date:** 2026-08-16
**Supersedes in part:** ADR-482 and ADR-483's field-discovery boundary
**Related:** ADR-479, ADR-480, ADR-481, ADR-482, ADR-483, ADR-484, ADR-485

## Decision

The technician scope composer's typeahead searches the whole field-selectable Price Book, not only
Common Items or the Owner/Admin-curated Quick scope action set.

For every non-empty query, one price-free, deterministic field-search read returns:

1. every Active catalog item whose display name, SKU/external key, or alias matches; and
2. every Active, operationally eligible Offering Assembly whose name matches.

The returned result is a typed, polymorphic row (`CatalogItem` or `OfferingAssembly`) with only
the id, display name, kind/type label, and non-price metadata required to render and select it.
An assembly result may include its default-item count for a non-price `Expands N items` cue. It
must never include price, cost, margin, tax, inventory, formula, or quote data.

Quick scope actions remain an optional, explicitly ordered set of at most six Owner/Admin-managed
accelerators. They are persistent one-tap controls above the search input; they are no longer the
only field-discovery route for assemblies or non-Common catalog items.

The result UI is grouped in this order:

1. **Matching assemblies** — badged `Assembly`; selecting one calls the existing default-only,
   atomic `expand-assembly` mutation.
2. **Matching catalog items** — badged with their catalog type; selecting one calls the existing
   `field-select` mutation.
3. **Custom item** — `Add “query” as custom item`, always last. When there are no Price Book
   matches, show an explicit `No Price Book matches` message immediately above this action.

Configured Quick actions may be visually marked as such in matching results, but must not be
duplicated as a separate search-result row. Their top-of-composer controls remain sufficient.
Typing never commits a line. Existing server-side Active-state/operational-eligibility validation,
three-gate authority, scope concurrency, authoritative reread, snapshots, Draft editing, and
price-blindness boundaries remain unchanged.

## Rationale

On a job site, a technician who searches for a known assembly or catalog item and receives no
result will naturally create raw custom text. Restricting text search to Common Items and making
only six assemblies discoverable as Quick actions therefore creates field friction and weakens the
structured catalog/assembly data the scope workflow is meant to capture.

Quick actions still save taps for common work. Search is the complete fallback: it makes every
field-selectable assembly and active catalog item discoverable without forcing a technician to
reconfigure the Price Book, browse artificial rungs, or create an avoidable custom line.

## Consequences and required follow-up

1. Replace the Common-Item-only field catalog search used by the composer with a dedicated
   polymorphic field scope-search endpoint. Do not merge independently paginated catalog and
   assembly reads in the browser.
2. Add deterministic ranking and pagination semantics that work across both kinds. Exact and
   prefix matches rank ahead of other deterministic matches; ties use normalized display name then
   id. The API is the sole ranking authority.
3. Keep existing `field-select` and `expand-assembly` mutations unchanged. A selection must still
   be revalidated server-side at mutation time.
4. Update the composer to render the three groups above, retain Quick actions as accelerators, and
   surface field-search failures distinctly from a genuine zero-result response.
5. Add integration and component coverage for catalog-only, assembly-only, mixed, no-match,
   inactive/ineligible exclusion, deterministic ordering, price blindness, and both selection
   mutation paths.
