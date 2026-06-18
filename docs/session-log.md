# Session Log — OpHalo Foundation

**Last updated:** 2026-06-18 (Session 3A complete)
**Branch:** `main` (no remote yet)

---

## Phase 8-B5 Session 3A — Domain + Persistence Invariants — COMPLETE

**Tests:** 396 unit · 14 arch · 251 integration (661 total — all green)
**Next free ADR:** ADR-236 (no new ADRs consumed in 3A)
**Migration:** `20260618102937_AddParticipationChangedEventFields` — 7 nullable columns on `keep_request_events`; filtered unique index on `keep_request_participants`

**Files changed:**

| File | Change |
|---|---|
| `Keep.Core/Entities/Enums/ParticipationAction.cs` | New: 9-value enum (`ResponsibleAssigned`..`Unmuted`) |
| `Keep.Core/Entities/Enums/ParticipationNotificationIntentKind.cs` | New: `Assignment = 1`, `WatcherAdded = 2` |
| `Keep.Core/Entities/Enums/KeepRequestEventType.cs` | Added `ParticipationChanged = 10` |
| `Keep.Core/Entities/KeepRequestParticipant.cs` | Added `Detach`, `Reactivate`, `SetNotificationsEnabled` mutation methods |
| `Keep.Core/Entities/KeepRequestEvent.cs` | Added 7 participation fields + `CreateParticipationChanged` factory with intent-pairing validation |
| `Keep.Core/Errors/KeepRequestErrors.cs` | Added `ParticipationNoteTooLong`, `ParticipationMuteRequiresActiveParticipation`, `ParticipationCannotUnwatchResponsible`, `ParticipationResponsibleCannotWatch`, `ParticipationStateCorrupt` |
| `Keep.Core/Domain/ParticipationChangeOutcome.cs` | New: factory-only outcome record (mirrors `KeepStatusChangeOutcome`) |
| `Keep.Core/Domain/KeepRequestParticipationService.cs` | New: domain service — 8 methods, participant-set invariant validation |
| `Keep.Infrastructure/.../KeepRequestEventConfiguration.cs` | EF config for 7 new nullable participation fields |
| `Keep.Infrastructure/.../KeepRequestParticipantConfiguration.cs` | Filtered unique index for one-active-Responsible per request |
| `Foundation.Infrastructure/Migrations/20260618102937_AddParticipationChangedEventFields.cs` | Migration |
| `UnitTests/Keep/KeepRequestParticipationTests.cs` | New: 41 unit tests |

**Design decisions resolved during 3A:**

- D1: Cross-participant invariants (one active Responsible, Responsible/Watching mutual exclusion) live in `KeepRequestParticipationService` (Core domain service), not the application layer. Application service (3B) handles auth, role, OffSeason, and target-user eligibility.
- D2: `ParticipationAction` as enum (9 values); `ParticipationNotificationIntentKind` as enum (`Assignment`, `WatcherAdded`) — both mapped to stable API strings at the detail mapper boundary (3C).
- D3: Optional participation notes capped at 4000 characters, matching internal-note convention.
- D4: `targetDisplayName` is nullable on remove/clear operations (participant rows have no display-name snapshot); the application service in 3B can supply it from the eligible-user lookup.
- D5: `RemoveWatcher` on a currently-Responsible user fails (not a no-op) — invalid action per ADR-230.
- D6: `ValidateParticipantSet` in the domain service guards against corrupt participant sets (wrong request/account, multiple active Responsible rows, multiple active rows per user) — returns `ParticipationStateCorrupt` error.
- D7: EF merged two `HasIndex(x => x.RequestId)` calls into one. Config consolidated to single filtered+unique index named `ix_keep_request_participants_request_id`; general request_id scans covered by existing composite `ix_keep_request_participants_request_user`.

