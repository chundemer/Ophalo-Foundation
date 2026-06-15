# Build Log 017 — Phase 5A: Auth / Session Foundation

**Phase:** 5A
**ADRs:** ADR-061 · ADR-062 · ADR-063 · ADR-064
**Status:** Implemented and green

---

## Scope

One server-side opaque session model for browser and mobile. No JWT. Wires ASP.NET Core authentication/authorization, protects existing routes, and adds `GET /auth/me` and `POST /auth/logout`.

---

## What was built

### Core

**`AccountSession`** (`Foundation.Core/Entities/Accounts/AccountSession.cs`)
Standalone entity (does not extend `BaseEntity` — no soft-delete, no generic audit timestamps). Fields: `Id`, `AccountId`, `AccountUserId`, `SessionTokenHash`, `ClientType` (SessionClientType), `DeviceName?`, `CreatedAtUtc`, `ExpiresAtUtc`, `LastActivityAtUtc`, `LastSeenAtUtc`, `RevokedAtUtc?`. Factory validates all args including UTC kind. `Revoke` is idempotent. `RenewActivity` updates both `LastActivityAtUtc` and `LastSeenAtUtc`, clamped to `ExpiresAtUtc`, no-op if invalid.

**`SessionClientType`** (`Foundation.Core/Entities/Accounts/Enums/SessionClientType.cs`)
`Browser = 1`, `MobileApp = 2`, `Admin = 3`.

**`AuthConstants`** (`Foundation.Core/Constants/AuthConstants.cs`)
`SessionAbsoluteExpiryDays = 30`, `SessionInactivityWindowDays = 7`, `SessionSchemeName = "OpHaloSession"`, `CookieName = "ophalo.sid"`, `AccountIdClaimType = "account_id"`, `IsVerifiedClaimType = "is_verified"`, `SessionRenewalThresholdMinutes = 5`.

### Application

**`IAccountSessionService`** and **`CreateSessionResult`** (`Foundation.Application/Abstractions/Security/`)
Foundation-neutral name — sessions authenticate across browser, mobile, admin, and Keep. Not Keep-owned.

### Infrastructure

**`SessionHasher`** — SHA-256 lowercase hex; throws on null/whitespace.

**`SessionData`** — auth projection record returned by `ISessionStore`. Includes `AccountUserMembershipStatus?` (null signals missing AccountUser or AccountId mismatch).

**`ISessionStore` / `SessionStore`** — two-query load (session then AccountUser); critical AccountId cross-check (`accountUser.AccountId != session.AccountId → null`).

**`SessionAuthenticationHandler`** — dual transport: Bearer header first (mobile), cookie fallback (browser). Active-only gate (`MembershipStatus != Active → NoResult`). Renewal throttle (5-minute threshold). `HandleChallengeAsync` sets 401 + `WWW-Authenticate` header with no body. `HandleForbiddenAsync` sets 403.

**`CurrentUser`** — replaces `AnonymousCurrentUser` in production DI. Reads claims from HTTP context.

**`AccountSessionService`** — `CreateSession`: 32-byte random token, Base64, hash, `AccountSession.Create`, save. `RevokeSessionByHash`: idempotent revoke via domain `Revoke()` method.

**`AccountSessionConfiguration`** — maps to `account_sessions` table. Unique index on `SessionTokenHash`. Index on `AccountUserId`. `IsRevoked` ignored (derived).

**Migration `20260615173121_AccountSessions`** — includes both `.cs` and `.Designer.cs` files; `OpHaloDbContextModelSnapshot` updated.

**`FrameworkReference Include="Microsoft.AspNetCore.App"`** added to Foundation.Infrastructure.csproj. Note: four `Microsoft.Extensions.Configuration.*` package references are now NU1510 warnings (redundant); clean up in a follow-up.

### API

**`AuthCookieSettings` / `AuthCookieOptionsFactory`** — cookie options factory sourced from `Auth:CookieDomain` config; secure outside Development; HttpOnly/Lax/Path="/".

**`ErrorHttpMapper`** — replaces the inline `ToProblem`/`Problem` helper functions. Pattern-based RFC 7807 ProblemDetails mapper; explicit codes first, then `.EndsWith`/`.Contains` patterns for all HTTP status families; unknown codes → 400.

