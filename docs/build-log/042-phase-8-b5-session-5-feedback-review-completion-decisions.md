# Build Log 042 — Phase 8-B5 Session 5 Feedback Review Completion Decisions

**Phase:** 8-B5 Session 5 discovery / pre-implementation gate
**Date:** 2026-06-18
**Status:** Decisions locked. Ready to split into bounded Claude coding sessions.
**Build log preceding this:** `041-phase-8-b5-session-4-filters-search-closed-history-pagination-decisions.md`
**V1 carry-forward lock:** `043-keep-v1-product-scope-and-freshness-lock.md`
**ADRs locked:** 261..286
**Current global next free ADR after Session 4 implementation/V1 locks:** ADR-295

---

## Purpose

Session 5 completes the post-close unresolved-feedback loop. Session 4 added an Owner/Admin
`feedback_review` queue and focused row context, but deliberately deferred the write that completes
review. Session 5 adds that foundation while keeping the lifecycle simple:

```text
Resolved = business believes the work is complete, but the request is still active/pre-close.
Closed = Owner/Admin finalizes active work and enables one-time customer feedback.
Negative closed feedback = post-close Owner/Admin review, not reopened work.
```

The key boundary is:

```text
Session 5 is foundation/services/API only.
It provides UI-ready contracts and server-owned workflow rules.
It does not implement web UI, native mobile UI, notification delivery, analytics, reopen, or rework.
V1 basic push/badges are a later pre-go-live slice, not part of Session 5. Mark reviewed sends no
notification.
```

---

## Locked Decisions

### ADR-261 — Session 5 excludes active complaint/issue workflow changes

Active customer issue reporting remains the existing active/pre-close customer message flow.

Rules:

- customer-facing active action remains `issue`, not a new `complaint` surface;
- `/issue` continues to map internally to complaint-priority attention;
- active complaint/issue workflow changes, management-only complaint mode, and new complaint
  visibility rules are out of Session 5;
- Session 5 focuses only on post-close unresolved feedback review completion.

Reason: active complaint triage and post-close feedback review are related but distinct workflows.

### ADR-262 — Feedback is one-time and Closed-only for v1

Customer feedback remains available only after a request is `Closed`.

Rules:

- `Resolved` remains active/pre-close and continues to support normal customer messages/issues;
- `Closed` opens the one-time feedback action when the customer page is unexpired and feedback has
  not already been submitted;
- each request may receive at most one feedback submission total, positive or negative;
- closing a request enables feedback but does not send an automatic feedback prompt.

Reason: keep `Resolved` as operational completion still open to communication, and `Closed` as the
final lifecycle point where resolution feedback belongs.

### ADR-263 — Positive feedback is stored; negative feedback creates review work

Closed-request feedback may be positive or negative.

Rules:

- positive feedback (`FeedbackWasResolved = true`) is stored and visible in authenticated
  request detail/history for users who may view the request;
- positive feedback creates no attention and never enters the feedback review queue;
- negative feedback (`FeedbackWasResolved = false`) stores the original feedback and creates
  `UnresolvedFeedback` post-close attention;
- full negative feedback comments remain Owner/Admin-only under the existing B4 visibility rule;
- lower roles receive only safe feedback metadata where existing request visibility allows it.

Reason: positive feedback is history; negative feedback is an operational recovery signal.

### ADR-264 — Only Owner/Admin can mark feedback reviewed

`Mark feedback reviewed` is an Owner/Admin-only action.

Rules:

- Operators and Viewers cannot mark feedback reviewed;
- this remains true even if an Operator was Responsible, Watching, or otherwise participated in the
  request;
- Operators may help with follow-up through separate workflows, but clearing the management review
  queue is Owner/Admin responsibility.

Reason: post-close unresolved feedback is management-sensitive and may reflect service quality,
customer satisfaction, or business follow-up after close.

### ADR-265 — Mark feedback reviewed is internal acknowledgement only

Marking feedback reviewed means internal management has reviewed the post-close negative feedback.

Rules:

- does not reopen the request;
- does not change `Status = Closed`;
- does not delete, edit, or hide the original feedback;
- does not notify the customer;
- does not count as resolving the customer's issue;
- does not overwrite `FeedbackWasResolved = false`;
- does not create positive feedback.

