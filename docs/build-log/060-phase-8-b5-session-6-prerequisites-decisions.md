# Build Log 060 — Phase 8-B5 Session 6 Prerequisites Decisions

**Date:** 2026-06-23  
**Status:** Prerequisite decisions locked; ready for prerequisite implementation slices  
**Baseline:** 1265 tests (677 unit · 14 architecture · 574 integration) from G8d final green gate  
**ADRs locked here:** ADR-337..ADR-344; next free ADR-345  

## Purpose

Lock the workflow semantics that must exist before Phase 8-B5 Session 6 closeout/stale work can be
implemented safely. The discussion exposed that ready-to-close and stale/status-check depend on
Follow Up On, Planned For, customer intent, customer-page usage, and notification boundaries.

Session 6 proper is paused until these prerequisites are implemented or explicitly scoped.

## Source Inputs

- Original Session 6 outline: `docs/build-log/039-phase-8-b5-claude-coding-sessions.md`
- Pre-Session-6 gap audit and final gate: `docs/build-log/047-pre-session-6-phase-7-session-5-gap-audit.md`
- Current execution brief: `docs/session-log.md`
- Deferred topics: DEF-030, DEF-036, DEF-037, DEF-063, DEF-067
- ADR carry-forwards: ADR-088, ADR-121..127, ADR-162, ADR-188, ADR-193, ADR-282, ADR-292, ADR-297
- G4/G5/G8 guardrails: row/action authorization, optimistic concurrency, token/rate-limit safety

## Locked Decisions

### ADR-337 — Follow Up On is active-request communication follow-up, not scheduling

Add staff-owned Follow Up On for existing active Keep requests only. It is not a general callback
tool for leads, new jobs, or unrelated tasks.

Rules:

- V1 uses a date, not a specific time.
- Owner/Admin can set it; Operators can set it when they have operate access to the request.
- Reason set: weather, parts, customer delay, business/operator availability, third party, other.
- Internal note is optional except `other`, which requires a note.
- Follow Up On does not change lifecycle status and does not notify the customer automatically.
- UI must make explicit customer update/contact easy from the flow, using normal auditable
  update/contact paths.
- While future Follow Up On exists, suppress stale/status-check noise and show scan-friendly row
  markers such as `Follow up Fri`, `Parts`, or `Weather`.
- On/after the date, surface follow-up attention such as `Follow up today` / `Follow-up overdue`;
  staff can extend, update/contact customer, change status, or resolve.

### ADR-338 — Planned For is lightweight internal timing context, not dispatch

Add staff-owned Planned For for active Keep requests.

Rules:

- V1 uses a date only.
- Optional and internal/business-owned by default.
- Customer cannot edit it.
- It does not book, confirm, dispatch, reserve availability, or become a calendar feature.
- It supports row scan context and attention priority: `Planned today`, `Planned tomorrow`,
  `Planned Fri`, `Planned date passed`.
- If the planned date passes and the request is still active/unresolved, it can feed status-check
  review.
- Planned date changes do not automatically notify the customer; UI should make explicit customer
  updates easy when staff wants to communicate the change.

### ADR-339 — Needs-status-check is policy-driven and signal-extensible

Needs status check is a quiet human-review queue, not an automatic action.

Rules:

- Threshold is account policy, with pilot default of 5 calendar days when unset.
- Active requests only; exclude `Resolved`, `Closed`, and `Cancelled`.
- Exclude active business attention, future Follow Up On, and future Planned For.
- Centralize latest-meaningful-activity calculation rather than scattering `UpdatedAtUtc` checks.
- Initial meaningful signals: request created, customer message/intent, business update, external
  contact, status change, Follow Up On set/changed/cleared, Planned For set/changed/cleared.
- Keep the model easy to extend with future signals such as customer page viewed, viewed after
  latest business update, attachments, integrations, callback/rework links, and notification state.
- No full signal/projection engine or reporting platform in this prerequisite slice.
- No auto-close, auto-resolve, or automatic customer update.

### ADR-340 — Notifications are immediate-attention only; badges and ranked lists carry the rest

Do not copy the reference app's polling-first notification posture.

Rules:

- Push notifications are reserved for immediate or time-sensitive attention.
- Badges/counts represent actionable backlog.
- The server-ranked request list remains the main prioritization surface once a user opens Keep.
- Notification delivery is event/state-driven after durable request state is recorded; delivery is
  fail-soft and never rolls back the underlying action.
- Foreground freshness may use refetch-after-write, pull-to-refresh, focus/resume sync, and light
  foreground polling if needed.
