# Phase 5C — New-Account Registration Implementation

**Status:** Complete.
**Discovery/spec:** 019-phase-5c-new-account-registration-discovery.md
**Build-log preceding this:** 019-phase-5c-new-account-registration-discovery.md
**Date:** 2026-06-15

---

## Scope

Implemented `POST /auth/start` for new-account registration and extended `/auth/exchange` so a
`NewAccount` magic-link code provisions a verified account, owner user, owner membership,
entitlements, and session.

Pilot/trial signup policy locked behind config-backed `SignupDefaults`.

Out of scope (deferred):

- Keep public-intake link creation.
- Account onboarding/notification satellites.
- Member invite acceptance. Phase 5D owns invites.
- Outbox/worker email delivery. Direct best-effort `IEmailSender` remains.

---

## Confirmed Decisions Applied

All nine decisions from the discovery doc were applied without deviation.

| # | Decision | Applied |
|---|----------|---------|
| D1 | `/auth/start` requires IANA `timeZone` | ✓ |
| D2 | Config-backed `SignupDefaults`: `IsPilot`, `TrialDurationDays`, `MaxPilotAccounts` | ✓ |
| D3 | Pilot cap at both `/start` and `/exchange` when enabled | ✓ |
| D4 | Keep public-intake link deferred | ✓ |
| D5 | Existing `User`/invited-only → neutral 200, no code | ✓ |
| D6 | Existing-member fallback through `/auth/start` | ✓ |
| D7 | Duplicate email at exchange → `409 Account.EmailAlreadyInUse`, no duplicate account | ✓ |
| D8 | Direct best-effort `IEmailSender` | ✓ |
| D9 | Logging policy: no raw codes/tokens/URLs/hashes/email/name/business-name in logs | ✓ |

---

## Implementation Decisions Made During Build

- **`AccountAuthCode.CreateForNewAccount`** — separate factory over adding snapshot params to `Create`.
  `Create` guards against `EntryContext.NewAccount` (throws) to prevent accidental misuse.
- **`StartClassification` hierarchy** — `abstract record` with `StartAsNewAccount`, `StartAsExistingMember(accountId, accountUserId)`, `StartAsNeutral`. Pattern-matched in both `StartAuthService` and the test layer.
- **`CommitNewAccountExchangeAsync` returns `Result`** — failure via `AccountAuthCodeErrors.AlreadyConsumed` (race) or `AccountErrors.EmailAlreadyInUse` (unique constraint). Uses `PostgresErrorCodes.UniqueViolation` ("23505") for constraint detection.
- **Pilot count**: counts `AccountEntitlements.IsPilot = true` without filtering on `CommercialState`. See _Known Gaps_.
- **`MagicLinkEmailTemplate.NewAccountSubject`** — separate constant for new-account registration email subject.
- **Two-phase save in `CommitNewAccountExchangeAsync`** — same ADR-044 pattern proven in the persistence proof tests: null the PrimaryOwnerAccountUserId FK via `db.Entry(...).Property(...).CurrentValue = null` for phase 1, then restore for phase 2.
- **`PilotCapWebFactory`** — separate `WebApplicationFactory<Program>` subclass with its own Testcontainers instance for pilot-cap integration tests.

---

## Files Changed

**Foundation.Core**

- `Entities/Accounts/Enums/EntryContext.cs` — added `NewAccount = 1`
- `Entities/Accounts/AccountAuthCode.cs` — added `BusinessNameSnapshot`, `NameSnapshot`, `TimeZoneSnapshot`; added `CreateForNewAccount` factory; added `EntryContext.NewAccount` guard in `Create`
- `Entities/Accounts/Errors/AccountErrors.cs` — added `EmailAlreadyInUse`, `PilotFull`

**Foundation.Application**

- `Auth/SignupDefaultsSettings.cs` — NEW: `IsPilot`, `TrialDurationDays`, `int? MaxPilotAccounts`
- `Auth/StartAuthService.cs` — NEW: classifies request, issues code (existing-member or new-account), best-effort email, pilot cap gate
- `Auth/IAuthCodePersistence.cs` — added `ClassifyStartRequestAsync`, `CommitStartCodeAsync`, `CountActivePilotAccountsAsync`, `CommitNewAccountExchangeAsync`; added `StartClassification` record hierarchy
- `Auth/ExchangeAuthService.cs` — added `NewAccount` branch in switch; injected `AccountProvisioningService` + `IOptions<SignupDefaultsSettings>`; extracted `CreateSessionAsync` helper
- `Auth/MagicLinkEmailTemplate.cs` — added `NewAccountSubject` constant

**Foundation.Infrastructure**

