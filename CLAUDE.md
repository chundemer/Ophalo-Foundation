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
2. Read `docs/decisions/decision-index.md` and the latest `docs/build-log/` entries
3. Ask Christian what we are working on today
4. Read only the build-plan phase for that work — nothing else
5. Read the specific reference source under `_reference/src/...` needed to understand current behavior
6. State scope before generating anything — files affected, layers touched

**Tier 2 — Implementation session** (session-log header says `Pre-work complete`):
1. Read `docs/session-log.md` only — it holds the confirmed facts
2. Ask Christian what we are working on today
3. Verify specific facts with targeted reads (a signature, an import) — do not re-read whole files
4. State scope and open design decisions — wait for confirmation before writing any file (see Pre-Implementation Gate)

**How to tell which tier:** the session log header says `Pre-work complete` for Tier 2. If absent, default to Tier 1.

### Session End — Every Session
1. Rewrite `docs/session-log.md` entirely (do not append) — completed, next, blockers
2. Mark next session `Pre-work complete` only when the implementation target is fully confirmed
3. Commit; Christian approves before closing

### Session Size Rule
Keep sessions well within the context limit. If a task touches more than ~8–10 files or spans more than one layer, **split into multiple sessions before writing code**, and state the split to Christian first. Start a new session after a commit.

---

## Authoritative vs Pending

**Authoritative for this build:** the **build plan**, `docs/decisions/decision-index.md`
(this project's own decision ledger), the `docs/build-log/` entries, and the
**architecture tests**. Add a one-liner to the decision index the moment a decision
is made.

**⚠️ Pending validation — do NOT load:** the *legacy* `decision-index.md`,
`docs/reference/decisions/**`, and `coding-rules.md` from the reference app are not
yet validated and are deliberately excluded. They remain readable under
`_reference/docs/` for historical reference only, after validation.

---

## Quality Over Speed — Non-Negotiable

This rebuild exists because the reference app accumulated shortcuts that compounded into structural debt. Every session must hold this boundary: **a correct foundation built slowly is the goal; fast code that compromises the foundation is not an acceptable trade.**

- If a design decision is not already locked in the ADRs, surface it as a question — do not resolve it silently and write code.
- If something feels like a shortcut or a rationalization ("the tests don't prevent it", "the reference app does it this way", "we can clean it up later"), treat that feeling as a flag, not a green light. Stop and ask.
- Delivery pressure does not override architecture. The cost of a bad abstraction in the foundation compounds across every phase that follows.

---

## Pre-Implementation Gate — Mandatory Before Writing Any File

After reading and stating scope, and **before writing the first file**, explicitly list:

1. **Files to create or modify** — names and layers.
2. **Open design decisions** — any architectural choice the implementation will resolve that is not already in the ADRs or the session-log confirmed facts. Each must be stated and confirmed by Christian before code is written.

If a design decision surfaces mid-implementation, stop, surface it, and wait for confirmation. Do not rationalize past it.

## Writing Code — Cross-Reference Before Every External Call

Before writing any file that calls existing code (domain factories, service methods, policy interfaces), do one explicit pass:

> For every external method this file will call, verify the method's signature and failure modes against the already-read source — argument guards, throw conditions, what null means.

This is not optional when in execution mode. The planning phase builds a mental model; that model drifts. The cross-reference step corrects drift before it becomes a bug in a written file.

Specifically: if a method throws on null/whitespace input, the caller must guard before calling it. If a switch covers an owned enum, it must be exhaustive with a `default` throw.

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
- Every decision gets a one-line entry in `docs/decisions/decision-index.md` (full ADR only when it needs more than a line).
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