Reason: review completion clears an internal obligation without rewriting what the customer said.

### ADR-266 — Unresolved feedback never auto-clears

Post-close unresolved feedback remains in Owner/Admin review surfaces until an Owner/Admin marks it
reviewed.

Rules:

- no automatic timeout clears negative feedback review;
- aging may affect labels and urgency metadata only;
- reviewed feedback may later be archived under separate archive/closeout decisions, but review is
  required before it leaves operational review queues.

Reason: the product should not silently bury explicit customer dissatisfaction.

### ADR-267 — Mark reviewed clears only unresolved-feedback review attention

Marking feedback reviewed clears the operational `UnresolvedFeedback` review state.

Rules:

- set reviewed metadata;
- clear attention only when the current attention is specifically `UnresolvedFeedback`;
- remove the row from the Owner/Admin default post-close follow-up surface;
- remove the row from `feedback_review` and `viewCounts.feedback_review`;
- preserve the request in `closed_history`;
- preserve the original feedback and review metadata in detail/history.

Reason: the review action must complete the review queue item without accidentally clearing unrelated
future attention.

### ADR-268 — Feedback review persists simple request-level metadata

Feedback review state is stored directly on the request.

Fields:

```text
FeedbackReviewedAtUtc
FeedbackReviewedByAccountUserId
FeedbackReviewNote
```

Rules:

- `FeedbackReviewedAtUtc` is nullable until reviewed;
- `FeedbackReviewedByAccountUserId` references the Owner/Admin actor;
- `FeedbackReviewNote` is optional, internal-only, trimmed, nullable, and capped at 2000 characters;
- do not modify original feedback fields;
- do not add a separate feedback review status enum in Session 5.

Reason: null reviewed timestamp cleanly represents unreviewed state; original feedback and internal
review acknowledgement remain separate.

### ADR-269 — Mark reviewed creates an internal request history event

Marking feedback reviewed creates an internal-only request history/timeline event attached to the
request.

Rules:

- record actor, timestamp, and optional note;
- keep the event available for historical reference;
- include enough metadata/content for audit without requiring the request-level note field alone;
- never show the event on the customer page;
- do not notify anyone from this event in Session 5.

Reason: clearing a management queue item needs an audit trail.

### ADR-270 — Optional review note, no required note

`Mark feedback reviewed` may include an optional internal note.

Rules:

- blank/missing note is valid;
- non-blank note is trimmed and capped at 2000 characters;
- note is internal-only and never customer-visible;
- note is distinct from the original customer feedback comment.

Reason: optional notes support useful context without forcing low-value "reviewed" text.

### ADR-271 — Reviewed feedback is collapsed in history by default

Unreviewed feedback is prominent in feedback review surfaces. Reviewed feedback remains visible in
closed history/detail as compact historical context.

Rules:

- unreviewed negative feedback is prominent in focused feedback-review detail;
- reviewed feedback should appear as a compact collapsed summary by default in closed history/detail;
- Owner/Admin can expand to view full feedback/review details;
- existing role visibility rules still apply.

Reason: reviewed negative feedback should remain honest historical context without dominating every
closed request view.

### ADR-272 — No undo/reopen-review in Session 5

Session 5 does not add undo or reopen-feedback-review.

Rules:

- mark reviewed is append-only for pilot;
- if reviewed by mistake, the original feedback and review record remain visible;
- Owner/Admin may add an internal note for correction/context;
- restoring review attention is deferred.

Reason: undo reintroduces queue/count/attention semantics and should wait for real correction needs.

### ADR-273 — Mark reviewed is valid only for eligible review state

The mark-reviewed command is valid only when the request is in unresolved-feedback review state.

Eligible:

```text
Closed
Feedback submitted
FeedbackWasResolved = false
not already reviewed
current unresolved-feedback review state active
```

Rejected:

```text
positive feedback
missing feedback
non-Closed requests
Cancelled requests
already-reviewed feedback
missing unresolved-feedback review state
```

Reason: the command should not duplicate, revive, or reinterpret feedback state.

### ADR-274 — Feedback review API endpoint

Session 5 adds a request-scoped command endpoint:

```text
POST /keep/requests/{requestId}/feedback-review
```

