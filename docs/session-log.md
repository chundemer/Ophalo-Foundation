# Session Log — OpHalo Foundation

**Last updated:** 2026-06-28 (S13a complete; S13b next)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** 939 unit · 14 arch · 705 integration = 1,658 total, 0 failures (1 pre-existing KeepG5 fluke excluded)
**Next free ADR:** ADR-385
**Current session:** Session 13b — PWA Workbench Next Slice

---

## Session Protocol

This file is the current execution brief, not the historical build archive. Completed implementation
detail lives in `docs/build-log/`; authoritative decisions live in
`docs/decisions/decision-index.md`.

For every implementation slice:

- Classify the work explicitly: discovery when pre-work is incomplete; mechanical implementation
  preflight when the current brief is marked pre-work complete.
- Use targeted `rg` during preflight to confirm named signatures and compile-impact callers. Do not
  rediscover already-locked architecture, scope, tests, or decisions.
- Inspect current signatures, endpoint/persistence patterns, failure modes, and tests before editing.
- Present the file-level gate before writing.
- Keep the hard slice gate unless explicitly split: at most 3 mutation families, 8 production files,
  and 12 total changed files including tests/docs.
- Preserve fail-closed account, row, action, membership, and public-token behavior.
- Add focused authorization/regression tests and run the proportionate broader suite.
- Self-review for policy drift, accidental visibility expansion, token leakage, untested direct-ID
  paths, stale docs, and unrelated scope.
- Commit only after Christian approves the completed diff.

---

## Current Work

**Current build log:** `docs/build-log/067-session-13-pwa-workbench.md`
**Last completed build log:** `docs/build-log/067-session-13-pwa-workbench.md` (S13a)
**Readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1
**Current session:** Session 13b — PWA Workbench Next Slice

**Prior session context:** S13a is complete. Details live in
`docs/build-log/067-session-13-pwa-workbench.md`. The next slice may rely on the standalone
`web/ophalo-app` scaffold, credentialed typed API client, auth guard, onboarding home, local CORS,
dev console email fallback, self-hosted fonts, and `docs/runbook/local-web-setup.md`.

**S13a status: complete.**

- `web/ophalo-app` scaffolded: Vite + React + TypeScript + Tailwind CSS, pnpm, `pnpm-workspace.yaml`.
- Self-hosted Inter Variable and Source Serif 4 Variable fonts copied to `public/fonts/` via
  `scripts/copy-fonts.mjs` (postinstall); preloaded from `index.html`; `@font-face` in `app.css`.
- Typed fetch client (`src/lib/apiClient.ts`) — `credentials: "include"`, throws `ApiError` on non-2xx.
- `AuthGuard` component — queries `GET /auth/me`, redirects to `{VITE_PUBLIC_BASE_URL}/auth/signin?return_to=<path>` on 401; `return_to` is path-only (no origin).
- `Home` page — queries `GET /keep/setup/onboarding`: 200 → checklist, 403 → access-limited, 402 → commercial-block, other → generic error.
- `AccessLimited` component — shown on 403 for non-Owner/Admin users.
- Minimal PWA manifest (`public/manifest.webmanifest`).
- API: CORS added — explicit origins via `Cors:AllowedOrigins`, `AllowCredentials()`, `UseCors` before auth middleware.
- API: `ConsoleEmailSender` — dev-only fallback when Resend key absent; writes magic-link URL to stderr, not structured logs.
- API config: `App:AppBaseUrl` and `Cors:AllowedOrigins` added to `appsettings.json`; dev defaults in `appsettings.Development.json`.
- Runbook: `docs/runbook/local-web-setup.md`.
- Verification: 939 unit · 14 arch · 705 integration (1 pre-existing KeepG5 fluke), 0 new failures; `pnpm typecheck` clean; `pnpm build` succeeds.

**S13b next: decision/preflight needed.**

- Use `docs/build-log/067-session-13-pwa-workbench.md` as the Session 13 umbrella plan. It now
  breaks Session 13 into proposed coding slices S13a-S13i with scope, live contracts, out-of-scope
  boundaries, and pre-code questions.
- Choose one narrow PWA workbench vertical before implementation. Strong candidate: S13b Quick
  Capture, because `DEF-076` marks business-first request capture UI as a V1 must-have and the
  backend `POST /keep/requests` contract already exists.
- Keep S13a foundation boundaries: `ophalo-app` remains a static Vite client over `OpHalo.Api`;
  fetch remains credentialed and throws typed errors; localhost uses host-only cookies; no fake data
  or placeholder nav for unbuilt workflows.
- Before coding S13b, answer the S13b questions in build-log 067 and prepare a file-level gate,
  role/access behavior, done gate, and screenshot/browser verification target.

---

## Carry-Forward Boundaries

- Real APNs/FCM provider implementation remains future work.
- Demo scenario packs, demo reset UI, and admin/internal classification management remain deferred.
- Classification is operational/reporting/safety posture, separate from commercial lifecycle.
- Public signup cannot create Demo/InternalTest accounts.
- Production push delivery must stay suppressed for Demo/InternalTest accounts.
- Keep sends no backend SMS/email to customers in V1; native `sms:`, `tel:`, and `mailto:` handoff
  remains operator-initiated on the user's device.

---

## Operational Watch-Outs

- GitHub remote `origin` is configured; push local commits daily when green.
- Integration tests reset PostgreSQL schema and run migrations.
- Testing environment intentionally skips runtime rate limiting; production-like proof exists in
  `RateLimitTesting` (G8a/S7b).
- Deployment still requires correct Cloudflare/Railway trusted-proxy and token-redaction
  configuration even though application-level proofs are complete.
- Persistent local PostgreSQL setup/migration/smoke runbook is verified against local `ophalo_local`
  in Docker; guarded reset remains documented but was not exercised.
