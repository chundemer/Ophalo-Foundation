# Session Log — OpHalo Foundation

**Last updated:** 2026-07-13 (Session 26 complete — S26 closeout)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** S26 closeout — 1044 unit tests passed, 14 architecture tests passed
**Next free ADR:** ADR-438
**Current session:** Session 27 — Customer tracker link email / Resend (Build 078)

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

**Current build log:** `docs/build-log/077-pre-deployment-cleanup-and-file-decomposition.md`
**Latest completed build log:** `docs/build-log/082-session-25-share-request-link-drawer.md`
**Readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Bug/gap tracker:** `docs/pilot-readiness-bug-tracker.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1

### Session 25 Complete

Session 25 is complete. Four slices landed:

- S25a — backend share commit and SMS handoff model/service;
- S25b — public SMS handoff page;
- S25c — authenticated PWA Share Link modal;
- S25d — tests, responsive QA, and closeout.

End-to-end QR/SMS device testing remains a deployment smoke item because it depends on real
production/mobile browser behavior.

### S26a Complete

API composition cleanup landed. `Program.cs` reduced from 1,092 → 254 lines.

### S26b Complete

PWA request detail mechanical split landed. `RequestDetail.tsx` reduced from 3,152 → 1,510 lines.

### Session 26 Complete

All four slices landed (S26a–S26d) plus S26 closeout. Build-log 077 is closed.

**S26d:** `CustomerTrackerView.tsx` (ophalo-web) split from 678 → 268 lines. Six new files:
`tracker-types.ts`, `TrackerExpiredView`, `TrackerStatusCard`, `TrackerActionCard`,
`TrackerInitialRequestCard`, `TrackerHistoryCard`. TypeScript check clean.

**S26 closeout:**
- Removed unused `IClock clock` from SMS handoff creation endpoint.
- Added `native_share` and `manual_mark_shared` to `ClearShareIntentService.ValidMethods`,
  fixing two pre-existing integration test failures (returning `BadRequest` instead of `NoContent`).
- New baseline: 1,044/1,044 unit tests, 14/14 architecture tests.

Deferred (recorded in build-log 077): mobile `[id].tsx` (1,588 lines), PWA `RequestDetail.tsx`
(1,510 lines), and remaining large frontend files.

### S26c Complete

QuickCapture and API client splits landed.

`QuickCapture.tsx` reduced from 811 → 183 lines. Five new files under `web/ophalo-app/src/components/quick-capture/`:
- `utils.ts` — `Stage` type, `SOURCE_OPTIONS`, `stripToDigits`, `isPhoneShaped`, `formatStatus`
- `LookupGate.tsx` — phone lookup form with clipboard + contact picker
- `LookupResultView.tsx` — customer match display + `ActiveRequestCard` (co-located)
- `CaptureForm.tsx` — new request capture form
- `SuccessPanel.tsx` — post-capture success actions

`apiClient.ts` reduced from 812 → 439 lines. All exported types extracted to `apiClient.types.ts` (468 lines). Consumer import surface preserved exactly via `import type` + `export type` re-exports.

TypeScript: zero errors. Vite production build: clean (1,599 modules). No behavior changes.

Seven new files created under `web/ophalo-app/src/pages/request-detail/`:
- `helpers.ts` — format utils, label constants, FOCUS_RING/INPUT_CLS/STATUS_CONFLICT_MESSAGE, attention guidance logic
- `highlights.tsx` — HighlightLevel, AttentionHighlights, RecommendedActionBadge, all highlight helpers
- `TimelineEvent.tsx` — timeline event display + TimelineEvent component
- `TimingPanel.tsx` — follow-up / planned timing controls
- `DetailHero.tsx` — CustomerPageHeroActions + TodayPromiseBanner + DetailHero
- `TeamSection.tsx` — team participation controls
- `BusinessSection.tsx` — WorkDoneCard + CloseRequestCard + BusinessUpdateSection

TypeScript: zero errors. Vite production build: clean. No behavior changes.

- `src/OpHalo.Api/Keep/KeepServiceCollectionExtensions.cs` — `AddKeepServices()` with all Keep DI registrations.
- `src/OpHalo.Api/Keep/KeepEndpoints.cs` — `MapKeepEndpoints()` with all `/keep/...` routes; four handlers as private statics.
- `src/OpHalo.Api/Keep/RenameLinkNameBody.cs`, `UpdateServiceLocationBody.cs` — orphan records moved.

Pre-existing gap identified: two ShareIntent integration tests (`ShareIntent_NeedsShare_false_after_successful_clear`, `ShareIntent_idempotent_second_call_returns_204_without_error`) return `BadRequest` instead of `NoContent` on baseline. Not caused by S26a; needs separate investigation.

### Session 27 Goal

Wire the tracker-link confirmation email so customers who submit public intake with an email
address receive a link back to their private request page.

**Current build log:** `docs/build-log/078-customer-tracker-link-email-and-resend-configuration.md`

Scope (from build-log 078):
- Resend production configuration via `IEmailSender` and `App:ResendApiKey`.
- Narrow transactional tracker-link email sent after successful public intake when the customer
  supplied an email address; fail-soft (intake succeeds even if email send fails).
- No customer SMS, broad notification workflows, delivery ledgers, or campaigns.
- ADR-432 locks the email-vs-SMS boundary.

Pre-work: read build-log 078 in full before the implementation gate.

---

## Standing Boundaries

- Keep does not send backend customer SMS or ingest SMS replies in V1.
- Platform email/Resend is in scope for auth/member flows, and ADR-432 allows one narrow fail-soft
  public-intake tracker-link email when the customer supplies email.
- Broad automated customer email/SMS notification workflows, notification preferences, quiet hours,
  proof-of-send, and delivery ledgers remain deferred.
- Google Voice remains owner-managed and is indirectly supported through **Copy Message** only.
- Business new request link sharing is separate from this drawer.
- Saved share-message templates are deferred.
- Mobile app push-to-phone handoff is deferred.

Always preserve:

- fail-closed account, membership, action-policy, public-token, and concurrency behavior;
- raw-token non-disclosure in logs, diagnostics, persisted frontend state, or long-lived UI state;
- `ophalo-app` as the authenticated Keep workbench;
- `ophalo-web` as the public/customer-facing web surface;
- `OpHalo.Api` as the authority for auth, sessions, account creation, rate limiting, email,
  authorization, and persistence.

Topology:

- Production: `ophalo.com`/`www.ophalo.com` -> `ophalo-web`, `app.ophalo.com` -> `ophalo-app`,
  `api.ophalo.com` -> `OpHalo.Api`.
- Local: `ophalo-web` `http://localhost:3000`, `ophalo-app` `http://localhost:5173`,
  `OpHalo.Api` `http://localhost:5092`.
