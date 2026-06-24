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

**Status:** Complete. Commit pending Christian approval.

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

**Verified:** 759 unit, 14 arch — all green. `dotnet build` succeeded.

---

## P6f-2 — Ready-to-close queue — PENDING

**Scope:**

- Add `ReadyToClose` to `ActiveViewKind`.
- Add `ReadyToClose` count to `KeepRequestViewCounts`.
- Add `ReadyToCloseInfo` nested record to `KeepRequestSummary` (customer-activity warning signal).
- `GetKeepRequestListService`: handle `ready_to_close` view — in-memory eligibility is
  `Status==Resolved && AttentionLevel==None` (matches `CanClose` gate); DB pre-filter is
  non-terminal + AttentionLevel==None.
- `KeepRequestListPersistence`: `view=ready_to_close` parameter mapping + DB pre-filter.
- Customer-activity warning: `ReadyToCloseInfo.HasCustomerActivityAfterResolution` =
  `LastCustomerActivityAt > LastBusinessActivityAt` when Status==Resolved. Mirrors DEF-063.

**DEF-036 / DEF-063 contract to finalize during this slice:**
- Queue pre-filter: Resolved + AttentionLevel==None.
- Warning signal on rows: `LastCustomerActivityAt > LastBusinessActivityAt`.
- Sort: no special sort required (use standard creation order or attention order).
- Cursor: new sentinel value (not 97 NeedsAttention or 98 NeedsStatusCheck).

**Files (production, ~6):**
1. `src/OpHalo.Keep.Application/Requests/IKeepRequestListPersistence.cs` — add `ReadyToClose`
2. `src/OpHalo.Keep.Application/Requests/GetKeepRequestListResult.cs` — add `ReadyToClose` count
3. `src/OpHalo.Keep.Application/Requests/KeepRequestSummary.cs` — add `ReadyToCloseInfo`
4. `src/OpHalo.Keep.Application/Requests/GetKeepRequestListService.cs` — ReadyToClose view path
5. `src/OpHalo.Keep.Infrastructure/Persistence/KeepRequestListPersistence.cs` — param + DB filter
6. `src/OpHalo.Api/Program.cs` or API layer — `view=ready_to_close` param (if not already handled)

Plus unit tests (~8) and integration test (~3).
