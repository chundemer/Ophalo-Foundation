# Session Log — OpHalo Foundation

**Last updated:** 2026-06-19
**Branch:** `main` (no remote yet)

---

## Planned Session Queue

These are planned implementation sessions, not completion logs. When a session finishes, replace the
planned item with a normal completed entry that records exact files changed, tests run, bugs found,
and carry-forward notes.

**Implementation quality contract for every planned session:**

- read the current code first and follow existing service, persistence, mapper, error, DI, endpoint,
  and test patterns;
- write production-quality code that preserves locked ADR behavior and fail-closed security posture;
- keep scope bounded to the named session and explicitly report any needed work that belongs in a
  later session or deferred topic;
- add focused unit/integration coverage for new contracts, validation, authorization, and regression
  risk;
- run the targeted tests required by the session and the broader suite when feasible;
- self-review the diff before handoff for bugs, inconsistent naming, untested branches, accidental
  visibility expansion, unnecessary schema churn, and deferred work accidentally pulled in;
- report any discovered bugs, gaps, or decision conflicts in `docs/session-log.md` instead of
  silently guessing policy.

| Order | Session | Status | Source / Gate |
|---|---|---|---|
| 1 | Phase 8-B5 Session 4C — Operator Unassigned Surface + Narrow Self-Assign Re-enable, Row Context/Action Metadata | Planned | `docs/build-log/041-phase-8-b5-session-4-filters-search-closed-history-pagination-decisions.md` |
| 2 | Phase 8-B5 Session 4D — Integration Verification, Docs, Decision Index, Deferred Tracker | Planned | `docs/build-log/041-phase-8-b5-session-4-filters-search-closed-history-pagination-decisions.md` |

---

## Phase 8-B5 Session 4B — Named Views, Filters/Search, History Visibility, View Counts, Sorting — COMPLETE

**Tests:** 784 total (449 unit · 14 arch · 321 integration) — all green
**Next free ADR:** ADR-288

### What was built

Session 4B replaced the two 4A placeholder gates (`HasFilters → FilterNotYetAvailable` and `view != default → ViewNotYetAvailable`) with real server-side execution across 7 files.

| File | Change |
|---|---|
| `Keep.Core/Errors/KeepRequestErrors.cs` | +3 errors: `RequestListInvalidStatus`, `RequestListInvalidAttentionReason`, `RequestListHistoryViewForbidden` |
| `Keep.Application/Requests/IKeepRequestListPersistence.cs` | +3 methods: `GetActiveViewRequestsAsync`, `GetHistoryRequestsAsync`, `GetViewCountsAsync`; +3 types: `ActiveViewKind`, `HistoryViewKind`, `KeepRequestListFilters` |
| `Keep.Application/Requests/GetKeepRequestListService.cs` | Complete rewrite: named view dispatch, status/attentionReason slug validation, contradiction updates (`feedback_review + non-Closed` and `closedFrom/closedTo + non-history`), history keyset pagination, in-memory B5 ranking sort, `FeedbackReviewComparer`, view counts always populated, `IsHistory`/`IsSearch` in list context |
| `Keep.Infrastructure/Persistence/KeepRequestListPersistence.cs` | Complete rewrite: `GetActiveViewRequestsAsync` (EXISTS subqueries for participant views), `GetHistoryRequestsAsync` (keyset cursor, TerminatedAtUtc guard), `GetViewCountsAsync` (6 sequential counts, role-aware), `ApplyCommonFilters` (q search with FeedbackComment gated by `IsOwnerOrAdmin`) |
| `Api/Helpers/ErrorHttpMapper.cs` | +3 explicit entries for `RequestListInvalidStatus` (400), `RequestListInvalidAttentionReason` (400), `RequestListHistoryViewForbidden` (403) |
| `UnitTests/Keep/KeepRequestListServiceTests.cs` | Updated: removed 4A filter gate tests; renamed 4 persistence-contract tests to use `IsOwnerOrAdmin`; changed `Assert.Null(ViewCounts)` to `Assert.NotNull`; added 15 new tests (invalid slug, filter/search pass-through, closedFrom, dispatch routing, list-context flags, view counts) |
| `IntegrationTests/Api/KeepRequestListQueryApiTests.cs` | Updated: removed 4A placeholder assertions; replaced 5 stale tests with 4B contracts; added 5 new tests (named view 200, history view 200, closedFrom contradictory, invalid status 400, invalid attentionReason 400, q 200) |

