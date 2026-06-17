# Build Log 036 — Phase 8-B4 Service Request Detail Enrichment Decisions

**Phase:** 8-B4 discovery / pre-implementation gate
**Date:** 2026-06-17
**Status:** Decision gate complete. Ready for implementation.
**Build log preceding this:** `035-phase-8-b3-plus-feedback-implementation.md`
**ADRs locked:** 145..163
**Next free ADR:** ADR-164

---

## Purpose

Define the service request detail enrichment slice before implementation.

Phase 8-B4 is not a new workflow slice. It makes the authenticated business-side
service request detail view trustworthy and efficient for busy operators, while keeping
the request list as the triage and quick-action surface.

Reference lessons reviewed:

- the reference app had request detail/list mapping drift that caused wrong labels or 500s;
- attention labels such as customer-message/unresolved-feedback need explicit mapping and tests;
- public customer-token pages must remain minimal and guarded;
- contact actions are useful operator affordances, but external contact logging carries first-response,
  attention-clearing, audit, and reversal semantics and remains a separate workflow;
- the reference app's slug/customer URL machinery should not be copied until this project has a safe
  browser-facing intake URL contract.

---

## Locked Decisions

### ADR-145 — Request list triages; service request detail is the workbench

The request list is the triage and common-action surface. The service request detail page is the
full-context workbench for careful action.

Locked implications:

- operators should not have to open detail for routine confident actions;
- sending a simple business update from the request list is desirable future behavior;
- request list owns scanning, movement, prioritization, and common low-friction actions;
- detail owns full history, internal notes, participant context, customer page link, feedback context,
  and careful state changes;
- request movement indicators stay with request-list work, not B4 detail enrichment.

Reason: busy operators often act between jobs, calls, and interruptions. Keep should let them handle
obvious actions quickly and reserve detail for moments where context changes the decision.

### ADR-146 — Participant display names prefer human name with email fallback

Service request participant display names use:

```text
Participant.DisplayName =
  nonblank linked User.Name
  else AccountUser.Email
```

Rules:

- applies to `KeepRequestParticipantItem.DisplayName`;
- works for active and invited participants;
- does not rewrite denormalized timeline `ActorDisplayName` snapshots;
- does not expose participant names on the public customer page;
- does not add assignment/watch/mute write workflows in B4.

Reason: detail should show friendly names when available, but must never show blank or fragile labels.

### ADR-147 — ParticipationType stays Responsible/Watching only

`ParticipationType` remains intentionally small:

```text
Responsible = I am handling this request.
Watching = Keep me in the loop.
```

Rules:

- participation is intentional involvement and future notification routing;
- commenting, adding an internal note, or taking an action does not automatically make someone a
  participant;
- contributors/actors may be shown later from timeline data, but they are not notification recipients
  by default;
- no `SignOffReview` participation type in B4.

Reason: notifications must be intentional. Auto-subscribing every contributor would create noise and
reduce operator trust.

### ADR-148 — Closeout review and archive are deferred, not ParticipationType

Closed-request closeout review and archive are real lifecycle concepts, but they are deferred and must
not be modeled as `ParticipationType`.

Future lifecycle language:

```text
Closed = customer/request work is done.
Closeout reviewed = internal business/admin follow-up is done.
Archived = no longer shown in normal active work surfaces.
```

Rules:

- B4 does not add closeout review, archive, billing disposition, or signoff notifications;
- future closeout review should be modeled separately from request participation;
- negative feedback after close should remain visible/attention-worthy and should likely block any
  future quiet archive behavior until reviewed.

### ADR-149 — Contact fields plus structured contact action metadata

Service request detail exposes customer contact information and structured action metadata:

```text
CustomerName
CustomerPhone
CustomerEmail?
ContactActions
```

Recommended shape:

```json
"contactActions": [
  { "type": "call", "available": true, "target": "0412345678" },
  { "type": "email", "available": true, "target": "jane@example.com" }
]
```

Rules:

- `call` is available when phone exists;
- `email` is available when email exists;
- missing email means no email action, not a broken disabled action;
- the metadata can later be reused by request-list quick actions;
- B4 does not log that a call/email happened;
- B4 does not count contact actions as first response;
- B4 does not clear attention from contact actions;
- B4 does not send email/SMS/push.

External contact capture remains deferred.

