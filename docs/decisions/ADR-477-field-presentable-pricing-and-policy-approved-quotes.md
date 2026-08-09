# ADR-477 — Field-Presentable Pricing and Policy-Approved Quotes

**Status:** Proposed — requires explicit promotion decision  
**Date:** 2026-08-09  
**Related:** ADR-461; ADR-465; ADR-473; ADR-475; Build Logs 108, 114, 117

## Proposed decision

Field users remain price-blind while diagnosing and constructing a proposed scope by default:
they do not see cost, margin, markup, pricing formulas, tax internals, or unrestricted catalog
prices, and they never edit prices.

Owner/Admin may explicitly designate a selectable unit as **field-presentable** when it has a
complete, office-published customer price. This is distinct from `IsCommonItem`: Common Items are
a selection-speed aid, while field-presentable is permission to show a customer-safe price. A
standalone CatalogItem is eligible only with `StandalonePrice`; an assembly is eligible only when
its configured price treatment yields one complete, customer-safe total. `NoStandalonePrice`
items cannot be field-presentable.

This capability must be separately and explicitly enabled at the account level before any
field-presentable configuration or Operator presentation is available. It is not implied merely by
the account's general Price Book entitlement or by setting an item-level flag. Session 3.0 must
choose the exact capability/permission key, default posture, and Owner/Admin configuration surface
using ADR-460 and the existing `AccountFeatureAccessResolver` pattern.

An Operator may select any authorized scope item, but may use **Present price** only when every
line in the scope is field-presentable and pre-priced. The presentation shows customer-safe
descriptions and totals only. A presentable assembly is one customer-facing unit: its associated
material/labor lines, cost, margin, and internal price construction are not exposed or editable in
the field view.

Present price creates an immutable, request-bound `OfficeQuote` revision from the exact published
price snapshots at that moment. It is `Approved` under a recorded
`PublishedFieldPricingPolicy` approval basis rather than a human-reviewer action. The record
retains the actor, presentation time, source price snapshots, and approval basis. It records no
customer sent/accepted/declined decision, signature, delivery link, or proof of customer receipt.

Present price performs the same server-authoritative authorization-constraint comparison as office
quote review. When an active NTE, customer-budget, insurer-limit, or equivalent request constraint
is exceeded, the field user receives the existing non-blocking over-limit warning before they
confirm presentation. It does not bypass, suppress, or turn the warning into an automatic block;
no separate client-authored "warning shown" fact is persisted.

If an off-catalog, non-presentable, or bespoke line enters the scope, Present price is unavailable.
The existing Owner/Admin pricing/review path applies. A changed scope creates a new immutable quote
revision: it may use the policy-approved path again only when every resulting line still qualifies;
otherwise it returns to the ordinary Draft → SubmittedForApproval → Approved workflow.

## Rationale

Small contractors often use a flat-rate book in the field. Requiring an office round trip for a
known, already-authorized callout, diagnosis, or repair package creates avoidable customer-facing
friction. Treating every published internal component price as customer-presentable would instead
leak incomplete or internal pricing and permit misleading totals.

The explicit field-presentable policy lets the office pre-authorize routine customer-facing units,
while preserving office control for exceptions. Reusing immutable `OfficeQuote` revisions avoids a
second, quote-like document type and preserves exactly what a technician showed even after later
price-book changes.

## Consequences

- If promoted, narrowly supersedes ADR-461's blanket field-price hiding rule and ADR-473's
  Owner/Admin-only quote approval rule as stated above; all other cost/margin and
  pricing-authority restrictions remain in force.
- `OfficeQuote` needs an immutable approval-basis/provenance field. A policy-approved quote must
  not claim a human reviewer who did not review it.
- If promoted, Session 3.1/3.2 define field-presentable assembly/item configuration. Sessions
  3.3/3.4 provide price-safe field read models and scope capture; Sessions 3.6/3.7 implement
  quote snapshot and presentation behavior.
- This is staff-mediated field presentation, not customer-facing quote delivery. ADR-475's
  customer delivery, decision, signature, and acceptance work remains deferred.

## Promotion gate

Do not implement this proposal until the product owner explicitly decides to promote it into the
current Price Book sequence. That decision must also lock the account-level gate described above
and preserve ADR-460's non-blocking authorization-constraint warning on the policy-approved path.
