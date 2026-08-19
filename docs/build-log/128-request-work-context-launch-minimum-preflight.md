# Build Log 128 — Residential / Commercial Request Work Context: Launch-Minimum Preflight

**Status:** Required before mixed residential/commercial pilot go-live — implementation not yet authorized  
**Date:** 2026-08-18  
**Related:** Build Logs 101, 104, 126; ADR-487

## Purpose

Keep's target contractor serves both residential and commercial work. A request needs a durable,
first-class work context so staff can qualify, route, and later reconcile the work without inferring
business workflow from free text. This is a request fact, not an account commercial-state field, a
permission, or a property-management product.

## Launch-minimum decision

Each request carries one of these values:

```text
Residential (B2C)   — individual customer ordinarily requests, receives, and pays for service.
Commercial (B2B)    — a business/property-management workflow may involve distinct requester,
                      site contact, authorization holder, and billing party.
Unclassified        — public intake has not established the context; staff must qualify it.
```

The context describes the workflow around this request, not the physical building. For example, a
property-manager request for an apartment unit is Commercial; a business owner's own home repair is
Residential.

## What this slice must do

- Persist `WorkContext` on `KeepRequest`; migrate existing requests to `Unclassified` because it
  cannot safely be inferred from historical text.
- Staff Quick Capture must require Residential or Commercial. Public intake creates Unclassified;
  it must not ask customers to understand the contractor's internal classification.
- Provide an authorized staff classification/correction action with actor/time audit history.
- Return and display context on request detail and list rows, and support an explicit list filter.
- Permit acknowledgement, customer communication, and ordinary triage while Unclassified. Require
  classification before assigning an Operator as Responsible and before a later accounting-
  reconciliation action; it must not prevent the business from promptly responding to a public
  request. The assignment UI presents this as a one-click inline context choice, then completes
  the originally requested responsibility assignment without a navigation detour.
- Commercial requests add three optional, durable facts: `OnSiteContactName`,
  `OnSiteContactPhone`, and `PurchaseOrderNumber` (usable for either a PO or work-order reference).
  Existing request-level service location remains the starting point. Unit hierarchy, billing entity,
  not-to-exceed rules, and authorization workflow are not added in this slice.

## What must not use it

- It does not alter role permissions, tenant visibility, or account commercial standing.
- It does not choose Direct Actual Work versus Proposed Scope, require a quote, or change request
  lifecycle status.
- It does not add a Commercial queue, property-manager portal, invoice system, accounting sync, or
  separate B2B application.
- It does not link an asset/equipment record; that remains post-go-live work.

## Required implementation preflight answers

Before code, the mechanical preflight must identify the existing capture, update, list, assignment,
and event/audit seams and lock:

1. Enum/API names and whether UI wording is Residential/Commercial while code uses B2C/B2B.
2. The exact authorized roles for classification/correction and whether a correction is versioned
   against the request's existing concurrency token.
3. The existing Responsible-assignment seam and the resulting inline prompt/error contract for an
   Unclassified request. No separate task-routing state, board, or route model is introduced.
4. Validation, visibility, list/detail projection, and audit treatment for the three selected
   Commercial facts. Do not use notes as a substitute for a later-confirmed durable required fact.
5. Migration/backfill behavior, list cursor/filter semantics, DTO compatibility, and focused
   authorization, public-intake, assignment-gate, audit, and filter regression tests.

## Relationship to Price Book and future history

Work context is independent of field work type. Both Residential and Commercial requests may be
diagnostic-only, Direct Actual Work, or Proposed Scope/estimate work. Future equipment history must
read factual Actual Work only; WorkContext is useful context but never evidence that work happened.

## Delivery priority

For a mixed residential/commercial pilot, complete this launch-minimum qualification path before
additional Price Book workbench features. It protects every request at intake and responsibility
assignment; it does not authorize a broader B2B/property-management scope.
