# Session Log — OpHalo Foundation

**Last updated:** 2026-07-23
**Deployment posture:** Not pilot-ready.
**Source of truth for acceptance criteria:** `docs/pilot-readiness-bug-tracker.md`.

This log intentionally contains only outstanding work. Historical implementation evidence belongs in
`docs/build-log/`; locked decisions belong in `docs/pilot-readiness-decision-questions.md` and the
decision index.

## Immediate Production Access And Reliability Blockers

- **Provision and verify the first production smoke account.** The deployed database is empty, so
  the existing-member **Sign in** flow cannot create a session. Use **Get started** (`/start`) with
  a dedicated internal inbox to create the first account, receive/exchange its magic link, confirm
  `/auth/me`, and load the authenticated Keep request list. Then test the normal Sign in flow with
  that same account. Do not use a pilot account as a test fixture. Account/User rows are expected
  only after link exchange; before then, validate the pending `account_auth_codes` record instead.
  If `/start` reports success without an email, inspect its browser/API response, the pending code,
  Railway configuration, and Resend delivery activity. The current sender can return a failed
  delivery result without surfacing it to the browser or application log, so GAP-039/error-capture
  work must make this diagnosable. Public DNS is delegated to Vercel; the Resend dashboard's
  Cloudflare provider label is stale metadata, not active DNS control. Confirm that
  `Resend__FromAddress` exactly uses the verified `mail.ophalo.com` subdomain (for example,
  `OpHalo <no-reply@mail.ophalo.com>`) and that the deployed API key belongs to that Resend team.
  The observed generic Sign in error also needs a Railway/browser correlation if it reproduces: an
  unknown-email sign-in is expected to return neutral success, not a visible generic failure.
- **GAP-039 (P0): production observability and pilot health.** Implement the locked low-cost
  posture: retain Vercel Pro/Railway, use redacted free errors-only Sentry for browser/API unhandled
  errors and release identity, add health/readiness/configuration checks, safe correlation/error
  references, founder email alerts, and a post-deployment smoke-test script. No paid observability
  add-on, paid uptime/incident tool, or persistent staging stack before revenue. Preserve token and
  PII redaction.
- **Verify deployed routing and release configuration.** The operator request list is served at
  `https://app.ophalo.com/`; `/keep/requests` currently produces a Vercel 404. Validate the
  intended public/deep-link contract, Vercel environment variables, DNS/domain/cookie topology, and
  API deployment configuration before pilot access.
- **GAP-020 (P0): complete the opaque desktop call-handoff deployment gate.** Live browser and
  real-device verification remains: QR scan → dialer/fallback, expiry/invalid-token behavior,
  cache headers, iOS Safari, Android Chrome, and a non-`localhost` phone-reachable environment.
- **GAP-016 (P0): complete phone-validation parity.** Native parity and the remaining manual
  browser/device verification are still required for the ADR-444 phone-input contract.

## Open Product And Pilot-Readiness Work

### Quick Capture, Input, And Modal Safety

- GAP-017 through GAP-019: complete service-location creation/disclosure behavior, the intended
  customer-self-service handoff posture, and Request Detail layout decomposition.
- GAP-021 through GAP-027: finish international/country-code lookup compatibility, address-draft
  preservation, customer-selection draft safety, modal accessibility, customer recognition from
  request-list phone search, clear search affordance, and the remaining lifecycle-row decision.
- GAP-028 through GAP-032: resolve CSS-token validation, status/badge consistency, transient UI
  disposal safety, the authenticated-workbench error boundary, and shared modal/focus architecture.

### Public Trust, Pilot Support, And Go-Live Gates

- GAP-033: collect remaining deployed public-intake/tracker evidence, including actual browser
  intake submission, expired-tracker presentation, and the known-business OffSeason decision.
- GAP-037: deliver the founder/internal weekly value-report path.
- GAP-038: deliver authenticated Pilot Feedback plus Help & Updates, with required native parity
  before store submission.
- GAP-040: complete marketing accuracy, assets, legal/support links, and deployment-readiness work.

### Authenticated Workspace And Request Operations

- GAP-041 through GAP-046: fix first-load queue transition; add business context; decide/verify
  paging at real-work scale; expose history; clarify queue orientation; and make search/filter state
  visible and recoverable.
- GAP-047 through GAP-051: make priority failures visible; preserve deliberate tracker-share intent;
  bound follow-up prefill; add same-customer related-work context; and finish consistent North
  American phone formatting.