- Pilot cap: `SignupDefaults:MaxPilotAccounts=15`.
- `OperatorBaseUrl` is retired; invite links use `{PublicBaseUrl}/invite/accept`.

---

## Historical Context

Completed implementation details live in the build logs and should not be repeated here:

- Session 13 — PWA workbench: `docs/build-log/067-session-13-pwa-workbench.md`
- Session 14 — web front door: `docs/build-log/068-session-14-ophalo-web-front-door.md`
- Session 15 — pilot readiness: `docs/build-log/069-session-15-pilot-readiness.md`
- Session 16 — native mobile foundation: `docs/build-log/070-session-16-native-mobile-foundation.md`
- Session 19 — Keep workbench UX migration: `docs/build-log/073-session-19-keep-workbench-ux-migration.md`
- Session 20 — mobile Owner/Admin control mode: `docs/build-log/074-session-20-mobile-owner-admin-control-mode.md`
- Session 21 — attention guidance resolution metadata: `docs/build-log/075-session-21-attention-guidance-resolution-metadata.md`
- Session 22 — guided setup, intake, service location: `docs/build-log/076-session-22-guided-setup-intake-and-service-location.md`
- Session 23 — work completed and closeout UX: `docs/build-log/080-session-23-work-completed-closeout-ux.md`
- Session 24 — request workbench 2-column layout and list quick actions:
  `docs/build-log/081-session-24-request-detail-2-column-workbench.md`

Remaining pre-deployment work lives in separate build logs:

1. `docs/build-log/078-customer-tracker-link-email-and-resend-configuration.md` — tracker-link
   email / Resend configuration and confirmation flow.
2. `docs/build-log/077-pre-deployment-cleanup-and-file-decomposition.md` — pre-deployment cleanup.

---

## Carry-Forward Boundaries

- Real APNs/FCM provider implementation remains future work.
- Demo scenario packs, demo reset UI, and admin/internal classification management remain deferred.
- Classification is operational/reporting/safety posture, separate from commercial lifecycle.
- Public signup cannot create Demo/InternalTest accounts.
- Production push delivery must stay suppressed for Demo/InternalTest accounts.

---

## Deferred Decisions

- **Mobile package manager standardisation:** `mobile/ophalo-mobile` was scaffolded with Expo's
  default npm and has used `package-lock.json` since S16b. `web/ophalo-app` uses `pnpm@11.9.0`.
  Standardising mobile to pnpm remains low urgency, but worth aligning before EAS Build
  configuration introduces CI/CD package-manager assumptions.

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
