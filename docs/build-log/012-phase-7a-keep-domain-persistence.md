# Build Log 012 - Phase 7A: Keep domain + persistence foundation

**Date:** 2026-06-15
**Phase:** 7A - Keep domain + persistence foundation (intake → operator list schema)
**Target repo:** `/Users/christian/saas/ophalo-foundation`
**Reference repo:** `/Users/christian/application/ophalo` (read-only)

---

## Purpose

Phase 7A builds and proves the Keep persistence foundation so the Phase 7B
application/API vertical can depend on a verified schema and domain contract. No
endpoints, auth, or DI wiring in this slice (ADR-045).

Scope delivered: Keep Core entities/enums/errors, a Keep token/reference service,
Keep EF configurations, the `KeepDomain` migration, and unit + integration proof
tests — all green across build, unit, architecture, and integration suites.

---

## What was built

### Keep.Core (new)

- `Entities/Enums/KeepRequestStatus.cs` — Received/InProgress/PendingCustomer/Resolved/Closed/Cancelled (explicit values; `Cancelled = 6`, ADR-050).
- `Entities/Enums/KeepRequestEventType.cs` — RequestCreated/StatusChanged/OperatorReplied/CustomerReplied/RequestClosed/RequestCancelled.
- `Entities/Enums/KeepRequestEventVisibility.cs` — System/All/Internal.
- `Entities/KeepCustomer.cs` — identity `(AccountId, PrimaryPhone)`; `Create(...)` factory + `UpdateContactInfo`.
- `Entities/KeepRequest.cs` — account/customer ids, denormalized customer name/phone/email, description, status, reference code, page token, page-expiry/closed timestamps, last business/customer activity; `IsTerminal` computed; static `Create(...)`.
- `Entities/KeepRequestEvent.cs` — audit record; static `CreateRequestCreated(...)`.
- `Entities/KeepPublicIntakeLink.cs` — `AccountId`/`PublicSlug`/`TokenHash`/`RevokedAtUtc`; `IsActive` computed; `Revoke()` returns `Result`; never stores a raw token (ADR-046).
- `Errors/KeepRequestErrors.cs`, `Errors/KeepPublicIntakeLinkErrors.cs`.

### Keep.Application (new)

- `Services/KeepTokenService.cs` — `GeneratePageToken()`, `GeneratePublicIntakeToken()`, `HashPublicIntakeToken(string)` (SHA-256 lowercase hex), `GenerateReferenceCode()`.

### Foundation.Infrastructure (adapted — ADR-047/048)

- `BaseEntityConfiguration<T>` `internal` → `public` so Keep configs can extend it (visibility only).
- `OpHaloDbContext` gains optional `IEnumerable<Assembly>? additionalModelAssemblies`; `OnModelCreating` applies Foundation's assembly first, then each additional assembly. No `DbSet<KeepT>` named properties — Foundation does not reference Keep (§8). Keep callers use `context.Set<T>()`.

### Keep.Infrastructure (new — ADR-049)

- Four EF configurations under `Persistence/Configurations/` (`keep_customers`, `keep_requests`, `keep_request_events`, `keep_public_intake_links`).
- `KeepDesignTimeDbContextFactory` — constructs `OpHaloDbContext` with the Keep assembly so EF discovers Keep configs without Foundation referencing Keep.
- `OpHalo.Keep.Infrastructure.csproj` — added `Microsoft.EntityFrameworkCore.Design` (`PrivateAssets=all`).

### Migration

`src/OpHalo.Foundation.Infrastructure/Migrations/20260615105355_KeepDomain.cs` —
creates all four `keep_*` tables. Generated into Foundation.Infrastructure (single
migration assembly / single `__OpHaloMigrationsHistory`, ADR-005/049) using the Keep
startup project so Keep configs are included:

```bash
ConnectionStrings__DefaultConnection="<dummy>" \
dotnet ef migrations add KeepDomain \
  --project src/OpHalo.Foundation.Infrastructure \
  --startup-project src/OpHalo.Keep.Infrastructure \
  --context OpHaloDbContext
```

`migrations add` does not connect to a database; a placeholder connection string
only satisfies the design-time factory's Npgsql configuration.

---

## Indexes (proven by integration tests)

- `keep_customers`: unique `(account_id, primary_phone)` → `ix_keep_customers_account_phone`.
- `keep_requests`: unique `page_token` → `ix_keep_requests_page_token`; unique `(account_id, reference_code)` → `ix_keep_requests_account_reference_code`; index `account_id`.
- `keep_request_events`: indexes on `request_id` and `account_id`.
- `keep_public_intake_links`: partial unique on `public_slug` and on `token_hash`, both filtered `revoked_at_utc IS NULL AND deleted_at_utc IS NULL`.

`IsTerminal` and `IsActive` are computed and `Ignore`d in their configs.

---

## Two bugs found and fixed (carried from the in-progress session)

1. **KeepTokenService reference code** — the safe alphabet `"ABCDEFGHJKMNPQRSTUVWXYZ23456789"` has 31 chars, not 32; the original `b % 32` index was out of range. Replaced with unbiased rejection sampling (accept bytes `< 248 = 256 - 256 % 31`).
2. **EF Core model caching** — EF caches the model per context type. If a Foundation test created a context without the Keep assembly first, the Keep tests received a stale Foundation-only model. Fix: `PostgresFixture.CreateContext()` always passes `Keep.Infrastructure.AssemblyMarker.Assembly` so every context in the integration suite builds the same Foundation+Keep model. Foundation tests are unaffected (they never touch Keep entities).

The final compile error this session — a missing `using OpHalo.Foundation.Infrastructure.Persistence;` in `KeepPersistenceProofTests.cs` (the `CreateContext()` return type) — was restored to close the build.

---

## Decisions

- **ADR-047** — `BaseEntityConfiguration<T>` `internal` → `public`.
- **ADR-048** — `OpHaloDbContext` optional `additionalModelAssemblies`; bounded contexts register configs without Foundation referencing Keep.
- **ADR-049** — single migration assembly (Foundation.Infrastructure); `KeepDesignTimeDbContextFactory` is the migration entrypoint when Keep tables are involved.
- **ADR-050** — `KeepRequestStatus.Cancelled = 6`; reason lives on the event, not the status enum.

---

## Verification

| Suite | Result |
|-------|--------|
| `dotnet build OpHalo.slnx` | Build succeeded, 0 warnings, 0 errors |
| UnitTests | **230 passed** (176 → 230) |
| ArchitectureTests | **14 passed** (Foundation still does not reference Keep) |
| IntegrationTests | **15 passed** (7 Foundation + 8 Keep) |

Integration tests run against real PostgreSQL via Testcontainers; Docker required.

---

## Exit gate — Phase 7A (met)

- Keep domain entities and EF mappings compile. ✅
- `KeepDomain` migration generated and applies in the PostgreSQL Testcontainers harness. ✅
- Unit/domain tests and integration persistence proofs green. ✅
- Architecture tests green: Foundation does not reference Keep; Keep may reference Foundation. ✅

Phase 7B (Application/API vertical) is now unblocked on a proven schema.

---

## Risks / debt carried forward

- **Phase 7B route compatibility** — public route names may be external contracts; decide `/keep/...` vs legacy `/continuity/...` aliases before shipping.
- **DI registration + `appsettings` connection string** — still deferred; 7B is likely the first runtime caller needing host DbContext wiring.
- **Two-phase provisioning save (ADR-044)** — future persistence boundary must still encapsulate the null-then-update owner sequence.
- Keep `KeepRequestStatus`/event-type values beyond v1 minimums exist in the enum but are not yet exercised by behavior — 7B+ will wire them.
