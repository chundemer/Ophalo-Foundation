# Phase 8 — Keep Communication Loop Reference Audit + Discovery

**Status:** Complete. All D1–D10 decisions locked (ADR-084..093). B1-α implementation decisions locked in ADR-094..098.
**Build-log preceding this:** 023-phase-5e-c-api-integration-tests.md
**Date:** 2026-06-16
**Current V1 lock:** Notification timing and realtime/list-streaming assumptions from this discovery log
are superseded by `043-keep-v1-product-scope-and-freshness-lock.md` and ADR-288..292. Basic
push/badges are V1 pre-go-live; SSE/WebSockets/list streaming are post-v1 unless pilot usage proves
the freshness matrix insufficient.

---

## Purpose

Phase 8 should not start from a blank page. The reference app already contains customer-page,
timeline, messaging, close/no-longer-needed, first-response, and attention behavior under the
Continuity name. This discovery pass audits that behavior and locks what the new Keep-shaped
Foundation should keep, modify, remove, or defer.

Reference areas inspected so far:

- `_reference/src/OpHalo.Continuity.API/Endpoints/Public/CustomerPageEndpoints.cs`
- `_reference/src/OpHalo.Continuity.API/Endpoints/Operator/ContinuityRequestEndpoints.cs`
- `_reference/src/OpHalo.Continuity.Application/ContinuityRequests/PublicCustomerAccessGuard.cs`
- `_reference/docs/reference/new-request-customer/customer-intake-decisions.md`
- current Keep entities/services under `src/OpHalo.Keep.*`

---

## Decisions Locked

### D1 — Customer Request Page Access

**Decision:** Phase 8 customer request pages use anonymous, request-scoped
`KeepRequest.PageToken` access under:

```text
/keep/r/{pageToken}
```

Customer actions use the same token-only route family:

```text
GET  /keep/r/{pageToken}
POST /keep/r/{pageToken}/message
POST /keep/r/{pageToken}/question
POST /keep/r/{pageToken}/change-or-cancel-request
```

`pageToken` is the only access key. The API never accepts client-supplied `AccountId`.
The page response must include `businessName` as a prominent trust signal for the customer.

**Recommendation accepted:** Do not add public/account slugs inside Phase 8. The reference app
has `PublicSlug`, but its latest decisions treat opaque tokens as the security primitive and
slugs as branding only. The new project does not yet have public slug/account setup, so branded
customer URLs are deferred rather than making Phase 8 depend on a missing account-identity phase.

**Deferred:** Branded customer URLs such as `/keep/r/{publicSlug}/{pageToken}`. When added,
the token must remain the security primitive; slug mismatch should return 404; slug alone must
never open a request.

**ADR:** ADR-084.

### D2 — Phase 8 Route Shape

**Decision:** Phase 8 uses Keep-named routes, split by access boundary:

Authenticated operator routes derive `AccountId` from the trusted session:

```text
GET  /keep/requests
GET  /keep/requests/{requestId}
POST /keep/requests/{requestId}/update
POST /keep/requests/{requestId}/close
POST /keep/requests/{requestId}/acknowledge
```

Anonymous customer routes derive account/request from `KeepRequest.PageToken`:

```text
GET  /keep/r/{pageToken}
POST /keep/r/{pageToken}/message
POST /keep/r/{pageToken}/question
POST /keep/r/{pageToken}/change-or-cancel-request
```

**Reference mapping:** Preserve the core behavior from reference Continuity routes, but rename
the route family to Keep and keep operator/customer boundaries explicit.

**Deferred from Phase 8 core route set:**

```text
GET  /keep/requests/stream
POST /keep/requests/{requestId}/external-contact
POST /keep/requests/{requestId}/revert-external-contact
POST /keep/r/{pageToken}/contact-preferences
POST /keep/r/{pageToken}/opt-out-email
```

