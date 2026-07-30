# Build Log 101 — HVAC Contractor Workflow: Price Book, QuickBooks, and Equipment QR Discovery

**Status:** Discovery and decision record — implementation not yet authorized
**Date:** 2026-07-28
**Scope:** HVAC contractor B2C/B2B work-record direction; price-book import; QuickBooks exchange;
equipment QR identity and history
**Related:** Build 091; Build 102; Build 103; Build 104; Build 105; Fleetmatics retirement discovery

## Why this record exists

The HVAC contractor is evaluating Keep as a replacement for the limited parts of Fleetmatics that
matter to its operation before Fleetmatics retires in December. The contractor does **not** need a
generic replacement for every field-service, property-management, accounting, or fleet function.

The intended product value is a durable, asset-aware work record: staff can identify the property
and equipment, capture the service need and work, assign the parts/materials used, retain history,
coordinate with the customer or property manager, and exchange the approved financial outcome with
the accounting system.

This expands the direction recorded in Build 091. It does not supersede current production
reliability gates, nor authorize an unbounded field-service-management rebuild. Build 104 controls
the four-week contractor-launch target: only customer-confirmed Day-1 B2B facts may be promoted
into bounded production slices; price-book, accounting, QR, and asset work remain separate unless
the customer establishes a hard launch dependency.

## Decisions locked 2026-07-29

The first contractor rollout is a deliberately narrow Keep wedge, not a comprehensive Fleetmatics
or field-service-management replacement. Keep should be exceptionally reliable at the workflows it
does own; it should not accumulate unrelated platform responsibilities merely to claim feature
parity.

- **Price-book import and material assignment are Day-1 requirements.** The implementation must
  retain the safe staging, mapping, validation, explicit-publish, version-audit, and historical-line
  snapshot rules below. The current price sheet cannot be published until the contractor supplies
  its column meanings and confirms the import mapping.
- **Equipment QR identity and retained asset history are deferred until customer testing begins.**
  Do not make QR labels, scanning, asset records, warranty history, or replacement/lost-label flows
  a launch gate. Capture real customer feedback first, then decide the bounded Asset Operations
  package.
- **QuickBooks is not a Day-1 full integration.** The observed problem is that payment received in
  QuickBooks is not reliably reflected in the operating software. Explore a narrow, explicit
  payment-status reconciliation aid after the actual handoff and source-of-truth rules are known;
  do not commit to two-way sync, payment processing, invoice creation, or a general accounting
  interface.

### Day-1 accounting reconciliation queue

The initial solution is a manual, internal reconciliation workflow in Keep. It works independently
of whether the contractor uses QuickBooks Desktop, QuickBooks Online, another accounting product,
or a manual ledger; it is not an import, API integration, or financial ledger.

When authorized office staff mark work completed, it enters the **Needs Invoicing** queue. The
office workflow is deliberately two-step:

```text
Work completed
  -> Needs Invoicing
  -> Mark Invoiced (required accounting-system invoice/reference number)
  -> Invoiced — Pending Payment
  -> Mark Reconciled (payment/outcome confirmed in the accounting system)
  -> Reconciled
```

`Reconciled` means staff confirmed the accounting outcome in the accounting system; Keep makes no
claim about a customer balance, collection, payment amount, partial payment, credit, write-off, or
financial finality. The queue and its state are internal-only and must not alter customer-visible
work status or the customer page.

For each transition Keep records the internal state, action time, and acting staff user. `Mark
Invoiced` records the contractor-provided accounting-system invoice/reference number. The first
slice must provide a dedicated Reconciliation queue/filter so office staff can clear Needs
Invoicing and Invoiced — Pending Payment work without mixing it into technician dispatch views.

This deliberately avoids Day-1 CSV matching/import and API synchronization. Those approaches need
stable cross-system identifiers, idempotency, duplicate handling, partial-payment/credit semantics,
unmatched-record resolution, reconciliation audit, and retry/support operations. They remain later
work only if the manual queue proves useful and the customer establishes a repeatable, reviewable
handoff.

## The two workflows must remain distinct

### Locked 2026-07-29 — request work context and workflow signals

Every Keep request must carry a first-class **work context**. It is operational workflow state, not
a loose tag, and controls the required qualification facts, available internal prompts, and
attention/reconciliation signals:

- **B2C** — an individual customer is the requester and ordinarily the service recipient/payer.
- **B2B** — any commercial or business customer, including property-management work. The requester,
  service location, site contact, authorization holder, and billing entity may be different.
