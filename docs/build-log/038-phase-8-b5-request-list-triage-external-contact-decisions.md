# Build Log 038 — Phase 8-B5 Request List Triage + External Contact Decisions

**Phase:** 8-B5 discovery / pre-implementation gate
**Date:** 2026-06-17
**Status:** B5 Session 1 complete. Session 2 external-contact discovery ready for final implementation handoff review.
**Build log preceding this:** `037-phase-8-b4-service-request-detail-enrichment-implementation.md`
**ADRs locked:** 164..218
**Next free ADR:** ADR-219

---

## Purpose

Phase 8-B5 turns the request list into the trusted operator command center. It should surface
attention-worthy requests, especially customer messages, complaints, schedule/change requests, and
closed unresolved feedback, while preparing quick actions that let busy operators act without
opening detail for routine cases.

This decision pass also locks how list quick contact relates to the later external-contact logging
workflow. Small businesses often resolve work through phone calls, text messages, email, and
in-person conversation. Keep must support that reality without pretending a button tap proves a
customer outcome.

---

## Locked Decisions

### ADR-164 — B5 list is the command center; detail remains the workbench

B5 implements the request list as the scanning, prioritization, and common-action surface.

Rules:

- list owns default triage, ranking, movement indicators, attention reason indicators, and quick
  action affordances;
- detail remains the full-history workbench for careful action, internal notes, participant context,
  feedback detail, customer page link, and complex state changes;
- B5 should not make notifications the primary destination yet;
- notification delivery remains later, after list routing, assignment/watch, and external-contact
  semantics are coherent.

Reason: the list must become the place operators trust before push/email notifications send people
there.

### ADR-165 — Default list surfaces closed unresolved feedback for Owner/Admin only

Closed requests with active `AttentionReason = UnresolvedFeedback` remain closed but reappear in
the default Owner/Admin list.

Rules:

- normal closed/cancelled requests stay hidden from the default list;
- closed unresolved-feedback items are visually distinct post-close follow-up, not reopened work;
- Owner/Admin see them by default;
- Operators do not see Owner/Admin-only post-close feedback escalation by default;
- Operators may see such items later only through explicit assignment/watch/visibility workflow;
- feedback comment visibility remains governed by B4: Owner/Admin-only unless a future decision
  changes it.

Reason: negative feedback after close is management-sensitive and operationally important, but it
should not silently become normal operator work.

### ADR-166 — B5 ranking is deterministic and attention-first

B5 request-list ranking uses explicit groups rather than a mystery score.

Recommended default order:

```text
1. Active overdue business-waiting requests
2. Active priority business-waiting requests
3. Closed post-close unresolved feedback needing Owner/Admin review
4. Active standard business-waiting requests
5. Other active requests
```

Within equivalent groups:

```text
Priority before standard.
Oldest unresolved AttentionSinceUtc first.
Stable fallback: latest customer/business activity, then CreatedAtUtc/Id.
```

Rules:

- waiting-on-customer should not rise in the business-waiting queue;
- list movement indicators should explain movement using stable fields such as attention level,
  priority band, attention reason, and waiting age;
- every attention reason must map explicitly to a stable public list code/label.

Reason: operators need to understand why a request moved. Deterministic ordering is easier to trust
and test than opaque scoring.

### ADR-167 — B5 quick actions are affordances unless backed by a write

Quick contact activation alone is not authoritative.

Rules:

- tapping `call`, `email`, or `text` does not by itself count as first response;
- tapping `call`, `email`, or `text` does not by itself clear attention;
- B5 may expose quick action metadata and UI placement for contact actions;
- authoritative first-response and attention effects come from a server-side logged business action,
  customer-visible Keep update, or explicit acknowledge action;
- B5 must not claim contact occurred merely because the device opened a phone, mail, or messaging app.

Reason: Keep should make contact easy, but audit/state changes require an operator-confirmed action.

### ADR-168 — Only calls collect an outcome component

The quick call workflow is the only quick contact flow that collects a customer outcome.

Initial call outcomes:

```text
Spoke with customer
Left voicemail
No answer
Wrong number
```

Rules:

- outcome is collected after the call attempt, not when the call button is tapped;
- email/text do not show outcome choices such as resolved, question answered, or customer handled;
- reason: the operator can know whether a call was answered or a voicemail was left, but cannot
  verify that a customer saw, understood, or accepted an email/text.

Reason: calls can produce immediate operator-known outcomes. Email/text can only prove sent activity
unless the customer later responds.

### ADR-169 — Call outcome effects depend on reach plus follow-up need

Call outcomes have explicit first-response and attention effects.

```text
Spoke with customer
- counts as first response
- may clear business-waiting attention when operator indicates no follow-up is needed

Left voicemail
- counts as first response
- does not prove resolution
- may clear business-waiting attention when operator indicates no follow-up is needed

No answer
- logs contact attempt
- does not count as first response
- does not clear attention

Wrong number
- logs contact issue
- does not count as first response
- does not clear attention
```

For reached/informed outcomes, the operator should answer:

