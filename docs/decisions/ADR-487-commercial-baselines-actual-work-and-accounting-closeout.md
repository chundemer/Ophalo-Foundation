# ADR-487 — Commercial Baselines, Actual Work, and Accounting Closeout

**Status:** Locked  
**Date:** 2026-08-17  
**Related:** ADR-453, ADR-456, ADR-463, ADR-465, ADR-473, ADR-475, ADR-478; Build Logs 116, 117, 126

## Decision

Keep closes the contractor operating loop with separate, request-bound records for field
recommendation, office commercial commitment, field execution, and accounting handoff. The Unified
Scope Composer is a reusable, price-blind field interaction surface; it is not a single mutable
business record.

```text
ProposedScope -> CommercialDocument / CommercialRevision (optional) -> ActualWork
                                                            -> Closeout / accounting export snapshot
```

### Record boundaries

- **`ProposedScope`** is the technician's price-blind recommendation. It remains immutable after
  office review and is never overwritten by a commercial edit or actual work.
- **`CommercialDocument` / `CommercialRevision`** is office-owned and request-bound. Its
  `CommercialPosture` is `Estimate`, `FixedPriceQuote`, or `TimeAndMaterialsAuthorization`.
  Owner/Admin alone owns customer-facing descriptions, price, discount, direct-cost snapshots,
  and internal approval. A commercial document may be created from reviewed proposed work or
  directly from an office-originated request.
- **`ActualWork` / `ActualWorkLine`** records work and materials actually performed or used. It is
  separate from both prior records and remains price-blind in the field. It may begin empty for a
  reactive job or clone a selected approved commercial revision as an editable baseline.
- A cloned actual line carries an optional source reference to the exact
  `CommercialRevisionLine` plus immutable source snapshots. A new field-added line has no such
  reference. This permits history and variance even if catalog data or later commercial revisions
  change.

Internal approval is not customer acceptance. Customer delivery, customer decision, signature,
and change-order authorization remain separately designed capabilities; no lifecycle field may
claim them before that work exists.

### Field fact, office commercial decision

When execution differs from the baseline, field staff record the factual change and a reason.
They do not decide whether it is billable and do not see price, cost, margin, discount, or customer
totals. At review, Owner/Admin classifies the difference as an internal variance, a billable
addition requiring appropriate revised/customer authorization, or work included under an existing
time-and-materials authorization. Actual work must never silently revise a customer-facing
commercial commitment.

### Cost and closeout truthfulness

Until Keep has receipts, purchasing, or inventory evidence, execution-cost reporting is labelled
**Standard/Expected Direct Cost**: actual recorded quantity multiplied by the applicable immutable
catalog or commercial cost snapshot. It is not represented as true COGS. Missing cost snapshots
produce an incomplete-cost state rather than a false margin figure.

Direct-to-actual work has no field price authority. Before its accounting handoff, Owner/Admin must
perform office pricing and closeout review.

An accounting export is an immutable, Owner/Admin-created snapshot of a reviewed closeout revision.
If work changes after export, an explicit Owner/Admin reopening reason creates a later adjustment /
closeout revision and a distinct later export; it never mutates the record already delivered to
accounting. The initial handoff is a bounded CSV for manual reconciliation, not a QuickBooks sync,
invoice, payment system, or ledger.

Actual work is retained against its request now. Property/equipment service history may link it to a
durable asset only after the separate property/asset identity model is intentionally established.

### Required later domain-preflight constraints

The later Actual Work and closeout preflights must lock the following before code:

- **Commercial clone survives catalog lifecycle.** Cloning an approved commercial baseline reads
  its immutable `CommercialRevisionLine` snapshots, not the live catalog. A later deactivation or
  discontinuation cannot prevent a technician from recording work against an already-approved
  baseline; any newly added line still follows the live-catalog eligibility rules.
- **Visit and closeout lifecycle.** The preflight must choose and prove either multiple immutable
  visit submissions beneath one request-level Actual Work aggregate, or one mutable aggregate that
  remains Draft until closeout. It must define partial-work visibility, reopening, concurrency, and
  the exact finalization boundary; these semantics are not inferred from ProposedScope.
- **No incomplete direct-actual accounting handoff.** Owner/Admin closeout hard-blocks accounting
  export when a direct-actual line lacks a valid customer price or Standard/Expected Direct Cost.
  The UI identifies each blocking line; it never exports a silent $0 financial value. The later
  contract must separately decide whether a commercial-baseline job with missing cost blocks export
  or permits a clearly marked incomplete-cost operational export.
- **Per-line field attribution.** Every `ActualWorkLine` records `RecordedByAccountUserId` and its
  record time independently of optional `DerivedFromCommercialRevisionLineId`, so work performed by
  a different technician remains attributable line by line.

## Consequences

- The next Price Book sequence must first provide Owner/Admin proposed-work review, queue context,
  and retained scope history; a submitted scope cannot be a dead-end signal.
- Quote-domain planning is generalized to the commercial-document/revision model above. This
  amends ADR-453 and ADR-473's fixed-price-only wording and permits the bounded T&M authorization
  posture, without adding dynamic pricing, tax engines, invoicing, or customer acceptance.
- Actual-work implementation reuses the Unified Scope Composer interaction contract but has its own
  lifecycle, concurrency, snapshots, read model, and audit history. It does not reopen or mutate a
  proposed scope or commercial revision.
- Owner/Admin variance and closeout views compare proposed, commercial, and actual records where
  source links exist. Accounting export is gated on the applicable closeout review.
- The first implementation preflight remains Owner/Admin proposed-work review queue/workbench and
  retained scope history. It must not silently implement Actual Work or closeout while defining the
  review contract.

## Non-goals

- Technician pricing authority or technician commercial-delta classification.
- A claim of true purchased cost, COGS, net profit, or accounting reconciliation until the needed
  evidence and workflow exist.
- QuickBooks/API sync, invoice creation, payments, inventory, purchasing, or vendor receipt entry.
- Customer acceptance, e-signature, delivery/open tracking, or a change-order customer workflow.
