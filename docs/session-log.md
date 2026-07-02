# Session Log — OpHalo Foundation

**Last updated:** 2026-07-02 (S16b complete; mobile scaffold launches on iOS simulator)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** 939 unit · 14 arch · 713 integration = 1,666 total, 0 failures (1 pre-existing KeepG5 fluke excluded)
**Next free ADR:** ADR-395
**Current session:** Session 16 — Native Mobile App Foundation

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

**Current build log:** `docs/build-log/070-session-16-native-mobile-foundation.md`
**Last completed build log:** `docs/build-log/069-session-15-pilot-readiness.md` (Session 15)
**Readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Bug/gap tracker:** `docs/pilot-readiness-bug-tracker.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1
**Current session:** Session 16 — Native Mobile App Foundation
**Current slice:** S16c complete. Auth and secure client foundation in place. Next: S16d (backend
contract changes — clientHint, handoff-code exchange/redeem, nullable pushToken device registration,
EF migration).

### S16c — Complete

Added `expo-secure-store` (SDK 57). New files: `src/auth/secureStore.ts` (session token +
`appInstallationId` ops; UUID v4 generated once, never reset by re-auth), `src/api/client.ts`
(typed fetch wrapper; Bearer injection via SecureStore; dev-only method/path/status log — no token
material in any log output), `src/auth/AuthContext.tsx` (AuthProvider; bootstrap reads token →
`GET /auth/me` → setUser or clear; `storeToken` validates via `/auth/me` before writing to
SecureStore; `logout` never throws — `setUser(null)` guaranteed in `finally`, all API/storage ops
are best-effort `.catch()`), `app/signin.tsx` (email → `POST /auth/signin` with
`clientHint: "mobile"`; "Check your email" confirmation; dev-only token paste field for S16c
verification; errors shown via Alert), `app/auth/callback.tsx` (handles `ophalo://auth/callback?code=`
deep link; calls `POST /auth/mobile-handoff/redeem`; stores token on success; error screen on
failure — `redeem` never throws, all paths set phase state).

Modified: `app/_layout.tsx` (AuthProvider wraps navigator; blank view during bootstrap);
`app/(tabs)/_layout.tsx` (auth guard — `<Redirect href="/signin" />` when no user);
`app/(tabs)/account.tsx` (role + userId display; Sign out with Alert confirmation).

Error handling contract established: `logout` and `bootstrap` never throw; `storeToken` and
`redeem` propagate errors to callers which handle them explicitly.

Verification: TypeScript clean (`npx tsc --noEmit`). Manually verified: bearer token stored via dev
field → `GET /auth/me` succeeds → Requests tab rendered. `git diff --check` clean.

Session 13 is complete and should be treated as historical context only. Completed Session 13 details
live in `docs/build-log/067-session-13-pwa-workbench.md`; do not carry Session 13 implementation
notes forward in this execution brief.

Session 14 is complete and should be treated as historical context. `ophalo-web` now owns the public
front door and browser token pages: homepage, About, Pilot, Privacy, Terms, `/signin`, `/start`,
`/auth/check-email`, `/auth/exchange`, `/auth/exchange/error`, `/invite/accept`,
`/invite/accept/error`, and `/keep/intake/{token}`. Completed Session 14 detail lives in
`docs/build-log/068-session-14-ophalo-web-front-door.md`.

Session 15 is complete and should be treated as historical context. It closed active pilot-readiness
bugs/gaps in `docs/pilot-readiness-bug-tracker.md`, including the S15c customer tracker page at
`/keep/r/{pageToken}`. Completed Session 15 detail lives in
`docs/build-log/069-session-15-pilot-readiness.md`.

### Current Direction

- Session 16 should establish the native mobile app foundation before broader product-ops or
  reporting polish. Mobile and store approval are now the critical path.
- Use `docs/pilot-readiness-bug-tracker.md` as the live source of bug/gap status.
- `ophalo-app` remains the authenticated Keep workbench.
- The native app should be phone-first for operators and may share contracts/patterns with
  `ophalo-app`, but it is a separate mobile deliverable that must satisfy Apple/Google review.
- `OpHalo.Api` remains the only authority for auth, sessions, account creation, rate limiting, email,
  authorization, and persistence.
- Preserve fail-closed account, membership, action-policy, public-token, and concurrency behavior.
- Production topology: `ophalo.com`/`www.ophalo.com` -> `ophalo-web`,
  `app.ophalo.com` -> `ophalo-app`, `api.ophalo.com` -> `OpHalo.Api`.
- Local topology: `ophalo-web` `http://localhost:3000`, `ophalo-app` `http://localhost:5173`,
  `OpHalo.Api` `http://localhost:5092`.
- Pilot cap for production launch: `SignupDefaults:MaxPilotAccounts=15`.
- `OperatorBaseUrl` is retired from active settings/config/test factories/runbooks; invite links now
  use `{PublicBaseUrl}/invite/accept`.

### Remaining Sessions

1. **Session 16 — Native Mobile App Foundation**
   Create the mobile project, lock stack/build posture, configure bundle identifiers/environments,
   establish auth/session handling, API client conventions, app shell/navigation, secure storage,
   device registration, badge refresh hooks, and deep-link placeholders.
2. **Session 17 — Native Operator Field App**
   Build the phone-first operator workflow: My Work, Available, request detail, native Quick Capture,
   contact handoff, log contact, customer update, mark completed, self-assign/watch/mute, tracker
   sharing, refresh/resume behavior, and mobile-safe error states.
3. **Session 18 — Push Delivery And Deep Links**
   Decide and implement real APNs/FCM delivery for store-bound builds, or explicitly document a
   review-safe no-push product posture before any submission. Verify Demo/InternalTest suppression,
   payloads, badge behavior, and stable deep links end to end.
4. **Session 19 — Store Submission Readiness**
   Prepare Apple/Google approval: app names, icons, screenshots, privacy labels, permissions copy,
   signing/profiles, production environment config, demo credentials/account, TestFlight/internal
   testing builds, and review notes.
5. **Session 20 — Weekly Value Report / Founder Ops Readout**
   Build the founder/internal-only weekly report endpoint/read service that generates copy-pasteable
   Markdown/text for an account and reporting period. No Owner/Admin report UI or automated email in
   the first slice.
6. **Session 21 — Pilot Support Surfaces**
   Build authenticated Report Friction plus Pilot Updates/Help, with compact context, no client-side
   webhook secrets, no anonymous customer OpHalo feedback hook, and no production impersonation.
7. **Session 22 — Pilot QA And Go-Live Gate**
   Run full web/mobile/API/customer-page/deployment/support verification, including onboarding,
   Quick Capture, public intake, tracker sharing, attention/follow-up/status-check behavior,
   close/cancel, feedback review, Spam/Test, weekly reporting, support runbooks, notification posture,
   store-readiness evidence, and known limitations.

### S15c — Complete

Added `web/ophalo-web/src/app/keep/r/[pageToken]/page.tsx` (server component). Fetches
`GET /keep/r/{pageToken}` with `cache: "no-store"` and renders three states: unavailable (404/error),
expired (410), active (200 — status, description, event timeline). Token never rendered as copyable
text. Metadata: `robots noindex/nofollow`, `referrer no-referrer`. Added minimal tracker CSS to
`globals.css`. Customer message/feedback submission forms deferred to a future slice.

Verification: `pnpm -C web/ophalo-web typecheck` clean; `pnpm -C web/ophalo-web build` emits
`ƒ /keep/r/[pageToken]`, 0 errors.

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