### Named view dispatch

| View | Kind | DB filter | Sort |
|---|---|---|---|
| `default` | Active | open + Owner/Admin unresolved feedback closed | in-memory B5 ranking |
| `assigned_to_me` | Active | active + Responsible EXISTS | in-memory B5 ranking |
| `watching` | Active | active + Watching EXISTS | in-memory B5 ranking |
| `unassigned` | Active | active + no Responsible | in-memory B5 ranking; Operator gate (4B: `ViewNotYetAvailable`) |
| `needs_attention` | Active | active + `AttentionLevel != None` | in-memory B5 ranking |
| `feedback_review` | Active | closed + UnresolvedFeedback + attention raised | Owner/Admin only; FeedbackReviewComparer (AttentionSinceUtc ASC) |
| `closed_history` | History | `TerminatedAtUtc != null && Closed` | DB keyset `TerminatedAtUtc DESC, Id ASC` |
| `cancelled_history` | History | `TerminatedAtUtc != null && Cancelled` | DB keyset |
| `all_history` | History | `TerminatedAtUtc != null && (Closed or Cancelled)` | DB keyset |

### Cursor sentinel values

- `HistorySortSentinel = 0` — marks a history keyset cursor (RankingOrder field)
- `FeedbackReviewSortSentinel = 99` — marks a feedback_review cursor; SecondaryTick = AttentionSinceUtc ticks

### Validation order (updated, locked)

`NormalizeView` → unknown view → `ValidateDateFormats` → status slug → attentionReason slug → `ValidateContradictions` → view role auth → Operator unassigned gate → limit range → cursor decode/fingerprint

Key changes from 4A: `HasFilters` gate removed; status/attentionReason slug validation inserted before contradictions; history view role auth (`RequestListHistoryViewForbidden → 403`) inserted after contradictions.

### Role access

- Owner/Admin: all views accessible
- Operator: `feedback_review`, `closed_history`, `cancelled_history`, `all_history` → 403; `unassigned` → `ViewNotYetAvailable` (4C removes); all other active views → 200
- Viewer: treated as non-admin; same view restrictions as Operator

### 4B placeholders remaining (for 4C)

- Operator `view=unassigned` returns `RequestListViewNotYetAvailable` (4C adds eligibility filtering and removes gate)
- `GetViewCountsAsync`: Operator unassigned count returns 0 (same gate)

---

## Phase 8-B5 Session 4A — Query Contract, Validation, Response Shape, Cursor/Page Primitives — COMPLETE

**Tests:** 765 total (434 unit · 14 arch · 317 integration) — all green
**Next free ADR:** ADR-288

### What was built

Session 4A implemented the query contract, cursor/page primitives, explicit validation, and response shape for `GET /keep/requests`. The no-query default command-center behavior is preserved; all new query params are optional. Server-side filter/search execution is deferred to 4B.

**12 files written / updated:**

