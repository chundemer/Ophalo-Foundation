# BL151 — `ophalo-web` Next 16.3 maintenance and build reproducibility

**Status:** Implementation-ready; no application code changed.
**Date:** 2026-09-10
**Scope:** The isolated public web app at `web/ophalo-web`. This is not GAP-073 work and does not
change `web/ophalo-app` (Vite) or the .NET API.

## Baseline confirmed

- `web/ophalo-web/package.json` pins `next` at `16.2.9`, with `react` and `react-dom` at `19.2.7`.
- The requested target is `next` `16.3.4`, a same-major minor/patch update. It does not authorize
  an App Router, caching, or other architecture migration.
- `next.config.ts` contains only the `/guides/:path*` `X-Robots-Tag` header rule. Release notes
  must still be skimmed at implementation time for option deprecations/renames.
- `next-env.d.ts` is generated and currently reflects local `.next/dev` route types; regenerate and
  commit its resulting correction in the same change rather than hand-editing it.
- `ophalo-web` has its own `pnpm-lock.yaml`, no `engines.node` field, no `.nvmrc`, no `vercel.json`,
  and no repository CI workflow. Its deploy gate is therefore the Vercel preview/production flow
  governed by `CLAUDE.md` and the founder.
- All 12 .NET projects target `net10.0`; local SDK `10.0.301` is installed, while the root
  Dockerfile uses floating `mcr.microsoft.com/dotnet/sdk:10.0` and `aspnet:10.0` tags. There is no
  `global.json`.

## Locked scope

### Slice A — Next maintenance (implement next)

1. Change only `next` from `16.2.9` to `16.3.4` in `web/ophalo-web/package.json`.
2. Add `"engines": { "node": "22.x" }` to that package manifest so local and Vercel builds have
   an explicit Node major. Do not add a second pin mechanism unless a later operational decision
   specifically prefers `.nvmrc`.
3. Run the workspace-local `pnpm install` to update `web/ophalo-web/pnpm-lock.yaml`; do not edit the
   lockfile by hand.
4. Run the web build so `next-env.d.ts` is regenerated, then include the generated change if one
   remains. It is an expected companion artifact, not a fourth independent behavior change.
5. Read the Next 16.3 release notes and re-check `next.config.ts`; record any required option
   adjustment in the delivery record. If an option change expands scope beyond this routine bump,
   stop and make a separately approved slice.

`react` and `react-dom` deliberately remain at `19.2.7` in this slice. Updating them to `19.3.0`
is compatible-looking but optional and should not be combined with the Next verification surface
without an explicit follow-up approval.

Expected changed files: `web/ophalo-web/package.json`, `web/ophalo-web/pnpm-lock.yaml`, and,
if regenerated differently, `web/ophalo-web/next-env.d.ts` — three production files, one isolated
maintenance batch. Commit message: `chore(ophalo-web): update next to 16.3.4`.

### Slice B — .NET SDK/container reproducibility (separate, non-urgent)

Create a later, independent maintenance slice to add root `global.json` pinning SDK `10.0.301` with
`rollForward: "latestFeature"`, then decide whether Docker should remain on supported floating
`10.0` tags or be pinned to a matching patch/digest. It requires a build/container verification
plan and is not a prerequisite for Slice A. No .NET framework upgrade is pending; `net10.0` remains
the intended target.

## Verification and release handoff

Engineering verifies locally:

- `pnpm build` succeeds in `web/ophalo-web`.
- `pnpm typecheck` succeeds in `web/ophalo-web`.
- Quick visual smoke: public home/marketing, sign-in/start, public request route, and guide/static
  header behavior as applicable to the local configured environment.
- `git diff --check`, and inspect the generated `next-env.d.ts` diff before committing.

Founder/platform verifies the actual deployment path:

1. Push the branch and wait for Vercel's preview deployment.
2. Click through the preview URL; this is the release gate, not the local build alone.
3. Merge to the configured deploy branch only after preview acceptance; Vercel then builds
   production.
4. Smoke the live site. If regression is found, roll back from Vercel to the previous deployment.

No Railway/API deployment, API lockfile, or .NET project is touched by Slice A.

## Non-goals / escalation

- No React 19.3.0 bump without separate approval.
- No Next major upgrade, App Router conversion, caching migration, config redesign, or `vercel.json`.
- No production push, Vercel dashboard action, or rollback is performed by this repo slice; those
  remain founder-controlled under `CLAUDE.md`.
- If install/build demands a Node major other than 22 or exposes a Next 16.3 breaking change, stop
  and record the finding before changing runtime/configuration scope.
