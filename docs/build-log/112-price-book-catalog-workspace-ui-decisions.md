# Build Log 112 — Price Book Catalog Workspace UI Decisions

**Status:** Locked product/UI decisions; amended before implementation
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
- The same eligible Owner/Admin sees **Price Book** in the mobile hamburger/overflow navigation
  alongside Requests. A phone user must not need a manually known URL to reach the workspace.
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

- The list defaults to active items and supports server-side, debounced, cursor-paged search.
- Search matches display name, SKU/external key, and active aliases. SKU matching is
  case-insensitive and punctuation/whitespace-insensitive, so `cop34`, `COP-34`, and `cop 34`
  resolve consistently. SKU uniqueness uses that same canonical normalization per account: preserve
  `ExternalKey` for display, but persist/index a normalized key so values resolving to the same
  search term cannot occupy different items. Aliases are unique only
  within an item, so a shared alias may return several literal matches. Each result must identify
  why it matched (name, SKU, or alias), rather than implying a single authoritative resolution.
- Default browse order (no active search term) is Display Name, A–Z. When a search term is active,
  results rank deterministically: exact matches before prefix matches before substring matches, with
  the match-reason label preserved at every rank. Both browse and search use normalized Display Name
  then item ID as final tie-breakers, yielding a total, stable order for cursor pagination.
- The list has one-click type chips: All, Materials, Services, Equipment, and Fees; it also has
  category and status filters.
- The initial table shows name, SKU, type, unit of measure, current sell price, status, and actions.
  It renders an item with `NoStandalonePrice` as **No standalone price**, never `$0.00` or a blank
  price. It does not yet carry a package-usage column, because offerings do not exist.
- A fresh, unfiltered catalog has a dedicated onboarding empty state—not a generic no-results
  message. Filtered no-results states remain distinct.
- **Amendment (2026-08-05, catalog empty-state refinement):** after a successful default list read
  that returns zero items, hide the page-header catalog-create CTA and show one bounded, centered
  onboarding panel instead. Its title is **Your catalog is empty**; its body is **Start with the
  parts, services, and fees you use most.**; and its one primary action is **Add your first catalog
  item**. That action opens the same create drawer as the normal page action. When one or more
  items exist, restore the page-header action as **Add catalog item** and do not duplicate it in the
  list body. Loading and error states must not claim that the catalog is empty. Do not introduce
  templates, invoices, package selection, accounting hooks, or unrelated workflow promises.
- Create and edit use a desktop slide-over drawer so list filters and scroll position persist. On
  viewports below 768px, the same editor is full-screen.
- Include **Save & add another**. It retains category, type, UOM, currency, and the chosen price
  mode, while clearing item identity, concurrency token, display name, SKU, aliases, Cost, and Sell
  Price. On success it shows a non-blocking confirmation and returns focus to Display Name.
  `Ctrl+Enter`/`Cmd+Enter` triggers this action from within the drawer.
- Include **Duplicate**. It starts a new draft with a ` (Copy)` name suffix and copies category,
  type, UOM, currency, common-item setting, and price values; it always clears SKU and aliases.
  If needed, truncate the source display name safely before adding the suffix so the resulting name
  remains within the server's 200-character limit.
- Every search, type-chip, category, or status-filter change resets the cursor/query stack to the
  first result set. It must not retain a cursor issued for a different filter combination.
- Keyboard shortcuts are discoverable through an accessible help affordance: `Ctrl+Enter`/`Cmd+Enter`
  saves and adds another from the drawer; `/` focuses catalog search; and `n` opens New Item only
  when focus is not in an editable or interactive control. Escape dismisses a clean drawer and
  invokes the dirty-dismiss safeguard for a changed one.

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
  constraint; a duplicate conflict causes a refetch and selection of the existing category before
  the item mutation is issued. Do not retry an already-submitted item mutation merely because
  category creation raced.
- **Amendment (2026-08-06, Build Log 114):** the initial native category selector is adequate only
  for a small pilot list. Session 2e.7 replaces it with an accessible searchable combobox shared
  with category filtering: Owner/Admin can search/select a business-owned category, clear to **No
  category**, and create a new category only when its normalized name has no exact existing match.
  This improves entry and browsing at 10–20+ categories without imposing a seeded taxonomy.
- **Amendment (2026-08-07, drawer polish and category safety):** the 2e.7 creatable category
  combobox is the sole category entry control — selecting **Create "[name]"** begins creation and
  then selects the resulting or race-resolved category. It must not reveal a second nested input or
  reflow Type from its desktop half-width column. While category creation or duplicate-race recovery
  is pending, **Save & activate**, **Save & add another**, and `Ctrl/Cmd+Enter` are disabled; an item
  mutation must never run with a null/stale category merely because its requested category has not
  resolved. A category remains optional when the user explicitly chooses **No category**.
