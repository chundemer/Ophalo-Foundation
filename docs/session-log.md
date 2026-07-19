# Session Log — OpHalo Foundation

**Last updated:** 2026-07-19 (GAP-036 resolved; GAP-034 resolved)
**Branch:** `main` tracking `origin/main`
**Deployment posture:** Not deployment-ready. The active launch gate is
`docs/pilot-readiness-bug-tracker.md`.
**Current work:** GAP-036 and GAP-034 are resolved (uncommitted, pending Christian's review). Next
code slice not yet selected — see `docs/pilot-readiness-bug-tracker.md` and the V1 Sequence below.

Detailed historical implementation evidence belongs in `docs/build-log/`; active requirements and
status belong in `docs/pilot-readiness-bug-tracker.md`; decisions belong in
`docs/decisions/decision-index.md`.

## Session Protocol

For every implementation slice:

- Read this file and the relevant active tracker entry before editing.
- Preflight named signatures, authorization, failure modes, and focused tests with `rg`.
- Present the file-level gate before production edits. Unless Christian explicitly overrides it,
  keep to three mutation families, eight production files, and twelve total changed files.
- Preserve fail-closed account, membership, action-policy, public-token, privacy, and concurrency
  behavior.
- Add focused regression coverage, run proportionate checks, self-review visibility/token policy,
  and commit only after Christian approves the completed diff.

## Current Baseline

- **GAP-035 / R90c-1 through R90c-4:** complete in `8aba5dc`, `0f70437`, `3490cb1`, and
  `c1a1379`. All normal browser auth/invite/recovery states now use `AuthShell`; mobile exchange
  uses `AuthShell bare` and remains ADR-390 sterile.
- **GAP-034 `/start` conversion redesign:** resolved (uncommitted) in
  `web/ophalo-web/src/app/start/page.tsx`. Contained two-panel desktop composition (navy identity/
  value panel + white form panel, compact stacked header on narrow widths), truthful `Start your
  Keep pilot` copy and CTA, friendly editable time-zone display, and a client-side `/auth/me`
  session check that redirects an already-authenticated business user to `NEXT_PUBLIC_APP_BASE_URL`
  with no start-form flash and an explicit retry state on session-check failure. `AuthShell.tsx` and
  the backend `/auth/start` contract were not changed. Live authenticated-redirect verified.
- **GAP-033 / R90b public trust:** public identity fields and projections are complete. Known public
  intake/tracker/expired pages can render business name, hosted logo, website, and public phone;
  unknown tokens remain non-enumerating. Successful public intake goes directly to the tracker with
  a one-time welcome state. Automatic tracker-link email was removed.
- **GAP-033 deployment evidence still required:** actual browser intake submission, an actual
  expired-tracker visual check, and a known-business OffSeason behavior/banner decision.
- **GAP-036 / GAP-036a + GAP-036b:** resolved in `8085971` and `014bae5`, with focus-containment
  follow-up `aae9257`. Owner/Admin can configure
  Logo URL/Website URL with a shared unsaved-draft customer preview (GAP-036a). Public-link
  replacement now requires an exact, case-sensitive `REPLACE` value in an accessible, focus-trapped
  destructive dialog (Tab contained, Escape/backdrop blocked while in flight, focus returns to the
  trigger on close); the server independently re-validates the same confirmation before revoking the
  active link — a missing or empty request body still reaches that gate rather than being rejected
  by model binding (GAP-036b). Live authenticated desktop/mobile keyboard verification is complete.
- **GAP-042:** newly recorded authenticated-workspace identity gap. Request List and Request Detail
  need restrained account business-name context so work visibly belongs to the business, without
  leaking it to public routes or repeating it on each request row.
- **GAP-043:** newly recorded request-list scale/readiness gap. Cursor paging already exists (50-row
  default, protected next cursor, Previous/Next stack); decide and verify its real-work UX rather
  than replacing it on the assumption that no pagination exists. Coordinate any implementation with
  GAP-041’s stable first-load queue transition.
- **GAP-044 through GAP-046:** newly recorded request-list operating gaps. Expose authorized
  closed/cancelled history through the existing protected contract; replace opaque `Default Queue`
  language with factual owner orientation; and make applied search/filter state visible and easy to
  recover. GAP-041 now explicitly includes arrow-key/roving-focus tab behavior.

## Locked Public-Trust Boundaries

- Public pages are business-first; OpHalo Keep is secondary. Hosted logo/website/public phone are
  allowed only for known businesses. Never expose public email.
- URLs are input-validated absolute HTTPS values, not ownership-verified domains. Unknown/invalid
  capability tokens never disclose business identity or contact data.
- No raw tokens in metadata, logs, diagnostics, persisted frontend state, or durable UI. Tracker
  pages remain `noindex` with restrictive referrer handling.
- Keep does not send backend customer SMS, ingest SMS replies, or automatically email a tracker link
  after intake. A business may use its own later communication to share the tracker link.

## V1 Sequence After GAP-036

1. GAP-034 full `/start` conversion redesign — resolved, uncommitted.
2. GAP-037 founder/internal weekly value report; GAP-038 PWA feedback/help; GAP-039 redacted
   production observability; GAP-040 marketing accuracy/assets/deployment readiness; GAP-042
   authenticated business context; GAP-043 deliberate request-list scale UX; GAP-044 history
   access; GAP-045 queue orientation; GAP-046 search/filter clarity.
3. Finish or explicitly defer all selected P0/P1 tracker items; collect the remaining GAP-033
   evidence; run Build 089 desktop, then real-device mobile PWA verification.
4. Deploy and validate the V1 production candidate: marketing and app HTTPS/domain/cookie/DNS,
   monitoring, and production smoke checks.
5. Lock and submit the native release against the stable deployed service. The store gate includes
   signing/EAS builds, permissions/privacy labels, screenshots, reviewer access/notes,
   Universal/App Links, direct account deletion, GAP-038 native parity, and real-device production
   checks. Christian forms the LLC and completes business setup during store review. Go live only
   after store approval and the final production-readiness decision.
