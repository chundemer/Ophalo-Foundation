# Session Log — OpHalo Foundation

**Last updated:** 2026-07-13 (S78e complete — Build 078 closed)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** S78b — 1,064 unit tests passed, 14 architecture tests passed; ophalo-app and ophalo-web TypeScript clean; Vite build clean (1,600 modules)
**Next free ADR:** ADR-442
**Current session:** Session 29 — Build 078 tracker-link email / Resend configuration / confirmation flow

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

**Current build log:** `docs/build-log/078-customer-tracker-link-email-and-resend-configuration.md`
**Completed build log:** `docs/build-log/083-session-26-follow-up-and-planned-promise-workflow-draft.md`
**Latest completed build logs:** `docs/build-log/077-pre-deployment-cleanup-and-file-decomposition.md`, `docs/build-log/082-session-25-share-request-link-drawer.md`
**Readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Bug/gap tracker:** `docs/pilot-readiness-bug-tracker.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1

### Session 29 Goal

Implement Build 078: make customer tracker links harder to lose after public intake while preserving
the V1 communication boundary.

Product direction:

- Platform email through `IEmailSender` / Resend is in scope for auth/member flows and this one
  narrow public-intake tracker-link email.
- If a public-intake customer supplies email, send a single transactional tracker-link email after
  the durable request commit and customer page token creation.
- Delivery is best-effort and fail-soft: intake must still succeed if email delivery fails, and the
  public API response must not reveal whether an email was sent.
- If no email is supplied, confirmation UX should still help the customer reach and retain the
  tracker page through auto-open, copy/share, and clear reassurance.
- Operator handoff surfaces should prefill the customer page link where supported, without treating
  composer launch as proof of contact.
- Customer page access remains high-entropy token/capability-link based; phone numbers are lookup and
  recovery identifiers, not URL access tokens.

### Session 29 Slice Plan

Use targeted preflight before each slice. Keep the hard slice gate unless Christian explicitly
splits or expands the work: at most 3 mutation families, 8 production files, and 12 total changed
files including tests/docs.

1. **S78a — Resend configuration verified** ✓ (no code change)
   - Wiring confirmed deployment-ready: `ConsoleEmailSender` in dev without `ApiKey`, `ResendEmailSender` otherwise.
   - Required secrets: `Resend:ApiKey`, `Resend:FromAddress`, `App:PublicBaseUrl`.
   - `MagicLinkSettings.PublicBaseUrl` is the injection point for outbound URLs.

2. **S78b — Public intake tracker-link email** ✓
   - `CreateKeepPublicIntakeService` now takes `IEmailSender` + `IOptions<MagicLinkSettings>`.
   - `TrySendTrackerLinkEmailAsync`: sends after durable commit, fail-soft, no public response disclosure.
   - No Reply-To for V1. Email copy clarifies replies may not be monitored.
   - 3 new unit tests + 2 new integration tests. Baseline: 1,064/1,064 unit.

3. **S78c — Confirmation flow and link-retention UX** ✓
   - `IntakeForm.tsx`: email helper copy → benefit-led; reference code demoted to footer metadata;
     ~2s auto-redirect to `/keep/r/{pageToken}`; email confirmation copy when email was provided.

4. **S78d — Operator correspondence prefill** ✓
   - `RequestDetail.tsx` `CustomerPanel` mailto: prefilled with subject + body containing customer page URL.
   - Uses `VITE_PUBLIC_BASE_URL`, not `window.location.origin`. No state mutation on open.

5. **S78e — Closeout docs** ✓
   - Build Log 078 updated. Session log advanced.

### Deployment smoke items (not automated)

- Real Resend email delivery end-to-end (requires `Resend:ApiKey` + `Resend:FromAddress` in production).
- Auto-redirect behavior on real mobile browsers after public intake.

### Session 29 Open Questions

- Should the tracker-link email `Reply-To` use a configured business customer-facing email when
  present, or a product-controlled support/no-reply address for V1?
- Should public-intake email helper copy explicitly say: "Add your email and we'll send you a secure
  link to track your request"?
- Should success auto-redirect happen immediately after the success response or after a short visible
  confirmation delay?

### Session 29 Hard Boundaries

- No backend customer SMS.
- No SMS reply ingestion.
- No broad automated customer notification workflows.
- No notification preferences, quiet hours, opt-out center, campaign behavior, delivery ledger,
  retries, dead-letter queue, or proof-of-send semantics.
- No phone-number-based customer request URLs.
- No public API disclosure of email delivery success/failure.
- No raw-token disclosure in logs, diagnostics, persisted frontend state, or long-lived UI state.

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
