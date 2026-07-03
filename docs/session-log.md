# Session Log — OpHalo Foundation

**Last updated:** 2026-07-02 (S16e implemented; pending manual verification)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** 939 unit · 14 arch · 713 integration = 1,666 total, 0 failures (1 pre-existing KeepG5 fluke excluded)
**Next free ADR:** ADR-396
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
**Current slice:** S16e — Web Handoff and Mobile Device/Badge Hooks.

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

### S16d — Complete

Committed backend mobile auth and device contracts in `80a33a0`.

Implemented:
- `POST /auth/signin` accepts optional `clientHint: "mobile"` and issues magic links with
  `&from=mobile`.
- `POST /auth/exchange` with `clientType: "mobile_app"` consumes the magic-link code and returns a
  10-minute one-time `{ handoffCode, expiresAtUtc }`, never a raw bearer token.
- `POST /auth/mobile-handoff/redeem` consumes the handoff code atomically and returns
  `{ sessionToken, expiresAtUtc }` for a `SessionClientType.MobileApp` session.
- `PUT /me/devices/{appInstallationId}` accepts omitted/null `pushToken`; null-token devices are
  Active but push-ineligible, with null fingerprint/last-four and no token-rebinding revocation.

Schema: Christian generated migration `S16dMobileHandoffAndNullableDeviceToken`.

Verification recorded in commit: 53 focused integration tests passed across auth magic link, mobile
handoff, and device registration.

### S16d Follow-Up Tightening — Complete

Committed in `2639cc6`.

Implemented:
- Native V1 sign-in remains existing-member only.
- New account creation and invite acceptance remain web/PWA-owned in V1 (ADR-395).
- `POST /auth/exchange` with `clientType: "mobile_app"` returns
  `AuthCode.MobileNewAccountUnsupported` for `EntryContext.NewAccount` codes before provisioning or
  consuming the code.
- Focused tests cover the rejected mobile new-account exchange, no graph creation, browser reuse of
  the same code, and `clientHint: "mobile"` producing `from=mobile` in the emailed link.

### S16e — Current

**Classification:** Mechanical implementation preflight. Decisions are locked in build log 070 and
ADR-389/390/391/392/393/394/395. Do not rediscover the mobile stack, app identity, custom-scheme
posture, existing-member-only mobile sign-in, web-owned signup/invite acceptance, or S16/S17/S18/S19
split.

**Goal:** Complete the web-to-native auth handoff and initial mobile authenticated hooks:
- `ophalo-web` `/auth/exchange` detects `?from=mobile` and renders a sterile mobile handoff client
  instead of the normal browser exchange client.
- `MobileExchangeClient` calls the API `/auth/exchange` with `{ code, clientType: "mobile_app" }`
  and no browser credentials, receives `{ handoffCode, expiresAtUtc }`, and never renders or stores a
  raw bearer session token.
- The mobile handoff page exposes a user-clicked `ophalo://auth/callback?code={handoffCode}` action.
  No auto-open on page load, no third-party scripts, no analytics, no external store/TestFlight links.
- Mobile callback redeems the handoff code through the existing
  `POST /auth/mobile-handoff/redeem` path, stores the returned bearer token, and reaches the
  authenticated shell.
- After auth, the mobile app upserts `PUT /me/devices/{appInstallationId}` with `platform`,
  `appVersion`, and omitted/null `pushToken`.
- Mobile adds `GET /me/badge` refresh/polling hooks for foreground/resume behavior.

**Expected files/areas:** `web/ophalo-web/src/app/auth/exchange/*` or nearby auth-exchange client
files, mobile auth callback/context or post-auth bootstrap files, mobile API/device/badge hooks, and
focused tests/type checks. Present the exact file-level gate before editing.

**Hard boundaries:**
- No native signup and no native invite acceptance in S16e.
- No `POST /auth/start` or invite-accept mobile UX changes.
- No real APNs/FCM provider or push-token capture; that remains S18.
- No store-submission assets, EAS profiles, app icons/screenshots, Universal Links/App Links, or
  Apple/Google domain-association work; those remain S19.
- Do not expose raw bearer tokens in browser state, DOM text, custom-scheme URLs, logs, or
  `ophalo-web` response bodies.

