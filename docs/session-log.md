# Session Log — OpHalo Foundation

**Last updated:** 2026-06-15
**Next session tier:** Tier 1 — Discovery (next phase TBD with Christian)
**Branch:** `main` (no remote yet)

---

## Current state — Phase 7B complete

Build: ✅ 0 warnings / 0 errors
UnitTests: ✅ **260** passed
ArchitectureTests: ✅ **14** passed
IntegrationTests: ✅ **22** passed (+7 new HTTP integration tests)

---

## What was completed this session (Phase 7B Session B)

| File | Notes |
|------|-------|
| `Foundation.Infrastructure/Security/AnonymousCurrentUser.cs` | ICurrentUser placeholder, IsAuthenticated=false, Guid.Empty IDs (ADR-058) |
| `Api/Keep/PublicIntakeRequest.cs` | Body DTO; emailNotificationsEnabled accepted/ignored (ADR-059) |
| `Api/Program.cs` | Full rewrite: AddProblemDetails, AddOpenApi, rate limiter, DI registrations, minimal API routes, RFC 7807 error mapper, Testing-env HTTPS bypass |
| `Api/appsettings.json` | Added ConnectionStrings:DefaultConnection (empty; supplied by environment) |
| `IntegrationTests/OpHalo.IntegrationTests.csproj` | Added Microsoft.AspNetCore.Mvc.Testing 10.0.9 + OpHalo.Api project reference |
| `IntegrationTests/Api/KeepApiWebFactory.cs` | WebApplicationFactory<Program> with Testcontainers postgres, ConfigureAppConfiguration for connection string, ICurrentUser override, ResetDatabaseAsync |
| `IntegrationTests/Api/KeepIntakeApiTests.cs` | 7 HTTP tests covering all build-log/014 exit criteria |
| `docs/decisions/decision-index.md` | ADR-060 added (rate limiting policy) |
| `docs/build-log/015-phase-7b-implementation.md` | Full build-log for both Session A + B |

---

## Process corrections made this session

Two feedback memories saved:
1. **Reference app check before declaring a decision open** — grep `_reference/src/` first; evaluate critically before surfacing as open
2. **Read handoff build-log before the Pre-Implementation Gate** — when session-log references a build-log as a handoff, read it before writing any code; it is the implementation spec, not background context

---

## Architecture decisions (ADR-055…060)

| ADR | Decision |
|-----|---------|
| ADR-055 | Intent-revealing persistence abstractions; EF stays out of Keep.Application; retry contract via PublicIntakeCommitResult enum |
| ADR-056 | All public gate failures return `keep.public_intake.unavailable` — no gate-specific codes |
| ADR-057 | KeepRequestStatus → lowercase snake_case slugs; exhaustive switch with `default: throw` |
| ADR-058 | Interim auth: AnonymousCurrentUser in production; ICurrentUser overridden in tests |
| ADR-059 | HTTP contract locked: 201/400/422/401/403; ProblemDetails with extensions.code |
| ADR-060 | Rate limiting: per-IP fixed-window 10 req/min, CF-Connecting-IP → X-Forwarded-For → RemoteIpAddress; deploy-time Cloudflare constraint documented |

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
| 7B — Application + Infrastructure + API + HTTP tests | ✅ done | `a1b67ee` + this session |
| **Next** | ⏭️ TBD | Discuss with Christian at next session start |

---

## Watch-outs / debt carried forward

- **Connection string fail-fast** — currently deferred to first DB scope creation; a hosted service should validate at startup before production traffic
- **Phone normalization** — `primaryPhone` stored as submitted (trimmed only)
- **AnonymousCurrentUser** — placeholder for Phase 5 auth
- **Two-phase provisioning save (ADR-044)** — canonical pattern; future repository must encapsulate
- **`AccountUser.IsActive` is `Ignore`d** — computed (ADR-023); never reintroduce as a column
- **EF model caching** — every context must build Foundation+Keep model together
- **Notifications** — CustomerEmail/EmailNotificationsEnabled stored, no delivery yet (ADR-053/059)
- **No GitHub remote yet**
- **Schema-drop reset pattern** — `DROP SCHEMA public CASCADE` + recreate + `MigrateAsync`
- **Migration generation** — always `--startup-project src/OpHalo.Keep.Infrastructure`
- Legacy `decision-index`/`decisions/**`/`coding-rules` remain **pending validation**
