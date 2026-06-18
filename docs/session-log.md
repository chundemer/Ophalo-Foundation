# Session Log — OpHalo Foundation

**Last updated:** 2026-06-18
**Branch:** `main` (no remote yet)

---

## Phase 8-B5 Session 3C — Participation Read Models, Timeline, List Assignment Metadata — COMPLETE

**Tests:** 698 (396 unit · 14 arch · 288 integration) — all green
**Next free ADR:** ADR-261

### What was built

Session 3C completed the participation read-model surface:

- **`AvailableActionsMetadata`** expanded to 4 participation flags: `CanWatch`, `CanUnwatch`, `CanMute`, `CanUnmute` — with precise semantics replacing the original 2-flag approach (ADR-237 refinement during session: `CanWatch` means "can start watching, not currently participating", `CanUnwatch/Mute/Unmute` are state-specific).
- **`CurrentUserDetailParticipation`** record on `KeepRequestDetailResult` — `participationType` + `notificationsEnabled` for the requesting user.
- **`participants[].isEligible`** derived from live membership in the mapper.
- **`KeepRequestEventItem`** participation event metadata fields: `participationAction`, `participationTargetAccountUserId`, `participationTargetDisplayName`, `participationPreviousResponsibleAccountUserId`, `participationInternalNote`.
- **`KeepRequestParticipationInfo`** on list summaries: `responsibleDisplayName`, `responsibleIsStale`, `canAssignFromList`.
- **`GetActorDisplayNameAsync`** fixed to return `User.Name ?? Email` (with `.Trim()`), consistent with `GetParticipantTargetAsync` and `GetParticipantCandidatesAsync`.

### Design decisions locked this session (refinements)

- **`CanWatch`**: `!isTerminal && currentUserRow is null` — "can start watching", false if already participating as anything.
- **`CanUnwatch`**: `!isTerminal && currentUserRow?.ParticipationType == Watching` — false for Responsible (DELETE /watch cannot clear responsibility).
- **`CanMute`**: `!isTerminal && currentUserRow is not null && currentUserRow.NotificationsEnabled` — only when participating and currently unmuted.
- **`CanUnmute`**: `!isTerminal && currentUserRow is not null && !currentUserRow.NotificationsEnabled` — only when participating and currently muted.
- **`CanAssignResponsible`**: `isOwnerOrAdmin && canWrite && !isTerminal` (unchanged).
- **`GetActorDisplayNameAsync` trim**: Returns `UserName.Trim()` or `Email.Trim()` for consistency.

### Files changed