| File | Change |
|---|---|
| `Keep.Core/Errors/KeepRequestErrors.cs` | +10 errors: 7 list query errors + `RequestListInvalidAssignedAccountUserId`, `RequestListUnknownParameter`, `RequestListDuplicateParameter` |
| `Keep.Application/Requests/KeepRequestListQuery.cs` | New — query contract record (View, Status, AttentionReason, AssignedAccountUserId, Q, CreatedFrom, CreatedTo, ClosedFrom, ClosedTo, Limit, Cursor) |
| `Keep.Application/Requests/IKeepRequestListCursorProtector.cs` | New — `Protect(string) string` + `TryUnprotect(string, out string?) bool` interface |
| `Keep.Application/Requests/KeepRequestListCursor.cs` | New — `Encode`, `TryDecode`, `ComputeFingerprint` static helpers; `KeepRequestListCursorPayload` record |
| `Keep.Application/Requests/GetKeepRequestListResult.cs` | Updated — added `KeepRequestPageInfo`, `KeepRequestViewCounts?`, `KeepRequestListContext`; result ctor updated to 4 params |
| `Keep.Application/Requests/GetKeepRequestListService.cs` | Updated — `IKeepRequestListCursorProtector` as 7th constructor param; `ExecuteAsync(KeepRequestListQuery? query = null, CancellationToken ct = default)`; full validation pipeline + cursor skip + limit+1 slicing |
| `Keep.Infrastructure/Cursors/HmacKeepRequestListCursorProtector.cs` | New — HMAC-SHA256 with `FixedTimeEquals`; Base64Url encode/decode; reads `Keep:RequestListCursorSigningKey` from config |
| `Api/Helpers/ErrorHttpMapper.cs` | +10 explicit 400 entries for all new request-list error codes |
| `Api/Keep/KeepRequestListQueryBinding.cs` | New — case-insensitive key normalization; unknown param, duplicate param, invalid limit, invalid GUID detection |
| `Api/Program.cs` | Updated `GET /keep/requests` to bind via `KeepRequestListQueryBinding`; lazy DI factory for `IKeepRequestListCursorProtector`; removed duplicate `using OpHalo.Api.Keep` |
| `IntegrationTests/Api/KeepApiWebFactory.cs` | +`Keep:RequestListCursorSigningKey` in `AddInMemoryCollection` (32-byte all-zeros deterministic key) |
| `UnitTests/Keep/KeepRequestListServiceTests.cs` | Rewrite — updated `BuildSut` with `FakeCursorProtector`; +tests for all validation paths, pagination, cursor follow, fingerprint equivalence |
| `IntegrationTests/Api/KeepRequestListQueryApiTests.cs` | New — 25 HTTP tests: auth gate, 200 shape, all 400 codes, pagination, cursor follow, HMAC tamper (sig + payload), view=default equivalence |

### Bugs found and fixed during implementation

Five design issues were caught during review before each file was written:

1. **Date validation shadowed by filter gate** — `createdFrom=banana` was returning `FilterNotYetAvailable` instead of `InvalidDateFormat` because the filter gate ran first. Fixed by inserting `ValidateDateFormats` before the filter gate in the validation pipeline.

2. **Fingerprint divergence for null vs "default" view** — `GET /keep/requests` and `GET /keep/requests?view=default` produced different fingerprints, making cursors invalid across equivalent queries. Fixed in `KeepRequestListCursor.ComputeFingerprint`: `string.IsNullOrWhiteSpace(view) ? "default" : view.Trim().ToLowerInvariant()`.

3. **Contradiction check shadowed by view-executability gate** — `view=closed_history&status=received` returned `ViewNotYetAvailable` before the contradiction check could run. Fixed by inserting `ValidateContradictions` before the view-executability gate.

4. **Too much parsing in Program.cs** — limit parsing, GUID parsing, unknown-param and duplicate-param detection were accumulating in the endpoint handler. Extracted into `KeepRequestListQueryBinding`.

5. **Wrong errors for binder cases** — `assignedAccountUserId=banana` returned `FilterNotYetAvailable`; `?limit=10&limit=20` silently took first; unknown params were silently ignored; case-variant duplicates were not detected. Fixed by adding three new error codes and using a case-insensitive dictionary with `TryAdd`.

6. **Integration test `code` field** — `ProblemBody.Extensions` deserializes as a nested dict, but `code` lands at the top level of ProblemDetails JSON (existing watch-out, ADR-irrelevant). Fixed `GetErrorCodeAsync` to use `JsonElement.GetProperty("code")`.

### Validation order (locked)

`NormalizeView` → unknown view → `ValidateDateFormats` → `ValidateContradictions` → `HasFilters` (4A gate) → view executability (4A gate) → limit range → cursor decode/fingerprint

### Cursor contract (locked)

