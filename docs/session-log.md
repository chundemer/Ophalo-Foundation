# Session Log — OpHalo Foundation

**Last updated:** 2026-06-16
**Branch:** `main` (no remote yet)

---

## Phase 8-B2-beta — COMPLETE

**Tests:** 454/454 (280 unit · 14 arch · 160 integration)
**ADRs:** 108..110 (see decision-index.md and build-log/029)

### Summary of what was built

Two new operator write endpoints:
- `POST /keep/requests/{requestId}/business-update` — customer-visible message with optional status change
- `POST /keep/requests/{requestId}/internal-note` — operator-only note

Full stack for both: domain methods, event factories, error codes, application services,
API request types, DI + routes, error mappings, 18 integration tests.

Shared mapper (`KeepRequestDetailMapper`) extracted to eliminate duplication across all
read/write services that return `KeepRequestDetailResult`.

First-response wiring corrected: now set in `ChangeStatus` (message path) and
`AddBusinessUpdate`/`AddBusinessUpdateWithStatus`.

**Key files:**

| Layer | File |
|-------|------|
| Keep.Core | `KeepRequestErrors.cs` (+BusinessUpdateMessageTooLong, NoteRequired, NoteTooLong) |
| Keep.Core | `KeepRequestEvent.cs` (+CreateBusinessUpdateMessage, +CreateInternalNote) |
| Keep.Core | `KeepRequest.cs` (+first-response in ChangeStatus, +AddBusinessUpdate, +AddBusinessUpdateWithStatus, +AddInternalNote) |
| Keep.Application | `KeepRequestDetailMapper.cs` (NEW — extracted) |
| Keep.Application | `GetKeepRequestDetailService.cs` (uses mapper) |
| Keep.Application | `ChangeKeepRequestStatusService.cs` (uses mapper, first-response wired) |
| Keep.Application | `AddBusinessUpdateService.cs` (NEW) |
| Keep.Application | `AddInternalNoteService.cs` (NEW) |
| OpHalo.Api | `BusinessUpdateRequest.cs` (NEW) |
| OpHalo.Api | `InternalNoteRequest.cs` (NEW) |
| OpHalo.Api | `ErrorHttpMapper.cs` (+3 mappings) |
| OpHalo.Api | `Program.cs` (+DI + 2 routes) |
| IntegrationTests | `AddBusinessUpdateTests.cs` (NEW — 10 tests) |
| IntegrationTests | `AddInternalNoteTests.cs` (NEW — 8 tests) |

---

## Phase 8-B2-alpha — COMPLETE

**Status:** 436/436 tests passing. Build log 028 and decision index (ADR-102..107) shipped.

### Summary of what was built

`PATCH /keep/requests/{requestId}/status` — authenticated operator status-change endpoint.
Full stack: domain method, outcome wrapper, persistence interfaces, EF implementations,
API endpoint, permission key, EF migration, 9 integration tests.

**Key files:**

| Layer | File |
|-------|------|
| Keep.Core | `KeepRequest.ChangeStatus`, `KeepStatusChangeOutcome`, `KeepRequestEvent.CreateStatusChanged`, `KeepRequestErrors` (+5), `KeepRequestStatus.Scheduled=7` |
| Foundation.Application | `PermissionKeys.Keep.RequestsOperate`, `RolePermissions` (OperatorBase) |
| Keep.Application | `IKeepRequestOperatePersistence`, `ChangeKeepRequestStatusService`, `KeepRequestDetailResult` (AvailableActions/Validation/StatusAfter), `GetKeepRequestDetailService` (canOperate/ComputeAllowedStatuses/Scheduled) |
| Keep.Infrastructure | `EfKeepRequestOperatePersistence`, `KeepRequestEventConfiguration` (StatusAfter) |
| Foundation.Infrastructure | Migration `20260616184305_AddStatusAfterToKeepRequestEvent` |
| OpHalo.Api | `ChangeStatusRequest.cs`, `ErrorHttpMapper` (4 new mappings), `Program.cs` (DI + route) |
| IntegrationTests | `ChangeKeepRequestStatusTests.cs` (9 tests) |

**ADRs:** 102..107 (see decision-index.md and build-log/028).

---

## Phase 8-B1-β — COMPLETE

Keep request detail + customer page read surfaces. 427/427 tests before B2-alpha.
ADRs 099..101. See build-log/026.

---

## Phase 8-B1-α — COMPLETE

Keep domain model + EF schema. ADRs 094..098. See build-log/025.

---

## Phase 5E-C — COMPLETE

Member management API + integration tests. See build-log/023.

---

## Watch-outs carried forward

- `docs/deferred-topics.md` holds the full deferred backlog.
- **ADR-058** superseded by ADR-061..064.
- **AnonymousCurrentUser** kept for potential worker/test use; not in production.
- **SystemClock** FQDN in Program.cs: `OpHalo.Foundation.Infrastructure.Services.SystemClock`.
- **Schema-drop reset** in integration test factory: `DROP SCHEMA public CASCADE` + recreate + `MigrateAsync`.
- **Migration generation** always: `--startup-project src/OpHalo.Keep.Infrastructure`.
- **No GitHub remote yet.**
- **B1-β watch-outs still active for B2+:**
  - `GetParticipantsAsync` returns `DisplayName = AccountUser.Email`. B4 enriches with `User.Name`.
  - `AllowedActions` always `[]` in `KeepCustomerPageResult`. B3/B4 owns this.
  - `NewRequestUrl` always `null`. B4 decides.
  - Customer page events sorted ascending — let frontend reverse if needed.
- **`CanAcknowledgeAttention = false`** hardcoded. B2-gamma wires it. Full formula when wired: `hasOperatePermission && request.AttentionLevel != AttentionLevel.None`.
- **`Results.Problem` extension shape:** extension dict entries land at the top level of ProblemDetails JSON, not under an `"extensions"` key. Test assertions must use `body.GetProperty("code")`.
- **`TerminatedAtUtc` not set by ChangeStatus/AddBusinessUpdateWithStatus** when target is Closed/Cancelled. Pre-existing gap, not yet in scope.
- **`businessName ?? string.Empty`** — persistence returns null if account missing post-auth; never expected in production.

---

## Next — Phase 8-B2-gamma

Attention acknowledge: wire `CanAcknowledgeAttention` and implement
`POST /keep/requests/{id}/acknowledge-attention`.

Full formula: `hasOperatePermission && request.AttentionLevel != AttentionLevel.None`.
