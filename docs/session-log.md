# Session Log — OpHalo Foundation

**Last updated:** 2026-07-10 (S22p1 complete — intake form UI polish: Source Serif H1, no dividers, description helper, iOS zoom guard, tap target)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** Full suite green — 986 unit · 786 integration = 1,772 total, 0 failures (1 pre-existing KeepG5 fluke excluded)
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
**Last completed prior-session build log:** `docs/build-log/075-session-21-attention-guidance-resolution-metadata.md`
**Readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Bug/gap tracker:** `docs/pilot-readiness-bug-tracker.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1
**Current session:** Session 22 — Day-Zero Settings Redesign, Intake Sharing, And Service Location Plan
**Current slice:** S22p2 — Urgency field (next slice, own session)

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
- S22r3 complete (2026-07-09): `KeepPublicIntakeLink.RenameSlug` (returns bool no-op indicator);
  `SlugExistsAsync` expanded to check active `KeepPublicIntakeSlugAlias` rows (hard pre-requisite);
  `KeepIntakeSetupService.RenameAsync` with diacritic-stripping `Slugify`, user-visible 422 on slug
  collision; `CommitRenameAsync` (transactional alias insert + slug update, race-condition catch);
  `PUT /keep/setup/intake/link-name`; `updateIntakeLinkName` in `apiClient.ts`; `IntakeSection`
  replaced with polished `PublicLinkSection`: durable copy/open via `VITE_PUBLIC_BASE_URL/keep/s/{slug}`,
  inline link-name editing (server slug returned, alias awareness copy), raw-token "shown once"
  banner preserved for ensure/replace, replace kept as destructive with warning, phone-sized customer
  preview. 19 unit tests + 29 integration tests (12 new rename tests) green.
- S22r4 complete (2026-07-09): `PolicySection` in `Settings.tsx` redesigned — stacked layout with
  plain-language helper copy for each field (First response, Standard response, Priority response,
  Status check); `min` tightened to 1 to match backend `> 0` constraint; intro paragraph reworded.
  TypeScript clean. No backend or test changes.
- S22r5 complete (2026-07-09): `TeamSection` intro copy updated to reassure solo owners ("Keep works
  great for solo businesses — no team required"); empty state changed from bare "No team members." to
  "Just you for now — use the form above to invite someone when you're ready." `Home.tsx` three-card
  Getting Started confirmed complete from S22r1 (verify public link, Quick Capture, invite teammates
  with explicit solo-optional framing). No backend changes. TypeScript clean.
- S22r6 complete (2026-07-09): Backend auto-provision hardening. `GET /keep/setup/intake` now
  calls `GetOrEnsureStatusAsync` — auto-provisions one active public intake link on first eligible
  Owner/Admin read using the same idempotent ensure loop (slug generation, collision-safe unique
  index, concurrent race handled). RawToken never returned from GET. Fallback slug "business" when
  businessName is non-slugifiable. `POST /keep/setup/intake/ensure` and replace remain unchanged.
  3 production files changed, 33/33 integration tests green (4 new tests: auto-provision,
  fallback slug, idempotency, concurrent GET).
- S22e complete (2026-07-10): Business identity header on intake form.
  `GET /keep/public-intake/token/{token}/info` and `GET /keep/public-intake/slug/{slug}/info` → `{ businessName }`
  (anonymous, rate-limited, 404 for unknown/revoked). `GetBusinessNameByTokenHashAsync` + `GetBusinessNameBySlugAsync`
  added to `IKeepIntakePersistence` and implemented in `KeepIntakePersistence` (reuses existing slug alias resolution).
  `GetInfoByTokenAsync` + `GetInfoBySlugAsync` added to `CreateKeepPublicIntakeService`. Both intake page routes
  (`[token]/page.tsx`, `s/[slug]/page.tsx`) now fetch info server-side and pass `businessName` to `IntakeForm`.
  `IntakeForm` renders business initials avatar + name header (matching customer tracker page) when name is present;
  falls back to generic "Submit a request" card. Footer corrected: brand logo + product description replacing
  inline SVG stand-in and internal tagline. 5 new info endpoint integration tests. S22d regression fixes:
  12 existing intake submission tests across `KeepIntakeApiTests`, `KeepIntakeSetupApiTests`, `KeepOffSeasonTests`
  updated to include service location fields (S22d made these required but targeted-suite-only verification missed them).
  `FakeIntakePersistence` in unit tests updated for new interface methods. Full suite: 986 unit + 786 integration.
- S22d complete (2026-07-10): Service location backend + intake page visual shell. Migration:
  `20260710095627_AddServiceLocationToKeepRequest` (5 nullable columns on `keep_requests`). Inline
  US 50-state + DC validation in `CreateKeepPublicIntakeService`; 4 error constants; DTO, command,
  handlers, EF config updated; 4 new service location errors mapped to 422 in `ErrorHttpMapper`.
  `IntakeForm.tsx` rewritten: customer-tracker canvas/card/footer shell, service location card + US
  state dropdown + privacy helper copy, spec-accurate success state (View your request page / Save
  this link / bookmark tip). 59/59 unit + 16/16 integration green (8 new unit, 6 new integration
  including privacy no-leak test on customer page). TypeScript clean.
  Deferred to S22e: `GET /keep/public-intake/slug/{slug}/info` → business name; business identity
  header + initials on intake form matching customer tracker page.
- Settings refactor complete (2026-07-09): `Settings.tsx` split into per-tab section files with no
  behavior changes — `settings/CompanySection.tsx`, `settings/PolicySection.tsx`,
  `settings/PublicLinkSection.tsx` (formerly `IntakeSection`), `settings/TeamSection.tsx` (includes
  `MemberRow`, `InviteForm`, shared helpers). `Settings.tsx` retained as ~75-line shell.
- Do not continue showing `Create intake page` and `Share intake page` as separate owner chores.
  Public intake should be auto-provisioned by default, then verified/copied/previewed from Settings.
- Do not make `Build your team` feel mandatory. Team is available in Settings and reassuringly
  optional for solo shops.
- Slug-based public intake URLs are the chosen durable path. Do not use `window.location.origin` for
  customer-facing intake links from `ophalo-app`; use the configured public web base URL.
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

### Next Session Brief

S22e complete. Current work: **S22p1 — Intake Form UI Polish** (in progress).

#### S22p1 — Intake Form UI Polish ✓ complete (2026-07-10)
`IntakeForm.tsx` only. No backend, no migration, no test changes.
- Main form H1: `font-serif text-2xl font-semibold leading-tight text-foreground sm:text-[28px]`
- Terminal H1s (`Request submitted.`, `This link is not available.`, `You're signed in…`): `font-serif` added, size unchanged
- Removed all 4x `border-t border-[var(--ophalo-border)] pt-5` dividers; sections wrapped in `<div className="mt-6 space-y-7">` with `<section>` children; submit area uses `mt-6` only
- Description helper text: "Include what happened, when it started, and anything urgent."
- State `<select>`: `text-base` added (iOS Safari zoom guard)
- Submit `<KeepButton>`: `min-h-[42px]` (minimum tap target)

