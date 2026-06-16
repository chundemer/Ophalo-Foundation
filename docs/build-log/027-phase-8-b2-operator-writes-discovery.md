# Phase 8-B2 — Operator Writes Discovery

**Status:** Discovery complete. Ready to implement in split sessions.
**Build-log preceding this:** 026-phase-8-b1-beta-detail-customer-page.md
**Date:** 2026-06-16

---

## Purpose

Define the operator write surface for Keep requests before implementation. B1 created the data
model and read surfaces. B2 starts the business workflow: status movement, customer-visible
business updates, internal notes, and attention acknowledgement.

B2 is intentionally split into smaller implementation sessions. Do not implement the full B2
surface in one coding session.

---

## Implementation Split

### B2-alpha — Status Foundation + Status Write

Scope:
- Add `Scheduled` to `KeepRequestStatus`.
- Add/use `Keep.RequestsOperate`.
- Add B2 `KeepRequest` error codes and HTTP mappings.
- Add operator detail UI metadata (`AvailableActions`, validation hints).
- Implement `PATCH /keep/requests/{id}/status`.
- Implement status transition rules.
- Implement status + message event behavior.
- Return updated operator detail result.
- Add integration coverage for status writes.

Out of scope:
- Business update endpoint.
- Internal note endpoint.
- Attention acknowledge endpoint.
- External contact logging.
- Participant attach/detach.

### B2-beta — Business Updates + Internal Notes

Scope:
- Implement `POST /keep/requests/{id}/business-updates`.
- Support optional explicit `setStatus`.
- Implement `POST /keep/requests/{id}/internal-notes`.
- Allow internal notes after `Closed`/`Cancelled`.
- Wire first-response effects for customer-visible business updates.
- Return updated operator detail result.
- Add integration coverage.

Out of scope:
- External contact logging.
- Participant attach/detach.
- Customer writes.

### B2-gamma — Attention Acknowledge + Attention Clearing

Scope:
- Implement `POST /keep/requests/{id}/attention/acknowledge`.
- Require acknowledgement reason.
- Apply request-level attention clearing rules.
- Ensure customer-visible business responses clear business-waiting attention.
- Ensure acknowledge does not count as first response.
- Return updated operator detail result.
- Add integration coverage.

Out of scope:
- Terminal lifecycle analytics (`TerminatedAtUtc`, terminal auto-clear) — split to B2-delta.
- Notification delivery.
- Participant routing.
- External contact logging.

### B2-delta — Terminal Lifecycle + Analytics Primitives

Scope:
- Set `TerminatedAtUtc` when a request transitions to `Closed` or `Cancelled`.
- Auto-clear active attention when a request transitions to `Closed` or `Cancelled`.
- Do not create `AttentionAcknowledged` or require an acknowledgement reason for terminal auto-clear.
- Preserve fallback attention acknowledgement for terminal requests only when active attention remains due to legacy/bad data.
- Add integration coverage for terminal timestamp and terminal auto-clear behavior.

Out of scope:
- Reopen.
- Terminal customer feedback expansion.
- External contact logging.

---

## Locked Decisions

### D1 — First Response

First response is satisfied only when the business makes customer-facing contact on a
customer-origin request.

Counts:
- Customer-visible Keep business update.
- Status change with a customer-visible message.
- Logged external customer contact, when that later slice is implemented.

Does not count:
- Customer-created request.
- Operator view.
- Attach/watch/routing.
- Internal note.
- Attention acknowledge.
- Silent status change.
- Business-created request by itself.

### D2 — Status Visibility And Customer Update Awareness

Status changes append customer-visible `StatusChanged` timeline events. Silent status changes are
visible as status movement but do not count as first response.

Customer update awareness is computed later from events:

```text
Visibility = All
ActorType = AccountUser
OccurredAtUtc > CustomerLastViewedAtUtc
```

B2 does not add a derived `LastCustomerVisibleBusinessActivityAtUtc` field. B2's job is to create
correctly typed, visible, timestamped events.

### D3 — Business Updates And Status Changes

Communication and status remain separate domain concepts. Business updates do not implicitly
change request status.

Combined operator actions are allowed only when the payload explicitly includes both:
- a customer-visible message/update
- an intended status change

Status + message together creates one coherent customer-visible update and must not create
duplicate timeline noise.

### D4 — Combined Event Shape

Combined status + customer-visible message creates one event:

```text
EventType = StatusChanged
Visibility = All
Content = operator message
ActorType = AccountUser
ActorDisplayName = operator display/email
MessageIntent = BusinessUpdate
CommunicationChannel = InApp
```

Message-only business update:

```text
EventType = MessageAdded
Visibility = All
Content = message
MessageIntent = BusinessUpdate
CommunicationChannel = InApp
```

