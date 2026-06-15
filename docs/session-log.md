# Session Log — OpHalo Foundation

**Last updated:** 2026-06-15
**Next session tier:** Tier 1 — Discovery · _Phase 6 **Session B** (persistence proof tests)_
**Branch:** `main` (no remote yet) · Last commit pending — Phase 6 Session A (persistence infra)

> **Phase 6 Session A (persistence infrastructure) is COMPLETE.** DbContext, configs,
> design-time factory, and the `InitialFoundationSchema` migration are built and inspected;
> build clean (0 warnings), architecture tests green (14). One operator step remains before
> Session B: **`dotnet ef database update` against a real DB** (Christian).
>
> Session B (persistence proof tests, **Testcontainers**) is **Tier 1** — the test-harness
> decision is still open and must be confirmed at the start.

---

## Completed this session — Phase 6 Session A

Layer: `OpHalo.Foundation.Infrastructure` only. Files added:

- `OpHalo.Foundation.Infrastructure.csproj` — EF packages (EFCore 10.0.5 + Relational + Design,
  Npgsql 10.0.1, NamingConventions 10.0.1), direct SharedKernel ref, four
  `Microsoft.Extensions.Configuration.*` (10.0.0) packages for the design-time factory.
- `Persistence/OpHaloDbContext.cs` — 4 DbSets; `ApplyConfigurationsFromAssembly`; ctor `(options, IClock)`;
  ported `SaveChangesAsync` timestamp interception + soft-delete global query filters (exempting
  `AccountEntitlements`). Legacy `SystemRecord`/non-`BaseEntity` branches dropped.
- `Persistence/Configurations/` — `BaseEntityConfiguration` + Account/AccountUser/User/AccountEntitlements.
- `Persistence/OpHaloDbContextFactory.cs` — design-time factory; `__OpHaloMigrationsHistory`; snake_case.
- `Services/SystemClock.cs` — wall-clock `IClock`.
- `Migrations/…_InitialFoundationSchema.cs` — generated + inspected (see below).

**Three divergences confirmed in the migration:** `AccountUser.IsActive` ignored (no column,
ADR-023); primary ownership = nullable FK `Account.PrimaryOwnerAccountUserId → AccountUser`
(`Restrict`, replaces legacy `is_primary_owner` flag/index, ADR-019); history table
`__OpHaloMigrationsHistory`.

**Migration inspection passed:** circular FK orders cleanly (account_users → accounts-with-owner-FK
→ separate `AddForeignKey` for account_users.account_id); all four filtered/partial unique indexes
present with exact predicates; enums `varchar(50)`; PKs plain `uuid` (no DB default). Docs:
build-log/008, ADR-041/042 (decision index updated, next free ID **ADR-043**).

---

## Operator step before Session B (Christian)

The migration is generated but **not applied**. To close the *migrates-cleanly* part of the gate:

```
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..." --project src/OpHalo.Foundation.Infrastructure
dotnet ef database update --project src/OpHalo.Foundation.Infrastructure --context OpHaloDbContext
```

(`migrations add` was already run with a throwaway connection string — it writes C# only, no DB.)

---

## Next session = Phase 6 Session B — persistence proof tests (Tier 1)

**Open decision to confirm first:** test harness. **Recommended: Testcontainers.PostgreSql** (real
Postgres — the only honest way to verify the partial/filtered unique indexes, the circular FK,
snake_case DDL, and duplicate-email rejection). Alternative (lighter, weaker): EF InMemory/SQLite —
ignores filtered indexes, so it cannot prove the dedup behavior. Needs Docker available.

**Tests to prove:**
- The `AccountProvisioningService` output graph (account + owner membership + entitlements)
  round-trips: saves and reloads.
- Duplicate normalized-email rejected (the `ix_account_users_account_email` filtered unique).
- 1:1 Account↔Entitlements enforced.
- Soft-deleted rows hidden by the query filter, while `AccountEntitlements` stays visible.
- Computed `IsActive` reloads correctly (ignored column, derived on read).

**Likely scope:** new `tests/OpHalo.IntegrationTests` fixtures (Testcontainers) + a small DI/options
seam to construct `OpHaloDbContext` against the container connection string. Confirm before writing.

---

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
| 6 — Persistence · **Session A (infra)** | ✅ done | this session, build-log/008, ADR-041/042 |
| 6 — Persistence · Session B (proof tests) | ⏭ next | Testcontainers (confirm harness at start) |

**Test baseline (must stay green):** UnitTests 176, ArchitectureTests 14, IntegrationTests 1.

## Watch-outs / debt carried forward

- **Circular FK** (Account↔AccountUser) — verified to order cleanly in the migration. Re-check if
  the schema changes; keep `PrimaryOwnerAccountUserId` nullable + `Restrict`.
- **`AccountUser.IsActive` is `Ignore`d** — computed (ADR-023); never reintroduce as a column.
- **Migration not yet applied to a live DB** — schema proven by inspection only until `database update`.
- **DI registration + `appsettings` connection string deferred to Phase 5** — the design-time
  factory reads its own config, so migrations don't need host wiring. No caller ⇒ no premature DI.
- Never glob through `_reference/**/bin` (recursive nesting). Read specific source paths.
- Legacy `decision-index`/`decisions/**`/`coding-rules` remain **pending validation** — do not load.
- No GitHub remote yet. When added, repo must be named `ophalo-foundation`.
