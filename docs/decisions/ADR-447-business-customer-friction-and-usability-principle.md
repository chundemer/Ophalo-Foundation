# ADR-447 — Business And Customer Friction / Usability Principle

**Status:** Locked  
**Date:** 2026-07-17  
**Related:** ADR-367, ADR-431, ADR-446, build-log/089-launch-verification-pass

## Context

Keep succeeds only if service businesses adopt it in real work and their customers willingly use the
pages and links those businesses provide. A feature can be technically complete, permission-safe,
and well documented while still failing the people who need to use it: an owner cannot tell what to
do between jobs, or a customer does not recognize a link well enough to submit a request.

The launch-readiness review showed that functional completion alone is insufficient as a product
standard. Public intake, account start, auth/recovery, operational actions, and customer return
flows must be judged by whether they reduce uncertainty and effort for the actual people involved.
This principle is intended to prevent future implementation, review, or AI-assisted work from
drifting toward system-centric completeness at the expense of usable outcomes.

## Decision

Every product, workflow, UI, copy, and launch decision must be evaluated from both the business and
customer perspective. The preferred solution reduces unnecessary friction while preserving the
information, confirmation, privacy, and recovery needed for a safe real-world outcome.

| Perspective | Required decision test |
|---|---|
| Business owner/staff | Does this reduce steps, guessing, duplicate entry, interruption, or follow-up work? Can a busy person see the next appropriate action and recover from a mistake without support? |
| Customer | Can this person recognize the intended business and page, understand what is being requested and why, know what happens next, and return or get help if the flow is interrupted? |
| Both | Is complexity shown only when it is necessary? Are defaults sensible, language plain, errors actionable, and the result/recovery path durable? |

Less friction does **not** mean hiding an important decision, weakening consent/privacy boundaries,
or removing a safety confirmation. It means avoiding unnecessary form fields, duplicate choices,
internal terminology, unexplained states, dead ends, and decorative complexity. Important complexity
must appear at the right moment with a clear reason and a usable path forward.

## Required Practice

- Define the business outcome and customer outcome before implementing a new workflow or materially
  changing an existing one.
- For every external/customer-facing surface, run the ADR-446 skeptical-first-time-person review.
- For every staff-facing workflow, verify the next action, error correction, draft preservation where
  applicable, and actual use under interruption, narrow screens, and long/realistic data.
- Prefer safe defaults, progressive disclosure, concise product language, and one clear primary
  action over exposing every capability equally.
- Treat error, empty, loading, expired, unavailable, confirmation, and return states as part of the
  workflow—not secondary implementation detail.
- When a tradeoff increases friction, document the reason, the harm it prevents, and how the UI
  explains or minimizes that burden.
- Do not close an implementation slice on typechecks/tests alone when the slice changes a human
  workflow. Include proportionate screenshot/manual evidence from the relevant business and customer
  viewpoints.

## Consequences

- Technical correctness is necessary but not sufficient for completion or release readiness.
- A workflow that is hard to understand, hard to recover, or implausible to trust is a product gap,
  even when its backend/API behavior is correct.
- Design reviews, ADRs, build logs, tracker items, and AI-assisted recommendations must explicitly
  identify friction/recovery/trust consequences rather than treating them as cosmetic follow-up.
- ADR-446 remains the specialized first-visit identity/trust rule; this ADR applies the usability and
  friction lens to the entire Keep product.
