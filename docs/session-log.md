# Session Log — OpHalo Foundation

**Last updated:** 2026-07-17
**Branch:** `main` tracking `origin/main`
**Last green code baseline:** R88f-c-panel-3. Slice-level automated evidence is recorded in the
completed R88 entries below; integrated desktop/mobile acceptance verification has not begun.
**Deployment posture:** Not deployment-ready. Active launch gaps remain in
`docs/pilot-readiness-bug-tracker.md`.
**Next free ADR:** ADR-446
**Current session:** Outstanding launch gaps and request-list triage. Verification is deferred until
those selected corrective slices are complete.

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

## Current Work — Outstanding Launch Gaps And Request List

**Tracker:** `docs/pilot-readiness-bug-tracker.md` — active GAP-016 through GAP-027.

**Current order:**

1. Execute the approved GAP-027 Request List parity slice under Build 087; GAP-026 and the
   remaining P0/P1 New Request blockers follow as separately bounded slices.
2. Implement and commit each approved slice with decision-record, contract, focused-test, and
   relevant build/typecheck verification.
3. Reassess the active tracker. Only after the selected blocker set is resolved or deliberately
   deferred, hold a pre-work discussion for the manual verification pass in
   `docs/build-log/089-launch-verification-pass.md`.
4. Run desktop operational verification first, then the dedicated real-device mobile PWA gate.

**GAP-027 approved implementation scope (2026-07-17):** Build 087 already locks the product
behavior; GAP-027 is an implementation-parity correction, not a new design or lifecycle decision.

1. Reproduce and diagnose any displayed operational-overdue versus Needs Attention count mismatch
   against the current API before changing a query or count contract.
2. Render one server-status pill and at most one highest-priority operational exception per row;
   merge its deadline into that exception and move other signals to quiet metadata.
3. Suppress response-SLA, overdue-follow-up, and planned-date alarms on Closed/Cancelled rows;
   preserve the existing unresolved-negative-feedback / Feedback Review exception. Do not treat
   Resolved (Work completed) as terminal.
4. Render future planned/follow-up dates as unbordered quiet metadata.
5. Apply Build 087's existing permitted primary-action priority: at most one promoted action and
   one relevant secondary action; no ambiguous `Next: X or Y`, repeated Add Note, or redundant
   Open Detail row action.
6. Verify count/alert parity, terminal feedback behavior, row hierarchy, future timing, permitted
   action selection, mobile/touch/keyboard behavior, and long-data layouts with focused tests and
   the relevant quality gates.

**Change-control rule:** Before and after each file change, confirm the controlling ADR/build-log,
affected API/UI contract, permissions and terminal-state behavior, focused automated coverage, and
relevant build/typecheck results. Do not expand a bounded gap fix into a new product policy without
an explicit decision.

**GAP-027 status (2026-07-17):** `needs_attention` list/count parity, Default Queue excluding Closed
unresolved-feedback (Feedback Review only), and the Request List row-hierarchy/action-priority
rework are committed and pushed to `main` (`6a33e6e`, `9359bf0`). A follow-up correction round —
canonical-urgency exception selection overriding action order for due/overdue follow-ups, and
Button-Hierarchy-Locked brand colors on row quick actions (teal communication, navy-outline
secondary, neutral bookkeeping, red destructive; no amber buttons) — is implemented and passing
focused tests, pending Christian's visual acceptance before commit. Verified baseline: 32
`KeepRequestListB5Tests`, 113 KeepRequestList/FeedbackReview/AcknowledgeAttention integration tests,
14 architecture tests, 891 unit tests, 19 `ophalo-app` frontend tests, TypeScript clean. One
pre-existing, unrelated integration failure noted and left untouched:
`KeepCustomerPageTests.GetCustomerPage_ValidRequest_DoesNotExposeOperatorInternalFields`.

Locked direction:

- Customer self-service is the primary New Request route; staff-entered request capture is the
  visible fallback for cases where the customer cannot submit it. The pre-capture handoff is the
  durable business public-intake link, never a per-request private customer page. ADR-445 locks the
  remote-caller text flow: confirm only the caller phone, then use a desktop opaque QR handoff or a
  mobile direct SMS draft addressed to that caller.
- Owner/Admin receive public-intake handoff controls; Operators do not receive link-management
  controls.
- The Locked Responsive-PWA Strategy in `docs/pilot-readiness-bug-tracker.md` applies: one shared
  PWA controller and behavior, with desktop QR handoffs and mobile direct external-channel actions.
  Keep does not send SMS, and raw phone/message data never appears in a QR payload.
- Staff service location remains optional under the existing GAP-006 decision, but a supplied
  location must be complete and valid.
- Request Detail keeps one shared controller and shared behavioral panels; desktop/mobile layout
  composition is extracted before further Request Detail launch changes.

Resolved handoff decisions: ADR-444 locks the normalized caller-phone policy; ADR-445 locks the
pre-capture caller-text, desktop QR, and mobile-direct behavior.

### Completed R88 Record

The entries below preserve the completed remediation history. Current execution order is the active
gap plan above; do not treat the historical R88 ordering as the next-task list.

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
9. **R88f-a/b — GAP-018 pre-capture handoff groundwork.** ✓ Committed 2026-07-16, superseded by
   ADR-445. Repaired by R88f-c-repair-a below.