- Off-screen awareness uses native push/APNs/FCM and server-derived badges.
- Push payloads are minimal and deep-link to API-refreshed state.
- Full notification preference matrices, quiet hours, delivery analytics, and broad notification
  reporting remain later work.
- Push-worthy by default: new customer request needing first response, customer message/intent
  needing business response, timing/cancel/issue/call-me intent especially when planned soon, Follow
  Up On due today for responsible/watching users, negative feedback to Owner/Admin, and assignment
  or responsibility changed to you when action is expected.
- Badge/list by default: ready to close, needs status check, planned date passed, future follow-up
  set, normal internal notes, closed history, and generic list movement.

### ADR-341 — Customer page viewed is a pre-pilot confidence signal, not a read receipt

Pull customer-page usage tracking forward before pilot.

Rules:

- Use the reference app for lessons and useful implementation ideas.
- Purpose: help the business know whether customers are using the request page; help operators
  decide whether to rely on page updates or call/text/email; help OpHalo measure customer-page
  adoption.
- Expose cautious staff-facing metadata such as `Last viewed`, `Viewed after latest update`, and
  `Never viewed`.
- Do not present it as presence, "customer is online", exact surveillance, or proof that every
  message was read.
- Debounce/rate-limit page views so refreshes do not spam telemetry.
- Page views do not raise business attention by themselves.
- Page views become available to future latest-meaningful-activity logic, but should not suppress
  stale/status-check by themselves without a later decision.

### ADR-342 — Customer page actions use customer-language intents and shared event handling

Keep the reference app's intent-coded lesson, but simplify the customer-facing menu.

V1 customer actions:

- Ask a question.
- Ask for an update.
- Send info.
- Call me.
- Need to change timing.
- Need to cancel?

Rules:

- Each action opens a lightweight message composer and maps to a stable intent code.
- All actions use one shared customer-message/event implementation.
- Suggested intent codes: `question`, `update_request`, `information_added`, `call_requested`,
  `timing_change_requested`, `cancellation_requested`.
- No customer action directly changes status, cancels, schedules, reschedules, closes, or sets Follow
  Up On.
- Business remains responsible for workflow decisions.
- `Need to cancel?` is available but secondary/less promoted and should require or strongly prompt
  for a note/confirmation.
- `Call me` is push-worthy by default; `Send info` is usually badge/list unless context makes it
  urgent; `Need to change timing` priority can depend on Planned For context.

### ADR-343 — Close permission is account-policy-shaped with Owner/Admin default

Ready-to-close remains Session 6 proper, but close permission must be shaped now.

Rules:

- Operators mark work complete by moving it to `Resolved`.
- Default close permission is Owner/Admin only.
- Account policy may later allow Operators to close requests they can operate / are responsible for.
- Full settings UI is not required immediately, but backend design must not block the policy.
- Closing still requires row access, expected version, eligible state, and no active blocking
  attention.
- Closing enables feedback and starts 30-day terminal customer-page expiry.
- No bulk close in V1; cancel remains careful/detail-only and business-confirmed.

### ADR-344 — Session 6 proper is paused behind prerequisites

Do not start Session 6 closeout implementation until prerequisite decisions are recorded and the
first prerequisite implementation slice is planned.

Rules:

- Session 6 prerequisites own Follow Up On, Planned For, needs-status-check semantics, notification
  boundaries, customer page viewed, and customer intent menu refinement.
- Session 6 proper resumes after prerequisites with ready-to-close, closeout, Closed expiry,
  context navigation, and final ledger work.
- Archive/unarchive, closeout-reviewed state, dispatch/calendar scheduling, customer self-scheduling,
  broad analytics/reporting, backend SMS, realtime streaming, and spam/test classification remain
  outside this prerequisite pass.

## Implementation Split

Keep each implementation slice inside the session protocol hard slice gate: at most 3 independent
mutation handler families, 8 production files, and 12 total changed files including tests/docs.

### P6a — Prerequisite docs and ledger

This document plus ADR/session-log/deferred-topic updates.

### P6b — Follow Up On + Planned For foundation

Implement active-request date-only Follow Up On and Planned For with scan metadata, versioned
mutations, row/detail/list exposure, and stale-suppression inputs.

### P6c — Customer intent menu + page viewed signal

Refine customer intent codes/actions and add debounced customer-page viewed telemetry/metadata.

### P6d — Needs-status-check policy/signal model

