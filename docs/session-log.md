# Session Log — OpHalo Foundation

**Last updated:** 2026-07-13 (S83a complete — FollowUpOn attention gap closed)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** S83a — 1,050 unit tests passed, 14 architecture tests passed
**Next free ADR:** ADR-442
**Current session:** Session 28 — Follow-up and Planned Promise Workflow (Build 083)

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

**Current build log:** `docs/build-log/083-session-26-follow-up-and-planned-promise-workflow-draft.md`
**Latest completed build logs:** `docs/build-log/077-pre-deployment-cleanup-and-file-decomposition.md`, `docs/build-log/082-session-25-share-request-link-drawer.md`
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

### Session 28 Goal

Implement the Follow Up On and Planned For promise workflow from Build Log 083.

Locked ADRs:

- ADR-439 — Follow Up On Promise Protection Semantics.
- ADR-440 — Follow Up Completion And Missed Follow-Up Handling.
- ADR-441 — Planned For Internal Timing And Past-Date Handling.

Scope:

- verify and close any backend gap for due/overdue Follow Up On attention/ranking;
- add a narrow atomic follow-up completion backend command;
- implement the PWA command-center detail workflow with mobile-width PWA as first-class;
- update request-list timing signals without inline list completion;
- keep native mobile command-center follow-up completion deferred from initial pilot but planned for
  early post-pilot/early release.

Slice order:

1. **S83a — Backend verification and attention gap closure** ✓ Complete
   - Gap confirmed at three levels and closed.
   - `KeepRequest.GetNeedsStatusCheckInputs()`: due/overdue FollowUpOn now suppressed with slug
     `"due_or_overdue_follow_up_on"` — routes to NeedsAttention, not NeedsStatusCheck.
   - `KeepRequestListPersistence`: `IClock` injected; NeedsAttention DB query and count expanded
     to include `FollowUpOnDate <= today && AttentionLevel == None` rows.
   - `GetKeepRequestListService.ToSummary`: computes `isDueOrOverdueFollowUpOn` / `isFollowUpOverdue`;
     new ranking group `"due_follow_up_on"` (order 5), severity `"danger"` / `"attention"`,
     rowContext `"needs_attention"`. Stronger persisted attention remains primary.
   - 3 pre-existing domain tests updated to ADR-439 semantics; 6 new service tests added.
   - Baseline: 1,050/1,050 unit tests, 14/14 architecture tests.

2. **S83b — Follow-up completion backend command**
   - Add `POST /keep/requests/{requestId}/follow-up-resolution` or the final route chosen in
     preflight.
   - Require `X-Keep-Request-Version`.
   - Return updated `KeepRequestDetailResult`.
   - Preserve fail-closed account/role/row/action authorization.
   - Ensure audit activity and Follow Up On state change commit together.

3. **S83c — PWA detail follow-up completion workflow**
   - Add `Record follow-up` entry from due/overdue detail banner and timing panel.
   - Desktop: focused modal/drawer.
   - Mobile-width PWA: bottom sheet or focused full-screen panel, tap-first choices, optional note.
   - Preserve drafts on conflict/error.

4. **S83d — Request list timing signals**
   - Show due/overdue Follow Up On and past Planned For signals.
   - Route list users to detail; do not implement inline follow-up completion in V1.

5. **S83e — Closeout docs**
   - Update Build Log 083 with what landed and remaining deferrals.
   - Update session-log next state after verification.

Hard boundaries:

- no customer self-scheduling;
- no customer direct editing of Follow Up On;
- no customer-page display of internal Follow Up On or Planned For by default;
- no automatic customer notifications from timing changes;
- no backend SMS;
- no Planned For completion workflow;
- no native mobile implementation in initial pilot.

Session 27 / Build 078 (`docs/build-log/078-customer-tracker-link-email-and-resend-configuration.md`)
remains a planned tracker-link email/Resend slice and should be resumed separately after this
implementation path or by explicit direction.

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
