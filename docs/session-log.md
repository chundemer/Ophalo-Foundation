# Session Log — OpHalo Foundation

**Last updated:** 2026-07-01 (S14b complete; S14c sign-in/start auth entry is next)
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
**Current slice:** S14c — Sign In, Start, And Check Email
**Current slice status:** Next in `docs/build-log/068-session-14-ophalo-web-front-door.md`

Session 13 is complete and should be treated as historical context only. Completed Session 13 details
live in `docs/build-log/067-session-13-pwa-workbench.md`; do not carry Session 13 implementation
notes forward in this execution brief.

`web/ophalo-web` now has the full public content shell from S14b: homepage, About, Pilot, Privacy,
Terms, shared SiteHeader/SiteFooter, marketing CSS, and brand assets. S14c adds the auth entry forms.

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
- `OperatorBaseUrl` is fully retired in S14e; invite links move to `{PublicBaseUrl}/invite/accept`.

### S14c Handoff Brief

Classify S14c as mechanical implementation preflight plus auth entry implementation. Do not
rediscover the S14 decisions; use targeted checks only.

Intent: implement the public owner/user auth entry loop.

Expected scope:

- `/signin` page with email field → `POST /auth/signin` to `OpHalo.Api`;
- `/start` page with name + email fields → `POST /auth/start` to `OpHalo.Api`;
- `/auth/check-email` confirmation page (no API call — static with email context);
- `Account.PilotFull` error handling on `/start`;
- neutral sign-in copy (works for both owner and invited user flows);
- browser time-zone detection and editable IANA time-zone field on `/start`;
- credentialed browser `fetch` directly to `NEXT_PUBLIC_API_BASE_URL`; no Next.js API routes or
  Server Actions;
- local dev support with `ConsoleEmailSender` already wired in `OpHalo.Api`.

Out of scope for S14c:

- magic-link exchange (`/auth/exchange`);
- invite accept (`/invite/accept`);
- public/customer intake page (required later in S14g before public pilot);
- backend invite-link changes;
- `ophalo-app` auth redirect changes;
- production deployment/DNS work.

S14c expected verification:

- `pnpm --filter ophalo-web typecheck`;
- `pnpm --filter ophalo-web build`;
- `git diff --check`;
- local end-to-end smoke: submit `/start`, confirm magic link appears in `OpHalo.Api` console log;
- `Account.PilotFull` error renders a clear "pilot is full" message on `/start`;
- no token/session leakage, no auth proxy layer.

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