```text
Still needs follow-up?
- Yes
- No
```

Rules:

- `Still needs follow-up = Yes` keeps attention active or moves it into a later follow-up state when
  that workflow exists;
- `Still needs follow-up = No` can clear business-waiting attention;
- no call outcome automatically changes request status;
- no call outcome marks a closed unresolved-feedback issue resolved.

Reason: a voicemail is a real business response, but not proof of resolution. The follow-up question
captures the operator's judgment without overclaiming.

### ADR-170 — Email/text log sent activity, not customer outcome

Email and text quick actions may be logged as outbound business contact activity, but they do not
collect customer outcome.

Rules:

- a substantive sent email/text can count as first response;
- a substantive sent email/text can clear business-waiting attention when the operator indicates it
  addressed the customer and no follow-up is needed;
- email/text logs must use sent-activity language, not outcome language;
- no `resolved`, `question answered`, or `customer handled` claims are inferred from email/text send;
- SMS sending by Keep itself remains excluded/deferred; text logging means the operator used an
  external channel and recorded it.

Reason: operators can honestly record that they sent a response. They cannot know from the send
action alone whether the customer saw it.

### ADR-171 — Operators can log inbound external customer contact

Keep must support operator-logged external customer contact, including customer calls/texts/emails
and in-person conversation.

Initial inbound channels:

```text
Customer called
Customer texted
Customer emailed
Customer spoke in person
Other customer contact
```

Recommended first payload shape:

```json
{
  "direction": "inbound",
  "channel": "sms",
  "summary": "Customer said the gate code is 4421 and asked us to use the side entrance.",
  "requiresBusinessFollowUp": true
}
```

Rules:

- inbound external contact is internal-only by default;
- it logs customer-provided information on the request;
- it should not count as business first response;
- if it requires follow-up, it sets or preserves business-waiting attention;
- if it is just recorded information, it appends internal history without raising attention;
- it may update last customer activity when it represents meaningful customer input.

Reason: many small businesses operate through customer phone/text/email threads. Keep should not
lose request context just because the customer did not use the customer page.

### ADR-172 — External contact logging is the immediate post-B5 workflow slice

B5 prepares list-level quick action metadata and placement. The authoritative external-contact
write workflow is a separate near-term slice, not vague post-foundation polish.

External-contact logging slice must implement:

```text
outbound call outcome logging
outbound email/text sent logging
inbound customer call/text/email/in-person logging
first-response effects where applicable
business-waiting attention clearing where explicitly allowed
business-waiting attention creation/preservation for inbound follow-up
internal audit/timeline events
optional operator note/summary
```

B5 must defer:

```text
call outcome persistence
email/text sent persistence
inbound external contact persistence
external-contact undo/revert
customer-visible receipts/recaps
SMS sending by Keep
notification delivery
assignment/watch writes
customer page visit telemetry
```

Reason: the list should expose and prepare the workflow, but contact logging has audit,
first-response, attention, and reversal semantics that deserve their own focused implementation.

### ADR-173 — B5 scope is the default command-center list only

B5 implements the trusted default request list. Filters, search, pagination, closed history, and
batch queues are separate post-B5/pre-go-live sessions.

B5 includes:

```text
default request list ranking
attention/movement indicators
closed unresolved-feedback surfacing for Owner/Admin
quick action metadata
contact action affordances
list-safe available actions
role visibility tests
ranking tests
```

B5 defers:

```text
filters
search
pagination
closed history browsing
SSE/list streaming
archive/history workflows
```

Reason: make the default list trustworthy first; then add ways to slice and navigate it.

### ADR-174 — Visibility, participation, and notification state are separate

B5 preserves current active-request visibility while preparing the model for assignment/watch and
notification-fatigue controls.

Definitions:

```text
Visibility = can this user see the request?
Participation = is this user responsible/watching?
Notification eligibility = may this user receive notifications?
Notification enabled = does this user currently want notifications for this request?
```

Role posture:

```text
Owner/Admin
- see all active account requests
- see closed unresolved-feedback items by default
- broad visibility does not mean automatic notification subscription

Operator
- keeps current active-request visibility in B5
- does not see closed unresolved-feedback escalation by default
- future assignment/watch phase may narrow default visibility to assigned/watching/routable work

Viewer
- read-only where permitted
- no quick actions
- notification-ineligible by default
- no closed unresolved-feedback escalation by default
```

Account posture:

```text
Off-season
- may allow read-only visibility
- suppresses request notifications account-wide
```

B5 prepares read metadata for future routing/filtering but defers watch/unwatch, mute/unmute,
per-request notification toggles, notification delivery, quiet hours, and preferences.

### ADR-175 — Default list inclusion by request status

Default B5 list includes:

```text
Received
Scheduled
InProgress
PendingCustomer
Resolved
Closed only when unresolved_feedback attention exists and user is Owner/Admin
```

Default B5 list excludes:

```text
normal Closed
Cancelled
```

Rules:

- `Resolved` remains in the default list because it is pre-terminal;
- `Resolved` with active business-waiting attention ranks by attention;
- normal `Closed` belongs to future closed-history views;
- `Cancelled` belongs to future closed/history filters and exposes no default-list quick actions.

### ADR-176 — Responsible/Watching assignment model

Keep request participation uses one active responsible owner and many watchers.

Rules:

```text
A request may have zero or one active Responsible participant.
A request may have many Watching participants.
New requests are unassigned by default unless a future account setting explicitly auto-assigns them.
Owner/Admin visibility is not assignment.
When unassigned, routing uses Owner/Admin fallback/default inbox behavior.
Responsibility transfer replaces the current Responsible participant; it does not add another Responsible.
```

Future assignment writes should enforce one active Responsible participant per request, ideally with
an application invariant plus a filtered unique index:

```text
request_id where participation_type = Responsible and detached_at_utc is null
```

B5 defers assigning, transferring, clearing responsible, watching, unwatching, muting, and auto-assign
account settings.

### ADR-177 — Exact default ranking and tie-breakers

B5 default ranking:

```text
1. Active overdue business-waiting requests
2. Active priority business-waiting requests
3. Closed unresolved feedback for Owner/Admin
4. Active standard business-waiting requests
5. Active first-response pending, not overdue
6. PendingCustomer / waiting-on-customer
7. Resolved without attention
8. Other active requests
```

Tie-breakers:

```text
attention groups:
- oldest AttentionSinceUtc first
- then priority band
- then latest customer activity
- then stable request id

first-response pending:
- soonest FirstResponseDueAtUtc first

PendingCustomer:
- latest customer activity if present
- then latest business activity

Resolved without attention:
- latest business activity desc
- then created desc

other active:
- latest customer activity
- then latest business activity
- then created desc
- then request id
```

`PendingCustomer` does not rise in the business-waiting queue unless the customer replies. A customer
reply flips waiting direction back to business and ranks by attention.

### ADR-178 — Attention indicators map reason to action prompt

B5 list indicators expose both:

```text
reason label = what happened
next-action prompt = what to do next
```

Locked initial mapping:

| Attention reason | Label | Prompt |
|---|---|---|
| `customer_message` | Reply needed | Update customer |
| `update_request` | Update requested | Update customer |
| `schedule_change_request` | Schedule change | Review schedule |
| `change_or_cancel_request` | Change/cancel request | Review request |
| `complaint` | Issue reported | Respond |
| `first_response_due` | Response overdue / New request by timing | Send first response |
| `unresolved_feedback` | Still needs help | Review feedback |

Rules:

- every attention reason must have a stable code, label, prompt, visual priority/style, and
  recommended safe quick action;
- server-side mappings should be exhaustive and tested;
- if a client receives an unknown attention code, it renders `Needs attention` + `Review request`
  and only recommends opening detail.

### ADR-179 — Movement indicators explain current rank, not rank history

B5 movement indicators explain current ranking using:

```text
reason
severity
elapsed waiting time
due/overdue timing
post-close context
waiting direction
```

B5 does not implement:

```text
up/down arrows
numeric movement deltas
rank history
new-since-last-view
customer viewed-after-update signal
```

Recommended metadata:

```text
rankingGroup
rankingReason
severity
elapsedSinceUtc
dueAtUtc
isOverdue
isPostClose
waitingDirection
```

Reason: current-state metadata keeps the UI flexible without inventing persistent rank history.

### ADR-180 — List quick contact reduces operator friction

B5 exposes a simple `Contact customer` affordance on active request rows when contact information
exists and the user can operate.

Rules:

- the operator does not need to leave Keep, search contacts, or inspect call history to find the
  customer's phone/email;
- `Contact customer` can be used for responding to customer attention or proactive business contact;
- the future external-contact logging workflow infers sensible default purpose:
  - business-waiting attention exists -> `responding_to_customer`
  - no business-waiting attention -> `initiating_business_contact`
- B5 does not require operators to classify contact purpose before contacting the customer.

Reason: Keep should make the natural field-service action easy, then make logging lightweight.

### ADR-181 — B5 list quick actions and labels

B5 list quick actions:

```text
Contact customer
Update customer
Mark handled
Open detail
Review feedback (closed unresolved feedback only)
```

`Update customer` is the list label for posting a customer-visible Keep update to the customer page.
Action code should be explicit, such as:

```text
post_customer_update
```

Rules:

- `Update customer` uses the existing business update path and does not change status by itself;
- `Contact customer` is an external contact affordance only in B5;
- `Open detail` is always available for visible rows;
- list status changes, close/cancel, assignment/watch/mute, notification toggles, and mark feedback
  reviewed are deferred.

### ADR-182 — Mark handled is secondary and frictioned

B5 exposes the existing acknowledge behavior as a secondary/overflow list action:

```text
UI label: Mark handled
Action code: acknowledge_attention
```

Eligibility:

```text
user can operate
active attention exists
not closed unresolved-feedback
not first-response-overdue with no first response
not Viewer/read-only
```

Rules:

