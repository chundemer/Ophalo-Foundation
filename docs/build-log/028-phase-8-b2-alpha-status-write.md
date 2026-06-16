# Build Log 028 — Phase 8-B2-alpha: Status Write (`PATCH /keep/requests/{id}/status`)

**Phase:** 8-B2-alpha
**Branch:** `main`
**Tests:** 436/436 passing (280 unit · 14 arch · 142 integration)

---

## Scope

Operator status-change write surface. Everything needed for an authenticated operator to
move a Keep request through its status lifecycle with an optional customer-visible message.

---

## Files

| File | Change |
|------|--------|
| `Keep.Core/Entities/Enums/KeepRequestStatus.cs` | Added `Scheduled = 7` |
| `Keep.Core/Errors/KeepRequestErrors.cs` | Added `InvalidStatus`, `InvalidStatusTransition`, `MessageRequired`, `MessageTooLong`, `TerminalState` |
| `Keep.Core/Entities/KeepRequestEvent.cs` | Added `StatusAfter` (ADR-103); updated `MessageIntent`/`CommunicationChannel` comments; added `CreateStatusChanged` factory |
| `Keep.Core/Entities/KeepRequest.cs` | Added `ChangeStatus` domain method; added `IsAllowedTransition` private helper |
| `Keep.Core/Entities/KeepStatusChangeOutcome.cs` | New — no-op outcome wrapper (ADR-104) |
| `Foundation.Application/Accounts/Authorization/PermissionKeys.cs` | Added `Keep.RequestsOperate` (ADR-105) |
| `Foundation.Application/Accounts/Authorization/RolePermissions.cs` | Added `RequestsOperate` to `OperatorBase` |
| `Keep.Application/Requests/KeepRequestDetailResult.cs` | Added `AvailableActionsMetadata`, `ValidationHintsMetadata`; added `StatusAfter` to `KeepRequestEventItem`; added both to `KeepRequestDetailResult` |
| `Keep.Application/Requests/GetKeepRequestDetailService.cs` | Added `canOperate`; `AvailableActions`; `Scheduled` to `MapStatus`; `StatusAfter` to `MapEvent`; `ValidationHints`; `ComputeAllowedStatuses` |
| `Keep.Application/Requests/IKeepRequestOperatePersistence.cs` | New — write-side persistence contract |
| `Keep.Application/Requests/ChangeKeepRequestStatusService.cs` | New — full operator write service (ADR-106) |
| `Keep.Infrastructure/Persistence/EfKeepRequestOperatePersistence.cs` | New — EF implementation |
| `Keep.Infrastructure/Persistence/Configurations/KeepRequestEventConfiguration.cs` | Added `StatusAfter` mapping; updated comments |
| `Api/Helpers/ErrorHttpMapper.cs` | Added explicit mappings for `InvalidStatus`→400, `MessageRequired`→400, `MessageTooLong`→400, `TerminalState`→409 |
| `Api/Program.cs` | DI registrations; `PATCH /keep/requests/{requestId:guid}/status` endpoint |
| `Api/Keep/ChangeStatusRequest.cs` | New — `ChangeStatusRequestBody(string Status, string? Message)` |
| `Foundation.Infrastructure/Migrations/20260616184305_AddStatusAfterToKeepRequestEvent.cs` | New migration — nullable `varchar(50) status_after` on `keep_request_events` |
| `IntegrationTests/Api/ChangeKeepRequestStatusTests.cs` | New — 9 integration tests |

---

## Key design decisions

### `Scheduled = 7` (ADR-102)
Added to the status enum. Allowed transitions: same table as
Received/InProgress/PendingCustomer → Scheduled/InProgress/PendingCustomer/Resolved/Cancelled.

### `KeepRequestEvent.StatusAfter` (ADR-103)
Every `StatusChanged` event records the new status at event time. The current
`KeepRequest.Status` cannot reconstruct what the status was at each past event; timeline
labels need the snapshot.

EF mapping: `HasConversion<string>().HasMaxLength(50)` (nullable). Migration adds
`status_after varchar(50) null` to `keep_request_events`.

### `KeepStatusChangeOutcome` (ADR-104)
`Result<T>.Success` has `ArgumentNullException.ThrowIfNull(value)` — null is rejected
regardless of whether T is a nullable reference type. The no-op case (same status, no
message) cannot return `null` through `Result<T>`. Introduced a non-null wrapper:

```csharp
public sealed record KeepStatusChangeOutcome(KeepRequestEvent? StatusChangedEvent)
{
    public bool IsNoOp => StatusChangedEvent is null;
    public static readonly KeepStatusChangeOutcome NoOp = new(StatusChangedEvent: null);
    public static KeepStatusChangeOutcome WithEvent(KeepRequestEvent e) { ... }
}
```

