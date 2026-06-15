# Session Log — OpHalo Foundation

**Last updated:** 2026-06-15
**Next session tier:** Tier 1 — Phase 5C discovery (new-account registration/start)
**Branch:** `main` (no remote yet)

---

## Phase 5B — COMPLETE

Existing-member magic-link sign-in and exchange. All exit-gate items verified.

### What was built

**Foundation.Core:**
- `Entities/Accounts/Enums/EntryContext.cs` — `ExistingMember = 2` (values match reference to avoid future conflicts)
- `Entities/Accounts/AccountAuthCode.cs` — single-use auth code entity; `AccountId?` and `TargetAccountUserId?` are nullable for Phase 5C NewAccount path; `Invalidate()` domain method
- `Entities/Accounts/Errors/AccountAuthCodeErrors.cs` — `NotFound`, `Expired`, `AlreadyConsumed`, `CannotConsumeInvalidated`, `CannotInvalidateConsumed`
- `AccountErrors.cs` — added `SessionCreationFailed` ("We could not finish signing you in. Please try signing in again.")

**Foundation.Application:**
- `Auth/MagicLinkSettings.cs` — `PublicBaseUrl`, bound from `"App"` section as `App:PublicBaseUrl`; this is the public frontend/auth site URL, not the API URL
- `Auth/IAuthCodePersistence.cs` — `FindEligibleSignInMemberByEmailAsync`, `CommitSignInCodeAsync` (atomic), `FindCodeByHashAsync`, `ConsumeCodeAsync`
- `Auth/MagicLinkCodeGenerator.cs` — `RandomNumberGenerator.GetBytes(32)` → URL-safe Base64 + SHA-256 hex
- `Auth/MagicLinkEmailTemplate.cs` — subject + HTML body builder
- `Auth/SignInAuthService.cs` — issues magic link for active existing members; neutral 200 for unknown/ineligible/ambiguous email; best-effort email delivery (delivery failure never changes public response)
- `Auth/ExchangeAuthService.cs` — validates code state, routes by `EntryContext`, consumes atomically (race guard), creates session; logs session creation failure with AccountId/AccountUserId/CodeId; no raw codes/tokens/emails in logs
- Added `Microsoft.Extensions.Logging.Abstractions` + `Microsoft.Extensions.Options` to csproj

**Foundation.Infrastructure:**
- `Auth/EfAuthCodePersistence.cs` — implements `IAuthCodePersistence`; `FindEligibleSignInMemberByEmailAsync` uses `.Take(2)` to detect multi-membership ambiguity; `CommitSignInCodeAsync` wraps invalidation + add + save in a single transaction
- `Email/ResendEmailSender.cs` — typed `HttpClient` pointing to `https://api.resend.com/emails`
- `Email/ResendSettings.cs` — `ApiKey`, `FromAddress`
- `Persistence/Configurations/AccountAuthCodeConfiguration.cs` — `account_auth_codes` table, code hash unique index, target account user index, expiry index
- `Persistence/OpHaloDbContext.cs` — added `AccountAuthCodes` DbSet
- Migration: `AccountAuthCodes`

**Api:**
- `Auth/AuthEndpoints.cs` — `POST /auth/signin` (anonymous, rate-limited), `POST /auth/exchange` (anonymous, rate-limited); `clientType` parsed from string only (`"browser"` / `"mobile_app"`); mobile gets token in body, browser gets HttpOnly cookie only; `entryContext` added to failure via `ErrorHttpMapper.ToHttpResult` extra extensions — status always from mapper, never overridden
- `Helpers/ErrorHttpMapper.cs` — refactored to `GetProblemMeta` tuple approach so `extraExtensions` can be threaded in at one call site
- `Program.cs` — wired `SignInAuthService`, `ExchangeAuthService`, `EfAuthCodePersistence`, `ResendEmailSender`; `UseRateLimiter` skipped in Testing environment
- `appsettings.json` — added `App:PublicBaseUrl`, `Resend:ApiKey`, `Resend:FromAddress`

