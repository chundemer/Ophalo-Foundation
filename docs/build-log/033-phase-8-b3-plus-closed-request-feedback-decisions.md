# Build Log 033 — Phase 8-B3+: Closed-Request Feedback Decisions

**Phase:** 8-B3+ discovery / pre-implementation gate
**Date:** 2026-06-16
**Status:** Decision gate complete. Ready after B3 customer-submitted messages.
**ADRs locked:** 135..144

---

## Purpose

Define the closed-request feedback follow-up slice before implementation. B3 itself remains scoped
to customer-submitted messages. This B3+ slice closes the loop after a business marks a request
`Closed`: the customer can confirm whether the request was actually resolved.

Key product posture:

- Basic resolution feedback is core Keep functionality, not an add-on.
- Feedback is not a review/rating/CSAT system.
- Negative feedback is operational: it raises business attention without reopening automatically.

---

## Locked Decisions

### ADR-135 — Basic feedback is core Keep resolution feedback

Basic closed-request feedback is a binary resolution check:

```text
Was this resolved?
- Yes, it's resolved
- No, I still need help
```

Payload:

```json
{
  "wasResolved": false,
  "comment": "The leak is still happening under the sink."
}
```

Rules:

- `wasResolved` is required.
- `comment` is optional.
- `comment` max length is 2000 characters.
- No separate `needs improvement` flag is introduced.
- UI should strongly prompt for a comment when `wasResolved=false`, but the server should not
  require one.

Reason: Keep needs to answer the operational question "is this request actually resolved?"
without becoming a review/rating product. `wasResolved=false` is the actionable signal; the
comment is helpful context when the customer provides it.

Deferred:

- star ratings
- "needs improvement" categories
- CSAT/NPS
- public reviews/testimonials
- analytics dashboards
- automated feedback campaigns

### ADR-136 — Feedback endpoint and eligibility

Endpoint:

```text
POST /keep/r/{pageToken}/feedback
```

Eligibility:

- allowed only when request status is `Closed`
- page token must pass the public customer access guard
- closed page must be unexpired
- feedback must not already be submitted
- not allowed on `Received`, `Scheduled`, `InProgress`, `PendingCustomer`, `Resolved`, or
  `Cancelled`

Reason:

- `Closed` means the business says the work/request is done, so resolution feedback makes sense.
- `Resolved` is still pre-close and should use normal customer message actions.
- `Cancelled` means not proceeding, not resolved, so feedback would be ambiguous.
- one-time submission avoids customers repeatedly changing feedback state.

### ADR-137 — Feedback stores request-level fields, not timeline events

Feedback stores existing `KeepRequest` fields:

```text
FeedbackWasResolved
FeedbackComment
FeedbackSubmittedAtUtc
```

Rules:

- trim comment before storage
- blank/missing comment stores `null`
- set `FeedbackSubmittedAtUtc = now`
- do not append `KeepRequestEvent`
- do not add a feedback event type in the first feedback slice
- feedback is one-time and not editable
- operator/customer pages may render feedback state from request fields

Reason: feedback is summary state on a closed request, not normal conversation. Existing fields
are sufficient for the first slice, and one-time/no-edit semantics avoid the need for append-only
feedback history right now.

### ADR-138 — Negative feedback raises priority business attention without reopening

If feedback is submitted with:

```text
wasResolved = false
```

set:

```text
AttentionLevel = Waiting
WaitingDirection = Business
AttentionReason = UnresolvedFeedback
PriorityBand = Priority
AttentionSinceUtc = now
NextAttentionAtUtc = now + priority response target
```

Use account response policy, falling back to the pilot priority default from ADR-124.

Rules:

- do not reopen automatically
- do not change status from `Closed`
- do not update `TerminatedAtUtc`
- this is an intentional exception to the usual terminal-no-attention posture
- business decides whether to contact the customer, create a new request, or use a future reopen
  flow

If `wasResolved=true`, store feedback only. Do not create attention. Do not mutate any existing
legacy attention; cleanup remains a separate operator/system concern.

Reason: unresolved feedback is a customer signal after closure. It should surface operationally
without silently reopening or changing lifecycle.

### ADR-139 — Feedback returns updated customer page and updates allowed actions

Successful feedback submission returns the updated `KeepCustomerPageResult`.

Before feedback is submitted on an eligible unexpired `Closed` page:

```text
AllowedActions = ["feedback"]
```

After feedback is submitted:

```text
AllowedActions = []
```

Other states:

```text
Cancelled             -> []
Expired terminal page -> null
```

Response fields populated after success:

```text
FeedbackWasResolved
FeedbackSubmittedAtUtc
```