- ADR-468's single account currency is not selectable per item.
- UOM is V1 free text: required, trimmed, and limited to 50 characters. It is a human-facing unit
  label, not a conversion/math engine, account-managed taxonomy, or enum. The UI may suggest common
  values without restricting entry.
- **Amendment (2026-08-05, 2e.5 drawer refinement):** default a new item's free-text UOM to
  `each` and offer quick-fill suggestions (`each`, `hour`, `ft`, `sq ft`, `gal`, `lb`, `box`,
  `lot`). A suggestion only writes that literal text into the editable field; it does not normalize,
  canonicalize, or constrain a contractor's custom value. Unit taxonomy, conversion, reporting, and
  accounting/procurement mapping remain deferred rather than being smuggled into catalog entry.
- The creation drawer's primary flow is Name, Type/Category, UOM, and pricing. SKU and the one
  optional initial alias are visibly grouped as **Codes & search (optional)**, not hidden behind an
  accordion. Use plain-language `Search keyword / shorthand` copy that promises one searchable term,
  not a tag system. `IsCommonItem` must not claim a current quick-list behavior that is not yet
  rendered for field users.
- The price-mode control may be expressed as a checkbox such as **This item doesn't have its own
  sell price**. Checked means `PricingMode=NoStandalonePrice` and the request must send
  `SellPrice=null` — never zero. Unchecked means `StandalonePrice` and a sell price is required.
- Cost and Sell Price share a desktop row and stack on mobile when standalone pricing is selected,
  so an owner can assess the cost-to-sell relationship at a glance. The no-standalone-price control
  sits with the Sell Price choice and hides/clears that field when selected. Per ADR-468's 2026-08-05
  amendment, the initial pilot is deliberately USD-only while no server-owned account-currency read
  exists: the creation request sends `USD`, pricing uses `$` prefixes, and the drawer quietly states
  **Prices in USD**. Currency is neither selectable nor rendered as a full form control. A
  server-owned account-currency setting is required before any non-USD pilot account is supported;
  do not invent client `AccountContext` data in this slice.
- `IsCommonItem` is the Owner/Admin-curated flag for Build 108's future field-selection **Common
  Items** rung. It is independent of categories and offerings, has no MVP hard cap, and does not
  grant field roles access to this administration workspace. It may sit beside Type/Category to
  improve grouping, but its label must not claim a present-day quick-add or pinning behavior.
- Selecting a UOM quick-fill writes only that literal value into the editable UOM field. It must not
  automatically move focus to pricing: the owner may want to refine the unit, and normal Tab order
  remains the predictable speed-entry path.

## Item edits, pricing, and lifecycle

- Session 2e's creation drawer has a single creation outcome: atomic **Save & activate**. It does not
  expose a user-facing Save Draft action. The domain's `Draft` status remains reserved by Build 108
  for a future actual-work promotion/office-review workflow; that workflow owns Draft creation,
  discovery, and activation and is out of scope for this slice.
- An active item's display name, SKU, category, aliases, and Common Item status may continue to be
  changed using the catalog item's concurrency token. Those changes apply to future use only;
  approved quote snapshots are never rewritten.
- Type and UOM become immutable on activation or once an Offering/Assembly references the item.
  In the active-item editor, show these as explanatory metadata rather than disabled controls, with
  guidance to duplicate/create a replacement item to change them. The rationale is not only future
  Offering dependence: the current price is already an immutable snapshot that includes UOM, so
  changing an active item's UOM without a simultaneous new price version would make the displayed
  item and its current-price snapshot semantically disagree. A warning-only confirmation is
  insufficient. Because Save & activate is the ordinary path, the activation drawer must make Type
  and UOM highly visible and reviewable before commit.
- The price UI has an explicit **No standalone price** choice. It is persisted on each immutable
  `PriceBookVersionLine` as `PriceBookLinePricingMode`, alongside the Cost and Sell Price snapshots:
  `StandalonePrice` requires a Sell Price of at least zero; `NoStandalonePrice` requires a null Sell
  Price. Cost remains optional in either mode. A nullable Sell Price alone is not sufficient because
  it cannot distinguish intentionally non-standalone pricing from omitted data. The label
  deliberately does not claim the item is already included in a package, since package behavior
  does not exist in this slice.
- When Cost is present and Sell Price is lower, the drawer shows a prominent below-cost warning and
  requires an explicit confirmation to save or publish. It remains permitted because below-cost
  pricing can be intentional; there is no automatic margin, markup, or pricing-rule engine.
- **Amendment (2026-08-06, Build Log 114):** active-item/current-price detail may show Owner/Admin
  users read-only gross profit (`Sell Price - Cost`), margin percentage (`gross profit / Sell Price`),
  and markup percentage (`gross profit / Cost`) when both snapshot inputs exist. This is a derived
  visibility aid, not stored data or a target-margin/markup pricing control. It remains hidden from
  field roles; omit unavailable values and never divide by zero.
