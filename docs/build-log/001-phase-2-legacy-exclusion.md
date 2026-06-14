# Build Log 001 — Phase 2: Low-Risk Legacy Exclusion

**Date:** 2026-06-14
**Phase:** 2 — Low-Risk Legacy Exclusion (build plan §9)
**Reference repo audited:** `/Users/christian/application/ophalo` (the current deployed system, kept as reference/fallback)
**Target repo:** `/Users/christian/saas/ophalo-foundation`

---

## Purpose

Phase 2 prunes dead legacy families from the *active target* before any product
behavior is moved. Because the target is a greenfield repo, the exclusion is
satisfied **by construction** (the legacy families were never created here). This
log records what exists in the reference repo, what is deliberately excluded from
the target and why, and confirms the Phase 2 exit gate.

---

## Reference-repo audit findings

The reference solution (`OpHalo.slnx`) contains these `src` projects:

```
OpHalo.API                       OpHalo.Core
OpHalo.Application                OpHalo.Infrastructure
OpHalo.Auth                      OpHalo.Shared
OpHalo.Continuity.API            OpHalo.Platform.Application
OpHalo.Continuity.Application    OpHalo.Platform.Core
OpHalo.Continuity.Core           OpHalo.Platform.Infrastructure
OpHalo.Continuity.Infrastructure
```

On disk there are **four additional `Signal.*` projects** that are **not listed in
the solution file** but remain on the build graph:

```
OpHalo.Signal.API   OpHalo.Signal.Application   OpHalo.Signal.Core   OpHalo.Signal.Infrastructure
```

### Live build-graph edges (the actual debt)

| Edge | Evidence | Meaning |
|------|----------|---------|
| `OpHalo.API` → `OpHalo.Signal.Infrastructure`, `OpHalo.Signal.API` | `src/OpHalo.API/OpHalo.API.csproj:31-32` | **This is the §2 finding.** Signal is absent from the `.slnx` yet the live API host references it by project, so it is compiled into the runtime build graph despite being dead at runtime. |
| `OpHalo.Signal.Infrastructure` → `OpHalo.Platform.Application`, `OpHalo.Platform.Infrastructure` | csproj comment cites ADR-033 | Signal transitively drags Platform in. |
| `OpHalo.Infrastructure` → `OpHalo.Continuity.*` | `src/OpHalo.Infrastructure/OpHalo.Infrastructure.csproj` | Continuity is wired into the consolidated infrastructure. |
| `OpHalo.Application` / `OpHalo.Infrastructure` → `OpHalo.Platform.*` | csproj references | Platform is on the active graph. |

### Other observations carried to later phases

- Two API hosts confirmed: `OpHalo.API` and `OpHalo.Continuity.API` (collapse in Phase 10), plus `Dockerfile` + `Dockerfile.continuity`.
- The reference repo has a recursive `bin/Debug/net10.0/bin/Debug/...` nesting (build-output misconfiguration) — noted, not relevant to the target.

---

## Decisions for the target

| Legacy family | Decision | Rationale |
|---------------|----------|-----------|
| `OpHalo.Signal.*` | **Excluded entirely** — not created in the target | Dead at runtime; only alive via the API host's project reference (build plan §2, §3.4, §9 Phase 2). |
| `OpHalo.Continuity.*` (as project/module names) | **Excluded** — product behavior will be ported into `OpHalo.Keep.*` in Phases 7–8 | "Greenfield boundaries" §4.2: Continuity does not survive as the active module name. |
| `OpHalo.Platform.*` | **Excluded** — concepts fold into `OpHalo.Foundation.*` | §4.3: Platform project naming is removed; capabilities become Foundation. |
| `OpHalo.Auth` (separate project) | **Excluded** — auth becomes part of Foundation | §4.3: "Auth is part of Foundation, not a separate project." |
| Second API host (`OpHalo.Continuity.API`) | **Excluded** — single `OpHalo.Api` host | §4.4 one-host strategy. |

These are exclusions from the *active target*. The reference repo is untouched and
remains the behavior reference until parity (§10).

---

## Target-repo verification

The greenfield target contains **no** Signal/Continuity/Platform/Auth projects. The
Phase 1 architecture tests already enforce this and pass:

- `No_active_project_may_reference_legacy_families` — theory over `OpHalo.Signal`,
  `OpHalo.Continuity`, `OpHalo.Platform` namespaces, asserted across all nine
  production assemblies.

## Phase 2 exit gate (§9)

- ✅ **Build green** — `dotnet build OpHalo.slnx`: 0 warnings, 0 errors (Phase 1).
- ✅ **Tests green** — 9 tests pass.
- ✅ **No active Signal references** — enforced by architecture test, and no such
  project exists in the target.

**Result:** Phase 2 exit gate met by construction + documentation. No code change
was required in the target.

---

## Phase-end summary

- **Preserved:** nothing moved yet (skeleton only).
- **Moved:** nothing.
- **Renamed:** legacy family names are designed out of the target (see table).
- **Adapted:** nothing.
- **Redesigned:** the dead `Signal.*` build-graph edge is eliminated by not
  recreating it; Platform/Continuity/Auth project boundaries are replaced by
  Foundation/Keep boundaries.
- **Why:** the live `OpHalo.API → Signal.*` reference compiled dead code into the
  runtime graph and the Platform/Continuity/Auth split fragmented identity/auth.
- **Tests proving it:** `No_active_project_may_reference_legacy_families` (+ full
  suite green).
- **Foundation rule now passing:** §8 "No active project may reference `Signal.*`".
- **Risks remaining:** Continuity/Platform/Auth *behavior* still has to be ported
  faithfully in later phases; this log only excludes the *names/structure*.