Reason: returning updated page avoids a follow-up GET and immediately removes the one-time feedback
action from the UI.

### ADR-140 — Feedback error contract

Access/guard:

```text
Unknown/blank token               -> 404 KeepRequest.NotFound
Account blocked / unavailable     -> 404 KeepRequest.NotFound
Expired terminal token            -> 410 safe expired-page context
```

Validation:

```text
wasResolved missing / invalid     -> 400 KeepRequest.FeedbackResolutionRequired
comment over 2000 chars           -> 400 KeepRequest.FeedbackCommentTooLong
```

State conflicts:

```text
Feedback on non-Closed request    -> 409 KeepRequest.FeedbackUnavailable
Feedback on Cancelled request     -> 409 KeepRequest.FeedbackUnavailable
Duplicate feedback                -> 409 KeepRequest.FeedbackAlreadySubmitted
```

Rate limit:

```text
Too many anonymous writes         -> 429
```

Reason:

- public access failures should not leak account/request state
- validation errors are client-correctable malformed payloads
- wrong lifecycle or duplicate submission is a valid request in the wrong state, so `409`

### ADR-141 — Feedback uses customer-write rate limit

`POST /keep/r/{pageToken}/feedback` uses the same `customer-write` rate limit policy as B3
customer messages.

Partition:

```text
client IP + pageToken
```

Limit:

```text
10 writes / 1 minute
QueueLimit = 0
```

Rules:

- duplicate feedback returns `409`, not silent success
- successful feedback remains one-time
- existing `Testing` environment rate-limiter bypass remains

Reason: feedback is an anonymous bearer-link write. Even though successful feedback is one-time,
invalid/duplicate/unknown-token attempts can still be spammed and should share the same abuse
posture.

### ADR-142 — Feedback implementation split and timing

Closed-request feedback is the next slice after B3 customer-submitted messages:

```text
Phase 8-B3+ — Closed-Request Feedback
```

Scope:

- feedback domain method
- feedback request body
- feedback service/persistence route
- error codes and HTTP mappings
- `AllowedActions` adds `feedback` for eligible closed requests
- negative feedback attention
- integration tests
- build/session docs after implementation

Out of scope:

- feedback timeline event
- feedback editing/deletion
- star ratings/CSAT/NPS
- public reviews/testimonials
- feedback analytics dashboards
- automated follow-up campaigns
- customer reopen

Reason: feedback is close enough to B3 that it should happen immediately after customer-submitted
messages, but separate enough that it should not interrupt the core customer-message implementation.

### ADR-143 — Feedback integration test matrix

Required integration coverage:

1. Closed unexpired request returns `AllowedActions=["feedback"]` before feedback.
2. Feedback on closed request succeeds and returns updated customer page.
3. Positive feedback stores fields and does not create attention.
4. Negative feedback stores fields and creates priority `UnresolvedFeedback` attention.
5. Duplicate feedback returns `409 KeepRequest.FeedbackAlreadySubmitted`.
6. Feedback on `Received` returns `409 KeepRequest.FeedbackUnavailable`.
7. Feedback on `Resolved` returns `409 KeepRequest.FeedbackUnavailable`.
8. Feedback on `Cancelled` returns `409 KeepRequest.FeedbackUnavailable`.
9. Expired closed token returns `410` safe context.
10. Missing/invalid `wasResolved` returns `400 KeepRequest.FeedbackResolutionRequired`.
11. Comment over 2000 chars returns `400 KeepRequest.FeedbackCommentTooLong`.
12. Feedback response exposes no internal IDs or attention internals on the customer page.
13. After feedback, `AllowedActions=[]`.

Reason: covers eligibility, one-time behavior, attention side effects, error contract, expiry, and
customer-safe response shape.

### ADR-144 — Feedback reference-app lessons

Use the reference app as a warning and guardrail source, not as a direct behavior source.

Adopt:

- public guard before mutation
- no side effects on denied access
- token-only request/account resolution
- rate limiting
- terminal-only expiry
- customer-safe public response

Do not port:

- `no-longer-needed` terminal customer action
- customer email opt-out behavior
- contact preference mutation
- page-opened beacon
- direct customer status/lifecycle mutation
- event-heavy feedback/history model unless later needed

Modify:

- feedback is not customer cancellation/no-longer-needed
- negative feedback raises attention but does not reopen
- feedback is request-level fields, not timeline event
- one-time feedback only

Reason: reference customer-token routes had useful guardrails, but the actual terminal/customer
action behaviors are not the product shape we want for Keep.

---

## Next

No further B3+ feedback decisions are required before implementation.

Implement after:

```text
Phase 8-B3 — Customer-Submitted Messages
```
