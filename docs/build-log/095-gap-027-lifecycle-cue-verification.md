# Build Log 095 — GAP-027 Lifecycle Cue Verification (No New Row UI)

**Status:** Complete
**Date:** 2026-07-27
**Scope:** Session 3.4 — verification-only, plus one unrelated pre-existing test-drift fix
**Controlling work to preserve:** Build 087; Session 3.0d; ADR-452

## Decision

ADR-452 locks the Request List's lifecycle cue as the existing single status chip plus at most one
deterministic exception pill — no milestone/stage strip is added. See ADR-452 for full rationale.

## Verification

`RequestRow.tsx` has not changed since ADR-450 (session 3.0c); sessions 3.1–3.3 did not touch row
exception/priority logic. Existing coverage was checked against each locked GAP-027 criterion:

- **One status cue + at most one actionable exception pill, deadline merged into the label, quiet
  unbordered planned/follow-up metadata:** `resolveException()` (`RequestRow.tsx`) is driven solely
  by the server's `ranking.rankingGroup`/`severity` — no client-side re-ranking. Covered for all six
  required lifecycle states (received-overdue, active, waiting-on-customer, work-completed, closed,
  closed-with-unresolved-feedback) by `RequestRow.test.tsx`.
- **Server ranking is the sole priority authority:** confirmed by inspection — no severity
  comparison exists outside the server-supplied `rankingGroup`.
- **Closed/cancelled rows suppress ordinary SLA/follow-up alarms, retain only the unresolved-feedback
  exception:** covered by `RequestRow.test.tsx` ("Closed row suppresses a stale overdue follow-up
  alarm and response-overdue badge"; "Closed row with unresolved negative feedback keeps the
  Feedback pending exception").
- **Queue tab/summary counts agree with visible row urgency:** the Needs Attention count/row-context
  mismatch was found and fixed in session 3.0d (`GetKeepRequestListService.ComputeRowContext`).
  `KeepRequestListServiceTests.cs` has dedicated `RowContext`/`ViewCounts` coverage across Default,
  Needs Attention, Ready to Close, Feedback Review, Waiting-on-Customer, and history views.

No gap was found against any GAP-027 acceptance criterion. No new production files, no new tests,
and no changes to `RequestRow.tsx` were required.

## Unrelated Test-Drift Fix Found During Verification

Full-suite verification surfaced two integration tests asserting pre-ADR-451 behavior:

- `AddBusinessUpdateTests.AddBusinessUpdate_FirstContactOnCustomerOriginRequest_WiresFirstResponse`
  asserted that a page-only business update sets `firstRespondedAtUtc`.
- `AcknowledgeAttentionTests.BusinessUpdate_ClearsBusinessWaitingAttention` asserted that a page-only
  business update clears business-waiting attention.

ADR-451 (session prior to this one) explicitly locked page-only updates as never setting first
response and never clearing gated attention — confirmed channel-appropriate contact is required. The
production domain methods (`KeepRequest.AddBusinessUpdate`/`AddBusinessUpdateWithStatus`) already
implement ADR-451 correctly; only these two tests were stale. Both were corrected to assert
preservation of the unresolved/unresponded state, renamed to reflect the current contract
(`..._DoesNotWireFirstResponse`, `BusinessUpdate_DoesNotClearBusinessWaitingAttention`), and now
mirror the existing `SilentStatusChange_DoesNotClearBusinessWaitingAttention` assertion shape. This
is unrelated to GAP-027; it is recorded here because it was found during this session's full-suite
verification pass and fixed before the suite could be honestly reported green.

## Test Results

- Frontend: 185/185; `tsc --noEmit` and CSS-token check pass.
- Backend unit: 1,207/1,207.
- Architecture: 14/14.
- Integration: 933/933 (previously 931/933; the two ADR-451 test-drift failures above are fixed).
