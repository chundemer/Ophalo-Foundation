# Build Log 061 — Phase 8-B5 Session 6 Proper

**Started:** 2026-06-23  
**Status:** In progress — P6f-1 complete, P6f-2 next.

Scope: Ready-to-close queue, close permission/affordance, Closed date filter shortcuts,
and closeout hygiene. Session 6 proper resumes after prerequisites (P6b–P6e complete).

Out of scope: notification delivery, archive/unarchive, dispatch/calendar scheduling, batch close,
customer identity portals, backend SMS/email, realtime/SSE, analytics.

---

## Preflight Findings (2026-06-23)

Confirmed from source inspection before P6f-1:

- `ComputeAllowedStatuses(Resolved)` included `Closed` for all actors — Operators should not see
  it per ADR-343. No `CanClose` existed on `KeepRequestActionDecision` or `AvailableActionsMetadata`.
- `AllowedStatuses_Resolved` policy test only covered `OwnerWrite()`; no Operator coverage.
- `ActiveViewKind.ReadyToClose` absent; no `ReadyToClose` count in `KeepRequestViewCounts`; no
  `ReadyToCloseInfo` on `KeepRequestSummary`; no `view=ready_to_close` API parameter.
- `ClosedFrom`/`ClosedTo` already exist in `KeepRequestListFilters` for history views.
- DEF-063 customer-activity warning: `LastCustomerActivityAt`, `LastBusinessActivityAt`,
  `AttentionLevel` all available on `KeepRequest`; exact signal contract deferred to P6f-2.
- Detail next/previous and Close and Next are not pre-decided; deferred beyond P6f-2.

Locked `CanClose` gate (ADR-343): `isOwnerAdmin && Status==Resolved && AttentionLevel==None`.
Owner/Admin must clear attention before close affordance appears. Domain/service path enforces
the same constraint regardless of affordance.

---

## P6f-1 — Close permission + CanClose affordance — COMPLETE

**Status:** Complete. Commits `efacb25` (affordance) + `afe41ed` (review fixes).

**Changes:**
- `KeepRequestActionDecision.cs` — added `CanClose: bool` field.
- `KeepRequestActionPolicy.cs` — `CanClose` computed as `isOwnerAdmin && Status==Resolved &&
  AttentionLevel==None`; `DenyAll` updated with `CanClose: false`; `ComputeAllowedStatuses`
  now accepts `isOwnerAdmin` and excludes `Closed` from `AllowedStatuses` for Operators on Resolved.
- `KeepRequestDetailResult.cs` — `CanClose` added to `AvailableActionsMetadata`.
- `KeepRequestDetailMapper.cs` — `CanClose` wired from decision in `ToAvailableActionsMetadata`.
- `KeepRequestActionPolicyTests.cs` — 7 new tests: Owner/Admin CanClose true, Operator false,
  attention-gated false, active/closed request false; Operator AllowedStatuses excludes Closed.
- `KeepRequestDetailServiceTests.cs` — 2 new tests: CanClose true for Owner+Resolved, false for
  non-Resolved.

**Review fixes (commit `afe41ed`):**
- `KeepRequestErrors.cs` — `CloseBlockedByAttention` (409) added alongside `CloseRequiresOwnerOrAdmin` (403).
- `ErrorHttpMapper.cs` — both new error codes mapped.
- `ChangeKeepRequestStatusService.cs` + `AddBusinessUpdateService.cs` — attention guard added (Operator → 403,
  Resolved+attention → 409). ADR-343 enforced end-to-end; domain path is no longer the sole gate.
- `KeepRequestActionPolicy.cs` — `AllowedStatuses` now driven by `canClose` so `Closed` never appears when
  `CanClose==false` (attention-present or Operator).
- `ChangeKeepRequestStatusTests.cs` — tests 10/11 rewritten: `_resolvedWithAttention` expects 409
  `CloseBlockedByAttention`; new `_resolvedClean` fixture; test 11 → `CloseRequest_Resolved_NoAttention_SetsTerminatedAtUtc`.
- `KeepRequestActionPolicyTests.cs` — `AllowedStatuses_Resolved_OwnerAdmin_with_attention_excludes_Closed`.

**Verified:** 760 unit · 14 arch · 604 integration — full suite green.

---

## P6f-2 — Ready-to-close queue — COMPLETE

**Scope delivered:**

