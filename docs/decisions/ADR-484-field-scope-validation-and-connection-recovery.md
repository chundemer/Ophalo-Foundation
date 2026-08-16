# ADR-484 — Field Scope Validation and Connection Recovery

**Status:** Locked  
**Date:** 2026-08-15  
**Extends:** ADR-482, ADR-483  
**Related:** ADR-480, ADR-481

## Decision

The unified field scope composer must be validated in a phone-sized prototype and implementation
against the following operational journeys before it is considered complete:

1. **Assembly plus delta:** the technician explicitly selects an assembly Quick scope action; its
   default lines expand into the Draft; the technician searches for and adds a specific additional
   catalog part, then submits.
2. **Direct custom item:** the technician types a description, explicitly selects `Add "…" as
   custom item`, sets a quantity, and submits without catalog browsing.
3. **Clean-slate scope:** a technician opens a new scope with zero pre-populated lines, adds known
   items, and submits.
4. **Accidental draft-line removal:** the technician removes an assembly-added line by mistake and
   restores it from the short Undo affordance. Expanded lines are not described as required or
   locked once in the Draft.
5. **Decimal quantities:** the technician records decimal quantities such as 1.5 hours of labor or
   0.5 gallons of material. All Draft line types accept positive decimals at the domain/API layer;
   unit conventions may guide presentation but cannot impose integer-only or unit-specific field
   validation.
6. **Connection interruption:** a failed add, edit, remove, or submit leaves authoritative server
   state unchanged; the UI makes the failure clear, preserves safely entered input where practical,
   and offers explicit retry/reconciliation without silently overwriting newer data.
7. **Concurrent change or terminal transition:** another authorized user changes the same Draft, or
   the request becomes terminal, while the technician is working. The client refreshes the
   authoritative state, explains the change, and never auto-retries or clobbers work.

## Offline boundary

V1 is not an offline-first scope composer. It does not promise local durable drafts, an offline
mutation queue, automatic replay on reconnection, or conflict resolution for queued mutations.
Those capabilities require a separate decision covering durable device storage, replay ordering,
authentication expiry, visibility of unsynced work, conflict handling, and no-data-loss guarantees.

The V1 recovery behavior is the existing server-authoritative model: a mutation is saved only after
server success. Failures are visible and recoverable through explicit retry/reconciliation, not
through a hidden client-side queue.

For an explicit custom-item add, the composer retains the typed description, quantity, note, and
inline validation state until the server confirms the add successfully. Selecting `Add "…" as
custom item` is not permission to clear the input optimistically. Network failure, timeout,
validation failure, or a concurrency conflict leaves the safely entered values available for an
explicit retry; only a successful response clears the custom-item input for the next entry.

## Scope and terminology

An assembly is an explicitly selected Quick scope action; its resulting Draft entries are
**assembly default lines**, not automatically inserted baseline work. Office quote pricing is a
separate workflow and must never mutate a submitted technician proposed-scope snapshot. The relevant
concurrency validation is concurrent Draft editing or a request-state transition, not an office
price edit.

## Consequences

- The focused composer UI-design/preflight must use these journeys as its acceptance scenarios.
- The implementation/tests must preserve authoritative re-fetch and no-auto-retry behavior on
  conflict or failed network requests.
- A composer opened on mobile must be validated with the virtual keyboard open in iOS Safari and
  Android Chrome: the focused input, feedback, and submit control remain usable and unobscured.
  The implementation mechanism is deliberately unconstrained.
- Offline-first capability is deferred pending separate pilot evidence and design authorization.
