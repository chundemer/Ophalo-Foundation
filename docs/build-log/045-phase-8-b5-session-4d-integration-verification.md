# Build Log 045 — Phase 8-B5 Session 4D Integration Verification

**Phase:** 8-B5 Session 4D — Integration Verification, Docs, Decision Index, Deferred Tracker
**Date:** 2026-06-19
**Status:** Complete
**Preceding build logs:** `041` (Session 4 decisions) · `042` (Session 5 decisions) · `043` (V1 scope lock) · `044` (Pilot support)
**ADRs marked Implemented this session:** ADR-237..260, ADR-287

---

## Purpose

Session 4D is the completion gate for Sessions 4A–4C. It verifies the full test suite, confirms the deferred boundary is clean, indexes all Session 4 decisions as Implemented, and closes the two deferred topics that Session 4 resolved.

---

## Test Suite Result

**803 total — all green**

| Suite | Count |
|---|---|
| Unit | 463 |
| Architecture | 14 |
| Integration | 326 |
| **Total** | **803** |

---

## Verification Pass

### Full suite
`dotnet test --verbosity minimal` — 803/803 pass, 0 failed, 0 skipped.

### Deferred boundary
No Session 5 work was accidentally included:
- `POST /keep/requests/{requestId}/feedback-review` endpoint is **not** registered in `Program.cs`.
- No `FeedbackReviewedAtUtc`, `FeedbackReviewedByAccountUserId`, or `FeedbackReviewNote` fields exist in any entity or DTO.
- `FeedbackReview` references in list files are the `feedback_review` named-view string only (Session 4 scope).

### Error code retention
`RequestListViewNotYetAvailable` is retained in `KeepRequestErrors.cs` and `ErrorHttpMapper.cs` but is no longer returned by any service path — the Operator unassigned gate was removed in 4C as planned.

### Cursor sentinels
`HistorySortSentinel = 0` and `FeedbackReviewSortSentinel = 99` are present in `GetKeepRequestListService`. Neither collides with B5 ranking groups (1–8).

### HMAC key
`Keep:RequestListCursorSigningKey` is wired in `KeepApiWebFactory` (32-byte all-zeros deterministic test key). Production must supply a real key via config.

### OffSeason on the list path
`GetKeepRequestListService` sets `RequestImplementsAllowedInOffSeason: true` (list read is not blocked) and derives `isOffSeason` from `accountSnapshot.OperatingMode`. OffSeason suppresses:
- notification eligibility (`suppression_reason = "off_season"`)
- `CanSelfAssignFromList` (formula includes `!isOffSeason`)
- `CanAssignFromList` (formula includes `canOperate && !isOffSeason`)

### Integration test coverage (4A–4C)
Key contracts verified by integration tests in `KeepRequestListQueryApiTests.cs`:
- Auth gate (401 unauthenticated)
- Response shape (200 with `requests`, `pageInfo`, `viewCounts`, `listContext`)
- All 400 validation paths (unknown view, bad date, contradictory view/filter, invalid status, invalid attentionReason, bad limit, bad cursor)
- Cursor pagination with `limit + 1` slicing, `hasMore`, `nextCursor`
- Cursor follow (continuation returns remaining rows, `hasMore = false`)
- HMAC integrity (tampered signature → 400, tampered payload → 400)
- `view=default` / no-view cursor interoperability
- `Operator_unassigned_view_returns_200`
- `rowContext` field present on each row

Unit test coverage in `KeepRequestListServiceTests.cs`: 80+ test methods covering all validation paths, all rowContext scenarios, all `CanSelfAssignFromList` conditions, pagination, cursor fingerprint, view dispatch routing, list-context flags, view counts, and OffSeason notification suppression.

---

## Sessions 4A–4C Summary

### Session 4A — Query Contract, Validation, Response Shape, Cursor/Page Primitives

