# ADR-463 — KeepRequestWorkSignal: Additive Cross-Module Work Signal

**Status:** Locked  
**Date:** 2026-07-30  
**Related:** ADR-450, build-log/108

## Decision

`KeepRequest.AttentionLevel`/`AttentionReason` remains the primary customer-attention model and is
never overwritten, narrowed, or replaced by a module. Cross-module operational signals — starting
with Price Book, Quotes & Materials' "proposed scope needs office review" — use a new, additive
`KeepRequestWorkSignal` projection instead:

```text
KeepRequestWorkSignal
- AccountId
- KeepRequestId
- SourceModuleKey   (Core-owned registry, e.g. price_book_quotes_materials)
- SignalKey         (Core-owned registry, e.g. proposed_scope_needs_office_review)
- RaisedAtUtc
- ResolvedAtUtc
- mutable state + concurrency token
- unique on (AccountId, KeepRequestId, SourceModuleKey, SignalKey)
```

- `SourceModuleKey` and `SignalKey` are registered values from a Core-owned registry — no arbitrary
  strings, no customer-loaded code, no emergent plugin bus.
- `AccountId` lives directly on the row (not only reachable via `KeepRequestId`) so every signal
  query is tenant-scoped without a join.
- The composite uniqueness rule prevents duplicate signals for the same request/module/signal
  combination.

## Resolution Semantics

Resolution is **aggregate-state-driven, not single-entity-driven**. A signal does not resolve the
moment any one qualifying entity (e.g. one `ProposedScope`) reaches its qualifying state (e.g.
`OfficeReviewed`). It resolves only when **no qualifying instance remains outstanding** on the
request:

- If two scopes are concurrently awaiting review on the same request, reviewing one must not clear
  the signal while the other remains pending.
- A later scope submitted after resolution **reopens the same logical row** (per the
  `(AccountId, KeepRequestId, SourceModuleKey, SignalKey)` uniqueness rule): set it active again,
  replace `RaisedAtUtc`, and clear `ResolvedAtUtc`. It never creates a second projection row for the
  same logical key. If a future requirement needs to reconstruct historical raise/resolve cycles,
  that is a separate audit/event record, not additional projection rows.
- Staff cannot dismiss a signal generically; only the owning module's own aggregate check resolves
  it.

The contract must preserve, per Build 107: "Proposed scope needs office review" never changes
`KeepRequestStatus`, never claims customer acceptance, and is never confused with an
invoice/payment state.

## Deferred

Whether an active signal changes queue bucket/ranking or renders as a separate actionable cue is an
explicit Session 3 UX decision, not embedded in this contract.

## Rationale

`KeepRequest`'s existing attention reasons are all Core-native customer-interaction concepts; this
is the first cross-module attention integration, so a dedicated, additive projection avoids forcing
a non-Core module to "fill in" Core's enum (as a generic new `AttentionReason` value would) or
requiring Core's list/detail read path to merge two independently-shaped signal sources ad hoc. This
extends ADR-450's server-authorized, request-keyed projection precedent, and is treated as more
operationally meaningful than that ADR's internal-note-presence dot, since it represents outstanding
work rather than passive context. Aggregate-state-driven resolution is required because per-entity
resolution would let a technician's second scope's review incorrectly report to staff that "office
review" work is fully clear when it is not.
