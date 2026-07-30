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