| File | Change |
|---|---|
| `Keep.Application/Requests/KeepRequestDetailResult.cs` | `AvailableActionsMetadata` +`CanUnwatch`/`CanUnmute`; `CurrentUserDetailParticipation` record; `KeepRequestParticipantItem` +`IsEligible`; `KeepRequestEventItem` +5 participation fields |
| `Keep.Application/Requests/IKeepRequestDetailPersistence.cs` | `KeepParticipantProjection` +`MembershipStatus` |
| `Keep.Application/Requests/IKeepRequestListPersistence.cs` | `KeepRequestParticipantSummary` +`ResponsibleDisplayName`/`ResponsibleIsStale`; `GetParticipantSummariesAsync` +`accountId` param |
| `Keep.Application/Requests/KeepRequestSummary.cs` | `KeepRequestParticipationInfo` +`ResponsibleDisplayName`/`ResponsibleIsStale`/`CanAssignFromList` |
| `Keep.Application/Requests/KeepRequestDetailMapper.cs` | `ToDetailResult` +`currentUserId`; `MapParticipant` +`IsEligible`; `MapEvent` +participation fields; `MapParticipationAction` added |
| `Keep.Application/Requests/GetKeepRequestDetailService.cs` | 4-flag AvailableActions; `canWrite` gates; `currentUser.UserId` to mapper |
| `Keep.Application/Requests/AcknowledgeAttentionService.cs` | 4-flag AvailableActions; `currentUser.UserId` to mapper |
| `Keep.Application/Requests/ManageResponsibleService.cs` | `BuildDetailAsync` 4-flag AvailableActions |
| `Keep.Application/Requests/ManageWatcherService.cs` | `BuildDetailAsync` 4-flag AvailableActions |
| `Keep.Application/Requests/SelfWatchService.cs` | `BuildDetailAsync` 4-flag AvailableActions |
| `Keep.Application/Requests/MuteService.cs` | `BuildDetailAsync` 4-flag AvailableActions |
| `Keep.Application/Requests/AddInternalNoteService.cs` | 4-flag AvailableActions; `currentUser.UserId` to mapper |
| `Keep.Application/Requests/AddBusinessUpdateService.cs` | +using Enums; 4-flag AvailableActions; `currentUser.UserId` to mapper |
| `Keep.Application/Requests/ChangeKeepRequestStatusService.cs` | +2 usings; 4-flag AvailableActions; `currentUser.UserId` to mapper |
| `Keep.Application/Requests/LogExternalContactService.cs` | +using Enums; 4-flag AvailableActions; `currentUser.UserId` to mapper |
| `Keep.Application/Requests/GetKeepRequestListService.cs` | `BuildParticipationInfo` +`responsibleDisplayName`/`responsibleIsStale`/`canAssignFromList`; passes `currentUser.AccountId` to persistence |
| `Keep.Infrastructure/Persistence/EfKeepRequestDetailPersistence.cs` | Fetches `MembershipStatus` in participant query |
| `Keep.Infrastructure/Persistence/EfKeepRequestOperatePersistence.cs` | `GetActorDisplayNameAsync` → `User.Name.Trim() ?? Email.Trim()` |
| `Keep.Infrastructure/Persistence/KeepRequestListPersistence.cs` | `GetParticipantSummariesAsync` — account-scoped, effective count, stale logic |
| `UnitTests/Keep/KeepRequestListServiceTests.cs` | `FakeRequestListPersistence.GetParticipantSummariesAsync` +`accountId` param |
| `IntegrationTests/Api/KeepRequestParticipationApiTests.cs` | +8 integration tests covering 3C read-model assertions |

---

## Phase 8-B5 Session 4 — Filters, Search, Closed History, Pagination — DECISIONS LOCKED

**Status:** Decision gate complete. Ready for bounded implementation sessions.
**ADRs:** 237..260
**Next free ADR:** ADR-261
**Build log:** `docs/build-log/041-phase-8-b5-session-4-filters-search-closed-history-pagination-decisions.md`

### What was decided

Session 4 is a backend/foundation list-navigation slice. It extends `GET /keep/requests` with
named views, server-side filters/search, cursor pagination, role-aware operational counts,
closed/cancelled/all history for Owner/Admin, row/list context metadata, and explicit query
validation while preserving no-query default command-center behavior.

### Deferred from Session 4

- realtime list/count invalidation;
- exact arbitrary totals for search/history/filter combinations;
- broad Operator terminal-history access;
- mark feedback reviewed;
- archive/unarchive;
- contextual customer/address history summaries;
- internal/team-memory search;
- web/PWA and native mobile UI.

---

## Phase 8-B5 Session 3B — Participation API/Application Services — COMPLETE

**Tests:** 690 (396 unit · 14 arch · 280 integration) — all green
**Next free ADR:** ADR-237

### What was built

Five application-layer services + 9 API endpoints + 29 integration tests covering all participation write and candidate-lookup surfaces.

**Bugs found and fixed during 3B testing:**
- `MapEventType` in `KeepRequestDetailMapper` missing `ParticipationChanged = 10` → added `"participation_changed"` mapping.
- Minimal API `MapDelete` with nullable body (`ClearResponsibleRequestBody?`, `WatcherRequestBody?`) causes startup failure (inferred body not allowed on DELETE). Fixed with explicit `[FromBody]` attribute on both DELETE endpoints.

