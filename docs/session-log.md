# Session Log — OpHalo Foundation

**Last updated:** 2026-06-30 (S13f complete; S13g next)
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
**Last completed build log:** `docs/build-log/067-session-13-pwa-workbench.md` (S13f)
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

**Next Slice — S13g Member Management And Settings Continuation**

Pre-work is complete in build-log 067. Treat this as mechanical implementation, not discovery.
S13g-as-a-whole breaches the mutation-family gate, so use the approved Option B split below.

**S13g-1 — Seat Usage + Settings Shell + Company/Policy**

- Backend prerequisite: add server-authoritative `seatUsage` to `GET /accounts/me/members` response.
  `ListMembersResponse` currently has only `members`.
- Expected backend files: `IMemberManagementPersistence.cs`, `EfMemberManagementPersistence.cs`,
  `MemberManagementService.cs`, plus focused assertions in `MemberManagementTests.cs`.
- Seat usage fields: `occupiedSeats`, `maxSeats`, `atLimit`, `limitApplies`. Compute from server-side
  member/entitlement state; do not count visible rows in the browser.
- Frontend files: `web/ophalo-app/src/App.tsx`, `web/ophalo-app/src/lib/apiClient.ts`,
  `web/ophalo-app/src/pages/Settings.tsx` (new).
- Frontend scope: Owner/Admin-only `Settings` nav/route; Settings page with `Company` and
  `Response Policy` sections only.
- Company fields: business name, timezone, customer-facing phone, customer-facing email.
- Policy fields: first response target minutes, standard response target minutes, priority response
  target minutes, status-check threshold days.
- Mutation family: Company/Policy only (`PUT /keep/setup/profile`, `PUT /keep/setup/policy`).
- Defer intake link, team roster/mutations, and onboarding marks to later S13g slices.

**S13g-2 — Intake Link + Team**

- Add intake link status/ensure/replace UI. Replacement requires explicit confirmation.
- Add Team roster using server `seatUsage` readout.
- Team mutations: invite team member, resend invite, change role, suspend, reactivate, remove.
- Invite roles: Admin, Operator, Viewer only; use product labels `Owner`, `Admin`, `Operator`, `Viewer`.
- Handle seat-limit and member conflict codes inline near the triggering form/row.
- Manual-share resend is a deliberate fallback only; show raw invite URL once after explicit action.

**S13g-3 — Onboarding Settings Section**

- Add onboarding checklist readout inside Settings.
- Manual mark buttons only for quick-capture exercise, tracker review, and spam classification explanation.
- Do not manually mark `operatorInvited` from invite UI; it derives from an active non-owner member.
- Keep solo-business posture non-blocking/add-later.

**Locked architecture (carries forward into S13g):**

- One Owner/Admin-only `Settings` nav item, not separate top-level Company/Team nav items.
- Operators/Viewers do not get editable Settings navigation; direct access should use standard
  access-limited/403 handling.
- Use friendly product language: `Settings`, `Team`, `team member`, `Invite team member`.
- Backend remains authoritative for permissions, owner/self/primary-owner constraints, membership
  state, seat limits, account state, and invite/manual-share token generation.
- S13g out of scope: primary-owner transfer, billing/plan management, internal support tooling, and
  invite accept implementation in `ophalo-app`.

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