**Known carry-forward:**
- No API endpoints yet; application services and candidate lookup are Session 3B scope.
- `targetDisplayName` on clear/remove operations: 3B application service should look up from account-user data and pass it in.
- Notification intent `includeNotificationIntent` flag: 3B application service passes `true` for Owner/Admin assign/transfer/add-watcher, `false` for Operator self-assign and self-watch.

---

## Phase 8-B5 Session 3 Discovery Checkpoint — Assignment / Watch / Mute

**Pre-work complete**
**Status:** Decisions ADR-222..235 locked. Ready for bounded implementation sessions.
**Next free ADR:** ADR-236
**Build logs:** `docs/build-log/040-phase-8-b5-session-3-assignment-watch-mute-decisions.md` (decisions) · `docs/build-log/039-phase-8-b5-claude-coding-sessions.md` (3A-3D implementation split)

Locked implementation split:

| Session | Goal |
|---|---|
| 3A | Domain + persistence invariants for participation changes and `ParticipationChanged` event metadata |
| 3B | API/application services plus compact eligible participant lookup |
| 3C | Detail/list read models, internal timeline metadata, list responsible-name/Owner-Admin assignment metadata |
| 3D | Cross-slice verification, docs, and completion gate |

Key locked decisions:

- Session 3 covers non-terminal assignment, transfer, clear responsible, watch, unwatch, mute, and unmute.
- Closed unresolved-feedback rows remain review-only; OffSeason blocks participation writes.
- Owner/Admin can assign, transfer, clear, and manage watchers; Operators can self-watch/unwatch and self-mute/unmute, and can self-assign only an unassigned/effectively unassigned request they can legitimately access.
- Operators cannot clear themselves as Responsible; Owner/Admin must clear or transfer responsibility.
- One active participation row per user per request; Responsible and Watching are mutually exclusive.
- Clear/transfer detaches the previous Responsible and does not auto-watch them.
- Watch defaults notifications enabled; mute requires active participation and only flips `NotificationsEnabled=false`.
- Mute/unmute is current-user only in Session 3, but internals should remain future-friendly for delegated controls.
- Only Active Owner/Admin/Operator users are eligible assignees/watchers. Stale Responsible users are notification-ineligible and effectively unassigned for routing, but not auto-detached.
- Operator default list hides unassigned requests; an Operator Unassigned/Available queue is deferred.
- Request detail is the main participation-control surface. Owner/Admin may assign/transfer Responsible from the list; list clear/watch/mute are not in Session 3.
- List shows responsible person name and may flag stale responsibility for Owner/Admin; full watcher lists stay on detail.
- Participation changes are internal-only, activity/ranking-neutral, and customer-page excluded.
- Writes use explicit endpoints, are idempotent, return updated `KeepRequestDetailResult`, and do not duplicate timeline events or notification intent on no-op retries.
- Optional internal notes are supported for Owner/Admin managed assignment/transfer/clear/add-watcher/remove-watcher actions; self-service watch/mute actions do not expose notes in Session 3.
- Assignment/watch notification intent is structured event metadata only; no notification delivery/outbox/device work in Session 3.
- Add/reuse a compact eligible participant lookup, preferably `GET /keep/requests/participant-candidates`.

Open/deferred after Session 3:

- notification delivery, quiet hours, global preferences, device tokens, badge counts;
- Operator Unassigned/Available queue or filters;
- Owner/Admin delegated mute/unmute;
- automatic cleanup of stale participants;
- list-level clear/watch/mute controls unless future workflow proves a need.

---

## Phase 8-B5 Session 1 — COMPLETE

**Pre-work complete**
**Tests:** 537/537 (299 unit · 14 arch · 224 integration)
**ADRs in scope:** 164..218
**Next free ADR:** 219
**Build logs:** `docs/build-log/038-phase-8-b5-request-list-triage-external-contact-decisions.md` (decisions) · `docs/build-log/039-phase-8-b5-claude-coding-sessions.md` (implementation spec)

---

### What was built

