# Build Log 129 — Direct Actual Work and Accounting Handoff: Product Preflight

**Status:** Product decisions locked; mechanical implementation preflight required before code  
**Date:** 2026-08-18  
**Related:** Build Logs 104, 126, 127, 128; ADR-487

## Purpose

Keep must support the ordinary field-service outcome: a staff member completes routine work during
a visit and records what actually happened, price-blind, without inventing a Proposed Scope. This
record is factual source data for office closeout, the Day-1 accounting CSV/reconciliation loop,
and future optional equipment history.

Keep is not a task-routing, payment-processing, accounting, or QuickBooks-sync product. This
preflight uses the existing responsibility/ownership language; it introduces no separate routing
state, board, or route model.

## Native field outcomes

```text
Direct repair / service  → Direct Actual Work
Recommendation needed    → Proposed Scope → office review/commercial path → Actual Work later
Diagnostic / advice only → zero-line Actual Work visit with a truthful outcome and completion note
```

No request is required to have a proposal, actual-work lines, a quote, or an accounting export.

## Direct Actual Work record boundary

- `ActualWork` is a distinct request-bound record; it is never a status change on `ProposedScope`
  or a commercial record.
- One finalized Actual Work record represents one field visit/execution event. A complex request may
  retain multiple immutable visit records.
- A staff member starts a Draft record through a **Record completed work** action that reuses the
  price-blind Unified Scope Composer interaction patterns. It does not expose customer price, cost,
  margin, discount, or quote controls.
- Submitted line records retain item/description snapshot, unit, actual quantity, optional field
  note, recorder identity, and recorded time. A line may optionally link to an approved commercial
  baseline source; direct work has no required upstream source.

## Zero-line diagnostic/service visits

An Actual Work visit may be submitted with zero lines only when it includes a required completion
note and one truthful structured outcome:

```text
DiagnosticOnly | NoWorkAuthorized | NoAccess
```

This prevents invented material/labor lines while preserving what occurred. A zero-line visit is
not automatically eligible for accounting export as a $0 job. If a diagnostic/trip charge applies,
it must be represented by a real actual-work line; otherwise Owner/Admin explicitly closes it as
no-charge.

## Office closeout and accounting handoff

- Office closeout selects finalized, not-previously-exported Actual Work visits for a request.
- The resulting accounting handoff is an immutable snapshot. A later visit or correction never
  changes a prior export; it becomes a later reviewed snapshot/handoff.
- The Day-1 handoff is a server-authoritative batch of `jobs.csv` and `work-lines.csv` for manual
  QuickBooks entry. It is not a QuickBooks import, API integration, invoice creator, or ledger.
- Both files include `RequestReferenceCode` (for example `REQ-1042`) prominently for human/Excel
  matching, plus `RequestId` and `AccountingExportId` for technical and batch traceability.
  `work-lines.csv` also carries its source Actual Work visit/line id.
- After export, Owner/Admin records the accounting-system invoice/reference number and reconciles
  the externally confirmed outcome: `PaidInFull`, `VoidedOrNoCharge`, or `Other` (required note).
  Keep does not store payment amount, payment method, balance, partial payment, credit, or
  collection activity.
- Margin is an Owner/Admin closeout view, not an accounting-export column by default.

## Required mechanical-preflight decisions

1. Exact Actual Work aggregate/table names, status transitions, concurrency contract, and whether
   one Draft visit is allowed per request or per responsible staff member.
2. Required submission timestamp and the exact role/visibility rules for field users, Owner/Admin,
   and authorized readers.
3. Exact closeout eligibility and price/cost snapshot rules, including the hard block on direct
   actual lines without valid customer price or Standard/Expected Direct Cost.
4. Exact CSV schemas, export audit event, retry/idempotency behavior, and later-correction rule.
5. `PermissionKeys.Keep.AccountingManage` is the accounting mutation/export seam and maps to
   Owner/Admin for the first pilot. No separate Accountant role or accounting-user UI is in the
   launch scope. A later role may receive this permission without changing accounting APIs, but
   role/membership and UI work remains a deliberate later slice.
6. Exact invoice/reference and `Other`-note validation.

## Non-goals

- Customer quote delivery, acceptance, signatures, invoices, payment collection, QuickBooks sync,
  tax engine, inventory, payroll, routes, or a task-routing board.
- Asset/equipment identity and equipment history UI; a later Asset Operations package may read
  factual Actual Work records only.
- Converting, editing, or deleting submitted Proposed Scope records.
