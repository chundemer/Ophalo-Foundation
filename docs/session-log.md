# Session Log — OpHalo Foundation

**Last updated:** 2026-07-14 (Session 30 feedback-loop follow-on landed; documentation reconciled)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** S84e — 1,069 unit tests passed, 14 architecture tests passed (not re-run this session); ophalo-app and ophalo-web TypeScript clean
**Next free ADR:** ADR-442
**Current session:** No active implementation session — verify Build 085 closeout before selecting new work

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

**Follow-on implementation record:** `docs/build-log/085-feedback-review-operational-loop.md`
**Completed build log:** `docs/build-log/084-feedback-and-closed-request-follow-up-gaps.md`
**Completed build log:** `docs/build-log/078-customer-tracker-link-email-and-resend-configuration.md`
**Latest completed build logs:** `docs/build-log/083-session-26-follow-up-and-planned-promise-workflow-draft.md`, `docs/build-log/082-session-25-share-request-link-drawer.md`, `docs/build-log/077-pre-deployment-cleanup-and-file-decomposition.md`
**Readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Bug/gap tracker:** `docs/pilot-readiness-bug-tracker.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1

### Current Feedback-Loop State

The feedback-review operational loop is implemented in commit `315b231` (following Build 084). The
implementation records customer feedback receipt, promotes unreviewed negative feedback when entered
from Feedback Review, clears active feedback on Owner/Admin review, and preserves separate activity
events. Build 085 records the locked product behavior and implementation evidence.

The normal request-detail Utility uses the existing secondary/recommended-action treatment for active
negative feedback rather than literal `New feedback` copy. Treat that wording as a product-polish
decision only if pilot testing shows the cue is not clear enough.

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

1. `docs/build-log/085-feedback-review-operational-loop.md` — active feedback review operational
   loop and accountability-trail work.
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