**Implementation complete.** Changed files: `web/ophalo-web/src/app/auth/exchange/page.tsx` (added
`from` param routing), `web/ophalo-web/src/app/auth/exchange/MobileExchangeClient.tsx` (new —
sterile handoff page, `credentials: "omit"`, tap-only `ophalo://` link),
`mobile/ophalo-mobile/src/api/client.ts` (added `api.put`),
`mobile/ophalo-mobile/src/auth/AuthContext.tsx` (best-effort `upsertDevice` after `storeToken` and
bootstrap), `mobile/ophalo-mobile/src/hooks/useBadge.ts` (new — TanStack Query poll + AppState
foreground refetch), `mobile/ophalo-mobile/app/_layout.tsx` (QueryClientProvider, focusManager
wired to AppState), `mobile/ophalo-mobile/app/(tabs)/index.tsx` (badge count display),
`mobile/ophalo-mobile/package.json` + `package-lock.json` (`@tanstack/react-query@^5.101.2`).
TypeScript clean on both projects. Pending Christian's manual verification below before commit.

**S16e manual verification steps (do in order):**

*Prerequisites — start all three services:*
1. Start the API: `dotnet run --project src/OpHalo.Api` (confirm on `http://localhost:5092`)
2. Start ophalo-web: `cd web/ophalo-web && npm run dev` (confirm on `http://localhost:3000`)
3. Start the iOS simulator: `cd mobile/ophalo-mobile && npx expo run:ios`

*Step 1 — Confirm `?from=mobile` routing:*
4. Open `http://localhost:3000/auth/exchange?code=FAKE&from=mobile` in a desktop browser.
   Expected: "Authorizing device…" then inline error — not a redirect to `/auth/exchange/error`.
5. Open `http://localhost:3000/auth/exchange?code=FAKE` (no `from`).
   Expected: "Signing you in…" from the existing ExchangeClient — confirms routing split is clean.
6. In DevTools → Network, find the `POST /auth/exchange` from step 4. Confirm no `Cookie` header
   (credentials: "omit" in effect).

*Step 2 — Full mobile sign-in handoff flow:*
7. In the simulator, go to sign-in screen. Enter email, tap "Send Magic Link".
8. Watch the API terminal — the magic link URL prints to stderr (no Resend key needed locally).
   Confirm it contains `&from=mobile`.
9. Copy the link from the terminal and open it in a desktop browser (Safari or Chrome on your Mac).
   Expected: "Authorizing device…" → "Device Authorized — Tap the button below to open OpHalo Keep"
   with one "Open Keep Mobile App" link and fallback copy below it.
10. Click "Open Keep Mobile App". Simulator should intercept `ophalo://auth/callback?code=...`,
    show "Signing you in…" briefly, then land on the authenticated Requests screen.
11. Confirm no raw bearer token appears in the browser URL bar, DOM, or browser history — only the
    short-lived handoff code.

*Step 3 — Device upsert (sign-in path):*
12. In the API terminal, find `PUT /me/devices/{appInstallationId}` fired after step 10.
    Confirm `platform` and `appVersion` present, `pushToken` null/absent.
    Or query: `SELECT * FROM account_user_devices ORDER BY created_at DESC LIMIT 1`.

*Step 4 — Badge polling and foreground refetch:*
13. On the Requests screen, watch API logs — `GET /me/badge` should fire within ~5 seconds.
14. Background the app (press Home), wait ~15 seconds, foreground it.
    Expected: `GET /me/badge` fires again on return (AppState → focusManager wiring).

*Step 5 — Device upsert (bootstrap path):*
15. Fully kill the app in the simulator (swipe up to close). Relaunch it.
    Expected: lands straight on authenticated Requests screen.
    `PUT /me/devices/{appInstallationId}` appears in API logs on bootstrap — same installId as step 12.

*Step 6 — Error states:*
16. Re-open the same magic link URL from step 9 in a new tab (already consumed).
    Expected: inline error state in the browser — not a redirect.
17. Copy the `ophalo://auth/callback?code=...` URL from step 10 (already redeemed), paste into
    Safari address bar. Expected: app opens callback screen and shows expired/used error.

*Step 7 — Final diff check:*
18. `git diff --stat HEAD` — confirm exactly the 9 S16e files.
19. `cd web/ophalo-web && npx tsc --noEmit` — zero errors.
20. `cd mobile/ophalo-mobile && npx tsc --noEmit` — zero errors.

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
