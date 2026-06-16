# Session Log — OpHalo Foundation

**Last updated:** 2026-06-16
**Branch:** `main` (no remote yet)

---

## Phase 8-B1-β — COMPLETE

Keep request detail + customer page read surfaces. 427/427 tests passing.

### What was built

**Keep.Core:**
- `Errors/KeepRequestErrors.cs` — added `Forbidden` (reserved for B4 per-request access control).

**Keep.Application (6 new files):**
- `IKeepRequestDetailPersistence.cs` — persistence contract for operator detail + customer page.
  Snapshot methods duplicated from list persistence (decision gate, no shared base yet).
  `KeepParticipantProjection` (with `DisplayName`, `Role` from `AccountUser`) and
  `KeepRequestPageLookup` as named return types.
- `KeepRequestDetailResult.cs` — operator detail result. All request fields + participants
  (with `DisplayName = Email` for B1-β) + events using denormalized `ActorDisplayName`.
- `KeepCustomerPageResult.cs` — public customer page result. `IsExpired = true` → only
  `BusinessName`, `ReferenceCode`, `IsExpired`, `NewRequestUrl` populated; all other null.
- `GetKeepRequestDetailService.cs` — full auth stack before loading; cross-account returns 404.
- `GetKeepCustomerPageService.cs` — anonymous; expired result is `Success(IsExpired=true)`,
  not a failure; defensive `Visibility == All` filter in service layer.

**Keep.Infrastructure (1 new file):**
- `EfKeepRequestDetailPersistence.cs` — implements `IKeepRequestDetailPersistence`.
  Two-query pattern for participants (participants → AccountUsers in memory; safe for small lists).
  LINQ join query for customer page (KeepRequest JOIN Accounts for businessName).

**OpHalo.Api:**
- `Program.cs` — registered new services + 2 endpoints:
  - `GET /keep/requests/{requestId:guid}` — authenticated.
  - `GET /keep/r/{pageToken}` — anonymous; `page.IsExpired ? Results.Json(page, 410) : Results.Ok(page)`.

**Tests (2 new files, 7 new tests):**
- `KeepRequestDetailTests.cs` — anonymous→401, unknown→404, cross-account→404, owner→200.
- `KeepCustomerPageTests.cs` — unknown token→404, expired→410 (safe context only), valid→200.

### Key decisions

ADR-099..101 (see decision-index.md and build-log/026).

---

## Phase 8-B1-α — COMPLETE

Keep domain model + EF schema. 417/417 tests passing (before B1-β).

---

## Phase 5E-C — COMPLETE

Member management API + integration tests. 126/126 integration tests passing.

---

## Build state

- `dotnet build` → 0 errors, 0 warnings
- Architecture tests → 14/14 passing
- Unit tests → 280/280 passing
- Integration tests → 133/133 passing
- Total → 427/427 passing

---

## Watch-outs carried forward

- Full deferred backlog in `docs/deferred-topics.md`.
- **ADR-058** — Superseded by ADR-061..064.
- **AnonymousCurrentUser** — kept for potential worker/test use; not registered in production.
- **SystemClock** FQDN in Program.cs — `OpHalo.Foundation.Infrastructure.Services.SystemClock`.
- **Schema-drop reset pattern** — integration test factory uses `DROP SCHEMA public CASCADE` + recreate + `MigrateAsync`.
- **Migration generation** — always `--startup-project src/OpHalo.Keep.Infrastructure`; use dummy connection string if no DB yet.
- **No GitHub remote yet.**
- **B1-β watch-outs for B2:**
  - `GetParticipantsAsync` returns `DisplayName = AccountUser.Email`. B4 must enrich with `User.Name`.
  - `AllowedActions` is always `[]` in `KeepCustomerPageResult`. B3/B4 owns this.
  - `NewRequestUrl` is always `null`. B4 decides the new-request URL for expired pages.
  - Customer page events sorted ascending — let frontend reverse if needed.

---

## Next session — Phase 8-B2 (operator writes)

B2 scope is the operator write surface. Not yet in pre-work-complete state — needs a B2 discovery session to confirm scope and ADR choices before implementation.

Suggested B2 scope (from build plan §8 + ADR-093):
- Operator status transitions (`POST /keep/requests/{id}/status`)
- Terminal: close and cancel (`/close`, `/cancel`)
- Message add — operator sends message to customer
- Internal note add
- Attention acknowledgement
- First-response wiring (set `FirstRespondedAtUtc` etc. on first business message/status change)
- Attention state transitions (set `AttentionLevel`, `WaitingDirection`, etc.)
- Notification-routing state updates (who is notified, B2 doesn't deliver — see ADR-092)
