# ADR-444 — Launch Phone Validation: Normalized 10-Digit North American

**Status:** Locked  
**Date:** 2026-07-16  
**Related:** GAP-016, GAP-017, ADR-306, build-log/088-pwa-launch-readiness-remediation

## Decision

For launch, Keep enforces a normalized 10-digit North American phone number everywhere a phone
number is accepted: Quick Capture lookup, authenticated staff request-create, and public intake.

Accepted input: 10 digits, or 11 digits beginning with `1` (leading country code). Whitespace,
dashes, dots, parentheses, and `+` are stripped before digit counting. The stored/canonical form
is exactly 10 digits with no formatting.

International phone support and a country selector are deferred to a dedicated
internationalization slice; do not imply international support with an ambiguous wider digit range.

## Behavior

| Surface | Rule |
|---|---|
| Quick Capture lookup input | Disable lookup until exactly 10 normalized digits; show inline `Enter a 10-digit phone number.` |
| Capture-stage phone display | Replace read-only display with a **Change** action; returning to lookup preserves and focuses the number and re-runs duplicate lookup |
| Authenticated create API | Reject phone outside 10 normalized digits with a named 400 |
| Public intake API | Already validated via ADR-306; align digit rule to 10 (was 7–15) |
| `KeepRequestInputValidator` | Align to 10 normalized digits across all paths |

Preserved draft fields on **Change**: customer name, email, description, source, and address draft.

## Rationale

The current 7–15 digit window allows nine-digit numbers to reach the capture stage where the
operator cannot correct them without abandoning the request (GAP-016). A 10-digit North American
policy closes the trap, aligns with the pilot customer base, and avoids premature international
complexity. A country selector can be added in a single dedicated slice when international need is
confirmed.

## Consequences

- All three phone-accepting surfaces share one normalized validation rule.
- Operators cannot submit an uncorrectable phone; they can always return to lookup and fix it.
- International numbers are explicitly unsupported at launch; the UI must not suggest otherwise.
- The stored canonical form (10 digits, no formatting) is unchanged from existing practice.
