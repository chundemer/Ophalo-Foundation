# Build Log 037 — Phase 8-B4 Service Request Detail Enrichment — Implementation

**Phase:** 8-B4 implementation
**Date:** 2026-06-17
**Status:** Complete. All tests pass.
**ADRs implemented:** 145..163 (decisions locked in build-log/036)
**Tests:** 510/510 (280 unit · 14 arch · 216 integration)
**Previous log:** `036-phase-8-b4-service-request-detail-enrichment-decisions.md`
**Next free ADR:** ADR-164

---

## What was built

B4 is a no-migration read-model enrichment slice. No schema changes. All new behavior
derives from existing tables using current-user role and operate permission.

---

## Files changed

| File | Change |
|------|--------|
| `Keep.Application/Requests/KeepRequestDetailResult.cs` | Add `ContactActionItem` record; add `FeedbackCommentVisible` and `ContactActions` fields to `KeepRequestDetailResult`; remove stale B4 comment from `KeepRequestParticipantItem` |
| `Keep.Application/Requests/KeepRequestDetailMapper.cs` | Extend `ToDetailResult` with `AccountUserRole role` + `bool canOperate`; compute `FeedbackCommentVisible` from role; redact `FeedbackComment` for Operator/Viewer; add `BuildContactActions` (populates only when `canOperate`, only available actions included) |
| `Keep.Application/Requests/IKeepRequestDetailPersistence.cs` | Update `GetParticipantsAsync` and `KeepParticipantProjection` doc comments to reflect B4 enrichment |
| `Keep.Infrastructure/Persistence/EfKeepRequestDetailPersistence.cs` | Update `GetParticipantsAsync` to project `User.Name` via the `AccountUser.User` navigation (EF LEFT JOIN); compute `DisplayName = nonblank UserName ?? Email` |
| `Keep.Application/Requests/GetKeepRequestDetailService.cs` | Pass `userSnapshot.Role` and `canOperate` to `ToDetailResult` |
| `Keep.Application/Requests/ChangeKeepRequestStatusService.cs` | Pass `userSnapshot.Role` and `canOperate: true` to `ToDetailResult` |
| `Keep.Application/Requests/AddBusinessUpdateService.cs` | Pass `userSnapshot.Role` and `canOperate: true` to `ToDetailResult` |
| `Keep.Application/Requests/AddInternalNoteService.cs` | Pass `userSnapshot.Role` and `canOperate: true` to `ToDetailResult` |
| `Keep.Application/Requests/AcknowledgeAttentionService.cs` | Pass `userSnapshot.Role` and `canOperate: true` to `ToDetailResult` |
| `tests/IntegrationTests/Api/KeepRequestDetailB4Tests.cs` | New — 8 B4 integration tests (see below) |
| `tests/IntegrationTests/Api/KeepCustomerPageTests.cs` | Add Test 4 (operator fields not exposed) and Test 5 (expired page NewRequestUrl = null) |

---

## Key design decisions carried into implementation

**Feedback comment redaction** lives in the mapper (`KeepRequestDetailMapper.ToDetailResult`),
not in persistence. Persistence returns raw data; the application layer applies role-aware
redaction before building the result. `FeedbackCommentVisible` is a DTO field only —
no migration.

**Contact actions** are built from `CustomerPhone` and `CustomerEmail` already on `KeepRequest`.
`BuildContactActions` only includes present values. Returns `[]` for users without
`keep.requests.operate`. No logging, no attention clearing, no first-response side effects.

**Participant display name** computed in `EfKeepRequestDetailPersistence.GetParticipantsAsync`
via EF LEFT JOIN through the `AccountUser.User` navigation property:
```csharp
.Select(au => new {
    ...,
    UserName = au.UserId != null ? au.User!.Name : null
})
```
`DisplayName = !string.IsNullOrWhiteSpace(au.UserName) ? au.UserName : au.Email`

Invited members (no `UserId`) and members whose `User.Name` is empty both fall back to
`AccountUser.Email`. The two-query approach (participants first, then AccountUsers) is
retained; the User.Name is projected within the AccountUsers query, not a third query.

**Write services** (`ChangeKeepRequestStatus`, `AddBusinessUpdate`, `AddInternalNote`,
`AcknowledgeAttention`) all gate on `keep.requests.operate`, so they pass `canOperate: true`
explicitly. `GetKeepRequestDetailService` passes the computed `canOperate` variable.

---

## Integration test matrix (ADR-156)

New `KeepRequestDetailB4Tests` class:

| # | Test | Covers |
|---|------|--------|
| 1 | Operator with phone+email → 2 contact actions (call + email) | ADR-149, 160, 161 |
| 2 | Viewer role → contactActions = [] | ADR-161 |
| 3 | Owner sees feedbackComment + feedbackCommentVisible = true | ADR-151 |
| 4 | Admin sees feedbackComment + feedbackCommentVisible = true | ADR-151 |
| 5 | Operator: feedbackComment = null, feedbackCommentVisible = false | ADR-151 |
| 6 | Viewer: feedbackComment = null, feedbackCommentVisible = false | ADR-151 |
| 7 | Participant with User.Name shows name; Invited participant shows email | ADR-146 |
| 8 | Closed+unresolved_feedback: status=closed, attentionReason=unresolved_feedback, waitingDirection=business, priorityBand=priority | ADR-152 |

New in `KeepCustomerPageTests`:

| # | Test | Covers |
|---|------|--------|
| 4 | Customer page does not expose requestId, pageToken, contactActions, participants, attention fields, FeedbackComment, feedbackCommentVisible | ADR-155 |
| 5 | Expired customer page: newRequestUrl = null | ADR-150 |

---

## Completion gate (ADR-163)

- [x] No migration generated
- [x] Detail returns `ContactActions` and `FeedbackCommentVisible`
- [x] Participant display uses `User.Name` with email fallback
- [x] `FeedbackComment` is Owner/Admin-only; Operator/Viewer receive null
- [x] Viewer receives `ContactActions = []`
- [x] Customer page boundary tests remain green
- [x] B4 integration matrix passes
- [x] Full test suite passes (510/510)
