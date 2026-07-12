# Session Log — OpHalo Foundation

**Last updated:** 2026-07-12 (Session 25 complete — Share Request Link Drawer)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** S25d — 1042 unit tests passed, 14 architecture tests passed
**Next free ADR:** ADR-438
**Current session:** Session 26 — TBD

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

**Current build log:** `docs/build-log/082-session-25-share-request-link-drawer.md`
**Latest completed build log:** `docs/build-log/081-session-24-request-detail-2-column-workbench.md`
**Readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Bug/gap tracker:** `docs/pilot-readiness-bug-tracker.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1

### Session 25 Goal

Build the authenticated PWA **Share Link** workflow for an existing customer request page.

The product contract is:

```text
OpHalo prepares the handoff.
The human sends through their chosen channel.
OpHalo records only the human confirmation: Mark as Shared.
```

The session should deliver a modal/dialog available from request list rows and request detail, with:

- Scan to Text QR as the primary method;
- Open Email, Open WhatsApp, Copy Message, and Copy Link as secondary paths;
- editable customer-ready message body;
- honest **Mark as Shared** audit flow;
- secure 15-minute SMS micro-handoff tokens;
- owner/customer private-link transparency copy.

### Locked S25 Decisions

Use build-log 082 as the source of truth. Summary:

- Scan to Text is primary.
- Drawer stays open until **Mark as Shared**.
- Never say **Sent** for external handoffs.
- V1 actions: Scan to Text, Open Email, Open WhatsApp, Copy Message, Copy Link.
- Phone is required for requests; email is optional.
- Default message uses customer first name and business name.
- Message is editable, but edited copy is not saved permanently.
- **Mark as Shared** is always enabled and can log `manual_other`.
- Entry points are request list and request detail.
- This drawer shares only the existing customer request page link.
- Customer page and drawer disclose private-link visibility.
- SMS QR uses a compact handoff URL with opaque 15-minute token.
- QR uses stale-guard refresh after edits.
- Expired handoff tokens expose no payload.
- Tokens are reusable until fixed expiry.
- Mobile handoff page includes **Open Text Message** and **Copy Message** while valid.
- Only **Mark as Shared** creates request history.
- Short-lived handoff record may store the prepared message body; request history may not.
- Email/WhatsApp use direct launch links.
- Frontend renders QR with a small QR library.
- V1 UI container is a modal/dialog.

### Current Code Validation

The customer request page already supports the share-message promise:

- customers can view request status/history;
- active pages expose update/question, request update, request call, share availability, add details,
  and cancellation request actions;
- closed eligible pages expose feedback;
- anonymous customer write routes are rate limited;
- customer-visible events are filtered before public exposure.

Relevant files:

- `web/ophalo-web/src/app/keep/r/[pageToken]/CustomerTrackerView.tsx`
- `src/OpHalo.Api/Program.cs`
- `src/OpHalo.Keep.Application/Requests/KeepCustomerPageMapper.cs`

### Expected Slice Order

1. **S25a — Backend share commit and SMS handoff model/service**
   - Confirm existing clear-share-intent service/endpoint shape.
   - Add or adapt honest manual-share commit semantics.
   - Add short-lived SMS handoff token creation/resolve persistence.
   - Preserve token hashing and fail-closed request access.

2. **S25b — Public SMS handoff page**
   - Add `/keep/share-sms/{handoffToken}` public mobile page.
   - Resolve valid token, format `sms:` URI, attempt launch, and show fallback controls.
   - Expired/invalid tokens expose no payload.

3. **S25c — PWA Share Link modal**
   - Add shared modal/dialog component.
   - Wire from request list and request detail.
   - Add editable message, direct email/WhatsApp/copy actions, QR rendering, stale guard, expiry
     state, and **Mark as Shared** flow.

4. **S25d — Tests, responsive QA, and docs closeout**
   - Add focused API tests for access, token expiry, no payload leakage, and manual-share history.
   - Add frontend/unit or integration coverage where the current test setup supports it.
   - Run proportionate suites and close build-log 082.

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
