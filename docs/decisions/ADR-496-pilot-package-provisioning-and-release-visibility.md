# ADR-496 — Pilot Package Provisioning and Release Visibility

**Date:** 2026-09-04
**Status:** Locked
**Amends:** [ADR-454](decision-index.md) and [ADR-428](ADR-428-day-zero-settings-getting-started-redesign.md)

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

1. A new **Pilot** account receives the server-enforced
   `keep.price_book_quotes_materials` capability-package enrollment as part of the same atomic
   provisioning transaction that creates its account, owner, and base entitlements. Pilot admission
   is the authorized OpHalo action required by ADR-454. The internal enrollment API remains for
   recovery, exceptions, and non-pilot commercial activation.
   Automated enrollment is recorded as system-originated, not as an action falsely attributed to
   the newly created customer owner. The enrollment audit model therefore distinguishes a real
   internal-user change from `SystemProvisioning`; only the former carries a
   `changed_by_account_user_id`.
2. Package enrollment and workflow release visibility are distinct concerns.
   - **Price Book** is live and discoverable for pilot Owner/Admin users.
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
6. Price Book is visible immediately to entitled Pilot Owner/Admin users. It is presented as a
   secondary, guided activation layer after Requests; it is not deferred until a first request.

## Rationale

Pilot admission is already a deliberate commercial/product decision. Making the package enrollment
automatic at that boundary is more reliable than a manual post-provisioning operation, while the
existing internal operator route preserves exceptional control.

Automatic provisioning is a product behavior, not a human internal-operator action. Explicit system
provenance is more truthful and reliable than creating a pseudo-user that every environment must
bootstrap and protect. Human internal enable/disable/re-enable actions remain attributable to their
real AccountUser.

Separating entitlement from release visibility allows the pilot to use live Price Book value without
promising incomplete Proposed Work or Quote workflows. Server gating avoids a hidden UI becoming an
unsupported but reachable product surface.

Request-first onboarding proves the core daily loop with real business data. It avoids both
checklist anxiety and ambiguous sample/demo records. Price Book then introduces financial readiness
only after the business understands where requests enter Keep.

## Consequences

- New pilot provisioning needs a migration that adds enrollment change provenance, a transactional
  capability-enrollment insert, and coverage for its atomicity. Existing pilots need an idempotent
  backfill recorded as `SystemProvisioning`; a deliberately Disabled row is never silently
  re-enabled.
- The PWA needs an empty-workspace first-use state, Settings readiness labels, and removal of the
  duplicate checklist/Get Started navigation treatment.
- A release-gating contract for Proposed Work and Quotes must be verified before their entitlement
  is provisioned automatically for pilots.
- This ADR does not create automatic customer email notifications. Existing customer email actions
  remain `mailto:` handoffs until a separate notification product decision is made.