Phase 8-B5: default command-center request list — ranking, attention indicators, quick actions, participation/notification metadata.

**Key design decisions resolved:**
- D1: `GetDefaultListRequestsAsync(accountId, bool includeClosedUnresolvedFeedback, ct)` — Owner/Admin pass `true`, everyone else `false`
- D2: Preview text deferred (`null`/`false` in B5)
- D3: Nested grouped `KeepRequestSummary` records
- D4: In-memory ranking after bounded candidate set load
- D5: `GetKeepRequestListResult(IReadOnlyList<KeepRequestSummary>)` — no pagination

**Files changed:**

| File | Change |
|------|--------|
| `Keep.Core/Entities/KeepRequest.cs` | Added `firstResponseTargetMinutes` param (position 10, required, `> 0`); sets `FirstResponseDueAtUtc = now + minutes` for Customer origin |
| `Keep.Application/PublicIntake/IKeepIntakePersistence.cs` | Added `GetResponsePolicyAsync` |
| `Keep.Infrastructure/Persistence/KeepIntakePersistence.cs` | Implemented `GetResponsePolicyAsync` |
| `Keep.Application/PublicIntake/CreateKeepPublicIntakeService.cs` | Loads policy; passes `firstResponseTargetMinutes` |
| `Keep.Application/Requests/KeepRequestSummary.cs` | Full B5 nested shape: `KeepRequestAttentionInfo`, `KeepRequestRankingInfo`, `KeepRequestPreviewInfo`, `KeepRequestActionsInfo`, `KeepQuickAction`, `KeepRequestParticipationInfo`, `KeepRequestNotificationInfo` |
| `Keep.Application/Requests/IKeepRequestListPersistence.cs` | Renamed `GetOpenRequestsAsync` → `GetDefaultListRequestsAsync(accountId, bool, ct)`; added `GetParticipantSummariesAsync`; added `KeepRequestParticipantSummary` record |
| `Keep.Application/Requests/GetKeepRequestListService.cs` | Full B5 rewrite: ranking groups 1–8, severity, quick actions, contact actions, participation/notification info, `RequestListComparer`, exhaustive enum maps |
| `Keep.Infrastructure/Persistence/KeepRequestListPersistence.cs` | Replaced `GetOpenRequestsAsync` with `GetDefaultListRequestsAsync`; added `GetParticipantSummariesAsync` (batch participant query) |
| `UnitTests/Keep/KeepRequestListServiceTests.cs` | Updated fake (role-aware policy, new persistence interface); updated mapping test with nested assertions; added `Scheduled` theory; added B5 tests (include-closed-feedback by role, Viewer restrictions, OffSeason, overdue group 1, post-close group 3, first-response-pending group 5, ClearsAttention state-awareness, sort order) |
| `IntegrationTests/Api/KeepRequestListB5Tests.cs` | New file: 8 integration tests covering Owner/Admin vs Operator closed-feedback visibility, Viewer restrictions, Cancelled/Closed exclusion, Resolved inclusion, first-response-pending metadata, post-close row actions |
| All ~30 `KeepRequest.Create` call sites in tests | Added `firstResponseTargetMinutes` arg (60) |

**Service bugs fixed during B5:**
- `GetKeepRequestListService.cs` line 84: `userSnapshot.Id` → `userSnapshot.AccountUserId`
- `ComputeSeverity`: added `firstResponsePending` parameter; returns `"attention"` for first-response-pending (was falling through to `"muted"`)

---

### Ranking summary

