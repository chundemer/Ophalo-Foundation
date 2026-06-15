# Session Log — OpHalo Foundation

**Last updated:** 2026-06-15
**Next session tier:** Tier 2 — Implementation · _Phase 7B Session B — API + HTTP integration tests + build-log_
**Branch:** `main` (no remote yet)

---

## Current state — Phase 7B Session A complete, ready to commit

Build: ✅ 0 warnings / 0 errors  
UnitTests: ✅ **260** passed (+30 new — KeepPublicIntakeServiceTests + KeepRequestListServiceTests)  
ArchitectureTests: ✅ **14** passed  
IntegrationTests: ✅ **15** passed  

---

## What was completed this session (not yet committed)

| File | Notes |
|------|-------|
| `Keep.Application/Abstractions/AccountAccessSnapshot.cs` | Foundation state as Keep read model |
| `Keep.Application/Abstractions/AccountUserSnapshot.cs` | AccountUser state as Keep read model |
| `Keep.Application/PublicIntake/PublicIntakeCommitResult.cs` | Explicit enum: Committed=1, UniqueTokenCollision=2 |
| `Keep.Application/PublicIntake/IKeepIntakePersistence.cs` | Intent-revealing interface, EF-free |
| `Keep.Application/PublicIntake/CreateKeepPublicIntakeCommand.cs` | Command record |
| `Keep.Application/PublicIntake/CreateKeepPublicIntakeResult.cs` | Result record |
| `Keep.Application/PublicIntake/CreateKeepPublicIntakeService.cs` | Full service with null guard + exhaustive switch |
| `Keep.Application/Requests/IKeepRequestListPersistence.cs` | Intent-revealing interface, EF-free |
| `Keep.Application/Requests/KeepRequestSummary.cs` | Operator list read model |
| `Keep.Application/Requests/GetKeepRequestListResult.cs` | Result wrapper |
| `Keep.Application/Requests/GetKeepRequestListService.cs` | Auth → permission → access → feature → list |
| `Keep.Infrastructure/Persistence/KeepIntakePersistence.cs` | EF impl, Postgres collision detection |
| `Keep.Infrastructure/Persistence/KeepRequestListPersistence.cs` | EF impl, open requests ordered by LastBusinessActivityAt |
| `Keep.Infrastructure/OpHalo.Keep.Infrastructure.csproj` | Added `Npgsql.EntityFrameworkCore.PostgreSQL` |
| `UnitTests/Keep/KeepPublicIntakeServiceTests.cs` | 17 tests, manual fakes |
| `UnitTests/Keep/KeepRequestListServiceTests.cs` | 13 tests, manual fakes |
| `docs/decisions/decision-index.md` | ADR-055 added |
| `docs/session-log.md` | This file |

---

## CLAUDE.md changes made this session (earlier in context)

Three additions:
1. **Quality Over Speed** — explicit rule that delivery pressure does not override architecture.
2. **Pre-Implementation Gate** — mandatory step: list files AND open design decisions, wait for confirmation before writing any file.
3. **Cross-Reference Before Every External Call** — before writing a file, verify every external method's signature and failure modes against already-read source; guard against throws; exhaustive switches on owned enums.

---

## Next session — Phase 7B Session B

**Pre-work complete.** Proceed with API wiring, HTTP integration tests, and build-log.

### Session B scope (in order)

1. `Foundation.Infrastructure/Security/AnonymousCurrentUser.cs` — implements `ICurrentUser` with `IsAuthenticated = false`; used as the DI registration in the API host until Phase 5 auth exists
2. `Api/Program.cs` full rewrite — DI registrations (`OpHaloDbContext`, Keep.Infrastructure persistence classes, `KeepTokenService`, access/feature policies, `AnonymousCurrentUser`), rate limiting, minimal API routes (`POST /keep/public-intake/token/{token}`, `GET /keep/requests`)
3. `Api/appsettings.json` — add `ConnectionStrings:DefaultConnection`
4. `IntegrationTests.csproj` — add `Microsoft.AspNetCore.Mvc.Testing` + project reference to `OpHalo.Api`
5. `IntegrationTests/Api/KeepApiWebFactory.cs` — `WebApplicationFactory<Program>` with test DB configuration
6. `IntegrationTests/Api/KeepIntakeApiTests.cs` — HTTP integration tests for both endpoints
7. `docs/build-log/014-phase-7b-implementation.md` — build-log entry for both Session A + B
8. Final session-log.md rewrite + commit

