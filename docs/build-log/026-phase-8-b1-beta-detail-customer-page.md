# Phase 8-B1-β — Keep Request Detail + Customer Page

**Status:** Complete.
**Build-log preceding this:** 025-phase-8-b1-alpha-domain-ef-schema.md
**Date:** 2026-06-16

---

## Purpose

Add two read endpoints to the B1 slice: the authenticated operator detail view
(`GET /keep/requests/{requestId}`) and the anonymous customer page
(`GET /keep/r/{pageToken}`). No writes. No mutations. Read-only shape for B2/B4 to build on.

---

## What Was Built

**Keep.Core — errors (1 file modified):**
- `Errors/KeepRequestErrors.cs` — added `Forbidden` (future B4 fine-grained access control;
  unused in this slice but in the confirmed B1-β scope).

**Keep.Application — new (6 files):**
- `Requests/IKeepRequestDetailPersistence.cs` — persistence contract for both services.
  Snapshot methods duplicated from `IKeepRequestListPersistence` by design (decision gate,
  no shared base until a third caller exists). Defines `KeepParticipantProjection` and
  `KeepRequestPageLookup` as named return types.
- `Requests/KeepRequestDetailResult.cs` — operator detail result type. Includes all request
  fields (attention, first-response, feedback), `PageToken` for operator copy-link, enriched
  `KeepRequestParticipantItem` (AccountUserId, DisplayName=Email for B1-β, Role), and
  `KeepRequestEventItem` (using denormalized `ActorDisplayName` — no join required).
- `Requests/KeepCustomerPageResult.cs` — public customer page result. When `IsExpired = true`,
  only `BusinessName`, `ReferenceCode`, `IsExpired`, `NewRequestUrl` are populated; all other
  fields are null. When `IsExpired = false`, all fields populated. `KeepCustomerPageEventItem`
  exposes `ActorLabel` ("business" or "customer") — no IDs.
- `Requests/GetKeepRequestDetailService.cs` — authenticated operator read. Full auth stack
  (user snapshot → account snapshot → permission → account access → feature flag) before loading
  the request, scoped to `(requestId, accountId)`. Cross-account and not-found cases both return
  `KeepRequestErrors.NotFound` (no existence leak). All enum fields mapped exhaustively.
- `Requests/GetKeepCustomerPageService.cs` — anonymous customer page read. Loads by page token;
  returns expired result (IsExpired=true) if `ExpiresAtUtc` is in the past without loading events.
  Defensive `Visibility == All` filter applied in the service layer even though persistence already
  scopes this — belt and suspenders for a public endpoint.
- `Requests/KeepCustomerPageResult.cs` — (see above)

**Keep.Infrastructure — new (1 file):**
- `Persistence/EfKeepRequestDetailPersistence.cs` — implements `IKeepRequestDetailPersistence`.
  - Participant query: two-query pattern (participants → account users by ID); avoids complex
    LEFT JOIN translation; in-memory join is safe since participant count per request is small.
  - Customer page: single LINQ join query (KeepRequest JOIN Accounts by AccountId) for
    request + BusinessName.
  - Customer visible events filtered by `Visibility = All` at DB level.

**OpHalo.Api — modified (1 file):**
- `Program.cs` — registered `IKeepRequestDetailPersistence`, `GetKeepRequestDetailService`,
  `GetKeepCustomerPageService`. Added two endpoints:
  - `GET /keep/requests/{requestId:guid}` — authenticated, `.RequireAuthorization()`.
  - `GET /keep/r/{pageToken}` — anonymous. Returns `Results.Ok(page)` for live requests,
    `Results.Json(page, statusCode: 410)` for expired (endpoint dispatches on `page.IsExpired`).

**Tests — new (2 files, 7 tests):**
- `IntegrationTests/Api/KeepRequestDetailTests.cs` (4 tests):
  - Anonymous → 401
  - Unknown requestId → 404
  - Cross-account request → 404 (no existence leak)
  - Authenticated owner → 200 with correct field values, including single RequestCreated event
- `IntegrationTests/Api/KeepCustomerPageTests.cs` (3 tests):
  - Unknown token → 404
  - Expired request → 410 with only businessName/referenceCode/isExpired (null elsewhere)
  - Valid request → 200 with expected shape; no internal IDs; events empty (System visibility
    only in B1-β)

---

## Key Decisions Applied

| Decision | Applied |
|----------|---------|
| Expired page = success result, not error (Option A) | `IsExpired` flag on `KeepCustomerPageResult`; endpoint dispatches on flag value |
| Snapshot methods duplicated (Option A) | `IKeepRequestDetailPersistence` has its own snapshot methods; no shared base |
| `KeepRequestErrors.Forbidden` added but unused this slice | Confirmed in scope; reserved for B4 per-request access control |
| `PageToken` included in operator detail | Intentional — enables operator "copy customer link" action |
| Participant `DisplayName = AccountUser.Email` | B4 to enrich with `User.Name`; noted in code and result type |
| Defensive `Visibility == All` filter in service | Belt-and-suspenders for public endpoint |

---

## Build State

- `dotnet build` → 0 errors, 0 warnings
- Architecture tests → 14/14 passing
- Unit tests → 280/280 passing
- Integration tests → 133/133 passing (+7 from this slice)
- Total → 427/427 passing

---

## Exit Gate

Operator detail and customer page are readable and correctly access-controlled.
Expired 410 returns safe context only. All 427 tests green. Ready for B2.

---

## Risks / Watch-outs for B2

- `GetParticipantsAsync` returns `DisplayName = AccountUser.Email`. When B4 adds the
  participant attach UI, the service must be updated to resolve `User.Name` (requires a
  second AccountUsers → Users join or denormalization).
- `AllowedActions` is always `[]` in `KeepCustomerPageResult`. B3/B4 owns this.
- `NewRequestUrl` is always `null`. B4 decides what URL a customer should use for a
  new request after the old page expires.
- `GetCustomerVisibleEventsAsync` sorts ascending — if the customer UI wants newest-first,
  the sort is controlled by the frontend (consistent with the operator detail which is
  also ascending).