| Group | Code | Condition | Secondary sort |
|-------|------|-----------|---------------|
| 1 | `overdue_business_waiting` | Business-waiting + NextAttentionAt < now, OR first-response overdue | Asc NextAttentionAt / FirstResponseDueAt |
| 2 | `priority_business_waiting` | PriorityBand=Priority + WaitingDirection=Business (not overdue, not post-close) | Asc NextAttentionAt |
| 3 | `post_close_unresolved_feedback` | Closed + UnresolvedFeedback + AttentionLevel≠None | Asc AttentionSinceUtc |
| 4 | `standard_business_waiting` | WaitingDirection=Business, standard band | Asc NextAttentionAt |
| 5 | `first_response_pending` | FirstResponseDueAt > now, no first response | Asc FirstResponseDueAt |
| 6 | `waiting_on_customer` | PendingCustomer | Desc LastBusinessActivityAt |
| 7 | `resolved_quiet` | Resolved + AttentionLevel=None | Desc LastBusinessActivityAt |
| 8 | `active` | Everything else active | Desc LastBusinessActivityAt |

---

### Session 2A — External Contact Schema + Domain — COMPLETE

**Tests:** 355 unit · 14 arch (integration tests pending migration apply)
**Migration:** `20260617224828_AddExternalContactEventFields` — 5 nullable columns on `keep_request_events`
**Next free ADR:** ADR-219 (unchanged)

**Files changed:**

| File | Change |
|------|--------|
| `Keep.Core/Entities/Enums/ExternalContactDirection.cs` | New: `Outbound = 1, Inbound = 2` |
| `Keep.Core/Entities/Enums/ExternalContactOutcome.cs` | New: `SpokeWithCustomer = 1, LeftVoicemail = 2, NoAnswer = 3, WrongNumber = 4` |
| `Keep.Core/Entities/Enums/KeepRequestEventType.cs` | Added `ExternalContactLogged = 9` |
| `Keep.Core/Entities/KeepRequestEvent.cs` | Added 5 nullable fields + `CreateExternalContactLogged` factory |
| `Keep.Core/Entities/KeepRequest.cs` | Added `LogOutboundExternalContact` + `LogInboundExternalContact` |
| `Keep.Core/Errors/KeepRequestErrors.cs` | Added 8 external-contact domain errors |
| `Keep.Infrastructure/Persistence/Configurations/KeepRequestEventConfiguration.cs` | EF config for 5 new event fields |
| `Foundation.Infrastructure/Migrations/20260617224828_AddExternalContactEventFields.cs` | Schema migration |
| `UnitTests/Keep/KeepRequestExternalContactTests.cs` | New: 36 domain unit tests covering full effect matrix |

**Design decisions refined during 2A:**
- `CommunicationChannel` reused for external contact channel (no new enum) — refinement to ADR-203
- `ExternalContactSetFirstResponse` (not `CountsFirstResponse`) — field means "this event set first-response state"
- Separate `ExternalContactInvalidInboundChannel` and `ExternalContactInvalidOutboundChannel` errors
- `WaitingDirection.Customer` is not set by any current domain method; the flip branch mirrors `AddCustomerMessage`
- ADR-215 DTO field `externalContactCountsFirstResponse` should be renamed to `externalContactSetFirstResponse` when timeline DTO is built in 2B

---

### Session 2D — External Contact Completion Gate — COMPLETE

**Tests:** 355 unit · 14 arch · 251 integration (620 total — all green)
**Next free ADR:** ADR-222
**No new code written** — verification and docs only.

**Verification findings:**

| Item | Result |
|------|--------|
| Full test suite | 620/620 green |
| External-contact domain unit tests | 36 tests — full outbound/inbound × channel × outcome × first-response × attention effects × event-shape matrix |
| External-contact integration tests | 16 tests in `KeepRequestExternalContactApiTests.cs` (auth, permission, 4 happy paths, not-found/terminal, 7 validation, customer-page exclusion) |
| OffSeason external-contact tests | `PostExternalContact_OffSeason_Returns403` + `canLogExternalContact=false` in detail — in `KeepOffSeasonTests.cs` |
| Migration scope | `20260617224828_AddExternalContactEventFields` — 5 nullable columns on `keep_request_events` only; no extra tables, occurred/source/provenance fields |
| Deferred work audit | No customer recap, SMS delivery, notifications, telemetry, assignment writes, list previews, undo/revert, or provenance fields pulled in |
| List behavior | Request list still changes only through `KeepRequest` state fields (`LastBusinessActivityAt`, `LastCustomerActivityAt`, attention fields, `FirstRespondedAtUtc`) |
| `LogExternalContactService` | Clean: OffSeason blocked via `RequestImplementsAllowedInOffSeason: false` + `decision.IsReadOnly` (ADR-221) |