SSE/list streaming is real-time polish. External contact is useful but not required for the
communication-loop foundation. Contact preferences and email opt-out belong closer to the
notification-preferences work in Phase 9.

**ADR:** ADR-085.

### D3 — Timeline/Event Model

**Decision:** The Keep timeline is persisted as append-only `KeepRequestEvent` records.
`KeepRequest` stores current state fields for fast reads; `KeepRequestEvent` stores historical
facts for customer timeline, operator detail, and audit.

Event type describes **what happened**. Actor fields describe **who did it**.

Target event shape:

```text
KeepRequestEvent
- RequestId
- AccountId
- EventType
- Visibility
- Content
- ActorType
- ActorAccountUserId?
- ActorDisplayName?
- OccurredAtUtc
```

Target event type vocabulary should stay action-oriented, not actor-oriented:

```text
RequestCreated
MessageAdded
StatusChanged
RequestClosed
RequestCancelled
InternalNoteAdded
```

Examples:

- `MessageAdded + ActorType=Customer` → customer message/question/update request.
- `MessageAdded + ActorType=AccountUser` → business/operator reply.
- `RequestClosed + ActorType=AccountUser` → named account user closed the request.

Visibility rules:

- `All` — visible to customer and operator.
- `Internal` — visible to operator only.
- `System` — lifecycle/audit event; operator-visible where useful, never customer-visible.

**Rationale:** This preserves the reference app's timeline/audit behavior while avoiding event
names that overfit to one role. The customer-facing UI can say who did what using actor snapshots
instead of inferring author from event type.

**ADR:** ADR-086.

### D4 — Lightweight Request Participation For Filtering And Notification Routing

**Decision:** Keep needs a lightweight participation concept so businesses are not forced into
an all-open-requests firehose. This is not a heavyweight request tracker; it is a noise-control
and routing model.

Recommended model:

```text
KeepRequestParticipant
- RequestId
- AccountId
- AccountUserId
- ParticipationType: Responsible | Watching
- NotificationsEnabled
- AttachedAtUtc
- DetachedAtUtc
```

Product meaning:

- `Responsible` — "I am handling this request."
- `Watching` — "Keep me in the loop."
- `NotificationsEnabled = false` — user can still see the request through their allowed view,
  but normal notifications do not target them.

Initial visibility policy:

- Owner/Admin can see all account requests.
- Operator sees requests they are responsible for or watching.
- Viewer visibility remains to be confirmed before implementation; likely read-only and either
  all account requests or attached-only depending on noise/privacy posture.
- New/unassigned public intake requests are visible to Owner/Admin so they can be routed.

Initial notification-routing policy:

- Customer replies/messages notify Responsible + Watching participants with notifications enabled.
- If no Responsible participant exists, notification routing falls back to an account-level inbox
  or Owner/Admin path; exact notification mechanics belong to Phase 9.
- Quiet hours, user notification preferences, and deeper watch/mute behavior remain Phase 9.

**Rationale:** This matches how small service businesses actually operate: people need to see and
be notified about the work they are handling, without turning Keep into a full project-management
or ticketing system.

**ADR:** ADR-087.

### D5 — Message/Update Model And Customer Action Intents

**Decision:** Phase 8 keeps separate customer-facing action intents for product clarity, but
persists customer/business communication through one shared timeline shape:

```text
KeepRequestEvent
- EventType = MessageAdded
- MessageIntent
- ActorType
- Content
- Visibility = All
```

Initial pilot customer actions:

```text
Send a message
Ask a question
Request an update
Request a time change
Change or cancel request
Report an issue
```

Initial message intent vocabulary:

```text
GeneralMessage
Question
UpdateRequest
ScheduleChangeRequest
ChangeOrCancelRequest
Complaint
BusinessUpdate
```

Customer routes should stay intent-specific where the UI benefits, but route names should avoid
ambiguous overlap with operator/business actions. Example:

```text
POST /keep/r/{pageToken}/message
POST /keep/r/{pageToken}/question
POST /keep/r/{pageToken}/update-request
POST /keep/r/{pageToken}/schedule-change-request
POST /keep/r/{pageToken}/change-or-cancel-request
POST /keep/r/{pageToken}/complaint

POST /keep/requests/{requestId}/update
```

For pilot, customer actions do **not** automatically transition request status. Even
`ChangeOrCancelRequest` records customer intent and creates attention for the business; the
business/operator decides the actual status transition. This intentionally differs from the
reference app's `no-longer-needed` action that can behave as a terminal customer action.

**Rationale:** This gives pilot businesses useful signal without making the customer page feel
like a complex request tracker. The intent list is broad enough to cover rescheduling, scope
changes, special instructions, ETA/status questions, complaints, pricing/policy questions, and
receipt/document requests without exposing a long menu of edge-case buttons.

**ADR:** ADR-088.

### D6 — Request Status Transitions, Terminal Pages, Feedback, And Expiry

**Decision:** Phase 8 keeps the existing `KeepRequestStatus` values:

```text
Received
InProgress
PendingCustomer
Resolved
Closed
Cancelled
```

Only authenticated business users change request status in pilot. Customer actions create
timeline activity and attention, but never directly transition request status.

Operator routes:

```text
POST /keep/requests/{requestId}/status
POST /keep/requests/{requestId}/close
POST /keep/requests/{requestId}/cancel
```

`/status` handles non-terminal transitions such as `Received` → `InProgress`,
`InProgress` → `PendingCustomer`, and `PendingCustomer` → `Resolved`.

`/close` and `/cancel` are explicit terminal commands. They may accept an optional
customer-visible `finalMessage`; when present, it is persisted as
`MessageAdded + MessageIntent=BusinessUpdate` before or with the terminal event.

Terminal rules:

- `Closed` means completed/done.
- `Cancelled` means the request is no longer proceeding.
- `Closed` and `Cancelled` are terminal in Phase 8.
- Reopen is not included in Phase 8; new work after terminal state should start a new request.

Customer page behavior:

- Active requests: customer can view and submit allowed customer actions.
- Closed/cancelled requests while link is still valid: customer can view the final timeline,
  but normal customer messages/intents are blocked.
- Closed requests may accept one lightweight resolution feedback submission while the link is
  still valid.
- Feedback does not reopen the request automatically. If feedback indicates the issue was not
  resolved, it records a timeline/audit item and may create attention for the business.

Feedback shape for pilot:

```text
WasResolved: true | false
Comment?
```

This is intentionally not a full review/CSAT system. It answers the pilot-business question:
"Did this request actually get resolved?"

Link expiry behavior:

- Customer request links remain readable for active requests.
- After terminal state, the page remains readable through `KeepRequest.ExpiresAtUtc`.
- After expiry, customer page reads return `410 Gone` with safe expired-page context:

```json
{
  "businessName": "Smith Plumbing",
  "referenceCode": "PQRS7842",
  "expired": true,
  "newRequestUrl": null
}
```

When public intake link setup exists, `newRequestUrl` may be populated. The API should not
automatically redirect; the frontend should show a tombstone page explaining that the request
link expired and offer a new-request action when available.

**Rationale:** This keeps lifecycle control with the business, avoids accidental customer-driven
cancellations, gives customers a clear post-close experience, and captures useful pilot feedback
without turning Keep into a review platform or request tracker.

**ADR:** ADR-089.

### D7 — First Response And Business Communication Reality

**Decision:** Keep first-response policy must model how service businesses actually communicate,
not assume every response happens inside Keep.

Principles:

- First response means the customer heard from the business or the business made a real
  customer-contact attempt.
- Internal operations are not customer communication.
- Mobile/native capture is strategically important because much communication happens outside
  Keep: phone calls, SMS/iMessage, email, in-person conversations, voicemail, and field visits.

Response SLA:

- First-response target is account/business-configurable, not hard-coded.
- Recommended policy model:

```text
KeepResponsePolicy
- AccountId
- FirstResponseTargetMinutes
- BusinessHoursOnly?
```

- Phase 8 may use a default when no policy exists, but attention/first-response logic must read
  from account policy rather than embedding one universal SLA.

Request origin:

```text
KeepRequestOrigin
- Customer
- Business
```

Rules:

- `Origin=Customer`: first-response timer starts at request creation. The customer's submission
  does not count as first response.
- `Origin=Business`: creating the request does not automatically mean the customer is waiting.
  First-response timer starts only after customer inbound activity or when the business creates
  a customer-visible request/update that expects customer awareness.

What counts as first response:

- In-Keep customer-visible business update.
- Close/cancel final customer-visible message.
- Logged external customer contact, when explicitly marked as customer contact.

What does not count:

- Internal notes.
- Assignment/participation changes.
- Watching/muting.
- Acknowledging or clearing attention.
- Status changes without customer-visible text/message or logged customer contact.
- System events.
- Customer-originated messages.

Data to track:

```text
KeepRequest.FirstResponseDueAtUtc?
KeepRequest.FirstRespondedAtUtc?
KeepRequest.FirstResponderAccountUserId?
KeepRequest.FirstResponseEventId?
```

External communication capture:

- The Phase 8 model must not block later mobile-native capture of phone/SMS/email/in-person
  customer contact.
- Full external-contact capture routes/workflows may be a later slice, but the event model should
  be ready for a communication channel/source such as:

```text
CommunicationChannel
- InApp
- Phone
- Sms
- Email
- InPerson
- Other
```

External contact should count as first response only when logged as real customer contact. Pure
internal notes, even if created from mobile, do not count.

**Rationale:** The reference app had useful first-response machinery, but it overfit to an
in-app workflow. Pilot businesses need Keep to reflect how they actually work: customer care
happens through multiple channels, with different expectations by business type.

**ADR:** ADR-090.

### D8 — Attention State And Queue Movement

**Decision:** Keep uses a simple persisted current attention state on `KeepRequest` for pilot,
with append-only events for history/audit. Do not port the reference app's full signal/projection
engine in Phase 8.

Current-state fields:

```text
AttentionLevel: None | Waiting | NeedsAttention | Overdue
WaitingDirection: None | Business | Customer
AttentionReason: CustomerMessage | UpdateRequest | ScheduleChangeRequest | ChangeOrCancelRequest | Complaint | FirstResponseDue | UnresolvedFeedback
PriorityBand: Standard | Priority
AttentionSinceUtc?
NextAttentionAtUtc?
AttentionClearedAtUtc?
AttentionClearedByAccountUserId?
AttentionClearReason?
```

Policy fields:

```text
KeepResponsePolicy
- FirstResponseTargetMinutes
- StandardResponseTargetMinutes
- PriorityResponseTargetMinutes
- BusinessHoursOnly?
```

Intent-to-priority mapping for pilot:

- Standard: `GeneralMessage`, `Question`, `UpdateRequest`
- Priority: `ScheduleChangeRequest`, `ChangeOrCancelRequest`, `Complaint`, `UnresolvedFeedback`

Queue movement:

- Customer communication that needs business response sets `WaitingDirection=Business`.
- Waiting requests move up the list as `AttentionSinceUtc` ages.
- The list should sort deterministically rather than by mystery score:
  1. `Overdue`
  2. `NeedsAttention`
  3. `Waiting`
  4. `None`
  5. within attention groups: `Priority` before `Standard`
  6. within matching severity/priority: oldest unresolved `AttentionSinceUtc` first

Threshold behavior:

- New customer communication creates `AttentionLevel=Waiting`.
- When `NextAttentionAtUtc` is reached, attention becomes `NeedsAttention`.
- When the relevant SLA/target is breached, attention becomes `Overdue`.

Clock behavior:

