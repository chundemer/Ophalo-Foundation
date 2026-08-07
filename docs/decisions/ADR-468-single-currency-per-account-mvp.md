# ADR-468 — Single Currency Per Account For MVP

**Status:** Locked  
**Date:** 2026-07-30  
**Related:** build-log/107, build-log/108

## Decision

Price Book, Quotes & Materials assumes exactly one currency per account for MVP. Multi-currency
support remains explicitly out of scope and deferred.

## Rationale

Confirmed against the pilot contractor's actual needs; nothing in Build 107/108's data or money
model requires multi-currency, and introducing it now would reopen the price-book/quote money model
before Coding Session 1 for no present requirement.

## Amendment — 2026-08-05 (build-log/113, 2e.5)

The single account currency this MVP assumes is **USD**, and there is still no server-owned
account-currency source for the client to read. Christian explicitly approved a USD-only pilot
posture for the Price Book catalog-item creation drawer: `CatalogItemDrawer` sends a hard-coded
`"USD"` currency value deliberately, not as an unresolved gap. A server-owned account-currency
setting remains required before multi-currency or non-USD pilot accounts are supported; introducing
one is out of 2e.5's scope.
