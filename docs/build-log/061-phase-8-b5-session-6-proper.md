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