- requires confirmation;
- requires reason;
- clearly states the customer is not notified;
- clearly states it does not count as first response;
- clears attention and appends internal audit only;
- does not update customer page, notify customer, count first response, change status, or resolve
  feedback.

Reason: operators need an escape hatch for work handled elsewhere, but silent clearing should never
compete with customer-facing actions.

### ADR-183 — B5 list is mobile-first with expandable previews

B5 list is mobile-first. Long messages should not force the operator into detail just to understand
the immediate issue.

Default row:

```text
show 2-3 lines of the most relevant customer/customer-visible message
show a "More" affordance when truncated
keep row height controlled
keep primary quick actions reachable
```

Expanded row:

```text
expands on demand to show the full message or a larger useful preview
does not show full timeline/history
does not replace detail
can collapse again
should reset naturally on navigation/refresh
```

Detail remains the place for full timeline, internal notes, feedback comment, participants, customer
page link, careful status changes, and close/cancel decisions. Closed unresolved-feedback rows show
`Still needs help` + `Review feedback`; full feedback comment remains detail-only.

### ADR-184 — B5 list summary metadata contract

B5 locks the conceptual response shape, not final C# names.

Recommended list summary metadata:

```text
identity/contact:
- requestId
- referenceCode
- customerName
- customerPhone
- customerEmail
- description

status/lifecycle:
- status
- isTerminal
- isPostCloseFollowUp

attention:
- attentionLevel
- waitingDirection
- attentionReason
- priorityBand
- attentionSinceUtc
- nextAttentionAtUtc
- firstResponseDueAtUtc
- firstRespondedAtUtc

display/ranking:
- rankingGroup
- rankingReason
- severity
- previewText
- previewSource
- previewTruncated

actions:
- quickActions[]
- contactActions[]

participation/notifications:
- participantSummary
- currentUserParticipation
- currentUserNotificationState
```

Backend sends stable codes and state metadata. Frontend owns human-facing labels/prompts, with
exhaustive mapping tests and safe unknown-code fallback.

Implementation note: B5 Session 1 may ship the preview contract fields with `previewText = null`,
`previewSource = null`, and `previewTruncated = false`. Computing message previews requires
event-derived content from `KeepRequestEvent` and is deferred to the list UX/history follow-up so
the default-list upgrade does not grow an event-loading path prematurely.

### ADR-185 — B5 includes compact read-only participation and notification metadata

B5 includes compact read-only metadata even before assignment/watch writes exist.

Participant summary:

```text
responsibleCount
watchingCount
hasResponsible
isUnassigned
```

Current user participation:

```text
type: responsible | watching | none
notificationsEnabled: true | false | null
```

Current user notification state:

```text
eligible: true | false
enabled: true | false
suppressionReason:
- viewer
- off_season
- not_participating
- muted
- no_delivery_configured
- null
```

Rules:

- visibility does not imply notifications;
- Owner/Admin broad list visibility does not automatically subscribe them to every request;
- Viewer is notification-ineligible by default;
- off-season suppresses request notifications account-wide;
- unassigned requests route later through Owner/Admin fallback/default inbox.

### ADR-186 — Closed unresolved feedback is review-only from the list

B5 closed unresolved-feedback row behavior:

```text
label: Still needs help
prompt/action: Review feedback
status context: Closed
style: post-close follow-up, not reopened active work
```

B5 list action:

```text
Review feedback -> opens request detail focused on feedback context
```

B5 does not expose these actions on closed unresolved-feedback rows:

```text
Contact customer
Update customer
Mark handled
Mark feedback reviewed
status change
```

Reason: feedback may be sensitive, comment is Owner/Admin-only, and review requires context/history.
Future feedback-review workflow should add a filtered queue, next/previous navigation, and mark
feedback reviewed.

### ADR-187 — Filters, search, and batch queues are deferred but pre-go-live

B5 default list only. The following are deferred to separate post-B5/pre-go-live sessions:

```text
search
status filters
closed history browsing
feedback review queue
next/previous detail navigation for batch review
pagination
list SSE/realtime refresh
```

B5 must preserve metadata for future filters:

```text
status
attentionReason
rankingGroup
isPostCloseFollowUp
needsFeedbackReview
isTerminal
createdAtUtc
updatedAtUtc
lastCustomerActivityAtUtc
lastBusinessActivityAtUtc
```

Pre-go-live priority:

```text
feedback review queue
search/status filters
closed history
pagination if list volume requires it
```

### ADR-188 — Archive is future closeout/history workflow

Archive is needed, but it is not part of B5.

Definitions:

```text
Closed = customer/request work is done.
Closeout reviewed = internal Owner/Admin follow-up/review is complete.
Archived = no longer shown in normal operational/history surfaces, but still retained.
History = searchable/readable retained request records.
```

B5 behavior:

```text
normal Closed excluded from default list
Cancelled excluded from default list
closed unresolved_feedback visible to Owner/Admin until reviewed/cleared by future workflow
no archive action
no unarchive action
no auto-archive
```

