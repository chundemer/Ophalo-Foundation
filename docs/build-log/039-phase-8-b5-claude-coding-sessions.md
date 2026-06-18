# Build Log 039 — Phase 8-B5 Claude Coding Sessions

**Date:** 2026-06-17
**Purpose:** Claude-ready implementation sessions after B5 decisions.
**Decision source:** `038-phase-8-b5-request-list-triage-external-contact-decisions.md` · `040-phase-8-b5-session-3-assignment-watch-mute-decisions.md`
**ADRs covered:** 164..235
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

## Session 2C — OffSeason Freeze + Public Intake Unavailable — COMPLETE

**Tests:** 620 total (355 unit · 14 arch · 251 integration)
**ADR:** ADR-221

**What was built:**
- `RequestImplementsAllowedInOffSeason: false` on all 4 operator write services; each also gains `|| decision.IsReadOnly` on the blocked check
- `LogExternalContactService` (from 2B) also fixed: only had `IsBlocked` check despite `false` flag
- `GetKeepRequestDetailService`: `canWrite = canOperate && !isOffSeason`; all 5 write-action flags use `canWrite`
- `KeepPublicCustomerContext`: added `bool IsOffSeason`
- `KeepPublicCustomerAccessGuard`: populates `IsOffSeason`; comment updated to ADR-208
- `AddCustomerMessageService` + `SubmitFeedbackService`: check `context.IsOffSeason` → `KeepRequest.OffSeasonUnavailable` → 409
- `KeepRequestErrors.OffSeasonUnavailable` + `ErrorHttpMapper` 409 entry
- `KeepOffSeasonTests.cs`: 11 integration tests (reads pass, all writes blocked, customer 409, intake 422)

**Key implementation note:** `EnterOffSeason` requires `CommercialState.Active`. Integration tests transition Trial → PastDue (`MarkPastDue`) → Active (`ResolvePastDue`) before calling `EnterOffSeason`.

**Deferred:** Owner/Admin narrow closeout in OffSeason → DEF-042

---

## Session 2C — OffSeason Freeze + Public Intake Unavailable (spec)

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

## Session 3A — Assignment/Watch/Mute Domain + Persistence

**Goal:** Add the participation domain behavior, event metadata, persistence support, and invariants
without adding API endpoints yet.

Decision/deferred refs:

```text
ADR-174, ADR-176, ADR-185, ADR-222..235
DEF-012, DEF-033
docs/build-log/040-phase-8-b5-session-3-assignment-watch-mute-decisions.md
```

Scope:

- Add `ParticipationChanged` event support with structured metadata:
  - `participationAction`
  - `targetAccountUserId`
  - target display-name snapshot if local event patterns support snapshots
  - previous/new responsible ids where applicable
  - optional internal note where applicable
  - notification-intent metadata where applicable
- Add domain behavior on `KeepRequest` or the existing participation aggregate/service boundary for:
  - assign responsible
  - transfer responsible
  - clear responsible
  - add watcher
  - remove watcher
  - self watch/unwatch if domain-owned
  - mute/unmute if domain-owned
- Enforce:
  - one active Responsible per request
  - one active participation row per user per request
  - Responsible and Watching are mutually exclusive
  - transfer detaches the previous Responsible, does not auto-watch them
  - clear Responsible detaches, does not auto-watch
  - watch defaults `NotificationsEnabled = true`
  - mute requires active participation and only flips `NotificationsEnabled`
  - idempotent no-ops do not create duplicate events or duplicate notification intent
- Add EF mapping/migration for any new event fields and filtered unique index/invariant support.
- Keep existing `KeepRequestParticipant` table/model unless implementation proves a schema gap.

Out of scope:

- API endpoints/application services.
- Request detail/list DTO updates.
- Notification delivery/outbox/device-token work.
- Operator Unassigned/Available queue.
- Automatic cleanup of stale participants.

Required tests:

- Domain/unit tests for assignment, transfer, clear, watch, unwatch, mute, unmute.
- One-active-Responsible invariant.
- One-active-participation-row-per-user invariant.
- Responsible/Watching mutual exclusion.
- Clear/transfer does not auto-watch.
- Watch and assignment default notifications enabled.
- Mute/unmute preserves participation and does not hide/detach.
- Idempotent no-ops do not append duplicate events or notification intent.
- Terminal requests reject participation writes at the domain/service boundary chosen for the slice.
- Migration/model snapshot inspected for only locked schema/index additions.

Completion gate:

```text
targeted domain/unit tests pass
migration inspected
docs/session-log updated with files/tests
```

---

## Session 3B — Participation API/Application Services + Candidate Lookup

**Goal:** Expose participation write endpoints and the compact eligible participant lookup.

Decision/deferred refs:

```text
ADR-222..235
DEF-012, DEF-033
docs/build-log/040-phase-8-b5-session-3-assignment-watch-mute-decisions.md
```

Scope:

- Add explicit endpoints:
  - `PUT /keep/requests/{requestId}/responsible`
  - `DELETE /keep/requests/{requestId}/responsible`
  - `PUT /keep/requests/{requestId}/watchers/{accountUserId}`
  - `DELETE /keep/requests/{requestId}/watchers/{accountUserId}`
  - `PUT /keep/requests/{requestId}/watch`
  - `DELETE /keep/requests/{requestId}/watch`
  - `PUT /keep/requests/{requestId}/mute`
  - `DELETE /keep/requests/{requestId}/mute`
