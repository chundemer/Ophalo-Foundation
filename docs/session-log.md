# Session Log — OpHalo Foundation

**Last updated:** 2026-07-02 (S14e complete; S14f app redirect and verification is next)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** 939 unit · 14 arch · 713 integration = 1,666 total, 0 failures (1 pre-existing KeepG5 fluke excluded)
**Next free ADR:** ADR-385
**Current session:** Session 14 — `ophalo-web` public/front-door foundation

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

**Current build log:** `docs/build-log/068-session-14-ophalo-web-front-door.md`
**Last completed build log:** `docs/build-log/067-session-13-pwa-workbench.md` (Session 13 Verify)
**Readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1
**Current session:** Session 14 — `ophalo-web` public/front-door foundation
**Current slice:** S14f — App Redirect, Verification, Production Config, And Runbook
**Current slice status:** Next in `docs/build-log/068-session-14-ophalo-web-front-door.md`

Session 13 is complete and should be treated as historical context only. Completed Session 13 details
live in `docs/build-log/067-session-13-pwa-workbench.md`; do not carry Session 13 implementation
notes forward in this execution brief.

`web/ophalo-web` now has the public content shell and browser auth/token landing pages from S14b
through S14e: homepage, About, Pilot, Privacy, Terms, `/signin`, `/start`, `/auth/check-email`,
`/auth/exchange`, `/auth/exchange/error`, `/invite/accept`, and `/invite/accept/error`. Magic-link
exchange and invite accept both use direct credentialed browser fetches to `OpHalo.Api` with
`useRef` double-fire guards; successful token exchange redirects via
`window.location.assign(NEXT_PUBLIC_APP_BASE_URL)`.

### S14 Locked Direction

- `ophalo-web` is the public/front-door surface: homepage, About, Pilot, signin/start auth entry,
  check-email, magic-link exchange, invite accept, Privacy, and Terms.
- `ophalo-app` remains the authenticated Keep workbench.
- `OpHalo.Api` remains the only authority for auth, sessions, account creation, rate limiting, email,
  authorization, and persistence.
- Public auth calls use credentialed browser fetches directly to `OpHalo.Api`; no Next.js API routes
  or Server Actions for S14 auth flows.
- Public/customer intake is required before public pilot launch and is now pulled into Session 14 as
  a later S14g slice. S14b copy should not imply customer-submitted intake is live until that slice
  lands.
- Production topology: `ophalo.com`/`www.ophalo.com` -> `ophalo-web`,
  `app.ophalo.com` -> `ophalo-app`, `api.ophalo.com` -> `OpHalo.Api`.
- Local topology: `ophalo-web` `http://localhost:3000`, `ophalo-app` `http://localhost:5173`,
  `OpHalo.Api` `http://localhost:5092`.
- Pilot cap for production launch: `SignupDefaults:MaxPilotAccounts=15`.
- `OperatorBaseUrl` is retired from active settings/config/test factories/runbooks; invite links now
  use `{PublicBaseUrl}/invite/accept`.

### S14f Handoff Brief

Classify S14f as mechanical implementation preflight plus verification/runbook cleanup. Pre-work is
complete; do not rediscover the S14 decisions. Use targeted checks only.

Intent: prove the public front door works with the existing backend topology and route unauthenticated
`ophalo-app` users to the new public sign-in page.

Expected scope:

- update `ophalo-app` unauthenticated redirect from `{PublicBaseUrl}/auth/signin?return_to=...` to
  `{PublicBaseUrl}/signin`;
- do not append `return_to` in S14f;
- run `ophalo-web` typecheck/build;
- run relevant API/integration tests for auth exchange, invite accept, and invite-link URL generation;
- browser-verify the S14 route set at desktop and mobile widths:
  `/`, `/about`, `/pilot`, `/signin`, `/start`, `/auth/check-email`, `/auth/exchange`,
  `/invite/accept`, and handoff into `ophalo-app`;
- update local setup/runbook docs for the three-server local topology;
- add/confirm production config checklist for public pilot.

Post-S14e review cleanup already applied in the working tree:

- The stale invite-link integration assertion and test name from the retired operator-host posture
  have been updated to assert the configured `App:PublicBaseUrl` invite accept URL before
  integration verification.

Likely S14f file impact:

- `web/ophalo-app/src/components/AuthGuard.tsx`
- `docs/runbook/local-web-setup.md`
- `docs/deployment/local-postgres-runbook.md` only if the three-server startup/config checklist is
  incomplete after S14e
- `docs/build-log/068-session-14-ophalo-web-front-door.md`
- possibly `tests/OpHalo.IntegrationTests/Api/InviteTests.cs` for the stale public invite-link
  assertion described above

S14f expected verification:

- `pnpm --filter ophalo-web typecheck`
- `pnpm --filter ophalo-web build`
- proportionate `ophalo-app` check for the AuthGuard change
- relevant API/integration tests for auth exchange, invite accept, and invite-link URL generation
- browser smoke for public routes and app redirect, using fresh magic/invite links only when needed
- `git diff --check`

Out of scope for S14f:

- public/customer intake (S14g);
- customer tracker page (`/keep/r/...`);
- production deployment/DNS changes, beyond documenting the checklist.

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
- Deployment still requires correct Cloudflare/Vercel/Railway topology, trusted-proxy posture, and
  token-redaction configuration even though application-level proofs are complete.
- Persistent local PostgreSQL setup/migration/smoke runbook is verified against local `ophalo_local`
  in Docker; guarded reset remains documented but was not exercised.
