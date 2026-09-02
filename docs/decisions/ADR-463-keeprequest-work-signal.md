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

Whether an active signal changes queue bucket/ranking remains out of scope for this contract.

## Amendment — 2026-09-02 (GAP-065 Slice 3a)

The Session 3 UX decision on rendering the `actual_work_needs_office_review` signal as a cue is
resolved: the request list shows a **quiet, non-interactive Owner/Admin row metadata line**
("{N} visit(s) need financial review"). The count is the **exact server-authoritative** number of
live submitted / unreviewed / non-superseded Actual Work visits on the request (same predicate as
`EfActualWorkFinancialReviewPersistence.GetUnreviewedQueueAsync`), computed as a bounded batched
projection over the caller's already-sliced list page — never a client-derived value or a re-derived
predicate. It is gated **identically to the Actual Work Review destination**
(`ActualWorkFinancialReadApiService.AuthorizeAsync`): Owner/Admin role plus `RequestsOperate`,
`AccountingManage`, the Price Book, Quotes & Materials entitlement, and account access evaluated
with the office-financial Off Season context (`RequestImplementsAllowedInOffSeason: false`),
rejecting a Blocked **or** read-only account. The ordinary request list stays available in Off
Season, but the destination is read-only there — so an account that cannot open the destination
(entitlement disabled with retained history, Off Season, past-due grace) shows neither the cue nor
a dead link. The cue does **not** change queue bucket, ranking, Attention, view counts, or
normal row-click routing (the row still opens Request Detail). Operators and Viewers never receive
it. A cross-request, one-row-per-visit review queue remains later work with its own read model,
authorization, and ranking decision.

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