**`AuthEndpoints`** — `GET /auth/me` (requires auth, returns `{ accountUserId, accountId, isAuthenticated, isVerified }`) and `POST /auth/logout` (requires auth, Bearer-first then cookie, idempotent revoke + cookie delete). Both under `"auth"` rate limiter.

**`Program.cs`** — adds `AddHttpContextAccessor`, `Configure<AuthCookieSettings>`, `AddAuthentication`, `AddAuthorization`, `ISessionStore`/`IAccountSessionService`/`AuthCookieOptionsFactory` registrations. `ICurrentUser → CurrentUser` in production. Middleware order: `UseAuthentication → UseAuthorization → UseRateLimiter`. `GET /keep/requests` adds `RequireAuthorization()`. `"auth"` rate limiter policy added. `MapAuthEndpoints()` wired.

---

## Decisions made

| ADR | Summary |
|-----|---------|
| ADR-061 | AccountSession is a standalone entity; LastActivityAtUtc and LastSeenAtUtc updated together |
| ADR-062 | One session model; Bearer-first (mobile priority), cookie fallback; raw token never stored |
| ADR-063 | Fails closed unless Active; AccountId cross-check in SessionStore |
| ADR-064 | ErrorHttpMapper replaces inline helpers; pattern-based, explicit codes first |

`ADR-058` (Locked → still in the index as Locked) is superseded in practice: real framework auth is now wired and `AnonymousCurrentUser` is no longer registered in production.

---

## Tests

### Integration (new)

**`AuthApiTests`** (12 tests):
- `Me_Anonymous_Returns401`
- `Me_ValidCookieSession_Returns200WithIdentity`
- `Me_ValidBearerSession_Returns200`
- `Me_RevokedSession_Returns401`
- `Me_ExpiredSession_Returns401`
- `Me_SuspendedMember_Returns401`
- `Me_RemovedMember_Returns401`
- `Logout_Anonymous_Returns401`
- `Logout_WithValidCookieSession_Returns200AndRevokesSession`
- `Logout_WithBearerToken_Returns200AndRevokesSession`
- `KeepRequests_RevokedSession_Returns401`
- `KeepRequests_SuspendedMember_Returns401`

### Integration (updated)

**`KeepIntakeApiTests`** — removed `ICurrentUser` override pattern. Test 3 sends a real session cookie. Test 4 (anonymous → 401) no longer checks body (framework challenge has no body). Test 5 redesigned: seeds Account B without entitlements → `GetAccountAccessSnapshotAsync` returns null → 403.

**`KeepApiWebFactory`** — removed `CurrentUser` property and `ConfigureTestServices` block. Added `SeedSessionAsync(accountUserId, accountId, clientType, overrideCreatedAt?)` returning raw token.

### All tests

**34/34 passing** after this session.

---

## Exit gate

- [x] `dotnet build` — 0 errors (4 NU1510 warnings, deferred cleanup)
- [x] All 34 tests pass
- [x] Bearer token route authenticates correctly
- [x] Cookie route authenticates correctly
- [x] Revoked session → 401
- [x] Expired session → 401
- [x] Suspended member → 401 (Active-only gate in handler)
- [x] Removed member → 401
- [x] `POST /auth/logout` with auth → 200, session revoked in DB, Set-Cookie deletion header present
- [x] `POST /auth/logout` without auth → 401 (requires authorization)
- [x] `GET /keep/requests` anonymous → 401 (framework challenge, no body)
- [x] `GET /keep/requests` no entitlements → 403 with `auth.forbidden` code

---

## Risks / watch-outs

- **NU1510 warnings** — 4 redundant `Microsoft.Extensions.Configuration.*` packages in Foundation.Infrastructure.csproj after adding FrameworkReference. Clean up separately.
- **ADR-058 status** — still marked `Locked` in the index; update to `Superseded` when ADR-058 is revisited.
- **`AnonymousCurrentUser`** — kept in the codebase for potential worker/test use; not registered in production DI after this session.
- **No Phase 5B yet** — login endpoint (magic-link exchange → session creation) is out of scope. Integration tests seed sessions directly via `SeedSessionAsync`.
- **`appsettings.json Auth:CookieDomain`** is empty string (host-only cookies). Set to `.ophalo.com` for cross-subdomain sharing when deploying.
