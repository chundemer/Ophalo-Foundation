# Session Log — OpHalo Foundation

**Last updated:** 2026-07-10 (S22 complete; pre-next-session issues pending)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** Targeted intake baseline — 66 intake unit · 25 intake integration confirmed; full suite pending (1 pre-existing KeepG5 fluke excluded)
**Next free ADR:** ADR-434
**Current session:** Between sessions — Session 22 complete

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

**Current build log:** none active
**Latest completed build log:** `docs/build-log/076-session-22-guided-setup-intake-and-service-location.md`
**Last completed prior-session build log:** `docs/build-log/075-session-21-attention-guidance-resolution-metadata.md`
**Readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Bug/gap tracker:** `docs/pilot-readiness-bug-tracker.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1
**Current session:** Between sessions — Session 22 complete
**Current slice:** None active — discuss/resolve pre-next-session issues before selecting build-log 078 or 077

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

### Post-S22 State

Session 22 is complete. Historical S22 detail is archived in
`docs/build-log/076-session-22-guided-setup-intake-and-service-location.md`; use this session log as
the current handoff and discussion brief only.

Locked decisions to preserve:

- ADR-428: Keep launches in a day-zero functional state. Settings is split into `Public Link &
  Profile`, `Response Policy`, and `Team`; Getting Started is a lightweight verification/on-ramp.
- ADR-429: ordinary public link-name edits preserve old shared slugs as aliases. Replacement or
  regeneration is the destructive/security action and must warn that old shared links break.
- Public intake URLs use durable slug routing from the configured public web base URL. Do not build
  customer-facing links from `window.location.origin` inside `ophalo-app`.
- Public intake is auto-provisioned by default; owners verify/copy/preview it from Settings.
- Team setup must remain optional and reassuring for solo businesses.
- Public intake form now collects service location, intake urgency, and preferred contact method.
- ADR-430: intake urgency and preferred contact method are persisted customer-reported triage
  metadata. They appear on operator detail and on the operator request list because road operators
  use the list as their primary view. Customer-selected urgency is not a verified system attention
  condition; preferred contact is not a full notification preference/opt-out system.
- ADR-431: public Keep pages use business-first identity hierarchy and neutral public motto copy:
  `The trust and continuity layer between businesses and customers.`
- ADR-432: platform email/Resend is in scope. Public intake may send a narrow, fail-soft tracker-link
  email when the customer supplies email; backend customer SMS and broad automated customer
  notification workflows remain deferred.
- Staff-auth public-intake blocking remains post-submit only for now; pre-submit staff blocking is
  deferred because the public Next.js page has no load-time session context without a new API call.

Implementation status:

- S22 day-zero Settings redesign, slug routing, slug aliases, Settings tab split, backend
  auto-provisioning, service location, business identity header, intake UI polish, intake urgency,
  preferred contact persistence, and operator list/detail display are complete.
- `Settings.tsx` was split into tab files:
  `settings/CompanySection.tsx`, `settings/PolicySection.tsx`,
  `settings/PublicLinkSection.tsx`, and `settings/TeamSection.tsx`.
- Pre-deployment file-decomposition cleanup is parked in
  `docs/build-log/077-pre-deployment-cleanup-and-file-decomposition.md`; do not start that cleanup
  until the customer request page and testing are complete.

Always preserve:

- fail-closed account, membership, action-policy, public-token, and concurrency behavior;
- raw-token non-disclosure in logs, diagnostics, persisted frontend state, or long-lived UI state;
- `ophalo-app` as the authenticated Keep workbench;
- `OpHalo.Api` as the authority for auth, sessions, account creation, rate limiting, email,
  authorization, and persistence.

Topology:

- Production: `ophalo.com`/`www.ophalo.com` -> `ophalo-web`, `app.ophalo.com` -> `ophalo-app`,
  `api.ophalo.com` -> `OpHalo.Api`.
- Local: `ophalo-web` `http://localhost:3000`, `ophalo-app` `http://localhost:5173`,
  `OpHalo.Api` `http://localhost:5092`.
- Pilot cap: `SignupDefaults:MaxPilotAccounts=15`.
- `OperatorBaseUrl` is retired; invite links use `{PublicBaseUrl}/invite/accept`.

### Pre-Next-Session Discussion Queue

No next implementation session is selected yet. Use this space to capture and resolve Christian's
issues before opening build-log 078 or resuming build-log 077.

- Request detail command-center simplification: split into small UI slices before the next major
  session rather than bundling into tracker-link email or pre-deployment cleanup.

Resolved before next session:

- Request detail external-contact action: use the generic label `Log external contact`, remove the
  duplicate inline `Log phone contact` pill beside the customer phone number, keep the right-rail
  `Handled outside Keep?` card as the canonical external-contact logging action, and retitle the
  modal `Log external contact`.
