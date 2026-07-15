# Session Log — OpHalo Foundation

**Last updated:** 2026-07-15
**Branch:** `main` tracking `origin/main`
**Last green baseline:** Build 085 — 1,073 unit tests, 14 architecture tests, and TypeScript clean
for `ophalo-app` and `ophalo-web` (verified 2026-07-14; re-run proportionately before closeout).
**Next free ADR:** ADR-442
**Current session:** R86b — Summary-first timing and Actions/Context sidebar

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

## Current Work — Build 086 Request Detail V1 Launch Pass

**Controlling brief:** `docs/build-log/086-request-detail-v1-launch-pass.md`
**Status:** R86a complete — R86b next.
**Follow-on brief:** `docs/build-log/087-request-list-v1-launch-pass.md` — do not begin until Build
086 is complete and its closeout evidence is recorded.
**Production roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md`, Section 9.0.

### R86a — Unified communication composer ✓

**Implemented 2026-07-15. TypeScript clean.**

Files changed:
- `web/ophalo-app/src/pages/request-detail/UnifiedComposer.tsx` — new; two-tab composer with
  `role="tablist"`, proper ARIA, always-mounted panels using `hidden` for inactive, explicit label
  for internal-note textarea, `Cmd/Ctrl+Enter` on both textareas, default tab driven by
  `canSendBusinessUpdate`, no-render when neither action permitted.
- `web/ophalo-app/src/pages/request-detail/BusinessSection.tsx` — `composerMode` prop; submit
  logic extracted to `doSubmit()`; composerMode suppresses outer card and title, shows persistent
  "Visible to customer" label; `Cmd/Ctrl+Enter` via `handleKeyDown`.
- `web/ophalo-app/src/pages/RequestDetail.tsx` — `BusinessUpdateSection` replaced with
  `UnifiedComposer`; local `InternalNoteCard` definition deleted; both sidebar/mobile
  `InternalNoteCard` renders removed.

### Subsequent Build 086 Sessions — Not Yet Active

1. **R86b:** summary-first timing and Actions/Context sidebar.
2. **R86c:** state-aware Work Done hierarchy and inline confirmation.
3. **R86d:** skeleton/Retry, readable type, contrast, focus, and long-data resilience.
4. **R86e:** integrated verification and Build 086 closeout.

The full scope, boundaries, and acceptance evidence for each are in Build 086. Advance one session
only after the preceding session is green and its results are recorded here.

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