Future archive/closeout workflow may include manual archive/unarchive, closed-history filters,
include-archived filter, closeout reviewed state, and optional auto-archive after an age threshold.
Archive is a product/history posture, not a substitute for efficient default-list queries.

### ADR-189 — Resolved request list behavior

`Resolved` remains in the B5 default list because it is pre-terminal.

Rules:

- `Resolved` with active business-waiting attention ranks by attention;
- `Resolved` without attention ranks below waiting-on-customer and above other lower-priority active
  work only if ranking rules place it there;
- allowed list actions: `Open detail`, `Contact customer`, `Update customer`;
- `Mark handled` is available only if active attention exists and normal eligibility passes;
- `Close`, `Cancel`, status change, archive, and mark feedback reviewed are not list actions in B5.

Reason: closing is a careful lifecycle action, while resolved requests can still need final customer
communication or customer reply handling.

### ADR-190 — PendingCustomer list behavior

`PendingCustomer` means the business is waiting on the customer.

Rules:

- included in the default list;
- lower priority than business-waiting attention;
- shown as `Waiting on customer`;
- does not rise in the business-waiting queue;
- allowed list actions: `Contact customer`, `Update customer`, `Open detail`;
- `Update customer` posts another customer-visible update/nudge, does not change status, does not
  clear attention by default, and keeps the request waiting on the customer;
- `Mark handled` is unavailable unless active business-waiting attention exists and normal
  eligibility passes;
- if the customer replies, waiting direction flips to business, attention is raised, and the
  request ranks by attention.

### ADR-191 — List actions must communicate their effects

B5 list actions must communicate what they do.

Every quick action should expose effect metadata/copy:

```text
is customer-visible?
is internal-only?
is external contact affordance only?
does it clear attention?
does it count as first response?
does it change status?
does request status remain unchanged?
```

Recommended metadata:

```text
quickActions[]:
- code
- label
- visibility: customer_visible | internal | external_affordance
- clearsAttention: true | false
- countsFirstResponse: true | false
- changesStatus: true | false
- effectSummaryCode
```

Reason: operators must understand whether `Update customer`, `Contact customer`, or `Mark handled`
will clear attention, count as first response, notify the customer, or leave status unchanged.

### ADR-192 — Calm attention design and first-response behavior

B5 uses list order as the primary urgency signal. Visual indicators support scanning, not shouting.

Use:

```text
reason chip
short action prompt
waiting/due time
subtle severity treatment
section grouping
```

Avoid:

```text
excessive warning icons
stacked red badges
numeric urgency scores
loud treatment for routine messages
```

Strong alert styling is reserved for job/trust-risk items:

```text
response overdue
complaint / issue reported
schedule change
change/cancel request
closed unresolved feedback
```

First-response behavior:

```text
No first response yet, not overdue:
- included above quiet active work
- label: New request
- supporting text: Response due in Xm/Xh
- actions: Contact customer, Update customer, Open detail
- calm active styling

No first response yet, overdue:
- high priority attention group
- label: Response overdue
- prompt: Send first response
- actions: Contact customer, Update customer, Open detail
- Mark handled unavailable
- strong styling used sparingly
```

Reason: first response is job/trust-risk, but not every new request should visually scream before it
is overdue.

### ADR-193 — Operator completion and Owner/Admin closeout are separate

Product language separates completion from closeout:

```text
Operator-facing language: Mark completed
Backend status: Resolved

Owner/Admin closeout language: Close request
Backend status: Closed
```

Default posture:

- Operators can operate active work and mark completed/resolved;
- Owner/Admin close requests by default;
- Closed enables customer feedback;
- B5 does not add list-level close, cancel, resolve, or status-change actions;
- B5 does not auto-complete or auto-close stale requests.

Future pre-go-live closeout tools should include:

```text
Ready to close queue
Needs feedback review queue
Needs status check queue
Closed yesterday / this week filters
Next/previous detail navigation
Close and next
```

`Ready to close` must exclude requests with active attention. `Cancel` remains careful detail-only
with no first batch shortcut unless a later product decision changes it.

---

## Implementation Guidance For B5

## Session 2 External Contact Discovery Checkpoint

**Date:** 2026-06-17
**Status:** Decisions ADR-196..218 locked. Ready for implementation handoff review.

### ADR-196 — External contact logs are structured internal request events

Session 2 stores external contact logs as first-class `KeepRequestEvent` rows, not a separate
external-contact table.

Rules:

- add an `ExternalContactLogged` event type;
- event visibility is internal-only in Session 2;
- add structured nullable external-contact fields for direction, channel, outcome, follow-up,
  first-response effect, and attention-clearing effect;
- use event `Content` for the internal summary/note;
- preserve actor and timestamp fields so later list/detail projections can render who did what
  when;
- external contact mutates `KeepRequest` through domain methods and the event remains the durable
  audit/timeline record.

Reason: request events are Foundation's canonical request audit trail. Structured fields avoid
guessing from free text and keep later mobile list previews possible without changing the model.

### ADR-197 — One external-contact endpoint and service

