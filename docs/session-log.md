# Session Log — OpHalo Foundation

**Last updated:** 2026-06-26 (S8c complete; S8d mutation hooks next)
**Branch:** `main` tracking `origin/main`
**Current baseline:** 815 unit · 14 arch green before Session 8; S8a committed in `1fcf933`,
`4c170ca`, and review fix `2b54640`. S8b committed in `e09258e`; S8c committed in `2c7b911`.
**Next free ADR:** ADR-363
**Next batch:** S8d — Limited push-worthy mutation hooks.

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

**Current build log:** `docs/build-log/063-session-8-notification-device-foundation.md`  
**Pilot readiness working doc:** `docs/pilot-readiness-decision-questions.md`  
**Post-Session-8 provisional roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1  
**Completed Session 7 build log:** `docs/build-log/062-session-7-pilot-safety-decision-build.md`  
**Next implementation:** S8d — Limited push-worthy mutation hooks.

Session 8 builds the narrow V1 staff notification/device foundation: account-user-scoped device
records, token registration/revocation, personal badge count, push abstraction with no-op delivery,
non-sensitive payload/display mapping, routed candidate foundation, and limited explicit post-commit
hooks.

Session 8 does not build real APNs/FCM adapters, notification history/outbox/retry/analytics, full
notification preferences, quiet hours, backend customer SMS/email, customer-facing platform
notifications, SSE/WebSockets/realtime, Demo/InternalTest classification, or Planned For-aware push
escalation.

---

## S8a Status

S8a is complete. Device registration/revoke foundation was committed in two parts plus review fix:

- `1fcf933`
- `4c170ca`
- `2b54640`

Implementation detail and reconciliation belong in
`docs/build-log/063-session-8-notification-device-foundation.md`.

---

## S8b Status

S8b is complete and committed in `e09258e`.

- Added `GET /me/badge` personal badge endpoint.
- Added Keep badge service/persistence and focused integration coverage.
- S8b details belong in `docs/build-log/063-session-8-notification-device-foundation.md`.

---

## S8c Status

S8c is complete and committed in `2c7b911`.

- Added push adapter abstraction and no-op delivery.
- Added non-sensitive payload/display mapping and candidate/routing foundation.
- S8c details belong in `docs/build-log/063-session-8-notification-device-foundation.md`.

---

## Session 8 Slice Order

- **S8a:** Device table + `/me/devices/{appInstallationId}` register/revoke API — complete.
- **S8b:** Server-derived personal OS badge endpoint — complete.
- **S8c:** Push abstraction, no-op adapter, payload/display mapping, and candidate/routing
  foundation — complete.
- **S8d:** Limited push-worthy mutation hooks — next.
- **S8e:** Session 8 ledger and regression gate.

---

## Carry-Forward Boundaries

- Real push delivery cannot be enabled until Demo/InternalTest suppression and account
  classification are implemented.
- Pilot notification posture must be explicit: either real APNs/FCM is tested end to end with
  suppression confirmed, or no-op/test adapter posture is documented and users are trained that push
  is not live yet.
- Keep sends no backend SMS/email to customers in V1. Native `sms:`, `tel:`, and `mailto:` handoff
  remains operator-initiated on the user's device.
- One durable public intake link; no ordinary pause/resume. Exceptional replacement is
  transactional and warns about stale links.
- Public-intake abuse posture before pilot: bounded validation, trusted-IP/rate-limit proof,
  Spam/Test terminal classification before notifications, token-safe logs, and internal emergency
  path.
- Terminal customer pages expire 30 days after Closed/Cancelled; active and Resolved do not.
- Native is the operator quick-action surface; PWA is the Owner/Admin command center/workbench.
- Keep remains fresh-not-realtime for V1: refetch after writes, focus/resume sync, pull-to-refresh,
  bounded polling, and later push for urgent off-screen work.

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