#### S22p2 — Urgency Field (next slice, own session, full backend slice)
New `IntakeUrgency` enum (Routine/Soon/Urgent), default Routine. Piped through intake submission
and surfaced to operator. Conservative model: no auto-conversion to verified attention condition.
Backend files: `KeepRequest.cs`, new `IntakeUrgency.cs`, `CreateKeepPublicIntakeCommand.cs`,
`CreateKeepPublicIntakeService.cs`, `PublicIntakeRequest.cs`, EF config + migration.
Frontend: urgency select in `IntakeForm.tsx` after description, with urgent helper copy and quiet
safety disclaimer. Operator copy: "Customer marked this urgent." Scope operator display separately
if it would push past the batch gate.

#### S22p3 — Preferred Contact Method (own session, after S22p2)
New `ContactPreference` enum (NoPreference/TextMessage/PhoneCall/Email) on `KeepRequest`. Same
vertical depth as S22p2. Frontend: select in contact section; if Email selected, email field becomes
visible and `required`. Operator display: show near customer contact details.

#### S22p4 — Staff Auth Early Block (deferred)
No session context is available on the public Next.js intake page at load time without an
authenticated API call. Post-submit `staff_not_permitted` guard (line 159 of `IntakeForm.tsx`)
remains the correct defensive fallback. Do not block S22p1–S22p3 on this. If pursued, requires a
new lightweight `GET /keep/public-intake/session-check` endpoint and a `useEffect` call in
`IntakeForm` — document as a follow-up backend/UI slice.

#### Remaining S22 slices (unchanged)
1. **Customer page copy alignment (lightweight discovery):** Inspect `CustomerTrackerView.tsx` for
   any capability hint, cancellation copy, or label misalignment introduced by S22 decisions. Expected
   to be copy-only or a no-op; do not assume changes are needed before looking.
2. **S22f — Mobile Carry-Forward Preflight:** See build log 076 for scope (service-location fields
   mobile must consume, active-intake share utility feasibility, no new mobile settings/admin scope).
3. **S22g — Documentation Reconciliation:** ADR-295, ADR-375, ADR-383 and response policy placement.
4. Docs/index reconciliation.

Historical mobile context lives in `docs/build-log/071-session-17-review-safe-native-product-foundation.md`.

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