Body:

```json
{
  "note": "Called customer and scheduled follow-up."
}
```

Rules:

- note is optional;
- endpoint returns updated `KeepRequestDetailResult`;
- no separate review-and-next mutation endpoint is added;
- clients may implement "Mark reviewed and next" by calling this endpoint and navigating with
  current queue context.

Reason: this matches existing Keep write-service/API style and keeps one mutation path.

### ADR-275 — Feedback review is single-shot

Mark feedback reviewed is not idempotent-success for duplicates.

Rules:

- if already reviewed, return conflict;
- do not overwrite reviewer/timestamp/note;
- do not create a second timeline event;
- clients should refresh to show current reviewed state.

Reason: audit should preserve the single actor and timestamp that completed review.

### ADR-276 — Feedback review errors are explicit

Feedback review uses explicit errors.

Recommended errors:

```text
404 KeepRequest.NotFound
403 KeepRequest.Forbidden
403 KeepRequest.ReadOnly
409 KeepRequest.FeedbackReviewUnavailable
409 KeepRequest.FeedbackAlreadyReviewed
400 KeepRequest.FeedbackReviewNoteTooLong
```

Rules:

- invalid business states collapse to `FeedbackReviewUnavailable` except already-reviewed;
- note length gets a validation-specific error;
- access/read-only guards follow existing API conventions.

Reason: clients need predictable messages without a bespoke error for every invalid state.

### ADR-277 — OffSeason blocks customer feedback and review writes

OffSeason/read-only mode blocks feedback writes.

Rules:

- `POST /keep/r/{pageToken}/feedback` is blocked in OffSeason/read-only mode;
- safe read-only customer page access remains allowed where already supported;
- closed customer pages do not expose `feedback` in `AllowedActions` during OffSeason/read-only;
- `POST /keep/requests/{requestId}/feedback-review` is also blocked in OffSeason/read-only;
- existing unreviewed feedback remains visible as read-only Owner/Admin context and becomes
  actionable again when writes are enabled.

Reason: OffSeason should not create new operational feedback work or clear internal review queues.

### ADR-278 — OffSeason entry should warn about unresolved work

Future OffSeason entry should surface a pre-flight checklist/warning for unresolved feedback and
other open Keep work.

Rules:

- Session 5 does not implement OffSeason activation UI or block activation;
- unresolved feedback should be called out before switching to read-only mode;
- warning should explain that unreviewed feedback remains visible but locked during OffSeason.

Reason: businesses should be encouraged to clean up before read-only mode without being trapped.

### ADR-279 — Feedback review context includes aging metadata

Feedback review exposes server-computed aging metadata for unreviewed negative feedback.

Pilot defaults:

```text
new:      less than 24 hours
aging:    24 to 72 hours
overdue:  more than 72 hours
```

Rules:

- thresholds must be centralized in constants/options or a small policy method;
- later they may move to business preference settings;
- API returns `feedbackReviewAgeBucket` and `feedbackReviewDueAtUtc`;
- aging affects labels/sort urgency only;
- no auto-clear, status change, or notification.

Reason: Owner/Admin need urgency cues, but the policy should remain easy to evolve.

### ADR-280 — Feedback review surfaces include action context

Owner/Admin feedback review surfaces include enough immediate context to act.

Include:

- customer contact context and contact action availability;
- responsible person;
- participant involvement summary;
- original feedback summary/full detail according to surface and role;
- aging metadata;
- closed/request context.

Rules:

- full customer/address history summaries remain deferred;
- focused feedback detail may reserve future compact history space;
- contact actions do not mark feedback reviewed or clear attention;
- actual direct contact remains separately logged through external-contact workflows.

Reason: reviewers need "who do I call, who worked this, what happened?" without turning the queue row
into a full history/search surface.

### ADR-281 — Backend owns feedback review UI-ready metadata

Session 5 keeps feedback-review workflow logic out of web/native UI clients.

Rules:

- backend exposes server-computed review context, visibility, aging, participant/contact context, and
  available actions;
- list rows keep `review_feedback` as the open/focused-review action;
- detail exposes `mark_feedback_reviewed` only when the write is currently allowed;
- clients render returned actions/metadata and do not infer eligibility from raw fields.