**Docs updated:**
- `docs/deferred-topics.md` — DEF-018, DEF-031, DEF-032 marked Implemented (2A-2B); DEF-042 marked Implemented (2C, partial)
- `docs/session-log.md` — this entry

**Known carry-forward:**
- Owner/Admin narrow closeout in OffSeason deferred to DEF-042
- DEF-041: mistake/ignore marker on contact logs
- DEF-040: compact mobile recent-activity previews
- DEF-044: external-contact source/provenance

---

### Session 2C — OffSeason Freeze — COMPLETE

**Tests:** 355 unit · 14 arch · 251 integration (620 total)
**ADR implemented:** ADR-221
**Next free ADR:** ADR-222

**Files changed:**

| File | Change |
|------|--------|
| `Keep.Core/Errors/KeepRequestErrors.cs` | Added `OffSeasonUnavailable` error (ADR-221) |
| `Api/Helpers/ErrorHttpMapper.cs` | Added `"KeepRequest.OffSeasonUnavailable"` → 409 |
| `Keep.Application/Requests/AcknowledgeAttentionService.cs` | `RequestImplementsAllowedInOffSeason: false`; added `|| decision.IsReadOnly` check |
| `Keep.Application/Requests/AddInternalNoteService.cs` | Same |
| `Keep.Application/Requests/AddBusinessUpdateService.cs` | Same |
| `Keep.Application/Requests/ChangeKeepRequestStatusService.cs` | Same |
| `Keep.Application/Requests/LogExternalContactService.cs` | Added `|| decision.IsReadOnly` (already had `false`; this was the missing piece from 2B) |
| `Keep.Application/Requests/GetKeepRequestDetailService.cs` | Added `isOffSeason` from snapshot; `canWrite = canOperate && !isOffSeason`; all write-action flags now use `canWrite`; added `using` for `AccountOperatingMode` |
| `Keep.Application/Requests/KeepPublicCustomerContext.cs` | Added `bool IsOffSeason` |
| `Keep.Application/Requests/KeepPublicCustomerAccessGuard.cs` | Populates `IsOffSeason` on context; updated comment from ADR-083 to ADR-208; added `using` for `AccountOperatingMode` |
| `Keep.Application/Requests/AddCustomerMessageService.cs` | Added `context.IsOffSeason` → `OffSeasonUnavailable` check before domain call |
| `Keep.Application/Requests/SubmitFeedbackService.cs` | Same |
| `IntegrationTests/Api/KeepOffSeasonTests.cs` | New: 11 tests — read list/detail/customer-page pass in OffSeason; all 5 operator write endpoints return 403; customer message/feedback return 409 OffSeasonUnavailable; public intake returns 422 |
| `docs/decisions/decision-index.md` | Added ADR-221 |

**Design decisions locked:**
- D1 (ADR-221): `KeepRequest.OffSeasonUnavailable` → 409 for customer-page writes; `auth.forbidden` → 403 for operator writes; Owner/Admin closeout deferred to DEF-042
- D2: `canWrite = canOperate && !isOffSeason` applies to all write-action flags in `GetKeepRequestDetailService` (not just `CanLogExternalContact`)

**Key findings during implementation:**
- `LogExternalContactService` bug fixed: had `RequestImplementsAllowedInOffSeason: false` (set in 2B) but only checked `decision.IsBlocked` — OffSeason slips through because `ReadOnly` is not `Blocked`. Added `|| decision.IsReadOnly` to complete the 2B intent.
- `EnterOffSeason` requires `CommercialState.Active`. Integration tests transition Trial → PastDue → Active via `MarkPastDue` / `ResolvePastDue` before calling `EnterOffSeason`.

