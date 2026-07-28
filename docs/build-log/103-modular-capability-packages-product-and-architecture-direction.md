# Build Log 103 — Modular Capability Packages: Product and Architecture Direction

**Status:** Direction locked — individual capability scopes remain decision-pending
**Date:** 2026-07-28
**Scope:** Product packaging and implementation architecture for customer-requested operational
capabilities
**Related:** Build 101; Build 102; Foundation feature/entitlement model

## Customer signal

Several service-business conversations report the same problem with large incumbent systems: they
pay for and carry a broad field-service platform, but depend on only a small subset of its features.
Their needs are real and specific—equipment history, technician work records, material/price
context, accounting handoff, or property-manager authorization—not a desire to operate a complete
generic enterprise suite.

Keep will respond to those customer pain points with complete, bounded capability packages. It will
not copy another product's feature checklist merely because that product contains adjacent modules.

## Decision

Keep will evolve as a **modular monolith with subscribable capability packages**.

```text
one Keep deployment
  → first-party bounded capability modules
  → server-enforced account entitlements + role permissions
  → customer subscription/enablement by complete workflow outcome
```

This is deliberately not a generic runtime plugin platform. Modules are built, versioned, deployed,
and operated by OpHalo as part of Keep. No third party may load executable code, migrations, or
unreviewed integrations into a customer account.

## Product packaging principle

Sell/enable outcomes, not isolated buttons. A customer should not have to purchase a QR scanner and
then discover that asset history, work records, and material assignment are separate incomplete
pieces.

Illustrative capability packages:

| Package | Customer outcome | Likely contents |
|---|---|---|
| Keep Core | A customer need is visible, owned, followed up, communicated, and closed. | Requests, roles, continuity, customer page, attention/follow-up, history. |
| Asset Operations | A technician identifies the exact equipment and sees its permitted history on site. | Property/unit context, equipment assets, QR-label lifecycle/scanning, service history, warranty context. |
| Price Book & Materials | Staff choose known labor/material/equipment items and retain what was used at the price then applicable. | Controlled import, versioned catalog, material/work lines, immutable price snapshots. |
| Accounting Exchange | Approved work reaches the accounting workflow without avoidable re-entry. | Reviewed export/import, QuickBooks connector where justified, external IDs, sync/reconciliation status. |
| B2B Property Workflow | Contractor and property-manager work is traceable through unit, authorization, completion, and billing handoff. | Property-manager contacts, property/unit identity, PO/not-to-exceed/approval context, B2B handoff. |

Packages may have dependencies. For example, Price Book & Materials is useful only with a work
record, and a QR label without Asset Operations is not a meaningful standalone product.

## Technical architecture rules

Every capability package must have clear ownership of:

- its domain model/tables and migrations;
- its application services/API endpoints/background work;
- its UI/mobile surfaces;
- its permissions and account-level entitlement;
- its audit/history and export rules;
- focused tests, upgrade behavior, and disable/degradation behavior.

The existing Foundation distinction remains mandatory:

```text
entitlement: may this account use this capability?
permission: may this active user perform this action?
policy: may this action occur for this record in its current state?
```

Entitlements are enforced on the server. Hiding UI is never sufficient: a disabled package must not
permit API mutations, exports, synchronization, background processing, or unintended data access.

Modules may share stable Core identities and approved contracts, but must not reach directly into
another module's internals merely because the code is deployed together. Integration is explicit
through contracts/events/services owned at the boundary.

## What this avoids

- A monolithic page/component/domain file that grows without ownership boundaries.
- One-off customer code paths that cannot be safely reused or priced.
- A generic third-party plugin SDK, code-loading system, marketplace, or dynamic customer-supplied
  database migrations.
- Selling individual technical fragments rather than a usable workflow.
- Treating a package entitlement as a replacement for record-level authorization.

## QuickBooks and external integrations

External integrations are modules with higher operational requirements. They need credential
security, external-ID mapping, idempotency, retries, reconciliation, auditability, rate-limit/error
handling, and an explicit source-of-truth policy.

For this reason, a QuickBooks capability begins with a narrow, reviewed exchange contract. It does
not begin as a promise of broad two-way synchronization or a replacement for accounting software.

## Productization rule

Customer conversations are discovery input, not automatic bespoke commitments. A capability becomes
a productized package when it has:

1. a named customer job-to-be-done and a complete smallest workflow;
2. stable data/authorization boundaries;
3. evidence that at least two or three customers share the need, or a strategic pilot justifies it;
4. an explicit support, pricing, rollout, and disable/degradation posture; and
5. a bounded implementation/readiness gate appropriate to the risk.

The first customer may still justify building a capability when it is strategic, but the build must
be designed as a reusable module rather than a customer-specific fork.

## Sequencing

Current production reliability and deployment-evidence gates remain in force. After those gates,
the HVAC/property workflow should proceed through the bounded discovery items in Build 101:

1. asset/work-record and QR identity foundation;
2. controlled price-book import and materials assignment;
3. B2B property/authorization workflow where validated; and
4. narrow accounting exchange after the actual QuickBooks source-of-truth contract is known.

Large-account Request List capacity is a separate platform gate documented in Build 102. A
capability package does not bypass the need to prove list/query performance for the account volumes
it is intended to support.