- Add request bodies:
  - responsible `PUT`: `{ "accountUserId": "...", "note": "optional internal note" }`
  - responsible `DELETE`: optional `{ "note": "optional internal note" }` for Owner/Admin managed
    clearing
  - watcher add/remove routes: optional `{ "note": "optional internal note" }` for Owner/Admin
    managed watcher changes
  - self watch/unwatch/mute/unmute routes: no note body in Session 3
- Add or reuse compact candidate lookup:
  - preferred: `GET /keep/requests/participant-candidates`
  - returns `accountUserId`, `displayName`, `role`
  - returns Active Owner/Admin/Operator only
  - excludes Viewer, Invited, Suspended, Removed, other accounts, private contact details, and
    notification preferences.
- Authorization:
  - Owner/Admin can assign, transfer, clear, add/remove watchers.
  - Operators can self-assign only unassigned/effectively unassigned requests they can already
    access through an allowed surface.
  - Operators can self-watch/self-unwatch visible non-terminal requests.
  - Operators can self-mute/self-unmute only when actively Responsible or Watching.
  - Operators cannot clear themselves as Responsible.
  - Viewer cannot mutate participation.
- Validate eligible target users on every write.
- Apply OffSeason/read-only write blocking consistently with ADR-208/221.
- Return updated `KeepRequestDetailResult` from all write endpoints.

Out of scope:

- Push/email/in-app notification delivery.
- Notification delivery/outbox table.
- Operator Unassigned/Available queue or filters.
- List-level clear responsible.
- Owner/Admin delegated mute/unmute for other users.

Required tests:

- Endpoint auth required.
- Owner/Admin assignment, transfer, clear, add watcher, remove watcher.
- Owner/Admin list-compatible assignment uses same responsible endpoint.
- Operator self-assign allowed only for unassigned/effectively unassigned accessible request.
- Operator cannot assign others, transfer, clear self, or clear others.
- Operator self-watch/unwatch visible non-terminal request.
- Responsible user cannot unwatch out of responsibility.
- Mute/unmute requires active current-user participation.
- Viewer cannot mutate.
- Ineligible users cannot be assigned/watched.
- OffSeason blocks all participation writes.
- Terminal requests reject participation writes.
- Candidate lookup returns only eligible users and no private contact details.
- Idempotent no-ops return detail and do not duplicate timeline/intent.

Completion gate:

```text
targeted API/application tests pass
candidate lookup tests pass
docs/session-log updated with files/tests
```

---

## Session 3C — Participation Read Models, Timeline, and List Assignment Metadata

**Goal:** Make request detail/list surfaces truthful after participation writes without adding extra
list controls.

Decision/deferred refs:

```text
ADR-222..235
DEF-012, DEF-033
docs/build-log/040-phase-8-b5-session-3-assignment-watch-mute-decisions.md
```

Scope:

- Update request detail result to expose:
  - current responsible user
  - watchers
  - current-user participation
  - current-user notification enabled/muted state
  - stale responsible indicator for Owner/Admin
  - participation action availability if consistent with existing detail action metadata style.
- Update detail timeline DTO to render internal `ParticipationChanged` metadata.
- Ensure participation events remain internal-only and are excluded from customer-page timelines.
- Update default list read model to show responsible person name.
- Keep list watcher display compact:
  - retain B5 compact metadata if present;
  - do not add full watcher lists to rows.
- Add Owner/Admin list assignment metadata if needed by the frontend:
  - list can show assign/transfer affordance for Owner/Admin only;
  - no list-level clear responsible;
  - no list-level watch/unwatch/mute/unmute.
- Represent stale/ineligible Responsible:
  - Owner/Admin can see stored responsible name plus stale/ineligible state;
  - stale Responsible does not count as effective routing or notification eligibility.

Out of scope:

- Frontend implementation unless explicitly requested.
- List-level clear responsible.
- Full watcher lists in default rows.
- Operator Unassigned/Available queue.
- List SSE/realtime refresh.
- Customer-visible participation events.

Required tests:

- Detail shows updated responsible, watchers, current-user notification state.
- Detail shows stale responsible indicator for Owner/Admin.
- List shows responsible person name.
- List stays read-only for Operators and does not expose participation write actions.
- Owner/Admin list metadata allows assignment/transfer only where eligible.
- Customer page excludes participation events.
- Participation writes do not update customer/business activity, attention, first response, status, or
  ranking except through participation metadata.

Completion gate:

```text
targeted read-model/timeline tests pass
request-list tests remain green
customer-page boundary tests remain green
docs/session-log updated with files/tests
```

---

## Session 3D — Assignment/Watch/Mute Completion Gate

**Goal:** Verify cross-slice behavior, update docs, and close the Session 3 handoff.

Decision/deferred refs:

```text
ADR-222..235
DEF-012, DEF-033
docs/build-log/040-phase-8-b5-session-3-assignment-watch-mute-decisions.md
```

Scope:

- Run full participation semantic matrix across domain, API, detail, list, and customer-page
  boundaries.
- Verify no deferred work was pulled in:
  - no notification delivery/outbox/device-token work;
  - no quiet hours/global preferences;
  - no Operator Unassigned/Available queue;
  - no list-level clear/watch/mute controls;
  - no customer-visible participation timeline;
  - no automatic stale-participant cleanup.
- Verify OffSeason/read-only write blocking.
- Verify migration/model snapshot and full suite if feasible.
- Update:
  - `docs/session-log.md`
  - `docs/deferred-topics.md`
  - `docs/decisions/decision-index.md`

Required tests:

- Full targeted Session 3 test set passes.
- Existing request-list suite remains green.
- Existing request-detail/customer-page tests remain green.
- Full suite passes if feasible.

Completion gate:

```text
all targeted tests pass
full suite pass if feasible
decision index/deferred topics/session log updated
```

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
