# Build Log 104 — Mixed Contractor Pilot Go-Live Roadmap

**Status:** Decision and delivery-control record — implementation requires a separately approved,
bounded preflight per slice
**Date:** 2026-07-29
**Amended:** 2026-08-18 — authoritative mixed-contractor pilot roadmap and delivery order
**Scope:** First HVAC contractor launch: mixed public-customer and B2B work, Fleetmatics retirement,
four-week target with one-week contingency
**Related:** Build 101 (workflow), Build 102 (scale), Build 103 (capabilities), Build 105 (photos),
Build 106 (reliability)

## Outcome and boundary

The contractor needs a dependable replacement for the *specific* Fleetmatics workflows it relies on
before Fleetmatics retires. Keep is not being positioned as a replacement for fleet GPS, routing,
routing optimization, payroll, invoicing, or every Fleetmatics feature.

The production outcome is a reusable Keep workflow for both public-customer and B2B work:

```text
capture -> identify customer or B2B context -> own work -> record updates/evidence
-> close with retained history
```

Every launch change must be production code: account-scoped, role/policy enforced server-side,
observable, recoverable, tested against its failure paths, and usable after the pilot without a
customer-specific fork. A test passing, a demo-only branch, or a free-text workaround is not an
acceptable completion condition.

**Amended 2026-08-18:** This is a phased rollout of Keep's narrow, high-quality operational wedge,
not a comprehensive Fleetmatics replacement. Generic Price Book CSV import is deferred under
DEF-087; pilot accounts use curated manual catalog entry and real business data. Equipment
QR/asset history is deferred until customer testing. QuickBooks synchronization, invoice creation,
payments, and financial-ledger behavior remain excluded.

**Accounting handoff boundary:** Owner/Admin closeout of factual Actual Work is launch work. The
handoff to the accounting system must be explicit and auditable. The Day-1 mechanism is a narrow,
server-authoritative CSV export for manual QuickBooks entry; exact columns are locked from the
contractor's accounting workflow during its preflight. Owner/Admin then records the external
invoice/reference number and reconciles the accounting outcome back into Keep. `Paid
in full` may be a reconciliation outcome asserted from QuickBooks; Keep does not collect payment,
store payment amounts or methods, calculate balances, support partial payments, create invoices,
or synchronize QuickBooks.

**Locked 2026-07-29 — work context:** Each request is B2C, B2B, or temporarily Unclassified when
public intake cannot establish the type. Staff capture selects B2C/B2B; Unclassified work must be
qualified before an Operator is assigned as Responsible or before accounting reconciliation. B2C and B2B share the accountable work
record, but their required facts and signals differ: B2C centers the customer relationship, while
B2B can require distinct requester, service-site/unit, site-contact, authorization, billing, and
reconciliation context. This supports commercial work without creating a property-management
platform.

## Authoritative mixed-contractor pilot roadmap — 2026-08-18

**Controlled-pilot amendment (2026-08-19):** The must-ship sequence below remains the target for
the later full mixed-contractor pilot. [Build Log 131](131-next-week-parallel-field-pilot-plan.md)
supersedes it only for next week's limited parallel field pilot: Work Context has a storage
foundation but its unapproved user-facing workflow is deferred; Direct Actual Work and production
operational insight are the immediate priorities.

This section supersedes earlier implied delivery ordering in this document. It is the single
working path to pilot go-live; each numbered item still requires its own bounded mechanical
preflight, implementation review, and acceptance evidence.

| Priority | Deliverable | Operational outcome |
|---|---|---|
| Must ship | Request Work Context — Residential, Commercial, Unclassified, on-site contact, and PO/work-order reference (Build 128) | Staff can qualify and assign responsibility for mixed work without inferring business workflow from notes. |
| Must ship | Direct Actual Work capture ([Build 129](129-direct-actual-work-and-accounting-handoff-preflight.md)) | A staff member can record routine completed work price-blind without creating a fictitious proposal. |
| Must ship | Proposed Work Review queue/history (Build 127) | The office can acknowledge a recommendation that genuinely needs a decision; it is not the required path for direct repairs. |
| Must ship | Office Commercial Estimate ([Build 130](130-office-commercial-estimate-preflight.md)) | Owner/Admin turns a reviewed recommendation into a priced, approved baseline without exposing commercial data to field staff. |
| Must ship | Owner/Admin Actual Work closeout, accounting CSV, and reconciliation | Factual work reaches accountable office review; Owner/Admin exports the Day-1 QuickBooks CSV, records the external invoice/reference, and reconciles the later outcome back into Keep. |
| Must ship | Pilot Updates and Report Friction | The business receives truthful role-specific release communication and can report confusion, bugs, and missing needs in the app. |
| Must ship | Production observability and release evidence | Sentry errors-only capture, health/smoke evidence, deployment/configuration checks, and named incident ownership are live. |
| Fast follow | Authenticated field photo evidence | Technicians can attach bounded site/work images through the already-delivered R2 storage seam. |
| Fast follow | Additional commercial work context | Add unit, billing entity, or authorization facts only where the pilot proves each is needed. |
| Post-launch | Equipment/asset identity and service history | QR labels, warranties, and asset views consume factual Actual Work only. |
| Post-launch | Customer quote delivery/acceptance, signatures, integrations, payments | These remain separate commercial, legal, and integration decisions. |