**Known carry-forward:**
- Owner/Admin narrow closeout action in OffSeason deferred to DEF-042
- Session 2D: cross-slice verification of full external-contact semantic matrix; deferred-topic audit

---

### Session 2B — External Contact API + Detail Timeline — COMPLETE

**Tests:** 355 unit · 14 arch · 240 integration (609 total)
**ADRs implemented:** 197, 199, 200, 202, 203, 207, 209, 211, 215, 216; new ADR-219, ADR-220
**Next free ADR:** ADR-221

**Files changed:**

| File | Change |
|------|--------|
| `Keep.Core/Errors/KeepRequestErrors.cs` | Added `ExternalContactInvalidDirection` (ADR-207 gap from 2A) |
| `Keep.Application/Requests/KeepRequestDetailResult.cs` | Added `CanLogExternalContact` to `AvailableActionsMetadata`; added `ExternalContactSummaryMaxLength` to `ValidationHintsMetadata`; added 6 nullable external-contact fields to `KeepRequestEventItem` |
| `Keep.Application/Requests/KeepRequestDetailMapper.cs` | Added `ExternalContactLogged` to `MapEventType`; updated `MapEvent` to populate external-contact fields; added `MapExternalContactDirection` and `MapExternalContactOutcome`; updated `ValidationHints` static |
| `Keep.Application/Requests/IKeepRequestOperatePersistence.cs` | Added `GetResponsePolicyAsync(Guid accountId, ct)` (ADR-219) |
| `Keep.Infrastructure/Persistence/EfKeepRequestOperatePersistence.cs` | Implemented `GetResponsePolicyAsync` |
| `Keep.Application/Requests/LogExternalContactService.cs` | New: `LogExternalContactCommand` record + `LogExternalContactService`; parses direction/channel/outcome strings; validates inbound constraints before domain; `RequestImplementsAllowedInOffSeason: false` per ADR-209 |
| `Api/Keep/ExternalContactRequest.cs` | New: `ExternalContactRequestBody` record |
| `Api/Program.cs` | Registered `LogExternalContactService`; added `POST /keep/requests/{requestId}/external-contact` endpoint |
| `GetKeepRequestDetailService.cs` + 4 write services | Added `CanLogExternalContact: canOperate && !request.IsTerminal` (or confirmed true equivalents) to all `AvailableActionsMetadata` constructors (ADR-220) |
| `IntegrationTests/Api/KeepRequestExternalContactApiTests.cs` | New: 14 integration tests — auth (401), Viewer 403, outbound phone spoke (first-response set), outbound phone no-answer (log only), outbound SMS, inbound follow-up (attention raised), unknown request 404, terminal 409, invalid direction/channel/outcome errors, follow-up required/summary required/summary too long, customer page exclusion |

**Design decisions resolved during 2B:**
- D1 (ADR-219): `GetResponsePolicyAsync` on `IKeepRequestOperatePersistence` — keeps operator-write service self-contained; fallback `policy?.StandardResponseTargetMinutes ?? 240`
- D2 (ADR-220): `CanLogExternalContact` in `AvailableActionsMetadata` now; rule `canOperate && !request.IsTerminal`; OffSeason tightening deferred to Session 2C

**Known carry-forward:**
- `CanLogExternalContact` in detail reads from other services does not yet reflect OffSeason (returns `true` for non-terminal requests even in OffSeason). Session 2C should correct all action metadata for OffSeason operating mode.
- Existing write services continue to use `RequestImplementsAllowedInOffSeason: true` — Session 2C scope per ADR-208/209.

---

### Session 2 discovery checkpoint — External Contact Logging