- Request detail command cleanup: removed the separate lower-right `Change status` card; kept one
  status selector in the `Send customer update` composer; made composer behavior explicit for
  message-only, status-only, and message+status cases; moved customer-page actions into quiet hero
  links; kept the customer-name heading in `font-serif`; and replaced `Already handled?` with
  quieter `Clear attention` semantics.
- Request queue navigation: add detail-page navigation so users can move to the next request without
  scrolling back to the list. Preferred direction is `Previous` / `Next` or `Next needing attention`
  near the hero/top bar, plus a post-action route to the next request if the current workflow supports
  it. **Complete** — Prev/Next buttons in breadcrumb bar; client-side list context passed from
  `Requests` through `App` to `RequestDetail`; disabled at list boundaries; no nav shown on direct
  open (QuickCapture, deep link). Committed and pushed (`14347ce`).

Queued Claude slices:

- **S22p9 — Urgency-aware default queue ordering:** **Complete.** Added `customer_urgent_active`
  ranking bucket (order 3) covering any urgent active intake not yet terminal, pending-customer, or
  resolved. New order: overdue (1) → priority-biz-waiting (2) → customer-urgent-active (3) →
  post-close (4) → standard-biz-waiting (5) → first-response-pending (6) → waiting-on-customer (7)
  → resolved-quiet (8) → active (9). `isPostClose` guard preserved before priority and urgent checks.
  UI clarification pass: `KeepRequestRankingInfo` type added to `apiClient.ts`; `Overdue` badge now
  shown first in badge row when `ranking.isOverdue`; danger left-border for overdue rows; `Due [date]`
  compact text from `ranking.dueAtUtc`; mock fixtures updated. TypeScript clean.
- **S22p10 — Business-editable request priority:** **Complete.** Backend: `BusinessPriority` enum
  (Routine/Soon/Urgent), `SetBusinessPriority` domain method, `BusinessPriorityChanged = 17` event
  type, `SetBusinessPriorityService`, `PUT /keep/requests/{requestId}/priority` endpoint, EF migration
  (`business_priority` nullable varchar(50)), effective-priority ranking override in list service.
  ADR-433 locked. Frontend: `KeepRequestSummary`/`KeepRequestDetailResult` updated, dual-signal badge
  in `RequestRow.tsx`, Priority selector in `RequestDetail.tsx` (`canAddInternalNote` gate), mock
  fixtures (10 locations) and mockApiClient inline details + stub updated. TypeScript clean.
  Committed at `743a664` (backend + tests + ADR) and frontend pending commit.
- Full Claude-ready details and locked decisions are in
  `docs/build-log/076-session-22-guided-setup-intake-and-service-location.md`.

Recommended slice order:

1. Request detail command cleanup: complete.
2. Request detail queue navigation: complete (see above).
3. S22p9 urgency-aware queue ordering: **complete** (see above).
4. S22p10 business-priority slice: **complete** (see above).
5. Resume build-log 078 or build-log 077 — command-center issues are now settled.

### Completed S22 Summary

Session 22 is complete. Shipped work included day-zero Settings redesign, durable slug-based public
intake, slug aliases, backend intake-link auto-provisioning, business-first public identity, intake
urgency, preferred contact method, service location collection/exposure, PWA operator display, mobile
request-detail carry-forward, mobile Open in Maps, and documentation reconciliation.

Detailed S22 slice notes live in
`docs/build-log/076-session-22-guided-setup-intake-and-service-location.md`.

Remaining pre-deployment work lives in separate build logs:

1. **Customer tracker-link email / Resend:** `docs/build-log/078-customer-tracker-link-email-and-resend-configuration.md` — locks tracker-link retention decisions, Resend configuration checks, public-intake tracker-link email, confirmation-flow copy, and operator correspondence prefill.
2. **Pre-deployment cleanup:** `docs/build-log/077-pre-deployment-cleanup-and-file-decomposition.md` — deferred until customer request page work and testing are complete.

Historical mobile context lives in `docs/build-log/071-session-17-review-safe-native-product-foundation.md`.

## Carry-Forward Boundaries

- Real APNs/FCM provider implementation remains future work.
- Demo scenario packs, demo reset UI, and admin/internal classification management remain deferred.
- Classification is operational/reporting/safety posture, separate from commercial lifecycle.
- Public signup cannot create Demo/InternalTest accounts.
- Production push delivery must stay suppressed for Demo/InternalTest accounts.
- Keep does not send backend customer SMS or ingest SMS replies in V1. Platform email/Resend is in
  scope for auth/member flows, and ADR-432 allows one narrow fail-soft public-intake tracker-link
  email when the customer supplies email. Broad automated customer email/SMS notification workflows,
  notification preferences, quiet hours, proof-of-send, and delivery ledgers remain deferred.

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
