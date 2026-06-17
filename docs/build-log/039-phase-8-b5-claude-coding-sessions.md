# Build Log 039 — Phase 8-B5 Claude Coding Sessions

**Date:** 2026-06-17
**Purpose:** Claude-ready implementation sessions after B5 decisions.
**Decision source:** `038-phase-8-b5-request-list-triage-external-contact-decisions.md`
**ADRs covered:** 164..218
**Deferred tracker:** `docs/deferred-topics.md`

---

## How To Use This File

Each session is intentionally bounded. Do not hand Claude "build B5 and everything after it" as one
task. Finish and verify one session before starting the next. If a session discovers schema needs
not described here, pause and update decisions before coding.

---

## Session 1 — B5 Default Command-Center List

**Goal:** Upgrade `GET /keep/requests` into the default command-center list.

Decision refs:

```text
ADR-164, ADR-165, ADR-166, ADR-173, ADR-174, ADR-175, ADR-177, ADR-178,
ADR-179, ADR-183, ADR-184, ADR-185, ADR-186, ADR-189, ADR-190,
ADR-191, ADR-192
```

Scope:

- Keep default list only. No filters/search/pagination/closed history.
- Include active statuses: `Received`, `Scheduled`, `InProgress`, `PendingCustomer`, `Resolved`.
- Include closed unresolved-feedback rows for Owner/Admin only.
- Exclude normal `Closed` and all `Cancelled`.
- Add deterministic ranking groups and tie-breakers.
- Add attention/ranking/display metadata.
- Add compact read-only participant and current-user notification metadata.
- Add quick action metadata:
  - `contact_customer`
  - `post_customer_update`
  - `acknowledge_attention`
  - `open_detail`
  - `review_feedback`
- Add action-effect metadata so UI can explain customer-visible/internal/external-affordance,
  clears attention, counts first response, changes status, and status unchanged.
- Add mobile-first preview contract fields: preview text/source/truncated. In Session 1 these may
  be `null` / `false` because event-derived preview content is deferred to a later list UX/history
  slice.
- Add exhaustive attention mapping tests and safe unknown-client fallback guidance in result docs.

Confirmed implementation answers:

- Rename the persistence method away from `GetOpenRequestsAsync` if helpful, for example
  `GetDefaultListRequestsAsync(accountId, includeClosedUnresolvedFeedback, ct)`.
- Use a single role-aware persistence call with `includeClosedUnresolvedFeedback`; do not add a
  separate closed-feedback query in Session 1.
- Filter candidate rows in persistence, but perform B5 ranking in memory after loading the bounded
  candidate set.
- Keep `GetKeepRequestListResult(IReadOnlyList<KeepRequestSummary> Requests)` unchanged; no list
  meta/pagination in B5.
- Prefer nested/grouped summary records over a very wide flat `KeepRequestSummary`.
- Do not batch-load `KeepRequestEvent` rows in Session 1 just for message previews.

Out of scope:

- external contact logging writes;
- assignment/watch writes;
- notification delivery;
- notification toggle writes;
- filters/search/pagination;
- closed history;
- feedback review queue;
- closeout queue;
- event-derived preview text computation;
- archive;
- status changes from list;
- frontend implementation unless explicitly requested.

Recommended code areas:

```text
src/OpHalo.Keep.Application/Requests/GetKeepRequestListService.cs
src/OpHalo.Keep.Application/Requests/GetKeepRequestListResult.cs
src/OpHalo.Keep.Application/Requests/KeepRequestSummary.cs
src/OpHalo.Keep.Application/Requests/IKeepRequestListPersistence.cs
src/OpHalo.Keep.Infrastructure/Persistence/KeepRequestListPersistence.cs
tests/OpHalo.UnitTests/Keep/KeepRequestListServiceTests.cs
tests/OpHalo.IntegrationTests/Api/... request-list tests
```

Required tests:

- Owner/Admin see all active requests plus closed unresolved feedback.
- Operator does not see closed unresolved feedback by default.
- Viewer/read-only receives no quick actions and notification-ineligible metadata.
- Off-season/read-only posture suppresses notification eligibility while allowing reads when policy
  allows reads.
- Cancelled and normal Closed are excluded.
- Resolved is included and ranks according to attention/non-attention state.
- PendingCustomer ranks below business-waiting and exposes correct action effects.
- First-response pending/overdue metadata and Mark handled eligibility.
- Each attention reason maps explicitly.
- Closed unresolved-feedback row exposes only `review_feedback`.
- Ranking tie-breakers are deterministic.

Completion gate:

```text
unit tests pass
integration tests pass for request-list behavior
full test suite pass if feasible
docs/session-log updated with exact tests and file changes
```

---

## Session 2A — External Contact Schema + Domain

**Goal:** Add the external-contact event model, migration, and `KeepRequest` domain behavior.

Decision/deferred refs:

```text
ADR-167..172, ADR-196, ADR-198, ADR-199, ADR-200, ADR-203, ADR-204,
ADR-205, ADR-206, ADR-210, ADR-211, ADR-212, ADR-213, ADR-214, ADR-217
DEF-018, DEF-031, DEF-032, DEF-041, DEF-044
```

