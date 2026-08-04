# ADR-474 — Emergent Scope Recipes And Grouped Quote History

**Status:** Locked  
**Date:** 2026-08-04  
**Related:** ADR-453; ADR-456; ADR-457; ADR-458; ADR-461; ADR-473; DEF-088; DEF-089

## Decision

Keep's future reusable-scope capability is a single composable **scope recipe** model, not a
library of opaque traditional bundles and not separate bundle, modifier, and checklist systems.
A recipe is a reusable, named selection of catalog and labor lines that expands into ordinary,
editable proposed-scope or quote lines.

The capability must be introduced from real work upward:

1. A business may create a normal request-bound scope/quote with recognizable catalog items,
   quantities, and labor lines.
2. An Owner/Admin may select suitable lines from that completed real-work composition and save them
   as a named reusable recipe (for example, `Standard Water Connection Kit`). Field users may
   propose that a selection be saved, but may not silently create or alter the account's reusable
   library in the first version.
3. A later scope may add the recipe in one action; it expands into visible ordinary lines that the
   authorized user may review and edit for that job.
4. Only after actual use establishes recurring variants may Keep add an anchor/primary-work entry
   point, optional modifier groups, and condition-driven checklist recommendations over the same
   recipe/line model.

Every expansion must retain immutable source and grouping snapshots sufficient to reconstruct what
was selected: recipe/assembly name, relevant component descriptions and quantities, display order,
and the then-current catalog/price snapshots required by ADR-458. Later catalog or recipe edits
affect future selections only; they never rewrite submitted scopes or quote revisions.

Keep maintains two representations of the same work:

- **Operational representation:** the office/field view remains itemized and auditable. Recipe
  expansion never hides the actual materials or labor lines from authorized staff.
- **Presentation grouping:** the stored recipe/assembly source and grouping metadata may later
  render a concise customer-facing scope summary (for example, a named replacement package with an
  included-work description). It does not authorize customer quote delivery, approval, PDFs, or
  Good/Better/Best options in V1; those remain deferred under ADR-473 and DEF-088.

The initial field workflow remains ADR-456's price-blind proposed-scope capture. Its progressive
selection path is primary offering, Common Items, client categories, deterministic catalog
name/SKU/alias search, then off-catalog capture. Catalog search is therefore the dependable
fallback, not the intended long-term primary experience. Reusable recipes, then anchors/modifiers,
make common work faster only after a business has created or approved them from real jobs.

## Consequences

- Do not require a pilot business to create abstract assemblies, modifier groups, or a complete
  bundle library before it can scope or quote its first job.
- Do not automatically seed a pilot account with fictitious trade prices or supposedly universal
  parts. Any future optional starter recipes must be editable and mapped to that account's real,
  reviewed catalog; early pilot onboarding may instead use a bounded, founder-assisted setup of the
  business's common jobs.
- The static office-owned associated-item assemblies in ADR-457 remain the bounded MVP behavior.
  This ADR locks the direction for their evidence-led evolution; it does not pull nested,
  conditional, compatibility, automatic-selection, customer-delivery, or technician-priced quote
  work into the current pilot scope.
- A checklist initially recommends or preselects a visible addition and explains its rationale.
  It must not silently add chargeable work. A later mandatory safety/code rule requires an explicit
  separate policy decision and visible explanation.
- Owner/Admin remains the sole V1 pricing and approval authority. If pilot evidence shows that
  office review harms an in-home sale, a later decision may introduce mobile one-minute approval or
  owner-configured, narrowly bounded pre-approved recipes/price ceilings. It must not become
  unrestricted technician pricing by implication.

## Rationale

Traditional Good/Better/Best packages and large rigid bundle libraries create duplicate maintenance
and "bundle sprawl." A composable recipe model allows a contractor to reuse the work they actually
repeat while preserving a transparent financial and operational record.

Forcing abstract configuration at onboarding would trade long-term maintainability for immediate
abandonment risk. Bottom-up creation captures the same reusable structure during real quoting, when
the owner already knows what the selected lines mean. Grouped presentation preserves a future
customer-friendly explanation without weakening office/field auditability or prematurely expanding
the V1 quote boundary.
