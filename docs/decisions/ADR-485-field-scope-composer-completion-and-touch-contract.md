# ADR-485 — Field Scope Composer Completion and Touch Contract

**Status:** Locked  
**Date:** 2026-08-15  
**Extends:** ADR-482, ADR-483, ADR-484  
**Related:** ADR-456, ADR-463, ADR-473, ADR-480, ADR-481

## Decision

The following final V1 composer rules are locked before redesign implementation.

### Quick scope action curation

An Owner/Admin explicitly configures and orders the small field-facing Quick scope action set. It
contains no more than six eligible assemblies and/or Common Items. It is never inferred from all
active catalog data, trade terminology, request type, or a business-wide automatic baseline.

The configuration uses one account-owned polymorphic ordered-slot record: exactly one of
`CatalogItemId` and `OfferingAssemblyId` is set. At write time, the target must be either an Active
Common Item or an operationally eligible assembly. If a later catalog/assembly lifecycle change
makes a configured target ineligible, Keep retains the configuration and shows it as ineligible to
Owner/Admin users for explicit correction; the price-free field read omits it. Keep does not silently
auto-drop the slot.

### Assembly and line behavior

Selecting an assembly adds all of its configured default items in one action and expands them into
separate, ordinary Draft lines. There is no pre-add optional-item chooser. A technician may remove
or edit any resulting Draft line under the normal line-type rules.

Adding the same catalog item or the same assembly more than once creates distinct Draft lines; Keep
does not silently merge quantities. Separate lines preserve their source, note, and physical-work
context. Any later grouping is an office presentation concern, not a field-capture mutation.

### Submit and completion semantics

An empty scope is permitted only while Draft. The server must reject submission of a Draft with zero
lines; the client disables or explains the unavailable submit action before that request is made.

`Submit scope to office` is the technician's explicit declaration that the proposed
work/material scope is complete for office review. It transitions the scope to `SubmittedToOffice`,
raises the existing office-review work signal (ADR-463), and presents this field-facing outcome:

> **Submitted to office — awaiting review**  
> Office will review the work and prepare customer-facing next steps if needed.

It is not a declaration that physical work is complete, that a customer accepted work, or that the
request is ready for billing. `Mark work done` remains the distinct request-lifecycle action for
physical completion. V1 has no invoice, payment, accounting-sync, or ready-for-billing workflow.

### Undo, recovery, and touch accessibility

Removing a Draft line applies immediately and offers an Undo toast for five seconds. Undo is a
standard versioned server mutation; if the scope has changed, normal refresh/reconciliation wins
over restoring stale client state. The server, not the toast timer, authoritatively enforces the
five-second expiry. It retains a separate removed-line snapshot sufficient to recreate the original
line and provenance, because a removed Draft line is otherwise hard-deleted; restoring reinserts it
at its original display order and advances the scope version. Restore does not re-normalize other
Draft-line display orders; reads use their existing deterministic `DisplayOrder`, then line-id tie
break so Undo does not create unrelated edits.

A Quick action's field-read eligibility is point-in-time only. If its resolved catalog-item or
assembly target becomes unavailable before selection, the normal authoritative selection error and
reconciliation path applies; the client presents a contextual office-updated/unavailable notice.
No separate Quick-action-specific server error is introduced while field actions operate on the
resolved target id.

The composer is phone-first. Interactive controls provide a minimum 44 by 44 CSS-pixel touch
target, visible focus indication, and accessible names. On opening a writable composer, focus moves
to the unified search/type input after the dialog is available; the implementation must not obscure
the active input or sticky submit area behind the mobile keyboard. The sticky submit footer remains
available whenever submission is allowed and never hides line-edit controls or validation feedback.

## Required implementation follow-up

- Add the server submission invariant for one-or-more lines (`ProposedScopeErrors.EmptySubmit`).
- Design the Owner/Admin Quick scope action configuration/data/API seam before connecting the field
  composer to it; no static or client-only accelerator list is acceptable.
- Replace the existing optional-item pre-expansion path and ladder UI/tests with this contract.
- Validate all ADR-484 journeys plus duplicate selection, submit semantics, Undo conflict recovery,
  keyboard behavior, and accessibility at phone viewport before declaring the redesign complete.
