# ADR-464 — Repeated Field Visit Creates A New ProposedScope Row

**Status:** Locked  
**Date:** 2026-07-30  
**Related:** build-log/107, build-log/108, ADR-463

## Decision

When a technician visits the same `KeepRequest` again after an existing `ProposedScope` has already
reached `OfficeReviewed`, the later visit creates a **new** `ProposedScope` row. The existing,
already-reviewed `ProposedScope` is never reopened or mutated back to `Draft`.

## Rationale

A new row per reviewed visit preserves a clean, immutable audit trail of exactly what the office
reviewed and when. Reopening a reviewed record to `Draft` would let a "reviewed" scope silently
un-review itself, which directly conflicts with ADR-463's aggregate-state-driven
`KeepRequestWorkSignal` resolution: that model already assumes multiple concurrently pending
`ProposedScope` rows can coexist on one request, and resolves the signal only when none remain
outstanding. Creating a new row per visit is the natural fit for that model, rather than a special
case.