Reason: foundations/services/API should own workflow rules so web and native clients can plug in.

### ADR-282 — Queue navigation is context-based, not bulk review

Session 5 supports efficient one-at-a-time review through list/query context.

Rules:

- opening from `view=feedback_review` can carry queue context;
- clients may offer next/previous navigation within the current result/query context;
- clients may offer "Mark reviewed and next" using the same mark-reviewed endpoint;
- no bulk mark-reviewed action;
- no separate heavy batch workflow.

Reason: feedback should be reviewed in detail, but Owner/Admin should not have to keep returning to
the list after every item.

### ADR-283 — No customer-visible feedback follow-up in Session 5

Session 5 does not add customer-visible replies, receipts, or feedback follow-up messages.

Rules:

- direct customer follow-up may happen outside Keep and be logged through existing external-contact
  workflows;
- no "we reviewed your feedback" message is sent;
- no customer page conversation is reopened by feedback review;
- no email/SMS/push delivery is added.

Reason: customer-visible follow-up touches expiry, terminal page permissions, delivery, and reopen
semantics, so it belongs to a later slice.

### ADR-284 — No reopen, callback/rework, or linked requests in Session 5

Session 5 does not add request reopen from feedback review or a formal callback/rework model.

Rules:

- negative feedback never reopens automatically;
- Owner/Admin cannot reopen from feedback review in Session 5;
- if follow-up work is needed, Owner/Admin may manually create a new request and record context in
  internal notes for now;
- formal linked callback/rework requests are deferred.

Reason: reopen and linked rework affect lifecycle, visibility, reporting, assignment, and customer
page semantics; manual new-request plus notes is the safe v1 path.

### ADR-285 — No analytics or notification delivery in Session 5

Session 5 stores clean review metadata but does not build analytics or delivery.

Out of scope:

```text
CSAT/NPS
feedback dashboards
public review prompts
automated campaigns
time-to-review reporting UI
notification delivery
push/email/SMS/in-app notifications
```

Reason: the first release needs a solid operational foundation before insights and notification
products are added.

### ADR-286 — Session 5 implementation must follow existing code standards

Session 5 implementation must follow the current Keep architecture and coding practice.

Required patterns:

- domain-owned state changes;
- application services for command behavior;
- explicit API command endpoint;
- `Result<T>` and `KeepRequestErrors` style errors;
- existing OffSeason account access policy checks with write services blocked;
- write endpoints return updated `KeepRequestDetailResult`;
- internal-only timeline event patterns;
- server-computed action metadata;
- customer page excludes internal events and review metadata;
- focused integration tests matching existing API test style.

Required verification:

- implementation handoff must include tests for eligibility, roles, OffSeason blocking, duplicate
  review conflict, optional note validation, attention/queue clearing, internal history event
  creation, customer-page non-exposure, and no status/participation side effects;
- coding sessions should inspect existing Session 1-4 code paths before editing;
- coding sessions should self-review and report discovered issues/bugs.

Reason: Session 5 should extend the existing foundation, not introduce a parallel style.

---

## Implementation Handoff

### Suggested coding split

```text
5A — Domain, schema, persistence, and aging policy
5B — Mark-feedback-reviewed service/API/detail contract
5C — List/customer-page OffSeason and UI-ready metadata integration
5D — Integration verification, docs, deferred tracker, and implementation self-review
```

### Recommended build order after Session 4 completion

Session 4D completed with `803/803` tests passing and confirmed no Session 5 code exists yet. Build
Session 5 in this order:

1. **5A first** because every later slice needs the durable fields, domain invariant, migration,
   event shape, and aging policy.
2. **5B second** because it turns the domain foundation into the write path and detail response
   contract.
3. **5C third** because list/customer-page behavior depends on the new reviewed state and write
   action metadata from 5A/5B.
4. **5D last** as the integration and documentation gate. It should run broad tests, check the
   Session 5 boundary, update ADR statuses, and record any implementation-discovered issues.

Do not skip the final self-review gate. Claude should explicitly report any bug, inconsistency, or
surprising codebase pattern it finds, even if it fixes the issue in the same session.

### Session 5A pre-implementation gate clarifications

Claude's 5A pre-flight questions are answered as follows.

