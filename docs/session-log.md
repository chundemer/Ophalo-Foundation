# Session Log — OpHalo Foundation

**Last updated:** 2026-07-09 (S22r2 complete — slug alias routing live, 7 integration tests green)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** 964 unit · 755 integration = 1,719 total, 0 failures (1 pre-existing KeepG5 fluke excluded)
**Next free ADR:** ADR-430
**Current session:** Session 22 — Day-Zero Settings Redesign, Intake Sharing, And Service Location Plan

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

**Current build log:** `docs/build-log/076-session-22-guided-setup-intake-and-service-location.md`
**Last completed build log:** `docs/build-log/075-session-21-attention-guidance-resolution-metadata.md` (draft handoff)
**Readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Bug/gap tracker:** `docs/pilot-readiness-bug-tracker.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1
**Current session:** Session 22 — Day-Zero Settings Redesign, Intake Sharing, And Service Location Plan
**Current slice:** S22r5 — Team tab and lightweight Getting Started

### Completed Context

Completed implementation details live in the build logs and should not be repeated here:

- Session 13 — PWA workbench: `docs/build-log/067-session-13-pwa-workbench.md`
- Session 14 — web front door: `docs/build-log/068-session-14-ophalo-web-front-door.md`
- Session 15 — pilot readiness: `docs/build-log/069-session-15-pilot-readiness.md`
- Session 16 — native mobile foundation: `docs/build-log/070-session-16-native-mobile-foundation.md`
- Session 19 — Keep workbench UX migration: `docs/build-log/073-session-19-keep-workbench-ux-migration.md` (S19a–S19d complete; S19e + Gate 3 sign-off deferred)
- Session 20 — mobile Owner/Admin control mode: `docs/build-log/074-session-20-mobile-owner-admin-control-mode.md`
- Session 21 — attention guidance resolution metadata: `docs/build-log/075-session-21-attention-guidance-resolution-metadata.md`

Session 16 completed the native mobile foundation, including the Expo app shell, secure token and
installation storage, mobile magic-link handoff, nullable push-token device registration, badge hook,
viewer/unknown mobile role gate, crypto UUID generation, and the S19 store-submission checklist.
Treat these as historical context unless a later discovery step finds a concrete gap.

### Current Direction

- S22 decisions and implementation slicing are captured in
  `docs/build-log/076-session-22-guided-setup-intake-and-service-location.md`.
- ADR-428 is now locked: Keep launches in a day-zero functional state. Settings is split into
  `Public Link & Profile`, `Response Policy`, and `Team`; Getting Started becomes lightweight
  verification/on-ramp, not a seven-step checklist.
- Treat Session 12 onboarding (`docs/build-log/066-session-12-account-settings-and-onboarding.md`)
  as the existing foundation to migrate, not as absent work.
- S22a preflight and S22b-backend are complete. See build log 076 for locked decisions and
  implementation archive.
- S22b delivered: `KeepSetupStep`, `IntendedTeamSize`, `KeepSetupDeferral`, `KeepBusinessSetupService`,
  `IKeepSetupDeferralPersistence`, `EfKeepSetupDeferralPersistence`, `keep_setup_deferrals` table,
  `GET /keep/setup/guided`, `POST /keep/setup/guided/defer/{step}`.
- S22r0 preflight complete (2026-07-09): dirty S22c files classified; file-level gate confirmed.
- S22r1 complete (2026-07-09): `SetupBar.tsx` deleted; `App.tsx` SetupBar wiring removed and route
  section type updated to `"public-profile" | "policy" | "team"`; `Home.tsx` GuidedHub and
  seven-step checklist stripped, replaced with lightweight three-card Getting Started stub;
  `Settings.tsx` converted from monolithic scroll to three-tab subnav (Public Link & Profile,
  Response Policy, Team), `OnboardingSection` removed from primary render. TypeScript clean.
- `apiClient.ts` additions from S22b (`KeepBusinessSetupResult`, `getGuidedSetup`,
  `deferSetupStep`) are kept — backed by live backend endpoints, needed for S22r5 Getting Started.
- S22r2 complete (2026-07-09): `KeepPublicIntakeSlugAlias` entity + EF migration
  (`keep_public_intake_slug_aliases`, partial unique index on active slug);
  `FindActivePublicIntakeLinkBySlugAsync` (current slug first, then alias fallback, input
  normalized to lowercase); `ExecuteWithLinkAsync` Phase B extracted; `ExecuteBySlugAsync` added;
  `POST /keep/public-intake/slug/{slug}` endpoint; `ophalo-web` `keep/s/[slug]/page.tsx` route;
  `IntakeForm.tsx` accepts `{ token?: string; slug?: string }`. 7 integration tests green.
- S22r4 complete (2026-07-09): `PolicySection` in `Settings.tsx` redesigned — stacked layout with
  plain-language helper copy for each field (First response, Standard response, Priority response,
  Status check); `min` tightened to 1 to match backend `> 0` constraint; intro paragraph reworded.
  TypeScript clean. No backend or test changes.
- S22r3 complete (2026-07-09): `KeepPublicIntakeLink.RenameSlug` (returns bool no-op indicator);
  `SlugExistsAsync` expanded to check active `KeepPublicIntakeSlugAlias` rows (hard pre-requisite);
  `KeepIntakeSetupService.RenameAsync` with diacritic-stripping `Slugify`, user-visible 422 on slug
  collision; `CommitRenameAsync` (transactional alias insert + slug update, race-condition catch);
  `PUT /keep/setup/intake/link-name`; `updateIntakeLinkName` in `apiClient.ts`; `IntakeSection`
  replaced with polished `PublicLinkSection`: durable copy/open via `VITE_PUBLIC_BASE_URL/keep/s/{slug}`,
  inline link-name editing (server slug returned, alias awareness copy), raw-token "shown once"
  banner preserved for ensure/replace, replace kept as destructive with warning, phone-sized customer
  preview. 19 unit tests + 29 integration tests (12 new rename tests) green.
- Do not continue showing `Create intake page` and `Share intake page` as separate owner chores.
  Public intake should be auto-provisioned by default, then verified/copied/previewed from Settings.
- Do not make `Build your team` feel mandatory. Team is available in Settings and reassuringly
  optional for solo shops.
- First redo target: Settings tabs/subnav. Build `Public Link & Profile` as the first Settings tab,
  but gate durable copy/open controls until slug-based public routing is implemented. Then split
  `Response Policy` with helper copy and `Team` as a clean roster.
- Slug-based public intake URLs are the chosen durable path. Do not use `window.location.origin` for
  customer-facing intake links from `ophalo-app`; use the configured public web base URL. Do not show
  copy/open as guaranteed durable until `ophalo-web`/API can resolve active intake links by
  `publicSlug`.
- ADR-429 is locked: ordinary public link-name edits preserve old shared slugs as aliases; replacement
  or regeneration is the destructive/security action and must warn that old shared links break. S22r2
  includes alias persistence/migration and slug-resolution tests.
- `seatUsage` sourced from `api.listMembers()` (`["members", false]` queryKey); fail-soft on error.
- `IntendedTeamSize` returns null from `GET /keep/setup/guided` until any future backend preference
  slice. It must never affect seat limits or entitlements. It is no longer required for the immediate
  redesign.

- S17 decisions are locked in build log 071 as ADR-396 through ADR-405. Treat S17 as historical
  implementation context unless a later S22/S20 preflight explicitly pulls a mobile dependency
  forward.
- S17a through S17j are complete. Use build-log 071 as the implementation archive, not the active
  handoff brief.
- New backend endpoints are allowed only after explicit S22 preflight gap evidence and documentation.
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

### S17 — Complete

**Roadmap label:** Native Operator Field App.

**Status:** All slices S17a–S17j complete. Session 17 done.

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
  only). S17f shipped Quick Capture only. Intake sharing is a separately approved pre-pilot gap slice.
- `signin.tsx` `__DEV__` paste field removed (S17c complete).
- My Work (My Promises / Watching) and Available lists wired with real cached queries; intentional loading/error/empty states; pull-to-refresh; `hasMore` indicator. Rows are read-only except detail navigation (S17d complete).
- Request detail read surface: header, description, attention/timing/participation fields, contact affordances and available actions as plain-text metadata, oldest-first event timeline. `useRequestDetail` hook with `enabled: !!id`; `version` retained for S17h/S17i mutations. No write controls, no S17g/h/i leakage (S17e complete).
- Quick Capture modal: phone input → lookup (`GET /keep/requests/lookup?phone=...`) → auto-fill known customer name/email; description + source picker (7 slugs, `public_intake` excluded); offline blocking via `useNetworkState` (`@react-native-community/netinfo`); `POST /keep/requests` authenticated; post-save `router.replace` to `requests/[id]`. No intake share UI (S17f complete).
- Contact handoff and tracker share: contact rows tappable via `Linking` (`tel:`/`mailto:`); phone outcome custom sheet (Spoke/Voicemail/No answer/Skip) logs `POST .../external-contact` with `X-Keep-Request-Version`; email requires one-tap confirm; tracker share via native share sheet + explicit post-share confirm before calling `POST .../share-intent` (`native_share`); "Mark as shared" explicit confirm sends `manual_mark_shared`; `pageToken` added to `KeepRequestDetailDto`; `EXPO_PUBLIC_PUBLIC_BASE_URL` documented; share-intent requires no version header (S17a table correction documented). ADR-401 compliant (S17g complete).
- Customer update and mark completed: composer gated on `canSendBusinessUpdate`; "Send Update" posts `{ message }`; "Send Update & Mark Completed" posts `{ message, setStatus: 'resolved' }` gated on `allowedStatuses.includes('resolved')`; both disabled when message empty/pending/offline; 409/`KeepRequest.RequestChanged` preserves text and shows conflict notice; network failure shows retry-safe copy; badge invalidated on success; composer omitted entirely when `canSendBusinessUpdate` false (S17h complete).
- Participation and timing controls: Watch/Unwatch/Mute/Unmute in detail screen Participation section (outline buttons, per-action error state); self-assign from Available list row and detail screen (`canAssignResponsible`; passes `user.accountUserId`; `ParticipationRequestAlreadyAssigned` → "Already claimed"); Follow Up On and Planned For set (YYYY-MM-DD `TextInput` with client-side calendar validation) and clear (`canSetFollowUpOn`/`canSetPlannedFor`); timing section renders when any of four timing flags are true; `ACTION_LABELS` reduced to still-future capabilities; ADR-403 offline guard applied to previously-unguarded sheet mutation buttons (S17i complete).
- Brand guide alignment (canvas color, Keep teal moment, Inter/Source Serif 4 fonts) deferred to a follow-up UX pass before S17j.
- E2E handoff runbook recorded in build log 071.

**S17j active brief: Review-Safe Final Pass**

- Scope is mobile-only. No backend changes unless a concrete gap is found during verification.
- No placeholder authenticated tabs or screens visible in the shipped app.
- No unexpected runtime permissions. `@react-native-community/netinfo` (passive connectivity) is the only expected non-standard permission. Verify Android manifest output has no spurious entries.
- Account screen: add Privacy, Support, and Request Account Deletion rows. Support row gated on `EXPO_PUBLIC_SUPPORT_URL` — omit if missing or empty. Request Account Deletion row gated on `EXPO_PUBLIC_ACCOUNT_DELETION_URL` — omit if missing, empty, invalid, non-HTTPS, or not an approved OpHalo deletion route.
- Route and deep-link guardrails: all authenticated routes protected; `ophalo://` scheme routes verified; `+not-found` fallback in place.
- Optional pre-S17j UX pass (brand alignment): `src/constants/brand.ts` token file, canvas/card color separation, Inter/Source Serif 4 fonts via `expo-google-fonts`, single teal moment per screen. If included, scope tightly and do not let it bleed into behavior.
- `npx tsc --noEmit` must pass clean at S17j completion.
- Manual workflow smoke notes recorded in build log 071 before handoff.

