# Build Log 029 — Phase 8-B2-beta: Business Update + Internal Note Writes

**Phase:** 8-B2-beta
**Branch:** `main`
**Tests at original B2-beta handoff:** 454/454 passing (280 unit · 14 arch · 160 integration)

---

## Scope

Operator write surfaces for customer-facing business updates and internal notes.
Two new endpoints, three new domain methods, shared mapper extraction, and 21 focused
B2-beta integration tests.

---

## Files

| File | Change |
|------|--------|
| `Keep.Core/Errors/KeepRequestErrors.cs` | Added `BusinessUpdateMessageTooLong`, `NoteRequired`, `NoteTooLong` |
| `Keep.Core/Entities/KeepRequestEvent.cs` | Added `CreateBusinessUpdateMessage`, `CreateInternalNote` factories |
| `Keep.Core/Entities/KeepRequest.cs` | First-response wired in `ChangeStatus`; added `AddBusinessUpdate`, `AddBusinessUpdateWithStatus`, `AddInternalNote` |
| `Keep.Application/Requests/KeepRequestDetailMapper.cs` | New — extracted static mapper from both read/write services |
| `Keep.Application/Requests/GetKeepRequestDetailService.cs` | Rewritten to use shared mapper |
| `Keep.Application/Requests/ChangeKeepRequestStatusService.cs` | Rewritten to use shared mapper |
| `Keep.Application/Requests/AddBusinessUpdateService.cs` | New — `POST /keep/requests/{id}/business-updates` service |
| `Keep.Application/Requests/AddInternalNoteService.cs` | New — `POST /keep/requests/{id}/internal-notes` service |
| `Api/Keep/BusinessUpdateRequest.cs` | New — `BusinessUpdateRequestBody(string Message, string? SetStatus)` |
| `Api/Keep/InternalNoteRequest.cs` | New — `InternalNoteRequestBody(string Note)` |
| `Api/Helpers/ErrorHttpMapper.cs` | Added `BusinessUpdateMessageTooLong`→400, `NoteRequired`→400, `NoteTooLong`→400 |
| `Api/Program.cs` | DI registrations; two new POST routes |
| `IntegrationTests/Api/AddBusinessUpdateTests.cs` | New — 12 integration tests |
| `IntegrationTests/Api/AddInternalNoteTests.cs` | New — 9 integration tests |

---

## Key design decisions

### Separate error codes per character limit (ADR-108)
`ChangeStatus` messages: 2000 chars → `MessageTooLong`.
Business update messages: 4000 chars → `BusinessUpdateMessageTooLong`.
Internal notes: 4000 chars → `NoteTooLong`.
Separate codes let the API surface the exact constraint to the caller. A shared
`MessageTooLong` with a dynamic limit would require embedding the limit in the error
payload; separate codes are simpler and self-documenting.

### First-response wired in ChangeStatus + AddBusinessUpdate/AddBusinessUpdateWithStatus (ADR-109)
First response is set when:
1. `ChangeStatus` is called with a non-null message AND `FirstRespondedAtUtc` is not yet set.
2. `AddBusinessUpdate` / `AddBusinessUpdateWithStatus` is called AND `FirstRespondedAtUtc` is not yet set.

`AddBusinessUpdate` always adds a customer-visible business update event; this is by
definition a first response from the business. `ChangeStatus` with a message is also a
visible business communication. The B2-alpha implementation of `ChangeStatus` skipped this
wiring; B2-beta corrects the gap.

Internal notes do NOT set first response — they are operator-only and not customer-visible.

### `AddBusinessUpdateWithStatus` — separate domain method (ADR-108, choice B)
The combined path (new status + business update in one call) uses
`AddBusinessUpdateWithStatus` rather than routing through `ChangeStatus`. Rationale:
`ChangeStatus` enforces a 2000-char message limit; business update messages are allowed
4000 chars. Reusing `ChangeStatus` for the combined path would require overloading its
limit or adding a mode flag — both worsen the domain model. The separate method is
explicit, has its own limit check, and keeps `ChangeStatus` focused on status changes.

The service implements the combined path as: validate `NewStatus` → call
`AddBusinessUpdateWithStatus` → persist → return detail. Status validation (`ParseStatusSlug`,
transition rules) reuses the same helpers as `ChangeKeepRequestStatusService`.

