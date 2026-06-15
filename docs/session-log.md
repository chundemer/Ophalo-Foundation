# Session Log — OpHalo Foundation

**Last updated:** 2026-06-14
**Next session tier:** Tier 2 — Implementation · **Pre-work complete** · _Phase 6 **Session A** (persistence infra)_
**Branch:** `main` (no remote yet) · Last commit `e09d876` (account-creation orchestration)

> **Phase 6 — Persistence** is confirmed and **split into two sessions** (Session Size Rule):
> **Session A = persistence infrastructure** (DbContext + configs + design-time factory + migration),
> **Session B = Testcontainers harness + persistence tests**. The morning session is **Session A**.
> No code was written this session — this is a confirmed pre-work handoff.

---

## Next session = Phase 6 Session A — persistence infrastructure

**Target layer:** `OpHalo.Foundation.Infrastructure` only (+ the migration Christian runs). One layer.

**Confirmed implementation target (build this, in order):**

1. **EF packages on `OpHalo.Foundation.Infrastructure.csproj`** (match legacy versions):
   - `Microsoft.EntityFrameworkCore` 10.0.5, `Microsoft.EntityFrameworkCore.Relational` 10.0.5,
     `Microsoft.EntityFrameworkCore.Design` 10.0.5, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.1,
     `EFCore.NamingConventions` 10.0.1.
2. **`OpHaloDbContext`** (new, in Infrastructure) — `DbSet`s for `Account`, `AccountUser`, `User`,
   `AccountEntitlements`. `OnModelCreating` → `ApplyConfigurationsFromAssembly`. Constructor takes
   `IClock` (SharedKernel). Port two behaviors verbatim from legacy `ApplicationDbContext`:
   - **`SaveChangesAsync` timestamp interception**: Added ⇒ set `CreatedAtUtc` + `UpdatedAtUtc = clock.UtcNow`;
     Modified ⇒ set `UpdatedAtUtc`. (Drop the legacy `SystemRecord` / non-BaseEntity branches — not present here.)
   - **Soft-delete global query filters** over every `BaseEntity`, **except `AccountEntitlements`**
     (foundational satellite — always visible, required invariant, not deletable data).
3. **`BaseEntityConfiguration<TEntity>`** (abstract) + 4 entity configs, mirroring legacy
   (`_reference/src/OpHalo.Infrastructure/Persistence/Core/Configurations/`), **adapted** per the
   divergences below. Key = `ValueGeneratedNever` (domain mints v7 GUIDs). Enums `HasConversion<string>().HasMaxLength(50)`.
4. **`OpHaloDbContextFactory`** (`IDesignTimeDbContextFactory<OpHaloDbContext>`) — port the legacy factory,
   but: history table `__OpHaloMigrationsHistory`, `MigrationsAssembly` = Infrastructure,
   `.UseSnakeCaseNamingConvention()`, connection string `DefaultConnection` from config/env/user-secrets
   scoped to Infrastructure. Provide a `SystemClock` (`IClock`) — port the trivial legacy impl into Infrastructure.

**Then Christian runs** (do not run these — interactive/migration commands):
```
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..." --project src/OpHalo.Foundation.Infrastructure
dotnet ef migrations add InitialFoundationSchema --project src/OpHalo.Foundation.Infrastructure --context OpHaloDbContext
dotnet ef database update --project src/OpHalo.Foundation.Infrastructure --context OpHaloDbContext
```

**Session A exit gate:** build clean (0 warnings); architecture tests stay green (14); fresh DB migrates
cleanly (Christian verifies). Persistence *proof tests* are Session B.

## Three deliberate divergences from the legacy config (greenfield + our ADRs)

1. **`AccountUser.IsActive` is `Ignore`d, NOT mapped.** It is now a computed projection of
   `MembershipStatus == Active` (ADR-023). Legacy stored it as a column — we must call
   `builder.Ignore(x => x.IsActive)`.
2. **Primary ownership is an FK, not a flag (ADR-019).** Map `Account.PrimaryOwnerAccountUserId`
   (nullable `Guid?`) as an FK → `AccountUser`, `OnDelete.Restrict`, **no required navigation**.
   This *replaces* legacy's `is_primary_owner` column + `ix_account_users_one_primary_owner` filtered
   unique index (both gone). Note the **circular FK**: `Account.PrimaryOwnerAccountUserId → AccountUser`
   and `AccountUser.AccountId → Account`. Nullable owner FK + `Restrict` lets the migration order cleanly.
3. **Migrations history table `__OpHaloMigrationsHistory`** (not legacy `__ApplicationMigrationsHistory`).
   Trimmed/dropped legacy columns (account quotas/credits, halo booleans, Stripe/billing fields, public
   slug + intake token, `Account.Email`, push-subscription/welcome/install-prompt on AccountUser,
   `ProvisioningKey`, `Pilot*` dates, delinquency fields) simply aren't present — the entities were
   already trimmed in Phase 4a/4b; do **not** reintroduce them.

## Indexes / relationships to carry (adapted)

