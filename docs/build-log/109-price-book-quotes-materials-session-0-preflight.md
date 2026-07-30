# Build Log 109 — Price Book, Quotes & Materials: Session 0 Implementation Preflight

**Status:** Complete — decisions locked, Coding Session 1 authorized to begin
**Date:** 2026-07-30
**Scope:** Lock the remaining data-lifecycle and financial-correctness decisions Build 108 left
open, so Coding Session 1 cannot drift on them mid-implementation
**Related:** Build 107; Build 108; ADR-462 through ADR-468

## Purpose

Build 108's ERD preflight flagged seven open architecture/product questions before Coding Session 1.
ADR-462 and ADR-463 resolved the two structural ones (entitlement resolution, cross-module attention
contract). This record locks the remaining five, all of which affect data lifecycle or financial
correctness rather than architecture shape, and were confirmed directly with the product owner.

## Decisions Locked

1. **Repeated field visit after `OfficeReviewed`** — a later technician visit always creates a new
   `ProposedScope` row; the reviewed row is never reopened to `Draft`. **ADR-464.**
2. **Post-approval quote edit** — a new `QuoteRevision` created after `Approved` returns
   `OfficeQuote.Status` to `Draft`, requiring explicit resubmission rather than silently returning to
   `SubmittedForApproval`. **ADR-465.**
3. **Primary offering / assembly uniqueness** — `(AccountId, PrimaryCatalogItemId)` is unique among
   `Active` `OfferingAssembly` rows; a catalog item may still appear in multiple offerings overall,
   but at most one may be primary and active at once. **ADR-466.**
4. **Rounding policy** — traditional round-half-up (not banker's/`ToEven`); `QuoteLine.LineTotal`
   rounds independently and `QuoteRevision.TotalAmount` is the sum of already-rounded lines, never a
   single rounding of an unrounded sum. **ADR-467.**
5. **Currency** — confirmed one currency per account for MVP; multi-currency remains explicitly
   deferred. **ADR-468.**

Build 108's "Deliberately unresolved questions" section has been updated to point at all seven
resulting ADRs (ADR-462–468); none remain open.

## Outcome

No unresolved decisions remain that affect Coding Session 1's scope: capability foundation (feature
key/permission registration, `AccountCapabilityPackageEnrollment`, `AccountFeatureAccessResolver`,
and an Owner/Admin read-only package-status endpoint). No price-book, catalog, offering, proposed
scope, or quote tables are in scope for Session 1.
