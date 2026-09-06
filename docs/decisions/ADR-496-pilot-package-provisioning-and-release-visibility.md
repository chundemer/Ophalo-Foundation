# ADR-496 — Pilot Package Provisioning and Release Visibility

**Date:** 2026-09-04
**Status:** Locked
**Amends:** [ADR-454](decision-index.md) and [ADR-428](ADR-428-day-zero-settings-and-getting-started-redesign.md)

> **Amendment — 2026-09-06:** The automatic-enrollment portions of this ADR are superseded. The
> request-first, day-zero onboarding and server-owned workflow-release decisions remain locked.

## Context

The controlled pilot needs a consistent, useful first-run experience. Manually granting the Price
Book, Quotes & Materials package after an account is created makes pilot access inconsistent and
turns an internal commercial decision into support work. At the same time, Proposed Work and Quotes
are still being tested and finalized; their package entitlement must not accidentally publish those
unfinished workflows.

The current PWA also has contradictory first-run signals: Getting Started says the business is ready,
while the Requests workspace presents a checklist that says to set up an already auto-provisioned
public request page.

## Decision

1. `AccountClassification.Pilot` is an operational launch-cohort marker; it is not a commercial
   entitlement rule. A Pilot account does **not** receive
   `keep.price_book_quotes_materials` automatically merely because it is in that cohort. During
   the controlled pilot, an authenticated internal OpHalo operator enables the package for an
   individual business after an explicit fit/agreement decision. That human change is recorded as
   `InternalUser` with the real internal operator identity; it is never attributed to the customer
   Owner. `SystemProvisioning` remains reserved for a future authorized automated commercial grant,
   not for a blanket Pilot backfill.
2. Package enrollment and workflow release visibility are distinct concerns.
   - **Price Book** is live and discoverable for entitled Owner/Admin users.
   - **Proposed Work** and **Quotes** remain unreleased until the onboarding upgrade is completed
     and explicitly signed off. They must be absent from normal navigation and blocked by a
     server-owned release gate; hiding a button or route alone is not an authorization control.
3. Universal pilot onboarding is request-first. Keep's public link, Requests, and default response
   policy are ready on day zero. The empty Requests workspace is the first-use surface; it states
   that the public link is live and offers only `Open customer view` and `Add your first request`.
4. The permanent Getting Started navigation destination and the Requests checklist that asks the
   user to set up their request page are removed. Settings remains the three-section ADR-428 model,
   with passive readiness states rather than completion scoring or required chores.
5. Price Book is the pilot's next activation layer, not a prerequisite for receiving or manually
   entering the first request. Its initial guidance is to add the services, materials, equipment,
   and fees the business actually uses; a complete catalog, assemblies, Proposed Work, and quotes
   are not day-zero requirements.
6. Price Book is visible immediately to entitled Owner/Admin users. It remains secondary to
   Requests and is never a prerequisite for the request loop. The package is discovered and
   selected commercially through the account/subscription area, not through Business Settings;
   Business Settings configures an already-entitled package. Self-service Trial evaluation,
   billing, plan inclusion, downgrade, and expiry behavior are deferred to a separate commercial
   workflow decision.

7. Capability state is server-authoritative. Every package action must enforce both account
   enrollment and the applicable user permission; hidden navigation is not authorization. The PWA
   uses the authenticated account's capability-status read model for discovery and must refresh
   that state after a later entitlement change. No package flags are added to the auth-exchange or
   session/JWT contract in this decision.

8. Removing an enrollment is non-destructive: new package work stops, the module's normal
   navigation/configuration surfaces are omitted, and retained Price Book and historical work data
   stays available only where its established read-only/audit policy permits. Re-enrollment restores
   the account's retained package data; it does not fabricate or erase history.

## Rationale

Businesses differ materially in whether itemized pricing, materials, and catalog workflows fit
their operation. Binding a package to the Pilot delivery cohort would pollute pilot feedback and
would stop serving the intended model when ordinary post-launch accounts begin as Production
accounts in Trial. Explicit operator-assisted pilot selection keeps the cohort, commercial choice,
and audit record distinct.

Separating entitlement from release visibility allows the pilot to use live Price Book value without
promising incomplete Proposed Work or Quote workflows. Server gating avoids a hidden UI becoming an
unsupported but reachable product surface.

Request-first onboarding proves the core daily loop with real business data. It avoids both
checklist anxiety and ambiguous sample/demo records. Price Book then introduces financial readiness
only after the business understands where requests enter Keep.

## Consequences

- The provenance schema is retained: `InternalUser` requires the authenticated internal operator
  actor; `SystemProvisioning` has no actor. The previously proposed automatic new-Pilot insert and
  blanket Pilot backfill do not proceed.
- The PWA needs an empty-workspace first-use state, Settings readiness labels, and removal of the
  duplicate checklist/Get Started navigation treatment.
- A release-gating contract for Proposed Work and Quotes remains required before those workflows
  are released to any entitled account.
- This ADR does not create automatic customer email notifications. Existing customer email actions
  remain `mailto:` handoffs until a separate notification product decision is made.
- It does not create self-service checkout, a customer entitlement-write endpoint, package flags in
  auth exchange/JWT claims, or automatic expiry/downgrade revocation. Those require a separate
  commercial workflow decision.
