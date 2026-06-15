# Build Log 015 — Phase 7B Implementation: Application + Infrastructure + API + HTTP Tests

**Date:** 2026-06-15
**Phase:** 7B Session A + Session B
**Commits:** `a1b67ee` (Session A), Session B (this session)

---

## Purpose

Phase 7B completes the first full vertical slice through the Keep bounded context:
a customer submits a public intake request and an operator can retrieve the open
request list over HTTP. Session A built the Application + Infrastructure layers;
Session B wired those into the API host and proved the vertical with HTTP
integration tests.

---

## Session A — What was built

### Keep.Application

| File | Notes |
|------|-------|
| `Abstractions/AccountAccessSnapshot.cs` | Foundation account state as Keep read model |
| `Abstractions/AccountUserSnapshot.cs` | AccountUser state as Keep read model |
| `PublicIntake/PublicIntakeCommitResult.cs` | Explicit enum: Committed=1, UniqueTokenCollision=2 |
| `PublicIntake/IKeepIntakePersistence.cs` | Intent-revealing interface, EF-free |
| `PublicIntake/CreateKeepPublicIntakeCommand.cs` | Command record |
| `PublicIntake/CreateKeepPublicIntakeResult.cs` | Result record: RequestId, ReferenceCode, PageToken |
| `PublicIntake/CreateKeepPublicIntakeService.cs` | Full service: null guard → hash → link → snapshot → access → feature → customer → retry loop |
| `Requests/IKeepRequestListPersistence.cs` | Intent-revealing interface, EF-free |
| `Requests/KeepRequestSummary.cs` | Operator list read model |
| `Requests/GetKeepRequestListResult.cs` | Result wrapper |
| `Requests/GetKeepRequestListService.cs` | Auth → permission → access → feature → list |

Key service design decisions:
- All public gate failures return `keep.public_intake.unavailable` — no gate-specific
  codes on the public surface (ADR-056)
- `KeepRequestStatus` maps to lowercase snake_case slugs in `MapStatus` with
  `default: throw` (ADR-057)
- Bounded retry (MaxAttempts=5) in `CreateKeepPublicIntakeService` covers both
  pre-check and commit-time unique constraint collisions

### Keep.Infrastructure

| File | Notes |
|------|-------|
| `Persistence/KeepIntakePersistence.cs` | EF impl: link lookup, snapshot, customer find/create, two-attempt commit with Postgres duplicate-key collision detection |
| `Persistence/KeepRequestListPersistence.cs` | EF impl: account snapshot, user snapshot, open requests ordered by LastBusinessActivityAt |
| `OpHalo.Keep.Infrastructure.csproj` | Added `Npgsql.EntityFrameworkCore.PostgreSQL` |

### Unit Tests (+30)

| File | Count |
|------|-------|
| `UnitTests/Keep/KeepPublicIntakeServiceTests.cs` | 17 tests, manual fakes |
| `UnitTests/Keep/KeepRequestListServiceTests.cs` | 13 tests, manual fakes |

---

## Session B — What was built

### Foundation.Infrastructure

| File | Notes |
|------|-------|
| `Security/AnonymousCurrentUser.cs` | ICurrentUser placeholder until Phase 5 auth: IsAuthenticated=false, Guid.Empty IDs (ADR-058) |

### OpHalo.Api

| File | Notes |
|------|-------|
| `Keep/PublicIntakeRequest.cs` | Body DTO: customerName, customerPhone, customerEmail?, description, emailNotificationsEnabled? (accepted/ignored per ADR-059) |
| `Program.cs` | Full rewrite — see below |
| `appsettings.json` | Added `ConnectionStrings:DefaultConnection` (empty; supplied by environment/secrets) |

**Program.cs — key wiring decisions:**

- `AddProblemDetails()` first — RFC 7807 support across all error responses
- `AddOpenApi()` + `app.MapOpenApi()` in Development
- `OpHaloDbContext` registered via manual `AddScoped` factory so the Keep.Infrastructure
  assembly can be passed to the constructor alongside `IClock` from DI (ADR-048).
  Connection string read lazily from `IConfiguration` inside the factory — ensures
  `WebApplicationFactory.ConfigureAppConfiguration` overrides are visible at scope
  creation time (the fully-merged `IConfiguration` in DI includes test overrides;
  `builder.Configuration` at startup does not)
