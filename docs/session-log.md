# Session Log — OpHalo Foundation

**Last updated:** 2026-06-30 (S13g-3 committed; Session 13g complete)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** 939 unit · 14 arch · 713 integration = 1,666 total, 0 failures (1 pre-existing KeepG5 fluke excluded)
**Next free ADR:** ADR-385
**Current session:** Session 13g — Member Management And Settings Continuation

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
**Last completed build log:** `docs/build-log/067-session-13-pwa-workbench.md` (S13g-2)
**Readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1
**Current session:** Session 13g — Member Management And Settings Continuation

**Committed baseline (local, not yet pushed):**

- `6fc80e1 feat: expose share intent action metadata`
- `c9bde88 feat: add session 13d-1 request detail read view and share clearing`
- `88f8759 feat: add session 13d-2 status change workflow`
- `9ac0be9 feat: add session 13d-3 operational action rail`
- `50f4d17 docs: record S13d-3 commit hash in session log`
- `702c08f feat: add session 13f customer update composer`
- `a19c806 feat: add session 13g-1 seat usage + settings shell + company/policy`
- `e992844 feat: add session 13g-2 intake link + team settings sections`
- S13g-3 feat: add session 13g-3 onboarding settings section (hash TBD)

**Completed Session 13 slices:**

- S13a: standalone Vite PWA shell, credentialed typed API client, auth guard, onboarding home,
  local CORS/dev email fallback, self-hosted fonts, and local web runbook.
- S13b: Quick Capture drawer, phone lookup gate, create request flow, tracker-link success actions,
  and the S13b shell-access constraint note. Shell-level Viewer/past-due preflight remains deferred
  until role/commercial state is exposed through a reliable role-neutral source.
- S13c: authenticated command-center list, `/auth/me.accountRole`, role-rendered request tabs,
  search/status filters, attention badges/action prompts, `NeedsShare` callout, cursor pagination,
  and first-page polling.
- S13d-1: discriminated union `AppRoute` state nav, clickable rows (`onSelect` prop), `RequestDetail`
  page with header, unified timeline, read-only metadata, mobile sticky `NeedsShare` banner, desktop
  rail share section, and `share-intent` clearing (`copy_link`, `native_share`, `manual_mark_shared`).
  Contact launchers rendered from `detail.contactActions`, not raw field inference. S13e tracker-share
  scope (canRecordShareIntent, NeedsShare clearing, share method controls) absorbed here.
- S13d-2: `StatusChangeSection` in desktop rail and mobile stack; `patchRequestStatus` API method;
  `allowedStatuses` select, message textarea (required per `messageRequiredForStatuses`), per-action
  `isSubmitting` lock, query-cache replacement on success, 409 conflict banner with form preserved.
- S13d-3: `LogContactModal` (direction/channel/outcome/requiresFollowUp/summary, 409 conflict safe,
  reason required gate on acknowledge); `AcknowledgeAttentionSection` (orange rail card, reason textarea
  required, versioned POST); `ParticipationSection` (watch/unwatch/mute/unmute buttons from
  `availableActions`, per-action submitting lock); contact launchers wired to open modal on click;
  6 new `api.*` methods in `apiClient.ts`.
- S13f: `BusinessUpdateSection` in the detail rail/mobile stack; `postBusinessUpdate` API method;
  customer-visible message composer gated by `canSendBusinessUpdate`; optional status dropdown from
  filtered `allowedStatuses`; passive NeedsShare reminder; versioned `business-updates` submit; 409
  conflict handling preserves draft + selected status with `"Your message is saved here."` copy.
- S13g-1: server-authoritative `seatUsage` on `GET /accounts/me/members` (`occupiedSeats`, `maxSeats`,
  `atLimit`, `limitApplies`); Settings nav/route for Owner/Admin; `Settings.tsx` with Company and
  Response Policy forms only; setup/profile/policy DTOs and methods in `apiClient.ts`; focused
  `MemberManagementTests` seat usage assertion. Verified by clean backend compile, frontend tsc,
  `MemberManagementTests` 32/32, `InviteTests` 26/26, and `git diff --check`.
- S13g-2: Settings Intake Link + Team sections. Added intake status/ensure/replace client methods and UI;
  `apiFetchMaybeJson` for resend-invite email/manual-share responses; team roster with server `seatUsage`,
  include-removed toggle, invite form, resend email/manual-share one-time URL display, role change,
  suspend, reactivate, remove, and inline mapping for the 10 locked invite/member conflict codes.
  Review corrections are included in the commit: `Settings` receives `callerRole`; Owner promotion appears
  only for Owner callers; Admin callers do not see owner-management affordances for Owner targets; active
  members expose direct Remove with explicit confirmation while the backend remains authoritative for
  self/primary-owner/last-owner/seat-limit constraints.

**Session 13g complete.** Next session TBD.

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
