# Build Log 102 — Request List Freshness and Large-Account Scalability Discussion

**Status:** Discussion record — no implementation decision is locked
**Date:** 2026-07-28
**Scope:** Authenticated Request List freshness, own-write visibility, polling, and large-account
capacity
**Related:** Build 092–096; Build 101; Build 103; Build 104; Build 106; ADR-449; ADR-450

## Why this needs a separate decision

Recent customer conversations introduced two materially different operating scales:

- a small HVAC team with roughly ten technicians; and
- a potential property-management-related workflow spanning a portfolio of roughly 30,000 homes
  and buildings across multiple states.

The question is not simply whether a 30-second poll costs money. It is whether Keep can keep the
Request List accurate and responsive for many users and large active-work volumes without loading,
ranking, counting, or transmitting far more data than the current screen needs.

This document is intentionally separate from the contractor asset/price-book discussion in Build
101. Request List capacity is a platform concern that affects every authenticated workflow. Under
Build 104, the first contractor pilot supplies a measured baseline; it is not evidence for a
property-manager portfolio commitment.

## Current implementation — factual baseline

### Client freshness behavior

`web/ophalo-app/src/pages/Requests.tsx`:

- uses server-backed React Query entries keyed by view, submitted search/status, cursor, and history
  date scope;
- polls the active first cursor page every 30 seconds;
- refetches that first page when the browser window regains focus;
- does not poll later cursor pages; and
- uses the cached response on an already-visited queue before a background refresh resolves.

The current Quick Capture success path does **not** explicitly invalidate/refetch Request List
queries after creating a request. The creator can therefore wait for the poll/focus refresh before
seeing a server-eligible newly created request. This is an identified own-write freshness gap, not
an approved implementation change in this document.

### Server list behavior

The Request List response is server-authoritative for account/role visibility, queue membership,
ranking, page cursor, and view counts. This is essential: the client must not locally invent where a
new or changed request belongs.

The active-view implementation currently obtains broad matching candidate sets, applies some
ranking/eligibility logic in application memory, then slices the requested cursor page. It also
calculates several role-aware queue counts per response. Although the response page is bounded, the
active-query work is not yet proven bounded by page size for a high-volume account.

### What that means

A 30,000-property portfolio does not automatically produce 30,000 active Keep requests. However,
hundreds or thousands of active records plus several concurrent dispatcher/technician views would
make repeated broad candidate retrieval and in-memory ranking a capacity risk. The current path has
not been load-tested or promised at that scale.

## Principles already worth preserving

- The server remains the authority for authorization, role scope, ranking, queue membership,
  count/row reconciliation, and cursor integrity.
- A user’s successful write should become visible promptly, but only when the resulting request is
  actually eligible for the currently selected server-owned view.
- Cursor pages must not claim an exact total that the contract does not provide.
- Freshness must not create a separate client-side sorting or hidden-cache policy that diverges from
  the server.
- Any solution must continue to handle time-driven changes—such as response deadlines or follow-up
  dates becoming due—even when no user mutation occurred.
- No decision in this document authorizes WebSockets, SSE, Redis, broad caching, or a database
  rewrite by itself.

## Options for discussion

### Option A — keep the current polling model, add own-write refresh

After a successful create/mutation, invalidate/refetch the affected Request List queries; retain
30-second polling and focus/reconnect refresh for cross-user changes.

**Benefit:** small, low-risk correction for the immediate user experience.

**Limit:** does not make the underlying active-list query safe at large volume.

### Option B — bound list work in the database

Move filtering, deterministic ranking, and keyset/cursor pagination to database execution so the
server obtains only the requested page plus the minimal lookahead. Re-evaluate queue counts with
measured query plans and preserve their exact current role/scope semantics.

**Benefit:** addresses the primary scale risk while retaining the present HTTP/query model.

**Limit:** ranking and eligibility rules currently contain time- and role-sensitive behavior; this
is a correctness-sensitive redesign, not a mechanical `OFFSET` conversion. Preserve the existing
keyset cursor contract rather than adopting offset paging.

### Option C — lightweight change/revision check before full list refresh

Maintain an account- and viewer-aware list revision (or comparable cheap change signal). The client
polls that signal; it fetches full list rows/counts only when an applicable change occurred.

**Benefit:** can reduce repeated full responses and expensive list work for idle queues.

**Limit:** a simple `MAX(updated_at)`/ETag is insufficient by itself. Time-driven queue/ranking
changes can occur without a record write. Any revision design needs a defined next-refresh boundary
or equivalent treatment for those transitions, and it must not leak cross-account/activity data.

### Option D — authenticated event stream (SSE/WebSocket)

Use account-scoped server events to tell active clients that a relevant list view is stale, then
refetch the server-authoritative list.

**Benefit:** near-immediate cross-user freshness without client polling frequency.

**Limit:** introduces connection lifecycle, reconnect/missed-event recovery, account/role fan-out,
deployment/load-balancer behavior, observability, and operational support. It does not remove the
need for a bounded list query. It should not be selected merely to fix own-write refresh.

## Decisions required before a large-account commitment

1. What active-request volume, history volume, and concurrent active-user count must be supported
   for the property-management-associated workflow?
2. What p95 latency and error-rate targets apply to first-page list load, mutation completion,
   create-to-list visibility, and queue counts?
3. Which list views must update immediately for the actor, and what cross-user freshness delay is
   acceptable for dispatch/office users versus technicians?
4. Which time-driven states must surface at their deadline even without a user write?
5. Can counts be eventually fresh, or must every visible count reconcile with rows at every refresh?
6. Do high-volume accounts need different entitlement/capacity limits or an explicit account class?
7. Is an initial constrained regional/portfolio pilot acceptable before any portfolio-wide launch?

## Required evidence before selecting an architecture

- A representative seeded or safely sanitized account with agreed active/history volumes, queue
  distributions, participants, and request-event density.
- Load tests at agreed concurrent active users and request rates.
- Database `EXPLAIN ANALYZE`/equivalent measurements for each required queue and count query.
- API p50/p95/p99 latency, database CPU/read volume, API memory, response size, and error results.
- Tests proving row/count reconciliation, authorization scope, cursor stability, own-write refresh,
  cross-user refresh, and deadline-driven refresh behavior.
- A documented rollback/degradation behavior if a freshness mechanism is unavailable.

## Provisional sequencing — not a commitment

1. Discuss and set the workload/SLO envelope with the prospective customer.
2. Correct the narrow own-write visibility gap only when it can be isolated from broader list work.
3. Run the representative-load discovery and establish whether the current active-list path meets
   the envelope.
4. If it does not, select and implement a bounded database-query path before onboarding a
   portfolio-wide account.
5. Decide whether revision polling or an event stream is warranted only after the bounded-query
   result is measured.

## Explicit non-decisions

- No statement that Keep is currently ready for a 30,000-property portfolio.
- No decision to abandon polling, adopt real-time streaming, cache counts, or use a simplistic
  `updated_at` ETag.
- No authorization to weaken server-owned visibility/ranking, fabricate totals, or locally insert
  rows into queues without server confirmation.
