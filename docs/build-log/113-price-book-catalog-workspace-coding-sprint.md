# Build Log 113 — Price Book Catalog Workspace Coding Sprint

**Status:** 2e.1a through 2e.5 are complete, including manual browser acceptance; next implementation
session is 2e.6 preflight (Active-item maintenance)
**Date:** 2026-08-03  
**Scope:** Bounded implementation sequence for Session 2e, following Build 112.  
**Related:** Build 108; Build 110; Build 111; Build 112; ADR-462; ADR-468; ADR-470; ADR-472; ADR-473

## Sprint objective

Deliver a usable, entitled Owner/Admin **Catalog Items** workspace for direct manual price-book
entry. It must be safe for a 50–100 item onboarding pass, usable on phone and desktop, and backed
by bounded server reads and atomic price activation. It does not implement a placeholder Offering &
Packages tab, quote composition, actual-work promotion, image storage, CSV import, or a user-facing
Draft workflow.

Every coding slice remains subject to the standard Claude preflight → Codex validation →
implementation/review gate. The named production-file counts are targets, not permission to exceed
the normal session limits; any exception requires an explicit decision before code lands.

## Order and dependencies

| Slice | Goal | Depends on | Completion gate |
| --- | --- | --- | --- |
| **2e.0 — Mechanical preflight** | Reconcile Build 112 with current entities/endpoints/UI. Propose exact files, migration plan, API shapes, query indexes, test plan, and batch sizes. | Build 112 | Validated plan; no code. Resolve any schema/data-migration issue before 2e.1. |
| **2e.1a — Canonical SKU foundation (complete)** | `SkuNormalizer` (ASCII `[A-Za-z0-9]` strip/lowercase), `CatalogItem.NormalizedExternalKey` plus a dedicated invalid-SKU error for an all-punctuation key (e.g. `---`) that normalizes to empty, catalog EF config unique index, `ICatalogItemPersistence`/EF persistence, lifecycle service, and the normalized-key backfill migration. | 2e.0 | Domain/configuration/migration tests prove same-canonical-SKU rejection, empty-after-normalization rejection, and safe backfill of existing data. No UI, no new workspace route. |
| **2e.1b — Pricing-mode foundation (complete)** | `PriceBookLinePricingMode` enum, `PriceBookVersionLine`/`PriceBookVersion` invariant, version-line EF config, and `EfPriceBookPublishPersistence` (the only current `CreatePublished` caller — deriving `StandalonePrice` when Sell Price exists, else `NoStandalonePrice`), plus the pricing-mode backfill migration. | 2e.1a | Domain/configuration/migration tests prove price-mode invariants both directions and safe legacy backfill. No API contract change, no UI. |
| **2e.2 — Atomic creation API (complete)** | Deliver one authorized Create-and-Activate operation: header, optional initial aliases, price mode, Cost/Sell Price, fixed `Initial catalog price` audit, and activation in one serializable transaction. `POST /keep/pricebook/catalog-items/create-and-activate` replaced the prior separate draft-create/draft-activate routes and `CatalogItemApiService` methods outright — they were removed, not left reachable alongside it. The transaction needs an intentional two-phase save (insert with a null price pointer, then repoint and save again) because `CatalogItem` and its own `PriceBookVersionLine` hold FKs back to each other, which EF cannot resolve in one `SaveChanges` call. | 2e.1a, 2e.1b | Success leaves a complete active item; each failure leaves no partial item/alias/version/audit row; entitlement/account isolation, SKU/category conflicts, price-mode invariants, and publish-lock conflicts are integration-tested. Full regression is clean (1,364 unit tests, 14 architecture tests, both integration suites, `git diff --check`). The new concurrent-create race test was improved (thread-pool scheduling plus pre-warmed connections) from ~50% to ~19/20 passing locally, but — reported honestly — retains the same class of real-Postgres-timing flakiness the existing `PriceBookPublishApiTests` concurrency coverage already carries; eliminating it fully would need test-only synchronization hooks in production code, out of this batch's scope. |
| **2e.3 — Catalog read contract (complete)** | Deliver bounded catalog list/detail/category-choice reads: server search, filters, cursor, stable ordering, current price mode/value, aliases, and match reasons. | 2e.1a, 2e.1b | Queries are account-scoped, paged, and stable; tests cover canonical SKU search, shared aliases, active/inactive filtering, match rank/reason, and cursor tie-breaks. No client-side unbounded catalog load. |
| **2e.4 — Workspace shell and navigation (complete)** | Add Price Book route/state, entitled desktop and mobile navigation entries, unavailable direct-access handling, API client types, and the list shell. | 2e.3 | Owner/Admin with entitlement can reach the empty/list/loading/error states on desktop and mobile; field roles and unentitled accounts cannot discover or operate it. No creation drawer yet. |
| **2e.5 — Create-and-activate drawer (complete)** | Build the responsive New Item drawer: names/SKU/UOM/category/type/common-item/aliases, price mode, below-cost confirmation, inline category creation, atomic submit, and Save & add another. The correction pass applies Build 112's 2026-08-05 amendments: owner-friendly grouping/copy, UOM default/suggestions while retaining free text, true retained-value Save & add another, Ctrl/Cmd+Enter, validation focus, accessible confirmations, robust category-race recovery, and the single-CTA catalog onboarding state. | 2e.2, 2e.4 | Desktop drawer/mobile full screen, dirty-dismiss protection, field errors/focus, keyboard flow, race-safe category selection, post-create list refresh, and distinct loading/error/empty states are covered by focused automated checks. No Save Draft UI. ADR-468 explicitly approves the current USD-only pilot posture: send `USD` deliberately, keep currency non-selectable, and defer a server-owned currency source until non-USD accounts are in scope. Manual entitled-app acceptance has covered empty and populated desktop/mobile states, reachable drawer footer, create/refresh, and keyboard/error paths. |
| **2e.6 — Active-item maintenance** | Add active-item detail/edit: mutable header fields, explicit alias management, current-price display, republish-price flow with owner reason, and clear item-token vs publish-lock conflict handling. Where both current Cost and Sell Price exist, add the owner/admin-only derived gross-profit, margin, and markup display locked in Build Log 114; it is read-only and introduces neither persisted values nor pricing formulas. | 2e.3, 2e.4 | Type/UOM render as immutable metadata; header updates and alias actions use item token; later price publishing uses only ADR-470 lock and does not auto-replay conflicts. Absent inputs leave all metrics unavailable; division-specific metrics are unavailable at a zero denominator. |
| **2e.7 — Lifecycle and operating-speed polish** | Add inactive filter/reactivation, final filter/search/pager behavior, no-standalone-price rendering, filtered no-results states, shortcuts help, and accessible mobile/desktop interaction polish. Replace the creation editor's native category select with one accessible searchable, creatable combobox and reuse its selection/search model for the category filter; preserve an explicit No category choice and duplicate-safe category creation. Keep the Type/Category desktop grid stable during create; category creation/race recovery disables every item-save path until it resolves. Pair Cost and Sell Price in a desktop row (stacked on mobile), with the no-standalone-price choice grouped with Sell Price. | 2e.4–2e.6 | Reactivation preserves current price pointer; all filter changes reset cursor state; browse/search ordering is stable; keyboard, focus, and screen-reader behavior pass focused tests. Category selection works with a short or long account-owned list without seeded taxonomy or a separate divergent filter control; a delayed category request cannot yield an uncategorized item. Desktop pricing keeps Cost/Sell comparable while mobile remains usable. |
| **2e.8 — Completion verification and handoff** | Run full proportionate suites, accessibility/interaction audit, document endpoints and deferrals, and reconcile Build 112/113/session log. | 2e.1a–2e.7 | No scope leakage; full required checks green; manual desktop/mobile happy paths and conflict/error paths recorded. |