Scope:

- Add external-contact enums:
  - direction: outbound/inbound
  - outcome: spoke with customer/left voicemail/no answer/wrong number
- Reuse existing `CommunicationChannel` for phone/SMS/email/in-person/other.
- Add `ExternalContactLogged` event support.
- Add structured nullable external-contact fields to `KeepRequestEvent`.
- Add EF configuration and migration.
- Add event factory for internal external-contact logs.
- Add `KeepRequest.LogOutboundExternalContact(...)`.
- Add `KeepRequest.LogInboundExternalContact(...)`.
- Enforce summary requirements in the domain methods, not only in the later API layer.
- Use nullable `requiresBusinessFollowUp` in domain/event data so no-answer/wrong-number can be
  represented as not applicable rather than false.
- Apply the state-effect matrix:
  - spoke/voicemail/email/text sent can count first response;
  - no-answer/wrong-number log only;
  - inbound does not count first response;
  - inbound follow-up raises/preserves business-waiting attention;
  - eligible outbound contact can clear attention when no follow-up is needed.
- Set `FirstResponseEventId` to the `ExternalContactLogged` event when external contact counts as
  first response.
- Use `AttentionClearReason = "external_contact_no_follow_up"` when contact clears attention.
- Use existing `OccurredAtUtc` as log time; do not add occurred/source/provenance fields.

Out of scope:

- API endpoint/service.
- Request detail response DTO updates.
- OffSeason account/public-intake behavior.
- List previews or last-contact denormalization.
- Undo/revert/mistake flag.

Required tests:

- Domain/unit tests for every row in the effect matrix.
- First-response event linkage tests.
- Attention clear reason tests.
- Non-terminal vs terminal request domain behavior.
- Migration/model snapshot generated and inspected.

Completion gate:

```text
unit tests for domain behavior pass
migration generated with only the locked schema additions
docs/session-log updated with files/tests
```

---

## Session 2B — External Contact API + Detail Timeline

**Goal:** Add the operator endpoint/application service and expose contact logs in request detail.

Decision/deferred refs:

```text
ADR-197, ADR-199, ADR-200, ADR-202, ADR-203, ADR-207, ADR-209,
ADR-211, ADR-215, ADR-216
```

Scope:

- Add `POST /keep/requests/{requestId}/external-contact`.
- Add request body:
  - `direction`
  - `channel`
  - `outcome`
  - `requiresBusinessFollowUp`
  - `summary`
- Add contact-specific validation and error codes.
- Add `LogExternalContactService`.
- Use existing operator-write authorization stack.
- Block Viewer/read-only users.
- Block terminal requests.
- Return `KeepRequestDetailResult`.
- Add nullable external-contact metadata fields to detail timeline event DTO.
- Ensure customer page timelines exclude internal external-contact logs.

Out of scope:

- OffSeason policy changes beyond preserving a clear integration point for ADR-208.
- Public intake OffSeason unavailable behavior.
- List recent-activity previews.
- Customer-visible recap/update shortcut.
- Native app launch/source/provenance.

Required tests:

- Endpoint requires auth.
- Viewer cannot log contact.
- Owner/Admin/Operator with operate permission can log contact on non-terminal requests.
- Cross-account request returns not found.
- Invalid direction/channel/outcome combinations fail with contact-specific errors.
- Summary/follow-up validation rules are enforced.
- Event persists with internal visibility and structured fields.
- Detail response includes new event metadata.
- Customer page excludes internal external-contact logs.

Completion gate:

```text
targeted unit/integration tests pass
docs/session-log updated with files/tests
```

---

## Session 2C — OffSeason Freeze + Public Intake Unavailable

**Goal:** Align Keep write/public-intake behavior with the OffSeason frozen/read-mostly posture.

Decision/deferred refs:

```text
ADR-208, ADR-209
DEF-042, DEF-043
```

Scope:

- Update account-access/write policy usage so normal Keep writes are blocked in OffSeason.
- Ensure external-contact logging is blocked in OffSeason.
- Preserve normal role-based reads in OffSeason.
- Keep public intake links reachable in OffSeason.
- Return unavailable/read-only public intake response instead of 404.
- Block public-intake POST creation in OffSeason.
- Ensure no customer/request records, notifications, or background workflows start from OffSeason
  public intake.
- Add the narrow Owner/Admin closeout action only if it is already supported by existing status
  mechanics without expanding scope; otherwise document as follow-up inside DEF-042.

Out of scope:

- OffSeason fallback contact settings UI.
- Custom unavailable message/social links/reopen date.
- Export workflow.
- Batch closeout/archive tools.

Required tests:

- Existing list/detail reads still work in OffSeason for permitted roles.
- Operator/customer writes are blocked in OffSeason.
- External-contact endpoint returns forbidden/conflict according to existing account-access mapping.
- Public intake GET/token resolution returns unavailable response in OffSeason.
- Public intake POST does not create request/customer records in OffSeason.
- Non-OffSeason intake/write behavior remains green.