**Status:** Decisions ADR-196..218 locked. Ready for implementation in bounded sessions.
**Goal:** Implement authoritative external-contact writes after B5 list affordances, applying the
OffSeason freeze from ADR-208.

Decision/deferred refs: ADR-167..172, ADR-180, ADR-191, ADR-196..218; DEF-018, DEF-031,
DEF-032, DEF-040, DEF-041, DEF-042, DEF-043, DEF-044

Locked discovery decisions:

- External contact logs are structured internal `KeepRequestEvent` rows, not a separate table.
- One endpoint/service: `POST /keep/requests/{requestId}/external-contact`.
- Backend owns the first-response/attention effect matrix; quick contact launch alone is not durable evidence.
- Customer-visible recap remains a separate explicit business update/status action.
- Outbound phone logs stay low-friction; outbound email/text and inbound customer contact require summaries.
- Only non-terminal requests can log external contact.
- Detail timeline renders contact logs; list recent-activity previews are deferred but preserved by structured event data.
- The endpoint returns `KeepRequestDetailResult`.
- Explicit external-contact direction/outcome enums, existing `CommunicationChannel` reuse, stable API codes, and validation errors are required.
- Inbound follow-up uses response-policy timing, standard priority by default.
- Logged contact updates existing activity timestamps by direction.
- No undo/revert/mistake flag in Session 2.
- OffSeason is frozen/read-mostly: normal reads remain, normal writes are blocked, public intake
  links show an unavailable response instead of 404, and Owner/Admin may perform narrow closeout.
- External-contact logging follows operator-write permissions and is blocked in OffSeason.
- External contact uses `OccurredAtUtc` as log time only; separate occurred-time/source provenance is deferred.
- Implementation uses one endpoint/service and separate outbound/inbound domain methods.
- First-response evidence links to the `ExternalContactLogged` event when contact counts as first response.
- Attention clear reason uses stable code `external_contact_no_follow_up`.
- Detail timeline DTO, request body shape, migration scope, and test gate are locked.

Implementation watch-out:

- Current code allows request writes in OffSeason via `RequestImplementsAllowedInOffSeason=true`.
  Session 2C must align all existing services with ADR-208. Session 2B already sets `false` for `LogExternalContactService`.

See `docs/build-log/038-phase-8-b5-request-list-triage-external-contact-decisions.md` Session 2 checkpoint and `docs/build-log/039-phase-8-b5-claude-coding-sessions.md` Session 2 for implementation scope.

---

## Phase 8-B4 Service Request Detail Enrichment — COMPLETE

**Tests:** 510/510 (280 unit · 14 arch · 216 integration)
**ADRs:** 145..163 implemented
**Build logs:** `docs/build-log/036-phase-8-b4-service-request-detail-enrichment-decisions.md` · `docs/build-log/037-phase-8-b4-service-request-detail-enrichment-implementation.md`

---

## Phase 8-B3+ Closed-Request Feedback — COMPLETE

**Tests:** 500/500 · ADRs 135..144 · `docs/build-log/035-phase-8-b3-plus-feedback-implementation.md`

---

## Phase 8-B3-beta — COMPLETE

**Tests:** 487/487 · ADRs 118..134

---

## Phase 8-B3-alpha — COMPLETE

**Tests:** 471/471 · ADRs 118..134 (decision gate)

---

## Phase 8-B2-delta — COMPLETE

**Tests:** 471/471 · ADRs 116..117

---

## Phase 8-B2-gamma — COMPLETE

**Tests:** 468/468 · ADRs 111..114

---

## Phase 8-B2-beta — COMPLETE

**Tests:** 454/454 · ADRs 108..110

---

## Phase 8-B2-alpha — COMPLETE

**Tests:** 436/436 · `PATCH /keep/requests/{requestId}/status`

---

## Phase 8-B1-β — COMPLETE

Keep request detail + customer page read surfaces. ADRs 099..101.

---

## Phase 8-B1-α — COMPLETE

