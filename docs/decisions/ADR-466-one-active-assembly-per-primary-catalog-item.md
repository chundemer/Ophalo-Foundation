# ADR-466 — One Active Assembly Per Primary Catalog Item

**Status:** Locked  
**Date:** 2026-07-30  
**Related:** build-log/107, build-log/108, ADR-457

## Decision

`(AccountId, PrimaryCatalogItemId)` is unique among `Active` `OfferingAssembly` rows. A catalog item
may still appear in multiple offerings/assemblies overall (per Build 107), but at most one of those
assemblies may be both *primary* and *Active* for that item at any time.

## Rationale

The technician-facing escape ladder (ADR-461) exists specifically to give the field user zero
ambiguity at each rung. Allowing more than one active assembly per primary item would leave
"selecting this primary offering expands which default items" undefined precisely at the ladder's
fastest, most-used rung. Build 107/108 have no variant-selection mechanism to disambiguate multiple
active assemblies, so uniqueness is the only option that keeps the primary-offering rung
deterministic.
