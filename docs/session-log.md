# Session Log — OpHalo Foundation

**Last updated:** 2026-06-15
**Next session tier:** Tier 1 — Phase 5D discovery (member invites)
**Branch:** `main` (no remote yet)

---

## Phase 5C — COMPLETE

New-account registration via `POST /auth/start` + extended `/auth/exchange`. All exit-gate items verified.

### What was built

**Foundation.Core:**
- `Entities/Accounts/Enums/EntryContext.cs` — added `NewAccount = 1`
- `Entities/Accounts/AccountAuthCode.cs` — added `BusinessNameSnapshot`, `NameSnapshot`, `TimeZoneSnapshot`; added `CreateForNewAccount` factory; `Create` now rejects `EntryContext.NewAccount` at call site
- `Entities/Accounts/Errors/AccountErrors.cs` — added `EmailAlreadyInUse` + `PilotFull`

**Foundation.Application:**
- `Auth/SignupDefaultsSettings.cs` — NEW: `IsPilot`, `TrialDurationDays`, `int? MaxPilotAccounts`
- `Auth/StartAuthService.cs` — NEW: classifies request (ExistingMember / NewAccount / Neutral), issues code, sends best-effort email, enforces pilot cap gate
- `Auth/IAuthCodePersistence.cs` — added `ClassifyStartRequestAsync`, `CommitStartCodeAsync`, `CountActivePilotAccountsAsync`, `CommitNewAccountExchangeAsync`; added `StartClassification` record hierarchy (`StartAsNewAccount`, `StartAsExistingMember`, `StartAsNeutral`)
- `Auth/ExchangeAuthService.cs` — added `NewAccount` branch; injected `AccountProvisioningService` + `IOptions<SignupDefaultsSettings>`; extracted `CreateSessionAsync` helper
- `Auth/MagicLinkEmailTemplate.cs` — added `NewAccountSubject`

**Foundation.Infrastructure:**
- `Auth/EfAuthCodePersistence.cs` — implemented 4 new methods; `CommitNewAccountExchangeAsync` uses atomic consume + two-phase graph save (ADR-044) in one transaction; catches `PostgresErrorCodes.UniqueViolation` → `AccountErrors.EmailAlreadyInUse`
- `Persistence/Configurations/AccountAuthCodeConfiguration.cs` — added snapshot columns (200/200/100) + composite index on `(delivery_email_snapshot, entry_context)`
- Migration: `AccountAuthCodeNewAccountSnapshots`

**Api:**
- `Auth/AuthEndpoints.cs` — added `POST /auth/start`, `StartBody` record, IANA TZ validation via `TimeZoneInfo.TryFindSystemTimeZoneById`, `new_account` in `ToEntryContextString`
- `Program.cs` — registered `AccountProvisioningService`, `StartAuthService`; bound `SignupDefaults`
- `Helpers/ErrorHttpMapper.cs` — explicit `Account.PilotFull` → 409
- `appsettings.json` — added `SignupDefaults` section

**Tests:**
- `UnitTests/Auth/AccountAuthCodeTests.cs` — NEW: 11 unit tests for `CreateForNewAccount` validation
- `IntegrationTests/Api/KeepApiWebFactory.cs` — added `PilotCapWebFactory` (IsPilot=true, MaxPilotAccounts=1, own Testcontainers instance)
- `IntegrationTests/Api/AuthStartTests.cs` — NEW: `AuthStartTests` (18 tests) + `AuthStartPilotCapTests` (2 tests)

### Key decisions applied

| # | Decision | Applied |
|---|----------|---------|
| ADR-065 | `EntryContext.NewAccount = 1`; `CreateForNewAccount` factory with snapshot validation; `Create` guard | ✓ |
| ADR-066 | Config-backed `SignupDefaults`; no plan/pilot values hard-coded | ✓ |
| ADR-067 | 3-query `ClassifyStartRequestAsync`: active AccountUsers → any AccountUser → any User → NewAccount | ✓ |
| ADR-068 | Context-specific invalidation in `CommitStartCodeAsync`; composite index on email+context | ✓ |
| ADR-069 | `CommitNewAccountExchangeAsync`: atomic consume + two-phase graph; unconsumed on any failure | ✓ |
| ADR-070 | Pilot cap at both `/start` and `/exchange`; cap-full rejection leaves code unconsumed | ✓ |
| ADR-071 | `NewAccountSubject` only email distinction; same `BuildHtmlBody` template | ✓ |
| ADR-072 | `Account.PilotFull` explicit 409 in mapper | ✓ |
| ADR-073 | IANA TZ via `TimeZoneInfo.TryFindSystemTimeZoneById` at endpoint | ✓ |

---

## Build state

- `dotnet build` → 0 errors, 0 warnings
- Architecture tests → 14/14 passing
- Unit tests → 271/271 passing (11 new Phase 5C AccountAuthCode tests)
- Integration tests → 69/69 passing (20 new Phase 5C tests)
- Total → 354/354 passing

---

## Watch-outs carried forward

- **ADR-058** — still marked `Locked` in decision index; superseded by 5A real-auth. Update when revisiting.
- **AnonymousCurrentUser** — kept for potential worker/test use; not registered in production.
- **SystemClock** FQDN in Program.cs — `OpHalo.Foundation.Infrastructure.Services.SystemClock` (avoids collision).
- **Schema-drop reset pattern** — integration test factory uses `DROP SCHEMA public CASCADE` + recreate + `MigrateAsync`.
- **Migration generation** — always `--startup-project src/OpHalo.Keep.Infrastructure`; needs `ConnectionStrings__DefaultConnection` env var for design-time factory.
- **No GitHub remote yet.**
- **Resend `ApiKey` and `FromAddress`** — must be set via user secrets in production; appsettings.json values are empty placeholders.
- **`App:PublicBaseUrl`** — must point at the public frontend/auth site that owns `/auth/exchange?code=...`.
- **`App:OperatorBaseUrl`** — Phase 5D invite links should add this following the reference app.
- **`UseRateLimiter` skipped in Testing** — intentional.
- **`CountActivePilotAccountsAsync`** — counts all `IsPilot = true` without filtering `CommercialState != Canceled`. Conservative count is safe for now; refine with a join on `Accounts.LifecycleState` if needed.
- **`SignupDefaultsSettings` startup validation** — `TrialDurationDays <= 0` and `MaxPilotAccounts <= 0` are not validated at startup. Add `IValidateOptions<SignupDefaultsSettings>` in a follow-up.
- **Session creation failure test** — not covered in 5C integration tests. Would require mocking `IAccountSessionService` to throw for a specific account ID. Deferred; 5B session-failure path is already covered.

---

## Next session — Phase 5D (member invites)

Phase 5D builds the member invite flow: sending invites, invite acceptance via magic-link exchange,
and the `InvitedUser` entry context. Discovery session should read the reference app
`_reference/src/OpHalo.Application/Accounts/Commands/Invites/` and the existing Phase 4/5 decisions
to plan the new flow.

Read the build plan §5D before proposing any design.
