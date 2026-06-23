# Build Log 058 — Gap G7: Feedback Review Hardening

**Date:** 2026-06-23
**Gaps closed:** GAP-017, GAP-018, GAP-020
**ADRs implemented:** ADR-263, ADR-300 (G7c); ADR-186 note (G7b exception)
**ADRs corrected:** ADR-282 (deferred, not implemented), ADR-283 (G7b source note)

---

## G7 Summary

G7 hardened three feedback-review behavioral gaps identified in the pre-Session-6 gap audit.

### G7a — Generic acknowledgement blocked for UnresolvedFeedback (commit `a9e87bd`)

- `AcknowledgeAttentionService` returns dedicated 409 `KeepRequest.AttentionRequiresFeedbackReview`
  when the request carries `UnresolvedFeedback` attention reason.
- Shared action policy removes the acknowledgement affordance (`CanAcknowledge = false`) in that
  state; all other attention paths remain unaffected.
- 2 new tests. Baseline after G7a: 1190.

### G7b — Owner/Admin outbound external-contact exception (commit `4f583d8`)

- Owner/Admin may log outbound external contact on a `Closed` request if and only if an active
  `UnresolvedFeedback` review attention is present (`HasActiveUnresolvedFeedbackReview` predicate).
- The exception is internal-only, does not count first response, does not reopen or change status,
  does not notify the customer, and does not automatically mark feedback reviewed.
- Operator, inbound, ordinary Closed, and Cancelled paths remain blocked.
- `HasActiveUnresolvedFeedbackReview` drives domain guard, service authorization, shared action
  policy (`CanLogExternalContact`), list affordances, and detail affordances coherently.
- 30 new tests. Baseline after G7b: 1220.

### G7c — Positive-feedback comment visibility correction (this commit)

**Root cause (GAP-020 / ADR-300):** `KeepRequestDetailMapper` used `role is Owner or Admin` as
the sole gate for `feedbackCommentVisible`, contradicting ADR-263's policy that positive feedback
is broadly visible to authenticated request viewers who pass G4 row access.

**Fix — `KeepRequestDetailMapper.cs`:**

- `feedbackCommentVisible` now evaluates to `true` for Owner/Admin regardless of feedback outcome,
  and additionally to `true` for any other authenticated row-authorized reader when
  `FeedbackWasResolved == true`.
- A separate `reviewNoteVisible` variable (Owner/Admin only) guards `FeedbackReviewNote`
  independently, preserving the Owner/Admin-only review-note contract even when
  `feedbackCommentVisible` is `true` for a lower role on a positive-feedback request.
- No changes to public customer-page mapping, list visibility, DTO fields, routes, or persistence.

**New tests — `KeepRequestDetailB4Tests.cs`:**

- Added `_positiveFeedbackRequestId` seed: Closed request with `wasResolved: true` and comment
  "Great service, very happy." Operator has a participation entry for row access.
- 4 new tests: Owner/Admin/Operator/Viewer on positive feedback all see comment and
  `feedbackCommentVisible=true`; Operator and Viewer tests additionally assert
  `feedbackReviewNote=null`.
- All 12 pre-existing B4 tests pass unchanged (negative-feedback redaction for Operator/Viewer
  preserved; Owner/Admin negative comment preserved; unresolved-attention mapping preserved;
  G7b contact-action tests preserved).
- Total B4 tests: 16.

---

## Deferred ledger reconciliation

- **DEF-029** updated to Implemented (mark-reviewed was built in Sessions 5A-5C; status was stale).
- **DEF-064** updated to Implemented by G7b.
- **ADR-282** corrected from Implemented to Deferred — Session 6 (feedback-review navigation/
  next-previous was never built; the Session 5 ADR was mis-tagged).

---

## Exclusions

- No feedback-review navigation/next-previous (ADR-282, Session 6 scope).
- No customer-visible feedback replies, receipts, or reopen workflow.
- No public customer-page exposure of feedback comments.
- No notification or reopen behavior.
- No status mutation, migration, or new DTO fields.
- List/search feedback-comment visibility not changed (no named test proved current list code
  violates ADR-263; the known defect was authenticated detail mapping only).

---

## Final test counts

| Suite | Count |
|---|---|
| Unit | 643 |
| Architecture | 14 |
| Integration | 567 |
| **Total** | **1224** |
