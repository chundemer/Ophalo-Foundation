# Build Log 104 — First Contractor Launch Plan and Production Gates

**Status:** Decision and delivery-control record — implementation requires a separately approved,
bounded preflight per slice
**Date:** 2026-07-29
**Scope:** First HVAC contractor launch: mixed public-customer and B2B work, Fleetmatics retirement,
four-week target with one-week contingency
**Related:** Build 101 (workflow), Build 102 (scale), Build 103 (capabilities), Build 105 (photos),
Build 106 (reliability)

## Outcome and boundary

The contractor needs a dependable replacement for the *specific* Fleetmatics workflows it relies on
before Fleetmatics retires. Keep is not being positioned as a replacement for fleet GPS, routing,
dispatch optimization, payroll, invoicing, or every Fleetmatics feature.

The production outcome is a reusable Keep workflow for both public-customer and B2B work:

```text
capture -> identify customer or B2B context -> own work -> record updates/evidence
-> close with retained history
```

Every launch change must be production code: account-scoped, role/policy enforced server-side,
observable, recoverable, tested against its failure paths, and usable after the pilot without a
customer-specific fork. A test passing, a demo-only branch, or a free-text workaround is not an
acceptable completion condition.

**Locked 2026-07-29:** This is a phased rollout of Keep's narrow, high-quality operational wedge,
not a comprehensive Fleetmatics replacement. Price-book import/material assignment is launch
minimum; equipment QR/asset history is deferred until customer testing. A narrow QuickBooks
payment-status reconciliation aid may be considered only after its workflow is defined, and is not
a commitment to a general accounting integration.

**Locked 2026-07-29 — accounting slice:** Work completed enters an internal **Needs Invoicing**
queue. Authorized office staff record an accounting-system invoice/reference number with **Mark
Invoiced**, then later use **Mark Reconciled** after confirming the accounting outcome in that
system. Keep records action/state/time/actor and presents a dedicated Reconciliation queue; it
does not calculate balances or process financial data. CSV/API import, automatic matching, and
two-way QuickBooks synchronization are excluded from this rollout.

**Locked 2026-07-29 — work context:** Each request is B2C, B2B, or temporarily Unclassified when
public intake cannot establish the type. Staff capture selects B2C/B2B; Unclassified work must be
qualified before dispatch or accounting reconciliation. B2C and B2B share the accountable work
record, but their required facts and signals differ: B2C centers the customer relationship, while
B2B can require distinct requester, service-site/unit, site-contact, authorization, billing, and
reconciliation context. This supports commercial work without creating a property-management
platform.

## Four-week target and one-week contingency

The target is four calendar weeks from the locked customer-workflow decision. Week five is a
contingency for a discovered production defect, migration/release rehearsal correction, or a
single genuinely launch-blocking workflow gap. It is not capacity for new features.

| Window | Lane deliverable | Gate |
|---|---|---|
| Decision days (maximum 2 business days) | Fleetmatics capability inventory; one end-to-end public and B2B walkthrough; role, volume, property/contact, PO/WO, authorization, evidence, and fallback answers | Christian locks launch-minimum workflow and exclusions |
| Week 1 | Production architecture/preflights for the selected B2B and photo slices; Sentry external prerequisite provisioned; recovery/release evidence audit started | No implementation begins from assumed customer behavior |
| Week 2 | First bounded B2B/work-record and/or photo vertical slice committed after review; Sentry slice ready for deployment verification | Each slice stays within the repository file/family gate and has authorization/failure tests |
| Week 3 | Remaining launch-minimum workflow slices; representative-data and team workflow rehearsal; support/cutover runbook complete | Public and B2B paths complete without Fleetmatics-only hidden dependencies |
| Week 4 | Controlled production-candidate release, migration rehearsal, smoke tests, staff training, cutover decision | All launch gates below pass |
| Week 5 (only if needed) | Correct verified launch blocker; repeat affected release evidence | No scope expansion; Christian explicitly approves use of contingency |

## Launch lanes

### 1. Customer workflow and Fleetmatics transition — Build 101

Deliver a signed-off capability inventory and a minimum workflow for both work types. The inventory
must say who performs each action today, what information is mandatory before dispatch, where it
currently lives, what Keep replaces on day one, and what remains in another tool or manual fallback.

Candidate B2B fields such as property, unit/location, site contact, requester, PO/work-order
number, and authorization context are not automatically approved. They become production fields
only when the customer confirms their operational necessity and their search/audit/visibility rules.

### 2. Capability architecture — Build 103

Every new capability is first-party and reusable. It has an account entitlement, user permission,
record/state policy, owned data/migration boundary, audit rule, and disable/degradation posture.
No customer-specific feature flag, route, schema branch, or special-case role is permitted.

### 3. Field evidence/photos — Build 105

Technician photo upload is launch-critical discovery. The first slice is authenticated staff photo
evidence only unless the contract expressly expands it. Customer upload, public photo viewing,
documents/video, and offline queued sync are separate decisions.

### 4. Reliability and release safety — Build 106

Sentry errors-only capture, alert ownership, provider backup/PITR facts, restore proof or explicit
provider limitation, migration sequencing, rollback/degradation behavior, and production smoke
evidence are launch gates. The OPS-008 migration outage makes this non-optional.

### 5. Scale and later property-manager expansion — Build 102

The first contractor pilot must establish its real workload envelope and collect baseline metrics.
It does not certify a 30,000-property onboarding. That later commitment requires the Build 102
representative data, query-plan, load, and bounded-database-query evidence.

## Hard launch gates

Do not cut over real work until all are true:

1. The contractor has approved the Day-1 workflow and explicit exclusions/fallbacks.
2. Every required B2B/public path is server-authorized, tenant-scoped, tested, and rehearsed.
3. Photos, if included, meet Build 105's security/storage/failure contract.
4. Sentry DSNs and founder alerting are live with the existing redaction policy.
5. Database backup/PITR posture and restore procedure are evidenced; migration ordering and rollback
   are rehearsed for the release.
6. Production health/readiness and controlled smoke checks pass after deployment.
7. A named support owner, incident contact path, release owner, and customer fallback procedure are
   written and understood.
8. The pilot workload is measured, but no large-property-manager capacity claim is made without
   Build 102 evidence.

## Immediate decisions for the customer session

1. Which three Fleetmatics actions are used daily by office staff and by technicians?
2. Which actions are hard Day-1 blockers, and what is the fallback for the rest?
3. What B2B identity and authorization facts are mandatory before a technician is dispatched?
4. Are PO/work-order number and not-to-exceed approval required for every B2B job, some jobs, or
   only retained as reference text?
5. How are photos used: before/after, equipment plate, diagnosis, completion evidence, or billing?
6. What active-work volume, history volume, users, and connectivity conditions will the pilot have?
7. Who receives a production incident alert and who communicates a service interruption to the
   customer?

## Explicit exclusions unless a customer answer makes one a hard blocker

- Fleet GPS, route optimization, fleet diagnostics, timekeeping/payroll, and full dispatch.
- Broad QuickBooks synchronization, invoicing, payments, inventory, procurement, or tax logic.
- Public/customer quote approval, NTE automation, and public evidence upload.
- Equipment QR labels and asset history; deferred until customer testing, then reconsider as a
  separate Asset Operations package.
- Offline mutation queues and automatic replay.
- A property-manager or 30,000-property commitment.