**Hard boundaries (unchanged):**
- No real APNs/FCM or push-token capture (S18).
- No store-submission assets or Universal Links (S19).
- No demo mode, local-only reviewer bypass, or production-visible dev auth.
- No native account creation or invite acceptance.
- `Request Account Deletion` is config-gated by `EXPO_PUBLIC_ACCOUNT_DELETION_URL`: if missing,
  empty, invalid, non-HTTPS, or not an approved OpHalo deletion route, omit the UI row entirely.
- Support is config-gated by `EXPO_PUBLIC_SUPPORT_URL` unless a known existing support route is
  provided; do not link to an unverified `/support` route.
- Slice dependencies locked: S17b/S17c before S17d; S17e before S17f/S17g/S17h/S17i/S17j.

---

### Remaining Sessions

1. ~~**Session 16 — Native Mobile App Foundation** — Complete~~
2. **Session 17 — Review-Safe Native Product Foundation**
   Build the review-safe native Keep field workflow in locked slices S17a through S17j. Current
   active slice is S17j — Review-Safe Final Pass.
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

## Deferred Decisions

- **Mobile package manager standardisation (S19 candidate):** `mobile/ophalo-mobile` was scaffolded
  with Expo's default npm and has used `package-lock.json` since S16b. `web/ophalo-app` uses
  `pnpm@11.9.0`. The monorepo root has no shared workspace lock file. Standardising mobile to pnpm
  would require deleting `package-lock.json`, adding `"packageManager": "pnpm@x.x.x"` to
  `mobile/ophalo-mobile/package.json`, regenerating `pnpm-lock.yaml`, and switching `expo install`
  calls to `pnpm exec expo install`. Low urgency — npm is consistent within the mobile project —
  but worth aligning before S19 EAS Build configuration introduces CI/CD package-manager assumptions.

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