- **Unclassified** — permitted only when new public intake cannot reliably establish the context.
  Authorized staff must classify it before dispatch or entry into the accounting reconciliation
  workflow. Authenticated staff capture should select B2C or B2B rather than create unclassified
  work.

Both contexts use the core Keep accountability loop: capture, ownership, customer-safe/internal
updates, evidence, completion, and retained history. Their operational signals remain distinct:

| B2C | B2B |
|---|---|
| customer contact, reply/update/promise, and closeout signals | requester/business, property/site and unit/location, site-contact, authorization (PO/work order/not-to-exceed when applicable), invoicing, and reconciliation signals |
| customer-safe communication and feedback posture | separate requester, service-site, and billing-party context where the workflow requires it |

This does **not** authorize a property-management platform. B2B fields become production fields
only when the contractor confirms that they are needed to receive, dispatch, complete, invoice, or
reconcile work, along with their visibility, search, and audit rules.

### B2C — residential/customer work

```text
customer need → diagnosis → materials/work performed → customer communication or approval
→ completed work → accounting export/invoice handling
```

The existing Keep customer-continuity surface remains useful here: request intake, customer-safe
updates, internal notes, ownership, follow-up, and closeout. Materials and equipment context are
internal operational data unless a separately approved quote/customer view exposes selected facts.

### B2B — property-management work

```text
property-manager request → property + unit/location + equipment asset → authorization
(PO / not-to-exceed / approval rule) → technician work + materials → completion evidence
→ accounting export/invoice status → retained asset history
```

B2B is not B2C with more rows. Before implementation, discovery must establish the authoritative
property, unit, requester/contact, authorization, invoice recipient, and status-exchange rules.
The property manager's existing system (for example Accela) may remain its formal system of record.
Keep must not assume it becomes a portfolio-wide property-management platform.

## Price-book upload and materials assignment

### Sample sheet finding

The supplied `2025 Price Sheet MEM - System Prices.csv` is a 23-row, headerless, semi-structured
worksheet. It contains capacity headings (for example `2.0 ton` through `5.0 ton`), grouped system
descriptions, and multiple unlabeled numeric columns. The current data does not safely identify
which values mean material cost, labor hours, markup, wholesale price, sell price, or another
business value.

Keep must therefore **not** offer an unrestricted "upload CSV and publish" action. The contractor
must define a mapping/template and explicitly confirm its meaning before any data is published.

### Minimum safe price-book model

A published catalog item needs at least:

- stable item/SKU or contractor-supplied external key;
- display name and category (`equipment`, `material`, `labor`, or another approved type);
- unit of measure (for example `each`, `lb`, `hour`);
- current cost and/or sell price, with currency;
- active/inactive state; and
- source import/version and effective time.

The import flow must be account-scoped and permissioned:

```text
upload → parse into staging → map/validate required fields → show row-level errors and preview
→ explicit publish → immutable import/version audit
```

An import must be all-or-nothing at publish time. It must not partially alter the live catalog.
Raw uploaded files require retention, access-control, malware/size/content validation, and a
defined deletion/audit policy before they become a production feature.

### Materials on work

A technician or authorized office user must be able to add a line to a work record, for example:

```text
3-ton heat-pump system × 1
Refrigerant × 2 lb
Labor × 3.5 hr
```

Each assigned line must snapshot its description, quantity, unit, cost/sell price as applicable,
and source price-book version at the time it is added. Subsequent price-book changes must never
rewrite historical work or quoted/invoiced values.

Initial scope excludes inventory depletion, purchase orders, vendor stock, tax calculation,
payments, and autonomous pricing. Those are separate accounting/inventory commitments.

## QuickBooks exchange — define before building

"Update QuickBooks data" is not a sufficient contract. Discovery must first answer:

1. Is the accounting product QuickBooks Online or QuickBooks Desktop?
2. Which system is authoritative for customers, items, prices, invoices, payments, and credits?
3. Is the first direction import to Keep, export from Keep, or both?
4. Does Keep create an estimate, invoice draft, final invoice, journal entry, or a reviewed CSV/IIF
   export?
5. What fields identify a record across systems, and who resolves duplicate/conflicting changes?

The safe first integration is one-direction and explicit: export a reviewed, immutable work/line-item
payload to create or draft the approved accounting transaction, store the external ID and export
status, and never represent a successful export as payment or collection.

