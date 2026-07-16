# Session Log — OpHalo Foundation

**Last updated:** 2026-07-16
**Branch:** `main` tracking `origin/main`
**Last green code baseline:** R88e-c — 1,093 unit tests, 14 architecture tests, 15 mobile unit tests (verified 2026-07-16).
Not deployment-ready; GAP-016–GAP-019 still active.
**Next free ADR:** ADR-445
**Current session:** New Request launch blockers — decisions and implementation preflight

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

**Launch-readiness note:** Manual verification is still required before Vercel deployment. Testing
also surfaced New Request defects/workflow gaps and a Request Detail maintainability seam, now
tracked as **GAP-016–GAP-019** in
`docs/pilot-readiness-bug-tracker.md`; resolve their selected scope before resuming page-pass work.

## Current Work — New Request And Request Detail Launch Blockers

**Tracker:** `docs/pilot-readiness-bug-tracker.md` — GAP-016 through GAP-019
**Status:** GAP-019 preflight may begin now. The Request Detail, sharing, and public-intake handoff
work may follow; staff fallback capture remains blocked only on the stated phone-policy decision.

Locked direction:

- Customer self-service is the primary New Request route; staff-entered request capture is the
  visible fallback for cases where the customer cannot submit it. The pre-capture handoff is the
  durable business public-intake link, never a per-request private customer page.
- Owner/Admin receive public-intake handoff controls; Operators do not receive link-management
  controls.
- The Locked Responsive-PWA Strategy in `docs/pilot-readiness-bug-tracker.md` applies: one shared
  PWA controller and behavior, with desktop QR handoffs and mobile direct external-channel actions.
  Keep does not send SMS, and raw phone/message data never appears in a QR payload.
- Staff service location remains optional under the existing GAP-006 decision, but a supplied
  location must be complete and valid.
- Request Detail keeps one shared controller and shared behavioral panels; desktop/mobile layout
  composition is extracted before further Request Detail launch changes.

Open decision before staff fallback implementation:

1. Launch phone scope: normalized 10-digit North American validation (recommended), or an explicit
   international phone/country-selection model.

### Required Resolution Order

**Controlling remediation brief:** `docs/build-log/088-pwa-launch-readiness-remediation.md` — create
and approve before R88a implementation. Build 087 remains paused.

1. **R88a — GAP-019 Request Detail layout decomposition.** Extract the shared controller from
   responsive desktop/mobile composition before touching Detail contact, sharing, timing, or
   location UI. Structural only: no API, lifecycle, or permission behavior changes.
2. **R88b — Request Detail contact and communication handoffs.** Implement the locked header
   contact strip, desktop QR-to-call/mobile direct-call pattern, direct-email behavior, and
   separate explicit contact-outcome logging.
3. **R88c — Request Detail timing and location clarity.** Correct service-location warning copy;
   add Follow-up versus Planned-date guidance; make the Follow-up `Other` note requirement visible,
   prevented client-side, and specifically reported on server rejection.
4. **R88d — Post-capture sharing and Quick Capture accessibility.** Give the success state the
   required desktop Email + Scan-to-Text QR and mobile Email + direct Text actions; do not bypass
   that state on mobile. Add Escape, initial focus, and focus restoration to the Quick Capture
   drawer.
5. **R88e-a — GAP-016 shared backend normalization + public intake client readiness.** ✓ Committed
   2026-07-16. ADR-444 locked (10-digit North American, leading `+1`/`1` stripped).
6. **R88e-b — GAP-016/GAP-017 authenticated create-address contract.** ✓ Committed 2026-07-16.
   Optional service address on business-staff create (if-any-then-all validation, US state code
   check). `LookupGate` gated to exactly 10 digits with inline error. `CaptureForm` collapsible
   address disclosure. 1,093 unit tests, 14 architecture tests green.
7. **R88e-b2 — Change-phone UX and draft preservation.** In `CaptureForm`: replace read-only phone
   display with **Change** link; `onBack(draft)` passes current form state back to `QuickCapture`.
   In `QuickCapture`: own `captureFormDraft` state; restore draft when lookup completes and re-enters
   capture. 3 production files (CaptureForm, QuickCapture, utils); manual verification in R88g.
8. **R88e-c — GAP-016/GAP-017 native mobile capture parity.** ✓ Committed 2026-07-16.
   `phoneUtils.ts` (pure): ADR-444-correct `normalizePhoneDigits` (leading-1 strip) and
   `validateAddressIfOpen` (GAP-022 required-if-open). `useQuickCapture`: 10-digit gate, address
   fields on `CreateRequestBody`. `modal.tsx`: 10-digit lookup/create gates, service address
   disclosure with required-if-open field errors, address fields forwarded on create. 15 mobile
   unit tests (vitest); manual verification deferred to R88g.
9. **R88f — GAP-018 customer self-service New Request handoff.** Implement ADR-442: make the
   durable business public-intake link the Owner/Admin handoff default and staff entry the visible
   fallback; preflight the public-intake and SMS-handoff contracts before coding.
10. **R88g — Integrated PWA verification.** Run the complete desktop and mobile PWA matrix for all
    above flows, including QR handoffs, direct external actions, draft/error retention,
    accessibility, long data, and real-device behavior. Only then resume Build 087.

R88a–R88d and R88f may preflight now. R88f is next (GAP-018 customer self-service New Request handoff).
Every R88 slice requires its own bounded brief section, file-level preflight, proportionate
verification, Christian's approval of the completed diff, and a commit before the next slice begins.

**Public intake client-validation posture (R88e-a):** `IntakeForm.tsx` performs no client-side
digit normalization; server-side `PhoneNormalizer` is authoritative. R88e-a updates the
`CustomerPhoneInvalidFormat` error translation to `"Please enter a 10-digit phone number."` — no
additional client-side digit-counting is added to the public intake form.

## Build 087 — Request List V1 Launch Pass

**Controlling brief:** `docs/build-log/087-request-list-v1-launch-pass.md`
**Status:** Paused. Do not begin while GAP-016–GAP-019 are active; return after their scope is
implemented or deliberately deferred.

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

## Remaining Path To Production

1. Resolve or deliberately defer the active New Request launch blockers and Request Detail
   maintainability seam (GAP-016–GAP-019), then resume Build 087.
2. Conduct decision-first marketing and onboarding launch passes.
3. Deploy and validate a Vercel production candidate.
4. Test the PWA on real iPhone and Android devices before promotion, then run a production smoke
   test after deployment.
5. Shift primary implementation attention to native mobile public-release readiness and Apple/Google
   store approval.

Historical implementation detail remains in `docs/build-log/`. Do not re-add it to this active
execution brief.
