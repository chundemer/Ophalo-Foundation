# Build Log 010 — Phase 6 Session B: PostgreSQL persistence proof implementation

**Date:** 2026-06-15
**Phase:** 6 — Persistence · Session B implementation
**Target repo:** `/Users/christian/saas/ophalo-foundation`

---

## What was built

Six Testcontainers.PostgreSql integration tests that prove the `InitialFoundationSchema`
migration and EF Core configuration against real PostgreSQL behavior. No domain changes.
No new abstractions. Tests compose the existing Phase 4–6A artifacts without repositories
or DI.

---

## Files added / modified

| File | Change |
|------|--------|
| `tests/OpHalo.IntegrationTests/OpHalo.IntegrationTests.csproj` | Added `Testcontainers.PostgreSql` 4.12.0; added project refs to SharedKernel, Foundation.Core, Foundation.Application, Foundation.Infrastructure |
| `tests/OpHalo.IntegrationTests/Persistence/PostgresFixture.cs` | New — container lifecycle (`IAsyncLifetime`), `CreateContext()`, `FakeClock`, `FixedNow` constant |
| `tests/OpHalo.IntegrationTests/Persistence/PersistenceProofTests.cs` | New — 6 proof tests + `PersistGraph` helper |

---

## Fixture design

`PostgresFixture` starts a `PostgreSqlContainer` (image `postgres:17.5-alpine`) once per
test class via `IClassFixture<PostgresFixture>`. Per-test isolation is achieved in
`PersistenceProofTests.InitializeAsync()` by dropping and recreating the `public` schema,
then running `MigrateAsync()` fresh.

`EnsureDeletedAsync` was considered but rejected: PostgreSQL refuses `DROP DATABASE` when
Npgsql's connection pool holds open connections. Dropping the schema avoids closing the
database-level connections while still wiping all tables and the migration history table.

`OpHaloDbContext` is constructed directly with:
```
UseNpgsql(connectionString, npgsql => {
    MigrationsHistoryTable("__OpHaloMigrationsHistory")
    MigrationsAssembly(typeof(OpHaloDbContext).Assembly.FullName)
})
.UseSnakeCaseNamingConvention()
```

A `FakeClock` (fixed at `2025-01-15T12:00:00Z`) makes audit timestamp assertions stable.

---

## Two-phase persistence — key discovery (ADR-044)

The `PersistGraph` helper revealed that the provisioning graph **cannot be saved in a
single `SaveChangesAsync`** call. EF Core's topological sort detects the circular FK:

```
Account [Added] ← account_id ← AccountUser [Added] ← primary_owner_account_user_id ← Account
```

The fix is two-phase:
1. Add all four entities; null out `PrimaryOwnerAccountUserId` via EF's property entry API;
   `SaveChangesAsync` — inserts Account with `primary_owner_account_user_id = NULL`.
2. Restore `PrimaryOwnerAccountUserId`; `SaveChangesAsync` — updates the row.

This is the **canonical persistence contract** for the provisioning graph, not a test
workaround. The real persistence boundary (Phase 6 repository/unit-of-work) must
encapsulate this sequence. Application code must never manage it manually.

The migration already handles the circular FK correctly at DDL level (creates
`account_users`, then `accounts` with its nullable owner FK, then adds
`account_users.account_id → accounts` as a separate `AddForeignKey` step) — the two-phase
save is the runtime analogue of that migration ordering.

---

## Proof matrix — results

| Test | Proof | Result |
|------|-------|--------|
| `Migration_applies_to_real_PostgreSQL` | `GetAppliedMigrationsAsync` contains `*_InitialFoundationSchema` | ✅ |
| `Provisioned_account_graph_round_trips` | Full graph saves and reloads; FK links, enums, and audit timestamps correct | ✅ |
| `Duplicate_normalized_email_is_rejected` | `DbUpdateException` → `PostgresException` SQLSTATE `23505`, constraint `ix_account_users_account_email` | ✅ |
| `Second_entitlements_row_is_rejected` | `DbUpdateException` → `PostgresException` SQLSTATE `23505`, constraint `ix_account_entitlements_account_id` | ✅ |
| `Soft_deleted_rows_hidden_but_entitlements_remain_visible` | Account/User/AccountUser hidden by query filter; AccountEntitlements visible despite `DeletedAtUtc` set; `IgnoreQueryFilters()` recovers soft-deleted rows | ✅ |
| `Computed_IsActive_reloads_correctly_and_no_column_exists` | Active owner → `IsActive = true`; after `Suspend()` → `IsActive = false`; `information_schema.columns` confirms no `is_active` column | ✅ |

---

## Test baseline after this session

| Suite | Count | Status |
|-------|-------|--------|
| OpHalo.UnitTests | 176 | ✅ |
| OpHalo.ArchitectureTests | 14 | ✅ |
| OpHalo.IntegrationTests | 7 | ✅ (1 skeleton + 6 proof) |

---

## Exit gate — satisfied

- Integration tests compile and run against real PostgreSQL via Testcontainers.
- Migration applies through `Database.MigrateAsync()`.
- All 6 proof tests green.
- Unit test baseline: 176 ✅. Architecture test baseline: 14 ✅.

---

## Risks / debt carried forward

- **Two-phase provisioning save** must be encapsulated in the Phase 6 repository/unit-of-work.
  No application handler should ever manually null and restore `PrimaryOwnerAccountUserId`.
- The `PostgresFixture` uses `new PostgreSqlBuilder("postgres:17.5-alpine")` — the
  parameterless constructor was obsoleted in Testcontainers 4.12.0.
- `EnsureDeletedAsync` will remain broken in this harness (Npgsql pool / open-database
  constraint); the schema-drop reset is the correct approach going forward.
