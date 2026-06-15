# Session Log — OpHalo Foundation

**Last updated:** 2026-06-15
**Next session tier:** Tier 1 — Discovery · _Phase 7 (next build plan phase)_
**Branch:** `main` (no remote yet) · Last commit pending

---

## Completed this session — Phase 6 Session B implementation

Phase 6 is fully complete. Six Testcontainers.PostgreSql integration tests prove the
`InitialFoundationSchema` migration and EF Core configuration against real PostgreSQL.

**Key discovery — ADR-044:** Persisting the provisioning graph requires a **two-phase save**
because the circular FK (Account↔AccountUser, ADR-019) prevents EF Core from topologically
ordering a single-unit insert. The `PersistGraph` test helper demonstrates the canonical
persistence contract: insert with `PrimaryOwnerAccountUserId = NULL`, then update. The
real persistence boundary (Phase 6 repository/unit-of-work) must encapsulate this sequence.

Files added / modified:

- `tests/OpHalo.IntegrationTests/OpHalo.IntegrationTests.csproj` — `Testcontainers.PostgreSql`
  4.12.0 + project refs to SharedKernel, Foundation.Core, Foundation.Application,
  Foundation.Infrastructure.
- `tests/OpHalo.IntegrationTests/Persistence/PostgresFixture.cs` — container lifecycle,
  `CreateContext()`, `FakeClock`, `FixedNow`.
- `tests/OpHalo.IntegrationTests/Persistence/PersistenceProofTests.cs` — 6 proof tests.
- `docs/build-log/010-phase-6b-persistence-proof-implementation.md`
- `docs/decisions/decision-index.md` — ADR-044 added; next free ID **ADR-045**.

**Test baseline (all green):** UnitTests 176, ArchitectureTests 14, IntegrationTests 7.

---

## Previous session — Phase 6 Session B discovery

Decision: **use Testcontainers.PostgreSql** for persistence proof tests (ADR-043). This is the
only honest harness for the risks in this slice: PostgreSQL partial unique indexes, FK enforcement
around the Account↔AccountUser cycle, Npgsql duplicate-key exceptions, snake_case DDL, and applying
the migration for real. EF InMemory/SQLite are explicitly rejected for Session B proof value.

Docs added: `docs/build-log/009`, ADR-043 in decision-index.

---

## Previous session — Phase 6 Session A

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
- `Migrations/…_InitialFoundationSchema.cs` — generated + inspected.

Three divergences confirmed: `AccountUser.IsActive` ignored (no column, ADR-023); primary ownership =
nullable FK `Account.PrimaryOwnerAccountUserId → AccountUser` (`Restrict`, ADR-019); history table
`__OpHaloMigrationsHistory`. Docs: build-log/008, ADR-041/042.

---

## Optional operator step (Christian)

The migration is generated but **not applied to a long-lived live DB**. To apply:

```
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..." --project src/OpHalo.Foundation.Infrastructure
dotnet ef database update --project src/OpHalo.Foundation.Infrastructure --context OpHaloDbContext
```

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
| 6 — Persistence · Session A (infra) | ✅ done | `68354dc`, build-log/008, ADR-041/042 |
| 6 — Persistence · Session B (proof tests) | ✅ done | this session, build-log/009/010, ADR-043/044 |
| 7 — next build plan phase | ⏭ next | |

**Test baseline (must stay green):** UnitTests 176, ArchitectureTests 14, IntegrationTests 7.

---

## Watch-outs / debt carried forward

- **Two-phase provisioning save (ADR-044)** — the Phase 6 repository/unit-of-work must
  encapsulate the null-then-update sequence for `PrimaryOwnerAccountUserId`. No handler
  should ever manage it manually.
- **Circular FK** (Account↔AccountUser) — verified to order cleanly in the migration and at
  runtime via the two-phase save. Re-check if the schema changes.
- **`AccountUser.IsActive` is `Ignore`d** — computed (ADR-023); never reintroduce as a column.
- **Migration not yet applied to a long-lived live DB** — Session B proved it via disposable
  container; Christian may run the operator command against a local/manual DB.
- **DI registration + `appsettings` connection string deferred to Phase 5** — design-time
  factory reads its own config; no caller ⇒ no premature DI.
- **Schema-drop reset pattern** — `EnsureDeletedAsync` is broken under Npgsql connection pooling;
  the `DROP SCHEMA public CASCADE` + `CREATE SCHEMA public` + `MigrateAsync` sequence is the
  correct per-test reset for this harness.
- Never glob through `_reference/**/bin` (recursive nesting). Read specific source paths.
- Legacy `decision-index`/`decisions/**`/`coding-rules` remain **pending validation** — do not load.
- No GitHub remote yet. When added, repo must be named `ophalo-foundation`.