10. **R88f-c-repair-a — GAP-018 backend contract repair.** ✓ Committed 2026-07-16. Adds
    `CustomerPhone` to entity/migration/service/endpoint; 3-step ADR-444 phone validation
    (`Required` → `InvalidCharacters` → `InvalidFormat`); `IOptions<MagicLinkSettings>` for
    `PublicBaseUrl`; `App.NotConfigured` guard; `/keep/s/{slug}` URL; backward-safe
    `NOT NULL DEFAULT ''` migration with blank-phone legacy 404; `IsDeleted` → `DeletedAtUtc == null`
    query fix. 1,117 unit + 14 integration tests green.
11. **R88f-c-repair-b — GAP-018 web handoff page repair.** ✓ Committed 2026-07-17. Two
    `ophalo-web` files: `page.tsx` reads `customerPhone` from API; `IntakeSmsHandoffView.tsx`
    builds `sms:{phone}${sep}body=...`. Mechanical port of the sibling `keep/share-sms`
    pair's proven pattern. TypeScript clean (`npm run typecheck`).
12. **R88f-c-panel — GAP-018 Owner/Admin New Request handoff panel.** Split into two slices because
    the panel needed a response field the backend did not yet return.
    - **R88f-c-panel-1 — backend response-contract slice.** ✓ Committed 2026-07-17 (`b6e223e`).
      `CreateIntakeSmsHandoffService` / `CreateIntakeSmsHandoffResult` and the
      `POST /keep/setup/intake/sms-handoff` handler in `KeepEndpoints.cs` add `customerPhone`
      (canonical) and `messageBody` to the response, alongside the existing
      `handoffUrl`/`expiresAtUtc`. 23 unit tests, 14 integration tests green.
    - **R88f-c-panel-2 — panel slice.** ✓ Committed 2026-07-17. 8 `web/ophalo-app` production
      files: `App.tsx` (adds `isOwnerOrAdmin` and `onNavigateSettings` props to `QuickCapture`,
      reusing existing `navigateToSettings`), `QuickCapture.tsx` (initial stage `handoff` for
      Owner/Admin vs. existing `lookup` for Operator; `followUpPrefill` bypass unchanged regardless
      of role), `quick-capture/utils.ts` (`Stage` gains `{ kind: "handoff" }`), new
      `quick-capture/HandoffPanel.tsx`, `lib/apiClient.ts` (`createIntakeSmsHandoff`),
      `lib/apiClient.types.ts` (`CreateIntakeSmsHandoffResult`), `mocks/mockApiClient.ts`,
      `mocks/fixtures.ts`. Phone field labeled "Customer's mobile number for a text link" with a
      prompt to confirm it can receive texts; desktop QR renders `handoffUrl` (pattern mirrors
      `SuccessPanel.tsx`); mobile shows an explicit **Open Text Message** button once the POST
      resolves (no auto-navigation), built from `customerPhone`/`messageBody` using the same iOS
      `&` / other `?` separator logic as `IntakeSmsHandoffView.tsx`; **Enter request for customer**
      fallback to the `lookup` stage stays immediately visible; `hasActiveLink === false` empty
      state routes through `onNavigateSettings`. The originally included in-panel durable customer
      QR is superseded by the R88f-c-panel-3 correction below. TypeScript clean
      (`npm run typecheck`).
13. **R88f-c-panel-3 — Text a Link single-QR correction.** ✓ Committed 2026-07-17. 1 production
    file: `quick-capture/HandoffPanel.tsx`. Removed the durable customer-facing QR block and its
    now-unused `durableSlugUrl`/`publicBaseUrl` computation; the panel shows only the opaque staff
    QR after **Prepare text** succeeds, and the Owner/Admin scans it and sends the draft from their
    phone. Also relabeled the action button `Send`/`Sending…` → `Prepare text`/`Preparing…` per the
    ADR-445 consequence that the label must describe preparation, not sending. **Enter request for
    customer** fallback and the `hasActiveLink === false` empty state are unchanged. TypeScript
    clean (`npm run typecheck`).
14. **R88g — Integrated PWA verification.** Superseded as a single undifferentiated pass by
    `docs/build-log/089-launch-verification-pass.md`: Gate 1 is desktop operational verification
    after the selected gap work; Gate 2 is dedicated real-device mobile PWA verification.

Do not begin manual verification until the active-gap plan above is ready. The verification pre-work
discussion occurs in a fresh session; implementation discovery/review is not carried into it.
Every R88 slice requires its own bounded brief section, file-level preflight, proportionate
verification, Christian's approval of the completed diff, and a commit before the next slice begins.

**Public intake client-validation posture (R88e-a):** `IntakeForm.tsx` performs no client-side
digit normalization; server-side `PhoneNormalizer` is authoritative. R88e-a updates the
`CustomerPhoneInvalidFormat` error translation to `"Please enter a 10-digit phone number."` — no
additional client-side digit-counting is added to the public intake form.

## Build 087 — Request List V1 Launch Pass

**Controlling brief:** `docs/build-log/087-request-list-v1-launch-pass.md`
**Status:** GAP-027 implementation parity is approved under the locked Build 087 decisions; begin
with its evidence-led preflight. GAP-026 follows as a separate bounded slice. Do not begin broad
manual verification while their selected corrective scope is incomplete.

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
