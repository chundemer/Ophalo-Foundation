# Session Log — OpHalo Foundation

**Last updated:** 2026-07-10 (S22p4 complete — IntakeUrgency + ContactPreference exposed on operator detail and request list; 98 unit · 101 integration all green)
**Branch:** `main` tracking `origin/main`
**Last green baseline:** Targeted intake baseline — 66 intake unit · 25 intake integration confirmed; full suite pending (1 pre-existing KeepG5 fluke excluded)
**Next free ADR:** ADR-431
**Current session:** Session 22 — Day-Zero Settings Redesign, Intake Sharing, And Service Location Plan

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

**Current build log:** `docs/build-log/076-session-22-guided-setup-intake-and-service-location.md`
**Last completed prior-session build log:** `docs/build-log/075-session-21-attention-guidance-resolution-metadata.md`
**Readiness working doc:** `docs/pilot-readiness-decision-questions.md`
**Bug/gap tracker:** `docs/pilot-readiness-bug-tracker.md`
**Foundation roadmap:** `docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md` section 9.1
**Current session:** Session 22 — Day-Zero Settings Redesign, Intake Sharing, And Service Location Plan
**Current slice:** S22p5/remaining slices (see Next Session Brief)

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

### Current Direction

Session 22 is now in the customer request/intake metadata finishing lane. Historical S22 detail is
archived in `docs/build-log/076-session-22-guided-setup-intake-and-service-location.md`; use this
session log as the handoff brief only.

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

### Next Session Brief

S22p4 complete. Current work: **Customer page copy alignment** (lightweight discovery, next slice).

#### S22p1 — Intake Form UI Polish ✓ complete (2026-07-10)
`IntakeForm.tsx` only. No backend, no migration, no test changes.
- Main form H1: `font-serif text-2xl font-semibold leading-tight text-foreground sm:text-[28px]`
- Terminal H1s (`Request submitted.`, `This link is not available.`, `You're signed in…`): `font-serif` added, size unchanged
- Removed all 4x `border-t border-[var(--ophalo-border)] pt-5` dividers; sections wrapped in `<div className="mt-6 space-y-7">` with `<section>` children; submit area uses `mt-6` only
- Description helper text: "Include what happened, when it started, and anything urgent."
- State `<select>`: `text-base` added (iOS Safari zoom guard)
- Submit `<KeepButton>`: `min-h-[42px]` (minimum tap target)

#### S22p2 — Intake Urgency Field ✓ complete (2026-07-10)
New `IntakeUrgency` enum (Routine/Soon/Urgent), default Routine. Piped through intake submission:
`PublicIntakeRequest.Urgency`, `CreateKeepPublicIntakeCommand.IntakeUrgency`,
`CreateKeepPublicIntakeService`, and `KeepRequest.CreateFromCustomerIntake`. EF config + migration
`AddUrgencyToKeepRequest` complete. Frontend: urgency select in `IntakeForm.tsx` after description,
with urgent helper copy and quiet safety disclaimer. 66 unit · 25 intake integration green after
S22p3. Operator display deferred to S22p4.

#### S22p3 — Preferred Contact Method ✓ complete (2026-07-10)
New `ContactPreference` enum (NoPreference/TextMessage/PhoneCall/Email) on `KeepRequest`, default
NoPreference. Piped through intake submission: `CreateKeepPublicIntakeCommand`, service, API body
(`PublicIntakeRequest`), both token and slug handlers in `Program.cs`. EF config + migration
`AddContactPreferenceToKeepRequest`. Frontend: preference select in contact section of `IntakeForm.tsx`;
email becomes visible and `required` when Email selected. 66 unit · 25 intake integration green.
Operator display deferred to S22p4 (would have exceeded batch gate).

#### S22p4 — Intake Metadata Operator Display ✓ complete (2026-07-10)
Exposed `IntakeUrgency` and `ContactPreference` on operator detail and request list. Backend: new
fields on `KeepRequestDetailResult` and `KeepRequestSummary`, mapped with exhaustive enum-string
methods in `KeepRequestDetailMapper` and `GetKeepRequestListService`. Frontend: request detail shows
urgency alert ("Customer marked this urgent/soon.") when source is `public_intake` and urgency ≠
routine, and preferred contact line when source is `public_intake`; request rows show intake urgency
and contact preference cues because operators on the road use the list as their primary view.
98 unit · 101 integration all green.

#### S22p5 — Staff Auth Early Block (deferred)
No session context is available on the public Next.js intake page at load time without an
authenticated API call. Post-submit `staff_not_permitted` guard (line 159 of `IntakeForm.tsx`)
remains the correct defensive fallback. Do not block S22p1–S22p4 on this. If pursued, requires a
new lightweight `GET /keep/public-intake/session-check` endpoint and a `useEffect` call in
`IntakeForm` — document as a follow-up backend/UI slice.

#### Remaining S22 slices (unchanged)
1. **Customer page copy alignment (lightweight discovery):** Inspect `CustomerTrackerView.tsx` for
   any capability hint, cancellation copy, or label misalignment introduced by S22 decisions. Expected
   to be copy-only or a no-op; do not assume changes are needed before looking.
2. **S22f — Mobile Carry-Forward Preflight:** See build log 076 for scope (service-location fields
   mobile must consume, active-intake share utility feasibility, no new mobile settings/admin scope).
3. **S22g — Documentation Reconciliation:** ADR-295, ADR-375, ADR-383 and response policy placement.
4. Docs/index reconciliation.
5. **Pre-deployment cleanup:** Build log 077 is deferred until customer request page work and testing
   are complete.

Historical mobile context lives in `docs/build-log/071-session-17-review-safe-native-product-foundation.md`.

## Carry-Forward Boundaries

- Real APNs/FCM provider implementation remains future work.
- Demo scenario packs, demo reset UI, and admin/internal classification management remain deferred.
- Classification is operational/reporting/safety posture, separate from commercial lifecycle.
- Public signup cannot create Demo/InternalTest accounts.
- Production push delivery must stay suppressed for Demo/InternalTest accounts.
- Keep sends no backend SMS/email to customers in V1; native `sms:`, `tel:`, and `mailto:` handoff
  remains operator-initiated on the user's device.

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