### `KeepRequestDetailMapper` extraction (ADR-110)
`ChangeKeepRequestStatusService` had full duplicates of all mapper methods from
`GetKeepRequestDetailService`. B2-beta added `AddBusinessUpdateService` and
`AddInternalNoteService` — both also return `KeepRequestDetailResult`. Extracting to a
shared `internal static class KeepRequestDetailMapper` in `Keep.Application` eliminates
the duplication before a third write service would require it. All three write services
and the read service now call the shared mapper.

### `ParseStatusSlug` null/blank safety
The helper returns `null` for null or blank input, avoiding a throw inside the switch.
`AddBusinessUpdateService` uses this for optional `SetStatus`: if `SetStatus` is null
(pure update, no status change), the service skips the status path cleanly. If `SetStatus`
is present but blank or unknown, the service returns `KeepRequest.InvalidStatus`.

---

## Architecture compliance

- No Foundation→Keep dependency introduced.
- Mapper extraction stays within `Keep.Application` — `internal static`, not a
  cross-layer abstraction.
- Application does not reference Infrastructure. Core does not reference Application or
  Infrastructure.
- Architecture tests: 14/14.

---

## Integration test coverage (21 tests)

### `AddBusinessUpdateTests` (12 tests)

| Test | Asserts |
|------|---------|
| `Anonymous_Returns401` | No cookie → 401 |
| `UnknownRequestId_Returns404` | Random GUID → 404 |
| `CrossAccountRequest_Returns404` | Account B operator → Account A request → 404 |
| `ViewerRole_Returns403` | Active Viewer (no `RequestsOperate`) → 403 |
| `MissingMessage_Returns400` | Blank message → `KeepRequest.MessageRequired` |
| `ValidBusinessUpdate_Returns200` | Message added, event in timeline, `currentStatusText` updated |
| `ValidBusinessUpdateWithStatusChange_Returns200` | Message + status, event recorded, status transitions |
| `InvalidStatusTransition_Returns422` | Disallowed status slug → `KeepRequest.InvalidStatusTransition` |
| `UnknownSetStatusSlug_Returns400` | Garbage slug → `KeepRequest.InvalidStatus` |
| `MessageTooLong_Returns400` | >4000 chars → `KeepRequest.BusinessUpdateMessageTooLong` |
| `TerminalRequest_Returns409` | Closed request → `KeepRequest.TerminalState` |
| `FirstContactOnCustomerOriginRequest_WiresFirstResponse` | Business update sets first-response fields |

### `AddInternalNoteTests` (9 tests)

| Test | Asserts |
|------|---------|
| `Anonymous_Returns401` | No cookie → 401 |
| `UnknownRequestId_Returns404` | Random GUID → 404 |
| `CrossAccountRequest_Returns404` | Account B operator → Account A request → 404 |
| `ViewerRole_Returns403` | Active Viewer (no `RequestsOperate`) → 403 |
| `ValidInternalNote_Returns200` | Note event in timeline, not visible on customer page |
| `EmptyNote_Returns400` | Blank string → `KeepRequest.NoteRequired` |
| `NoteTooLong_Returns400` | >4000 chars → `KeepRequest.NoteTooLong` |
| `ClosedRequest_Returns200` | Internal note is allowed after terminal state per D8 |
| `DoesNotWireFirstResponse` | Internal note leaves first-response fields unset |

---

## Exit gate

- `dotnet build --no-incremental -warnaserror` → 0 errors, 0 warnings
- Architecture tests → 14/14
- Unit tests → 280/280
- Integration tests → 160/160
- All 454/454

---

## Risks / watch-outs for B2-gamma

- `CanAcknowledgeAttention` is always `false`. B2-gamma wires this.
- `TerminatedAtUtc` is not set by `ChangeStatus` or `AddBusinessUpdateWithStatus` when the
  target is Closed or Cancelled. Pre-existing gap carried forward; not in scope until
  terminal-action endpoints are introduced.
- `GetParticipantsAsync` returns `DisplayName = AccountUser.Email` — not the user's name.
  B4 enriches with `User.Name`.
