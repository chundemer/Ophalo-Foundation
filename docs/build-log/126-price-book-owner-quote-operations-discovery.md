# Build Log 126 — Price Book Owner, Commercial, Actual-Work, and Operations Workflow Discovery

**Status:** Product-direction and delivery-plan record — no implementation authorized
**Date:** 2026-08-17
**Scope:** Complete the operational path after field proposed-scope capture for a small business:
office review, commercial estimates, actual-work capture, owner margin and discount decisions,
customer presentation, accounting handoff, and business-owned Price Book exports.
**Related:** Build Logs 107, 122–125; ADR-463–468, ADR-473, ADR-475, ADR-477–478; DEF-087–093

## Why this record exists

The currently delivered Price Book surface is a strong foundation for maintaining a catalog,
assembling common offerings, and letting field staff capture price-blind proposed work. Paired
Nudges completes the technician capture flow.

But a submitted proposed scope is only the beginning of the business workflow. A small-business
owner needs to review what was recommended, decide what to charge, understand the job's expected
margin, communicate a customer-safe estimate where needed, record what was actually used, and hand
reviewed closeout data to accounting. The business also needs portable exports of its Price Book
configuration.

This document turns those observed gaps into a coherent delivery plan. It does not authorize any
schema, endpoint, UI, export, accounting integration, or customer-facing implementation.

## Product outcome

```text
Field staff capture proposed work, price-blind
  -> office sees it in an actionable review queue
  -> Owner/Admin reviews and commercializes it when an estimate is needed
  -> field records actual work from a cloned commercial baseline or a direct blank draft
  -> Owner/Admin reviews variance and commercial implications of factual field deltas
  -> office closes out and exports an immutable accounting snapshot
```

This is not an attempt to turn Keep into a full field-service-management suite, accounting ledger,
inventory system, or generic spreadsheet-import product. ADR-487 locks the record boundaries and
truthfulness rules for this path.

## Current state and concrete gaps

### Delivered foundation

- Owner/Admin catalog, cost, sell-price, alias, category, and lifecycle management.
- Owner/Admin offering/assembly management with calculated price and margin-repair cues.
- Price-blind technician proposed-scope capture, Draft editing, assembly expansion, quick actions,
  and Paired Nudges.
- Submitted scope creates the additive `ProposedScopeNeedsOfficeReview` request work signal.
- Per-item immutable price-publication snapshots and request-bound, immutable proposed-scope line
  snapshots.

### Missing operational path

1. No office queue or request-list indicator tells an Owner/Admin that proposed work awaits review.
2. No Owner/Admin screen/action exists to review, edit, approve, reject, or mark a submitted scope
   `OfficeReviewed`.
3. No scope-history view shows all field visits/submitted scopes on the request.
4. No formal request-bound commercial document/revision, estimate/quote/T&M posture, approval
   lifecycle, or conversion from proposed scope exists.
5. No job-level margin view exists. Current catalog/assembly margin cues are configuration support,
   not historical expected job profitability.
6. No governed discount capability exists.
7. No customer-safe quote presentation, delivery/share, decision, or signature workflow exists.
8. No export exists for approved quote/work data for manual accounting entry or reconciliation.
9. No owner export exists for catalog items, assemblies, or Paired Nudge configuration.
10. No actual-work/material-history workflow exists; proposed, commercial, and actual work must
    not be conflated.

## Operating principles

- **Technicians stay price-blind.** They capture recommended work only. They do not see cost,
  margin, discount authority, or customer totals.
- **Office owns price and commitment.** Owner/Admin is the initial authority for commercial
  document creation, discounts, approval, customer presentation, and accounting export.
- **Catalog pricing is never overwritten for one job.** A discount or negotiated adjustment belongs
  to an immutable quote revision/line, with actor, time, reason, and before/after values retained.
- **Margins are historical commercial facts.** Commercial revenue and nullable direct-cost snapshots
  calculate expected gross-profit dollars and percentage. Actual-work cost is initially labelled
  Standard/Expected Direct Cost, never true COGS, and is incomplete when source costs are absent.
- **Customer surfaces are deliberately separate.** A proposed scope and internal notes are never
  exposed as a customer quote. Customer presentation omits cost, margin, internal notes, and
  operational-only context.
- **Exports are bounded and business-owned.** CSV is a download of the account's full authorized
  dataset, not only the page currently visible in a cursor-paginated UI. Export is not an import
  commitment, QuickBooks sync, inventory, or a general reporting engine.
- **Every lifecycle transition is auditable and versioned.** Review, commercial revision, discount,
  approval, actual-work confirmation, closeout, and accounting-handoff actions require clear
  actor/time provenance and concurrency treatment.

## Required capability areas and deliverables

### A. Office scope-review workbench

**Owner problem:** "What did the technician find, and what needs my attention now?"

Deliverables:

- A request-list indicator and dedicated Owner/Admin queue for submitted scopes awaiting office
  review, with truthful counts and row-level context.
- An office scope-review screen that shows the submitted scope and its immutable line snapshots,
  including field notes and the relevant request context.
- An explicit review decision: edit/create a reviewed outcome, approve for quote preparation, or
  return/reject with a recorded internal reason. Exact terminology and technician notification
  posture require a dedicated contract.
- A versioned `SubmittedToOffice -> OfficeReviewed` transition that records reviewer and time and
  resolves the request work signal only when no qualifying submitted scope remains.
- Request-bound scope history: all submitted/reviewed scope visits, ordered and read-only, rather
  than only the latest record exposed to the field composer.

**Locked Step 1 boundary (ADR-488):** the sole review decision is **Mark reviewed**, an auditable
office acknowledgement. Submitted field scope remains immutable; Owner/Admin may add a bounded
internal review note but does not edit field lines here. Return/reject and technician notification
are deferred. The dedicated Owner/Admin queue is separate from customer attention, and review is
blocked on terminal requests while retained history remains readable.

### B. Commercial estimate / quote / T&M workflow

**Owner problem:** "Turn the recommendation into the work and price I am actually willing to offer."

Deliverables:

- Convert a reviewed scope into an office-owned, request-bound commercial draft without retyping
  its visible work lines; also permit an office-originated commercial draft when no scope exists.
- Support an explicit `Estimate`, `FixedPriceQuote`, or `TimeAndMaterialsAuthorization` posture.
- Commercial draft editor for catalog-backed and permitted ad-hoc lines, quantity, description,
  grouping/customer-facing summary, and price treatment.