Add account-policy threshold fallback, centralized latest-meaningful-activity helper, and
status-check query inputs that consume Follow Up On / Planned For.

### P6e — Notification candidate/badge contract notes

Record immediate-attention push candidates, badge/list-only categories, and transport boundary.
Do not implement delivery unless explicitly pulled into the notification/device session.

## File-Level Gate Before First Code Slice

Before P6b writes code, confirm exact current signatures for the touched request aggregate,
customer-write services, customer-page mapper/service, list mapper/service, persistence interfaces,
API query/body binders, and versioned mutation helpers. Enumerate compile-impact callers for any DTO
constructor changed by the slice.

### P6b preflight result — 2026-06-23

Preflight completed with no code edits.

Current signatures confirmed:

- Request aggregate: `src/OpHalo.Keep.Core/Entities/KeepRequest.cs`
- Request events: `src/OpHalo.Keep.Core/Entities/KeepRequestEvent.cs`
- Request event enum: `src/OpHalo.Keep.Core/Entities/Enums/KeepRequestEventType.cs`
- EF request mapping: `src/OpHalo.Keep.Infrastructure/Persistence/Configurations/KeepRequestConfiguration.cs`
- EF event mapping: `src/OpHalo.Keep.Infrastructure/Persistence/Configurations/KeepRequestEventConfiguration.cs`
- Versioned operator write persistence: `src/OpHalo.Keep.Application/Requests/IKeepRequestOperatePersistence.cs`
  and `src/OpHalo.Keep.Infrastructure/Persistence/EfKeepRequestOperatePersistence.cs`
- Detail read contract and mapper: `KeepRequestDetailResult.cs`, `KeepRequestDetailMapper.cs`,
  `IKeepRequestDetailPersistence.cs`, `EfKeepRequestDetailPersistence.cs`
- List contract and mapper: `KeepRequestSummary.cs`, `GetKeepRequestListService.cs`,
  `IKeepRequestListPersistence.cs`, `KeepRequestListPersistence.cs`
- Customer page contract: `KeepCustomerPageResult.cs`, `KeepCustomerPageMapper.cs`,
  `GetKeepCustomerPageService.cs`, `AddCustomerMessageService.cs`,
  `IKeepCustomerWritePersistence.cs`, `EfKeepCustomerWritePersistence.cs`
- API edge: `src/OpHalo.Api/Program.cs`, `src/OpHalo.Api/Keep/KeepRequestVersionHeader.cs`,
  and request body records under `src/OpHalo.Api/Keep/`

Compile-impact notes:

- `KeepRequestSummary` has one production constructor call in `GetKeepRequestListService`.
- `KeepRequestDetailResult` and `KeepCustomerPageResult` are constructed only by their mappers.
- Unit fakes exist for list/detail/operate persistence in `tests/OpHalo.UnitTests/Keep/`.
- `Program.cs` feedback-review endpoint was checked after preflight and is clean: only one
  `MarkFeedbackReviewedCommand` construction exists.

Recommended implementation shape:

- Do not fit all of P6b into one coding slice. Domain/schema/API/list/detail/tests/migration would
  exceed the 12-file hard gate.
- Keep Follow Up On and Planned For as one mutation handler family by using one shared timing
  service with parameterized operations and separate routes.
- Keep customer page output internal-safe: do not expose Follow Up On or Planned For to anonymous
  customer pages in P6b.

### Claude coding sessions for P6b

Use one Claude session per slice below. Each session should read only this build log header, the
Session Protocol in `docs/session-log.md`, and the files named in that slice. Do not re-open older
Phase 8 history unless a named signature has changed.

#### P6b-0 — Program.cs clean check

Goal: start from a clean API edge.

Read:

- `src/OpHalo.Api/Program.cs`

Task:

- Confirm the feedback-review endpoint has exactly one
  `new MarkFeedbackReviewedCommand(requestId, body.Note, versionResult.Value)` line.
- If duplicated, remove only the duplicate line.
- Run `dotnet build`.

Exit:

- No behavior changes.
- `Program.cs` has no duplicate feedback-review command construction.

#### P6b-1 — Domain, schema, and audit events

Goal: add durable Follow Up On and Planned For state plus internal audit events, without API routes.

Read:

