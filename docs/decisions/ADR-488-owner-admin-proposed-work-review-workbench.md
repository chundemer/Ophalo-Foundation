# ADR-488 — Owner/Admin Proposed-Work Review Workbench

**Status:** Locked  
**Date:** 2026-08-17  
**Related:** ADR-463, ADR-464, ADR-480, ADR-481, ADR-487; Build Logs 117, 126

## Decision

Step 1 of the contractor operational loop is a bounded Owner/Admin proposed-work review workflow.
It makes submitted technician recommendations actionable without introducing commercial pricing,
Actual Work, customer acceptance, or technician-return workflows.

### Review meaning and record immutability

The explicit action is **Mark reviewed**. It means only that the office has reviewed a submitted
field recommendation. It does not mean the customer approved work, the office approved a price,
work is complete, or the request is closed.

`SubmittedToOffice` ProposedScope and its lines remain immutable. Owner/Admin may write an optional,
bounded internal review note, but cannot rewrite technician-captured scope, quantity, note, or
source snapshot. Office-owned commercial editing begins in the later Commercial Document workbench.
"Return to technician" and "Reject" are deferred until notification, resubmission, and
accountability semantics are explicitly designed.

### Queue and history

Owner/Admin receives a dedicated **Proposed Work Review** request-workspace queue containing only
requests with at least one `SubmittedToOffice` scope. It has a truthful count and row-level context:
submitted age, submitting technician, request/customer context, and concise line summary. It is
actionable office work, not customer-attention; it neither changes nor is merged into
`KeepRequest.AttentionLevel` / `AttentionReason`.

Request detail provides a read-only, newest-first history of every submitted and reviewed scope.
A later field visit remains a new ProposedScope row under ADR-464; no reviewed row is reopened.

### Resolution, authority, and concurrency

Mark reviewed transitions one submitted scope to `OfficeReviewed`, records reviewer, timestamp, and
optional review note, and requires its opaque expected concurrency version. A conflict reloads the
authoritative current state; it never overwrites another review.

The existing `ProposedScopeNeedsOfficeReview` signal resolves only when no `SubmittedToOffice` scope
remains on that request. Reviewing one of several submitted scopes leaves the signal active.

The workflow is Owner/Admin-only and requires the account's Price Book entitlement. It is blocked
for terminal requests; authorized readers retain read-only scope history. The mutation and all queue
and history reads remain tenant-scoped and server-authorized.

## Consequences

- Session 3.5 / Sequence Step 1 preflight defines the narrow endpoint, version-header contract,
  persistence transaction, list/read DTO additions, rank/cue behavior, error mapping, and tests.
- The request detail review surface may reuse the existing scope display components only in
  read-only mode. It must not expose the technician composer or imply office line editing.
- The later Commercial Document workbench may create a commercial draft from a reviewed scope while
  preserving source traceability. It is not part of this slice.
