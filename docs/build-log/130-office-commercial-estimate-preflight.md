# Build Log 130 — Office Commercial Estimate: Product Preflight

**Status:** Product direction locked; mechanical implementation preflight required before code  
**Date:** 2026-08-18  
**Related:** Build Logs 104, 126–129; ADR-487, ADR-488

## Purpose

The proposed-work path must not end at office acknowledgement. When a field recommendation needs
upfront pricing or customer authorization, Owner/Admin needs an office-owned Commercial Document
that turns a reviewed Proposed Scope into a priced, retained baseline for later Actual Work.

## Locked boundary

- A Commercial Document is distinct from Proposed Scope and Actual Work. It may be created from a
  reviewed scope without retyping its visible lines, or originate in the office when no field scope
  exists.
- Owner/Admin alone controls customer price, expected direct cost, margin, discount, commercial
  posture, and approval. Field staff remain price-blind.
- Every editable draft becomes an immutable revision at approval; later editing creates a new
  revision rather than altering prior commercial truth.
- The estimate provides the optional source baseline for Actual Work. Actual Work remains factual
  and may still begin directly for routine repair.
- Initial customer communication uses the contractor's existing process. This slice does not add
  a customer quote portal, electronic acceptance/signature, payment collection, invoice creation,
  or QuickBooks synchronization.

## Required office outcome

Owner/Admin can review a submitted scope, prepare a commercial estimate, inspect revenue, expected
direct cost, gross-profit dollars/percentage, and incomplete-cost state, apply a reasoned discount,
and approve an immutable revision for use as an Actual Work baseline. Cost/margin are internal and
never exposed to field users or accounting CSV recipients.

## Required mechanical-preflight decisions

1. The initial postures required by the pilot under ADR-487: Estimate, Fixed-Price Quote, and/or
   Time-and-Materials Authorization. Do not add a posture the pilot cannot explain operationally.
2. Revision, approval, discount, and price/cost snapshot data model and concurrency contract.
3. Customer-safe existing-process share/export boundary and its truthful audit semantics; no
   unproven delivery or customer-acceptance claim.
4. Exact clone contract from reviewed Proposed Scope and source-link contract from approved
   Commercial Revision to Actual Work lines.
5. Owner/Admin authorization, Price Book entitlement composition, and required margin/incomplete
   cost/discount validation tests.

## Non-goals

- Field-visible price/cost/margin, customer self-service estimate view, signatures, payment,
  invoice, accounting sync, inventory, or customer relationship management.