### ADR-150 — NewRequestUrl remains null

`NewRequestUrl` remains `null` in B4 for both active and expired customer pages.

Rules:

- do not construct a new-request URL from raw public intake tokens;
- do not expose public intake token/hash through service request detail or customer page;
- do not introduce branded/public-slug customer intake URLs in B4;
- expired customer pages may show safe tombstone copy without a start-new-request link when
  `newRequestUrl` is null.

Reason: a new-request link should exist only after public intake browser routing, account public
identity, and setup/rotation rules are explicit.

### ADR-151 — Feedback comment is Owner/Admin-only on detail

Service request detail exposes closed-request feedback state to authenticated request viewers, but
`FeedbackComment` is visible only to Owner/Admin.

Owner/Admin detail view:

```text
FeedbackWasResolved
FeedbackComment
FeedbackSubmittedAtUtc
```

Operator/Viewer detail view:

```text
FeedbackWasResolved
FeedbackSubmittedAtUtc
FeedbackComment = null/redacted
FeedbackCommentVisible = false
```

Rules:

- `FeedbackWasResolved` and `FeedbackSubmittedAtUtc` are visible to authenticated request viewers;
- `FeedbackComment` is Owner/Admin-only because it may contain sensitive dissatisfaction, billing
  frustration, or criticism of staff;
- B4 should include metadata such as `FeedbackCommentVisible` so UI can distinguish "no comment"
  from "comment not permitted";
- feedback remains request-level state, not a timeline event;
- public customer page does not expose `FeedbackComment`;
- B4 does not add feedback edit/delete, ratings, CSAT/NPS, review prompts, or analytics.

### ADR-152 — Negative feedback creates post-close attention without reopening

Negative feedback on a closed request remains:

```text
Status = closed
AttentionReason = unresolved_feedback
WaitingDirection = business
PriorityBand = priority
```

Rules:

- do not reopen automatically;
- do not change `TerminatedAtUtc`;
- service request detail must show closed status and unresolved-feedback attention together;
- `unresolved_feedback` must be explicitly mapped and tested;
- do not add a parallel feedback status.

Reason: closed is the business lifecycle state; unresolved feedback is the customer signal requiring
follow-up.

### ADR-153 — Closed unresolved feedback reappears in Owner/Admin list attention

Closed requests with active `unresolved_feedback` attention must not disappear into closed-only
history.

Rules:

- normal closed requests stay hidden unless closed filters are selected;
- closed requests with active `unresolved_feedback` attention appear in the default Owner/Admin
  request list;
- operators do not see these in their default list unless explicitly attached/assigned by a future
  workflow;
- they are visually distinct post-close follow-up, not reopened work;
- they sort above non-attention active requests;
- exact request-list ranking is deferred to request-list implementation.

Recommended ordering principle:

```text
1. Active overdue business-waiting requests
2. Active priority business-waiting requests
3. Closed post-close unresolved feedback needing Owner/Admin review
4. Active standard business-waiting requests
5. Other active requests
```

### ADR-154 — Mark feedback reviewed is future clear-from-list action

Future Owner/Admin action:

```text
Mark feedback reviewed
```

Purpose: clear the closed unresolved-feedback item from the default request list after responsible
review.

Rules:

- Owner/Admin only;
- does not reopen the request;
- does not delete feedback;
- does not hide feedback from detail;
- does not notify the customer;
- does not count as resolving the customer's issue;
- stores reviewer and timestamp when implemented;
- implementation deferred out of B4 unless explicitly pulled in.

Likely future fields:

```text
FeedbackReviewedAtUtc
FeedbackReviewedByAccountUserId
```

### ADR-155 — Operator and customer field boundaries stay separate

Service request detail is authenticated/operator-facing and may expose internal operational context.
The customer page remains a minimal public bearer-link surface.

Operator/service request detail may expose:

```text
RequestId
PageToken
CustomerPhone
CustomerEmail
ContactActions
Participants
Internal notes/events
Attention fields
First response fields
FeedbackWasResolved
FeedbackComment only when Owner/Admin
FeedbackCommentVisible
FeedbackSubmittedAtUtc
AvailableActions
Validation hints
```

Customer page may expose:

```text
BusinessName
ReferenceCode
Status
Description
CurrentStatusText
IsTerminal
FeedbackWasResolved
FeedbackSubmittedAtUtc
ExpiresAtUtc
Customer-visible timeline events
AllowedActions
NewRequestUrl
```

