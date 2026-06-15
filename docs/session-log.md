# Session Log — OpHalo Foundation

**Last updated:** 2026-06-15
**Next session tier:** Tier 1 — Discovery · _Phase 7B — Keep intake/operator-list application + API vertical_
**Branch:** `main` (no remote yet)

---

## Current state — Phase 7A complete ✅

Keep domain + persistence foundation is built, migrated, and proven against real
PostgreSQL. All four suites green. Build-log/012 written; ADR-047…050 added to the
decision index (next free ID: **ADR-051**).

### Test baselines (verified this session)

| Suite | Result |
|-------|--------|
| Build (`OpHalo.slnx`) | succeeded, 0 warnings / 0 errors |
| UnitTests | **230** passed |
| ArchitectureTests | **14** passed |
| IntegrationTests | **15** passed (7 Foundation + 8 Keep) |

### What landed

- Keep.Core: `KeepCustomer`, `KeepRequest`, `KeepRequestEvent`, `KeepPublicIntakeLink`, three enums, two error classes.
- Keep.Application: `KeepTokenService` (page token, public intake token, SHA-256 hash, reference code).
- Foundation.Infrastructure: `BaseEntityConfiguration<T>` made `public` (ADR-047); `OpHaloDbContext` optional `additionalModelAssemblies` (ADR-048).
- Keep.Infrastructure: four EF configs, `KeepDesignTimeDbContextFactory` (ADR-049), `EFCore.Design` package.
- Migration `20260615105355_KeepDomain` in Foundation.Infrastructure (single migration assembly, ADR-005/049).
- Unit tests (40 Keep) + 8 Keep integration proof tests.

---

## Next session — Phase 7B (discovery first)

Application/API vertical for "customer submits intake → operator sees request".
This is a **Tier 1 discovery** session: confirm scope before generating code.
Open questions to resolve with Christian (from build-log/011):

1. Public route names: only `/keep/...`, or also legacy `/continuity/...` aliases as cutover contracts?
2. Is the `publicSlug` segment required in the first backend route, or ship token-only first?
3. Does customer email-on-intake move into 7B or stay deferred to Phase 9 notifications?
4. Does the operator "sign in" exit-gate require real Phase 5 auth before 7B, or can 7B use `ICurrentUser` with integration-level fakes?

Likely surface (confirm + split if needed under the Session Size Rule):
- Public intake endpoint + create-request service (resolves account via `KeepPublicIntakeLink`, no client `AccountId`).
- Foundation access-policy + feature-key + permission-key + public-guard + rate-limit checks.
- Operator request-list endpoint (minimal account-scoped list).
- HTTP-level tests.

DI registration + host DbContext wiring + `appsettings` connection string will
likely first be needed here (deferred since Phase 6).

---

## Architecture decisions made this session

- **ADR-047** — `BaseEntityConfiguration<T>` `internal` → `public` (visibility only).
- **ADR-048** — `OpHaloDbContext` optional `additionalModelAssemblies`; Keep registers configs without Foundation referencing Keep.
- **ADR-049** — single migration assembly (Foundation.Infrastructure); `KeepDesignTimeDbContextFactory` is the migration entrypoint for Keep tables.
- **ADR-050** — `KeepRequestStatus.Cancelled = 6`; reason lives on the event, not the status enum.

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
| Account-creation orchestration | ✅ done | `e09d876`, build-log/007, ADR-039/040 |
| 6 — Persistence · Session A (infra) | ✅ done | `68354dc`, build-log/008, ADR-041/042 |
| 6 — Persistence · Session B (proof tests) | ✅ done | build-log/009/010, ADR-043/044 |
| 7 — Keep intake to operator view discovery | ✅ done | build-log/011, ADR-045/046 |
| 7A — Keep domain + persistence foundation | ✅ done | build-log/012, ADR-047…050 |
| 7B — Keep intake/operator-list application + API | ⏭️ next | discovery |

---

## Watch-outs / debt carried forward

- **Phase 7B route compatibility** — public route names may be external contracts; decide `/keep/...` vs legacy `/continuity/...` before shipping.
- **DI registration + `appsettings` connection string** — still deferred; 7B is the likely first runtime caller needing host DbContext wiring.
- **Two-phase provisioning save (ADR-044)** — future persistence boundary must encapsulate the null-then-update `PrimaryOwnerAccountUserId` sequence.
- **Keep public intake token (ADR-046)** — store hashed token in the Keep-owned satellite; never revive `Account.PublicIntakeToken` or trust client `AccountId`.
- **`AccountUser.IsActive` is `Ignore`d** — computed (ADR-023); never reintroduce as a column. Same for Keep `IsTerminal`/`IsActive`.
- **EF model caching** — every integration context must build the same Foundation+Keep model (`PostgresFixture` passes the Keep assembly unconditionally). Don't revert.
- **Schema-drop reset pattern** — `DROP SCHEMA public CASCADE` + `CREATE SCHEMA public` + `MigrateAsync`; `EnsureDeletedAsync` is unreliable with Npgsql pooling.
- **Migration generation** — always `--startup-project src/OpHalo.Keep.Infrastructure` when Keep tables are involved; a dummy `ConnectionStrings__DefaultConnection` is enough for `migrations add` (no DB touched).
- Never glob through `_reference/**/bin` or legacy `obj` trees.
- Legacy `decision-index`/`decisions/**`/`coding-rules` remain **pending validation** — do not load.
- No GitHub remote yet. When added, repo must be named `ophalo-foundation`.
