# Build Log 035 — Phase 8-B3+: Closed-Request Feedback Implementation

**Phase:** 8-B3+ implementation
**Date:** 2026-06-17
**Status:** Complete
**Tests:** 500/500 (280 unit · 14 arch · 206 integration)
**ADRs implemented:** 135..144

---

## What was built

Closed-request customer feedback — the resolution loop after a business marks a
request `Closed`. Customer can submit a one-time binary resolution check via the
public customer page.

---

## Files changed

| Layer | File | Action |
|---|---|---|
| Keep.Core | `Errors/KeepRequestErrors.cs` | Added 4 new error codes |
| Keep.Core | `Entities/KeepRequest.cs` | Added `SubmitFeedback` domain method |
| Keep.Application | `Requests/SubmitFeedbackCommand.cs` | New command record |
| Keep.Application | `Requests/SubmitFeedbackService.cs` | New service |
| Keep.Application | `Requests/IKeepCustomerWritePersistence.cs` | Added `CommitFeedbackAsync` |
| Keep.Application | `Requests/KeepCustomerPageMapper.cs` | Updated `ComputeAllowedActions` for Closed+feedback |
| Keep.Infrastructure | `Persistence/EfKeepCustomerWritePersistence.cs` | Implemented `CommitFeedbackAsync` |
| Api | `Keep/FeedbackRequest.cs` | New request body record |
| Api | `Helpers/ErrorHttpMapper.cs` | 5 new explicit cases (CustomerMessageTooLong + 4 feedback) |
| Api | `Program.cs` | Route, DI, `HandleFeedback` local function |
| IntegrationTests | `Api/ClosedRequestFeedbackTests.cs` | 13 tests per ADR-143 |
| IntegrationTests | `Api/CustomerMessageTests.cs` | Updated test 15 Closed assertion (ADR-139) |

---

## Key implementation decisions

### SubmitFeedback returns non-generic Result (ADR-137)

No timeline event is produced — feedback is stored as request-level fields only.
Non-generic `Result` is the right return shape; returning an event would be wrong.

### CommitFeedbackAsync has no event parameter

Separate from `CommitAsync(request, event, ct)`. `CommitFeedbackAsync(request, ct)`
calls `SaveChangesAsync` without adding an event — clean contract.

### Post-commit context rebuild (no second round-trip)

After `CommitFeedbackAsync`, the service rebuilds the `KeepPublicCustomerContext`
inline using C# record `with`:

```csharp
var updatedContext = context with
{
    FeedbackWasResolved = request.FeedbackWasResolved,
    FeedbackSubmittedAtUtc = request.FeedbackSubmittedAtUtc
};
```

Avoids a second guard evaluation. The tracked request is the authoritative source
after the domain method succeeds.

### ComputeAllowedActions takes feedbackAlreadySubmitted bool

`BuildActiveResult` passes `context.FeedbackSubmittedAtUtc.HasValue`. Rules:
- Active/Resolved statuses → message actions
- Closed + no feedback submitted → `["feedback"]`
- Closed + feedback submitted → `[]`
- Cancelled → `[]`
- Expired tombstone → `null` (BuildExpiredResult)

### wasResolved validation at the endpoint layer

`FeedbackBody.WasResolved` is `bool?` so the JSON deserializer does not silently
default a missing value to `false`. The `HandleFeedback` local function checks for
`null` and returns `FeedbackResolutionRequired` (400) before reaching the service.
The domain method receives a concrete `bool` — no nullable ambiguity in domain logic.

### No new migration needed

`feedback_was_resolved`, `feedback_comment`, `feedback_submitted_at_utc` columns
were added in the `Phase8KeepDataModel` migration (2026-06-16). The EF configuration
was already written ahead of the implementation session.

---

## Error contract

| Error | HTTP |
|---|---|
| `KeepRequest.FeedbackResolutionRequired` | 400 |
| `KeepRequest.FeedbackCommentTooLong` | 400 |
| `KeepRequest.FeedbackUnavailable` | 409 |
| `KeepRequest.FeedbackAlreadySubmitted` | 409 |

Guard errors (NotFound, expired 410) pass through unchanged — same as B3 messages.

---

## Integration test coverage (ADR-143)

1. Closed unexpired request returns `AllowedActions=["feedback"]` before feedback.
2. Feedback on closed request succeeds and returns updated customer page.
3. Positive feedback stores fields and does not create attention.
4. Negative feedback stores fields and creates priority `UnresolvedFeedback` attention.
5. Duplicate feedback returns `409 KeepRequest.FeedbackAlreadySubmitted`.
6. Feedback on `Received` returns `409 KeepRequest.FeedbackUnavailable`.
7. Feedback on `Resolved` returns `409 KeepRequest.FeedbackUnavailable`.
8. Feedback on `Cancelled` returns `409 KeepRequest.FeedbackUnavailable`.
9. Expired closed token returns `410` safe context.
10. Missing `wasResolved` returns `400 KeepRequest.FeedbackResolutionRequired`.
11. Comment over 2000 chars returns `400 KeepRequest.FeedbackCommentTooLong`.
12. Feedback response exposes no internal IDs or attention internals.
13. After feedback, `AllowedActions=[]`.

---

## Session-log correction

Session log incorrectly stated "Negative feedback requires a comment." ADR-135 is
authoritative: comment is optional even when `wasResolved=false`. UI may prompt
strongly but the server must not require it. Implementation follows ADR-135.

---

## Watch-outs

- **Negative feedback on Closed raises attention** — intentional exception to the
  terminal-no-attention posture (ADR-138). Business decides next action.
- **Status stays Closed** — negative feedback does not reopen. `TerminatedAtUtc` is
  not updated. Status change is a separate future flow if needed.
- **One-time submission enforced** — `FeedbackSubmittedAtUtc.HasValue` checked in
  domain method. Guard evaluation happens before the tracked reload, so a race
  between guard and write is handled by the domain guard in `SubmitFeedback`.

---

## Exit gate

- [x] All 500 tests pass (280 unit · 14 arch · 206 integration)
- [x] 13 new integration tests match ADR-143 matrix exactly
- [x] Architecture tests green (layer boundaries unchanged)
- [x] No new migration needed — columns already in Phase8KeepDataModel
- [x] Session log updated; decision index entries verified