- If a request is already waiting on the business, additional customer messages do **not** reset
  `AttentionSinceUtc`; the oldest unresolved customer-waiting timestamp wins.
- A later, higher-priority intent can upgrade `AttentionReason`/`PriorityBand` without resetting
  `AttentionSinceUtc`.

Direction rules:

- `WaitingDirection=Business` means the business owes customer-facing action.
- `WaitingDirection=Customer` means the business is waiting on the customer, such as after setting
  `PendingCustomer` with a customer-visible status text. This should not continue moving up the
  business needs-attention queue.
- `WaitingDirection=None` means no current waiting posture.

Clearing/changing attention:

- Business customer-visible update/message may clear `WaitingDirection=Business`.
- Logged external customer contact may clear attention when marked as real customer contact.
- Setting `PendingCustomer` with customer-visible text moves waiting direction to customer.
- Closing/cancelling clears normal active-request attention, except unresolved feedback can create
  separate post-close attention.
- Internal notes, assignment/participation changes, watching/muting, and status changes with no
  customer-visible communication do not clear attention.

Acknowledgement:

```text
POST /keep/requests/{requestId}/acknowledge
```

Acknowledgement is an internal clear action. It:

- clears current business-waiting attention;
- does not change request status;
- does not count as first response unless paired with a separately logged customer contact event;
- stores current-state audit fields (`AttentionClearedAtUtc`, `AttentionClearedByAccountUserId`,
  optional `AttentionClearReason`);
- appends an internal timeline/audit event:

```text
EventType = AttentionAcknowledged
Visibility = Internal
ActorType = AccountUser
ActorAccountUserId = ...
ActorDisplayName = ...
Content = optional note/reason
OccurredAtUtc = ...
```

**Rationale:** Attention is not just a flag. Pilot businesses need request movement: the customer
waiting longest should rise rather than get buried. Direction prevents "waiting on customer" from
polluting the business needs-attention queue. Acknowledgement audit answers who cleared attention
and why.

**ADR:** ADR-091.

### D9 — Notifications Boundary

**Decision:** Phase 8 prepares notification-routing state, but does not implement notification
delivery. This is Option B from the discovery discussion: notification-ready state only.

Phase 8 persists routing inputs:

```text
KeepRequestParticipant
- ParticipationType: Responsible | Watching
- NotificationsEnabled

KeepRequest
- AttentionLevel
- AttentionReason
- PriorityBand
- WaitingDirection
- FirstResponseDueAtUtc?
- FirstRespondedAtUtc?

KeepRequestEvent
- MessageIntent
- ActorType
- ActorAccountUserId?
- CommunicationChannel
```

Phase 8 documents routing rules:

- Customer activity routes to Responsible + Watching participants with notifications enabled.
- If no Responsible participant exists, routing falls back to Owner/Admin or an account-level inbox.
- Priority intents (`ScheduleChangeRequest`, `ChangeOrCancelRequest`, `Complaint`,
  `UnresolvedFeedback`) route as priority.
- First-response overdue routes to responsible participants or Owner/Admin fallback.
- Unresolved post-close feedback routes to Responsible + Owner/Admin fallback.
- Actor exclusion applies in the eventual notification phase: the actor who caused the event should
  not receive a notification for their own action.

Phase 8 does **not** implement:

- outbound email/push/SMS delivery;
- notification delivery rows;
- quiet hours;
- user-level notification preferences;
- device tokens / APNs / FCM registration;
- mobile badge count calculation;
- notification retries, outbox, dead-letter, or work-engine integration;
- full watch/mute behavior beyond participant `NotificationsEnabled`.

Mobile/push posture:

- No outbound SMS sending in Keep/OpHalo for pilot; Twilio-style SMS is avoided due per-message
  cost, carrier/compliance overhead, and pilot cost control.
- Phase 9 should target native push notifications for mobile via APNs/FCM, plus badge counts and
  in-app needs-attention views.