## Mutation-family boundaries

- **2e.1a** and **2e.1b** are schema/domain work only, split because 2e.1b's only production
  caller (`EfPriceBookPublishPersistence`) pushed the combined slice past the 8-production-file
  gate. Neither bundles API or UI.
- **2e.2** owns the new atomic creation/activation family only.
- **2e.3** is read-only.
- **2e.4**, **2e.5**, and **2e.7** are frontend-led; do not use them to introduce unplanned server
  mutations.
- **2e.6** may add the one header-update family and consume the existing explicit alias and later
  price-publish families. It must not invent bulk alias replacement, Type/UOM mutation, or a generic
  catalog PATCH endpoint.
- If the 2e.6 preflight shows those changes cannot remain reviewable, split header/alias UI from the
  later-price UI rather than relaxing the batch gate.

## Shared acceptance rules

1. Server authority is unchanged: account access → capability entitlement → user permission precedes
   every mutation and sensitive read.
2. Catalog queries are server-filtered and cursor-paged; filter changes discard old cursor state.
3. `ExternalKey` display text is preserved, while the canonical normalized SKU is the account-wide
   uniqueness/search key. Aliases may be shared across items and results state their literal match
   reason.
4. Initial pricing is atomic and audit-safe. Subsequent prices use the account-wide ADR-470 lock;
   header/alias/lifecycle actions use the catalog item's `Guid` concurrency token.
5. A user-visible Draft workflow, Offering/Assembly relationships, package usage badges, quote
   generation, field-role catalog access, tax/margin engines, and images remain out of scope.

## Verification baseline per slice

- Focused domain/application/integration tests for every changed server rule.
- Focused frontend tests for each altered user flow, plus `tsc --noEmit` and the established CSS-token
  check where frontend files change.
- `git diff --check` for every slice.
- At 2e.8: full relevant backend suites, frontend suite, production build, and a manual mobile and
  desktop pass using an entitled Owner/Admin account.