### Files changed

| File | Change |
|---|---|
| `docs/deferred-topics.md` | Updated DEF-045 to document Operator self-assign block in 3B |
| `Keep.Core/Errors/KeepRequestErrors.cs` | Added 4 application-layer participation errors |
| `Keep.Application/Requests/IKeepRequestOperatePersistence.cs` | Added `GetParticipantsForUpdateAsync`, `GetParticipantTargetAsync`, `GetParticipantCandidatesAsync`, `CommitParticipationAsync` + `ParticipantTargetInfo` + `ParticipantCandidateRecord` types |
| `Keep.Application/Requests/KeepRequestDetailMapper.cs` | `MapRole` → `internal`; added `"participation_changed"` to `MapEventType` switch |
| `Keep.Infrastructure/Persistence/EfKeepRequestOperatePersistence.cs` | Implemented 4 new methods |
| `Keep.Application/Requests/ManageResponsibleService.cs` | New — `SetAsync` (Owner/Admin), `ClearAsync` (Owner/Admin); Operator blocked for both |
| `Keep.Application/Requests/ManageWatcherService.cs` | New — `AddAsync`, `RemoveAsync` (Owner/Admin, stale-safe policy) |
| `Keep.Application/Requests/SelfWatchService.cs` | New — `WatchAsync`, `UnwatchAsync` (all operators) |
| `Keep.Application/Requests/MuteService.cs` | New — `MuteAsync`, `UnmuteAsync` (all operators) |
| `Keep.Application/Requests/GetParticipantCandidatesService.cs` | New — Owner/Admin only, OffSeason read posture |
| `Api/Helpers/ErrorHttpMapper.cs` | Added 9 participation error HTTP mappings |
| `Api/Keep/ParticipationRequest.cs` | New — `SetResponsibleRequestBody`, `ClearResponsibleRequestBody`, `WatcherRequestBody` |
| `Api/Program.cs` | Registered 5 services; added 9 endpoints; `[FromBody]` fix on 2 DELETE endpoints |
| `IntegrationTests/Api/KeepRequestParticipationApiTests.cs` | New — 29 integration tests across all 5 services + candidates |

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
| `Keep.Core/Domain/ParticipationChangeOutcome.cs` | New: factory-only outcome record |
| `Keep.Core/Domain/KeepRequestParticipationService.cs` | New: domain service — 8 methods, participant-set invariant validation |
| `Keep.Infrastructure/.../KeepRequestEventConfiguration.cs` | EF config for 7 new nullable participation fields |
| `Keep.Infrastructure/.../KeepRequestParticipantConfiguration.cs` | Filtered unique index for one-active-Responsible per request |
| `Foundation.Infrastructure/Migrations/20260618102937_AddParticipationChangedEventFields.cs` | Migration |
| `UnitTests/Keep/KeepRequestParticipationTests.cs` | New: 41 unit tests |

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

---

## Phase 8-B5 Session 1 — COMPLETE

**Pre-work complete**
**Tests:** 537/537 (299 unit · 14 arch · 224 integration)
**ADRs in scope:** 164..218
**Next free ADR:** 219
**Build logs:** `docs/build-log/038-phase-8-b5-request-list-triage-external-contact-decisions.md` (decisions) · `docs/build-log/039-phase-8-b5-claude-coding-sessions.md` (implementation spec)

---

### Session 2A — External Contact Schema + Domain — COMPLETE

**Tests:** 355 unit · 14 arch (integration tests pending migration apply)
**Migration:** `20260617224828_AddExternalContactEventFields` — 5 nullable columns on `keep_request_events`
**Next free ADR:** ADR-219 (unchanged)

---

### Session 2D — External Contact Completion Gate — COMPLETE

**Tests:** 355 unit · 14 arch · 251 integration (620 total — all green)
**Next free ADR:** ADR-222
**No new code written** — verification and docs only.

---

