# Session Log — OpHalo Foundation

**Last updated:** 2026-06-16
**Next session tier:** Tier 2 — Phase 8-B1-β Application + API + Tests
**Branch:** `main` (no remote yet)

---

## Phase 8-B1-α — COMPLETE

Keep domain model + EF schema. 417/417 tests passing.

### What was built

**Keep.Core:**
- 9 new enum files: `ActorType`, `MessageIntent`, `CommunicationChannel`, `KeepRequestOrigin`,
  `AttentionLevel`, `WaitingDirection`, `AttentionReason`, `PriorityBand`, `ParticipationType`
- `KeepRequestEventType.cs` — action-oriented vocabulary (ADR-094): `RequestCreated=1`,
  `StatusChanged=2`, `MessageAdded=3`, `RequestClosed=5`, `RequestCancelled=6`,
  `InternalNoteAdded=7`, `AttentionAcknowledged=8`. Gap at 4 intentional.
- `KeepRequestEvent.cs` — added `ActorType` (required), `ActorAccountUserId?`,
  `ActorDisplayName?`, `MessageIntent?`, `CommunicationChannel?`.
  `CreateRequestCreated` sets `ActorType=System`.
- `KeepRequest.cs` — added `Origin` (default Customer, ADR-095); renamed `ClosedAtUtc` →
  `TerminatedAtUtc` (ADR-096); first-response fields; attention fields (init to None/None/Standard,
  ADR-098); terminal feedback fields.
- `KeepResponsePolicy.cs` — NEW: account-level SLA policy, inherits BaseEntity (ADR-097).
- `KeepRequestParticipant.cs` — NEW: Responsible/Watching participant record.

**Keep.Infrastructure:**
- `KeepRequestConfiguration.cs` — updated for all new fields + attention composite index.
- `KeepRequestEventConfiguration.cs` — updated for actor + intent + channel fields.
- `KeepResponsePolicyConfiguration.cs` — NEW: unique index on AccountId.
- `KeepRequestParticipantConfiguration.cs` — NEW: unique on (request_id, account_user_id).

**Foundation.Infrastructure/Migrations:**
- `20260616150747_Phase8KeepDataModel` — full schema migration.

**Tests:**
- `UnitTests/Keep/KeepRequestTests.cs` — `ClosedAtUtc` → `TerminatedAtUtc` reference fixed.

### Key decisions

ADR-094..098 (see decision-index.md and build-log/025).

---

## Phase 5E-C — COMPLETE

Member management API + integration tests. 126/126 integration tests passing (417/417 total).

---

## Build state

- `dotnet build` → 0 errors, 0 warnings
- Architecture tests → 14/14 passing
- Unit tests → 280/280 passing
- Integration tests → 126/126 passing
- Total → 417/417 passing

---

## Watch-outs carried forward

- Full deferred backlog in `docs/deferred-topics.md`.
- **ADR-058** — Superseded by ADR-061..064.
- **AnonymousCurrentUser** — kept for potential worker/test use; not registered in production.
- **SystemClock** FQDN in Program.cs — `OpHalo.Foundation.Infrastructure.Services.SystemClock`.
- **Schema-drop reset pattern** — integration test factory uses `DROP SCHEMA public CASCADE` + recreate + `MigrateAsync`.
- **Migration generation** — always `--startup-project src/OpHalo.Keep.Infrastructure`; use dummy connection string if no DB yet.
- **No GitHub remote yet.**
- **B1-α watch-outs for B1-β** — see build-log/025 risks section.

---

## Next session — Phase 8-B1-β (Pre-work complete)

### Confirmed facts from B1-α

- All Phase 8 enums live in `OpHalo.Keep.Core.Entities.Enums`.
- `KeepRequestEvent.ActorType` is required — every new factory method must set it.
- `KeepRequest.TerminatedAtUtc` replaces `ClosedAtUtc`.
- `KeepResponsePolicy` has no default seeding — read service must handle null policy
  (fall back to a default SLA value, e.g. 60 minutes first response).
- `KeepRequestParticipant` unique index is on `(request_id, account_user_id)`.

### B1-β scope

**Keep.Application (new):**
- `Requests/IKeepRequestDetailPersistence.cs` — interface for operator detail + customer page reads
- `Requests/GetKeepRequestDetailService.cs` — authenticated operator read (`GET /keep/requests/{requestId}`)
- `Requests/GetKeepCustomerPageService.cs` — anonymous customer page read (`GET /keep/r/{pageToken}`)
- Response types: `KeepRequestDetailResult`, `KeepRequestEventItem`, `KeepCustomerPageResult`

**Keep.Application errors:**
- `KeepRequestErrors` — add `Forbidden`, `Expired` (for 410 case)

**Keep.Infrastructure (new):**
- `Persistence/EfKeepRequestDetailPersistence.cs` — implements `IKeepRequestDetailPersistence`;
  loads request + events + account business name; customer page strips Internal events

**OpHalo.Api:**
- `Program.cs` — register new services, add two endpoints:
  - `GET /keep/requests/{requestId}` (authenticated, operator detail)
  - `GET /keep/r/{pageToken}` (anonymous, customer page; 410 when expired)
- `Helpers/ErrorHttpMapper.cs` — add `KeepRequest.Expired` → 410

**Tests:**
- `IntegrationTests/Api/KeepRequestDetailTests.cs` — NEW: operator detail read + access rules
- `IntegrationTests/Api/KeepCustomerPageTests.cs` — NEW: customer page read + expired link 410

### Confirmed facts for B1-β gate

1. **Operator detail response** — rich internal projection: request fields, customer summary,
   attention + first-response fields, participants, full timeline including Internal and System
   events.
2. **Customer page response** — public-safe projection: `businessName`, reference/status/details,
   `All`-visibility events only, terminal/feedback state, allowed actions, expiry info. No account
   IDs, user IDs, internal notes, or routing internals.
3. **Expired link response** — 410 with `{ businessName, referenceCode, expired: true,
   newRequestUrl: null }`.
4. **BusinessName** — join/project from `Account` at read time. Do not denormalize onto
   `KeepRequest` yet.