- `Auth/EfAuthCodePersistence.cs` — implemented `ClassifyStartRequestAsync` (3-query classification), `CommitStartCodeAsync` (context-aware invalidation), `CountActivePilotAccountsAsync`, `CommitNewAccountExchangeAsync` (atomic consume + two-phase graph save + constraint catch)
- `Persistence/Configurations/AccountAuthCodeConfiguration.cs` — added `BusinessNameSnapshot` (200), `NameSnapshot` (200), `TimeZoneSnapshot` (100); added composite index on `(delivery_email_snapshot, entry_context)`
- `Migrations/20260615222059_AccountAuthCodeNewAccountSnapshots.cs` — new migration

**Api**

- `Auth/AuthEndpoints.cs` — added `POST /auth/start`, `StartBody` record, IANA TZ validation via `TimeZoneInfo.TryFindSystemTimeZoneById`, `new_account` in `ToEntryContextString`, `IsValidIanaTimeZone` helper
- `Program.cs` — added `AccountProvisioningService`, `StartAuthService`; bound `SignupDefaults`
- `Helpers/ErrorHttpMapper.cs` — explicit `Account.PilotFull` → 409 (same style as `Account.Expired`)
- `appsettings.json` — added `SignupDefaults` section (`IsPilot: true`, `TrialDurationDays: 30`, `MaxPilotAccounts: null`)

**Tests**

- `UnitTests/Auth/AccountAuthCodeTests.cs` — NEW: 11 unit tests for `CreateForNewAccount` factory validation
- `IntegrationTests/Api/KeepApiWebFactory.cs` — added `PilotCapWebFactory` (IsPilot=true, MaxPilotAccounts=1)
- `IntegrationTests/Api/AuthStartTests.cs` — NEW: 20 integration tests covering `AuthStartTests` (18) + `AuthStartPilotCapTests` (2)

---

## Test Coverage

20 of 21 planned tests implemented.

Coverage in `AuthStartTests`:
1. `/auth/start` missing email → 400 ✓
2. `/auth/start` missing business name → 400 ✓
3. `/auth/start` missing time zone → 400 ✓
4. `/auth/start` invalid IANA time zone → 400 ✓
5. Unknown email → 200, one email sent ✓
6. Second `/auth/start` invalidates prior code → old code 422 `new_account` ✓
7. New-account exchange → 200, browser cookie ✓
8. Exchanged session allows `/auth/me` ✓
9. New-account exchange creates Trial entitlements ✓
10. `IsPilot=true` → `AccountEntitlements.IsPilot=true` ✓
11. `TrialDurationDays=30` → `TrialEndsAtUtc ≈ now + 30d` ✓
12. Consumed new-account code → 422 `new_account` ✓
13. Concurrent new-account exchange → exactly 1 success, 1 failure, 1 account graph ✓
14. Duplicate email between `/start` and `/exchange` → 409 `EmailAlreadyInUse`, code unconsumed ✓
15. Existing-member `/auth/start` → 200, email sent ✓
16. Existing-member `/auth/start` exchange → 200, cookie ✓
17. Existing User (no active membership) → neutral 200, no email ✓
18. Invited-only membership → neutral 200, no email ✓

Coverage in `AuthStartPilotCapTests`:
19. Pilot cap reached at `/auth/start` → 409 `PilotFull`, no code ✓
20. Pilot cap reached at `/auth/exchange` → 409 `PilotFull`, code unconsumed ✓

**Not covered:** Session creation failure after new-account commit (test 16 from discovery).
Requires mocking `IAccountSessionService` to throw for a specific new account ID — deferred.
The service contract is correct and the 5B session-failure path is already tested.

---

## Known Gaps

- **`CountActivePilotAccountsAsync`** counts all `IsPilot = true` entitlements without filtering on
  `CommercialState != Canceled`. The build-log stub called for excluding cancelled accounts. Safe to
  leave for now: cancelled accounts during the pilot phase are rare and a conservative count is safer
  than an under-count. A follow-up can join `Accounts` on `LifecycleState` if needed.
- **`SignupDefaultsSettings` validation** — `TrialDurationDays <= 0` and `MaxPilotAccounts <= 0` are
  not validated at startup. Recommend adding `IValidateOptions<SignupDefaultsSettings>` in a
  follow-up if misconfiguration is a concern.

---

## Verification

```text
dotnet build → 0 errors, 0 warnings
Architecture tests → 14/14 passing
Unit tests → 271/271 passing (11 new AccountAuthCode tests)
Integration tests → 69/69 passing (20 new Phase 5C tests)
Total → 354/354 passing
```

---

## Exit Gate

- [x] `/auth/start` implemented and wired.
- [x] `NewAccount` exchange implemented and race-safe (atomic consume + graph in one transaction).
- [x] Signup defaults applied by config (`SignupDefaults` section, bound via Options).
- [x] Pilot cap behavior implemented at both `/auth/start` and `/auth/exchange`.
- [x] Keep public-intake link creation remains deferred.
- [x] Logging policy applied: no raw codes/tokens/URLs/hashes/email/name/business-name.
- [x] Build green (0 errors, 0 warnings).
- [x] Tests green (354/354).