- The mobile app should not rely on running continuously in the background to poll. Server-side
  notification delivery should wake/display through APNs/FCM; the app refreshes request state from
  the API when opened or after a push.
- Push payloads should be minimal and avoid sensitive customer details where possible.

**Rationale:** Phase 8 is the communication-loop and attention foundation. Notification delivery
requires preferences, quiet hours, actor exclusion, delivery reliability, mobile device tokens,
and later work-engine behavior. Capturing routing-ready state now avoids rework while keeping
Phase 8 bounded.

**ADR:** ADR-092.

### D10 — Phase 8 Implementation Slice Order

**Decision:** Phase 8 must be implemented in bounded slices. Do not hand off "build Phase 8"
as one coding task.

#### Phase 8-B1 — Data Model + Read Surfaces

First coding slice for Claude.

Scope:

- extend Keep domain model for the locked Phase 8 read foundation:
  - `KeepRequestEvent` actor fields;
  - `MessageIntent`;
  - `CommunicationChannel`;
  - `KeepRequestOrigin`;
  - first-response fields;
  - attention fields;
  - terminal feedback fields if needed for read contracts;
- add `KeepResponsePolicy`;
- add `KeepRequestParticipant`;
- update EF configurations and create migration;
- add operator request detail read:

```text
GET /keep/requests/{requestId}
```

- add customer page read:

```text
GET /keep/r/{pageToken}
```

- add expired link `410 Gone` read behavior with safe context;
- add integration tests for read contracts and access rules;
- update build log/session log with final build/test state.

Out of scope for B1:

- customer message/action POST routes;
- operator status/update/close/cancel/acknowledge POST routes;
- participant attach/detach endpoints;
- request-list participation filtering changes;
- notification delivery or notification rows;
- external contact capture workflows;
- public slug/branded URLs;
- SSE/list streaming.

#### Phase 8-B2 — Operator Workflow Writes

Scope:

```text
POST /keep/requests/{requestId}/status
POST /keep/requests/{requestId}/update
POST /keep/requests/{requestId}/close
POST /keep/requests/{requestId}/cancel
POST /keep/requests/{requestId}/acknowledge
```

Includes first-response calculation, attention clear/change behavior, timeline events,
optional final messages, and integration tests.

#### Phase 8-B3 — Customer Page Writes

Scope:

```text
POST /keep/r/{pageToken}/message
POST /keep/r/{pageToken}/question
POST /keep/r/{pageToken}/update-request
POST /keep/r/{pageToken}/schedule-change-request
POST /keep/r/{pageToken}/change-or-cancel-request
POST /keep/r/{pageToken}/complaint
POST /keep/r/{pageToken}/feedback
```

Includes attention creation/escalation, terminal/expired blocking, one-feedback rule, and
integration tests.

#### Phase 8-B4 — Participation Filtering And Routing Surface

Scope:

- Responsible/Watching attach/detach or management service;
- update `GET /keep/requests` filtering/sorting:
  - Owner/Admin see all;
  - Operator sees Responsible/Watching;
  - unassigned requests visible to Owner/Admin;
  - attention sort order from D8;
- tests for visibility and routing-ready participant state.

**Rationale:** B1 establishes schema and read contracts first. Later write slices append events
and update state into a proven model instead of reshaping responses mid-build.

**ADR:** ADR-093.

---

## Decision Queue

1. D1 — Customer request page access — **Locked**
2. D2 — Phase 8 route shape — **Locked**
3. D3 — Timeline/event model — **Locked**
4. D4 — Lightweight request participation — **Locked**
5. D5 — Message/update model — **Locked**
6. D6 — Request status transitions, terminal pages, feedback, and expiry — **Locked**
7. D7 — First-response policy and business communication reality — **Locked**
8. D8 — Attention state and queue movement — **Locked**
9. D9 — Notifications boundary — **Locked**
10. D10 — Phase 8 implementation slice order — **Locked**
