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

## Controlled parallel-pilot implementation addendum — 2026-08-19

The next-week release implements the complete technician-to-office loop: price-blind factual field
capture, an Owner/Admin Actual Work Review queue, and Owner/Admin financial review sourced from
the Price Book. It must be an end-to-end vertical batch, not an isolated schema foundation.

### Locked pilot guardrails

- The request's active **Responsible** user is the sole field recorder for the pilot. That user
  may be an Owner, Admin, or Operator; an Owner/Admin assigned as Responsible uses the same
  price-blind capture surface as an Operator, and role does not expose financial fields there.
- There is one open Draft visit per request, owned by its Responsible recorder. The recorder may
  create, edit, or discard it. Submitted visits and their factual lines are immutable.
- Multi-technician work is supported without parallel drafts: the active Responsible user records
  the single visit and remains accountable for its job details; other technicians do not create
  their own Actual Work record for that request in this pilot.
- No cross-user Draft **Take over** action, cross-user Draft edit, linked **Correct prior visit**
  workflow, silent submitted-record edit, closeout, or export is in this pilot. These are
  explicitly deferred product options, not missing implementation.
- Submitting a visit raises an additive Actual Work needs office review signal. Owner/Admin sees
  it in an Actual Work Review queue and can mark the submitted visit reviewed, recording reviewer,
  time, and an optional internal note. The signal resolves only when no submitted visit on the
  request remains unreviewed. Review is not an invoice, customer approval, export, or payment fact.
- The field capture surface is Request Detail's **Record completed work** action. The Owner/Admin
  review surface shows immutable visit history plus Price Book-backed sales price,
  Standard/Expected Direct Cost, margin, totals, and clear incomplete-financial-data cues. No new
  top-level navigation is added; the queue is an Owner/Admin-only **Actual Work Review** tab in the
  existing Requests workspace and is the office's actionable entry point.
- A catalog-backed Actual Work line snapshots its selected Price Book version-line identity,
  sell price, and Standard/Expected Direct Cost when the field fact is recorded. Owner/Admin review
  calculates from those immutable snapshots, never from the catalog's then-current price. A custom
  or otherwise unsnapshotted line renders an explicit incomplete-financial-data cue; it never
  produces invented totals or margin.
- Actual Work mutations require the Price Book account entitlement, `RequestsOperate`, the distinct
  `ActualWorkCapture` permission (`keep.pricebook.actualwork.capture`), and an active-Responsible
  row-authorization check. The new permission is granted through `OperatorBase`; Owner/Admin
  inherit it through role composition. The Responsible check is exposed as one reusable
  participation read primitive, not duplicated by callers.
- The domain and API boundary both reject a zero-line submit unless its completion note is
  non-whitespace and its outcome is exactly `DiagnosticOnly`, `NoWorkAuthorized`, or `NoAccess`.
- The database enforces one active Draft per request with a partial unique index whose predicate
  matches the persisted lifecycle exactly. It must not invent a redundant `IsDiscarded` state.
- Marking a visit reviewed and resolving the aggregate Actual Work review signal run in one
  database transaction; a request remains queued while any submitted visit is unreviewed.

### Pilot draft-concurrency decision

The pilot locks **one open Draft visit per request**. If multiple technicians are present, the
assigned Responsible user remains accountable for the job details and records the one field visit.
The pilot does not introduce independent second-technician drafts, a shared draft, or takeover.

All remaining items are mechanical-preflight choices constrained by this document and ADR-487:
aggregate/table names, API/DTO shape, version header, persistence transaction, exact read
visibility query, and focused authorization/concurrency/failure tests.

### Approved implementation sequence

Each session must publish its exact file/test count for the hard batch gate before edits. A
foundation session is not feature completion; the next named session follows immediately.

1. **Actual Work domain.** Visit/line aggregate, immutable financial snapshots, draft lifecycle,
   zero-line outcome invariant, and domain tests.
2. **Persistence and migration.** EF mappings, exact active-Draft index, persistence contract,
   migration/designer/model-snapshot files counted individually, and persistence tests.
3. **Draft API and authorization.** Create/edit/discard service and endpoints, `ActualWorkCapture`,
   reusable active-Responsible check, and authorization/concurrency contracts.
4. **Submission and review signal.** Atomic submit, zero-line boundary validation, and additive
   Actual Work review-signal raise/reopen behavior.
5. **Field capture UI.** Request Detail composer, client API/types, retry/error behavior, and
   read-only submitted visit history.
6. **Owner/Admin review mutation.** Mark reviewed, reviewer/time/note, and atomic aggregate-signal
   resolution.
7. **Owner/Admin financial read.** Immutable-snapshot totals, Standard/Expected Direct Cost,
   margin, and incomplete-data projection/API.
8. **Owner/Admin review UI.** Existing Requests-workspace Actual Work Review tab plus request-detail
   review card; review action updates the queue and history.

## Required later closeout/handoff decisions

The pilot implementation sequence above resolves Actual Work aggregate, Draft, submission,
review, snapshot, and visibility behavior. The later closeout/export preflight must still lock:

1. Exact closeout eligibility over reviewed visits, including the hard block on a line lacking a
   valid sales-price or Standard/Expected Direct Cost snapshot.
2. Exact CSV schemas, export audit event, retry/idempotency behavior, and later-correction rule.
3. `PermissionKeys.Keep.AccountingManage` is the accounting mutation/export seam and maps to
   Owner/Admin for the first pilot. No separate Accountant role or accounting-user UI is in the
   launch scope. A later role may receive this permission without changing accounting APIs, but
   role/membership and UI work remains a deliberate later slice.
4. Exact invoice/reference and `Other`-note validation.

## Non-goals

- Customer quote delivery, acceptance, signatures, invoices, payment collection, QuickBooks sync,
  tax engine, inventory, payroll, routes, or a task-routing board.
- Asset/equipment identity and equipment history UI; a later Asset Operations package may read
  factual Actual Work records only.
- Converting, editing, or deleting submitted Proposed Scope records.
