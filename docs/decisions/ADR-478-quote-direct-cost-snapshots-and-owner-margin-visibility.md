# ADR-478 — Quote Direct-Cost Snapshots and Owner Margin Visibility

**Status:** Locked  
**Date:** 2026-08-09  
**Related:** ADR-458; ADR-467; ADR-473; Build Logs 108, 114, 117

## Decision

Each `QuoteLine` carries a nullable, immutable `CostSnapshot` representing its estimated direct
cost at the time that quote revision is created. It is never derived later from a live catalog
price, and it is distinct from the line's customer-facing `UnitPrice`.

For a catalog-backed line, `CostSnapshot` copies the selected published price-book line's cost
snapshot, which may itself be absent. For an off-catalog line, an Owner/Admin may enter an
optional, quote-specific estimated direct cost; it is retained only on that quote revision and
never creates or changes a catalog cost.

Initial entry of an off-catalog estimated direct cost is ordinary quote-line entry and requires no
override reason. An Owner/Admin's manual change to an already-entered cost in a later quote
revision is a manual cost override: it requires a reason and creates the existing
`ManualPriceOverride` audit record with old/new cost, actor, and time. A new revision that merely
copies a newer published catalog cost is not a manual override. This amends Build 108's prior rule
that a `QuoteLine` target has no independent cost and therefore leaves
`ManualPriceOverride.OldCost`/`NewCost` null.

The direct-cost total for a revision sums every line's cost contribution exactly once:

- **Summed assembly:** the primary and each priced associated line contribute their own
  `CostSnapshot`.
- **AllInclusive assembly:** the priced primary contributes only its own direct cost, and every
  `IncludedInPackage` child contributes its own direct cost. A primary item's cost must never be a
  rolled-up duplicate of its included children.
- **Standalone lines:** each contributes its own `CostSnapshot`.

If any line contributing to the quoted work has an unknown cost, aggregate profitability is
**incomplete**. Keep must not display a false quote-level cost, gross-profit, or margin total.

Owner/Admin users may view a revision's `Estimated direct cost`, `Estimated gross profit`, and
`Gross margin`. Operators and customers never receive cost, gross-profit, margin, markup, or
pricing-formula data. Discounting or other price overrides recompute the Owner/Admin figures from
that revision's snapshots and preserve the existing reason/audit rule. Keep does not add a minimum
margin block, target-margin recommendation, automatic pricing formula, payroll model, or net-profit
claim.

## Rationale

An owner considering a discount needs the expected direct-cost impact for the exact price presented
on that revision. Current catalog metrics alone are insufficient because catalog costs and assembly
composition can change after a quote is made, and an all-inclusive package can hide real component
costs. Snapshotting at quote creation preserves an honest, explainable financial record.

The figures are deliberately called *estimated* gross profit/margin: direct costs do not include
all business overhead, taxes, labor burden unless represented in the configured labor cost, or
other inputs required to calculate net profit.

## Consequences

- Session 3.6 includes the nullable `QuoteLine.CostSnapshot` schema and cost-contribution
  invariant when the quote domain is first created. Its preflight must confirm the additional
  field and AllInclusive aggregation proof remain within the batch-size gate.
- A bounded Owner/Admin profitability panel follows the basic quote workbench in Session 3.7; it
  uses revision snapshots only and clearly shows incomplete values.
- Quote cost must not be recomputed from current catalog values for historical display, reporting,
  or a later discount decision.
- Build 108's ERD and any implementation contract must be reconciled to remove the obsolete
  quote-line-cost-null assumption.
