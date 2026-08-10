# ADR-479 — Live OfferingAssembly Lifecycle and Computed Eligibility

**Status:** Locked  
**Date:** 2026-08-09  
**Related:** ADR-461; ADR-466; ADR-473; Build Logs 108, 117

## Decision

An `OfferingAssembly` is an Owner/Admin-controlled, live reusable configuration. Owner/Admin may
edit its primary item, associated items, quantities, optionality, display order, name, and
`PriceTreatment` while it is Active; no deactivate/edit/reactivate ceremony is required. The
existing ADR-466 uniqueness rule remains enforced whenever an active primary item changes.

Active is the Owner/Admin lifecycle choice. **Operational eligibility** is a separately computed
predicate used to permit new field selection and new quote composition; it never rewrites the
stored Active state:

- every referenced catalog item must be Active;
- a `Summed` assembly requires the primary and every associated item to have a current published
  `StandalonePrice`;
- an `AllInclusive` assembly requires the primary to have a current published `StandalonePrice`;
  active included children may have `NoStandalonePrice`.

An assembly that fails the predicate is excluded from new selection/composition and visibly flagged
to Owner/Admin as needing attention. Catalog-item inactivation, missing required price, or a
structural assembly edit may make it ineligible, but never cascades an automatic assembly
inactivation. Repricing a still-valid referenced item does not make an assembly ineligible; future
quotes use current published prices while existing quote revisions remain immutable.

An existing technician draft retains the assembly expansion it selected. A later live assembly or
catalog change never silently rewrites that draft and does not block ordinary price-blind scope
submission. The office-review workflow handles the resulting context. Submitted scopes and quote
revisions retain their established immutable snapshots.

Field-presentable pricing is deferred by ADR-477/DEF-093. This ADR intentionally establishes no
field-price visibility, customer presentation, or policy-approved quote path.

## Rationale

Parts substitution and offering maintenance are routine business work. Forcing an Owner/Admin to
retire and reactivate a package for every change adds ceremony without protecting history, because
scope and quote snapshots already preserve what was selected and quoted. A computed eligibility
predicate prevents a broken configuration from being used for new work without silently overriding
the owner's lifecycle decision or hiding the assembly in an unrelated inactive view.

## Consequences

- Session 3.1 owns the domain model, persistence, account isolation, eligibility query/invariant,
  and migration proof; Session 3.2 owns the Owner/Admin management surface and needs-attention
  presentation.
- The ordinary field workflow remains price-blind. It may record real work via the existing
  off-catalog escape hatch when an assembly is not eligible for new selection.
- No automatic catalog-to-assembly cascade write, no nested assembly, conditional selection, or
  compatibility engine is introduced.