- `ReadyToClose` added to `ActiveViewKind` enum.
- `ReadyToClose: int` added to `KeepRequestViewCounts`.
- `KeepRequestReadyToCloseInfo(bool HasCustomerActivityAfterResolution)` added to
  `KeepRequestSummary`; populated for every row in every view (matches `StatusCheckInfo` precedent).
- `GetKeepRequestListService`: `ready_to_close` added to `ValidViews`, `ActiveOnlyViews`,
  `OwnerAdminOnlyViews`, and `ActiveViewKinds`; `isReadyToClose` flag; in-memory filter
  `Status==Resolved && AttentionLevel==None`; `BuildReadyToCloseInfo` helper.
- `KeepRequestListPersistence`: `ReadyToClose` DB pre-filter (non-terminal + AttentionLevel==None);
  `readyToCloseCount` in `GetViewCountsAsync` (Owner/Admin scoped — 0 for Operator).
- No cursor sentinel needed — ReadyToClose uses standard active-view sort/cursor path.
- `_resolvedRequestId` tracked in B5 fixture; `Resolved_request_included` test fixed to use ID
  (was brittle `SingleOrDefault(status == "resolved")`).

**DEF-036 / DEF-063 finalized:**
- Queue pre-filter: Resolved + AttentionLevel==None (DB: non-terminal + no-attention; in-memory: exact).
- Warning signal: `LastCustomerActivityAt > LastBusinessActivityAt`.
- Sort: standard (no special comparer).
- Cursor: no sentinel — standard active-view path.
- `ready_to_close` is Owner/Admin-only (ADR-343, DEF-036).

**Files changed (9):**
1. `src/OpHalo.Keep.Application/Requests/IKeepRequestListPersistence.cs`
2. `src/OpHalo.Keep.Application/Requests/GetKeepRequestListResult.cs`
3. `src/OpHalo.Keep.Application/Requests/KeepRequestSummary.cs`
4. `src/OpHalo.Keep.Application/Requests/GetKeepRequestListService.cs`
5. `src/OpHalo.Keep.Infrastructure/Persistence/KeepRequestListPersistence.cs`
6. `tests/OpHalo.UnitTests/Keep/KeepRequestListServiceTests.cs` (13 unit tests)
7. `tests/OpHalo.IntegrationTests/Api/KeepRequestListB5Tests.cs` (4 + fixture + 1 fix)
8. `tests/OpHalo.IntegrationTests/Api/KeepRequestListQueryApiTests.cs` (4 integration tests)
9. `docs/session-log.md` (pre-existing dirty)

**Test gate:** 773 unit · 14 arch · 612 integration = 1399 total — full suite green.

---

## P6f-3 — Closed-history date shortcuts — COMPLETE

**Decisions locked (pre-implementation):** UTC-only shortcut resolution (no account timezone, deferred DEF-078);
values `yesterday` and `this_week` only; parameter name `closedShortcut`; mutually exclusive with explicit
`closedFrom`/`closedTo`; valid only on history views; unknown value → new specific error.

**Contract:**
- `closedShortcut=yesterday` → `[UTC midnight yesterday, UTC midnight today)` via `TerminatedAtUtc`
- `closedShortcut=this_week` → `[UTC Monday 00:00Z, UTC tomorrow 00:00Z)` (ISO week, Monday-start)

**Changes:**
- `KeepRequestErrors.cs` — `RequestListInvalidClosedShortcut` added.
- `KeepRequestListQuery.cs` — `ClosedShortcut: string?` field added.
- `KeepRequestListQueryBinding.cs` — `"closedShortcut"` in `KnownParams`; bound in result.
- `KeepRequestListCursor.cs` — `closedShortcut` added to `ComputeFingerprint` canonical shape.
- `GetKeepRequestListService.cs` — `ValidClosedShortcuts` set; step 2b shortcut value validation;
  `ValidateContradictions` extended (shortcut on non-history view → contradictory; shortcut + explicit
  date bounds → contradictory); `ResolveClosedShortcut` helper; shortcut resolved to `closedFrom`/`closedTo`
  in filter-build block after explicit date parsing.
- `KeepRequestListServiceTests.cs` — 5 new tests (unknown shortcut, non-history view, explicit-date conflict,
  yesterday resolution, this_week resolution).
- `KeepRequestListQueryApiTests.cs` — 3 new tests (unknown shortcut 400, non-history 400, yesterday 200).

**Test gate:** 779 unit · 14 arch — unit + arch suite green. Integration focused suite green (44 tests).

