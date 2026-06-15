# Build Log 008 — Phase 6 Session A: persistence infrastructure

**Date:** 2026-06-15
**Phase:** 6 — Persistence · **Session A** (DbContext + configs + design-time factory + migration)
**Reference repo:** `/Users/christian/application/ophalo` (read-only; aliased at `_reference/`)
**Target repo:** `/Users/christian/saas/ophalo-foundation`

---

## Purpose

Phase 4 settled the Foundation domain graph (`Account`, `AccountUser`, `User`,
`AccountEntitlements` + factories + access/authorization/entitlement policies) and the
account-creation orchestration (build-log/007) assembled it in memory — but **nothing
persisted it**. This slice adds the persistence *infrastructure*: the EF Core mapping, the
DbContext with its two ported behaviors, the design-time factory, and the initial migration.

Scope is **one layer** — `OpHalo.Foundation.Infrastructure` — plus the generated migration.
Persistence *proof tests* (Testcontainers) are **Session B**, deliberately split out under the
Session Size Rule.

---

## What was built

**`OpHalo.Foundation.Infrastructure.csproj`**
- EF packages at legacy-matched versions: `Microsoft.EntityFrameworkCore` /
  `.Relational` / `.Design` **10.0.5**, `Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.1**,
  `EFCore.NamingConventions` **10.0.1**.
- Direct `OpHalo.SharedKernel` project reference (the DbContext ctor consumes `IClock`).
- Added the four `Microsoft.Extensions.Configuration.*` packages (10.0.0) the design-time
  factory needs (`ConfigurationBuilder` + Json/UserSecrets/EnvironmentVariables) — the legacy
  app got these transitively; here they are explicit.

**`Persistence/OpHaloDbContext.cs`** (new) — DbSets for the four entities;
`ApplyConfigurationsFromAssembly`; ctor `(DbContextOptions, IClock)`. Two behaviors ported
**verbatim** from legacy `ApplicationDbContext`:
- `SaveChangesAsync` timestamp interception over `BaseEntity` (Added ⇒ `CreatedAtUtc` +
  `UpdatedAtUtc = clock.UtcNow`; Modified ⇒ `UpdatedAtUtc`). The legacy `SystemRecord` and
  non-`BaseEntity` Keep branches are **dropped** — neither exists here.
- Soft-delete global query filters over every `BaseEntity`, **except `AccountEntitlements`**
  (foundational satellite, ADR-025 — always visible, a required invariant, not deletable data).

**`Persistence/Configurations/`** (new) — `BaseEntityConfiguration<TEntity>` (key
`ValueGeneratedNever`, audit columns) + `Account`, `AccountUser`, `User`,
`AccountEntitlements` configs, mirroring the legacy configs and **adapted** per the divergences
below. Enums `HasConversion<string>().HasMaxLength(50)`.

**`Persistence/OpHaloDbContextFactory.cs`** (new) — `IDesignTimeDbContextFactory`. Reads its
own config (user-secrets scoped to Infrastructure → env → appsettings) and throws if
`DefaultConnection` is missing. `UseNpgsql` with history table `__OpHaloMigrationsHistory` +
`MigrationsAssembly` = Infrastructure; `.UseSnakeCaseNamingConvention()`.

**`Services/SystemClock.cs`** (new) — wall-clock `IClock`, ported and collapsed onto the single
SharedKernel `IClock` (the legacy dual AppClock/SharedClock impl is no longer needed, Phase 3).

**`Migrations/…_InitialFoundationSchema.cs`** (generated) — the initial schema.

---

## Three deliberate divergences from the legacy config

1. **`AccountUser.IsActive` is `builder.Ignore`d, not mapped.** It is now a computed projection
   of `MembershipStatus == Active` (ADR-023). Confirmed: no `is_active` column in the migration.
2. **Primary ownership is an FK, not a flag (ADR-019).** `Account.PrimaryOwnerAccountUserId`
   (nullable `Guid?`) maps as an FK → `AccountUser`, `OnDelete.Restrict`, **no navigation**. This
   replaces the legacy `is_primary_owner` column + `ix_account_users_one_primary_owner` filtered
   unique index (both gone). Nullable owner FK + `Restrict` resolves the **circular FK**
   (`Account↔AccountUser`).
3. **Migrations history `__OpHaloMigrationsHistory`** (not legacy `__ApplicationMigrationsHistory`).

Trimmed legacy columns (quotas/credits, halo booleans, Stripe/billing/delinquency, public slug +
intake token, `Account.Email`, push/welcome/install-prompt, `ProvisioningKey`, pilot dates) are
**absent** — the entities were trimmed in Phase 4a/4b; not reintroduced.

---

## Migration inspection (the Session-A exit gate)

`InitialFoundationSchema` was generated (`dotnet ef migrations add`, throwaway connection
string — `migrations add` writes C# only, touches no DB) and inspected:

- **Circular FK orders cleanly.** EF creates `users` → `account_entitlements` → `account_users`
  (with its `user_id → users` FK inline) → `accounts` (with `primary_owner_account_user_id →
  account_users` FK inline), then adds `account_users.account_id → accounts` via a **separate
  `AddForeignKey`** after both tables exist. No deferred-constraint error.
- **Filtered/partial indexes** all present with exact predicates:
  - `ix_users_email` unique, `deleted_at_utc IS NULL`
  - `ix_account_users_account_email` (`account_id`,`normalized_email`) unique, `deleted_at_utc IS NULL`
  - `ix_account_users_account_user` (`account_id`,`user_id`) unique, `user_id IS NOT NULL AND deleted_at_utc IS NULL`
  - `ix_account_users_invite_token_hash` unique, `invite_token_hash IS NOT NULL`
  - `ix_account_entitlements_account_id` unique (the 1:1)
  - `ix_account_users_account_id` plain
- **Enums** all `character varying(50)` (plan, commercial_state, operating_mode, membership_status,
  role, purpose, lifecycle_state). **PKs** plain `uuid`, no DB default — domain mints v7.
- snake_case throughout. (EF also auto-adds plain `ix_account_users_user_id` to back the `user_id`
  FK — expected, harmless alongside the filtered unique.)

---

## Preserved / Adapted

**Preserved (ported verbatim):** `BaseEntityConfiguration`, the `SaveChangesAsync` timestamp
interception, the soft-delete query-filter helpers + the satellite exemption, the design-time
factory shape, `SystemClock`.

**Adapted:** the three divergences above; DbContext renamed `ApplicationDbContext` →
`OpHaloDbContext` and stripped of all Keep/Platform/Continuity DbSets and interfaces;
`AccountEntitlements` 1:1 mapped via `HasOne<Account>().WithOne()` (no navigation on the satellite
side, since the trimmed entity holds only `AccountId`); the Account↔AccountUser relationship
defined once (on the `AccountUser` side, which carries both navigations).

---

## Tests / build

- Build: **0 warnings / 0 errors** (Infrastructure and full solution).
- **Architecture tests: 14/14 green** — Infrastructure referencing SharedKernel + the EF packages
  introduced no boundary violation (Application stayed free of Infrastructure; §8 intact).
- Unit 176 / Integration 1 baseline untouched (no test code in this slice).
- No persistence *proof* tests yet — that is Session B (Testcontainers).

---

## Exit gate (Session A)

- ✅ EF packages added at legacy-matched versions.
- ✅ `OpHaloDbContext` + `BaseEntityConfiguration` + 4 entity configs + design-time factory built.
- ✅ Build clean (0 warnings); architecture tests green (14).
- ✅ `InitialFoundationSchema` migration generated and **inspected** — circular FK orders cleanly,
  divergences confirmed, filtered indexes correct.
- ⏳ **`dotnet ef database update` against a live DB is the operator step** (Christian) — the
  *migrates-cleanly* confirmation closes the last part of the gate.

---

## Deferred / follow-ups

- **DI registration + `appsettings` connection string → Phase 5 (auth)**, when the first endpoint
  consumes the DbContext. The design-time factory reads its own config, so migrations need no host
  wiring. No caller ⇒ no premature DI (ADR-039 spirit).
- **Session B — persistence proof tests** (Testcontainers.PostgreSql, pending confirm): account +
  owner + entitlements graph round-trips; duplicate normalized-email rejected; 1:1
  Account↔Entitlements enforced; soft-deleted rows hidden while `AccountEntitlements` stays
  visible; computed `IsActive` reloads correctly.
- **Repositories / `IUnitOfWork`** still not introduced — no caller yet (ADR-039).
- **`database update` not yet run** — schema is proven only by inspection until the operator
  applies it to a fresh DB.
