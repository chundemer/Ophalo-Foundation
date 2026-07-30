# ADR-467 — Rounding Policy: Round-Half-Up, Sum Of Already-Rounded Lines

**Status:** Locked  
**Date:** 2026-07-30  
**Related:** build-log/107, build-log/108, ADR-458

## Decision

Money math uses traditional round-half-up (not banker's/`ToEven`) rounding. Each `QuoteLine.LineTotal`
is rounded independently, and `QuoteRevision.TotalAmount` equals the sum of those already-rounded
line totals — never a single rounding applied to an unrounded sum.

## Rationale

Round-half-up matches how contractors and customers read a quote: the printed line totals are the
values that were actually rounded, and the printed grand total is their exact sum, so nothing can
look like a cent-level math error. Rounding a single unrounded sum instead can make `TotalAmount`
differ by a cent from the sum of the printed line totals — technically correct under one rounding
convention, but indistinguishable from an error to non-technical staff or a customer. Banker's
rounding reduces aggregate bias across many transactions but is not the rounding behavior anyone
manually re-adding a quote's line items would expect on a single document.