### API design (confirmed in Session A/discovery)

**POST `/keep/public-intake/token/{publicIntakeToken}`**  
Anonymous (no auth). Body: `customerName`, `customerPhone`, `customerEmail?`, `description`. Returns `{ requestId, referenceCode, pageToken }` on 201. All failures → 422 `keep.public_intake.unavailable` (no information leakage).

**GET `/keep/requests`**  
Operator auth required. Returns `{ requests: [...] }` with KeepRequestSummary items. 401 unauthorized, 403 forbidden.

---

## Architecture decisions (ADR-055…057)

**ADR-055** — Intent-revealing persistence abstractions (`IKeepIntakePersistence`, `IKeepRequestListPersistence`) with `AccountAccessSnapshot`/`AccountUserSnapshot` read models; EF Core stays out of Keep.Application; bounded retry (MaxAttempts=5); `PublicIntakeCommitResult` enum makes retry contract explicit; Infrastructure owns EF entity state cleanup on collision.

**ADR-056** — All public intake gate failures return the same generic `keep.public_intake.unavailable` error — no gate-specific codes on the public surface (information hiding, extends ADR-011 public-guard posture).

**ADR-057** — `KeepRequestStatus` serializes to lowercase snake_case slugs (`received`, `in_progress`, `pending_customer`, `resolved`, `closed`, `cancelled`). These are an API contract; breaking to change. Mapping is exhaustive with `default: throw`.

---

## Service design notes (carry into Session B for API wiring)

### CreateKeepPublicIntakeService
- Returns `Result<CreateKeepPublicIntakeResult>` — map to 201/422 in API
- All gate failures return the generic `keep.public_intake.unavailable` error (public-safe — no internal state revealed)
- Validation errors (`KeepRequest.CustomerNameRequired` etc.) are also returned as 422 — map the same way or distinguish

### GetKeepRequestListService
- Returns `Result<GetKeepRequestListResult>` — map to 200/401/403 in API
- `auth.unauthorized` → 401; `auth.forbidden` → 403; anything else → 403

### Infrastructure watch-out (EF entity state on collision)
`CommitPublicIntakeAsync`: detaches request + event + new customer on `UniqueTokenCollision`; leaves existing tracked customer alone. ✅ Implemented.

---

## Phase status

| Phase | Status | Notes |
|-------|--------|-------|
| 1 — Skeleton + architecture tests | ✅ done | `00227b0` |
| 2 — Legacy exclusion (doc) | ✅ done | `2fce382` |
| 3 — SharedKernel + abstraction cleanup | ✅ done | `2fce382` |
| 4a — Account/User/AccountUser + lifecycle + access policy | ✅ done | `ec4c35c` |
| 4b — AccountEntitlements | ✅ done | `7cf49aa` |
| 4c — Permission keys + role access policy | ✅ done | `034eee4` |
| 4d — Feature keys / entitlements | ✅ done | `eef4b07` |
| Account-creation orchestration | ✅ done | `e09d876` |
| 6 — Persistence Session A (infra) | ✅ done | `68354dc` |
| 6 — Persistence Session B (proof tests) | ✅ done | `88a9dd6` |
| 7 — Keep intake to operator view discovery | ✅ done | |
| 7A — Keep domain + persistence foundation | ✅ done | `41f4f0c` |
| 7B discovery | ✅ done | build-log/013 |
| 7B Session A — Application + Infrastructure | ✅ complete, pending commit | |
| 7B Session B — API + HTTP tests + build-log | ⏭️ next | |

---

## Watch-outs / debt carried forward

- **Phone normalization** — deferred; `primaryPhone` stored as submitted (trimmed only)
- **DI registration + `appsettings`** — wired in Session B
- **AnonymousCurrentUser** — placeholder for Phase 5 auth, lives in Foundation.Infrastructure; deferred to Session B
- **Two-phase provisioning save (ADR-044)** — future persistence boundary concern
- **`AccountUser.IsActive` is `Ignore`d** — computed (ADR-023); never reintroduce as a column
- **EF model caching** — every context must build Foundation+Keep model together
- **Schema-drop reset pattern** — `DROP SCHEMA public CASCADE` + recreate + `MigrateAsync`
- **Migration generation** — always `--startup-project src/OpHalo.Keep.Infrastructure`
- Never glob through `_reference/**/bin` or legacy `obj` trees
- Legacy `decision-index`/`decisions/**`/`coding-rules` remain **pending validation**
- No GitHub remote yet