`ChangeStatus` returns `Result<KeepStatusChangeOutcome>`. The service commits only when
`!changeResult.Value.IsNoOp`, and persists `changeResult.Value.StatusChangedEvent`.

**Gotcha fixed during this session:** `new(null)` on the `static readonly NoOp` field is
ambiguous between the positional constructor (`KeepRequestEvent?`) and the record's
implicit copy constructor (`KeepStatusChangeOutcome`). Fixed with the named argument form:
`new(StatusChangedEvent: null)`.

### `ChangeStatus` guard order (important)
No-op check BEFORE terminal check. Closed → Closed / no message is a valid no-op, not a
`TerminalState` error. The wrong order surfaces as a 409 for a legal idempotent call.

### `CurrentStatusText` preservation (ADR-107)
Silent status changes (no message) do not clear `CurrentStatusText`. Only overwritten when
`trimmedMessage is not null`. Erasing the last customer-facing text on an internal
reclassification is a silent UX regression.

### Transition table
```
Received / Scheduled / InProgress / PendingCustomer  →  Scheduled / InProgress / PendingCustomer / Resolved / Cancelled
Resolved  →  InProgress / PendingCustomer / Closed / Cancelled
Closed / Cancelled  →  (terminal; TerminalState error)
```
Same-status transitions are always allowed (subject to the no-op / message-required rules).

### `AvailableActions` on detail response
`ChangeKeepRequestStatusService` populates `AvailableActionsMetadata` and
`ValidationHintsMetadata` on every response. `CanAcknowledgeAttention = false` is
hardcoded for B2-alpha; B2-gamma wires attention acknowledgement.

### `ErrorHttpMapper` extension shapes
`Results.Problem(extensions: dict)` serializes extension entries at the **top level** of
the ProblemDetails JSON, not nested under `"extensions"`. Test assertions must read
`body.GetProperty("code")`, not `body.GetProperty("extensions").GetProperty("code")`.

---

## Architecture compliance

- No Foundation→Keep dependency introduced.
- `ChangeKeepRequestStatusService` is in `Keep.Application`; `EfKeepRequestOperatePersistence`
  is in `Keep.Infrastructure`. Application does not depend on Infrastructure.
- Snapshot methods are duplicated on `IKeepRequestOperatePersistence` and
  `IKeepRequestDetailPersistence`. Extraction to a shared interface is deferred until a
  fourth service repeats the pattern (ADR-106).
- Mapper methods are duplicated in `ChangeKeepRequestStatusService`. Extract to a shared
  `internal static KeepRequestDetailMapper` class when B2-beta adds the next write service.

---

## Integration test coverage (9 tests)

| Test | Asserts |
|------|---------|
| `Anonymous_Returns401` | No cookie → 401 |
| `UnknownRequestId_Returns404` | Random GUID → 404 |
| `CrossAccountRequest_Returns404` | Account B operator → Account A request → 404 |
| `ViewerRole_Returns403` | Active Viewer (no `RequestsOperate`) → 403 |
| `ValidTransitionWithMessage_Returns200` | Received → Scheduled + message → 200; event count, `statusAfter`, `currentStatusText`, `availableActions` |
| `SameStatusNoMessage_Returns200AsNoOp` | Received → Received / no message → 200; event count still 1 |
| `PendingCustomerWithoutMessage_Returns400` | `KeepRequest.MessageRequired` code at root |
| `TerminalRequest_Returns409` | Closed → InProgress → `KeepRequest.TerminalState` code at root |
| `DisallowedTransition_Returns422` | Received → Closed → `KeepRequest.InvalidStatusTransition` code at root |

---

## Exit gate

- `dotnet build --no-incremental -warnaserror` → 0 errors, 0 warnings
- Architecture tests → 14/14
- Unit tests → 280/280
- Integration tests → 142/142
- All 436/436

---

## Risks / watch-outs for B2-beta

- `ChangeKeepRequestStatusService` duplicates all mapper methods from
  `GetKeepRequestDetailService`. Extract to a shared static class before B2-gamma adds a
  third write service returning `KeepRequestDetailResult`.
- `CanAcknowledgeAttention` is always `false`. B2-gamma wires this.
- `GetParticipantsAsync` returns `DisplayName = AccountUser.Email` — not the user's name.
  B4 enriches with `User.Name`.
- The `PATCH /status` no-op path does not run `CommitAsync`; it still calls
  `GetAllEventsAsync` / `GetParticipantsAsync` / `GetAccountBusinessNameAsync` for the
  response. No EF change tracking issue (request is tracked but unchanged).