- The ordinary primary action is **Save & activate** and must be atomic: create the item, create its
  initial price version (or explicit no-standalone-price version), and activate it in one server-side
  serializable transaction. Its price-audit reason is system-owned: `Initial catalog price`; do not
  require a repetitive owner-entered explanation during first-time setup. It is the drawer's only
  creation outcome for this slice.
- There is no hard delete. Inactivation removes an item from new selection while preserving all
  historical records and quoted snapshots.
- The Inactive status filter exposes **Reactivate**. It requires the item's current concurrency token
  and retains the existing current-price pointer, including an explicit no-standalone-price state;
  reactivation does not create a new price version.

## Application-service command boundary

The mechanical preflight defines concrete request/response DTO names, but it must preserve these
service operations and their authority boundaries:

| Operation | Input boundary | Result/invariant |
| --- | --- | --- |
| Create and activate | Header fields, aliases, price mode, Cost, Sell Price | One serializable transaction creates the item, validates aliases/SKU/category, records the initial price snapshot and audit, then activates. Any failure leaves no partial item, aliases, versions, or audit row. This is the only item-creation path exposed in Session 2e. |
| Update item header | Item ID, current item concurrency token, mutable header fields | Updates only permitted header fields. Type/UOM and alias replacement are excluded. |
| Manage aliases | Existing explicit alias add/activate/inactivate operations with item token | Do not replace an entire alias list through a header update; replacement/removal semantics have not been designed. |
| Publish a later price | Item ID, price mode, Cost, Sell Price, owner-entered reason | Uses ADR-470's account-scoped serializable publish lock. It does **not** take the catalog item's concurrency token; a publish-lock conflict requires reload/retry. |
| Reactivate | Item ID and current item concurrency token | Restores Active using the existing current-price pointer; does not publish a new price. |

Create-draft and activate-existing-draft operations are reserved by Build 108's domain model for the
future actual-work promotion workflow; neither is implemented or exposed in Session 2e.

Account ID, authenticated actor, account-access state, capability entitlement, and permission are
resolved by the API-facing service layer, never trusted from the client request. Existing catalog
items use a `Guid` concurrency token, not a database `byte[]` row version. The application follows
the existing service/persistence pattern; this decision does not introduce MediatR or a generic
domain-event/outbox mechanism.

## Drawer safety, validation, and conflict behavior

- A clean drawer closes immediately. Escape, backdrop, navigation, or an explicit close attempt on
  a changed drawer presents **Discard changes** or **Keep editing**; there is no implicit autosave.
- Server and client validation errors render next to their relevant fields, preserve all entered
  values, and move focus to the first invalid field after submit. Category normalization/race
  recovery remains silent when it can resolve to the existing category; it must not discard the
  item form or show a misleading failure.
- **Save & add another** retains Type, Category, UOM, the read-only account currency, and selected
  pricing mode; it clears display name, SKU, alias, Cost, Sell Price, and any below-cost
  confirmation, then focuses Display Name. `Ctrl+Enter`/`Cmd+Enter` invokes that same action from
  any drawer field. Inline category entry and discard confirmation must have their own accessible
  labels and correctly confined focus; a failed category-refresh recovery must expose a retryable
  error rather than silently stalling.
- On an item-header concurrency conflict, preserve the drawer's entered values and show a clear,
  non-destructive reload-and-review path. On an ADR-470 account publish-lock conflict, do not
  automatically replay the attempted price change; show that another price update completed and
  require the owner to reload current values and deliberately retry.

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
- Replace the current raw ExternalKey-only uniqueness rule with the locked canonical normalized-SKU
  storage/index and apply exactly the same normalization in validation and search. Include collision
  migration/compatibility checks and focused race/normalization tests.
- Reconcile the existing separate create, publish, and activate endpoints with the locked atomic
  `Save & activate` command, including the new version-line price mode. Specify recovery and the
  item-token versus account-publish-lock behavior before code.
- Define the safe mutation contract for the permitted item-header edits; current code does not yet
  expose those mutations.
- Verify mobile focus trapping, drawer dismissal, unsaved-change behavior, query invalidation, and
  direct-route entitlement behavior, including the capability/permission-gated mobile overflow
  entry. Cover keyboard Save & add another focus/reset behavior, duplicate name bounds,
  reactivation concurrency, and cursor reset on every filter change.
- Keep Offering/Assembly entities, package counts, quote composition, and field-image work out of
  the Session 2e implementation change set.
- Treat Session 2e as the validated UI/API preflight and split its several mutation families into
  bounded implementation batches. Do not ship header, atomic creation/activation, later price
  publishing, lifecycle/reactivation, and alias changes as one oversized review change.
