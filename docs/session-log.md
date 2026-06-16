# Session Log — OpHalo Foundation

**Last updated:** 2026-06-16
**Branch:** `main` (no remote yet)

---

## Phase 8-B2-alpha — COMPLETE

**Status:** 436/436 tests passing. Build log 028 and decision index (ADR-102..107) shipped with the code.

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
- **B2-alpha mapper duplication:** `ChangeKeepRequestStatusService` duplicates all mapper methods from `GetKeepRequestDetailService`. Extract to `internal static KeepRequestDetailMapper` when B2-beta adds the next write service.
- **`CanAcknowledgeAttention = false`** hardcoded. B2-gamma wires it.
- **`Results.Problem` extension shape:** extension dict entries land at the top level of ProblemDetails JSON, not under an `"extensions"` key. Test assertions must use `body.GetProperty("code")`.

---

## Next session — Phase 8-B2-beta (business updates + internal notes)

**Pre-work:** Read build-log/027 section for B2-beta scope before starting.

### B2-split remaining
- **B2-beta** — business updates + internal notes (`POST /keep/requests/{id}/messages`, `POST /keep/requests/{id}/notes`)
- **B2-gamma** — attention acknowledge + attention clearing