- `User.Email` — unique, filtered `deleted_at_utc IS NULL`.
- `AccountUser (AccountId, NormalizedEmail)` — unique, filtered `deleted_at_utc IS NULL` (the dedup index).
- `AccountUser.InviteTokenHash` — unique, filtered `invite_token_hash IS NOT NULL`.
- `AccountUser (AccountId, UserId)` — unique, filtered `user_id IS NOT NULL AND deleted_at_utc IS NULL`.
- `AccountUser.AccountId` plain index; `Account.HasMany(Users).WithOne().HasForeignKey(AccountId)` `Restrict`.
- `AccountUser.User` optional FK (`IsRequired(false)`, `Restrict`).
- `AccountEntitlements` — 1:1 with `Account`: unique `AccountId`, `HasOne(Account).WithOne().HasForeignKey<AccountEntitlements>(AccountId)` `Restrict`.
- Reference source to mirror: `_reference/.../Configurations/{BaseEntity,User,Account,AccountUser,AccountEntitlements}Configuration.cs`
  and `ApplicationDbContext.cs` (SaveChanges + soft-delete filter helpers).

## Decisions to log when built (next free ID **ADR-041**)

- **ADR-041 (Persistence-001):** `OpHaloDbContext` in Infrastructure; snake_case naming; ported
  `BaseEntityConfiguration` + `SaveChangesAsync` timestamp interception via `IClock`; soft-delete global
  query filters; `AccountEntitlements` exempt as a foundational satellite.
- **ADR-042 (Persistence-002):** the three divergences — `IsActive` ignored (ADR-023 consequence);
  primary-owner FK replaces the legacy `is_primary_owner` flag/index (ADR-019), nullable + `Restrict` to
  resolve the circular FK; history table `__OpHaloMigrationsHistory`.
  _(Fold into one ADR entry if a single line suffices; expand to full ADR files only if rationale needs it.)_

## Session B (the session after Session A) — persistence proof tests

- **Test harness — DECISION PENDING, confirm at Session B start.** Recommended: **Testcontainers.PostgreSql**
  (real Postgres; the only honest way to verify partial/filtered unique indexes, the circular FK, snake_case
  DDL, and duplicate-email rejection). Alternative (lighter, weaker): EF InMemory/SQLite — but it ignores
  filtered indexes so it can't prove the dedup behavior. Needs Docker available.
- Tests prove: account + owner-membership + entitlements graph round-trips (the
  `AccountProvisioningService` output saves & reloads); duplicate normalized-email rejected;
  1:1 Account↔Entitlements enforced; soft-deleted rows hidden by the query filter while `AccountEntitlements`
  stays visible; computed `IsActive` reloads correctly.

## Scope intentionally deferred (decided with Christian)

- **DI registration into the `OpHalo.Api` host + `appsettings` connection string** → deferred to **Phase 5
  (auth)**, when the first endpoint actually consumes the DbContext. The design-time factory reads its own
  config, so migrations don't need host wiring. No caller ⇒ no premature DI.
- **Auth-flow portion of the Phase 6 exit gate** ("Foundation auth flow works against the DB") → can't be met
  until Phase 5 exists; persistence was deliberately built first. Session A meets the *migrates-cleanly* gate.
- Repositories / `IUnitOfWork` / generic persistence abstractions → not introduced; no caller yet (ADR-039).

## Where we are

| Phase | Status | Notes |
|-------|--------|-------|
| 1 — Skeleton + architecture tests | ✅ done | `00227b0` |
| 2 — Legacy exclusion (doc) | ✅ done | `2fce382` |
| 3 — SharedKernel + abstraction cleanup | ✅ done | `2fce382` |
| 4a — Account/User/AccountUser + lifecycle + access policy | ✅ done | `ec4c35c`, build-log/003 |
| 4b — AccountEntitlements (commercial posture producer) | ✅ done | `7cf49aa`, build-log/004 |
| 4c — Permission keys + role access policy (User permitted) | ✅ done | `034eee4`, build-log/005 |
| 4d — Feature keys / entitlements (Account entitled) | ✅ done | `eef4b07`, build-log/006 |
| Account-creation orchestration (first composing caller) | ✅ done | `e09d876`, build-log/007, ADR-039/040 |
| 6 — Persistence · **Session A (infra)** | ⏭ next | this handoff |
| 6 — Persistence · Session B (proof tests) | ⬜ after A | Testcontainers (pending confirm) |

**Test baseline (must stay green):** UnitTests 176, ArchitectureTests 14, IntegrationTests 1.

## Watch-outs / debt carried forward

- **Circular FK** (Account↔AccountUser) — keep `PrimaryOwnerAccountUserId` nullable + `Restrict`; verify the
  generated migration orders table creation without a deferred-constraint error.
- **`AccountUser.IsActive` must be `Ignore`d** — easy to map by reflex; it is computed (ADR-023).
- Never glob through `_reference/**/bin` (recursive nesting). Read specific source paths.
- Legacy `decision-index`/`decisions/**`/`coding-rules` remain **pending validation** — do not load.
- No GitHub remote yet. When added, repo must be named `ophalo-foundation`.