**12 files** — `KeepRequestListQuery`, `KeepRequestListCursor`, `IKeepRequestListCursorProtector`, `HmacKeepRequestListCursorProtector`, `GetKeepRequestListResult` (updated), `GetKeepRequestListService` (updated), `KeepRequestListQueryBinding`, `ErrorHttpMapper` (+10 entries), `Program.cs` (query binding + DI), `KeepApiWebFactory` (+HMAC key), plus unit and integration test files. ADR-287 locked.

Key design decisions:
- `KeepRequestListQueryBinding` is a static binder in `OpHalo.Api.Keep` — HTTP structural concerns out of `Program.cs`
- HMAC-SHA256 cursor with `FixedTimeEquals`; fingerprint normalizes `null` view and `"default"` identically
- Validation order: `NormalizeView` → unknown view → `ValidateDateFormats` → status slug → attentionReason slug → `ValidateContradictions` → view role auth → Operator unassigned gate → limit range → cursor decode/fingerprint

### Session 4B — Named Views, Filters/Search, History Visibility, View Counts, Sorting

**7 files** — `KeepRequestErrors` (+3), `IKeepRequestListPersistence` (+3 methods + 3 types), `GetKeepRequestListService` (complete rewrite), `KeepRequestListPersistence` (complete rewrite), `ErrorHttpMapper` (+3 entries), unit tests (updated + 15 new), integration tests (updated + 5 new).

Key additions:
- 9 named views with full server-side dispatch
- `GetActiveViewRequestsAsync` (EXISTS subqueries for participant views), `GetHistoryRequestsAsync` (keyset cursor, `TerminatedAtUtc DESC, Id ASC`), `GetViewCountsAsync` (6 sequential role-aware counts)
- In-memory B5 ranking sort for active views; `FeedbackReviewComparer` (AttentionSinceUtc ASC) for feedback_review
- `HistorySortSentinel = 0`, `FeedbackReviewSortSentinel = 99`
- `IsHistory`, `IsSearch` flags on list context
- `viewCounts` always populated (previously null in 4A)

### Session 4C — Operator Unassigned Surface, Self-Assign Re-enable, Row Context

**7 files** — `KeepRequestErrors` (message update), `KeepRequestSummary` (+`RowContext`, +`CanSelfAssignFromList`), `GetKeepRequestListService` (rowContext/CanSelfAssign logic), `ManageResponsibleService` (targeted self-assign gate), `KeepRequestListPersistence` (unassigned count ungated), unit tests (+14), integration tests (+2, Operator user seeding).

Key additions:
- `ComputeRowContext` static method — 8 values, priority order: feedback_review → closed_history → cancelled_history → needs_attention → first_response → waiting_on_customer → unassigned_available → active_work
- `CanSelfAssignFromList`: `view=="unassigned" && !isOwnerOrAdmin && canOperate && !isOffSeason && !r.IsTerminal && isUnassigned`
- `ManageResponsibleService.SetAsync`: Operator block narrowed to `isOperator && target != self → 403`; stale Responsible + self-assign → 409
- `GetViewCountsAsync`: all roles get real unassigned count

---

## Decision Index Updates

**ADR-237..260** status changed from `Locked` to `Implemented | Sessions 4A-4C` (24 entries).
**ADR-287** was already `Implemented | Session 4A`.

---

## Deferred Tracker Updates

| ID | Update |
|---|---|
| DEF-034 | `Ready for implementation` → `Implemented — Sessions 4A-4C` |
| DEF-045 | `Ready for implementation` → `Implemented — Session 4C` |

---

## Exit Gate

- [x] 803/803 tests pass
- [x] No Session 5 code present
- [x] `RequestListViewNotYetAvailable` retained but unused
- [x] HMAC key wired in test factory
- [x] OffSeason list posture correct (reads open, eligibility/assignment suppressed)
- [x] ADR-237..260 marked Implemented in decision index
- [x] DEF-034, DEF-045 closed in deferred-topics
- [x] Build-log written
- [x] Session-log rewritten (see session-log.md)

**Next free ADR:** ADR-295
**Next session:** Phase 8-B5 Session 5 — Feedback Review Completion (build-log/042, ADRs 261-286)