Do not start with broad two-way synchronization. It requires OAuth/credential handling, external-ID
mapping, idempotency, retries, reconciliation, conflict resolution, audit history, rate-limit/error
handling, and support operations. A manual reviewed CSV/IIF export may be the appropriate precursor
if it matches the contractor's actual accounting workflow.

## Equipment QR workflow — required capability

Equipment identity and retained history are a required workflow, not a decorative QR feature.

### Technician flow

```text
technician arrives → selects/scans an unused physical QR label → assigns it to the correct
property + unit/location + equipment asset → records equipment identity → saves

later visit → scans the active QR label → opens the exact asset → sees permitted work/service
history and warranty work → creates or continues the linked work record
```

At initial assignment, the authorized technician records the required asset fields to be finalized
in discovery, at minimum equipment type, manufacturer, model number, serial number, description,
and property/unit/location. Equipment replacement must retire the old asset while preserving its
history; the replacement receives a distinct active QR assignment.

### QR security and lifecycle

- QR content is an opaque random identifier only—never an address, tenant/customer data, serial
  number, internal database ID, or capability URL.
- Labels have explicit `unassigned`, `active`, and `retired/lost` states. An active label maps to
  one asset only; reassignment is an audited, authorized exception.
- Scanning a QR is not authorization. Anonymous scanning may at most start a restricted issue-report
  flow; asset editing, full history, warranty information, and material/work data require an
  authorized technician or internal user.
- The scanned-asset view must enforce the same account/property/role boundaries as every other
  Keep read. It must never expose cross-account history or tenant data.
- Warranty information needs a defined source, fields, visibility rules, and confidence language;
  Keep must not infer warranty eligibility merely from an old work record.

## Fleetmatics boundary

Fleetmatics retirement is the commercial trigger, not a blank authorization to replace fleet
management. The contractor must name the exact retiring workflows: for example work orders,
technician mobile access, asset/service history, dispatch, GPS/route tracking, timekeeping,
inspection forms, or reporting. Keep may validate a narrow replacement for the workflows above;
dispatch, route optimization, vehicle GPS, payroll/time tracking, and fleet administration remain
separate decisions.

## Scale and rollout gate

The prospective property-management portfolio changes the capacity bar. A 30,000-property
portfolio does not necessarily mean 30,000 active work records, but the current active-request list
path loads broad candidate sets before ranking/slicing and is not yet a safe assumption for that
volume or multi-dispatcher concurrency.

Before any portfolio-wide B2B commitment:

1. Run a bounded pilot for one region, team, or portfolio segment.
2. Establish representative active/history request volume and concurrent-user targets.
3. Measure p95 list latency, database/query load, memory, create-to-list visibility, and failure
   recovery against seeded or safely sanitized representative data.
4. Move active-list filtering, deterministic ranking, and keyset pagination into the database while
   preserving authorization, queue/count reconciliation, and cursor behavior.
5. Use immediate targeted refresh after a user's own write; reassess broader poll/revision behavior
   only after the underlying list query is bounded.

## Required discovery decisions before implementation

- Annotated price-sheet columns, canonical item keys, units, price/cost semantics, ownership, and
  update cadence.
- One complete B2C and one complete B2B workflow walkthrough, including authorization and billing
  handoff.
- QuickBooks product/version, source-of-truth matrix, initial exchange direction, transaction type,
  and error/reconciliation owner.
- Property/unit/asset identity model, required technician fields, QR-label procurement/format, and
  lost/replacement process.
- Warranty data source, exact fields, and which roles can view/change it.
- Specific Fleetmatics workflows required before December, ranked by operational consequence.
- Pilot size, success metrics, and an explicit no-go threshold for portfolio rollout.

## Explicit non-goals until separately approved

- Full property-management or tenant platform; cross-vendor portfolio system of record.
- Replacement of Accela, all Fleetmatics features, QuickBooks, dispatch, routing/GPS, payroll,
  inventory, procurement, payments, or broad CRM.
- Broad two-way accounting synchronization, automatic invoice/payment claims, or autonomous tax/
  pricing decisions.
- Public QR access to private asset history, tenant information, warranty details, or internal work
  records.

## Sequencing

This is a product-discovery track. It must not silently displace current production reliability and
deployment-evidence work. After those gates, the first implementation decision should be a bounded
asset/work-record foundation and price-book-import preflight—not a QuickBooks or Fleetmatics
replacement commitment.
