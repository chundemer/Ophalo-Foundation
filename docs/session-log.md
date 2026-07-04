# Session Log — OpHalo Foundation

**Last updated:** 2026-07-04 (S17e complete; S17f next)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** 939 unit · 14 arch · 713 integration = 1,666 total, 0 failures (1 pre-existing KeepG5 fluke excluded)
**Next free ADR:** ADR-406
**Current session:** Session 17 — Review-Safe Native Product Foundation

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

**Current build log:** `docs/build-log/071-session-17-review-safe-native-product-foundation.md`
**Last completed build log:** `docs/build-log/070-session-16-native-mobile-foundation.md` (Session 16)
**Readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Bug/gap tracker:** `docs/pilot-readiness-bug-tracker.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1
**Current session:** Session 17 — Review-Safe Native Product Foundation
**Current slice:** S17f — Quick Capture And Public Intake Share

### Completed Context

Completed implementation details live in the build logs and should not be repeated here:

- Session 13 — PWA workbench: `docs/build-log/067-session-13-pwa-workbench.md`
- Session 14 — web front door: `docs/build-log/068-session-14-ophalo-web-front-door.md`
- Session 15 — pilot readiness: `docs/build-log/069-session-15-pilot-readiness.md`
- Session 16 — native mobile foundation: `docs/build-log/070-session-16-native-mobile-foundation.md`

Session 16 completed the native mobile foundation, including the Expo app shell, secure token and
installation storage, mobile magic-link handoff, nullable push-token device registration, badge hook,
viewer/unknown mobile role gate, crypto UUID generation, and the S19 store-submission checklist.
Treat these as historical context unless a later discovery step finds a concrete gap.

### Current Direction

- S17 decisions are locked in build log 071 as ADR-396 through ADR-405. Treat the build log and
  decision index as the active S17 authority.
- S17a preflight is complete. Use the build-log 071 "S17a Preflight Findings" section as the S17b
  implementation brief for exact endpoint names, response fields, mobile routes, auth behavior,
  test coverage, conflict contracts, and public-intake gap scope.
- New backend endpoints are allowed only after explicit S17a gap evidence and documentation.
- Use `docs/pilot-readiness-bug-tracker.md` as the live source of bug/gap status.
- `ophalo-app` remains the authenticated Keep workbench.
- `mobile/ophalo-mobile` is a separate native deliverable and must remain aligned with Apple/Google
  review posture captured in `docs/mobile-store-submission-checklist.md`.
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

### S17 — S17e Complete; S17f Active

**Roadmap label:** Native Operator Field App.

**Status:** S17e complete. S17f is the active slice.

**S17a findings summary:**

- All S17 API endpoints confirmed in `OpHalo.Api`: lists (`view=assigned_to_me`, `view=watching`),
  `/keep/requests/available`, lookup, create, detail (with `AvailableActions`, `CurrentUserParticipation`,
  `Version`, `PageToken`, `NeedsShare`), business-updates, status, external-contact, share-intent,
  watch/unwatch, mute/unmute, responsible, follow-up-on, planned-for.
- `X-Keep-Request-Version` conflict contract confirmed: missing header → 400
  `KeepRequest.ExpectedVersionRequired`; invalid → 400 `KeepRequest.ExpectedVersionInvalid`; stale
  race → 409 `KeepRequest.RequestChanged`; already-claimed → 409
  `KeepRequest.ParticipationRequestAlreadyAssigned`. All errors are ProblemDetails JSON with `code`
  extension field. Mobile must JSON-parse error bodies to read `code`.
- Public intake gap confirmed: `GET /keep/setup/intake` requires `KeepSettingsManage` (Admin/Owner
  only). S17f ships Quick Capture only. Intake sharing is a separately approved pre-pilot gap slice.
- `signin.tsx` `__DEV__` paste field removed (S17c complete).
- My Work (My Promises / Watching) and Available lists wired with real cached queries; intentional loading/error/empty states; pull-to-refresh; `hasMore` indicator. Rows are read-only except detail navigation (S17d complete).
- Request detail read surface: header, description, attention/timing/participation fields, contact affordances and available actions as plain-text metadata, oldest-first event timeline. `useRequestDetail` hook with `enabled: !!id`; `version` retained for S17h/S17i mutations. No write controls, no S17g/h/i leakage (S17e complete).
- Brand guide alignment (canvas color, Keep teal moment, Inter/Source Serif 4 fonts) deferred to a follow-up UX pass before S17j.
- E2E handoff runbook recorded in build log 071.

**Hard boundaries (unchanged):**
- No real APNs/FCM or push-token capture (S18).
- No store-submission assets or Universal Links (S19).
- No demo mode, local-only reviewer bypass, or production-visible dev auth.
- No native account creation or invite acceptance.
- `Request Account Deletion` is config-gated by `EXPO_PUBLIC_ACCOUNT_DELETION_URL`: if missing,
  empty, invalid, non-HTTPS, or not an approved OpHalo deletion route, omit the UI row entirely.
- Support is config-gated by `EXPO_PUBLIC_SUPPORT_URL` unless a known existing support route is
  provided; do not link to an unverified `/support` route.
- Slice dependencies locked: S17b/S17c before S17d; S17e before S17f/S17g/S17h/S17i.

---

### Remaining Sessions

1. ~~**Session 16 — Native Mobile App Foundation** — Complete~~
2. **Session 17 — Review-Safe Native Product Foundation**
   Build the review-safe native Keep field workflow in locked slices S17a through S17j. Current
   active slice is S17f — Quick Capture And Public Intake Share.
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