- `src/OpHalo.Keep.Core/Entities/KeepRequest.cs`
- `src/OpHalo.Keep.Core/Entities/KeepRequestEvent.cs`
- `src/OpHalo.Keep.Core/Entities/Enums/KeepRequestEventType.cs`
- `src/OpHalo.Keep.Core/Errors/KeepRequestErrors.cs`
- `src/OpHalo.Keep.Infrastructure/Persistence/Configurations/KeepRequestConfiguration.cs`
- `src/OpHalo.Keep.Infrastructure/Persistence/Configurations/KeepRequestEventConfiguration.cs`
- `tests/OpHalo.UnitTests/Keep/KeepRequestTests.cs`

Implement:

- `FollowUpOnDate` as date-only data on `KeepRequest`.
- `FollowUpReason` with values: `weather`, `parts`, `customer_delay`,
  `business_operator_availability`, `third_party`, `other`.
- Optional Follow Up note; require note when reason is `other`.
- `PlannedForDate` as optional date-only data on `KeepRequest`.
- Domain methods to set/change/clear each field. Active requests only; reject `Resolved`,
  `Closed`, and `Cancelled`.
- Internal-only event factory/factories for Follow Up On changed and Planned For changed.
- EF configuration for new request fields and event metadata needed to render/query later.
- Migration and model snapshot update.
- Focused unit tests for validation, active-only behavior, set/change/clear, and event shape.

Do not implement:

- API routes.
- Detail/list DTO exposure.
- Needs-status-check queue.
- Notification delivery.

Suggested changed-file cap:

- Keep production files to domain/enums/errors/config/migration only.
- Keep tests focused on domain behavior.

Verify:

- `dotnet test tests/OpHalo.UnitTests/OpHalo.UnitTests.csproj --filter KeepRequest`
- `dotnet build`

#### P6b-2 — Versioned operator mutations and detail exposure

Goal: expose authenticated versioned mutations and authenticated detail metadata.

Read:

- `src/OpHalo.Keep.Application/Requests/IKeepRequestOperatePersistence.cs`
- `src/OpHalo.Keep.Infrastructure/Persistence/EfKeepRequestOperatePersistence.cs`
- `src/OpHalo.Keep.Application/Requests/ChangeKeepRequestStatusService.cs`
- `src/OpHalo.Keep.Application/Requests/KeepRequestDetailResult.cs`
- `src/OpHalo.Keep.Application/Requests/KeepRequestDetailMapper.cs`
- `src/OpHalo.Api/Program.cs`
- `src/OpHalo.Api/Keep/KeepRequestVersionHeader.cs`
- representative API tests for versioned mutations, such as
  `tests/OpHalo.IntegrationTests/Api/AddBusinessUpdateTests.cs`

Implement:

- One shared application service for Follow Up On + Planned For mutations.
- Separate API routes for set/clear Follow Up On and set/clear Planned For.
- Strict `X-Keep-Request-Version` parsing using `KeepRequestVersionHeader`.
- Owner/Admin account-wide access; Operator only with operate/row access, matching existing
  operator write semantics.
- OffSeason/read-only blocked, matching existing operator writes.
- Detail response fields for Follow Up On and Planned For, including scan-safe metadata needed by
  clients.
- Validation hints only if clients need max lengths/reason values from detail.
- Integration tests for success, stale version, missing/malformed version, terminal/resolved
  rejection, Operator row authorization, and Owner/Admin success.

Do not implement:

- Request list ranking/row context.
- Customer page exposure.
- Needs-status-check queue.

Verify:

- Focused integration tests for the new routes.
- `dotnet build`

#### P6b-2 — COMPLETE

**Commit:** `1030f07`. **Baseline:** 1280 tests (677 unit · 14 arch · 589 integration; +15 integration).

**Delivered:**
- `src/OpHalo.Keep.Application/Requests/ManageRequestTimingService.cs` — single service,
  four public methods (SetFollowUpOnAsync / ClearFollowUpOnAsync / SetPlannedForAsync /
  ClearPlannedForAsync), shared `AuthAsync` helper matching `ManageResponsibleService` pattern.
  Reason slug parsed internally via `KeepRequestDetailMapper.ParseFollowUpReasonSlug`.
- `src/OpHalo.Api/Keep/RequestTimingRequests.cs` — `SetFollowUpOnRequestBody(Date, Reason, Note)`,
  `SetPlannedForRequestBody(Date)`. Date passed as `yyyy-MM-dd` string, parsed at API edge.
- 4 routes: `PUT/DELETE /keep/requests/{id}/follow-up-on`, `PUT/DELETE /keep/requests/{id}/planned-for`.
  All versioned; clears use DELETE with no body.
