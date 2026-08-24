# ADR-492 — New Request Possible Existing-Customer Lookup

**Status:** Locked  
**Date:** 2026-08-24  
**Related:** ADR-444, ADR-447, GAP-025, build-log/067-session-13-pwa-workbench

## Decision

New Request phone lookup has two distinct successful-match states:

1. An exact `KeepCustomer.CanonicalPhone` match is an **existing customer**. Keep shows the
   customer, up to three active request cards, and the existing explicit **Create New Request for
   [Name]** action.
2. A normalized match found only in a historical `KeepRequest.CustomerPhone` is a **possible
   existing customer**. It is continuity evidence, not proof that the current caller remains the
   same person: the number may be stale, shared, or recycled.

The possible-match screen identifies the customer/request context as possible, shows up to three
linked active requests when any exist, and allows staff to open one. It must never auto-select a
request, auto-navigate, or silently attach a new request to that customer. With only historical
work, it shows a concise prior-request cue rather than a full history browser.

Staff then make an explicit choice:

- **Use existing customer details** continues the new-request flow with the candidate's identity
  prefilled and records the deliberate reuse choice; or
- **Create as new customer** continues with the entered phone but no candidate identity/reuse.

The create contract must make the reuse choice explicit and server-authorized. A request-phone
fallback alone must never create/link/backfill a `KeepCustomer` record. Account isolation, phone
normalization, and the existing max-three / activity-descending active-request behavior remain
unchanged.

## Amendment (2026-08-24) — candidate identity and reuse contract

`KeepRequest.KeepCustomerId` is non-nullable: the historical request that produces a possible
match already links to a real `KeepCustomer` row, even though that customer's *current*
`CanonicalPhone` no longer matches the entered number. This settles three points left open by the
original decision text:

1. **Candidate identity.** The possible-match result carries that request's real
   `KeepCustomerId` as a *candidate*, not confirmed identity. The historical link proves a
   tenant-scoped relationship to a real customer record; it does not prove the current caller is
   that person. The "Possible existing customer" label and the explicit staff choice below are
   the safeguard for stale, shared, or recycled numbers — not identity-less display text.
2. **Linked active requests.** Query active requests by the candidate's `KeepCustomerId`
   (reusing the same lookup the exact-match path uses), not by raw-phone regexp match. This
   correctly surfaces all of the candidate's active work regardless of which phone
   formatting/number was typed on each individual request.
3. **Reuse contract.** **Use existing customer details** sends the candidate's `KeepCustomerId`
   to the create endpoint explicitly. The server must verify the ID is tenant-scoped and was a
   valid candidate for this lookup, then attach the new request to that customer. It must not
   silently update the customer's `CanonicalPhone` to the newly entered number. **Create as new
   customer** is a distinct path that does not attach to the candidate at all.

The only behavior a bare phone-number entry may ever produce is a lookup result. Attachment to an
existing customer happens solely through the explicit reuse action.

## Consequences

The old GAP-025 behavior—silently carrying a raw request-phone match as a form prefill—has been
superseded. It hid active-work context and made a duplicate request too easy to create. The lookup
gate remains a brief decision point, not a customer-history workspace: exact matches use the
existing customer-found screen; possible matches make uncertainty visible and require an operator
choice.
