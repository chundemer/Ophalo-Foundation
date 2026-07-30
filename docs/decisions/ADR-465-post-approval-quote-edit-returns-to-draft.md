# ADR-465 — Post-Approval Quote Edit Returns OfficeQuote To Draft

**Status:** Locked  
**Date:** 2026-07-30  
**Related:** build-log/107, build-log/108

## Decision

Creating a new `QuoteRevision` after `OfficeQuote.Status = Approved` returns `OfficeQuote.Status` to
`Draft`. It never transitions directly back to `SubmittedForApproval`.

## Rationale

Requiring an explicit resubmission step after any post-approval edit means an approval workflow is
never silently re-triggered by a small change. `Draft` forces a deliberate office action to resend
the edited quote for approval, which is worth the small extra step given `QuoteRevision` is a
financially consequential, server-authoritative artifact (ADR-458).
