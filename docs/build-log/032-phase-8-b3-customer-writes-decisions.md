# Build Log 032 — Phase 8-B3 Customer Writes Decisions

**Phase:** 8-B3 discovery / pre-implementation gate
**Date:** 2026-06-16
**Status:** Decision gate complete. Ready for B3 implementation.
**ADRs locked:** 118..134

---

## Purpose

Run a customer-write discovery pass before implementation, including a fresh reference-app review,
so B3 avoids known public customer-token mistakes while keeping the pilot surface small and useful
for busy operators.

Reference areas reviewed:

- `_reference/src/OpHalo.Continuity.API/Endpoints/Public/CustomerPageEndpoints.cs`
- `_reference/src/OpHalo.Continuity.Application/ContinuityRequests/PublicCustomerAccessGuard.cs`
- `_reference/src/OpHalo.Continuity.Application/ContinuityRequests/Commands/Customer*`
- `_reference/src/OpHalo.Continuity.Core/Entities/ContinuityRequest.cs`
- `_reference/src/OpHalo.Continuity.Application/ContinuityRequests/ContinuityAttentionPolicy.cs`
- `_reference/tests/OpHalo.UnitTests/ContinuityRequests/PublicCustomerAccessGuardTests.cs`
- `_reference/tests/OpHalo.UnitTests/ContinuityRequests/CustomerTokenMutationHandlerTests.cs`

---

## Locked Decisions

### ADR-118 — Customer page links are bearer-access links

Customer request page links under `/keep/r/{pageToken}` are bearer-access links for the pilot.
Anyone with a valid, unexpired page token can view the customer-visible request page and submit
allowed customer actions.

Reason: customers often forward service links to spouses, tenants, property managers, coworkers,
or family members. Identity-bound access would add substantial friction before Keep has a customer
account/passcode model.

Guardrails:

- high-entropy unguessable tokens
- token-only server-side request/account resolution
- customer-safe public response fields only
- account/feature/expiry guard checks
- terminal-state write blocking
- anonymous-write rate limiting

Deferred: identity-bound access, passcodes, per-recipient access, and link revocation/rotation UI.

### ADR-119 — Centralized public customer access guard

All customer page reads and writes under `/keep/r/{pageToken}` pass through a centralized Keep
public customer access guard. The guard resolves request/account context from the token, enforces
account access and Keep feature gates, handles expiry consistently, and returns a safe projection.
Write services re-fetch the tracked request by the server-resolved request id only after guard
success.

Reason: the reference app previously had a class of bug where token validity could outlive
account/customer-page eligibility. Centralizing the guard prevents each endpoint from inventing
its own partial checks.

### ADR-120 — Expiry applies only after terminal lifecycle

Customer page expiry is enforced only when the request is terminal. Active and resolved requests
remain readable even if `ExpiresAtUtc` is populated. Closed/cancelled requests remain readable
until `ExpiresAtUtc`; after that, reads and writes return `410 Gone` with safe expired context.

Reason: the reference app had a bug where open requests could show an expired tombstone because
an old expiry timestamp passed.

### ADR-121 — Intent-specific customer reply routes, shared implementation

B3 normal customer reply routes:

```text
POST /keep/r/{pageToken}/message
POST /keep/r/{pageToken}/question
POST /keep/r/{pageToken}/update-request
POST /keep/r/{pageToken}/schedule-change-request
POST /keep/r/{pageToken}/change-or-cancel-request
POST /keep/r/{pageToken}/issue
```

Each route accepts `{ "message": "..." }`, maps to a fixed `MessageIntent`, and uses one shared
customer-reply service/domain path. Successful writes return the updated `KeepCustomerPageResult`.

Route mapping:

```text
/message                  -> GeneralMessage
/question                 -> Question
/update-request           -> UpdateRequest
/schedule-change-request  -> ScheduleChangeRequest
/change-or-cancel-request -> ChangeOrCancelRequest
/issue                    -> Complaint
```

Reason: route-level intent is clear and customer-friendly, while one implementation avoids the
reference app's repeated handler pattern. `/issue` is softer public language than `/complaint`
while preserving the internal `Complaint` intent.

### ADR-122 — Customer reply validation

Customer replies use one required plain-text `message` field. The message is trimmed before
validation/storage, must be non-blank, and must not exceed 4000 characters.

Errors:

- blank/missing message: `400 KeepRequest.MessageRequired`
- over-limit message: `400 KeepRequest.CustomerMessageTooLong`

Reason: the reference app's 500-character limit is too tight for real customer explanations,
access instructions, scheduling constraints, and issue reports. 4000 matches Keep's newer
communication surface.

### ADR-123 — Reply timeline shape and business contact card

Customer replies append `KeepRequestEvent` records:

```text
EventType = MessageAdded
Visibility = All
ActorType = Customer
ActorAccountUserId = null
ActorDisplayName = request.CustomerName
Content = trimmed message
MessageIntent = mapped route intent
CommunicationChannel = InApp
StatusAfter = null
```

Business quick-contact actions are request-level, not event-level. Operator/mobile clients use
the denormalized request contact card:

```text
CustomerName
CustomerPhone
CustomerEmail?
```

to render one-click call/email actions. Reply events do not duplicate phone/email.

Reason: a customer reply creates the need; the request contact card gives a busy operator the
fastest next move without copying or remembering a phone number.

### ADR-124 — Response targets are account policy with pilot defaults

Response timing is account policy, not a universal constant. `KeepResponsePolicy` owns:

```text
FirstResponseTargetMinutes
StandardResponseTargetMinutes
PriorityResponseTargetMinutes
BusinessHoursOnly?
```

B3 uses account policy when present and falls back to:

```text
FirstResponseTargetMinutes = 60
StandardResponseTargetMinutes = 240
PriorityResponseTargetMinutes = 60
```

`BusinessHoursOnly` remains stored, but B3 does not implement business-hours-aware attention math.

Reason: one-size SLAs are wrong across business types, but a full onboarding/account-settings
surface and business-hours engine would expand B3 too far. Defaults keep the pilot operational
while preserving the correct model.

### ADR-125 — Customer-submitted messages raise business-waiting attention

Customer-submitted messages from the customer page raise request-level business-waiting attention.
This includes normal customer routes:

```text
/message
/question
/update-request
/schedule-change-request
/change-or-cancel-request
/issue
```

On success:

```text
AttentionLevel = Waiting
WaitingDirection = Business
LastCustomerActivityAt = now
NextAttentionAtUtc = now + response-policy target
```

Intent mapping:

```text
/message                  -> CustomerMessage       -> Standard
/question                 -> CustomerMessage       -> Standard
/update-request           -> UpdateRequest         -> Standard
/schedule-change-request  -> ScheduleChangeRequest -> Priority
/change-or-cancel-request -> ChangeOrCancelRequest -> Priority
/issue                    -> Complaint             -> Priority
```

Timestamp rules:

- new business-waiting attention sets `AttentionSinceUtc = now`
- additional customer messages while already business-waiting preserve the oldest unresolved
  `AttentionSinceUtc`
- higher-priority messages may upgrade `AttentionReason` and `PriorityBand` without resetting
  `AttentionSinceUtc`
- a message while waiting on the customer flips `WaitingDirection` to `Business` and sets
  `AttentionSinceUtc = now`

Customer-submitted messages do not change request status and do not count as first business
response.

Reason: a customer-submitted message means the business owes the next move. Preserving the oldest
unresolved timestamp keeps queue ordering fair while allowing urgent customer signals to rise.

### ADR-126 — Customer page allowed actions are server-computed

`KeepCustomerPageResult.AllowedActions` is server-computed by request state.

For B3, these states expose normal customer message actions:

```text
Received
Scheduled
InProgress
PendingCustomer
Resolved
```

Actions:

```text
message
question
update_request
schedule_change_request
change_or_cancel_request
issue
```

`Closed` and `Cancelled` expose no B3 actions. Expired terminal pages return safe tombstone
context with `AllowedActions = null`.

Reason: active/resolved requests need low-friction customer communication. Closed-request feedback
is deferred to the next slice, so closed/cancelled are read-only in B3. Server validation remains
authoritative; `AllowedActions` is a UI affordance.

### ADR-127 — Terminal customer write behavior

`Resolved` accepts normal customer-submitted messages. `Closed` and `Cancelled` block normal
customer-submitted messages with `409 KeepRequest.TerminalState`. Expired terminal tokens return
`410 Gone` with safe expired context. Unknown tokens return `404`.

Customer writes never reopen a request automatically. `ChangeOrCancelRequest` records customer
intent only and does not transition status to `Cancelled`.

Reason: lifecycle control stays with the business and avoids the reference app's ambiguous
customer terminal action path.

### ADR-128 — Closed-request feedback behavior is locked but deferred

Closed-request feedback is locked as core future behavior but is not implemented in B3.

Future route:

```text
POST /keep/r/{pageToken}/feedback
```

Future behavior:

- allowed only on unexpired `Closed` requests
- not allowed on active/resolved/cancelled/expired requests
- one submission only
- `wasResolved` required
- optional plain-text `comment`, max 2000 characters
- stores `FeedbackWasResolved`, `FeedbackComment`, `FeedbackSubmittedAtUtc`
- positive feedback creates no attention
- negative feedback raises priority `UnresolvedFeedback` business attention
- feedback does not reopen automatically
- no feedback timeline event type is required for the first feedback slice unless that later
  implementation decision changes

Reason: feedback answers "did we actually resolve it?" but is a different post-close lifecycle
surface from normal customer messaging.

### ADR-129 — Anonymous customer writes use dedicated rate limiting

Add a dedicated `customer-write` rate limit policy for anonymous customer writes. Apply it to all
normal customer message routes and the future feedback route.

Partition:

```text
client IP + pageToken
```

Limit:

```text
10 writes / 1 minute
QueueLimit = 0
```

Keep the existing `Testing` environment rate-limiter bypass.

Reason: customer page links are bearer-access links, so writes need basic spam/brute-force
friction. Composite IP+token avoids over-penalizing shared networks while still limiting a single
actor hammering one request.

