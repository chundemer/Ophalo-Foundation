# Session Log — OpHalo Foundation

**Last updated:** 2026-07-02 (S14 complete; Session 15 pilot-readiness bug/gap closure is next)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** 939 unit · 14 arch · 713 integration = 1,666 total, 0 failures (1 pre-existing KeepG5 fluke excluded)
**Next free ADR:** ADR-385
**Current session:** Session 15 — Pilot Readiness Bug And Gap Closure

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

**Current build log:** TBD — create Session 15 build log before implementation
**Last completed build log:** `docs/build-log/068-session-14-ophalo-web-front-door.md` (Session 14)
**Readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Bug/gap tracker:** `docs/pilot-readiness-bug-tracker.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1
**Current session:** Session 15 — Pilot Readiness Bug And Gap Closure
**Current slice:** S15a — Pilot Loop Bug Triage And First Fix Set
**Current slice status:** Next. Pre-work complete enough for mechanical preflight; start from
`docs/pilot-readiness-bug-tracker.md`.

Session 13 is complete and should be treated as historical context only. Completed Session 13 details
live in `docs/build-log/067-session-13-pwa-workbench.md`; do not carry Session 13 implementation
notes forward in this execution brief.

Session 14 is complete and should be treated as historical context. `ophalo-web` now owns the public
front door and browser token pages: homepage, About, Pilot, Privacy, Terms, `/signin`, `/start`,
`/auth/check-email`, `/auth/exchange`, `/auth/exchange/error`, `/invite/accept`,
`/invite/accept/error`, and `/keep/intake/{token}`. Completed Session 14 detail lives in
`docs/build-log/068-session-14-ophalo-web-front-door.md`.

### Current Direction

- Session 15 should close the highest-risk pilot workflow bugs and gaps before new feature expansion.
- Use `docs/pilot-readiness-bug-tracker.md` as the live source of bug/gap status.
- `ophalo-app` remains the authenticated Keep workbench.
- `OpHalo.Api` remains the only authority for auth, sessions, account creation, rate limiting, email,
  authorization, and persistence.
- Preserve fail-closed account, membership, action-policy, public-token, and concurrency behavior.
- Production topology: `ophalo.com`/`www.ophalo.com` -> `ophalo-web`,
  `app.ophalo.com` -> `ophalo-app`, `api.ophalo.com` -> `OpHalo.Api`.
- Local topology: `ophalo-web` `http://localhost:3000`, `ophalo-app` `http://localhost:5173`,
  `OpHalo.Api` `http://localhost:5092`.
- Pilot cap for production launch: `SignupDefaults:MaxPilotAccounts=15`.
- `OperatorBaseUrl` is retired from active settings/config/test factories/runbooks; invite links now
  use `{PublicBaseUrl}/invite/accept`.

### S15a Handoff Brief

Classify S15a as mechanical implementation preflight plus focused bug fixes. Do not rediscover the
S14 public-front-door decisions. Use targeted checks against the tracker and current code.

Recommended first fix set:

- `BUG-001` — share-intent stale version / false `409`;
- `BUG-002` — `ApiError.extensions` flattened ProblemDetails parsing;
- `BUG-004` — exchange client duplicate-email code mismatch;
- `BUG-005` — post-capture Copy Tracker Link does not record share intent;
- verify `GAP-003` — public intake setup exposes a usable customer-facing URL.

Keep `GAP-001` assign/responsible/watcher management and `GAP-002` customer tracker page visible as
larger follow-on slices; they may exceed a small first bug-fix batch.

Likely S15a file impact:

- `web/ophalo-app/src/lib/apiClient.ts`
- `web/ophalo-app/src/pages/RequestDetail.tsx`
- `web/ophalo-app/src/components/NeedsShareBanner.tsx`
- `web/ophalo-app/src/components/QuickCapture.tsx`
- `web/ophalo-web/src/app/auth/exchange/ExchangeClient.tsx`
- `web/ophalo-app/src/pages/Settings.tsx` if `GAP-003` is pulled into S15a
- `docs/pilot-readiness-bug-tracker.md`
- new Session 15 build log under `docs/build-log/`

Expected verification:

- focused frontend typecheck/build command for each touched app;
- relevant API/integration tests only if backend behavior changes;
- `git diff --check`;
- update `docs/pilot-readiness-bug-tracker.md` statuses as fixes land.

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
- Deployment still requires correct Cloudflare/Vercel/Railway topology, trusted-proxy posture, and
  token-redaction configuration even though application-level proofs are complete.
- Persistent local PostgreSQL setup/migration/smoke runbook is verified against local `ophalo_local`
  in Docker; guarded reset remains documented but was not exercised.