**D1 — Eligibility requires active `UnresolvedFeedback` attention.**

Confirmed. Implement the ADR-273 literal eligibility check for `MarkFeedbackReviewed`:

```text
Status == Closed
FeedbackSubmittedAtUtc.HasValue
FeedbackWasResolved == false
!FeedbackReviewedAtUtc.HasValue
AttentionLevel != None
AttentionReason == UnresolvedFeedback
```

If an Owner/Admin first uses `AcknowledgeAttention` on a closed unresolved-feedback request, feedback
review should fail as `FeedbackReviewUnavailable` because the active review state has already been
cleared. This is acceptable for pilot and should be flagged in 5B/5D self-review as an interaction
to watch, not "fixed" by weakening the Session 5 eligibility rule.

**D2 — Feedback review aging policy lives in Keep.Core.**

Confirmed. Add a pure core policy class such as `FeedbackReviewPolicy` under
`src/OpHalo.Keep.Core/Domain/` with centralized pilot thresholds and methods:

```text
New < 24h
Aging 24h..72h
Overdue > 72h
ComputeAgeBucket(submittedAtUtc, nowUtc)
ComputeReviewDueAtUtc(submittedAtUtc)
```

Add `FeedbackReviewAgeBucket` under `src/OpHalo.Keep.Core/Entities/Enums/`. Keep this independent
of Application so future account preference settings can feed the policy without moving the core
bucket semantics.

**D3 — Feedback reviewed event uses `Content` for the optional note.**

Confirmed. Add `FeedbackReviewed = 11` to `KeepRequestEventType` and add a
`CreateFeedbackReviewed` factory on `KeepRequestEvent`.

Rules:

- `Visibility = Internal`;
- `ActorType = AccountUser`;
- `Content = trimmed note` when a note is present;
- `Content = null` when no note is provided;
- no new event-table columns for the review note in 5A;
- customer-page mappers must never expose this internal event.

This differs from `CreateInternalNote` only in that feedback-review note content is optional.

### Claude prompt for 5A

```text
You are implementing Phase 8-B5 Session 5A in the OpHalo foundation repo.

Before coding, read:
- docs/build-log/042-phase-8-b5-session-5-feedback-review-completion-decisions.md
- docs/build-log/045-phase-8-b5-session-4d-integration-verification.md
- docs/build-log/035-phase-8-b3-plus-feedback-implementation.md
- docs/build-log/037-phase-8-b4-service-request-detail-enrichment-implementation.md
- docs/build-log/040-phase-8-b5-session-3-assignment-watch-mute-decisions.md
- docs/build-log/041-phase-8-b5-session-4-filters-search-closed-history-pagination-decisions.md

Review the current code patterns in:
- KeepRequest, KeepRequestEvent, KeepRequestErrors
- KeepRequestConfiguration / migrations
- EfKeepRequestDetailPersistence / EfKeepRequestOperatePersistence
- existing external-contact and participation event patterns

Implement only foundation pieces for feedback review:
- request-level fields FeedbackReviewedAtUtc, FeedbackReviewedByAccountUserId, FeedbackReviewNote
- EF configuration and migration
- domain method/invariants for marking negative closed feedback reviewed
- internal-only history event support using existing event style
- centralized feedback review aging policy/constants/method
- the 5A pre-implementation gate clarifications in this build log:
  - active UnresolvedFeedback attention is required for eligibility
  - FeedbackReviewPolicy lives in Keep.Core/Domain
  - FeedbackReviewed event uses Content for the optional note and adds no new event columns

Keep code style consistent with the existing application. Do not implement web/native UI,
notifications, analytics, reopen, or callback/rework.

Run targeted unit/build checks that fit this slice. Self-review your diff and report any issues,
risks, or bugs you discover.
```

### Claude prompt for 5B

