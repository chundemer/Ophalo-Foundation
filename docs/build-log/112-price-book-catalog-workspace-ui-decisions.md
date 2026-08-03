# Build Log 112 — Price Book Catalog Workspace UI Decisions

**Status:** Locked product/UI decisions; implementation has not started  
**Date:** 2026-08-03  
**Scope:** Session 2e preflight and implementation boundary for the Owner/Admin Price Book catalog workspace.  
**Related:** Build 108; Build 110; Build 111; ADR-462; ADR-468; ADR-470; ADR-472; ADR-473

## Purpose

CSV import is deliberately out of the MVP. Direct catalog entry is therefore the office's primary
onboarding path and must be fast, safe, and usable before quote work or the separate pilot-image
slice proceeds. This record locks the product behavior for the catalog workspace; it does not
authorize implementation beyond a validated Session 2e preflight.

## Navigation, eligibility, and direct access

- **Price Book** is a first-class, top-level app navigation item, alongside Requests—not a Settings
  subsection. It appears in both applicable desktop navigation treatments.
- It is visible only to an authenticated Owner/Admin whose account has the
  `PriceBookQuotesMaterials` capability and whose user has `keep.pricebook.catalog.manage`.
- The client check controls discovery and clear unavailable states only. Every catalog API remains
  the authority for capability, permission, account scope, and concurrency.
- Field roles do not see the workspace and must never see or edit cost, margin, or master prices.

## Delivery sequence

1. Session 2e delivers a real **Catalog Items** workspace.
2. **Offerings & Packages** remains decided V1 scope (Build 108 and ADR-473), but its entities,
   APIs, relationship reads, and UI are not implemented yet. Do not render a ghost, disabled, or
   empty tab in 2e.
3. The immediately subsequent, separately bounded slice implements the real Offering/Assembly
   foundation and its workspace before request-bound quote work begins.

## Catalog workspace

- The list defaults to active items and supports server-side, debounced, paged search.
- Search matches display name, SKU/external key, and active aliases. SKU matching is
  case-insensitive and punctuation/whitespace-insensitive, so `cop34`, `COP-34`, and `cop 34`
  resolve consistently.
- The list has one-click type chips: All, Materials, Services, Equipment, and Fees; it also has
  category and status filters.
- The initial table shows name, SKU, type, unit of measure, current sell price, status, and actions.
  It does not yet carry a package-usage column, because offerings do not exist.
- Create and edit use a desktop slide-over drawer so list filters and scroll position persist. On
  viewports below 768px, the same editor is full-screen.
- Include **Save & add another**. It retains category, type, UOM, currency, and the chosen price
  mode, while clearing display name, SKU, and aliases.
- Include **Duplicate**. It starts a new draft with a ` (Copy)` name suffix and copies category,
  type, UOM, currency, common-item setting, and price values; it always clears SKU and aliases.

## Names, search terms, categories, and currency

- `DisplayName` is required customer/technician-facing text. The UI explains that it should be
  written as it should appear on a quote, with a concrete example.
- SKU/internal code is optional and is the proper home for a vendor part number or shorthand such
  as `CP20`. Aliases provide additional searchable shorthand without degrading display names.
- The creation drawer supports an optional initial alias and management of aliases on an existing
  item.
- Inline category creation is allowed from the category selector. It trims input and first compares
  normalized names against loaded categories. If an exact normalized category exists, select it.
  The server/database remains race-safe through the existing account-scoped normalized-name unique
  constraint; a duplicate conflict causes a refetch and selection of the existing category.
- ADR-468's single account currency is shown read-only beside pricing fields. The UI must not imply
  that currency is selectable per item.

## Item edits, pricing, and lifecycle

- An item's display name, SKU, category, aliases, and Common Item status may be changed using the
  catalog item's concurrency token. Those changes apply to future use only; approved quote snapshots
  are never rewritten.
- Type and UOM are immutable after activation or once an Offering/Assembly references the item.
  To change either, duplicate/create a replacement item and retire the former item when appropriate.
  A warning-only confirmation is insufficient because live offerings can depend on the unit/type.
- The price UI has an explicit **No standalone price** choice. When enabled, Sell Price is cleared
  and disabled; otherwise Sell Price is required and must be at least zero. The label deliberately
  does not claim the item is already included in a package, since package behavior does not exist in
  this slice.
- The ordinary primary action is **Save & activate** and must be atomic: create the item, create its
  initial price version (or explicit no-standalone-price version), and activate it in one server-side
  transaction. A separate **Save draft** action is available.
- There is no hard delete. Inactivation removes an item from new selection while preserving all
  historical records and quoted snapshots.

## Future Offering/Package safety, locked now but not implemented in 2e

- Once offerings exist, an item drawer may show an active-offering usage count and link to the
  relevant offering workspace. The list can add a compact count only after usability evidence
  justifies the extra density.
- An active offering containing an inactive component has a derived **Needs review** state; it is
  not a stored `HasInactiveComponents` flag.
- Such an offering cannot be used to make a new quote until an Owner/Admin reactivates or replaces
  the component. Historical quotes remain unchanged.
- Inactivation must warn about active offering usage when that relationship exists. The relationship
  and quote-block implementation belong to the Offering/quote slice, not the catalog-only slice.

## Required Session 2e preflight checks

- Identify the bounded read API(s) required for list/search, category choices, current-price display,
  and an item detail drawer; no client-side loading of an unbounded catalog.
- Reconcile the existing separate create, publish, and activate endpoints with the locked atomic
  `Save & activate` command. Specify recovery and optimistic-concurrency behavior before code.
- Define the safe mutation contract for the permitted item-header edits; current code does not yet
  expose those mutations.
- Verify mobile focus trapping, drawer dismissal, unsaved-change behavior, query invalidation, and
  direct-route entitlement behavior.
- Keep Offering/Assembly entities, package counts, quote composition, and field-image work out of
  the Session 2e implementation change set.
