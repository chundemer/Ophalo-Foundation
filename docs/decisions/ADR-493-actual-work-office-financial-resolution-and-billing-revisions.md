# ADR-493 — Actual Work Office Financial Resolution and Billing Revisions

**Status:** Locked — implementation deferred until the active field-pilot release slices are complete  
**Date:** 2026-08-27  
**Related:** ADR-463, ADR-487; Build Logs 129 and 131

## Context

Actual Work is a price-blind field fact. A submitted visit and its field lines are immutable, yet
ordinary service work can include custom, off-catalog, or incompletely configured catalog lines
with no valid sell-price or Standard/Expected Direct Cost snapshot. The office must be able to
complete those financial facts without changing what the technician recorded.

The pilot also needs a durable manual-billing handoff before CSV export exists. A long-running
request may have reviewed, billable visits before all requested work is complete; request closeout
must not delay legitimate progressive billing. Conversely, a copied/printed work summary with no
durable membership record cannot prevent double billing or explain what the office entered into its
existing accounting system.

## Decision

### 1. Immutable field fact; separate office financial resolution

`ActualWork` and `ActualWorkLine` remain immutable after submission. Owner/Admin may resolve a
missing financial value through an immutable `ActualWorkLineFinancialResolution` record, never by
updating `SellPriceSnapshot` or `StandardExpectedDirectCostSnapshot` on the submitted field line.

A resolution applies to any submitted line missing a valid sell-price and/or Standard/Expected
Direct Cost snapshot, whether the line is custom, off-catalog, or catalog-backed. It may fill only
missing values; changing a valid captured snapshot is a correction/adjustment concern, not a
financial resolution.

Each resolution records the affected line, resolved unit sell price and/or unit Standard/Expected
Direct Cost, required reason, basis (`SupplierReceipt`, `OwnerSetPrice`, `FixedAgreement`, or
`Other`), Owner/Admin actor, and timestamp. Correcting a resolution is additive/audited; it does
not rewrite prior financial evidence. Financial read models calculate totals from captured snapshots
plus the applicable immutable resolutions and identify every remaining blocker.

### 2. Review and billing eligibility are distinct, server-derived facts

A submitted visit is billing eligible only when it is reviewed, all required financial values are
captured or resolved, it has no unresolved correction, and it is not already reserved by an active
Billing Revision. Eligibility is a server-owned projection with explicit blocking reasons, not a
client-written boolean.

Zero-line `DiagnosticOnly`, `NoWorkAuthorized`, and `NoAccess` visits are not automatically billing
eligible. Office review must explicitly record a no-charge disposition or a real billed/addendum
line must exist.

The existing Actual Work review signal remains aggregate request work: it resolves only when every
submitted visit on the request is reviewed. It is not a billing, invoice, or request-closeout state.

### 3. Billing Revision is the manual-handoff and future-export boundary

An Owner/Admin creates a request-bound `BillingRevision` from one or more currently billing-eligible
visits. It is the immutable package used first for manual entry into the existing accounting system,
and later as the sole source for CSV export. A revision contains its selected visits, resolved line
financial facts, request/customer/service-location context, totals, completeness state, and review
audit information.

The initial lifecycle is:

```text
Draft -> ReadyForBilling -> HandedOffToBilling -> Voided
```

- A visit can belong to at most one active revision. Voiding a Draft revision releases its visits.
- `ReadyForBilling` freezes the revision's membership and financial contents. A change voids the
  revision with a required reason and creates a later revision; it never removes a visit in place.
- `HandedOffToBilling` records `HandedOffByAccountUserId`, `HandedOffAtUtc`, and an optional external
  billing reference such as a QuickBooks invoice number. It does not claim payment, reconciliation,
  or invoice creation by Keep.

Progressive billing is permitted: a request may remain operationally open while one revision is
handed off and later field visits are captured/reviewed. Request **Work completed** and final request
closeout remain separate operational lifecycle decisions.

### 4. Corrections preserve history and accounting truth

A correction explicitly declares `Addendum` or `Replacement`.

- An **Addendum** is additional/missed work. The original visit remains eligible; the addendum is a
  separate factual visit that must itself be resolved and reviewed.
- A **Replacement** corrects erroneous factual work. Before billing handoff, it voids the affected
  Draft/Ready revision with a reason and the replacement later becomes eligible after review. After
  handoff, it creates a later adjustment Billing Revision linked to the prior revision; it never
  mutates the prior handoff.

The correction Draft is linked and auditable. It may be owned by a qualified Owner/Admin under the
existing recorder-ownership and Draft-transfer rules; correction work is not forced back to the
original field recorder.

### 5. Office surfaces and pilot boundary

Owner/Admin-only Office Review may provide inline financial-resolution inputs and review actions.
The pre-export Billing Revision summary is readable, copyable, and printable, and includes customer,
service location, request reference, included visits/dates/recorders, resolved line data, totals,
completeness, and review/resolution audit data.

CSV generation, QuickBooks/API integration, invoice creation, payments, tax, inventory, and
reconciliation remain separate work. The initial implementation must first create the durable
financial-resolution and Billing Revision records; it must not ship a “Ready for billing” list that
cannot prevent duplicate manual handoff.

### 6. Required persistence and execution safeguards

The closeout implementation must make the following mechanics explicit before code, rather than
leaving them as UI or parent-status assumptions:

- A zero-line visit needs its own immutable, visit-level Office Financial Disposition (including
  `NoCharge`); a line-level financial resolution cannot represent it.
- Financial resolution corrections are additive. The read model selects one effective resolution per
  missing value component and retains prior resolution evidence; it does not merely reject every
  second resolution as a duplicate.
- Billing Revision membership must support a database-proven reservation lifecycle. A membership
  records release/audit data when its unhanded revision is voided, and a partial uniqueness rule
  prevents a visit from having more than one unreleased membership. A cross-table index based on the
  parent revision's status is not an acceptable substitute.
- The database also permits at most one request-level `Draft` or `ReadyForBilling` revision at a
  time. `HandedOffToBilling` revisions retain their memberships permanently; only an unhanded void
  releases a visit.

Financial-resolution controls belong only to Owner/Admin office-review surfaces, beginning with
`ActualWorkReviewCard`. They must never be added to the price-blind `ActualWorkComposer` field
surface. Queue expansion may reuse the review card only in a separately bounded Office Review UI
slice.

## Consequences

- Field price blindness and submitted factual immutability are preserved.
- Office staff can resolve real-world missing financial facts without a technician bottleneck.
- Future export serializes one stable, auditable revision rather than reconstructing a billing view
  from mutable/live data.
- This is a new closeout-domain implementation slice and is not authority to expand the active
  Slice 3 field-assist or release-validation scope.

## Non-goals

- Editing submitted field lines or captured financial snapshots in place.
- Customer approval, change-order authorization, automatic invoice creation, payment processing,
  QuickBooks synchronization, or true-cost/COGS claims.
