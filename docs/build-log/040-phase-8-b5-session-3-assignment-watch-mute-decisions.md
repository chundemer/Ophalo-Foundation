# Build Log 040 — Phase 8-B5 Session 3 Assignment / Watch / Mute Decisions

**Phase:** 8-B5 Session 3 discovery / pre-implementation gate
**Date:** 2026-06-17
**Status:** Decisions locked. Ready to split into bounded Claude coding sessions.
**Build log preceding this:** `039-phase-8-b5-claude-coding-sessions.md`
**ADRs locked:** 222..235
**Next free ADR:** ADR-236

---

## Purpose

Session 3 turns the B5 read-only participation model into controlled routing writes:
assignment, transfer, watching, unwatching, mute, and unmute. The goal is to make request ownership
clear without turning Keep into a heavyweight ticket tracker or prematurely implementing notification
delivery.

The key boundary is:

```text
Participation controls routing and future notification eligibility.
It does not change customer-visible request state.
It does not count as customer response.
It does not become notification delivery in Session 3.
```

---

## Gap Check

The decisions below close the gaps needed before implementation:

- who may assign, transfer, clear, watch, unwatch, mute, and unmute;
- whether operators may self-assign and where they can discover unassigned work;
- how unassigned and stale responsibility behave in list/detail;
- how list-level Owner/Admin assignment differs from detail-only controls;
- how optional assignment notes and notification intent are represented before delivery exists;
- which endpoints and idempotency behavior Claude should implement;
- how to split implementation into manageable coding sessions.

No open product decision should block Session 3 implementation after this log.

---

## Locked Decisions

### ADR-222 — Participation writes are controlled routing actions

Session 3 implements participation writes for non-terminal requests only.

Included actions:

```text
assign responsible
transfer responsible
clear responsible
watch
unwatch
mute
unmute
```

Rules:

- actions apply to `Received`, `Scheduled`, `InProgress`, `PendingCustomer`, and `Resolved`;
- actions are blocked for `Closed` and `Cancelled`;
- closed unresolved-feedback rows remain Owner/Admin review-only and do not support participation
  writes in Session 3;
- OffSeason blocks all participation writes;
- participation writes are internal routing changes only.

Out of scope:

```text
notification delivery
quiet hours
global notification preferences
Operator Unassigned/Available queue
auto-assignment account settings
list SSE/realtime refresh
customer-visible receipts/recaps
```

Reason: Session 3 should make routing writable without pulling in list-navigation or notification
delivery work.

### ADR-223 — Responsible assignment permissions

Owner/Admin may assign, transfer, and clear responsibility for any eligible account user.

Operators may self-assign only when all are true:

```text
request is non-terminal
request is unassigned/effectively unassigned
operator can legitimately access the request through an allowed surface
account is not OffSeason/read-only
```

Operators may not:

```text
assign requests to other users
transfer responsibility away from another user
clear another user's responsibility
clear themselves as Responsible
```

If an Operator needs fewer interruptions while still responsible, they may mute themselves. Owner/Admin
must clear or transfer responsibility.

Reason: Operators can claim available work, but only Owner/Admin can put work back into the
unassigned pool or reroute someone else's work.

### ADR-224 — Responsible, Watching, and clear semantics

A user may have only one active participation row per request.

Rules:

- `Responsible` and `Watching` are mutually exclusive for the same user;
- a request may have zero or one effective Responsible participant;
- a request may have many Watching participants;
- if a Watching user becomes Responsible, their active participation changes from Watching to
  Responsible;
- clearing responsibility detaches the Responsible participant and does not convert them to Watching;
- transferring responsibility detaches the previous Responsible participant and does not convert
  them to Watching;
- Owner/Admin must explicitly add the previous Responsible as Watching if they should remain in the
  loop;
- unwatch applies only to active Watching participants and never clears responsibility.

Session 3 must enforce one active Responsible participant with an application invariant and should
add a filtered unique index where the database/provider supports it.

Reason: one active participation role per user keeps notification state unambiguous.

### ADR-225 — Watch and mute semantics

Watch/unwatch and mute/unmute are personal participation controls.

Rules:

- watching creates or reactivates a Watching participant with `NotificationsEnabled = true`;
- muted state is not remembered after unwatch/detach;
- mute requires an active participation row for the current user;
- mute sets `NotificationsEnabled = false`;
- unmute sets `NotificationsEnabled = true`;
- mute does not detach participation;
- mute does not hide the request;
- mute/unmute is current-user only in Session 3;
- Owner/Admin cannot mute or unmute another user's request participation in Session 3.

The domain/service design should target notification-enabled changes by `AccountUserId` internally so
future delegated Owner/Admin notification controls can be added without a model rewrite.

Reason: mute means "do not interrupt me about this request," not "remove me" or "hide this."

### ADR-226 — Eligible participants and stale responsibility

