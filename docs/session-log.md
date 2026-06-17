# Session Log — OpHalo Foundation

**Last updated:** 2026-06-17
**Branch:** `main` (no remote yet)

---

## Phase 8-B3-beta — COMPLETE

**Tests:** 487/487 (280 unit · 14 arch · 193 integration)
**ADRs:** 118..134 locked (no new ADRs; all locked in build-log/032)

### Summary of what was built

B3-beta: customer-submitted messages API layer.

- `KeepCustomerPageMapper` — shared static mapper for read and write services:
  `MapStatus` (Scheduled fix), `MapEvent`, `MapActorLabel`, `ComputeAllowedActions`,
  `BuildExpiredResult`, `BuildActiveResult`.
- `GetKeepCustomerPageService` refactored: drops `IClock`, injects
  `KeepPublicCustomerAccessGuard` + `IKeepRequestDetailPersistence`; delegates all
  token/access/expiry logic to the guard.
- `AddCustomerMessageCommand` + `AddCustomerMessageService` — guard → expiry
  short-circuit → tracked reload → `AddCustomerMessage` → commit → events → result.
- `EfKeepCustomerWritePersistence` — implements `IKeepCustomerWritePersistence`.
- `CustomerMessageBody` request record.
- `Program.cs`: `customer-write` rate limit (10/min, IP+token composite), 6 customer
  message routes, `HandleCustomerMessage` local function, DI registrations.
- `KeepCustomerPageTests` expired-page test updated to also set `status = 'Closed'`
  (required by ADR-120 terminal-only expiry now enforced by the guard).
- 16 integration tests per ADR-131.

**AllowedActions:**
- Received/Scheduled/InProgress/PendingCustomer/Resolved → `["message","question","update_request","schedule_change_request","change_or_cancel_request","issue"]`
- Closed/Cancelled → `[]`
- Expired tombstone → `null`

**Key files:**

| Layer | File |
|-------|------|
| Keep.Application | `Requests/KeepCustomerPageMapper.cs` (new) |
| Keep.Application | `Requests/GetKeepCustomerPageService.cs` (refactored) |
| Keep.Application | `Requests/AddCustomerMessageCommand.cs` (new) |
| Keep.Application | `Requests/AddCustomerMessageService.cs` (new) |
| Keep.Infrastructure | `Persistence/EfKeepCustomerWritePersistence.cs` (new) |
| Api | `Keep/CustomerMessageRequest.cs` (new) |
| Api | `Program.cs` (routes + DI + rate limit) |
| IntegrationTests | `Api/CustomerMessageTests.cs` (new, 16 tests) |
| IntegrationTests | `Api/KeepCustomerPageTests.cs` (expired test fixed) |

---

## Phase 8-B3-alpha — COMPLETE

**Tests:** 471/471 (280 unit · 14 arch · 177 integration)
**ADRs:** 118..134 locked (decision gate, build-log/032); B3-alpha implements the Core/Application foundations.

### Summary of what was built

B3-alpha: low-level foundations for customer-submitted messages.

- `KeepRequestErrors.CustomerMessageTooLong` — new error code (4000-char cap).
- `KeepRequestEvent.CreateCustomerMessage` — factory: `EventType=MessageAdded`,
  `Visibility=All`, `ActorType=Customer`, `ActorAccountUserId=null`,
  `ActorDisplayName=CustomerName`, mapped `MessageIntent`, `CommunicationChannel=InApp`.
- `KeepRequest.AddCustomerMessage(intent, message, firstResponse, standard, priority, nowUtc)` —
  domain method. Blocks on Closed/Cancelled (`TerminalState`); Resolved accepted. Updates
  `LastCustomerActivityAt`. Raises/updates business-waiting attention per ADR-125.
- `KeepPublicCustomerContext` — safe non-tracked guard result record.
- `KeepPublicCustomerAccessGuard` — centralized gate (ADR-119).
- `IKeepCustomerWritePersistence` — write-side persistence contract.

---

## Phase 8-B2-delta — COMPLETE

**Tests:** 471/471 (280 unit · 14 arch · 177 integration)
**ADRs:** 116..117 implemented (see decision-index.md and build-log/031)

Terminal lifecycle analytics primitives: `TerminatedAtUtc`, `ClearAllAttentionForTerminal`.

---

## Phase 8-B2-gamma — COMPLETE

**Tests:** 468/468 (280 unit · 14 arch · 174 integration)
**ADRs:** 111..114 implemented.

`POST /keep/requests/{requestId}/attention/acknowledge` + attention-clearing in business-update and status-change paths.

---

## Phase 8-B2-beta — COMPLETE

**Tests:** 454/454 (280 unit · 14 arch · 160 integration)
**ADRs:** 108..110

`POST /keep/requests/{requestId}/business-updates` + `POST /keep/requests/{requestId}/internal-notes`

---

## Phase 8-B2-alpha — COMPLETE

**Status:** 436/436 tests passing.
`PATCH /keep/requests/{requestId}/status`

---

## Phase 8-B1-β — COMPLETE

Keep request detail + customer page read surfaces. ADRs 099..101.

---

## Phase 8-B1-α — COMPLETE

Keep domain model + EF schema. ADRs 094..098.

---

## Phase 5E-C — COMPLETE

Member management API + integration tests.

---

## Watch-outs carried forward

- `docs/deferred-topics.md` holds the full deferred backlog.
- **ADR-058** superseded by ADR-061..064.
- **AnonymousCurrentUser** kept for potential worker/test use; not in production.
- **SystemClock** FQDN in Program.cs: `OpHalo.Foundation.Infrastructure.Services.SystemClock`.
- **Schema-drop reset** in integration test factory: `DROP SCHEMA public CASCADE` + recreate + `MigrateAsync`.
- **Migration generation** always: `--startup-project src/OpHalo.Keep.Infrastructure`.
- **No GitHub remote yet.**
- **B1-β watch-outs still active for B4:**
  - `GetParticipantsAsync` returns `DisplayName = AccountUser.Email`. B4 enriches with `User.Name`.
  - `NewRequestUrl` always `null`. B4 decides.
  - Customer page events sorted ascending — let frontend reverse if needed.
- **`Results.Problem` extension shape:** extension dict entries land at the top level of ProblemDetails JSON, not under an `"extensions"` key. Test assertions must use `body.GetProperty("code")`.
- **External contact logging/capture (ADR-115)** remains pre-go-live/deferred.
- **`businessName ?? string.Empty`** — persistence returns null if account missing post-auth; never expected in production.
- **`KeepResponsePolicy` defaults** (first=60, standard=240, priority=60 min) apply when no policy row exists for an account; silent fallback by design.
- **Next free ADR: ADR-135.**
- **Always rebuild before running tests** — `dotnet test --no-build` can run stale assemblies that mask real failures.

---

## Next — Phase 8-B4 (or deferred topics — confirm with Christian)