- Format: `base64url(UTF8(payloadJson)) + "." + base64url(HMAC-SHA256(key, payloadBytes))`
- Fingerprint: SHA-256 of canonical normalized query JSON (view/status/attentionReason lowercased; null-view = "default"; excludes limit/cursor)
- Config key: `Keep:RequestListCursorSigningKey` (base64 string; read lazily from `IConfiguration`; fail-hard if missing outside Testing)
- Unit tests: `FakeCursorProtector` (plain Base64, no HMAC); HMAC integrity covered by integration tests

### listContext (4A default path)

`{ view: "default", isDefaultCommandCenter: true, isHistory: false, isSearch: false }` — named views return view-specific context in 4B.

### viewCounts

`null` in 4A; real count queries wired in 4B.

---

## Phase 8-B5 Session 3D — Assignment/Watch/Mute Completion Gate — COMPLETE

**Tests:** 702 (396 unit · 14 arch · 292 integration) — all green
**Next free ADR:** ADR-261

### What was done

Session 3D was a verification and documentation gate for Sessions 3A–3C.

**Verification pass — all clean:**
- Full test suite: 702/702 pass
- Migration (`20260618102937_AddParticipationChangedEventFields`): exactly 7 nullable event fields + filtered unique Responsible index; no extra schema changes
- Customer-page exclusion: `CustomerPage_ExcludesParticipationChangedEvents` test present and passing
- OffSeason blocking: all 4 write services have `RequestImplementsAllowedInOffSeason: false` and `|| decision.IsReadOnly` in `AuthAsync`; fires before request load or target user ID validation
- Deferred boundary: no notification delivery/outbox/device tokens, no Operator queue, no list-level clear/watch/mute, no auto stale-participant cleanup in any participation service
- Endpoints: exactly 9 registered (1 GET candidates + 8 writes); no list-level controls
- ADR-222..235: all marked `Implemented` in decision index

**Gap closed:**
OffSeason integration tests for participation endpoints were absent (`KeepOffSeasonTests.cs` covered 5 write services but not any of the 8 participation write endpoints). Added 4 representative tests: `PutResponsible_OffSeason_Returns403`, `PutWatcher_OffSeason_Returns403`, `PutWatch_OffSeason_Returns403`, `PutMute_OffSeason_Returns403`.

**Doc updates:**
- `DEF-033` updated to `Implemented — Sessions 3A-3D` with full implementation summary
- This session log

### Files changed

| File | Change |
|---|---|
| `IntegrationTests/Api/KeepOffSeasonTests.cs` | +4 OffSeason participation write tests |
| `docs/deferred-topics.md` | DEF-033 → Implemented |
| `docs/session-log.md` | Rewritten for 3D |

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

## Phase 8-B5 Session 3C — Participation Read Models, Timeline, List Assignment Metadata — COMPLETE

**Tests:** 698 (396 unit · 14 arch · 288 integration) — all green
**Next free ADR:** ADR-261

### What was built

Session 3C completed the participation read-model surface:

- **`AvailableActionsMetadata`** expanded to 4 participation flags: `CanWatch`, `CanUnwatch`, `CanMute`, `CanUnmute` — with precise semantics replacing the original 2-flag approach (`CanWatch` means "can start watching, not currently participating"; `CanUnwatch/Mute/Unmute` are state-specific).
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
- **`Results.Problem` extension shape:** extension dict entries land at the top level of ProblemDetails JSON, not under an `"extensions"` key. Test assertions must use `ReadFromJsonAsync<JsonElement>()` then `.GetProperty("code").GetString()`.
- **External contact logging/capture** implemented in B5 Sessions 2A-2C.
- **`businessName ?? string.Empty`** — persistence returns null if account missing post-auth; never expected in production.
- **`KeepResponsePolicy` defaults** (first=60, standard=240, priority=60 min) apply when no policy row exists for an account; silent fallback by design.
- **Negative feedback on Closed raises attention** — intentional exception to terminal-no-attention posture (ADR-138).
- **Feedback `WasResolved` is `bool?` at API layer** — null signals missing flag, validated before service. Domain method takes `bool`.
- **Always use `dotnet build --verbosity minimal`** — `dotnet build -q` is passed to MSBuild as `-q` (question build) and fails; `--verbosity minimal` is the correct quiet mode.
- **Next free ADR: ADR-288.**
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
- **Session 3B: Operator self-assign blocked** — DEF-045; any Operator `PUT /responsible` returns 403 `ParticipationOperatorCannotAssignOther`. Unblock when Unassigned/Available queue exists (Session 4).
- **Session 3B: `GetActorDisplayNameAsync`** — fixed in 3C to use `User.Name.Trim() ?? Email.Trim()`, matching `GetParticipantTargetAsync` convention.
- **Session 3C: 4-flag participation metadata** — `CanWatch` (not yet participating), `CanUnwatch` (currently watching), `CanMute` (participating + notifications on), `CanUnmute` (participating + notifications off). All 4 require `!IsTerminal`. `CanAssignResponsible` requires `isOwnerOrAdmin && canWrite && !IsTerminal`.
- **Minimal API DELETE body** — nullable body parameters on `MapDelete` endpoints require `[FromBody]`; without it the app fails to start at route data source initialization. Fixed on `DELETE /responsible` and `DELETE /watchers/{id}`.
- **`MapEventType` must be exhaustive** — `ParticipationChanged = 10` was missing; found during 3B testing. Pattern: every new `KeepRequestEventType` value must be added to `MapEventType` before integration tests run against any service that commits that event type.
- **OffSeason participation write blocking** — all 4 participation write services (`ManageResponsibleService`, `ManageWatcherService`, `SelfWatchService`, `MuteService`) use `RequestImplementsAllowedInOffSeason: false` and `|| decision.IsReadOnly` in `AuthAsync`; fires before request load or target user ID validation. Covered by `KeepOffSeasonTests` from 3D onward.
- **4A validation order** — updated in 4B; current locked order: `NormalizeView` → unknown view → `ValidateDateFormats` → status slug → attentionReason slug → `ValidateContradictions` → view role auth → Operator unassigned gate → limit range → cursor decode/fingerprint.
- **4A cursor fingerprint normalization** — null view and "default" produce identical fingerprints. Cursor from `GET /keep/requests` is reusable with `GET /keep/requests?view=default`.
- **4A `KeepRequestListQueryBinding`** — static class in `OpHalo.Api.Keep`; handles HTTP structural concerns; `Program.cs` stays thin. ADR-287.
- **4A HMAC key** — `Keep:RequestListCursorSigningKey` must be present in all environments; test factory uses 32-byte all-zeros key.
- **4B cursor sentinels** — `HistorySortSentinel = 0` (history keyset cursor), `FeedbackReviewSortSentinel = 99` (feedback_review in-memory cursor). Sentinel values must not collide with B5 ranking groups (1–8).
- **4B history keyset** — `WHERE TerminatedAtUtc != null` guard on history queries even though terminal records should always have this set (data invariant protection). `ORDER BY TerminatedAtUtc DESC, Id ASC`. Keyset: `WHERE terminated_at < cursorAt OR (terminated_at = cursorAt AND id > cursorId)`.
- **4B Operator unassigned gate** — `view=unassigned` returns `RequestListViewNotYetAvailable` for Operators in 4B; 4C removes gate and adds eligibility filtering. `GetViewCountsAsync` returns `Unassigned: 0` for Operators in 4B for same reason.
- **4B `feedback_review` authorization** — `RequestListHistoryViewForbidden → 403` for Operators (not 400). Applied to: `feedback_review`, `closed_history`, `cancelled_history`, `all_history`. These view names are public API contract and drive the explicit 403.
- **4B `FeedbackComment` visibility** — included in Q search only when `filters.IsOwnerOrAdmin = true` in the EF LINQ predicate. Operator/Viewer Q search does not scan feedback comments.
- **4B view counts always populated** — `GetViewCountsAsync` is called on every request in 4B; `viewCounts` is no longer null in the response.
- **Next session: Phase 8-B5 Session 4C** — Operator Unassigned Surface + Narrow Self-Assign Re-enable, Row Context/Action Metadata.
