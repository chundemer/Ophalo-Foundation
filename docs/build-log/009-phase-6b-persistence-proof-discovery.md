# Build Log 009 - Phase 6 Session B discovery: PostgreSQL persistence proof

**Date:** 2026-06-15
**Phase:** 6 - Persistence . **Session B discovery**
**Target repo:** `/Users/christian/saas/ophalo-foundation`

---

## Purpose

Phase 6A built the EF Core persistence infrastructure and generated the initial
migration. Session B should prove that schema against real PostgreSQL behavior,
not just EF's model. This discovery session locks the test harness and the proof
matrix for Claude to implement.

---

## Decision

Use **Testcontainers.PostgreSql** for Phase 6B integration tests.

Why:

- The risks are PostgreSQL-specific: partial unique indexes, circular FK migration
  order, Npgsql constraint exceptions, snake_case DDL, and the EF migration history
  table.
- EF InMemory cannot prove relational constraints.
- SQLite cannot faithfully prove PostgreSQL partial-index semantics or Npgsql
  provider behavior.
- The official Testcontainers .NET PostgreSQL module supports starting a pinned
  Postgres image and exposes a container connection string for tests; NuGet shows
  `Testcontainers.PostgreSql` **4.12.0** as current on 2026-06-15 and compatible
  with net10.0.

Implementation choice:

- Add `Testcontainers.PostgreSql` **4.12.0** to
  `tests/OpHalo.IntegrationTests/OpHalo.IntegrationTests.csproj`.
- Add project references from integration tests to:
  - `src/OpHalo.SharedKernel`
  - `src/OpHalo.Foundation.Core`
  - `src/OpHalo.Foundation.Application`
  - `src/OpHalo.Foundation.Infrastructure`
- Do **not** introduce repositories, `IUnitOfWork`, host DI, appsettings wiring, or
  runtime persistence abstractions in this slice. The tests can construct
  `OpHaloDbContext` directly with `DbContextOptionsBuilder`.
- Use the existing xUnit v2 package and implement fixture lifecycle manually with
  `IAsyncLifetime`; no need to add `Testcontainers.Xunit`.
- Pin the Docker image instead of using `latest`. Recommended image:
  `postgres:17.5-alpine`, unless local Docker availability or platform constraints
  make `postgres:16-alpine` more reliable.

This is ADR-043 in the decision index.

---

## Fixture shape

Add a small test fixture under `tests/OpHalo.IntegrationTests/Persistence/`, for
example `PostgresFixture`.

Responsibilities:

- Start a `PostgreSqlContainer` once for the persistence proof class.
- Build DbContext options with:
  - `UseNpgsql(container.GetConnectionString(), npgsql =>`
    - `npgsql.MigrationsHistoryTable("__OpHaloMigrationsHistory")`
    - `npgsql.MigrationsAssembly(typeof(OpHaloDbContext).Assembly.FullName)`)
  - `.UseSnakeCaseNamingConvention()`
- Provide `CreateContext(IClock? clock = null)`.
- Use a deterministic test clock so audit timestamp assertions are stable.
- Before each test, reset the schema with `Database.EnsureDeletedAsync()` then
  `Database.MigrateAsync()`.

Important: avoid sharing dirty rows between tests. Either reset before every test
or create an isolated database per test. Resetting with `EnsureDeleted/Migrate` is
fine for this small proof suite.

---

## Proof matrix for Claude

### 1. Migration applies to real PostgreSQL

Opening proof: the fixture's reset step should call `MigrateAsync()`. Add an
explicit test only if useful:

- Query `__OpHaloMigrationsHistory` and assert `InitialFoundationSchema` exists.
- Query PostgreSQL metadata if needed to confirm one or two key indexes exist.

Do not duplicate the whole migration inspection from Phase 6A in C#.

### 2. Provisioned account graph round-trips

Use `AccountProvisioningService.CreateVerified(...)` to create:

- `User`
- `Account`
- owner `AccountUser`
- `AccountEntitlements`

Persist in dependency-safe order:

1. `Users.Add(graph.User)`
2. `Accounts.Add(graph.Account)`
3. `AccountUsers.Add(graph.Owner)`
4. `AccountEntitlements.Add(graph.Entitlements)`
5. `SaveChangesAsync()`

Although the schema has circular FKs, this graph should save in one EF unit because
all IDs are domain-minted before persistence.

Reload through a fresh context and assert:

- account, owner, user, and entitlements exist;
- owner `UserId` and `AccountId` match;
- account `PrimaryOwnerAccountUserId == owner.Id`;
- entitlements `AccountId == account.Id`;
- key enum values reload as the expected domain enum values;
- audit timestamps are set to the fixture clock value.

### 3. Duplicate normalized email is rejected

After saving a valid graph, create a second pending invite in the same account
with the same normalized email but a different casing of the email address.

Expected result:

- `SaveChangesAsync()` throws `DbUpdateException`;
- inner Npgsql exception should identify SQLSTATE `23505` (`unique_violation`);
- constraint name should be `ix_account_users_account_email`.

This proves the filtered unique index that EF InMemory/SQLite would not prove.

### 4. One-to-one Account <-> AccountEntitlements is enforced

After saving a valid graph, create a second `AccountEntitlements` row for the same
`AccountId`.

Expected result:

- `SaveChangesAsync()` throws `DbUpdateException`;
- SQLSTATE `23505`;
- constraint/index name should be `ix_account_entitlements_account_id`.

### 5. Soft-deleted base rows are hidden, but entitlements remain visible

Persist a valid graph, then set:

- `graph.Account.DeletedAtUtc = now`
- `graph.Owner.DeletedAtUtc = now`
- `graph.User.DeletedAtUtc = now`
- `graph.Entitlements.DeletedAtUtc = now`

Save and reload in a fresh context.

Assert normal queries return:

- no account;
- no owner membership;
- no user;
- the entitlements row is still visible because `AccountEntitlements` is exempt
  from the global soft-delete query filter.

Also assert `IgnoreQueryFilters()` can see the soft-deleted account/user/member.

### 6. Computed AccountUser.IsActive reloads correctly

Persist an invited membership and a suspended membership if needed, or mutate an
owner with `Suspend()`.

Assert after fresh reload:

- active owner has `IsActive == true`;
- suspended membership has `IsActive == false`;
- there is no `is_active` column in the database. This can be asserted via
  `information_schema.columns` for `account_users`.

---

## Test data notes

- Use UTC `DateTime` values; the domain factories throw on non-UTC where relevant.
- Use unique emails per test if the database is not reset per test.
- Invite token hashes must be 64 characters for the mapping's max length; a repeated
  hex string is enough for these tests.
- For pending invites, `Owner` role is invalid by design; use `Admin`, `Operator`, or
  `Viewer`.
- For suspended membership proof, create an active member with `CreateOwner(...)`
  only if assigning ownership is not needed; otherwise use the existing owner and
  call `Suspend()` after saving the primary-owner graph.

---

## Verification command

Docker must be available locally.

```bash
dotnet test tests/OpHalo.IntegrationTests/OpHalo.IntegrationTests.csproj
dotnet test
```

If package restore or image pull fails in Codex because network is restricted, retry
with approval/escalation rather than changing the harness.

---

## Exit gate for implementation

- Integration tests compile and run against a real PostgreSQL container.
- Migration applies through `Database.MigrateAsync()`.
- Graph round-trip, duplicate normalized email, 1:1 entitlements, soft-delete filter,
  and computed `IsActive` proofs are green.
- Unit test baseline remains green: 176 unit tests.
- Architecture test baseline remains green: 14 architecture tests.
- Placeholder `SolutionSkeletonTests` may remain or be removed; if removed, keep the
  integration project discoverable through the new persistence tests.
