# Session Log — OpHalo Foundation

**Last updated:** 2026-06-15
**Next session tier:** Tier 1 — Phase 5E or next phase (discovery)
**Branch:** `main` (no remote yet)

---

## Phase 5D — COMPLETE

Member invites: send (`POST /accounts/me/invite`) + accept (`POST /accounts/invite/accept`). All exit-gate items verified. 389/389 tests passing.

### What was built

**Foundation.Core:**
- `Entities/Accounts/Enums/EntryContext.cs` — added `InvitedUser = 3`
- `Entities/Accounts/Errors/InviteErrors.cs` — NEW: `Forbidden`, `InvalidToken`, `Expired`, `AlreadyActive`, `SeatLimitReached`

**Foundation.Application:**
- `Auth/InviteTokenGenerator.cs` — NEW: 32-byte URL-safe Base64 token; uppercase SHA-256 hex hash (ADR-076)
- `Auth/IInvitePersistence.cs` — NEW: `GetSendInviteContextAsync`, `CommitSendInviteAsync`, `CommitAcceptInviteAsync`; `SendInviteContext`, `AcceptedInvite` records
- `Auth/SendInviteService.cs` — NEW: permission gate, seat-limit check (bypassed for resend), best-effort email with try/catch
- `Auth/AcceptInviteService.cs` — NEW: blank-token guard, `AcceptInviteResult` dedicated type, session outside transaction
- `Auth/InviteEmailTemplate.cs` — NEW: `BuildSubject`, `BuildHtmlBody`
- `Auth/MagicLinkSettings.cs` — added `OperatorBaseUrl`, corrected stale `"Auth"` → `"App"` comment

**Foundation.Infrastructure:**
- `Auth/EfInvitePersistence.cs` — NEW: 5-query context load; `EntityState.Detached` check for new vs tracked invite; `CommitAcceptInviteAsync` uses PostgreSQL savepoint for User creation race + `ExecuteUpdateAsync` conditioned on `Invited` state for activation race; `UpdatedAtUtc` set explicitly in `ExecuteUpdateAsync`

**Api:**
- `Accounts/AccountEndpoints.cs` — NEW: `POST /accounts/me/invite` (RequireAuthorization) + `POST /accounts/invite/accept` (RequireRateLimiting "auth")
- `Program.cs` — registered `SendInviteService`, `AcceptInviteService`, `IInvitePersistence`; mapped `AccountEndpoints`
- `Helpers/ErrorHttpMapper.cs` — explicit `Invite.SeatLimitReached` → 409
- `appsettings.json` — added `OperatorBaseUrl` to `App` section

**Tests:**
- `UnitTests/Auth/InviteTokenGeneratorTests.cs` — NEW: 9 tests
- `IntegrationTests/Api/InviteTests.cs` — NEW: 26 tests
- `IntegrationTests/Api/KeepApiWebFactory.cs` — added `App:OperatorBaseUrl` to test config; added `ExtractInviteToken()` helper

### Key decisions applied

| # | Decision | Applied |
|---|----------|---------|
| ADR-074 | `EntryContext.InvitedUser = 3`; not wired to exchange | ✓ |
| ADR-075 | Non-owner roles only; `MembersManage` gate; `OperatorBaseUrl` in `MagicLinkSettings`; resend skips seat-limit check | ✓ |
| ADR-076 | Direct token endpoint; uppercase SHA-256 hex; `ExecuteUpdateAsync` race guard; savepoint for User race; session outside tx; browser-only | ✓ |
| ADR-077 | Separate `IInvitePersistence` seam; seat limit via `FeatureAccessPolicy.ResolveLimit`; `Invite.SeatLimitReached` explicit 409 | ✓ |

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
- Unit tests → 280/280 passing
- Integration tests → 95/95 passing
- Total → 389/389 passing

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
- **`App:OperatorBaseUrl`** — must point at the operator app that owns `/invite/accept?token=...`.
- **`UseRateLimiter` skipped in Testing** — intentional.
- **`CountActivePilotAccountsAsync`** — counts all `IsPilot = true` without filtering `CommercialState != Canceled`. Conservative count is safe for now.
- **`SignupDefaultsSettings` startup validation** — `TrialDurationDays <= 0` and `MaxPilotAccounts <= 0` are not validated at startup. Add `IValidateOptions<SignupDefaultsSettings>` in a follow-up.
- **Session creation failure test (invite accept)** — not covered in 5D integration tests. Would require overriding `IAccountSessionService`. Deferred; the 503 path is exercised by existing 5B tests.
- **Mobile invite accept** — deferred (D9). Needs `clientType` parsing and bearer response when added.

---

## Next session — Phase 5E or next build-plan phase

Read the build plan to determine what comes after 5D. Likely: member management (suspend/remove/role change) or moving to a later phase. Start with Tier 1 discovery.
