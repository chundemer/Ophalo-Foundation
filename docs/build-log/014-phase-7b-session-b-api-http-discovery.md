# Build Log 014 - Phase 7B Session B discovery: API + HTTP tests

**Date:** 2026-06-15
**Phase:** 7B Session B - API wiring + HTTP integration tests discovery
**Target repo:** `/Users/christian/saas/ophalo-foundation`

---

## Purpose

Phase 7B Session A has completed the Keep Application + Infrastructure slice:
intent-revealing persistence ports, public-intake service, operator-list service,
EF adapters, and focused unit tests. Session B should wire those services through
`OpHalo.Api` and prove the customer-submitted request can be seen by an operator
over HTTP.

This discovery pass closes the remaining API/test decisions so Claude can
implement Session B without widening scope.

---

## Current inputs from Session A

Application services:

- `CreateKeepPublicIntakeService.ExecuteAsync(CreateKeepPublicIntakeCommand, ct)`
  returns `Result<CreateKeepPublicIntakeResult>`.
- `GetKeepRequestListService.ExecuteAsync(ct)` returns
  `Result<GetKeepRequestListResult>`.

Persistence adapters:

- `KeepIntakePersistence(OpHaloDbContext)`
- `KeepRequestListPersistence(OpHaloDbContext)`

Important service contracts:

- Public gate failures use `keep.public_intake.unavailable`.
- Public validation failures use existing `KeepRequest.*Required` errors.
- Operator list returns `auth.unauthorized` or `auth.forbidden`.
- `KeepRequestStatus` is already mapped to lowercase snake_case in
  `KeepRequestSummary`.

---

## Decisions

### ADR-058 - Interim API auth is service-level ICurrentUser

Do **not** add framework authentication/authorization middleware or
`RequireAuthorization()` in Session B. Real auth is still Phase 5/next auth work.

For now:

- production API registers `ICurrentUser` as `AnonymousCurrentUser`;
- `AnonymousCurrentUser` returns `IsAuthenticated = false`,
  `IsVerified = false`, and empty IDs;
- `GET /keep/requests` calls `GetKeepRequestListService`, which returns
  `auth.unauthorized` until a real/test `ICurrentUser` is registered;
- HTTP tests override `ICurrentUser` with a test implementation through
  `WebApplicationFactory`.

Reason: this keeps production fail-closed without inventing header auth or a fake
auth scheme that could accidentally become a contract.

### ADR-059 - Session B HTTP contract

Public intake routes:

```text
POST /keep/public-intake/token/{publicIntakeToken}
POST /continuity/public-intake/token/{publicIntakeToken}
```

Both routes call the same handler and rate-limit policy.

Request body:

```json
{
  "customerName": "Jane Doe",
  "customerPhone": "555-1234",
  "customerEmail": "jane@example.com",
  "description": "Help with the boiler",
  "emailNotificationsEnabled": true
}
```

`emailNotificationsEnabled` is accepted for legacy/public contract continuity
but ignored in Session B. Do not add it to
`CreateKeepPublicIntakeCommand`, Keep domain entities, or persistence.
Notification preference/audit behavior stays deferred.

Public intake response mapping:

- success: `201` with `{ requestId, referenceCode, pageToken }`;
- validation errors (`KeepRequest.CustomerNameRequired`,
  `KeepRequest.CustomerPhoneRequired`, `KeepRequest.DescriptionRequired`):
  `400` ProblemDetails with `extensions.code`;
- public gate failures (`keep.public_intake.unavailable`): `422`
  ProblemDetails with `extensions.code`;
- unexpected exceptions: default ASP.NET 500 behavior is acceptable for this
  slice; do not build custom exception middleware yet.

Operator list route:

```text
GET /keep/requests
```

Response mapping:

- success: `200` with `{ requests: [...] }`;
- `auth.unauthorized`: `401` ProblemDetails;
- `auth.forbidden`: `403` ProblemDetails;
- no `/continuity/requests` alias in 7B.

---

## Implementation instructions

### `Foundation.Infrastructure/Security/AnonymousCurrentUser.cs`

Add a small implementation of `ICurrentUser`:

- `UserId => Guid.Empty`
- `AccountId => Guid.Empty`
- `IsAuthenticated => false`
- `IsVerified => false`

No dependency on ASP.NET types.

### `OpHalo.Api/Program.cs`

Replace the weather template. Required registrations:

- `AddOpenApi()`
- `AddRateLimiter(...)` with policy name `public-intake`
  - fixed window
  - 10 requests per minute
  - partition by `CF-Connecting-IP` header, else `RemoteIpAddress`, else
    `"unknown"`
  - rejection status `429`
