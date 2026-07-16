# Session Log — OpHalo Foundation

**Last updated:** 2026-07-16
**Branch:** `main` tracking `origin/main`
**Last green baseline:** Build 086 — 1,073 unit tests, 14 architecture tests, and TypeScript clean
for `ophalo-app` and `ophalo-web` (verified 2026-07-16).
**Next free ADR:** ADR-442
**Current session:** Build 087 — Request List V1 Launch Pass

---

## Session Protocol

This file is the current execution brief, not the historical build archive. Completed implementation
detail lives in `docs/build-log/`; authoritative decisions live in
`docs/decisions/decision-index.md`.

For every implementation slice:

- Read this file and the active build brief before editing.
- Use targeted `rg` during preflight to confirm named signatures, existing tests, and compile-impact
  callers. Do not rediscover already-locked architecture or product decisions.
- Inspect current signatures, failure modes, action-policy seams, and tests before editing.
- Present the file-level gate before writing production code.
- Keep the hard slice gate unless Christian explicitly approves otherwise: at most three mutation
  families, eight production files, and twelve total changed files including tests/docs.
- Preserve fail-closed account, row, action, membership, public-token, and optimistic-concurrency
  behavior.
- Add focused regression tests and run proportionate broader checks.
- Self-review for policy drift, accidental visibility expansion, token leakage, untested direct-ID
  paths, stale docs, and unrelated scope.
- Commit only after Christian approves the completed diff.

## Build 086 — Request Detail V1 Launch Pass ✓ Complete

**Controlling brief:** `docs/build-log/086-request-detail-v1-launch-pass.md`
**Closed:** 2026-07-16

All five sessions committed to `main` and verified:

- **R86a** — Unified communication composer (2026-07-15)
- **R86b** — Summary-first timing and Actions/Context sidebar (2026-07-15)
- **R86c** — State-aware Work Done hierarchy and inline confirmation
- **R86d** — Skeleton/Retry, modal a11y, and 12px type-scale floor
- **R86e** — Integrated verification and closeout (2026-07-16)

**R86e evidence:** 1,073 unit tests passed, 14 architecture tests passed, TypeScript clean on
`ophalo-app` and `ophalo-web`.

**Deferred to pre-deployment testing:** full manual timing matrix (unset/configured/save/clear/
conflict/stale-detail), screen reader and keyboard focus pass, long-data and 200% zoom checks,
desktop/mobile conflict paths. Christian to complete before Vercel deployment.

## Current Work — Build 087 Request List V1 Launch Pass

**Controlling brief:** `docs/build-log/087-request-list-v1-launch-pass.md`
**Status:** Not yet started — read the brief before beginning.

## Standing Boundaries

- Keep does not send backend customer SMS or ingest SMS replies in V1.
- Platform email/Resend is in scope for auth/member flows, and ADR-432 allows one narrow fail-soft
  public-intake tracker-link email when the customer supplies email.
- Broad automated customer email/SMS notification workflows, notification preferences, quiet hours,
  proof-of-send, and delivery ledgers remain deferred.
- Google Voice remains owner-managed and is indirectly supported through **Copy Message** only.
- Preserve the Request Detail customer/internal visibility boundary. Internal notes and internal
  timing never become customer-visible.
- Do not change server action policy or lifecycle transitions merely to simplify PWA presentation.

Always preserve:

- fail-closed account, membership, action-policy, public-token, and concurrency behavior;
- raw-token non-disclosure in logs, diagnostics, persisted frontend state, or long-lived UI state;
- `ophalo-app` as the authenticated Keep workbench;
- `ophalo-web` as the public/customer-facing web surface;
- `OpHalo.Api` as the authority for auth, sessions, account creation, rate limiting, email,
  authorization, and persistence.

Topology:

- Production target: `ophalo.com`/`www.ophalo.com` → `ophalo-web`, `app.ophalo.com` →
  `ophalo-app`, `api.ophalo.com` → `OpHalo.Api`.
- Local: `ophalo-web` `http://localhost:3000`, `ophalo-app` `http://localhost:5173`,
  `OpHalo.Api` `http://localhost:5092`.
- Pilot cap: `SignupDefaults:MaxPilotAccounts=15`.
- `OperatorBaseUrl` is retired; invite links use `{PublicBaseUrl}/invite/accept`.

## After Build 086 And Build 087

1. Conduct a decision-first marketing and onboarding launch pass.
2. Deploy and validate a Vercel production candidate.
3. Test the PWA on real iPhone and Android devices before promotion, then run a production smoke
   test after deployment.
4. Shift primary implementation attention to native mobile public-release readiness and Apple/Google
   store approval.

Historical implementation detail remains in `docs/build-log/`. Do not re-add it to this active
execution brief.
