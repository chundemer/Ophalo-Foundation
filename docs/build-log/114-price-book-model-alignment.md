# Build Log 114 — Price Book Model Alignment

**Status:** Locked clarification; no implementation started  
**Date:** 2026-08-06  
**Related:** ADR-453; ADR-455; ADR-457; ADR-458; ADR-460; ADR-473; Build Logs 107, 112, and 113

## Purpose

Validate the pilot contractor's furnace-install price sheet against the already locked Price Book
model and the code delivered through Session 2e. This record corrects an erroneous reframing of
existing decisions as newly discovered architecture. It does not authorize a generic spreadsheet
formula engine, a tax engine, or an assembly implementation within the current catalog-item batch.

## Confirmed decisions

1. **One catalog-item form remains correct.** The existing Type (`Material`, `Equipment`,
   `Service`, or `Fee`) and Category fields keep routine catalog entry uniform. The types do not
   currently require divergent forms. This preserves ADR-455's role-appropriate progressive
   disclosure and avoids parallel UI with no behavioral value.

2. **Standard consumables belong in office-owned static associated-item assemblies.** ADR-457
   already provides the generic furnace-install shape: an offering/assembly selects a primary item
   and expands associated equipment, materials, services/labor, and supplies as appropriate. An
   assembly explicitly uses either summed published lines or one all-inclusive fixed parent price
   with linked included children, preventing double charges. Assemblies are decided but unbuilt;
   they are not a missing CatalogItem field or a new client-specific requirement.

3. **The sheet's Tax column is cost-side purchase tax, not customer sales tax.** It can inform the
   business's internal cost, but does not reopen ADR-458/ADR-473's prohibition on V1 customer-tax
   calculation, jurisdiction lookup, exemptions, remittance, invoicing, or accounting export.
   Customer-facing V1 quoted prices remain tax-included fixed prices.

4. **Dynamic pricing remains deferred.** Do not add target-margin/markup-to-price formulas,
   spreadsheet formula compatibility, or automatic labor/consumables/tax calculation. These remain
   outside ADR-453/ADR-473's bounded MVP and require pilot evidence plus a separate decision.

5. **Owner/Admin profitability visibility is a bounded 2e follow-up.** Existing immutable price
   snapshots already contain Cost and Sell Price. Where both values exist, the catalog detail/current
   price view may display, only to Owner/Admin users:

   ```text
   Gross profit = Sell Price - Cost
   Margin %     = Gross profit / Sell Price
   Markup %     = Gross profit / Cost
   ```

   This is read-only derived presentation, not new persisted data, an automatic pricing rule, or
   field-role price/cost visibility. An absent Cost or Sell Price leaves all three unavailable. When
   Cost is zero, retain the valid gross-profit and margin values but show markup as unavailable;
   when Sell Price is zero, show margin as unavailable. It belongs with Session 2e.6's
   current-price/detail work and must use the same owner/admin authorization boundary as Cost.

6. **Category governance is a later, dedicated catalog-maintenance follow-up.** ADR-461 remains
   unchanged: categories are optional, account-owned, client-named browse labels, never a seeded
   trade taxonomy or pricing rule. Session 2e.7 already owns the category filter so an Owner/Admin
   can browse a group and search within it. After the current 2e sequence, scope basic category
   maintenance separately: rename, assigned-item count, and inactivation. Do not infer a need for
   bulk merge/reassignment tooling until pilot use demonstrates it.

7. **Category inactivation clears current assignments.** When an Owner/Admin inactivates a category,
   Keep must show the assigned-item count and require confirmation. On confirmation, one atomic
   operation marks the category inactive and sets `CategoryId` to null for every current catalog
   item assigned to it, including inactive items. The inactive category is unavailable for future
   assignment and absent from normal category filters/choices. This is not deletion and does not
   alter immutable published-price, quote, or actual-work history. Reactivation does not restore
   prior item assignments automatically; the Owner/Admin deliberately assigns categories again.

8. **Category selection must scale beyond a short list.** A native select is acceptable for the
   pilot's small category set, but it is not the durable category-entry experience once an account
   has many or similarly named groups. Session 2e.7 will replace the catalog editor's native
   selector with one accessible, searchable category combobox and reuse its search/selection model
   for the catalog category filter. It retains an explicit `No category` clear choice and offers
   creation only when the entered normalized name does not exactly match an existing category. This
   is a browse/entry refinement only: it adds no seeded taxonomy or pricing behavior.

9. **2e.7 drawer interaction refinements are bounded.** The creatable category combobox must
   preserve the desktop Type/Category grid rather than expanding into a separate nested creation
   form. Item creation is unavailable while category creation or duplicate-race resolution is
   pending, preventing an intended category from being silently omitted. Cost and Sell Price share
   a desktop row and stack on mobile; this is visual comparison support only, not a margin rule.
   `Common item` may be grouped nearer Type/Category but must not be renamed to imply an already
   available Quick Add/pinning feature. UOM quick-fill remains literal-value fill with no automatic
   focus jump.

10. **Potential duplicate awareness is a later, non-blocking entry assist.** Pilot use may justify
    showing a small, debounced set of **Similar catalog items** while an Owner/Admin enters a new
    display name. It must be advisory, never prevent saving, and clearly distinguish similarity
    from a duplicate: related materials may intentionally have similar names, sizes, brands, or
    prices. Do not scan only the currently rendered catalog page or load the full catalog into the
    browser; that would miss paged/filtered items and produce misleading confidence. A later
    evidence-led slice may use the bounded server search read path, cap the results, and offer a
    direct way to inspect an existing item. It is not part of 2e.7b's category-combobox work or
    2e.7c's layout/accessibility polish.

## Delivery boundary

Finish Session 2e's catalog-item work and manual verification as planned. Scope the separately
decided Offering/Assembly foundation in its own preflight before quote composition. Do not attach
structured labor-hours, consumables, or purchase-tax fields to every flat CatalogItem in the current
session; component detail is an assembly concern. The former spreadsheet-import staging fields were
not retained by the direct-entry MVP pivot and do not create a requirement to revive import.