### ADR-130 — B3 customer-write error contract

Public guard/access:

```text
Unknown/blank page token                   -> 404 KeepRequest.NotFound
Account blocked / feature unavailable      -> 404 KeepRequest.NotFound
Expired terminal page token                -> 410 safe expired-page context
```

Validation:

```text
Missing/blank customer message             -> 400 KeepRequest.MessageRequired
Customer message over 4000 chars           -> 400 KeepRequest.CustomerMessageTooLong
```

State conflicts:

```text
Normal message on Closed/Cancelled         -> 409 KeepRequest.TerminalState
```

Rate limiting:

```text
Too many anonymous writes                  -> 429
```

Feedback-specific errors are deferred with the feedback slice.

Reason: token/account eligibility failures must not leak whether a real request/account exists.
Validation errors are `400`; lifecycle conflicts are `409`; expired terminal links keep the
special `410` tombstone contract.

### ADR-131 — B3 integration test matrix

B3 completion requires focused integration coverage for customer-submitted messages:

1. Unknown token returns `404`.
2. Blocked/unavailable account via guard returns `404` and no mutation.
3. Expired terminal token returns `410` safe context.
4. Missing/blank message returns `400 KeepRequest.MessageRequired`.
5. Over-limit message returns `400 KeepRequest.CustomerMessageTooLong`.
6. Happy path `/message` returns updated customer page with new customer timeline event.
7. `/issue` creates priority `Complaint` attention.
8. Repeated standard message preserves oldest `AttentionSinceUtc`.
9. Later priority message upgrades reason/priority without resetting `AttentionSinceUtc`.
10. Message while `WaitingDirection=Customer` flips direction and resets since to now.
11. `Resolved` request accepts normal message.
12. `Closed` normal message returns `409`.
13. `Cancelled` normal message returns `409`.
14. Customer page response exposes no internal IDs or attention internals.
15. `AllowedActions` matches active/resolved/closed/cancelled/expired state.
16. Scheduled customer page maps `scheduled` correctly.

Reason: these tests cover public guard behavior, anonymous writes, customer-safe response shape,
attention correctness, terminal rules, and the scheduled-status mapper watch-out.

### ADR-132 — B3 implementation scope

Phase 8-B3 implementation scope is customer-submitted messages only:

- public customer access guard
- expiry semantics
- `AllowedActions`
- scheduled status mapper fix
- customer message domain/service/persistence/API routes
- attention update rules
- error mappings
- `customer-write` rate limit policy
- integration tests

Closed-request feedback moves to the next customer-write follow-up slice.

Reason: customer-submitted messages are the core communication loop. Feedback has separate
post-close lifecycle rules and deserves its own focused implementation.

### ADR-133 — Final reference-app lessons

Adopt:

- centralized public customer-token guard
- guard-before-mutation/no-side-effects-on-denied-access
- tracked re-fetch by server-resolved request id
- customer message clears/flips waiting-on-customer
- anonymous customer route rate limiting
- terminal-only expiry
- no internal data exposure on customer page

Modify:

- intent-specific routes but one shared implementation
- `/issue` instead of public `/complaint`
- 4000-character customer messages instead of 500
- direct `KeepRequest` attention fields instead of the reference signal projection engine
- return updated customer page instead of `204`

Do not port:

- customer direct no-longer-needed terminal action
- contact preferences/opt-out in B3
- notification/email delivery in B3
- attachments/photos in B3
- page-opened beacon in B3
- full signal/projection attention engine
- customer direct status/cancel/close/reopen

Reason: this prevents accidental reference-app cargo-culting and keeps B3 aligned with the
greenfield Keep posture.

### ADR-134 — Feedback is core Keep follow-up, not B3

Closed-request feedback is deferred out of B3 but remains core Keep product direction, not an
add-on. Basic "was this resolved?" feedback is part of closing the Keep communication loop.

Advanced feedback analytics, CSAT/NPS/review generation, public review prompts, dashboards, and
automated follow-up campaigns are deferred separately and may be packaged differently later.

---

## Deferred Items Recorded

- DEF-023 — identity-bound customer page access and link management
- DEF-024 — Keep response policy onboarding/account settings UI
- DEF-025 — business-hours and quiet-hours SLA calculation
- DEF-026 — closed-request customer feedback implementation
- DEF-027 — advanced feedback/review analytics

Already deferred and still out of B3:

- DEF-018 — external contact capture workflows
- DEF-019 — customer contact preferences and email opt-out routes
- DEF-020 — full Keep signal/projection attention engine
- DEF-021 — native mobile push notifications and badge counts
- DEF-022 — SMS notification sending
- DEF-013 — attachments/file upload

---

## Next Decision

No further B3 decisions are required before implementation.

Next implementation slice:

```text
Phase 8-B3 — Customer-Submitted Messages
```

First implementation watch-out: `GetKeepCustomerPageService.MapStatus` currently lacks
`KeepRequestStatus.Scheduled`; fix this in B3 before/with `AllowedActions`.