Keep domain model + EF schema. ADRs 094..098.

---

## Phase 5E-C — COMPLETE

Member management API + integration tests.

---

## Watch-outs carried forward

- `docs/deferred-topics.md` holds the full deferred backlog.
- **ADR-058** superseded by ADR-061..064.
- **AnonymousCurrentUser** kept for potential worker/test use; not in production.
- **SystemClock** FQDN in Program.cs: `OpHalo.Foundation.Infrastructure.Services.SystemClock`.
- **Schema-drop reset** in integration test factory: `DROP SCHEMA public CASCADE` + recreate + `MigrateAsync`.
- **Migration generation** always: `--startup-project src/OpHalo.Keep.Infrastructure`.
- **No GitHub remote yet.**
- **`Results.Problem` extension shape:** extension dict entries land at the top level of ProblemDetails JSON, not under an `"extensions"` key. Test assertions must use `body.GetProperty("code")`.
- **External contact logging/capture** implemented in B5 Sessions 2A-2C; remaining adjacent follow-ups are tracked in DEF-040/041/044.
- **`businessName ?? string.Empty`** — persistence returns null if account missing post-auth; never expected in production.
- **`KeepResponsePolicy` defaults** (first=60, standard=240, priority=60 min) apply when no policy row exists for an account; silent fallback by design.
- **Negative feedback on Closed raises attention** — intentional exception to terminal-no-attention posture (ADR-138).
- **Feedback `WasResolved` is `bool?` at API layer** — null signals missing flag, validated before service. Domain method takes `bool`.
- **Always rebuild before running tests** — `dotnet test --no-build` can run stale assemblies that mask real failures.
- **Next free ADR: ADR-236.**
- **B4 mapper signature:** `ToDetailResult` now takes `AccountUserRole role` and `bool canOperate` — all callers updated; write services pass `canOperate: true`.
- **Participant `DisplayName`** computed in persistence (two-query approach retained; User.Name projected in the AccountUsers query via EF navigation LEFT JOIN).
- **`KeepRequestStatus.Scheduled = 7`** — added to `MapStatus` in B5 service rewrite.
- **`FirstResponseDueAtUtc` gap** — fixed in B5. `KeepRequest.Create` now requires `firstResponseTargetMinutes` (required param, position 10). All call sites updated. `CreateKeepPublicIntakeService` loads policy and passes `policy?.FirstResponseTargetMinutes ?? 60`.
- **Post-close ranking fix** — `isPostClose` must be checked before priority band in `ComputeRankingGroup`. Post-close rows have `WaitingDirection=Business + Priority`, would otherwise hit group 2 instead of group 3.
- **`post_customer_update.ClearsAttention`** — state-aware, not static. True only when `WaitingDirection=Business && AttentionLevel != None`.
- **`ComputeSeverity` first-response-pending** — fixed in B5 completion. Now returns `"attention"` for first-response pending (was falling through to `"muted"`). Method signature takes `bool firstResponsePending` parameter.
- **Resolved request resolved_quiet ranking** — applies only when neither first-response pending nor first-response overdue checks fire and `AttentionLevel=None`. A Resolved request with an outstanding first-response obligation stays in the first-response ranking path; `FirstRespondedAtUtc` suppresses those checks.
- **`CanLogExternalContact` OffSeason gap** — Fixed in 2C. `GetKeepRequestDetailService` now uses `canWrite = canOperate && !isOffSeason` for all write-action flags.
- **`ExternalContactInvalidDirection` added in 2B** — was omitted from the 2A error set despite being in ADR-207 scope.
- **External contact `ExternalContactChannel` in DTO** — `KeepRequestEventItem` exposes both the existing `CommunicationChannel` field (for all applicable event types) and the new `ExternalContactChannel` field (only non-null for external contact events). Both map the same `CommunicationChannel` enum value for contact events; the grouped external-contact fields are for client convenience.
