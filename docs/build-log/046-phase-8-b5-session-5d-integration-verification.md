# Build Log 046 — Phase 8-B5 Session 5D Integration Verification

**Phase:** 8-B5 Session 5D — Integration Verification, Docs, Decision Index, Deferred Tracker
**Date:** 2026-06-19
**Status:** Complete
**Preceding build logs:** `042` (Session 5 decisions) · `045` (Session 4D verification)
**ADRs marked Implemented this session:** ADR-261..286

---

## Purpose

Session 5D is the completion gate for Sessions 5A–5C. It verifies the full test suite, confirms the deferred boundary is clean, indexes all Session 5 decisions as Implemented, and records the DEF-074 status.

---

## Test Suite Result

**847 total — all green**

| Suite | Count |
|---|---|
| Unit | 494 |
| Architecture | 14 |
| Integration | 339 |
| **Total** | **847** |

---

## Verification Pass

### Full suite
`dotnet test --verbosity minimal` — 847/847 pass, 0 failed, 0 skipped.

### Session 5 boundary — expected code present
All Session 5 artifacts confirmed in place:
- `FeedbackReviewedAtUtc`, `FeedbackReviewedByAccountUserId`, `FeedbackReviewNote` on `KeepRequest` entity and EF configuration.
- `FeedbackReviewAgeBucket` enum, `FeedbackReviewPolicy` domain type in `Keep.Core`.
- `MarkFeedbackReviewedService` in `Keep.Application`.
- `POST /keep/requests/{requestId:guid}/feedback-review` registered in `Program.cs` (line 313).
- `FeedbackReviewAgeBucket`/`FeedbackReviewDueAtUtc` on `KeepRequestSummary`.
- `FeedbackReviewUnavailable`, `FeedbackAlreadyReviewed`, `FeedbackReviewNoteTooLong` in `KeepRequestErrors` and `ErrorHttpMapper`.
- Migration `20260619155507_AddFeedbackReviewFields` — three nullable columns on `keep_requests`.

### OffSeason closed customer page
`ComputeAllowedActions` in `KeepCustomerPageMapper`: `Closed + isOffSeason` case precedes the `feedbackAlreadySubmitted` check. Active-status pages are unchanged. Covered by `GetCustomerPage_OffSeason_ClosedWithPendingFeedback_AllowedActionsEmpty` integration test.

### Feedback review exclusion from reviewed requests
`MarkFeedbackReviewed` clears `UnresolvedFeedback` attention. The existing `AttentionLevel != None` filter in `GetActiveViewRequestsAsync` automatically excludes reviewed requests from the `feedback_review` view and default post-close follow-up. No additional filter was needed.

### Integration test coverage (5A–5C)
Key contracts in `KeepFeedbackReviewApiTests.cs` (10 tests):
- Owner success: attention cleared, `CanMarkFeedbackReviewed = false` on returned detail
- Admin + optional note
- Operator → 403, Viewer → 403, unauthenticated → 401
- Already-reviewed → 409 `FeedbackAlreadyReviewed`
- Positive feedback → 409 `FeedbackReviewUnavailable`
- No feedback → 409 `FeedbackReviewUnavailable`
- Note too long → 400 `FeedbackReviewNoteTooLong`
- Customer-page exclusion (internal event not visible)
- `FeedbackReview_ListView_IncludesAgingMetadata`: all `feedback_review` list rows carry `feedbackReviewAgeBucket` (string in `new`/`aging`/`overdue`) and `feedbackReviewDueAtUtc` (datetime string)

OffSeason coverage in `KeepOffSeasonTests.cs`:
- `MarkFeedbackReviewed_OffSeason_Returns403`
- `GetCustomerPage_OffSeason_ClosedWithPendingFeedback_AllowedActionsEmpty`

Unit test coverage in `KeepRequestFeedbackReviewTests.cs` (31 tests): all eligibility paths, note boundaries, all aging bucket thresholds (exactly 72h = Aging), ArgumentException guards.

---

## Sessions 5A–5C Summary

