# ADR-452 — Request List Lifecycle Cue Is the Existing Status Chip

**Status:** Locked
**Date:** 2026-07-27
**Related:** ADR-447, ADR-449, ADR-450, GAP-027, Build 087, build-log/095

## Decision

The Request List's truthful lifecycle representation is the existing single, server-authoritative
status chip (`received` / `scheduled` / `in_progress` → "Active" / `pending_customer` / `resolved` →
"Work completed" / `closed` / `cancelled`) plus at most one deterministically-selected exception
pill, as already delivered by Build 087 and unchanged since. No persistent multi-stage milestone
strip (`Received → Scheduled → Active → Work completed → Closed`) is added to the row.

## Rationale

- **Scannability over completeness.** A dispatch-style list row must answer "what is the current
  status" and "does this need my attention" in well under a second. A five-stage stepper on every
  row adds width, competes with the exception pill, and does not answer either question faster than
  the current chip.
- **Service work is not linear.** Real trade-service requests move `Received → Active → Pending
  customer → Active → Scheduled → …` non-sequentially; a fixed-stage stepper implies a pipeline the
  domain does not have. The status chip states the request's actual current state without
  fabricating a traversed path.
- **Pre-launch scope discipline.** Adding a new persistent row component immediately before go-live
  carries CSS/overflow, mobile-width, and state-mapping regression risk for a component that does
  not improve either scan question. The existing chip is already locked terminology (ADR-425,
  ADR-434) and already ships with full test coverage.

## Consequences

- GAP-027's other two required-resolution items — deterministic single-exception priority (server
  `rankingGroup`/`severity`-driven, one pill per row) and count/row reconciliation (queue tab/summary
  counts agreeing with visible urgency) — are unaffected by this decision and were already
  implemented in Build 087 and session 3.0d.
- No new row UI, API field, or client-side lifecycle state machine is introduced. `RequestRow.tsx`
  is unchanged by this ADR.
- Session 3.4 is verification-only: confirm the existing behavior against GAP-027's acceptance
  criteria with the existing test suite, and record the result in build-log/095.
- If a future pilot signal shows staff cannot infer lifecycle position from the status chip alone,
  a narrower affordance (e.g. a tooltip/expanded detail-only view) may be proposed as a new decision;
  this ADR does not preclude that, only the persistent per-row milestone strip.