Status-only change:

```text
EventType = StatusChanged
Visibility = All
Content = null
```

### D5 — Attention Clearing

Attention is request-level, not message-level. Multiple customer messages while the business is
waiting keep or escalate one request-level attention state. Sorting uses the oldest unresolved
customer-waiting timestamp.

Clears business-waiting attention:
- customer-visible business message/update
- status change with customer-visible message
- logged external customer contact, when implemented
- setting `PendingCustomer` with a customer-visible message explaining what is needed

Does not clear attention:
- internal note
- silent status change
- attach/detach/routing
- viewing the request

`Acknowledge Attention` is a separate internal action. It clears/dismisses the dashboard alert
without customer communication, does not notify the customer, does not count as first response,
creates an internal `AttentionAcknowledged` event, and records who cleared it and why.

External customer contact, once explicitly logged in a later pre-go-live slice, also clears
business-waiting attention and can count as first response when applicable. External contact logs
are internal-only by default; any customer-visible receipt/recap is a separate explicit future
option.

### D6 — Participant Routing Scope

B2 does not include participant attach/detach. B2 may return participant information in operator
detail responses, but routing changes remain B4.

Deferred to B4:
- attach operator to request
- detach operator from request
- reattach existing participant row
- responsible vs watching behavior
- request list filtering by participant
- unassigned request visibility
- notification routing semantics

### D7 — Terminal State Behavior

B2 does not support reopen.

`Resolved` is a review/follow-up state, not final archive. `Closed` and `Cancelled` are terminal
archive states.

Allowed while `Resolved`:
- send customer-visible follow-up
- ask for feedback
- close request
- add internal note

Allowed after `Closed`:
- read request/timeline/feedback
- add internal note
- copy/share read-only link until expiry

Allowed after `Cancelled`:
- read request/timeline
- add internal note

Not allowed after `Closed` or `Cancelled`:
- reopen
- business update to customer
- normal customer actions
- status changes except no-op
- new request work

Terminal transition rule (B2-delta):
- transitioning to `Closed` or `Cancelled` sets `TerminatedAtUtc = now`
- active attention is auto-cleared as terminal cleanup
- no `AttentionAcknowledged` event is created
- no acknowledgement reason is required
- `AttentionClearReason` remains null
- the terminal `StatusChanged` event is the audit anchor

### D8 — Internal Notes After Terminal States

Internal notes are allowed after `Closed` and `Cancelled` for private business recordkeeping.

Rules:
- never customer-visible
- never notify the customer
- never count as first response
- never clear customer-facing attention
- never change status/lifecycle
- update normal audit timestamps
- do not update `LastBusinessActivityAt`

### D9 — External Contact Scope

B2 does not include logged external customer contact. External contact deserves a dedicated
pre-go-live slice because it affects channel support, customer visibility, backdating, first
response, attention clearing, and mobile capture.

B2 should preserve compatibility with that later slice: first-response and attention rules mention
external contact, but no endpoint is implemented in B2.

Locked posture for that later slice:
- logged phone/SMS/email/in-person/other customer contact is internal-only by default
- logged external contact can clear business-waiting attention
- logged external contact can count as first response when applicable
- it must not automatically appear on the customer timeline or notify the customer
- any customer-visible receipt/recap must be a separate explicit operator choice

### D10 — Write Response Shape

B2 write endpoints return the updated operator detail result after successful writes. Minimal
success and event-only responses are deferred.

Reason:
- avoids stale UI state
- avoids immediate follow-up GETs
- lets integration tests assert status, attention, first-response, and timeline changes in one
  response

---

## Request Status Set

Add `Scheduled` to the request lifecycle.

Final B2 status set:

```text
Received
Scheduled
InProgress
PendingCustomer
Resolved
Closed
Cancelled
```

Definitions:
- `Received` — request submitted; business has not committed a next step yet.
- `Scheduled` — business has planned or committed a future service window/next action.
- `InProgress` — work is actively being performed or actively handled.
- `PendingCustomer` — business is waiting on customer input/action.
- `Resolved` — business believes request is handled; follow-up/feedback window remains.
- `Closed` — archived/final.
- `Cancelled` — request will not proceed.

Reference app changes:
- Do not copy `Reviewing`.
- Replace vague `Waiting` with `PendingCustomer`.
- Use attention for business-waiting state.
- Add `Scheduled`.
- Use `Resolved` before final `Closed`.
- No reopen in B2.

---

## Status Transition Rules

Allowed:

```text
From Received/Scheduled/InProgress/PendingCustomer:
  -> Scheduled
  -> InProgress
  -> PendingCustomer
  -> Resolved
  -> Cancelled

From Resolved:
  -> InProgress
  -> PendingCustomer
  -> Closed
  -> Cancelled

From Closed/Cancelled:
  no status transitions
```