Eligible Responsible assignees and Watchers are:

```text
Active Owner
Active Admin
Active Operator
```

Ineligible:

```text
Viewer
Invited users
Suspended users
Removed users
users from another account
```

Rules:

- Session 3 validates target-user eligibility when creating or changing participation;
- notification routing/intent must re-check that the recipient is Active and role-eligible;
- Suspended, Removed, Invited, and Viewer users are notification-ineligible even if a participation
  row exists;
- if stored Responsible is no longer Active Owner/Admin/Operator, the request is treated as
  effectively unassigned for routing and notification purposes;
- Session 3 does not automatically detach stale participants;
- Owner/Admin must explicitly reassign or clear stale responsibility;
- Owner/Admin list/detail metadata should expose enough state for a stale-assignment indicator.

Recommended Owner/Admin detail copy:

```text
Assigned user is no longer active. Reassign or clear responsibility.
```

Reason: Preserve audit history while making stale routing obvious and inert.

### ADR-227 — Operator default list hides unassigned requests

Unassigned requests are hidden from the Operator default list.

Rules:

- Owner/Admin continue to see unassigned active requests by default for routing;
- Operators may access eligible unassigned requests only through an explicit future
  Unassigned/Available queue or filter;
- Operator self-assignment requires the request to be visible through an allowed surface;
- Session 3 does not add the Operator Unassigned/Available queue;
- Session 3 does not decide whether future unassigned rows are returned hidden or fetched on demand.

Reason: Operators should not get a firehose of unassigned work in the default list, but the model
should still allow future self-claiming from an explicit available-work surface.

### ADR-228 — List and detail participation UI boundaries

Request detail is the primary surface for Session 3 participation controls.

Detail must expose:

```text
current responsible user
watchers
current-user participation
current-user notification enabled/muted state
stale responsible indicator for Owner/Admin
available action metadata for detail participation controls, if consistent with existing API style
```

Default list remains mostly read-only for participation, with one Owner/Admin routing exception.

List display requirements:

- show responsible person name;
- may retain compact B5 metadata such as `hasResponsible`, `watchingCount`,
  `currentUserParticipation`, and `currentUserNotificationState`;
- does not show full watcher lists;
- may show stale/ineligible responsible state to Owner/Admin;
- stale responsible does not count as effective routing.

List controls:

- Owner/Admin may assign or transfer Responsible from the list;
- Owner/Admin list assignment is available only for non-terminal eligible requests;
- Owner/Admin list assignment uses the same backend endpoint as detail assignment;
- Owner/Admin clear responsible is detail-only in Session 3;
- Operators do not see list-level assignment controls;
- watch, unwatch, mute, unmute, add watcher, remove watcher, and clear responsible remain detail
  controls in Session 3.

Reason: Owner/Admin dispatch from the list is efficient; destructive/unusual controls still deserve
detail context.

### ADR-229 — Participation writes are activity/ranking neutral and internal-only

Participation writes create internal audit/timeline events but do not affect customer/request activity
semantics.

Rules:

- do not update `LastCustomerActivityAtUtc`;
- do not update `LastBusinessActivityAtUtc`;
- do not set or clear attention;
- do not count as first response;
- do not change request status;
- do not affect ranking except through participation/notification metadata used by filters or future
  visibility rules;
- never appear on the customer page;
- do not create customer-visible receipts, recaps, emails, texts, or status updates.

Reason: assignment/watch/mute are internal routing state, not customer communication.

### ADR-230 — Explicit endpoints and idempotent write behavior

Session 3 uses explicit action endpoints rather than a generic participant mutation endpoint.

Responsible:

```text
PUT    /keep/requests/{requestId}/responsible
DELETE /keep/requests/{requestId}/responsible
```

`PUT /responsible` body:

```json
{ "accountUserId": "..." }
```

`DELETE /responsible` may accept an optional internal-note body for Owner/Admin managed clearing:

```json
{ "note": "optional internal note" }
```

Watching other users:

```text
PUT    /keep/requests/{requestId}/watchers/{accountUserId}
DELETE /keep/requests/{requestId}/watchers/{accountUserId}
```

Current-user watch convenience:

```text
PUT    /keep/requests/{requestId}/watch
DELETE /keep/requests/{requestId}/watch
```

Current-user mute:

```text
PUT    /keep/requests/{requestId}/mute
DELETE /keep/requests/{requestId}/mute
```

Mute/unmute routes require no body.

`PUT /watchers/{accountUserId}` and `DELETE /watchers/{accountUserId}` may accept an optional
internal-note body for Owner/Admin managed watcher changes:

```json
{ "note": "optional internal note" }
```

Idempotency rules:

- `PUT` routes are idempotent for the target state;
- `DELETE` routes are idempotent for the removed/deactivated state where safe;
- idempotent no-op writes return success with updated `KeepRequestDetailResult`;
- idempotent no-op writes do not create duplicate timeline events;
- idempotent no-op writes do not create duplicate notification intent;
- invalid actions still fail even when they resemble no-ops.

Examples of invalid actions:

```text
Operator trying to assign another user
Viewer trying to watch
mute without active participation
unwatch while Responsible, because Responsible is not Watching
```

Reason: explicit routes keep authorization clear; idempotency protects clients from retries and
double taps.

### ADR-231 — Participation write responses return updated detail

Assignment/watch/mute write endpoints return the updated `KeepRequestDetailResult`.

Rules:

- response includes updated participant summary;
- response includes current-user participation/notification state;
- response includes internal timeline events where the caller is allowed to see them;
- list refresh remains a client concern;
- Session 3 does not add list SSE, realtime invalidation, or separate list-level write responses.

Reason: participation controls mostly live on detail and need immediate local refresh.

### ADR-232 — Optional internal notes for managed participation changes

Participation writes do not require a reason or note.

Session 3 supports optional internal notes for Owner/Admin managed changes:

```text
assignment
transfer
clear responsible
add watcher
remove watcher
```

Rules:

- self-service watch, unwatch, mute, and unmute do not expose notes in Session 3;
- optional participation notes are internal-only;
- provided notes are stored with the internal timeline/audit event;
- notes never appear on the customer page;
- notes do not notify the customer;
- the event model should not prevent optional notes on additional participation actions later.

Reason: Owner/Admin sometimes need to include special instructions without forcing a note on every
routing change.

### ADR-233 — Assignment/watch notification intent is recorded, not delivered

Session 3 records notification intent as structured metadata on the internal participation event.

Notification intent is created for:

```text
Owner/Admin assignment or transfer to a new Responsible user
Owner/Admin adding another eligible user as Watching
```

Notification intent is not created for:

```text
reassigning the same Responsible user
clearing responsibility
Operator self-assignment
self-watch
remove watcher
mute/unmute
OffSeason writes, because writes are blocked
```

Rules:

- assignment and watcher intent are created only when the target user changes into the role;
- idempotent no-op writes do not create duplicate notification intent;
- actual mobile push/email/in-app delivery remains deferred;
- no notification delivery table, outbox, push job, email job, device-token lookup, retry state, or
  badge-count state is added in Session 3;
- when Phase 9 notification delivery exists, assignment/transfer and watcher-added events may send
  mobile/push notifications unless suppressed by account/user/request notification rules.

Intent metadata should be explicit enough for later delivery:

```text
intentKind: assignment | watcher_added
intendedRecipientAccountUserId
internalNoteIncluded: true | false
createdByAccountUserId
createdAtUtc
```

Optional internal assignment/watch notes may be included in the internal notification payload later,
but never in customer-visible communication.

Reason: assignment should remove the Owner/Admin's need to call/email the operator later, but actual
delivery belongs to the notification phase.

### ADR-234 — One ParticipationChanged event type with action codes

Session 3 uses one internal event type for participation changes, for example:

```text
ParticipationChanged
```

The event stores structured metadata:

```text
participationAction
targetAccountUserId
targetDisplayName snapshot if available
previousResponsibleAccountUserId when applicable
newResponsibleAccountUserId when applicable
optionalInternalNote when provided
notificationIntent kind when applicable
```

Initial `participationAction` codes:

```text
responsible_assigned
responsible_transferred
responsible_cleared
watcher_added
watcher_removed
self_watched
self_unwatched
muted
unmuted
```

Reason: these actions are one internal routing family. Separate event types would bloat the event enum
and timeline mapping.

### ADR-235 — Eligible participant lookup is compact and separate from request rows

Session 3 should add or reuse a compact eligible participant lookup if no suitable account-member
endpoint already exists.

Preferred shape:

```text
GET /keep/requests/participant-candidates
```

Response includes:

```text
accountUserId
displayName
role
```

Rules:

- returns Active Owner/Admin/Operator only;
- excludes Viewer, Invited, Suspended, Removed;
- excludes users from other accounts;
- does not include notification preferences;
- does not include private contact details;
- request-list rows do not include full candidate arrays;
- backend assignment/watch writes still validate eligibility authoritatively;
- Owner/Admin use this lookup for list/detail assignment and watcher management;
- Operators may use this lookup only if needed for display, but Operators cannot assign or watch
  other users.

Reason: assignment controls need eligible people, but request rows should stay compact.

---

## Claude Coding Session Split

Session 3 should be implemented in bounded slices:

```text
3A — Domain + persistence invariants
3B — API/application services + participant candidate lookup
3C — Detail/list read-model metadata + timeline DTO
3D — Integration verification, docs, and completion gate
```

See `039-phase-8-b5-claude-coding-sessions.md` for the Claude-ready implementation scopes.