- `KeepRequestDetailResult` — `FollowUpOnDate`, `FollowUpOnReason`, `FollowUpOnNote`, `PlannedForDate`.
- `AvailableActionsMetadata` — `CanSetFollowUpOn`, `CanSetPlannedFor`.
- `ValidationHintsMetadata` — `FollowUpNoteMaxLength: 500`, `AllowedFollowUpReasons` (6 slugs).
- `KeepRequestActionPolicy` — `canSetTiming` guards Resolved/Closed/Cancelled exactly matching domain.
- `ErrorHttpMapper` — explicit 409 for `FollowUpOnRequiresActiveRequest`/`PlannedForRequiresActiveRequest`;
  400 for `FollowUpOnReasonRequired`, `FollowUpOnNoteRequired`, `FollowUpOnNoteTooLong`, `InvalidDateFormat`.
- `KeepRequestDetailMapper.MapEventType` — added `follow_up_on_changed` / `planned_for_changed`.
- `KeepRequestErrors` — added `InvalidDateFormat`.
- `tests/OpHalo.IntegrationTests/Api/RequestTimingTests.cs` — 15 tests: set/clear success,
  field round-trip, event in timeline, stale 409, missing version 400, malformed version 400,
  terminal 409, resolved 409, Operator row access 200, Operator no-row 404, Viewer 403, anonymous 401,
  affordances in response, PlannedFor closed 409.

#### P6b-3 — List scan metadata and stale-suppression inputs

Goal: make Follow Up On and Planned For visible in authenticated request lists and available to the
future needs-status-check slice.

Read:

- `src/OpHalo.Keep.Application/Requests/KeepRequestSummary.cs`
- `src/OpHalo.Keep.Application/Requests/GetKeepRequestListService.cs`
- `src/OpHalo.Keep.Application/Requests/IKeepRequestListPersistence.cs`
- `src/OpHalo.Keep.Infrastructure/Persistence/KeepRequestListPersistence.cs`
- `tests/OpHalo.UnitTests/Keep/KeepRequestListServiceTests.cs`
- `tests/OpHalo.IntegrationTests/Api/KeepRequestListB5Tests.cs`

Implement:

- Summary metadata for Follow Up On and Planned For.
- Scan labels such as `Follow up today`, `Follow-up overdue`, `Follow up Fri`, `Parts`,
  `Weather`, `Planned today`, `Planned tomorrow`, `Planned Fri`, and `Planned date passed`.
- Ranking/row-context adjustments only where ADR-337/338 require scan priority.
- Explicit booleans or date fields that P6d can consume to suppress stale/status-check when future
  Follow Up On or future Planned For exists.
- Focused list tests for due/overdue/future labels and no visibility expansion.

Do not implement:

- The actual `needs_status_check` queue.
- Account policy threshold.
- Latest meaningful activity helper.

Verify:

- Focused list unit tests.
- Focused list API tests.
- `dotnet build`

#### P6b-4 — P6b completion gate

Goal: reconcile docs and run the proportionate suite after P6b-1 through P6b-3 are green.

Read:

- `docs/session-log.md`
- `docs/deferred-topics.md`
- `docs/decisions/decision-index.md`
- this build log

Task:

- Record implemented scope and final test baseline.
- Keep DEF-037 open for P6d.
- Keep DEF-030 open for P6c.
- Mark DEF-067 implemented only after Follow Up On is fully exposed in domain/API/list/detail.
- Move the session log next batch to P6c after P6b is complete.

Verify:

- `dotnet test tests/OpHalo.UnitTests/OpHalo.UnitTests.csproj`
- Focused integration tests touched by P6b.
- Full suite only if Christian approves or if the slice materially changes shared list/detail
  behavior beyond the focused coverage.

## Exclusions

Session 6 prerequisites explicitly exclude:

- archive/unarchive/closeout-reviewed state;
- auto-close, auto-complete, or background jobs;
- batch close, batch review, bulk status changes;
- dispatch/calendar scheduling, appointment booking, customer self-scheduling, route/crew capacity;
- notification delivery implementation, realtime/SSE/WebSockets;
- spam/test classification;
- analytics/reporting/read models;
- account timezone settings;
- customer identity portals, link management, token rotation.

## Exit Criteria

- Follow Up On and Planned For semantics are implemented or explicitly scheduled as the next slice.
- Customer intent menu and customer-page viewed signal have locked implementation contracts.
- Needs-status-check has an account-policy threshold and extensible signal calculation plan.
- Notification push/badge/list boundaries are documented for the later notification/device session.
- Session log points to the next prerequisite implementation slice, not Session 6 closeout proper.