Same-status behavior:

```text
same status + message -> allowed; creates customer-visible update
same status + no message -> no-op success; returns current detail
```

Required messages:

```text
PendingCustomer requires customer-visible message
Cancelled requires customer-visible message
```

---

## Endpoints And Payloads

### Change Status

```text
PATCH /keep/requests/{id}/status
```

```json
{
  "status": "scheduled",
  "message": "We have you scheduled for Thursday morning."
}
```

`message` is optional except when required by status rules.

### Business Update

```text
POST /keep/requests/{id}/business-updates
```

```json
{
  "message": "We have you scheduled for Thursday morning.",
  "setStatus": "scheduled"
}
```

`message` is required. `setStatus` is optional and explicit.

### Internal Note

```text
POST /keep/requests/{id}/internal-notes
```

```json
{
  "note": "Customer prefers morning appointments."
}
```

`note` is required.

### Acknowledge Attention

```text
POST /keep/requests/{id}/attention/acknowledge
```

```json
{
  "reason": "Duplicate ETA request already answered in previous update."
}
```

`reason` is required.

---

## Validation

Plain text only; trim input before validation/storage.

| Field | Required | Max |
|-------|----------|-----|
| business update `message` | yes | 4000 |
| internal note `note` | yes | 4000 |
| status `message` | optional by default | 2000 |
| acknowledge `reason` | yes | 500 |

Message-required statuses:
- `PendingCustomer`
- `Cancelled`

---

## Errors And HTTP Mapping

Add/use these `KeepRequest` errors for B2:

```text
KeepRequest.NotFound
KeepRequest.Forbidden
KeepRequest.InvalidStatus
KeepRequest.InvalidStatusTransition
KeepRequest.MessageRequired
KeepRequest.MessageTooLong
KeepRequest.NoteRequired
KeepRequest.NoteTooLong
KeepRequest.AttentionReasonRequired
KeepRequest.AttentionReasonTooLong
KeepRequest.TerminalState
```

HTTP mapping:

```text
NotFound -> 404
Forbidden -> 403
InvalidStatus -> 400
InvalidStatusTransition -> 422
MessageRequired/MessageTooLong -> 400
NoteRequired/NoteTooLong -> 400
AttentionReasonRequired/AttentionReasonTooLong -> 400
TerminalState -> 409
```

---

## Permissions

Add/use:

```text
Keep.RequestsOperate
```

Role mapping:

```text
Owner    -> Keep.RequestsView + Keep.RequestsOperate
Admin    -> Keep.RequestsView + Keep.RequestsOperate
Operator -> Keep.RequestsView + Keep.RequestsOperate
Viewer   -> Keep.RequestsView only
```

B2 write actions require `Keep.RequestsOperate`:
- status changes
- customer-facing business updates
- internal notes
- attention acknowledgements

Read endpoints remain on `Keep.RequestsView`.

---

## Operator Detail Metadata

Updated operator detail responses should include server-computed UI metadata so the frontend can
render actions without extra calls.

Suggested shape:

```json
{
  "availableActions": {
    "canChangeStatus": true,
    "canSendBusinessUpdate": true,
    "canAddInternalNote": true,
    "canAcknowledgeAttention": false,
    "allowedStatuses": ["scheduled", "in_progress", "pending_customer", "resolved", "cancelled"]
  },
  "validation": {
    "businessUpdateMaxLength": 4000,
    "internalNoteMaxLength": 4000,
    "statusMessageMaxLength": 2000,
    "acknowledgeReasonMaxLength": 500,
    "messageRequiredForStatuses": ["pending_customer", "cancelled"]
  }
}
```

The server is the source of truth. The UI may use this metadata to disable buttons and show local
validation hints, but server validation remains authoritative.

---

## Reference App Lessons Applied

The reference app used:

```text
Received
Reviewing
Waiting
Active
Complete
Closed
```

and separate endpoints for update, status text, change status, close, internal notes, external
contact, and acknowledge. It also had known status-text/timeline inconsistency:
initial visible status appeared in customer history, while later status-only updates could be
hidden or represented differently.

B2 deliberately modifies that behavior:
- clearer status vocabulary for service businesses
- scheduled work is not treated as in-progress
- status changes always create customer-visible timeline movement
- status + message is one coherent event
- external contact is deferred into its own slice
- no reopen in B2

---

## Exit Gate For B2

B2 is complete only when all split sessions pass:

- `dotnet build`
- architecture tests
- unit tests
- integration tests

The final B2 session must update `docs/session-log.md` with exact build/test state.
