# Pilot Discussion — Contractor Asset Workflow Discovery

**Date:** 2026-07-26  
**Status:** Product-direction record — implementation remains staged

## Context

An HVAC contractor pilot conversation surfaced a high-value B2B workflow. The contractor works with
property-management organizations, including one managing approximately 30,000 homes across seven
states. The contractor is not seeking to replace the property manager's existing Accela workflow.

The opportunity for Keep is the contractor's internal operating and B2B communication layer around
the physical equipment and service work: a faster, asset-aware record that can coexist with the
property manager's established system.

## What Was Learned

### Asset-specific service history is required

For the contractor, a useful record must hold more than a customer conversation. It needs to retain
the context of the equipment and work, including (at minimum):

- property and unit/location;
- equipment type, manufacturer, model number, serial number, and description;
- diagnosis, work performed, parts/materials, notes, and before/after photos;
- prior service, quotes, approvals, and completed-work history.

### Equipment QR tags are the near-term differentiator

Keep already uses QR codes for desktop-to-call/email handoff. The proposed equipment-tag workflow
is separate and must not overload that existing QR purpose.

A technician would carry preprinted, unique, durable equipment labels. At the property, the
technician scans an unused label and assigns it to a property, unit, and equipment asset while
recording the model, serial number, description, and other initial data. Subsequent scans identify
that exact asset.

This supports both technician and occupant/property-manager use:

```text
asset tag -> known equipment record -> service need -> contractor review -> retained history
```

The public scan experience can be limited to reporting an issue; editing asset data must require
an authorized technician or internal user. QR payloads must be opaque identifiers, never embedded
addresses, tenant data, or serial numbers. Asset tags will need at least `unassigned`, `active`, and
`retired/lost` lifecycle states; equipment replacement must preserve the old asset's history and use
a newly assigned tag.

### Quote preparation and approval are a tangible workflow gap

Today, the technician identifies needed work at the site, texts the office, and an administrator
manually selects items and calculates totals before returning the proposed quote for technician
approval and eventual customer/property-manager submission.

The desired future path is:

```text
price-sheet upload -> selectable quote line items -> office/technician review
-> secure quote link -> accept or decline/comment -> linked approved work record
```

An accepted quote should advance or create a work record linked to the originating service need and
asset; it must not create an unrelated duplicate request.

### Accounting and fleet needs are signals, not commitments

The contractor also named invoicing/QuickBooks data exchange and a forthcoming Fleetmatics
retirement as needs. Keep is not committed to replacing either system. Initial discovery must
identify the exact Fleetmatics workflows in use and the contractor's actual source of truth for
customers, properties, invoices, and service history.

A manual import/export path may be a sensible precursor to any QuickBooks integration, but neither
an accounting integration nor fleet-management replacement is a prerequisite for validating the
asset-to-service workflow.

### Remote equipment reporting is an R&D candidate

The contractor will explore whether a few homes can be used for Arduino/sensor prototyping and
testing. This is promising only if it demonstrates that specific readings improve diagnosis, reduce
truck rolls, or allow technicians to arrive prepared.

It is not a V1 dependency. A browser/tenant-phone Bluetooth relay is not a dependable production
assumption, particularly on iPhone. Any later hardware design requires deliberate choices on power,
durability, connectivity, installation liability, and device management. Sensor data, if validated,
belongs on the known asset record established by the equipment-tag workflow.

## Product Direction

### Decision: asset-aware continuity for service contractors

Keep is evolving from a continuity utility into an **asset-aware continuity product for service
contractors**. The core promise remains that a customer need is visible, owned, followed up, and
closed with confidence. The product will increasingly connect that promise to the actual equipment
and property involved.

```text
known equipment asset -> service need -> ownership/follow-up -> quote/approval
-> retained work history -> future service need
```

Keep will be **HVAC/service-contractor-first** in product discovery and pilot language, while
retaining reusable foundations for other property-service trades. This is not a decision to build a
full field-service-management suite, property-management platform, or replacement for Accela.

Keep's intended boundary is the contractor's internal operational record and lightweight B2B
communication layer. The property manager's existing application may remain the formal system for
its own workflow; whether it is primarily messaging, a system of record, or both still requires
direct discovery.

### Decisions deferred pending validation

The following are not current product commitments:

- a separate paid subscription for property managers;
- a cross-vendor, portfolio-wide asset system of record;
- replacing Accela, QuickBooks, Fleetmatics, dispatch, or fleet management;
- Arduino/sensor telemetry as an initial product capability.

The immediate validation question is whether an asset-linked contractor record makes technicians
and office staff materially faster, more accurate, and more responsive than their current workflow.

The emerging product sequence is:

1. Keep's existing request/continuity workflow remains the active product and current remediation
   work remains priority.
2. Validate an asset model and technician equipment-tagging flow.
3. Validate price-sheet-driven quotes and secure approval links attached to the same service record.
4. Research accounting exchange and the specific Fleetmatics gap without promising replacement.
5. Run a narrowly scoped remote-sensor experiment only if test homes and a diagnostic hypothesis are
   available.

The strongest prospective message is not generic CRM or generic small-business software:

```text
Scan the unit, identify the equipment, report the problem, retain the service history,
and keep the contractor, property manager, and customer aligned.
```

## Open Discovery Questions

- How do the contractor and property manager exchange requests, approvals, and status today, and
  which parts must remain in Accela?
- Which property, authorization, and equipment fields are required on every service record?
- Which three to five sensor readings would actually alter a technician's diagnosis or dispatch
  decision?
- What exact Fleetmatics functions are retiring from the contractor's workflow?
- What data should be imported/exported first for QuickBooks and invoicing?
- What is the smallest portfolio, asset count, and success metric for an equipment-tag pilot?

## Scope Boundary

This discovery does not change the approved request-list work or its known gaps. Those issues remain
separate, active work and must be resolved before the new workflow is treated as an implementation
commitment.