### Session 2C — OffSeason Freeze — COMPLETE

**Tests:** 355 unit · 14 arch · 251 integration (620 total)
**ADR implemented:** ADR-221
**Next free ADR:** ADR-222

---

### Session 2B — External Contact API + Detail Timeline — COMPLETE

**Tests:** 355 unit · 14 arch · 240 integration (609 total)
**ADRs implemented:** 197, 199, 200, 202, 203, 207, 209, 211, 215, 216; new ADR-219, ADR-220
**Next free ADR:** ADR-221

---

### Session 2 discovery checkpoint — External Contact Logging

**Status:** Decisions ADR-196..218 locked. Ready for implementation in bounded sessions.

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
- **External contact logging/capture** implemented in B5 Sessions 2A-2C.
- **`businessName ?? string.Empty`** — persistence returns null if account missing post-auth; never expected in production.
- **`KeepResponsePolicy` defaults** (first=60, standard=240, priority=60 min) apply when no policy row exists for an account; silent fallback by design.
- **Negative feedback on Closed raises attention** — intentional exception to terminal-no-attention posture (ADR-138).
- **Feedback `WasResolved` is `bool?` at API layer** — null signals missing flag, validated before service. Domain method takes `bool`.
- **Always rebuild before running tests** — `dotnet test --no-build` can run stale assemblies that mask real failures.
- **Next free ADR: ADR-261.**
- **B4 mapper signature:** `ToDetailResult` now takes `AccountUserRole role`, `bool canOperate`, `Guid currentUserId` — all callers updated; write services pass `canOperate: true`.
- **Participant `DisplayName`** computed in persistence (two-query approach retained; User.Name projected in the AccountUsers query via EF navigation LEFT JOIN).
- **`KeepRequestStatus.Scheduled = 7`** — added to `MapStatus` in B5 service rewrite.
- **`FirstResponseDueAtUtc` gap** — fixed in B5. `KeepRequest.Create` now requires `firstResponseTargetMinutes` (required param, position 10). All call sites updated.
- **Post-close ranking fix** — `isPostClose` must be checked before priority band in `ComputeRankingGroup`.
- **`post_customer_update.ClearsAttention`** — state-aware, not static.
- **`ComputeSeverity` first-response-pending** — fixed in B5 completion. Now returns `"attention"` for first-response pending.
- **`CanLogExternalContact` OffSeason gap** — Fixed in 2C.
- **`ExternalContactInvalidDirection` added in 2B** — was omitted from the 2A error set despite being in ADR-207 scope.
- **External contact `ExternalContactChannel` in DTO** — both `CommunicationChannel` and `ExternalContactChannel` on `KeepRequestEventItem`.
- **Session 3B: Operator self-assign blocked** — DEF-045; any Operator `PUT /responsible` returns 403 `ParticipationOperatorCannotAssignOther`. Unblock when Unassigned/Available queue exists.
- **Session 3B: `GetActorDisplayNameAsync`** — fixed in 3C to use `User.Name.Trim() ?? Email.Trim()`, matching `GetParticipantTargetAsync` convention.
- **Session 3C: 4-flag participation metadata** — `CanWatch` (not yet participating), `CanUnwatch` (currently watching), `CanMute` (participating + notifications on), `CanUnmute` (participating + notifications off). All 4 require `!IsTerminal`. `CanAssignResponsible` requires `isOwnerOrAdmin && canWrite && !IsTerminal`.
- **Minimal API DELETE body** — nullable body parameters on `MapDelete` endpoints require `[FromBody]`; without it the app fails to start at route data source initialization. Fixed on `DELETE /responsible` and `DELETE /watchers/{id}`.
- **`MapEventType` must be exhaustive** — `ParticipationChanged = 10` was missing; found during 3B testing. Pattern: every new `KeepRequestEventType` value must be added to `MapEventType` before integration tests run against any service that commits that event type.
- **Session 3D** — cross-slice verification, docs update, and completion gate. Next session.
