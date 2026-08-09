# Build Log 116 — Price Book Continuation And Field-Scope Handoff

**Status:** Handoff inventory — Build Log 117 sequences the next required preflight and coding sessions  
**Date:** 2026-08-09  
**Scope:** Complete the locked internal Price Book, Quotes & Materials foundation after Session 2e's
catalog workspace.  
**Related:** Build 107; Build 108; Build 109; Build 112; Build 113; Build 114; ADR-456 through
ADR-468; ADR-473 through ADR-475; DEF-088; DEF-089.

## Why this follows Session 2e

Session 2e completed the entitled Owner/Admin catalog workspace: catalog items, categories, aliases,
direct price entry/versioned publication, lifecycle maintenance, and desktop/mobile verification. It
did **not** complete the broader Price Book, Quotes & Materials capability.

In particular, the catalog workspace deliberately excluded Offering/Assembly relationships, a
user-visible draft workflow, quote generation, field-role catalog access, actual-work promotion,
and image storage. Build 114 requires the separately decided Offering/Assembly foundation to be
preflighted before quote composition. Image storage is paused as a separate dependency; it must not
displace the unimplemented Price Book workflow below.

## Completed foundation

- Account capability-package enrollment and account-aware Price Book entitlement (ADR-462).
- Owner/Admin catalog items: canonical SKU, categories, aliases, current price, immutable price
  versions, direct entry/publish, audit, lifecycle, and the Price Book workspace (Sessions 2a–2e).
- Owner/Admin-only profitability presentation where Cost and Sell Price are available.

## Locked but unimplemented Price Book capability

| Capability | Locked behavior | Implementation status |
| --- | --- | --- |
| Static offerings and assemblies | Owner/Admin configures a primary offering with associated material, equipment, service/labor, or supply items. Each assembly uses either summed published lines or one all-inclusive priced parent with included children, never both. No nested, conditional, compatibility, or automatic-selection assemblies. | Not built. |
| Technician proposed scope | On an existing request, field users capture internal work/material scope only; they cannot view or edit cost, price, margin, tax, formulas, or inventory. | Not built. |
| Field selection ladder | The fixed path is Primary offering → Common Items → account categories → deterministic Name/SKU/Alias search → always-available Off-Catalog Item. Search is literal/deterministic, never AI/fuzzy. | Not built for field roles. |
| Off-catalog field capture | A technician can always submit a one-off item with required description and quantity. It requires office review and never automatically creates a catalog item or price. | Not built. |
| Office review signal | Submitted scopes raise the additive `Proposed scope needs office review` work signal. It resolves only when no submitted scope remains for the request; a later visit creates a new scope rather than reopening a reviewed one. | Not built. |
| Authorized catalog promotion | After office review, an Owner/Admin may deliberately create a traceable catalog candidate from an off-catalog item and subject it to normal catalog/pricing authority. No automatic promotion. | Not built; needs reconciliation with 2e's deliberate no-user-visible-Draft policy. |
| Office quote | An internal, request-bound, single-option, tax-included quote with immutable revisions, line snapshots, round-half-up totals, audited overrides, submit/approve lifecycle, and no customer delivery/acceptance claim. | Not built. |
| Actual work/material records | Authorized field/office users record actual use, including ad-hoc/off-catalog lines, with a deliberate promotion path where approved. | Not built. |
| Entitled web/mobile workflows | Role-appropriate Owner/Admin review and field scope/actual-use surfaces, including unavailable/read-only behavior when entitlement changes. | Not built. |

## Technician-to-office workflow already decided

```text
Technician on an existing request
  -> select a primary offering, Common Item, category/search result,
     or the always-available Off-Catalog Item
  -> record internal proposed work/material scope without price visibility
  -> submit scope
  -> "Proposed scope needs office review" is raised
  -> Owner/Admin reviews and edits the office-owned scope/quote
  -> Owner/Admin may deliberately curate an off-catalog item into the catalog
     and publish its price under normal catalog authority
```

An off-catalog entry remains single-use unless that explicit Owner/Admin action succeeds. A
technician neither changes the shared catalog nor obtains price authority by entering a new item.

## Required preflight sequence

The next preflight must turn the following into bounded implementation batches; it must not combine
them merely because their entities are related.

1. **Offering/Assembly foundation:** office-owned assembly data model, price treatment, ownership,
   activation, and management surface.
2. **Proposed-scope foundation:** request-bound field scope/line model, the five-rung selection
   read model, off-catalog capture, submission lifecycle, and `KeepRequestWorkSignal` integration.
3. **Office scope review and catalog curation:** review/resolution semantics and an explicit,
   traceable off-catalog-to-catalog action. Reconcile the older "catalog draft" terminology with
   Session 2e's atomic create-and-activate/no-user-visible-Draft policy before code begins.
4. **Office quote foundation:** quote/revision/line snapshots, assembly expansion, price treatment,
   money rules, manual override audit, and internal approval workflow.
5. **Actual work/material records:** authorized actual-use lines, one-off capture, and any approved
   catalog-promotion traceability.
6. **Entitled surfaces and verification:** PWA/mobile scope capture, Owner/Admin review, direct
   access/entitlement degradation, and proportionate browser/device acceptance.

The preflight may split these further to preserve the repository's normal review gates.

Build Log 117 now records the ordered Session 3 plan and the mandatory decision/reconciliation
checkpoint before implementation begins.

## Intentional exclusions from this completion path

- Customer quote delivery, customer acceptance/decline, signatures, public quote links, and
  Good/Better/Best option groups remain deferred under ADR-473, ADR-475, and DEF-088.
- Evidence links remain optional. Image storage is a separate preflight and is paused until the
  internal Price Book continuation is completed; no scope/quote/actual-work slice may depend on
  image upload to function.
- Reusable recipes, anchors/modifiers, and guided condition checks remain evidence-led follow-up
  work under ADR-474 and DEF-089. They do not replace the initial static assembly model.
- Dynamic pricing, tax engines, inventory, procurement/accounting sync, technician pricing
  authority, generic CSV import, and a generic catalog draft UI remain outside this sequence.

## Source decisions

- [Build Log 108](108-price-book-quotes-materials-erd-preflight.md): data model and original
  staged sequence.
- [Build Log 114](114-price-book-model-alignment.md): static assemblies are decided-but-unbuilt and
  precede quote composition.
- [ADR-473](../decisions/ADR-473-direct-request-bound-quote-workflow.md): internal quote boundary.
- [ADR-474](../decisions/ADR-474-emergent-scope-recipes-and-grouped-quote-history.md): later
  evidence-led recipe evolution.
- [Deferred topics](../deferred-topics.md): DEF-088 and DEF-089 boundaries.