### Required delivery order

```text
0. Confirm Day-1 commercial-estimate postures, accounting CSV columns, and fallbacks
1. Request Work Context
2. Direct Actual Work preflight and capture slice
3. Proposed Work Review — Mark reviewed / queue / history
4. Office Commercial Estimate
5. Owner/Admin closeout, accounting CSV export, and accounting reconciliation slice
6. Pilot Updates, Report Friction, production observability, rehearsal, support, and controlled pilot release
```

Direct Actual Work and Proposed Work Review are twin launch priorities, but they are separate
reviewable implementation slices. Direct Actual Work comes first because it serves routine repair;
Proposed Work Review immediately follows because it serves recommendation/estimate-required work.

### Required operational safeguards

- Work Context gates Responsible assignment through a one-click inline choice—not a navigation
  dead end. Commercial requests retain optional on-site contact and PO/work-order reference; they
  do not introduce a property hierarchy or authorization engine.
- Complex requests retain multiple immutable Actual Work visit records. Office closeout/export
  snapshots selected finalized visits; it never rewrites a prior accounting handoff.
- Diagnostic-only/no-work visits may submit zero lines only with a required completion note and
  structured truthful outcome. They cannot silently export as a $0 job.
- Accounting CSV files include a human-readable `RequestReferenceCode` in addition to UUID/batch
  keys, so bookkeepers can safely match rows in Excel.

### Pilot communications and feedback release requirement

Keep provides authenticated Pilot Updates and Report Friction before pilot activation. Pilot Updates
is the in-app source of truth for release communication, with audience targeting limited to
`Everyone`, `OwnerAdmin`, and `FieldStaff`, and message types limited to `New`, `Fixed`, `KnownIssue`,
and `ActionNeeded`. It is not a social feed, roadmap portal, feature-voting system, or general
notification platform.

```text
Urgent  → immediate Owner/Admin escalation plus an in-app known-issue/workaround message
Daily   → one verified post-release update when user-visible behavior changed
Weekly  → Owner/Admin summary: shipped, known issues, pilot focus, and requested feedback
```

Each visible message states what changed, who it affects, any required action, and any known
workaround. Field-staff messages use practical workflow language; Owner/Admin messages include
configuration, operational effect, and feedback requested. Internal refactors and invisible fixes
do not generate user messages. Report Friction accepts a short authenticated report with bounded
app/account/screen context through the private founder route. No push delivery, email campaign,
notification preference matrix, delivery/open tracking, public status page, or generic CMS is
required for the pilot.

### V1 notification posture

The mixed-contractor pilot launches **queue-driven with no push requirement**. The authenticated
PWA/native surfaces use server-derived queues/counts, refetch-after-write, focus/resume refresh,
pull-to-refresh, and restrained active-list polling. Keep makes no claim that APNs/FCM alerts are
live. Push remains a later explicit release decision; it is not a go-live blocker under this
posture.

## Historical four-week target and one-week contingency

The original target was four calendar weeks from the locked customer-workflow decision. Week five is a
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
must say who performs each action today, what information is mandatory before responsibility is assigned, where it
currently lives, what Keep replaces on day one, and what remains in another tool or manual fallback.

Candidate B2B fields such as property, unit/location, site contact, requester, PO/work-order
number, and authorization context are not automatically approved. They become production fields
only when the customer confirms their operational necessity and their search/audit/visibility rules.

### 2. Capability architecture — Build 103

Every new capability is first-party and reusable. It has an account entitlement, user permission,
record/state policy, owned data/migration boundary, audit rule, and disable/degradation posture.
No customer-specific feature flag, route, schema branch, or special-case role is permitted.

### 3. Field evidence/photos — Build 105

Technician photo upload is a fast-follow capability, not a blocker for the queue-driven pilot
posture. The first slice is authenticated staff photo evidence only unless the contract expressly
expands it. Customer upload, public photo viewing, documents/video, and offline queued sync are
separate decisions.

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
8. Pilot Updates and Report Friction are usable by their intended roles, and Owner/Admin has the
   daily/weekly pilot communication cadence and escalation contact.
9. The pilot workload is measured, but no large-property-manager capacity claim is made without
   Build 102 evidence.

## Immediate decisions for the customer session

1. Which three Fleetmatics actions are used daily by office staff and by technicians?
2. Which actions are hard Day-1 blockers, and what is the fallback for the rest?
3. What B2B identity and authorization facts are mandatory before a technician is assigned responsibility?
4. Are PO/work-order number and not-to-exceed approval required for every B2B job, some jobs, or
   only retained as reference text?
5. How are photos used: before/after, equipment plate, diagnosis, completion evidence, or billing?
6. What active-work volume, history volume, users, and connectivity conditions will the pilot have?
7. Who receives a production incident alert and who communicates a service interruption to the
   customer?

## Explicit exclusions unless a customer answer makes one a hard blocker

- Fleet GPS, route optimization, fleet diagnostics, timekeeping/payroll, and a task-routing board.
- Broad QuickBooks synchronization, invoicing, payments, inventory, procurement, or tax logic.
- Public/customer quote approval, NTE automation, and public evidence upload.
- Equipment QR labels and asset history; deferred until customer testing, then reconsider as a
  separate Asset Operations package.
- Offline mutation queues and automatic replay.
- A property-manager or 30,000-property commitment.
