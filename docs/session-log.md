# Session Log — OpHalo Foundation

**Last updated:** 2026-07-13 (Session 30 complete — GAP-012/013/014)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** S84e — 1,069 unit tests passed, 14 architecture tests passed (not re-run this session); ophalo-app and ophalo-web TypeScript clean
**Next free ADR:** ADR-442
**Current session:** Session 30 — Build 084 feedback visibility and Closed request follow-up gaps

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

**Completed build log:** `docs/build-log/084-feedback-and-closed-request-follow-up-gaps.md`
**Completed build log:** `docs/build-log/078-customer-tracker-link-email-and-resend-configuration.md`
**Latest completed build logs:** `docs/build-log/083-session-26-follow-up-and-planned-promise-workflow-draft.md`, `docs/build-log/082-session-25-share-request-link-drawer.md`, `docs/build-log/077-pre-deployment-cleanup-and-file-decomposition.md`
**Readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Bug/gap tracker:** `docs/pilot-readiness-bug-tracker.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1

### Session 30 Goal

Implement the pilot feedback/closed-request gaps from the tracker:

- **GAP-013:** customer feedback submission needs a clear submitted state.
- **GAP-014:** authenticated request detail needs a visible submitted-feedback card/state.
- **GAP-012:** Closed requests need a new follow-up-work path without reopening the original request.

Product direction:

- Feedback remains binary and resolution-oriented in V1: `Yes, this was resolved` / `No, I still
  need help`.
- No ratings, stars, CSAT/NPS, public reviews, or testimonials.
- Customer feedback submission must show a clear submitted state and must not reveal internal review
  state.
- Staff request detail must show submitted feedback according to existing role/visibility rules.
- Negative feedback should be hard for Owner/Admin to miss and route to the existing review action.
- Positive feedback should be visible as a completed signal without creating false active work.
- Closed requests should not get a general V1 `Reopen` action unless a new ADR changes the lifecycle
  model.
- Preferred Closed-request direction is `Create follow-up request`; a lighter `Copy request info`
  utility may be acceptable if full prefill is too large for the pilot slice.

### Session 30 Slice Plan

Use targeted preflight before each slice. Keep the hard slice gate unless Christian explicitly
splits or expands the work: at most 3 mutation families, 8 production files, and 12 total changed
files including tests/docs.

1. **S84a — Preflight and GAP-012 product decision**
   - Decide whether V1 implements full `Create follow-up request`, lighter `Copy request info`, or
     documented deferral for Closed request follow-up work.
   - Inspect closed request detail actions, Quick Capture/business-created request prefill seams, and
     any existing backend relation/internal-note support.
   - Present the file-level gate before implementation.

2. **S84b — Customer feedback submitted state**
   - Replace the customer tracker feedback form with an explicit submitted state after success.
   - Render submitted state from server feedback fields on refresh/direct revisit when available.
   - Preserve binary resolved/unresolved semantics and customer-safe duplicate/error states.

3. **S84c — Authenticated request detail feedback card/state**
   - Add or correct request-detail feedback visibility.
   - Show positive feedback quietly, negative feedback prominently, and reviewed metadata where
     allowed.
   - Render `Mark feedback reviewed` only when server metadata allows it.

4. **S84d — Closed request follow-up path**
   - Implement the smallest path locked in S84a, if selected for this session.
   - Preserve the original Closed request lifecycle, feedback, closeout history, and customer page
     state.
   - Respect public-token and visibility boundaries for copied/prefilled data.

5. **S84e — Closeout docs**
   - Update Build 084, session log, and bug tracker statuses with landed scope and deferred items.

### Session 30 Hard Boundaries

- No feedback ratings, stars, CSAT/NPS, public reviews, or testimonials.
- No internal attention/review state on the customer tracker page.
- No automatic reopen from feedback.
- No general `Reopen` action for Closed requests in V1.
- No customer-visible implication that old Closed requests have reopened.
- No public-token leakage or visibility expansion through copied/prefilled follow-up data.
- Preserve fail-closed account, membership, row/action-policy, and public-token behavior.
- Preserve feedback comment/review visibility from ADR-151, ADR-263, ADR-271, and ADR-384.

### Recent Completed Work

Session 25 / Build 082 is complete: backend share commit and SMS handoff model/service, public SMS
handoff page, authenticated PWA Share Link modal, and closeout landed. End-to-end QR/SMS device
testing remains a deployment smoke item because it depends on real production/mobile browser
behavior.

Session 26 / Build 077 is complete: API composition cleanup, PWA request detail split, QuickCapture
split, API client type extraction, customer tracker page split, and closeout landed. Deferred file
decomposition remains recorded in Build 077: native mobile `[id].tsx`, PWA `RequestDetail.tsx`, and
remaining large frontend files.

Session 28 / Build 083 is complete: Follow Up On promise protection, follow-up resolution backend
command, PWA command-center completion workflow, request-list timing signals, and closeout docs
landed.

- **S83a**: backend attention gap closed (due/overdue FollowUpOn routes to NeedsAttention with `"due_or_overdue_follow_up_on"` slug, ranking group `"due_follow_up_on"` order 5).
- **S83b**: `POST /keep/requests/{id}/follow-up-resolution` backend command (complete / move / keep_active outcomes, full auth/row/audit guard, migration `S83bFollowUpResolution`).
- **S83c**: `FollowUpResolutionPanel` PWA workflow (modal/bottom-sheet, tap-first, due/overdue banners, timing panel affordance).
- **S83d**: `RequestRow` overdue follow-up → danger badge; past planned → attention; future → default.
- **S83e**: docs closed.

Deferred into next sessions:
- Native mobile follow-up completion (early post-pilot / early release).
- Planned For completion workflow.

Session 29 / Build 078 is complete: Resend configuration verified, public-intake tracker-link email
landed, customer intake confirmation now redirects to the tracker page after a short confirmation
state, operator mailto handoffs prefill the customer page link, and closeout docs landed.

Deployment smoke items carried forward from Build 078:
- Real Resend email delivery end-to-end with production `Resend:ApiKey` and `Resend:FromAddress`.
- Auto-redirect behavior on real mobile browsers after public intake.

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

1. `docs/build-log/084-feedback-and-closed-request-follow-up-gaps.md` — feedback visibility and
   Closed request follow-up gaps.
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