Completion gate:

```text
OffSeason-focused integration tests pass
affected existing integration tests updated intentionally
docs/session-log updated with files/tests
```

---

## Session 2D — External Contact Completion Gate

**Goal:** Finish cross-slice verification, docs, and handoff after Sessions 2A-2C.

Decision/deferred refs:

```text
ADR-196..218
DEF-018, DEF-031, DEF-032, DEF-040, DEF-041, DEF-042, DEF-043, DEF-044
```

Scope:

- Run the complete external-contact semantic matrix across domain and API where applicable.
- Verify no deferred work was pulled in:
  - no customer-visible recap;
  - no SMS delivery by Keep;
  - no notification delivery;
  - no customer page telemetry;
  - no assignment/watch writes;
  - no list recent-activity previews;
  - no undo/revert/mistake flag;
  - no source/provenance/occurred-time fields.
- Verify request list behavior still changes only through request state fields.
- Verify migration/model snapshot and full suite if feasible.
- Update `docs/session-log.md` with exact files changed and tests run.

Required tests:

- Full request-list suite remains green.
- Full request-detail/customer-page boundary tests remain green.
- Full external-contact tests pass.
- Full suite pass if feasible.

Completion gate:

```text
all targeted tests pass
full suite pass if feasible
decision index/deferred topics/session log updated
```

---

## Session 3 — Assignment / Watching / Per-Request Notification Controls

**Goal:** Add request routing writes and per-request notification participation controls.

Decision/deferred refs:

```text
ADR-174, ADR-176, ADR-185
DEF-012, DEF-033
```

Scope:

- Assign responsible.
- Transfer responsible.
- Clear responsible/unassign.
- Watch/unwatch.
- Mute/unmute per-request notifications.
- Enforce zero-or-one active Responsible participant.
- Keep many Watching participants.
- Preserve Owner/Admin visibility separate from assignment.
- Add filtered unique index or equivalent invariant for one active Responsible.

Out of scope:

- notification delivery;
- quiet hours/global preferences;
- operator visibility narrowing unless explicitly decided for that session.

Tests:

- new request may remain unassigned;
- assignment creates one active Responsible;
- transfer replaces active Responsible;
- watcher count and current-user participation metadata update;
- notification enabled state respects participant settings;
- Viewer remains notification-ineligible.

---

## Session 4 — Filters, Search, Closed History, Pagination

**Goal:** Add list navigation after the default command-center list is trusted.

Decision/deferred refs:

```text
ADR-173, ADR-187
DEF-034
```

Scope:

- Status filters.
- Search.
- Closed history browsing.
- Pagination/caps/cursors where needed.
- Optional list SSE/realtime only if list invalidation needs it and a separate decision confirms it.

Out of scope:

- feedback review queue behavior;
- closeout queue behavior;
- archive;
- external-contact writes.

Notes:

- Preserve default list behavior from Session 1.
- Search should not load unbounded event history.
- Closed history should not reintroduce normal closed rows into the default view.

---

## Session 5 — Feedback Review Queue

**Goal:** Make Owner/Admin closed unresolved-feedback review efficient.

Decision/deferred refs:

```text
ADR-165, ADR-186, ADR-187
DEF-029, DEF-035
```

Scope:

- Needs feedback review filter/queue.
- Detail opens focused on feedback context.
- Next/previous navigation within queue.
- Mark feedback reviewed action.
- Store reviewer/timestamp.

Rules:

- does not reopen request;
- does not delete feedback;
- does not hide feedback from detail;
- does not notify customer;
- does not count as resolving the customer's issue;
- clears from default Owner/Admin feedback-review surface.

Out of scope:

- customer-visible feedback replies/receipts;
- analytics/reviews/CSAT.

---

## Session 6 — Ready-To-Close + Stale Status Check

**Goal:** Add efficient closeout and request hygiene workflows before go-live.

Decision/deferred refs:

```text
ADR-188, ADR-193
DEF-028, DEF-036, DEF-037
```

Scope:

- Ready to close queue for `Resolved` requests with no active attention.
- Detail next/previous navigation.
- Close and next.
- Closed yesterday / this week filters.
- Needs status check queue for stale active requests.

Rules:

- Operator-facing "Mark completed" maps to `Resolved`.
- Owner/Admin "Close request" maps to `Closed`.
- `Closed` enables customer feedback.
- Ready to close excludes active attention.
- Cancel remains careful detail-only.
- No auto-complete or auto-close stale requests.

Out of scope:

- archive/unarchive unless added by a separate decision;
- automated closeout.

---

## Session 7 — Archive / History Cleanup

**Goal:** Add long-term history posture after closed history and closeout are in place.

Decision/deferred refs:

```text
ADR-188
DEF-028
```

Scope candidates:

- manual archive/unarchive;
- include archived filter;
- optional auto-archive after age threshold;
- retention/search posture.

Rules:

- archived records are retained;
- archive hides from normal operational/history surfaces only;
- do not archive active unresolved feedback or unreviewed negative feedback.

This session should be revisited after real closed-history and closeout behavior are implemented.