## Claude Work-Session Queue

Each session is one reviewable change set. Claude must read the named tracker entries first, keep
the change to the stated scope, add focused regression coverage, and run the proportionate checks.
Do not combine later sessions merely because files overlap. If a session uncovers a new production
blocker, record it in the tracker and stop for a decision rather than expanding scope.

### Phase 0 — Restore a Safe Validation Loop

| Order | Session | Scope and completion gate |
|---|---|---|
| 0.1 | First production smoke account and sign-in baseline | **Complete (manual/provider task).** Railway PostgreSQL-URL support, explicit startup migration switch, and runtime port binding are committed (`6a63d86`, `79aee3f`, `de1a8b9`). Dedicated internal smoke account created through `/start`; email delivery, link exchange, `/auth/me`, and authenticated request-list load all verified. A missing production cursor-signing secret caused Requests-workbench polling failures during verification (OPS-007) and has been resolved. Normal Sign in with the same account confirmed working; the earlier generic Sign in error did not reproduce. |
| 0.2 | GAP-039a — API readiness and safe diagnostics | **Complete** (`c8dd1e8`, `d7d0ee2`, `8b165b2`). Server-generated correlation IDs (`X-Correlation-Id` + log scope), minimal `/health/live` and `/health/ready` (no dependency/config detail in the public body; DB outage logged internally), fail-fast startup validation for required production config, and release identity (`RAILWAY_GIT_COMMIT_SHA`) in the log scope. Also fixed a live diagnosability gap: Resend delivery failures were silently discarded — now logged (status code in `ResendEmailSender`, auth-code ID in `StartAuthService`/`SignInAuthService`) without exposing PII. 896/896 integration tests pass. **Deployment note:** startup now fails fast if required config is missing — before the next deploy, confirm Railway sets `ConnectionStrings__DefaultConnection` (not only Railway's own `DATABASE_URL`, which this code does not read directly), `App__PublicBaseUrl`, `Resend__ApiKey`, and `Resend__FromAddress` (must be an address on the verified `mail.ophalo.com` domain, e.g. `OpHalo <no-reply@mail.ophalo.com>`). |
| 0.3 | Email trust template foundation | **Complete** (`027cfdf`). Shared `AccountEmailLayout` (table-based HTML, retina logo + text fallback, single CTA, locked ADR-431 motto, Privacy/Terms/Contact footer, no tracking pixel/click tracking) applied to account-start, sign-in, and invite emails, each with distinct truthful intro copy (ADR-446). `IEmailSender.SendAsync` gained a `textBody` parameter so every account email now ships a real plain-text alternative, threaded through `ResendEmailSender`, `ConsoleEmailSender`, and all callers. Logo asset hosted at `https://www.ophalo.com/brand/ophalo-lockup-color.png`. 898/898 integration tests pass. Future customer-facing messages remain business-primary with OpHalo only as a quiet footer; out of scope here. |
| 0.4 | GAP-039b — Error capture and safe customer references | Wire the selected free errors-only Sentry projects for browser/API unhandled errors, with release identity and PII/token scrubbing. Return/display a safe error reference where appropriate; do not add replay or broad telemetry. Requires the Sentry projects/DSNs before implementation. |
| 0.5 | GAP-039c — Deploy smoke checks and runbook | Add the repository-owned production smoke-test script and concise runbook. It must use the dedicated internal smoke account/inbox and verify health, sign-in, link exchange, authenticated session, and request-list load. |
| 0.6 | GAP-020 deployment verification | **Manual device task.** Complete the tracker’s real-device opaque call-handoff checks against the deployed, phone-reachable service; record evidence and any defects. |
| 0.7 | Authenticated Sign in redirect consistency | When a valid auth cookie exists, `/signin` must check `/auth/me` and redirect directly to `NEXT_PUBLIC_APP_BASE_URL` instead of showing the sign-in form. Extract the existing `/start` session-check logic into one shared client helper/hook used by both `/start` and `/signin`; preserve the current unauthenticated and transient-error behavior, and add focused regression coverage. |

### Phase 1 — Shared UI Safety Foundations

| Order | Session | Scope and completion gate |
|---|---|---|
| 1.1 | GAP-028 — CSS token validation | Identify undefined token usage, establish the narrow validation guard, repair affected states, and add focused proof. |
| 1.2 | GAP-029 — Status language and badges | Centralize the locked status labels/variants and update the Request List, Request Detail, and Quick Capture surfaces with regression tests. |
| 1.3 | GAP-030 / GAP-031 — Transient UI and error boundary | Make delayed copy/success feedback disposal-safe and add the root authenticated-workbench error boundary with recovery/reload coverage. |
| 1.4 | GAP-032 / GAP-024 — Modal and focus contract | Build/adopt the shared modal primitive, then apply it only to the scoped Quick Capture and desktop call-handoff modals; verify keyboard, focus, Escape/backdrop, and in-flight behavior. |

### Phase 2 — Quick Capture Reliability

| Order | Session | Scope and completion gate |
|---|---|---|
| 2.1 | GAP-016 / GAP-021 — Phone-entry contract | Finish ADR-444 phone validation, native parity, and country-code lookup compatibility across the PWA/native contract. |
| 2.2 | GAP-017 / GAP-022 / GAP-023 — Service location and draft safety | Complete address-at-creation behavior, prevent silent address loss, and make change-phone/customer-selection drafts safe. |
| 2.3 | GAP-018 / GAP-025 — Self-service handoff and customer recognition | Correct the entry/posture for public-intake handoff and make Quick Capture recognize the customer found through request-list phone search. |
| 2.4 | GAP-016 phone-input verification | **Manual device task.** Complete the tracker’s browser and real-device verification for the finished ADR-444 phone-input contract. |

### Phase 3 — Request List Operating Experience

| Order | Session | Scope and completion gate |
|---|---|---|
| 3.1 | GAP-041 / GAP-026 — First-load queue and search affordance | Remove the page-refresh-like first queue transition and make search discoverable without changing queue contracts. |
| 3.2 | GAP-043 / GAP-044 — Paging and history | Verify the existing cursor model with realistic data, make its controls accessible, and expose authorized closed/cancelled history through the existing protected contract. |
| 3.3 | GAP-045 / GAP-046 / GAP-027 — Queue orientation and filters | Replace opaque queue language, surface/recover applied filter state, and implement the separately locked row-hierarchy/lifecycle presentation decision. |

### Phase 4 — Request Detail Reliability And Continuity

| Order | Session | Scope and completion gate |
|---|---|---|
| 4.1 | GAP-019 — Request Detail decomposition | Establish the component/presentation seams required for later detail work without changing behavior beyond the documented refactor guard. |
| 4.2 | GAP-047 / GAP-048 / GAP-049 — Mutations, sharing, and follow-up bounds | Make priority failures visible, retain deliberate tracker-share intent for email, and bound follow-up prefill; cover optimistic/concurrency/error paths. |
| 4.3 | GAP-050 / GAP-051 — Continuity and phone presentation | Add compact same-customer related work and complete consistent North American phone display/entry without changing canonical storage. |
| 4.4 | GAP-042 — Authenticated workspace identity | Add restrained business-name context to Request List and Request Detail, with no public-route leakage or request-row repetition. |

### Phase 5 — Pilot Operations And Launch Evidence

| Order | Session | Scope and completion gate |
|---|---|---|
| 5.1 | GAP-033 — Public-trust deployment evidence | **Manual/review task.** Capture the required real-browser intake, expired-tracker, and OffSeason evidence; implement only defects or the explicit banner decision that the review uncovers. |
| 5.2 | GAP-037 — Weekly value report | Build the founder/internal account report endpoint/read path and manual-share output; do not build a business analytics dashboard or automated report delivery. |
| 5.3 | GAP-038 — Pilot feedback and help | Add authenticated Report Friction and Help & Updates, its private founder route, and the required native parity work; preserve PII boundaries. |
| 5.4 | GAP-040 — Marketing and launch accuracy | Bring public marketing copy/assets/legal/support links into alignment with the deployed product; verify deployment-facing claims and links. |
| 5.5 | Production-candidate release gate | **Manual/release task.** Run the full end-to-end checklist, validate alert routing/error capture/health/release identity, review known limitations, and decide whether pilot onboarding may begin. |

## Release Rules

- Finish or explicitly defer each selected P0/P1 tracker item before pilot invitation. A broken
  required-persona core flow, including authentication, is a pilot blocker.
- Before every production candidate, run the repository checks and the controlled smoke test;
  verify health/readiness, release identity, error capture, alert routing, and telemetry redaction.
- Do not onboard the excited pilot client until the production sign-in flow and the required
  end-to-end pilot checklist are verified.
