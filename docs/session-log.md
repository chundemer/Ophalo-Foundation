# Session Log — OpHalo Foundation

**Last updated:** 2026-06-14
**Next session tier:** Tier 1 — Discovery (Phase 4 pre-work NOT done)
**Branch:** `main` (no remote yet) — last commit `2fce382`

> Next session is **Tier 1**: do discovery for Phase 4 before writing code. The
> Phase 4 domain (Account / User / entitlements / permissions) has not yet been
> read out of the reference app.

---

## Where we are

Phases 0–3 of the build plan are complete and committed. Build is clean
(0 warnings / 0 errors); **33 tests passing**.

| Phase | Status | Commit |
|-------|--------|--------|
| 1 — Skeleton + architecture tests | ✅ done | `00227b0` |
| 2 — Legacy exclusion (doc) | ✅ done | `2fce382` |
| 3 — SharedKernel + duplicate abstraction cleanup | ✅ done | `2fce382` |

Build-log entries: `docs/build-log/001-phase-2-legacy-exclusion.md`,
`docs/build-log/002-phase-3-sharedkernel-abstractions.md`.

## What exists now

- Solution `OpHalo.slnx`, 12 projects, .NET 10, all in `OpHalo.*` namespaces.
- Architecture tests enforcing the §8 boundaries (Foundation↛Keep, Core↛App/Infra,
  App↛Infra, SharedKernel discipline, no Signal/Continuity/Platform) — green.
- SharedKernel: `Result`/`Result<T>`/`Error` (ported verbatim), `IClock`.
- Foundation.Application: canonical `ICurrentUser`, `IEmailSender`.

## Environment / setup (this machine)

- Reference (legacy) app: `/Users/christian/application/ophalo`, aliased read-only
  as `_reference/` (gitignored). Read access via gitignored
  `.claude/settings.local.json` — **may need a Claude Code reload to take effect**
  (open `/hooks` once, or restart).
- `_reference` and `.claude/settings.local.json` are gitignored and must stay untracked.

## Next — Phase 4 (Foundation Account/User/Lifecycle/Entitlements/Permissions)

Discovery to do before coding:
1. Read the reference identity/account domain under
   `_reference/src/OpHalo.Core`, `_reference/src/OpHalo.Application`,
   `_reference/src/OpHalo.Auth` (Account, User, AccountUser, membership, lifecycle).
2. Map to plan §4.8–§4.11: roles (Owner/Admin/Operator/Viewer), membership statuses
   (Invited/Active/Suspended/Removed), lifecycle (Active/Suspended/Closed),
   commercial (Trial/Active/PastDue/Expired/Canceled), operating mode, purpose,
   entitlements/feature keys, permission keys.
3. Preserve existing lifecycle semantics where correct; ADD the permission-key and
   feature-key layers (these are new — they did not exist in the reference).
4. Exit gate: unit tests for account posture, role→permission mapping, entitlement
   checks; Foundation has no Keep references.

This is a large phase — plan to split it across sessions (start with Account/User/
AccountUser + lifecycle; defer entitlements/permissions to a second session if needed).

## Blockers / watch-outs

- The reference repo has a recursive `bin/Debug/...` nesting — never glob through
  `_reference/**/bin`. Read specific source paths instead.
- Legacy `decision-index`, `decisions/**`, and `coding-rules` are **pending validation**
  — do not load or rely on them (see CLAUDE.md).
- No GitHub remote yet. When added, the repo must be named `ophalo-foundation`.
