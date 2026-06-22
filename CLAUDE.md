# OpHalo Foundation — Claude Code Instructions

These rules apply to every session. Keep responses concise and protect the context window.

## Authority

Repository documents are authoritative. Use them in this order:

1. `docs/session-log.md` for current scope, baseline, and the next approved batch.
2. Named entries in `docs/decisions/decision-index.md` for locked decisions.
3. Named `docs/build-log/` entries for completed implementation history.
4. Architecture tests for enforced dependency boundaries.

Do not treat external files, the legacy/reference application, or legacy decisions as authoritative.
Read `_reference/` or external build plans only when Christian or the current repository brief
explicitly requests them. Never edit `_reference/`.

## Context-Budget Rules

- Search first with `rg` or `rg --files`; inspect only the matching symbols and nearby code.
- Never read a source or documentation file over 250 lines in full unless Christian explicitly asks.
- Prefer narrow `sed` ranges, normally no more than 80–120 lines per read.
- For `session-log.md`, read the header and the named current-work section only.
- For `decision-index.md`, extract only the ADR numbers named by the task; never read the full file.
- For large services, read only the methods being changed plus directly called signatures and failure
  paths. Do not read a whole service merely to understand one mapper or action builder.
- Do not reread unchanged material already established in the current session.
- Do not paste complete files, long diffs, or test logs into chat. Report changed files, findings,
  concise outcomes, and exact test counts.
- Keep command output bounded with targeted paths, filters, and minimal verbosity.
- A correction from review should trigger a targeted patch and focused test, not rediscovery of the
  entire feature.

## Session and Scope Protocol

Use these terms precisely:

- **Discovery** determines code paths, unresolved decisions, architecture, scope, and tests. It is
  performed only when the session-log says pre-work is incomplete or when implementation exposes a
  concrete contradiction.
- **Pre-work complete / implementation-ready** means those questions are already answered in the
  repository brief. Treat that plan as authoritative; do not rediscover, reconsider, or reread the
  feature broadly.
- **Implementation preflight** is mechanical only: use targeted `rg` to confirm the named symbols
  still exist, enumerate compile-impact constructor/interface/test-helper callers, and report any
  drift from the brief. It is not a second discovery phase.

If the session-log marks the requested batch `Pre-work complete` or `implementation-ready`, read
only that batch, perform the mechanical preflight, present the exact file-level gate, and stop when
the brief requires approval. If no drift exists, do not ask architectural or product questions.

If pre-work is not complete, inspect the smallest relevant repository surface, identify decisions,
and stop for confirmation before implementation.

Before the first edit, state:

1. exact files to create or modify;
2. architectural layers touched;
3. unresolved decisions, or explicitly state that none remain.

Do not reopen locked decisions silently. If implementation reveals a real contradiction or missing
contract, stop and surface the specific evidence. Do not label ordinary signature confirmation as
"discovery."

Hard batch-size gate: no Claude implementation session may exceed three independent mutation
handler families, eight production files, or twelve total changed files including tests, fakes, and
documentation. Exact route aliases count as one family only when they delegate to the same handler
and service with no alias-specific behavior; enumerate every alias and cover the family with a
parameterized contract test. Count the actual caller/test fan-out before approval. A coherent
cross-cutting concern does not override these limits; split it into independently compiling vertical
slices. A very large file or broad test matrix requires a smaller slice. Start a fresh Claude session
after an approved commit instead of carrying discovery, review rounds, and the next batch in one
context window.

## Implementation Rules

- Preserve existing user changes and unrelated dirty-worktree content.
- Confirm every external method signature and relevant failure behavior before calling it.
- Do not invent types, methods, namespaces, or error contracts without checking that they exist.
- Owned-enum switches must be exhaustive and fail explicitly for unknown values unless a locked
  policy requires deny-all behavior.
- Preserve established guard ordering, stable errors, row authorization, and fail-closed behavior.
- Domain methods remain authoritative for domain validation; application metadata is advisory.
- Do not broaden scope into adjacent gaps, frontend work, migrations, or cleanup.

## Verification

- During implementation, run the smallest focused tests that exercise the changed behavior.
- Run broader unit/architecture/integration suites only at the batch completion gate or when a
  cross-cutting change warrants them.
- Use `--no-restore` and minimal verbosity when dependencies are already available.
- Summarize failures; do not paste repeated stack traces after the root cause is known.
- Before handoff, run `git diff --check`, inspect the final changed-file list, and self-review for
  policy drift, authorization expansion, direct-ID gaps, and missing regression tests.

## Git and Documentation

- Do not commit until Christian explicitly approves the reviewed diff.
- Update documentation surgically; do not rewrite an entire large document when one section changed.
- Record locked decisions in `docs/decisions/decision-index.md` and implementation history in the
  appropriate build log when the current batch requires it.
- Keep `docs/session-log.md` as the current execution brief: completed state, exact next batch,
  blockers, and verified test counts. Do not duplicate historical detail already preserved elsewhere.

## Architecture Boundaries

- Foundation must not reference Keep; Keep may reference Foundation.
- SharedKernel contains only shared primitives, not product or identity concepts.
- Application must not depend on Infrastructure.
- Core must not depend on Application or Infrastructure.
- Use the single `OpHalo.Api` host; do not reintroduce legacy `Signal`, `Continuity`, or `Platform`
  projects or namespaces.
- Product-specific user settings attach to `AccountUser`, never a duplicate identity.

## Commands

Claude may run targeted read-only inspection, builds, and tests. Christian runs interactive,
authenticated, deployment, and migration commands such as `dotnet ef`, cloud CLIs, and deploys.

_OpHalo — Quiet Intelligence. Clear Decisions._
