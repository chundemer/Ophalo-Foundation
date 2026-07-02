# Session Log — OpHalo Foundation

**Last updated:** 2026-07-02 (S14d complete; S14e invite accept and backend link wiring is next)
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
**Current slice:** S14e — Invite Accept And Backend Link Wiring
**Current slice status:** Next in `docs/build-log/068-session-14-ophalo-web-front-door.md`

Session 13 is complete and should be treated as historical context only. Completed Session 13 details
live in `docs/build-log/067-session-13-pwa-workbench.md`; do not carry Session 13 implementation
notes forward in this execution brief.

`web/ophalo-web` now has the full public content shell (S14b), auth entry forms (S14c), and
magic-link exchange (S14d): homepage, About, Pilot, Privacy, Terms, `/signin`, `/start`,
`/auth/check-email`, `/auth/exchange`, and `/auth/exchange/error`. S14d uses a direct credentialed
browser fetch to `OpHalo.Api` with a `useRef` double-fire guard; success redirects via
`window.location.assign(NEXT_PUBLIC_APP_BASE_URL)`. Typecheck/build/diff gates are clean.

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

### S14e Handoff Brief

Classify S14e as mechanical implementation preflight plus invite accept/backend link wiring.
Pre-work is complete; do not rediscover the S14 decisions. Use targeted checks only.

Intent: make invited operators/admins/viewers able to join a pilot account from their invite email,
and move invite links from `OperatorBaseUrl` to `PublicBaseUrl`.

Expected scope:

- `/invite/accept?token=...` page in `ophalo-web`;
- credentialed browser POST to `POST /accounts/invite/accept`;
- success redirect to `NEXT_PUBLIC_APP_BASE_URL`;
- bounded error states for missing/invalid/expired/already-active/seat-limit/session-failure;
- backend invite-link generation moves to `{PublicBaseUrl}/invite/accept?token=...`;
- fully retire `OperatorBaseUrl` from active settings/config/tests/runbooks.

Live API contract:

- `POST /accounts/invite/accept` body: `{ "token": "..." }`
- Browser success returns `200 OK`; the API sets `ophalo.sid` through normal browser response
  handling.
- Source of truth: `src/OpHalo.Api/Accounts/AccountEndpoints.cs` and invite services in
  `src/OpHalo.Foundation.Application`.

Error-state mapping:

- missing `?token=` -> show missing/invalid invite link with a CTA back to `/signin` or public home;
- `Invite.Expired` -> show expired invite copy;
- `Invite.InvalidToken` -> show invalid/already-used invite copy without exposing token detail;
- `Invite.AlreadyActive` -> redirect-oriented/sign-in copy;
- `Invite.SeatLimitReached` -> team limit reached copy;
- `Account.SessionCreationFailed` -> accepted-but-sign-in-failed copy with sign-in CTA;
- generic forbidden/server/network failure -> retry/support-oriented copy.

Backend/config targets:

- Replace invite-link generation from `{OperatorBaseUrl}/invite/accept?token=...` to
  `{PublicBaseUrl}/invite/accept?token=...`.
- Remove `OperatorBaseUrl` from `MagicLinkSettings`, checked-in app config, integration test
  factory config, and local runbooks.
- Update invite-link tests to assert `PublicBaseUrl`.

Likely S14e file impact:

- `web/ophalo-web/src/app/invite/accept/page.tsx`
- optional route-local client component under `web/ophalo-web/src/app/invite/accept/`
- `src/OpHalo.Foundation.Application/Auth/MagicLinkSettings.cs`
- invite-link generation in `src/OpHalo.Foundation.Application/Auth/SendInviteService.cs`
- invite-link generation in `src/OpHalo.Foundation.Application/Members/MemberManagementService.cs`
- checked-in API config files with `OperatorBaseUrl`
- integration test factory config and invite tests
- local setup/runbook docs that mention `OperatorBaseUrl`

S14e expected verification:

- `pnpm --filter ophalo-web typecheck`
- `pnpm --filter ophalo-web build`
- relevant API/integration tests for invite-link URL generation and invite accept behavior
- `git diff --check`
- local smoke with API + `ophalo-web`: open `/invite/accept` without a token and confirm bounded
  missing-token state; use a fresh real invite token only if available and do not paste raw token
  values into docs/logs.

Out of scope for S14e:

- public/customer intake (S14g);
- production deployment/DNS work.

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