Customer page must not expose:

```text
RequestId
AccountId
AccountUserId
PageToken in body
CustomerPhone
CustomerEmail
ContactActions
Participants
Internal notes
System/internal events
Attention fields
First response fields
FeedbackComment
Operator available actions
Operator validation hints
```

Reason: the detail page helps the business act; the customer page gives customers safe continuity.
These surfaces must not drift together just because they share a request.

### ADR-156 — B4 requires role-aware authorization, redaction, mapping, and boundary tests

B4 service request detail enrichment must be protected by focused integration tests.

Access coverage:

```text
Anonymous service request detail -> 401
No keep.requests.view permission -> 403
Blocked/unavailable account -> 403
Feature unavailable -> 403
Unknown request -> 404
Cross-account request -> 404
```

Role-aware feedback visibility:

```text
Owner sees FeedbackComment and FeedbackCommentVisible = true
Admin sees FeedbackComment and FeedbackCommentVisible = true
Operator sees FeedbackWasResolved/FeedbackSubmittedAtUtc but FeedbackComment = null and
  FeedbackCommentVisible = false
Viewer follows the same redaction posture as Operator unless a later decision further restricts
  Viewer feedback visibility
```

Participant display:

```text
Participant with nonblank linked User.Name -> display name
Participant with blank User.Name -> AccountUser.Email fallback
Participant with no linked User -> AccountUser.Email fallback
```

Contact actions:

```text
Phone exists -> call action present
Email exists -> email action present
Email missing -> no email action
Contact action metadata does not log contact, clear attention, or count first response
```

Negative feedback attention:

```text
Detail shows status = closed
Detail shows attentionReason = unresolved_feedback
Detail shows waitingDirection = business
Detail shows priorityBand = priority
Mapping is explicit, no generic fallback
```

Customer boundary:

```text
Customer page does not expose RequestId/AccountId/AccountUserId
Customer page does not expose PageToken in body
Customer page does not expose customer phone/email/contactActions
Customer page does not expose participants
Customer page does not expose attention fields
Customer page does not expose first-response fields
Customer page does not expose FeedbackComment
Expired customer page remains safe tombstone with NewRequestUrl = null
```

Reason: B4 is mostly read-model enrichment, so the biggest risks are leaks, role mistakes,
optional-field UI breakage, and mapping drift. The reference app had exactly those classes of
problems.

### ADR-157 — B4 implements detail enrichment only

B4 implements service request detail read-model enrichment only.

B4 includes:

```text
participant display name enrichment
contact action metadata on detail
feedback comment redaction/visibility metadata on detail
explicit unresolved-feedback detail mapping/tests
customer page boundary tests
role-aware detail tests
```

B4 excludes:

```text
request-list default surfacing/ranking changes
mark feedback reviewed
closeout/archive workflow
new request URL
external contact logging
assignment/watch writes
auto-watch contributors
contact preference editing
```

Reason: request list behavior has its own query, filtering, ordering, and role-specific visibility
contract. The product invariant is locked, but implementation belongs to the request-list slice.

### ADR-158 — Adjacent workflow/list/notification/telemetry work stays deferred

B4 explicitly defers adjacent work:

```text
request-list unresolved-feedback surfacing implementation
request-list sorting/ranking/movement indicators
mark feedback reviewed action
closeout review/archive workflow
assignment/watch/mute write workflows
contributor chips / recently active people
external contact logging/revert
contact action audit
contact preferences/email opt-out
outbound notifications: email, push, SMS
new request URL / branded public slug URLs
identity-bound customer access/link management
feedback timeline events
feedback edit/delete
ratings/CSAT/NPS/reviews/analytics
attachments/photos
full signal/projection engine
business-hours-aware SLA math
customer page visit tracking
```

Reason: these are real needs, but they belong to later slices with their own lifecycle, permissions,
and UX decisions.

### ADR-159 — B4 is no-migration read-model enrichment

B4 requires no database migration.

Rules:

```text
No schema changes.
No new persisted fields.
No migration.
ContactActions and FeedbackCommentVisible are response DTO fields only.
Participant display enrichment reads existing AccountUser/User data.
Feedback visibility reads existing feedback fields plus current user role.
```

