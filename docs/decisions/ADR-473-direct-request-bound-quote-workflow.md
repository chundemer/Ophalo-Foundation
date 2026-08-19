# ADR-473 — Direct, Request-Bound V1 Quote Workflow

**Status:** Locked
**Date:** 2026-08-02
**Related:** ADR-458; ADR-461; ADR-465; ADR-467; ADR-468; ADR-472; build-log/107; build-log/108; DEF-088
**Amended by:** ADR-487 — the office commercial record now supports Estimate, Fixed-Price Quote,
and T&M Authorization. The request-bound, office-controlled, revisioned, price-authority, and
no-customer-acceptance boundaries here remain in force.

## Decision

V1 quoting uses the curated, directly maintained Ophalo price book. A quote always belongs to an
existing Keep request; V1 does not create free-standing quotes.

A technician may capture a proposed scope from the field without seeing or editing price, cost,
margin, tax internals, or pricing formulas. An Owner/Admin may also begin a quote from an existing
request, including for a phone or office-originated inquiry. Owner/Admin alone reviews and edits
scope, chooses catalog items or offerings, sets quantities, and creates/submits/approves the quote.

V1 supports one scope and one quote option at a time. An off-catalog entry is a single-use quote/scope
input and never automatically creates or promotes a catalog item. Labor is represented as a normal
`Service` catalog item, with a flat-rate or hours-based unit of measure. V1 has no labor-rate,
technician-wage, overhead, markup, margin, or automatic pricing-rule engine.

The existing quote lifecycle is internal and remains unchanged:

`Draft → SubmittedForApproval → Approved`

An edit after approval creates a new immutable quote revision and returns the quote to `Draft`, as
locked by ADR-465. `Approved` is an internal office approval; it does not mean a customer accepted
the quote.

Quoted catalog prices and totals remain tax-included under the existing Build 107/ADR-468 MVP
posture. Keep snapshots those values in the immutable quote revision but does not calculate,
break down, look up, remit, or export tax.

## Consequences

- Do not add V1 `Sent`, `Accepted`, or `Declined` quote statuses; public/customer quote links,
  delivery/open tracking, electronic signatures, and recorded customer decisions are deferred.
- Do not add Good/Better/Best option groups or a parent-quote schema. If a business needs alternate
  proposals during pilot, the office manages them outside the V1 quote workflow.
- The direct price-entry/publish preflight must preserve the request boundary, quote revision
  snapshots, role separation, and tax-included totals above.
- Images and attachment purposes are deliberately outside this ADR and require their own later
  workflow decision.