### Session 5A — Domain, Schema, Migration, Aging Policy

**11 source files** — `KeepRequest` (+`MarkFeedbackReviewed` domain method), `KeepRequestEvent` (+`CreateFeedbackReviewed` factory + `FeedbackReviewed = 11` event type), `FeedbackReviewAgeBucket` enum, `FeedbackReviewPolicy`, `KeepRequestErrors` (+3 errors), `KeepRequestDetailMapper` (+`feedback_reviewed` event mapping), `KeepRequestConfiguration` (+3 nullable columns), migration, model snapshot, plus unit test file.

Key design decisions:
- Eligibility requires Closed + unreviewed negative feedback + active `UnresolvedFeedback` attention (D1: attention-cleared externally → `FeedbackReviewUnavailable`)
- `FeedbackReviewPolicy` in `Keep.Core/Domain/` — pure policy; pilot thresholds (new <24h, aging 24–72h, overdue >72h) centralized for future account-setting replacement
- `FeedbackReviewed` event uses `Content` for optional note; no new event-table columns

### Session 5B — Mark-Feedback-Reviewed Service/API

**7 files** — `MarkFeedbackReviewedService` (new), `MuteService` (`BuildDetailAsync` +`nowUtc` param and `CanMarkFeedbackReviewed`), `FeedbackReviewRequest` DTO, `ErrorHttpMapper` (+3 HTTP entries), `Program.cs` (DI + endpoint), `KeepFeedbackReviewApiTests` (10 tests), `KeepOffSeasonTests` (+1 test).

Key design decisions:
- Owner/Admin-only role check fires immediately after user snapshot load
- OffSeason returns generic `auth.forbidden` — uniform Keep write convention (ADR-276 aspirational error superseded by implemented convention)
- Concurrency race deferred (DEF-074): domain guard catches sequential duplicate; true-race window accepted for V1 pilot

### Session 5C — List/Customer-Page OffSeason and UI-Ready Metadata

**5 files** — `KeepRequestSummary` (+`FeedbackReviewAgeBucket: string?` + `FeedbackReviewDueAtUtc: DateTime?`), `GetKeepRequestListService` (+aging metadata computation in `ToSummary`, +`MapFeedbackReviewAgeBucket` switch), `KeepCustomerPageMapper` (`ComputeAllowedActions` +`isOffSeason` param; `Closed + isOffSeason` case), `KeepOffSeasonTests` (+`ClosedFeedbackPageToken` const + closed/feedback seed + AllowedActions-empty test), `KeepFeedbackReviewApiTests` (+`FeedbackReview_ListView_IncludesAgingMetadata` test).

Key design decisions:
- Aging metadata gated on `isPostClose` (Closed + UnresolvedFeedback + attention raised); null for all other rows
- `Closed + isOffSeason` precedes `feedbackAlreadySubmitted` check in `ComputeAllowedActions`

---

## Decision Index Updates

**ADR-261..286** status changed from `Locked` to `Implemented | Sessions 5A-5C` (26 entries).

---

## Deferred Tracker

**DEF-074** (Keep request write concurrency control) — status unchanged: **still deferred**. Session 5B acknowledged the gap (domain guard prevents sequential duplicate; true-race window accepted for V1 pilot). The correct fix (Npgsql `xmin` OCC or EF row-version token across all Keep write paths) is a cross-cutting infrastructure concern. No update to the tracker entry is needed.

---

## Exit Gate

- [x] 847/847 tests pass
- [x] Session 5 code complete and verified present (domain, service, API, list metadata, customer page)
- [x] `feedback_review` list exclusion handled automatically by existing `AttentionLevel != None` filter
- [x] OffSeason closed customer page returns `AllowedActions = []` (test confirmed)
- [x] ADR-261..286 marked Implemented in decision index
- [x] DEF-074 reviewed — no change needed (still deferred)
- [x] Build-log written
- [x] Session-log rewritten (see session-log.md)

**Next free ADR:** ADR-295
**Next session:** Phase 8-B6 or next build-plan phase