If implementation discovers a need for new storage, the work has drifted into a deferred workflow
and scope must be revisited.

### ADR-160 — Response-shape additions are ContactActions and FeedbackCommentVisible only

B4 adds two fields to `KeepRequestDetailResult`:

```text
IReadOnlyList<ContactActionItem> ContactActions
bool FeedbackCommentVisible
```

Contact action item:

```csharp
public sealed record ContactActionItem(
    string Type,
    bool Available,
    string Target);
```

Rules:

- contact actions include only available actions;
- `call` appears when phone exists;
- `email` appears when email exists;
- `FeedbackCommentVisible = true` for Owner/Admin;
- `FeedbackCommentVisible = false` for Operator/Viewer;
- `FeedbackComment = null` can mean no comment exists or comment redacted; `FeedbackCommentVisible`
  disambiguates.

Examples:

```json
"contactActions": [
  { "type": "call", "available": true, "target": "0412345678" },
  { "type": "email", "available": true, "target": "jane@example.com" }
]
```

```json
"feedbackComment": null,
"feedbackCommentVisible": false
```

### ADR-161 — Viewer is read-only and receives no contact actions

Viewer may read service request detail when permitted, but receives no write/action affordances and
no sensitive feedback comments.

Rules:

- Viewer access requires `keep.requests.view`;
- Viewer cannot change status, send business updates, add internal notes, acknowledge attention, or
  use future write actions;
- Viewer sees `FeedbackWasResolved` and `FeedbackSubmittedAtUtc`;
- Viewer receives `FeedbackComment = null` and `FeedbackCommentVisible = false`;
- Viewer receives `ContactActions = []`;
- `ContactActions` are populated only when the current user has `keep.requests.operate`;
- B4 does not change raw `CustomerPhone` / `CustomerEmail` visibility on service request detail.

### ADR-162 — Customer page visit tracking is deferred

Customer page visit tracking is a useful future operator confidence signal, not B4 scope.

Rules:

- no customer page opened endpoint in B4;
- no beacon/telemetry write in B4;
- no `CustomerPageLastViewedAtUtc` field in B4;
- no "customer is online" semantics;
- no "customer read this" semantics;
- future implementation may expose last viewed time and whether the customer viewed after the latest
  business update;
- future implementation must define debounce, rate limiting, privacy, and public-token guard behavior.

Reason: this product signal helps operators decide whether posting in Keep is likely enough or whether
they should call/email, but it requires storage and public-token telemetry rules.

### ADR-163 — B4 completion gate

B4 is complete when:

```text
decision docs are updated through ADR-163
no migration is generated
service request detail returns ContactActions and FeedbackCommentVisible
participant display names use User.Name with email fallback
FeedbackComment is Owner/Admin-only
Viewer/read-only users receive no ContactActions
customer page boundary tests remain green
B4 integration matrix passes
full test suite passes
```

B4 must not implement:

```text
request-list resurfacing/ranking
mark feedback reviewed
closeout/archive
visit tracking
contact logging
new request URL
external notifications
assignment/watch writes
```

If implementation discovers a schema need, stop and revisit scope.

---

## Implementation Handoff

Expected code changes:

```text
src/OpHalo.Keep.Application/Requests/KeepRequestDetailResult.cs
src/OpHalo.Keep.Application/Requests/KeepRequestDetailMapper.cs
src/OpHalo.Keep.Application/Requests/IKeepRequestDetailPersistence.cs
src/OpHalo.Keep.Infrastructure/Persistence/EfKeepRequestDetailPersistence.cs
src/OpHalo.Keep.Application/Requests/GetKeepRequestDetailService.cs
tests/OpHalo.IntegrationTests/Api/KeepRequestDetailTests.cs
tests/OpHalo.IntegrationTests/Api/KeepCustomerPageTests.cs or existing boundary coverage
```

Implementation notes:

- Redaction belongs in the application/detail mapping path using current user role/operate
  permission, not in persistence.
- Participant display enrichment should use existing `AccountUser.UserId -> User.Name` when present
  and nonblank, falling back to `AccountUser.Email`.
- `ContactActions` should be empty for read-only users; populated for users with
  `keep.requests.operate`.
- Owner/Admin means trusted business management. Operators/Viewers do not receive feedback comment
  content unless a future escalation/assignment workflow explicitly changes that.
- Do not add a migration.
