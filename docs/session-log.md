# Session Log — OpHalo Foundation

**Next-session pointer only.** Read this first, then the canonical [workboard](workboard.md) for
scope, sequencing, gates, and deferrals. Locked decisions are in
[decision-index](decisions/decision-index.md); completed-work evidence is in `docs/build-log/`.

**Updated 2026-09-24.** The foundation-first sequence is complete: GAP-100, DEF-037, the
maintainability/refactor review (items 1–8), and DEF-063 are all done. DEF-063's policy and
implementation evidence are recorded in workboard Next item 4 (`82254b20`, `c1e9a4ff`).

The `ophalo-app` security-maintenance preflight is **complete** (2026-09-24, `906667b4`, merged to
`main`): `pnpm audit` went from 14 advisories (6 high, 8 moderate) to 0. Direct-dep bumps `vitest`
^4.1.10→^4.1.11 and `postcss` ^8.4.0→^8.5.23; `pnpm-workspace.yaml` overrides pin the
transitive-only packages `nanoid` 3.3.18, `browserslist` 4.29.0, `baseline-browser-mapping`
2.11.25, `undici` 7.29.0 (compatible with `jsdom` 29.1.1's own range, so `jsdom`/Node are
unchanged). Verified clean: `pnpm typecheck`, `pnpm build`, full Vitest suite (143 files / 1,329
tests), and the `app.ophalo.com` Vercel preview (Node runtime confirmed compatible with the
overridden `undici`'s `>=20.18.1` requirement).

## Start here — `ophalo-web` Next 16.3 update

Implement the already preflighted public-site-only maintenance slice in
[BL151](build-log/151-ophalo-web-next-16-3-maintenance-preflight.md):

- Scope is only `web/ophalo-web` (public marketing/auth/intake site), not `web/ophalo-app`, mobile,
  or the API.
- Update Next `16.2.9` → `16.3.4`, add the Node `22.x` engine declaration, regenerate the local
  pnpm lockfile and `next-env.d.ts` if changed.
- Keep React/React DOM at `19.2.7`; do not combine React 19.3, an App Router/caching migration,
  `vercel.json`, or .NET pinning.
- Run the local build/typecheck and public-site smoke, then use Vercel preview acceptance as the
  founder-controlled deployment gate.

## Later, separate maintenance

BL151 Slice B remains a distinct, non-urgent .NET reproducibility decision: add root
`global.json` for SDK `10.0.301` (`rollForward: "latestFeature"`), then separately decide whether
the Docker SDK/runtime image tags remain floating or are pinned to a patch/digest. Do not batch it
with either web maintenance session.

## Pilot posture and release gate

The controlled parallel field pilot keeps the existing system authoritative for estimates,
invoices, payments, and accounting; Keep is the factual field record. See
[BL131](build-log/131-next-week-parallel-field-pilot-plan.md).

GAP-069 remains the release-readiness priority only when its trigger is reached: roughly two weeks
before Keep becomes the authoritative live record, after Railway Pro daily backups/PITR are enabled
and the first PITR recovery window exists. See
[authoritative-pilot-release-readiness.md](runbook/authoritative-pilot-release-readiness.md).

All other unstarted product work remains in the workboard Decision Queue or Deferred until a
business decision schedules it.
