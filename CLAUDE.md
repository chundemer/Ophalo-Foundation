# OpHalo Foundation — Claude Code Instructions

_These rules apply to every session without exception._

This is the **OpHalo Foundation rebuild**: "greenfield boundaries, brownfield
behavior." A clean repo whose structure is new, but whose *behavior* is ported
from the current deployed system. Do not drag the old architecture forward
unchanged; do not rewrite working behavior for cleanliness. Move, rename, adapt,
verify — redesign only when the old implementation blocks the foundation.

- **Authoritative build plan:** `/Users/christian/Downloads/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` (14 phases).
- **Reference (legacy) app:** `/Users/christian/application/ophalo`, aliased read-only as `_reference/` in this repo. It is the behavior reference/fallback until parity. Never edit it.

---

## Session Protocol

### Session Start — Two Tiers

**Tier 1 — Discovery session** (new phase/domain, pre-work not done):
1. Read `docs/session-log.md`
2. Read the latest entries in `docs/build-log/` (what shipped, exit gates met)
3. Ask Christian what we are working on today
4. Read only the build-plan phase for that work — nothing else
5. Read the specific reference source under `_reference/src/...` needed to understand current behavior
6. State scope before generating anything — files affected, layers touched

**Tier 2 — Implementation session** (session-log header says `Pre-work complete`):
1. Read `docs/session-log.md` only — it holds the confirmed facts
2. Ask Christian what we are working on today
3. Verify specific facts with targeted reads (a signature, an import) — do not re-read whole files
4. State scope and proceed

**How to tell which tier:** the session log header says `Pre-work complete` for Tier 2. If absent, default to Tier 1.

### Session End — Every Session
1. Rewrite `docs/session-log.md` entirely (do not append) — completed, next, blockers
2. Mark next session `Pre-work complete` only when the implementation target is fully confirmed
3. Commit; Christian approves before closing

### Session Size Rule
Keep sessions well within the context limit. If a task touches more than ~8–10 files or spans more than one layer, **split into multiple sessions before writing code**, and state the split to Christian first. Start a new session after a commit.

---

## ⚠️ Pending Validation — Do NOT Load

The legacy `decision-index.md`, `docs/reference/decisions/**`, and `coding-rules.md`
from the reference app are **not yet validated** and are deliberately excluded.
Do not load them or rely on them until Christian validates and ports them. They
remain visible read-only under `_reference/docs/` if a specific fact must be checked.

Until then, the authoritative sources are: the **build plan**, the **`docs/build-log/`**
entries, and the **architecture tests**.

---

## How We Work

- Be concise — short, direct, no preamble, no trailing fluff.
- Role: senior .NET architect — explain the WHY, flag issues and missing contracts before writing a file.
- Do not invent abstractions, namespaces, or factory methods. Confirm a symbol exists (targeted read) before calling it.
- **Commands:** Claude may run read-only inspection and `dotnet build` / `dotnet test` directly to keep work verified. Christian runs interactive / auth / deploy / migration commands himself (`dotnet ef`, `gcloud`, `npm run dev`, deploys) — suggest them with a leading `!` when needed. _(Adaptation from the reference app's "no CLI" rule — confirm if you'd prefer the stricter version.)_
- Permission prompts can be reduced for a session with **Shift+Tab** (auto-accept edits); Shift+Tab again to revert.

---

## Architecture Boundaries — Enforced by Tests

`tests/OpHalo.ArchitectureTests` must stay green. Rules (build plan §8):
- Foundation must not reference Keep; Keep may reference Foundation.
- SharedKernel holds Result/Error/IClock-style primitives only — no business concepts (no CurrentUser, email, DbContext, Account, entitlements, notifications, Keep).
- Application must not depend on Infrastructure. Core must not depend on Application or Infrastructure.
- One API host (`OpHalo.Api`). No `Signal.*`, `Continuity.*`, `Platform.*`, or separate `Auth` projects.
- Product-specific user settings attach to `AccountUser`, never a duplicate identity.

---

## Project Structure

```
src/
  OpHalo.Api/  OpHalo.Worker/                     (hosts / composition roots)
  OpHalo.Foundation.Core/.Application/.Infrastructure/
  OpHalo.Keep.Core/.Application/.Infrastructure/
  OpHalo.SharedKernel/
tests/
  OpHalo.ArchitectureTests/  OpHalo.UnitTests/  OpHalo.IntegrationTests/
docs/  (architecture, build-log, decisions, deployment)
web/   (ophalo-app, ophalo-web — placeholders; not yet scaffolded)
```

Canonical types ported so far:
- `OpHalo.SharedKernel.Results.Result` / `Result<T>` / `Error`
- `OpHalo.SharedKernel.Abstractions.IClock`
- `OpHalo.Foundation.Application.Abstractions.Security.ICurrentUser`
- `OpHalo.Foundation.Application.Abstractions.Messaging.IEmailSender`

---

## Naming Constraint

- **GitHub repo:** `ophalo-foundation` (the legacy repo already owns `ophalo`).
- **Internal solution/namespaces:** stay `OpHalo.slnx` / `OpHalo.*`. Do **not** rename to `OpHalo.Foundation.*` — that collides with the internal Foundation architecture layer.

---

## Documentation Maintenance — Mandatory

- Every phase ends with a `docs/build-log/NNN-*.md` entry (preserved / moved / renamed / adapted / redesigned / why / tests / exit gate / risks) shipped with the code.
- Every session ends with a rewritten, approved `docs/session-log.md`.

---

## Web — Two Apps (when scaffolded)

| App | Path | Purpose |
|---|---|---|
| `ophalo-web` | `web/ophalo-web` | Marketing / auth / public customer surfaces |
| `ophalo-app` | `web/ophalo-app` | Operator / account / admin app (admin shell under `app/admin`) |

Both target the single `OpHalo.Api` base URL. Always confirm which app a frontend task belongs to.

---

_OpHalo — ophalo.com — Quiet Intelligence. Clear Decisions._