- Immutable commercial revisions and line snapshots. Any edit after approval creates a new revision
  and returns the document to the approval path (ADR-465's revision rule).
- Owner/Admin approval and change-request actions, with status, actor, timestamp, and review note
  where appropriate.
- Tax-included totals and deterministic rounding per the existing decisions; no customer balance,
  payment, or tax-engine claim.

### C. Owner job-margin and discount controls

**Owner problem:** "Can I make this deal, and what does the concession cost me?"

Deliverables:

- Owner/Admin-only expected job profitability panel on quote draft/revision: revenue, known direct
  cost, gross-profit dollars, gross-margin percentage, and a truthful incomplete-cost state when
  one or more lines lack a cost snapshot.
- Server-authoritative cost and sell-price snapshotting at quote revision creation; never derive a
  historical margin from current catalog data.
- Controlled line-level and/or quote-level discount action. The eventual contract must decide
  whether both forms are needed in V1, but every discount must carry type (amount/percent), value,
  reason, actor, time, and pre/post total/margin effect.
- Discount authority limited to Owner/Admin initially. Discounts are quote adjustments, never a
  mutation of standard catalog sell price and never a technician-facing affordance.
- Clear, server-side handling for invalid discounts, excessive discounts, stale quote versions, and
  missing/incomplete cost data. Automatic approval thresholds or margin gates remain out of scope
  unless separately approved.

### D. Customer-safe commercial presentation and follow-through

**Owner problem:** "How do I get a clear estimate, quote, or authorization in front of the customer without leaking
internal information?"

Deliverables:

- A customer-safe commercial projection containing only approved customer-facing content: business
  identity, scope/sections, quantities/descriptions, tax-included totals, allowed discount
  presentation, and approved revision identity.
- A deliberate internal send/share flow (customer page link, SMS/email handoff, or other selected
  channel) with truthful audit semantics. Opening a draft or copying a link must not falsely claim
  delivery.
- An explicit customer response model (at minimum pending/accepted/declined) only after a separate
  privacy/authentication, revision, legal, and signature decision. Do not expose a raw proposed
  scope or add signature collection by implication.

### E. Accounting handoff and export

**Owner problem:** "I need the approved job data in my accounting process without re-keying every
field or adopting an unsafe sync."

Deliverables:

- A narrow Owner/Admin CSV export of approved/review-ready quote/work data suitable for manual
  QuickBooks or ledger entry. Its exact columns must be designed with a pilot business; likely
  candidates include request/reference, customer/business context permitted for the export,
  approved revision, line descriptions/quantities/totals, discount, tax posture, total, and
  accounting reference fields.
- A durable, internal accounting-handoff/reconciliation state only if the business confirms it is
  needed for its daily workflow. It must remain distinct from customer-visible request status.
- No QuickBooks API integration, invoice creation, payment processing, balance tracking, partial
  payment, or two-way synchronization in the first slice.

### F. Price Book configuration portability exports

**Owner problem:** "This is my business data; I need a usable backup and a way to work with it."

Deliverables:

- Owner/Admin, Price Book-entitlement-gated CSV export for the full catalog—not just the current
  paged list—including display name, external key/SKU, type, category, UOM, active state,
  Common-item designation, active aliases, current cost, sell price, currency, and applicable
  publication metadata.
- Assembly export that preserves both assembly-level metadata (primary item, name, active state,
  price treatment) and each component row (catalog item, quantity, required/optional state,
  display ordering). A separate component CSV or a documented row-per-component schema is
  acceptable; it must not flatten away the relationship.
- Paired Nudge export preserving trigger target/type, ordered suggestion targets/types, and the
  currently returned eligibility/repair state.
- Generation from server-authoritative complete account data with stable column names, UTF-8 CSV,
  spreadsheet-formula-injection-safe cell handling, audit logging appropriate to an internal data
  export, and no exposure to field roles.
- No import/parser/staging workflow implied by these exports. CSV import remains DEF-087.

### G. Actual work, variance, and materials

**Owner problem:** "What did we actually use and perform, separate from what we quoted?"

Deliverables:

- A distinct internal `ActualWork`/line record, not mutation of proposed scope or a commercial
  revision. It reuses the Unified Scope Composer interaction surface while retaining its own
  lifecycle, snapshots, concurrency, and history.
- A technician may start a blank direct-actual draft or clone a selected approved commercial
  revision, then confirm a matching baseline quickly or record factual quantity/item deltas and
  reasons. Field users remain price-blind and do not decide whether a delta is billable.
- Cloning reads immutable commercial-revision-line snapshots, not live catalog rows, so a later
  deactivated/discontinued catalog item cannot prevent execution of an already-approved baseline.
  New additions still use the normal live-catalog eligibility rules.
- Actual lines cloned from a commercial baseline retain an optional exact
  `CommercialRevisionLine` source link plus immutable source snapshots; new actual lines have no
  source link. Every actual line independently records its field recorder and recorded time.
- The Actual Work preflight must decide and prove whether multiple immutable visit submissions sit
  beneath a request-level aggregate or a single aggregate stays Draft until closeout, including
  partial-work visibility, reopening, concurrency, and finalization semantics.
- Owner/Admin reviews and classifies each relevant delta as internal variance, a billable addition
  requiring suitable revised/customer authorization, or included T&M work. Actuals must not
  silently alter the commercial commitment.
- Direct-to-actual work requires Owner/Admin office pricing and closeout review before accounting
  export. Accounting export hard-blocks when any direct-actual line lacks a valid customer price or
  Standard/Expected Direct Cost, with per-line blocking explanation rather than a silent $0 export.
  The later preflight decides the separate posture for a commercial-baseline job with incomplete
  cost. Actual quantities do not claim true COGS.
- Retain actual work against the request now; property/equipment history linkage follows only after
  a deliberate durable asset/property identity model exists.

## Proposed delivery sequence

Each numbered item is a separate preflight and file-gated implementation session or bounded series;
do not merge them simply because they share Price Book files.

1. **Office scope review and request-list signals.** Lock roles, review actions, request queue/list
   representation, work-signal resolution, history read shape, and audit/concurrency behavior.
2. **Commercial-document domain, snapshots, and revision lifecycle.** Introduce only the
   foundation needed for request-bound Estimates, Fixed-Price Quotes, and T&M Authorizations.
3. **Office commercial review UI and owner margin view.** Add draft editing, server-derived margin
   visibility, and approval/change-request workflow.
4. **Discount contract and implementation.** Decide line vs quote discount scope, validation,
   required reason, audit display, and revision interaction before adding a mutation.
5. **Actual-work foundation and field composer reuse.** Add blank and clone-from-commercial-baseline
   paths, factual-delta recording, history, and Owner/Admin variance review.
6. **Closeout and accounting CSV export revisions.** Design pilot-backed columns and immutable
   handoff/correction semantics before implementation.
7. **Customer commercial presentation and deliberate delivery.** Requires its own
   privacy/authentication and customer-decision preflight; do not expose internal scope data as a
   shortcut.
8. **Price Book configuration exports.** Catalog, assembly/component, and nudge exports as a
   bounded Owner/Admin portability feature.

The exact order of steps 6–8 may change with pilot evidence, but step 1 is the immediate blocking
workflow gap: a submitted scope must become actionable for the office before customer delivery,
commercial approval, actual-work comparison, or accounting handoff can be meaningful.

## Explicit non-goals for this plan

- No technician-visible prices, costs, margins, discounts, or customer totals.
- No generic spreadsheet import, arbitrary workbook parsing, or CSV round-trip promise.
- No inventory, purchase orders, vendor ordering, tax engine, or multi-currency system.
- No automatic quote approval, margin thresholds, or policy engine without a later decision.
- No QuickBooks/API accounting sync, payments, invoice generation, or ledger behavior.
- No customer e-signature, option packages, or customer decision model without a dedicated security,
  legal, and revision contract.
- No attempt to build all areas in one implementation batch.