Session 2 exposes one authenticated operator endpoint:

```text
POST /keep/requests/{requestId}/external-contact
```

Rules:

- one `LogExternalContactService` handles inbound and outbound contact;
- the request body is structured by direction, channel, outcome, follow-up need, and summary;
- backend validation rejects invalid combinations before domain mutation;
- backend derives first-response and attention effects instead of trusting client-provided state
  flags.

Reason: the product action is "log external contact." Splitting the API by phone/text/email would
bake UI workflow into the backend too early.

### ADR-198 — External contact state-effect matrix

External contact logging is primarily team memory/audit for customer communication that happened
outside Keep. Quick contact launch alone is not durable contact evidence.

State effects:

| Contact type | Counts first response? | Can clear eligible attention? | Can raise/preserve attention? |
|---|---:|---:|---:|
| Outbound phone: spoke with customer | Yes | Yes, when no follow-up is needed | No |
| Outbound phone: left voicemail | Yes | Yes, when no follow-up is needed | No |
| Outbound phone: no answer | No | No | No |
| Outbound phone: wrong number | No | No | No |
| Outbound email sent | Yes | Yes, when no follow-up is needed | No |
| Outbound text sent | Yes | Yes, when no follow-up is needed | No |
| Inbound customer contact | No | No | Yes, when follow-up is required |

Rules:

- logged contact is internal by default;
- external contact never changes lifecycle status;
- external contact never updates the customer page automatically;
- customer-visible recap remains a separate explicit business update/status action;
- no external contact resolves closed unresolved feedback.

Reason: offline customer communication should count inside Keep without confusing internal audit
with customer-visible communication.

### ADR-199 — Summary requirements balance speed and team memory

Session 2 keeps outbound call logging low-friction.

Rules:

- outbound phone outcomes can be logged without a required note;
- outbound no-answer and wrong-number must be especially fast to log;
- outbound email/text sent logs require a summary of what was sent;
- inbound external customer contact always requires a summary because it captures customer-provided
  context for the team;
- all summaries are internal-only in Session 2.

Reason: requiring notes for every call attempt discourages logging, but inbound contact without a
summary does not give the team useful memory.

### ADR-200 — External contact is non-terminal only

Session 2 allows external contact logging only on non-terminal requests.

Rules:

- `Received`, `Scheduled`, `InProgress`, `PendingCustomer`, and `Resolved` may log external
  contact;
- `Closed` and `Cancelled` reject external contact logs;
- internal notes remain available on terminal requests;
- external contact never reopens requests and never resolves closed unresolved feedback.

Reason: terminal feedback/contact follow-up needs the later feedback-review or closeout workflow,
not the first external-contact logging slice.

### ADR-201 — Detail timeline first; future list previews preserved

Session 2 exposes external contact logs in request detail timeline metadata and updates request
state fields that affect list ranking. It does not add default-list recent-activity previews, row
expansion, or event loading.

Rules:

- `KeepRequestDetailResult` events expose structured external-contact fields;
- the default list changes only indirectly through first-response, attention, and activity
  timestamp mutations;
- external-contact event data must be structured and actor/time-stamped so a later mobile
  list UX/history slice can show compact last 1-2 activity previews, including who did what when,
  without changing the contact-log model.

Reason: detail is the full workbench today, but mobile operators will likely need list-level recent
activity after pilot feedback. Session 2 should not build that UI, but it must not block it.

### ADR-202 — External-contact write returns request detail

`POST /keep/requests/{requestId}/external-contact` returns `KeepRequestDetailResult`.

Rules:

- response includes the new internal timeline event;
- response includes updated request state/action metadata;
- Session 2 does not return undo metadata and does not implement contact-log revert.

Reason: this matches existing operator write endpoints and lets the UI refresh detail immediately.

### ADR-203 — Explicit external-contact direction/outcome enums and stable API codes

Session 2 adds explicit external-contact enums for direction and call outcome. It reuses existing
`CommunicationChannel` for contact channel because that enum already has phone, SMS, email,
in-person, and other, and `KeepRequestEvent.CommunicationChannel` already anticipates externally
logged contact events.

Recommended API codes:

```text
direction: outbound | inbound
channel: phone | sms | email | in_person | other
outcome: spoke_with_customer | left_voicemail | no_answer | wrong_number
```

Rules:

- EF stores enum values as strings;
- outcomes are valid only for outbound phone contact;
- outbound contact initially supports phone, SMS, and email;
- inbound contact supports phone, SMS, email, in-person, and other;
- invalid combinations fail before domain mutation.

Reason: stable codes protect clients while domain enums keep magic strings out of business logic.
Reusing `CommunicationChannel` avoids a duplicate channel enum.

### ADR-204 — Inbound follow-up uses response-policy timing

Inbound external contact with `requiresBusinessFollowUp=true` raises or preserves
business-waiting attention using the same response-policy timing rules as customer messages.

Rules:

- standard priority by default;
- update last customer activity;
- do not count as business first response;
- if already business-waiting, preserve the oldest attention start;
- if waiting on customer, flip to business-waiting from the inbound contact time;
- future priority/intent workflow may add upgrades, but Session 2 does not.

Reason: inbound phone/text/email/in-person contact is customer activity from an operational
standpoint, even though it is not customer-page activity.

### ADR-205 — Logged contact updates existing activity timestamps by direction

When external contact is logged in Keep, activity timestamps update by direction.

Rules:

- all logged outbound contact attempts update `LastBusinessActivityAt`;
- logged inbound customer contact updates `LastCustomerActivityAt`;
- timestamps describe logged activity only and do not imply every off-platform call/text/email was
  captured;
- timestamps do not imply first response, attention clearing, status change, or customer-visible
  page updates.

Reason: existing activity fields are sufficient for freshness and tie-breakers in Session 2.

### ADR-206 — No undo, revert, or mistake flag in Session 2

Session 2 external contact logs are append-only.

Rules:

- no undo/revert action;
- no mistake/ignore marker;
- mistakes can be corrected with internal notes;
- future mistake markers may visually mark logs without reversing state effects;
- future true revert must use compensating events and define first-response, attention, activity,
  and projection semantics explicitly.

Reason: reversing contact logs is deceptively complex because logs can affect first response,
attention, activity timestamps, and list ranking.

### ADR-207 — Contact-specific validation errors

Session 2 uses explicit contact-specific validation errors for external-contact request bodies.

Rules:

- add errors for invalid direction, channel, outcome, outcome-not-allowed, missing summary, and
  missing follow-up answers;
- reuse existing auth, permission, account-access, feature, not-found, and terminal-state errors;
- validation fails before domain mutation and before any event is persisted.

Reason: clients need field-specific feedback, and tests need precise failure assertions.

### ADR-208 — OffSeason is frozen/read-mostly Keep posture

OffSeason is a frozen/read-mostly Keep posture, not an active workflow mode.

Rules:

- normal role-based read access remains available for list, detail, timeline, customer/request data,
  and future exports;
- normal operator writes are blocked, including status changes, business updates, internal notes,
  acknowledge attention, external contact logging, and future assignment/watch/mute writes;
- customer writes are blocked, including customer-page messages and feedback submission;
- background workflow processing, outbound notifications, email, and SMS are blocked for Keep;
- existing requests remain in their current status when OffSeason begins and are not auto-closed;
- Owner/Admin may perform a narrow closeout action for remaining non-terminal requests, with an
  internal audit reason and no customer-visible update or notification;
- public intake links remain reachable but switch to a read-only unavailable response instead of
  404;
- OffSeason public intake does not accept new requests, create customer/request records, send
  notifications, or start background workflows;
- the unavailable intake response may show business-approved fallback phone/email/message/social
  links when configured, but fallback contact settings and preview are deferred.

Reason: OffSeason should preserve access to records while limiting active workflow usage and OpHalo
operating cost. Leaving public intake links reachable avoids a broken customer experience for links
shared in email, social media, and websites, while blocking new work intake.

### ADR-209 — External-contact authorization follows operator-write rules and OffSeason freeze

External-contact logging uses the existing operator-write authorization stack, with the OffSeason
freeze from ADR-208 applied.

Rules:

- authenticated current user required;
- active account membership required;
- `Keep.RequestsOperate` permission required;
- `Keep.OperatorQueue` feature required;
- request lookup is account-scoped;
- Viewer/read-only users cannot log contact;
- blocked account access still blocks the write;
- OffSeason blocks external-contact logging because all normal Keep writes are frozen there.

Reason: external contact can mutate first response, attention, and activity state, so it is an
operator write. It should not diverge from the global Keep write policy.

### ADR-210 — External contact uses log time only in Session 2

External contact events use the existing `OccurredAtUtc` as the time the contact log is created in
Keep.

Rules:

- Session 2 does not add `ExternalContactOccurredAtUtc`;
- Session 2 does not add `ExternalContactSource`;
- future analytics/backdating/provenance may add occurred-time and source fields together if pilot
  usage shows a need;
- Session 2 must not pretend Keep knows when an off-platform contact happened unless it was logged
  in Keep.

Reason: separate real-world occurred time is useful only with source/provenance and validation
rules. Adding it alone would create misleading analytics.

### ADR-211 — One application service, two domain methods

Session 2 uses one endpoint/application service and two domain methods on `KeepRequest`.

Recommended domain methods:

```text
LogOutboundExternalContact(...)
LogInboundExternalContact(...)
```

Rules:

- one API command/service remains `LogExternalContactService`;
- outbound and inbound domain behavior is split because their invariants differ;
- outbound can count first response and clear eligible attention;
- inbound cannot count first response and can raise/preserve business-waiting attention.

Reason: separate domain methods keep validation and side effects explicit while preserving one
client-facing command.

### ADR-212 — External contact source/provenance is deferred

Session 2 does not distinguish Keep-initiated contact from manually logged contact.

Rules:

- both flows create the same `ExternalContactLogged` event after operator confirmation;
- no `keep_initiated` / `manual_log` source field is added in Session 2;
- future native-app/analytics work may add `ExternalContactSource` together with occurred-time and
  provenance rules.

Reason: quick contact launch alone is not evidence. Source becomes useful when native launch/return
instrumentation and analytics semantics are designed together.

### ADR-213 — External contact can become first-response evidence

When an external contact log counts as the first business response, the request first-response fields
point to that `ExternalContactLogged` event.

Rules:

- set `FirstRespondedAtUtc`, `FirstResponderAccountUserId`, and `FirstResponseEventId` from the
  contact event when the contact counts as first response;
- only set these fields when `Origin == Customer` and no first response is already recorded;
- no-answer, wrong-number, inbound contact, business-origin requests, and already-first-responded
  requests do not update first-response fields.

Reason: first response should remain traceable to the exact event that satisfied it.

### ADR-214 — External contact attention clear reason is stable code

When outbound external contact clears active eligible attention, the request stores a stable internal
attention clear reason.

Rules:

- `AttentionClearReason = "external_contact_no_follow_up"`;
- channel, outcome, and summary remain on the `ExternalContactLogged` event;
- no-answer, wrong-number, inbound contact, and contact that does not clear attention do not set an
  attention clear reason.

Reason: the clear reason should explain why attention cleared without duplicating structured event
metadata.

### ADR-215 — Detail timeline exposes nullable external-contact metadata

Request detail timeline events expose nullable external-contact fields on the existing event DTO.

Fields:

```text
externalContactDirection
externalContactChannel
externalContactOutcome
externalContactRequiresFollowUp
externalContactCountsFirstResponse
externalContactClearedAttention
```

Rules:

- non-contact events return null for these fields;
- external-contact API values use stable lowercase codes;
- customer page timelines continue to exclude internal external-contact logs.

Reason: this follows the existing event DTO pattern of nullable event-specific metadata while keeping
customer pages clean.

### ADR-216 — External-contact request body shape

External-contact request body uses five fields:

```json
{
  "direction": "outbound",
  "channel": "phone",
  "outcome": "spoke_with_customer",
  "requiresBusinessFollowUp": false,
  "summary": "Confirmed tomorrow 9-11 arrival window."
}
```

Rules:

- `direction` and `channel` are required;
- `outcome` is required only for outbound phone and rejected otherwise;
- `requiresBusinessFollowUp` is required for inbound, outbound SMS/email, and outbound phone
  spoke/voicemail;
- `requiresBusinessFollowUp` is rejected for no-answer/wrong-number and should be represented as
  null/not applicable in domain/event data rather than false;
- `summary` is required for inbound and outbound SMS/email;
- `summary` is optional for outbound phone;
- summary max length is 4000 characters.

Reason: one compact body supports all contact cases while validation keeps impossible combinations
out. Summary requirements are domain invariants as well as API validation rules.

### ADR-217 — Session 2 migration scope is event fields only

Session 2 migration scope is limited to external-contact event support.

Rules:

- add `ExternalContactLogged` event type support;
- add structured nullable external-contact fields on `KeepRequestEvent`;
- do not add a separate contact table;
- do not add occurred/source timestamp fields;
- do not add mistake/revert fields;
- do not add list preview fields;
- do not add denormalized last-contact columns;
- OffSeason freeze should use existing account operating-mode data unless implementation proves a
  schema gap.

Reason: the schema should support the locked workflow without pulling in deferred analytics,
preview, source/provenance, or undo work.

### ADR-218 — Session 2 test gate

Session 2 completion requires focused domain/unit tests and integration tests.

Required coverage:

- external-contact effect matrix;
- endpoint auth, permission, feature, account access, cross-account not-found, and OffSeason
  blocking;
- request-body validation and contact-specific errors;
- event persistence with internal visibility and structured fields;
- detail response metadata;
- customer-page exclusion for internal external-contact logs;
- OffSeason public-intake unavailable behavior;
- full suite pass if feasible;
- docs/session-log records exact tests run.

Reason: external contact touches state, audit, public boundaries, and account-mode policy, so the
completion gate must cover behavior and not only endpoint happy paths.

Recommended B5 read-contract additions:

```text
AttentionLevel
WaitingDirection
AttentionReason
PriorityBand
AttentionSinceUtc
NextAttentionAtUtc
FirstResponseDueAtUtc
FirstRespondedAtUtc
FeedbackWasResolved
FeedbackSubmittedAtUtc
ContactActions
QuickActions / AvailableActions
```

Recommended quick action metadata:

```text
contact_customer
post_customer_update
acknowledge_attention
open_detail
review_feedback
```

Rules:

- `contact_customer` in B5 is a contact affordance only unless the external-contact write exists;
- `post_customer_update` uses existing customer-visible business update behavior and may clear
  business-waiting attention through that existing path;
- `acknowledge_attention` is secondary, labeled `Mark handled`, and requires the existing
  acknowledge reason;
- status changes remain detail-first;
- B5 implementation should not pull in deferred write workflows.