---

## P6f-4 — Detail next/previous navigation — COMPLETE

**Design decisions (pre-implementation):**
- `navView` query param on `GET /keep/requests/{requestId}`. Phase 1 supports only `ready_to_close`.
- Unknown navView → `RequestDetailInvalidNavView` (400). Operator navView=ready_to_close → 403.
- All ready-to-close rows are B5 group-7 (resolved_quiet); sort pushed fully to in-process LINQ:
  coalesced `LastBusinessActivityAt ?? LastCustomerActivityAt ?? CreatedAtUtc DESC`, then `Id ASC`.
- `KeepRequestNavigation(PreviousId, NextId, Position, Total)`: null when no navView; Position=0
  when the current request is no longer in the queue.

**Files changed (9 + docs):**
1. `KeepRequestErrors.cs` — `RequestDetailInvalidNavView` added.
2. `KeepRequestDetailResult.cs` — `Navigation: KeepRequestNavigation?` field added; `KeepRequestNavigation`
   record added.
3. `KeepRequestDetailMapper.cs` — `ToDetailResult` gains optional `navigation` param; `Navigation:` field
   in result constructor. All 13 existing callers unaffected (default null).
4. `IKeepRequestDetailPersistence.cs` — `GetReadyToCloseNavigationIdsAsync` added.
5. `EfKeepRequestDetailPersistence.cs` — implemented: filters `Resolved + AttentionLevel.None` by accountId,
   sorts in-process (coalesced last-activity DESC, Id ASC), returns `IReadOnlyList<Guid>`.
6. `GetKeepRequestDetailService.cs` — `navView` param added to `ExecuteAsync`; navView validation + role
   check after scope computation (fail fast); navigation ID fetch + prev/next computation before result
   construction.
7. `Program.cs` — `string? navView` bound from query string on detail endpoint.
8. `ErrorHttpMapper.cs` — `RequestDetailInvalidNavView` → 400.
9. `KeepCreateBusinessRequestServiceTests.cs` — `FakeReadPersistence` stub for new interface method.
10. `KeepRequestDetailServiceTests.cs` — 6 new tests: null nav, middle item, first item, not-in-queue,
    unknown navView, Operator forbidden.
11. `KeepRequestDetailB5Tests.cs` (new) — 6 integration tests: middle/first/last items, no-navView null,
    invalid navView 400, Operator 403.

**Test gate:** 785 unit · 14 arch green. 41 detail integration tests green.

---

## P6f-5 review fixes

**Review findings addressed:**

- **Medium (existing Closed rows):** P6f-5 sets `ExpiresAtUtc` only on future Closed transitions. Any
  Closed rows created before this commit (e.g., earlier test seeds committed to a real database) will
  retain `ExpiresAtUtc = null` and never expire via the guard. **Assumption recorded:** no real
  customer-facing Closed rows exist in pilot data at the time of this commit. This is a pre-pilot
  deployment; if Closed rows exist at migration time a one-off backfill
  (`UPDATE keep_requests SET expires_at_utc = terminated_at_utc + interval '30 days' WHERE status = 'Closed' AND expires_at_utc IS NULL`)
  must be run before launch.

- **Low (navView on non-close status):** The navView block now also checks `parsedStatus != Closed`
  after the unknown-value check and returns `RequestDetailInvalidNavView` (400). navView is strictly
  close-and-next; supplying it on any other status change is rejected. Integration test added:
  `ChangeStatus_navView_on_non_close_status_returns_400`.

- **Low (weak close-and-next assertion):** The integration test now uses two staggered ready-to-close
  seeds — STATUS007 (resolved now+20m, close target, sorts first) and STATUS006 (resolved now+10m,
  stays in queue, sorts second). After closing STATUS007 the test asserts `nextId == STATUS006.Id`
  and `position == 0`. Integration test renamed
  `CloseRequest_with_navView_ready_to_close_returns_nextId_and_position_zero`.

**Files changed (3):**
1. `src/OpHalo.Keep.Application/Requests/ChangeKeepRequestStatusService.cs` — parsedStatus != Closed gate
2. `tests/OpHalo.IntegrationTests/Api/ChangeKeepRequestStatusTests.cs` — STATUS007 seed; STATUS006 now+10m; enhanced + new tests
3. `docs/build-log/061-phase-8-b5-session-6-proper.md`

**Test gate:** 787 unit · 14 arch green. 29 status-change integration tests green.
