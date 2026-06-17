# Build Log 034 — Phase 8-B3-beta: Customer-Submitted Messages API Layer

**Date:** 2026-06-17
**Tests:** 487/487 (280 unit · 14 arch · 193 integration)
**ADRs:** 118..134 (all locked in build-log/032; no new ADRs this session)

---

## What Was Built

B3-beta wires the API layer for customer-submitted messages on top of the B3-alpha foundations.

### Files created

| Layer | File | Notes |
|-------|------|-------|
| Keep.Application | `Requests/KeepCustomerPageMapper.cs` | Shared static mapper: `MapStatus` (with `Scheduled` fix), `MapEvent`, `MapActorLabel`, `ComputeAllowedActions`, `BuildExpiredResult`, `BuildActiveResult` |
| Keep.Application | `Requests/AddCustomerMessageCommand.cs` | `record(PageToken, Intent, Message)` |
| Keep.Application | `Requests/AddCustomerMessageService.cs` | Guard → expiry short-circuit → tracked reload → domain → commit → events → page result |
| Keep.Infrastructure | `Persistence/EfKeepCustomerWritePersistence.cs` | Implements `IKeepCustomerWritePersistence` |
| Api | `Keep/CustomerMessageRequest.cs` | `record CustomerMessageBody(string Message)` |
| IntegrationTests | `Api/CustomerMessageTests.cs` | 16 tests per ADR-131 |

### Files modified

| File | Change |
|------|--------|
| `Keep.Application/Requests/GetKeepCustomerPageService.cs` | Refactored: drop `IClock`, inject `KeepPublicCustomerAccessGuard` + `IKeepRequestDetailPersistence`; delegate all token/access/expiry logic to guard; delegate mapping to `KeepCustomerPageMapper` |
| `Api/Program.cs` | DI for guard/service/persistence; `customer-write` rate limit policy (10/min, IP+token composite); 6 customer message routes; `HandleCustomerMessage` local function; `using OpHalo.Keep.Core.Entities.Enums` |
| `IntegrationTests/Api/KeepCustomerPageTests.cs` | Fixed expired-page test to set `status = 'Closed'` alongside `expires_at_utc` — required by ADR-120 terminal-only expiry now enforced by the guard |

---

## Key Design Decisions

### GetKeepCustomerPageService refactor
The service now delegates entirely to `KeepPublicCustomerAccessGuard` for token resolution, account access, feature gate, and terminal-only expiry. The guard returns a `KeepPublicCustomerContext` snapshot; the service only needs to load events and build the result.

### Shared mapper (`KeepCustomerPageMapper`)
Both `GetKeepCustomerPageService` and `AddCustomerMessageService` produce `KeepCustomerPageResult` from the same context shape. Extracting the static mappers to a single internal class ensures `MapStatus` (including `Scheduled`) and `ComputeAllowedActions` stay in sync across read and write paths.

### Expired-write guardrail
`AddCustomerMessageService` checks `context.IsExpired` immediately after guard success and returns the safe tombstone without touching the tracked entity, calling `AddCustomerMessage`, or committing. This keeps read/write expiry behavior consistent (ADR-130).

### AllowedActions
Computed server-side by `KeepCustomerPageMapper.ComputeAllowedActions`:
- Received/Scheduled/InProgress/PendingCustomer/Resolved → 6-item action list
- Closed/Cancelled → `[]`
- Expired tombstone → `null` (set explicitly in `BuildExpiredResult`)

### `Scheduled` MapStatus fix
Added `KeepRequestStatus.Scheduled => "scheduled"` arm. The missing arm would have thrown `InvalidOperationException` on any Scheduled request hitting the customer page. Covered by test 16.

### customer-write rate limit (ADR-129)
Partition key: `GetClientIp(context) + ":" + pageToken` — composite IP+token avoids penalising shared networks while limiting a single actor hammering one request. 10 writes/min, `QueueLimit = 0`.

### KeepCustomerPageTests update
The existing expired-page test set `expires_at_utc` without setting a terminal status. The old service checked expiry unconditionally; the new guard enforces ADR-120 (terminal-only expiry). Test updated to also set `status = 'Closed'`.

---

## Test Coverage (CustomerMessageTests.cs — 16 tests)

1. Unknown token → 404
2. Blocked/suspended account → 404, event count unchanged
3. Expired terminal token → 410 safe context, no mutation
4. Blank message → 400 `KeepRequest.MessageRequired`
5. Over-limit message → 400 `KeepRequest.CustomerMessageTooLong`
6. Happy path `/message` → 200 with `message_added` customer event
7. `/issue` → 200, DB: `PriorityBand=Priority`, `AttentionReason=Complaint`
8. Repeated standard message → oldest `AttentionSinceUtc` preserved in DB
9. Later priority message upgrades reason/priority, `AttentionSinceUtc` preserved
10. Message while `WaitingDirection=Customer` → flips to Business, `AttentionSinceUtc` reset
11. Resolved request → 200 (accepts customer message)
12. Closed request → 409 `KeepRequest.TerminalState`
13. Cancelled request → 409 `KeepRequest.TerminalState`
14. Response contains no internal IDs or attention internals
15. `AllowedActions` correct for active/resolved/closed/cancelled/expired states
16. Scheduled status maps to `"scheduled"` on customer page; `AllowedActions` = 6-item list

---

## Exit Gate

- 487/487 tests passing (280 unit · 14 arch · 193 integration)
- Architecture tests green (no new dependency violations)
- `ErrorHttpMapper` unchanged — `CustomerMessageTooLong → 400` was already present
- B3 complete: guard + domain + persistence + API layer + 16 integration tests

## Risks / Watch-outs

- `KeepResponsePolicy` defaults (first=60, standard=240, priority=60 minutes) apply when no policy is configured for an account. No existing migration adds a default policy row; this is by design — the service falls back silently.
- Rate limiter is bypassed in the `Testing` environment (existing pattern); customer-write routes are not rate-limited in integration tests.
- Next free ADR remains ADR-135.