- `IClock -> SystemClock`
- `ICurrentUser -> AnonymousCurrentUser`
- `IAccountAccessPolicy -> AccountAccessPolicy`
- `IUserAccessPolicy -> UserAccessPolicy`
- `IFeatureAccessPolicy -> FeatureAccessPolicy`
- `KeepTokenService`
- `IKeepIntakePersistence -> KeepIntakePersistence`
- `IKeepRequestListPersistence -> KeepRequestListPersistence`
- `CreateKeepPublicIntakeService`
- `GetKeepRequestListService`
- `OpHaloDbContext` with:
  - `UseNpgsql(connectionString, npgsql =>`
    - `MigrationsHistoryTable("__OpHaloMigrationsHistory")`
    - `MigrationsAssembly(typeof(OpHaloDbContext).Assembly.FullName)`)
  - `.UseSnakeCaseNamingConvention()`
  - Keep model assembly passed to `OpHaloDbContext` constructor.

Connection string:

- read `ConnectionStrings:DefaultConnection`;
- fail fast at startup if missing/blank.

Pipeline:

- map OpenAPI in development;
- skip HTTPS redirection when environment is `Testing` so
  `WebApplicationFactory` tests do not get redirected;
- call `UseRateLimiter()`;
- map both public intake routes with `.RequireRateLimiting("public-intake")`;
- map `GET /keep/requests` without `RequireAuthorization()` in this slice.

Expose `public partial class Program { }` at the bottom so
`WebApplicationFactory<Program>` can reference the top-level app.

### Error mapping

Keep it local to `Program.cs` for Session B. A private/static helper is enough.
Return RFC 7807 `ProblemDetails` via `Results.Problem(...)` and include:

```csharp
extensions: new Dictionary<string, object?> { ["code"] = error.Code }
```

Do not add a shared mapper abstraction until more endpoints exist.

### `appsettings.json`

Add:

```json
"ConnectionStrings": {
  "DefaultConnection": ""
}
```

The empty value is intentional: local/test/prod supply it through environment or
test configuration. Program should fail fast if it is still blank at startup.

### HTTP integration tests

Add `Microsoft.AspNetCore.Mvc.Testing` to
`tests/OpHalo.IntegrationTests/OpHalo.IntegrationTests.csproj` and add a project
reference to `src/OpHalo.Api`.

Recommended package version: `10.0.9` to match `Microsoft.AspNetCore.OpenApi`.

Test factory:

- `KeepApiWebFactory : WebApplicationFactory<Program>, IAsyncLifetime`
- start its own `postgres:17.5-alpine` Testcontainer;
- override configuration with the container connection string;
- set environment to `Testing`;
- override `ICurrentUser` with a mutable test implementation;
- provide `ResetDatabaseAsync()` that:
  - drops/recreates `public` schema;
  - calls `Database.MigrateAsync()`;
- expose helpers to create a scope and seed data.

Do not share `PostgresFixture` with the API factory unless it naturally falls out
cleanly; the API factory needs to own configuration before the host builds.

Seeding:

- use `AccountProvisioningService.CreateVerified(...)`;
- persist the provisioning graph with the canonical two-phase save from
  ADR-044:
  - temporarily set `PrimaryOwnerAccountUserId` to `null`;
  - save user/account/owner/entitlements;
  - set owner id;
  - save again;
- add `KeepPublicIntakeLink` with a hashed raw token generated by
  `KeepTokenService`.

Minimum HTTP tests:

1. `POST /keep/public-intake/token/{token}` returns `201` and persists a request.
2. `POST /continuity/public-intake/token/{token}` returns `201` through the alias.
3. Customer-submitted request appears in `GET /keep/requests` for the seeded
   operator/account.
4. `GET /keep/requests` returns `401` when test current user is anonymous.
5. `GET /keep/requests` returns `403` when the test current user resolves to no
   active/authorized membership.
6. Public intake with blank/missing form fields returns `400`.
7. Public intake with missing/bad token returns `422` and
   `keep.public_intake.unavailable`.

---

## Out of scope

- Real magic-link/session auth.
- ASP.NET auth schemes or header-based fake auth.
- Customer page routes.
- Notification delivery or notification preference persistence.
- Request detail, operator mutations, SSE, attention policies.
- Shared/global error-mapper abstraction.

---

## Exit gate

- `OpHalo.Api` no longer exposes the weather template endpoint.
- API compiles with `public partial class Program`.
- Public intake routes create Keep requests via the Session A service.
- Legacy public intake alias is present and delegates to the same handler.
- Operator list is HTTP-visible and fail-closed without real auth.
- HTTP integration tests pass against real PostgreSQL.
- Build, unit, architecture, and integration suites are green.