- `UseHttpsRedirection` skipped when environment is `Testing` (WebApplicationFactory
  sets this — prevents test client being chased from http to https)
- `UseRateLimiter()` before routes — per-IP fixed-window `"public-intake"` policy
  (ADR-060): 10 req/min, CF-Connecting-IP → X-Forwarded-For → RemoteIpAddress
- Both public intake routes apply `.RequireRateLimiting("public-intake")`
- `GET /keep/requests` has no `RequireAuthorization()` until Phase 5 auth (ADR-058)
- Error mapper is a local `ToProblem(Error)` helper returning RFC 7807 via
  `Results.Problem(statusCode, title, detail: error.Message, type: "about:blank",
  extensions: ["code"] = error.Code)`. Validation errors fall through to the default
  400 — no enumeration of specific codes needed (build-log/014)

### IntegrationTests

| File | Notes |
|------|-------|
| `Api/KeepApiWebFactory.cs` | WebApplicationFactory<Program> + IAsyncLifetime; Testcontainers postgres:17.5-alpine; ConfigureAppConfiguration injects test connection string; ConfigureTestServices replaces ICurrentUser only; ResetDatabaseAsync drops/recreates schema + migrates; CurrentUser property controls auth state per test |
| `Api/KeepIntakeApiTests.cs` | 7 HTTP tests (all 7 from build-log/014 minimum list) |

**HTTP tests (7):**

| # | Test | Status |
|---|------|--------|
| 1 | POST public intake with valid token → 201 + persisted request verified via DbContext | ✅ |
| 2 | POST legacy alias `/continuity/` → 201 | ✅ |
| 3 | Authenticated owner GET /keep/requests → 200 with seeded request | ✅ |
| 4 | Anonymous GET /keep/requests → 401 ProblemDetails with `auth.unauthorized` | ✅ |
| 5 | Authenticated but no DB membership GET /keep/requests → 403 ProblemDetails with `auth.forbidden` | ✅ |
| 6 | POST public intake with missing customerName → 400 | ✅ |
| 7 | POST public intake with unknown token → 422 ProblemDetails with `keep.public_intake.unavailable` | ✅ |

---

## ADRs added this session

| ADR | Decision |
|-----|----------|
| ADR-055 | Intent-revealing persistence abstractions + AccountAccessSnapshot/AccountUserSnapshot read models |
| ADR-056 | All public intake gate failures return generic `keep.public_intake.unavailable` |
| ADR-057 | KeepRequestStatus serializes to lowercase snake_case; exhaustive switch with `default: throw` |
| ADR-058 | Interim auth: AnonymousCurrentUser in production, ICurrentUser overridden in tests |
| ADR-059 | HTTP status/body contract locked for Phase 7B |
| ADR-060 | Rate limiting: per-IP fixed-window 10 req/min, CF-Connecting-IP priority (deploy-time constraint), fixed window over sliding window accepted for intake form |

---

## Test counts

| Suite | Before | After |
|-------|--------|-------|
| UnitTests | 230 | 260 (+30) |
| ArchitectureTests | 14 | 14 |
| IntegrationTests | 15 | 22 (+7) |

All suites green: **0 warnings, 0 errors**.

---

## Watch-outs / debt carried forward

- **Connection string fail-fast** — currently deferred to first DB scope (production
  quality). A hosted service or `IStartupFilter` should validate at startup in a
  future phase before the API takes real traffic.
- **Phone normalization** — `primaryPhone` stored as submitted (trimmed only)
- **AnonymousCurrentUser** — placeholder for Phase 5 auth
- **Two-phase provisioning save** — canonical pattern, future repository must encapsulate
- **Notifications** — `CustomerEmail` and `EmailNotificationsEnabled` stored but no
  delivery until notifications phase (ADR-053, ADR-059)
- **No GitHub remote yet**

---

## Exit gate (build-log/014)

- [x] `OpHalo.Api` no longer exposes the weather template endpoint
- [x] API compiles with `public partial class Program`
- [x] Public intake routes create Keep requests via the Session A service
- [x] Legacy public intake alias delegates to the same handler
- [x] Operator list is HTTP-visible and fail-closed without real auth
- [x] HTTP integration tests pass against real PostgreSQL
- [x] Build, unit, architecture, and integration suites are green