**Tests:**
- `Api/KeepApiWebFactory.cs` — added `CapturingEmailSender` (test double for `IEmailSender`), `App:PublicBaseUrl` config override, `IEmailSender` replacement in `ConfigureTestServices`
- `Api/AuthMagicLinkTests.cs` — 15 tests covering: unknown/ineligible/suspended/removed email neutral 200, missing input 400, second sign-in invalidates prior code, valid exchange sets cookie, session from exchange allows `/auth/me`, unknown code 404, consumed/invalidated 422 with entryContext, invalid client type 400, mobile returns token in body (no cookie), mobile Bearer `/me`, concurrent exchange race (exactly one wins)

### Key decisions applied

| # | Decision | Applied |
|---|---|---|
| D1 | Code entity: `AccountAuthCode` | ✓ |
| D2 | Phase 5B: `/signin` + `/exchange` only | ✓ |
| D4 | Direct `IEmailSender`, best-effort | ✓ |
| D5 | Optional `clientType`/`deviceName` at exchange | ✓ |
| D8 | Unknown/ineligible/ambiguous email: neutral 200, no code | ✓ |
| — | Logging policy: only session creation failure logged; no codes/tokens/emails in logs | ✓ |
| — | Atomic `CommitSignInCodeAsync`: no partial-state window on issuance | ✓ |
| — | Multi-membership `.Take(2)` guard: 2+ active memberships → neutral 200 | ✓ |
| — | Mobile exchange returns token in body; browser gets cookie only | ✓ |
| — | `ErrorHttpMapper` refactored: `extraExtensions` adds `entryContext` without status override | ✓ |
| — | Magic-link email URL uses frontend `App:PublicBaseUrl`, not API/Auth URL | ✓ |

---

## Build state

- `dotnet build` → 0 errors, 0 warnings
- Architecture tests → 14/14 passing
- Unit tests → 260/260 passing
- Integration tests → 49/49 passing (34 existing + 15 new magic link tests)

---

## Watch-outs carried forward

- **ADR-058** — still marked `Locked` in decision index; superseded by 5A real-auth. Update when revisiting.
- **AnonymousCurrentUser** — kept for potential worker/test use; not registered in production.
- **SystemClock** FQDN in Program.cs — `OpHalo.Foundation.Infrastructure.Services.SystemClock` (avoids collision).
- **Schema-drop reset pattern** — integration test factory uses `DROP SCHEMA public CASCADE` + recreate + `MigrateAsync`.
- **Migration generation** — always `--startup-project src/OpHalo.Keep.Infrastructure`; needs `ConnectionStrings__DefaultConnection` env var for design-time factory.
- **No GitHub remote yet.**
- **Resend `ApiKey` and `FromAddress`** — must be set via user secrets in production; appsettings.json values are empty placeholders.
- **`App:PublicBaseUrl`** — must point at the public frontend/auth site that owns `/auth/exchange?code=...`; that frontend route posts the code to backend `POST /auth/exchange`. Do not point this value at `OpHalo.Api`.
- **`App:OperatorBaseUrl`** — not needed for 5B, but Phase 5D invite links should add it following the reference app.
- **`UseRateLimiter` skipped in Testing** — intentional, consistent with `UseHttpsRedirection` skip.

---

## Next session — Phase 5C discovery (Tier 1)

Phase 5C adds `POST /auth/start` (new-account registration). Discovery required before any code:

1. Read reference `/auth/start` flow: `StartOnboardingCommandHandler.cs`
2. Confirm time zone approach (recommendation: required IANA field — no silent UTC default)
3. Confirm `Account.CreateVerified` signature matches what `/start` will provide
4. Confirm `AccountAuthCode` entity is sufficient or needs `BusinessNameSnapshot` / `NameSnapshot`
5. Decide account provisioning atomicity with code issuance (no outbox yet)

Do not start Phase 5C implementation without a completed discovery pass. Phase 5D (member invites) follows 5C.