```text
You are implementing Phase 8-B5 Session 5B in the OpHalo foundation repo.

Before coding, read:
- docs/build-log/042-phase-8-b5-session-5-feedback-review-completion-decisions.md
- docs/build-log/045-phase-8-b5-session-4d-integration-verification.md
- the Session 5A diff/current code
- existing services: LogExternalContactService, AcknowledgeAttentionService, ManageResponsibleService,
  GetKeepRequestDetailService
- API endpoint style in src/OpHalo.Api/Program.cs and error mapping style

Implement the mark-feedback-reviewed application/API path:
- command/request DTO for optional note
- POST /keep/requests/{requestId}/feedback-review
- Owner/Admin-only authorization
- OffSeason/read-only blocked using existing account access policy style
- validation/errors: FeedbackReviewUnavailable, FeedbackAlreadyReviewed,
  FeedbackReviewNoteTooLong, read-only/forbidden/not-found as appropriate
- clear only current UnresolvedFeedback attention
- return updated KeepRequestDetailResult
- expose detail metadata/action for mark_feedback_reviewed only when allowed
- ensure customer page never exposes review metadata/event/note

Follow existing service, Result<T>, mapper, and integration-test patterns. Do not add separate
review-and-next endpoint, bulk review, notifications, reopen, or callback/rework.

Run targeted API/integration tests for this slice if feasible. Self-review your work and report any
issues, risks, or bugs you discovered.
```

### Claude prompt for 5C

```text
You are implementing Phase 8-B5 Session 5C in the OpHalo foundation repo.

Before coding, read:
- docs/build-log/042-phase-8-b5-session-5-feedback-review-completion-decisions.md
- docs/build-log/045-phase-8-b5-session-4d-integration-verification.md
- Session 4 list code: GetKeepRequestListService, KeepRequestSummary, GetKeepRequestListResult,
  list persistence/query code
- customer feedback code: SubmitFeedbackService, KeepCustomerPageMapper, customer-page tests
- OffSeason tests and access-policy patterns

Implement UI-ready metadata integration:
- feedback_review rows/counts exclude reviewed feedback
- reviewed negative feedback leaves operational queues but remains in closed_history/detail
- row action remains review_feedback for focused open; detail action is mark_feedback_reviewed
- age bucket and due timestamp are server-computed for unreviewed negative feedback
- Owner/Admin feedback review context includes customer contact context, responsible person, and
  participant involvement summary where existing contracts support it
- positive feedback is stored/history only and never enters feedback_review
- OffSeason customer page GET omits feedback from AllowedActions
- OffSeason POST /keep/r/{pageToken}/feedback is blocked server-side

Keep the backend as the source of truth; do not push eligibility logic into clients. Do not implement
web/native UI.

Run focused list/customer-page/API tests. Self-review your diff and report any issues, risks, or bugs
you discovered.
```

### Claude prompt for 5D

```text
You are completing Phase 8-B5 Session 5D verification and docs.

Before editing, read:
- docs/build-log/042-phase-8-b5-session-5-feedback-review-completion-decisions.md
- docs/build-log/045-phase-8-b5-session-4d-integration-verification.md
- docs/decisions/decision-index.md
- docs/deferred-topics.md
- docs/build-log/039-phase-8-b5-claude-coding-sessions.md
- all Session 5 implementation diffs

Do a codebase consistency review against Session 1-4 patterns:
- service shapes and Result<T> errors
- OffSeason access checks
- mapper/action metadata conventions
- event visibility and customer-page exclusion
- integration test style
- migration/configuration style

Add or finish focused integration tests covering:
- Owner/Admin success
- Operator/Viewer forbidden
- OffSeason blocks mark-reviewed
- existing unreviewed feedback visible read-only in OffSeason
- OffSeason customer feedback submit blocked and AllowedActions excludes feedback
- duplicate review conflict
- positive/missing/non-Closed/Cancelled invalid states
- note stored and note-too-long rejected
- attention/queue/count clearing
- original feedback remains in detail/history
- internal-only history event created
- customer page does not expose review metadata/note/event
- status/assignment/participation unchanged

Update docs only if implementation changed exact details. Run the most relevant test suite feasible,
then self-review and report any issues/bugs discovered plus exact commands run.
```

---

## Deferred / Still Out Of Scope

- active complaint/issue workflow changes;
- customer-visible feedback replies or receipts;
- feedback prompt notification delivery;
- analytics/CSAT/NPS/review prompts;
- reopen from feedback review;
- formal callback/rework linked requests;
- expired/no-feedback customer page check-in reminders;
- full customer/address history summaries;
- archive/unarchive/auto-archive;
- OffSeason activation UI/checklist implementation.
