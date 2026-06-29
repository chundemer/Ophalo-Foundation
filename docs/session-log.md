# Session Log — OpHalo Foundation

**Last updated:** 2026-06-29 (S13d-1 committed; S13d-2 next)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** 939 unit · 14 arch · 713 integration = 1,666 total, 0 failures (1 pre-existing KeepG5 fluke excluded)
**Next free ADR:** ADR-385
**Current session:** Session 13d — Request Detail Workbench

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
**Last completed build log:** `docs/build-log/067-session-13-pwa-workbench.md` (S13c)
**Readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1
**Current session:** Session 13d — Request Detail Workbench

**Committed baseline (local, not yet pushed):**

- `6fc80e1 feat: expose share intent action metadata`
- S13d-1 commit (this session)

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
  Contact launchers rendered from `detail.contactActions`, not raw field inference.

**S13d locked architecture (all sub-slices):**

- Detail source of truth: `GET /keep/requests/{requestId}` / `KeepRequestDetailResult`.
- Timeline: `detail.events` as one chronological feed.
- Permissions/actions: render from `detail.availableActions`, not client-side role policy.
- Validation: `detail.validation` for local form limits.
- Contact buttons: `detail.contactActions` only — never raw phone/email field inference.
- Share controls: `availableActions.canRecordShareIntent`; call `POST /keep/requests/{requestId}/share-intent`.
- Versioned writes: send `X-Keep-Request-Version: {detail.version}`; preserve open form input on 409.
- Out of scope: customer update composer, Follow Up On / Planned For UI, closeout/feedback flows,
  classification/spam/test UI, settings/member management, reporting, batch closeout.

**S13d-2 — Status Change Workflow** ← next

- Files expected: `web/ophalo-app/src/pages/RequestDetail.tsx`, `web/ophalo-app/src/lib/apiClient.ts`.
- Add status-change UI using `detail.availableActions.allowedStatuses` and `PATCH /keep/requests/{requestId}/status`.
- Send `X-Keep-Request-Version: {detail.version}`; replace local detail state with the response body on success.
- On 409 `KeepRequest.RequestChanged`, preserve form input, disable further submit, show conflict banner:
  `"This request has been updated by another team member. Copy your unsaved notes and refresh the workbench to load the latest history."`
- Per-action `isSubmitting` lock; no overlapping writes.

**S13d-3 — Operational Action Rail**

- Add external-contact log, attention acknowledge, and participation controls.
- Render availability from `detail.availableActions`; validate locally against `detail.validation`.
- Contact log modal: `direction`, `channel`, `outcome` (outbound only), `requiresBusinessFollowUp` (inbound), `summary`.
- Contact launchers already rendered in S13d-1; S13d-3 wires the post-launch modal open.
- Keep each mutation path fail-closed, version-aware where applicable.

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
