# Legacy Pilot Readiness Bug And Gap Tracker

**Archived:** 2026-09-06. This is a preserved reconciliation snapshot, not a live workboard. The canonical forward-looking board is [docs/workboard.md](workboard.md). ADRs own decisions and contracts; build logs retain delivery history.

**Former purpose:** The live, forward-looking backlog for unresolved pilot-readiness work.

**Last triaged:** 2026-09-06

This file preserves the prior tracker wording for traceability. Do not update it; use the workboard
for current state and the relevant build log for delivery history.

## Status Legend

- **Open:** Decision is sufficiently clear; implementation has not started.
- **In progress:** A bounded implementation is underway; the stated remainder is still required.
- **Reopened:** A prior remedy was incomplete or superseded.
- **Needs decision:** Product direction must be locked before implementation.
- **Deferred:** A real item deliberately not scheduled now; the deferral condition is stated.
- **Resolved:** Delivered and accepted; retained here only as a concise pointer to the evidence.

## Launch Gates

These are not an instruction to ship every remaining item before a supervised pilot. They define the conditions that must be satisfied before enabling the relevant workflow.

| Item | Gate condition |
| --- | --- |
| GAP-039 | Required before any customer-facing production pilot. Browser/API Sentry code is merged; founder-owned Sentry/Railway/Vercel console configuration, alert-delivery verification, and the Batch 4 production-candidate gate remain. |
| GAP-068 | Resolved (`755c7eaf`; manual verification `2cda916d` / BL143). |
| GAP-033 | Resolved (`11c19d3d`, `89a776d8`). |
| GAP-048 | Required before using email to share private customer request pages. |
| GAP-047 | Required if staff relies on Internal priority for operational triage. |
| GAP-016 / GAP-021 | Resolved. ADR-444 ten-digit path across backend + all web client paths; native deferred to Session 14 (ADR-236). |
| GAP-049 | Required before relying on follow-up creation from closed requests. |
| GAP-069 | Required before any customer-facing production pilot — the Railway API must persist Data Protection keys across container replacement and correctly recognize its TLS-terminating proxy. |
| GAP-040 | Required before the public intake link is shared or marketed — published copy, claims, and visuals must match shipped V1. |
| GAP-063 | Required while public intake is live during the pilot — an Owner/Admin must be able to classify a spam or test submission through the product (the server already supports it). |

## Implementation Order

Complete each numbered slice with focused automated coverage and a production-candidate/manual check where applicable. Do not start a dependent slice before its prerequisite is accepted.

1. **Release safety and production hardening (canonical order):** **GAP-039** (founder-owned operational configuration, alert-delivery verification, and the Batch 4 production-candidate gate — browser/API Sentry code is already merged: `d7d0ee22`, `fd34af34`, `baf07265`, `a69a8edf`, `70e75a3f`), then **GAP-069** (Data Protection key-ring persistence + trusted forwarded-headers hardening on the Railway API — this is the next coding session), then **GAP-040** (public request journey, copy, and published claims match shipped V1). (GAP-033 public-intake trust and tracker event-feed allowlist — resolved, commits `11c19d3d`, `89a776d8`. GAP-056 customer SMS/QR handoff sender/business context — resolved, commit `0fc7a2a`.)
2. **Field-work correctness:** No active item. (GAP-055 Actual Work recorder ownership — resolved across Batches A–D: migration/ownership `b3b3d41`, recorder authorization `d26b955` and `72ce6a5`, audited transfer `c7ce822`, and Owner/Admin recovery UI `de40491`.)
3. **Phone and capture integrity:** GAP-016 and GAP-021 resolved (ADR-444 ten-digit path consolidated across backend and all web client paths). GAP-051 public-web done (BL147); its remaining native-parity scope is deferred to Session 14. GAP-025 is deliberately deferred: ADR-492 is a narrow, explicit historical-phone continuity guardrail, not a solution for customer phone-number changes.
4. **Request Detail foundation and correctness:** GAP-019, GAP-058, GAP-059, then GAP-047, GAP-048, GAP-049, and GAP-063. First establish shared responsive seams without behavior change; then make Owner/Admin review, lifecycle, attention, and timing actions unmistakable. See [BL137](build-log/137-request-detail-and-queue-usability-handoff.md) for the bounded execution order.
5. **Request workspace next:** GAP-027 and GAP-045 are resolved. Implement GAP-067 first as the presentation-only Request List/Detail foundation, after a brief read-only GAP-042 business-identity placement preflight; then implement GAP-042, GAP-041, GAP-046, GAP-043, GAP-044, GAP-026, and GAP-053. The row grammar remains locked: one lifecycle cue, one server-ranked exception cue, and one next-action line. GAP-067 must preserve that grammar, server ranking, and existing behavior; do not merge a broad queue redesign into it. (GAP-057 empty-Attention fallback and truthful state; GAP-060 Views-menu off-screen clipping; GAP-061 queue/detail synchronization — resolved in `0cfb335`.)
6. **Pilot operating loop and final usability review:** GAP-064 (after GAP-039), GAP-037, GAP-038, and GAP-054. Establish a reliable new-customer-request alert path before relying on intake to create live work, then deliver the founder's evidence/reporting loop, a fail-soft feedback route, and a final role/device navigation review.

## Active Work

### GAP-068 — Multi-workspace sign-in is a dead end; invited users have no display name

**Status:** Resolved
**Severity:** P0
**Area:** Foundation auth (`/auth/signin`, `/auth/start`, `/auth/exchange`, invite acceptance)

An email with two or more active `AccountUser` memberships never received a magic link, and invite
acceptance created a global `User` with a blank name that surfaced as `userName: null`. Locked
decision [ADR-497](decisions/ADR-497-post-auth-continuation-multi-workspace-signin-and-display-name.md):
a single server-owned, single-use `PostAuthContinuation` row redeemed via `POST /auth/continue`,
with `/auth/start` / `/auth/signin` responses enumeration-safe throughout.

**Resolution:** Slices 1–4 merged to `main` — frontend completion `755c7eaf`; manual browser/network
verification recorded (`2cda916d`) and in [BL143](build-log/143-multi-workspace-signin-and-invited-name-handoff.md),
including a production-verified fix for an `AccountUsers.MembershipStatus` / `NormalizedEmail` data
issue that was blocking multi-workspace selection for one email. ADR-497 acceptance cases are
covered by automated tests.

### GAP-039 — Production failures and pilot health are not observable enough to earn trust

**Status:** In progress — browser/API Sentry implementation merged; founder-owned operational
configuration and verification remain
**Severity:** P0
**Area:** Production reliability and internal product operations

**Locked decision:** Use **Sentry** for redacted server and browser error capture, with release/environment identity and **founder email** as the initial alert destination. The telemetry boundary, production-configuration, source-map, and operational-gate contract is [ADR-495](decisions/ADR-495-gap-039-redacted-error-capture-and-release-safety.md). Do not enable session replay, broad behavioral analytics, a data warehouse, or an owner-facing analytics dashboard in this pilot slice.

**Implementation order:**

1. Define and test the telemetry boundary: permit only release/environment, server-generated correlation ID, safe route/status/error metadata, and narrowly justified account-level identifiers. Scrub or reject capability URLs/tokens, request text, service addresses, phones, emails, authorization headers, cookies, sessions, and broad request bodies.
2. Add the Sentry ASP.NET integration in production configuration. Attach the existing `ReleaseIdentity` and correlation ID; capture unhandled server failures without changing safe ProblemDetails responses. Retain `/health/live` and `/health/ready` as opaque availability/readiness signals.
3. Add the Sentry React integration to the authenticated PWA with release/environment identity. Validate `VITE_PUBLIC_BASE_URL` at startup/build time and make missing or malformed configuration fail safely rather than allowing request-detail `.replace()` calls to throw. Do not add session replay.
4. Configure Railway health checking against `/health/ready`, Sentry environment/DSN/release configuration, and the founder-email alert rule. Record a short runbook: inspect release/correlation ID, check health and Railway logs, decide mitigation versus rollback, and record the incident.
5. Verify a production candidate with controlled server and browser failures, normal/unhealthy health responses, an invalid public-base URL, alert delivery, release identity, and automated redaction checks for representative PII and public-token paths.

**Delivered (code, merged):**

- Steps 1–2 — the API telemetry boundary (`SentryTelemetryScrubber` allowlist rebuild with
  residual-token discard), the `Sentry.AspNetCore` integration, the authenticated-only `account_id`
  / correlation-id tags, and the `Sentry__Dsn` production startup requirement
  ([BL141](build-log/141-gap-039-batch-1-api-telemetry-boundary-and-error-capture.md), commit `baf07265`);
  API readiness / correlation IDs / safe diagnostics (`d7d0ee22`); production smoke-test script and
  runbook (`fd34af34`, `docs/runbook/production-smoke-test.md`).
- Step 3 — the `@sentry/react` errors-only integration with redacted browser capture and a
  fail-closed build gate (`70e75a3f`), and the single parsed `VITE_PUBLIC_BASE_URL` accessor with a
  fail-safe configuration-error UI replacing every direct `.replace()` use (`a69a8edf`).
- Step 4 — the operational runbook is in-repo (`docs/runbook/sentry-configuration.md`).

**Remaining — founder-owned, operational (no repository evidence that these are done):**

- Enter the API and Workbench-PWA Sentry DSNs, the `/health/ready` healthcheck path, and the
  release/environment settings in the Railway and Vercel consoles per the runbook.
- Create the founder-email alert rule in the Sentry console and verify alert delivery.
- Step 5 / Batch 4 production-candidate gate: run the controlled server and browser failure,
  health-response, invalid-public-URL, redaction, and alert-delivery checks against the production
  candidate and record the results.

**Done when:** Controlled server and browser failures arrive in Sentry with useful
release/correlation context and no protected data; founder email alerting is verified to deliver;
readiness/availability monitoring is verified; invalid required public configuration fails safely;
and the runbook is usable by the founder.

### GAP-069 — Production API container does not yet persist Data Protection keys or recognize its TLS-terminating proxy

**Status:** Open
**Severity:** P1
**Area:** Railway production API deployment hardening

The production API starts with two actionable ASP.NET Core warnings: its Data Protection key ring
is written to the container-local `/root/.aspnet/DataProtection-Keys` directory and has no
at-rest encryptor, while `UseHttpsRedirection()` cannot determine an HTTPS port after Railway
terminates TLS upstream. The explicit Railway `PORT`/`URLS` binding message is expected and is not
part of this gap.

The application currently uses opaque, database-validated session cookies rather than framework
cookie authentication, so a restarted container does not by itself invalidate an existing OpHalo
session. Even so, Data Protection is initialized by the host; future protected cookies,
antiforgery, or other framework consumers must not lose their ability to unprotect data after a
restart or across instances. Separately, the API must receive the original HTTPS scheme before
middleware makes redirect or link-generation decisions.

**Locked direction:** Treat both warnings as production hardening, in a bounded implementation
separate from GAP-068. Persist a dedicated Data Protection key ring outside the ephemeral container
and protect it at rest; use a Railway-compatible persistent volume or an external key store, with
the final storage/encryption choice recorded in a runbook. Configure narrowly trusted forwarded
headers (at least `X-Forwarded-Proto`, and only from known Railway ingress/proxy sources) before
`UseHttpsRedirection()`. Do not broadly trust client-supplied forwarded headers or merely suppress
either warning.

**Done when:** A redeploy/container replacement preserves the key ring; the stored key material has
documented at-rest protection and rotation/restore ownership; the API correctly recognizes a
trusted proxied HTTPS request before HTTPS-redirection middleware; an untrusted forwarded header
cannot spoof the scheme/client address; and the two warnings no longer appear in a production
startup log. Record the Railway mount/external-store configuration and proxy trust boundary in the
operational runbook.

### GAP-070 — Optional Price Book enrollment lacks a complete entitlement-aware shell

**Status:** Deferred — optional-module work; not blocking for the request-first controlled pilot.
The request-first pilot onboarding phase (BL142) is complete, but this remains deferred pending the
GAP-071 commercial/optional-module workflow decision.
**Severity:** P2
**Area:** Authenticated PWA navigation, routes, and capability-state refresh

Price Book is an optional account capability, not a Pilot-classification side effect. An unenrolled
account must have a complete Requests-first workspace: no Price Book primary-navigation item, no
reserved blank layout slot, and no module-specific affordances in settings or work toolbars. An
entitled Owner/Admin must discover the module without it competing with Requests. The existing
`GET /accounts/me/capability-packages` read model remains the client discovery authority; do not
move package flags into auth exchange/JWT claims. A later authorized entitlement change must
invalidate/refetch that client state promptly.

**Guardrails:** Server-side enrollment and role checks remain authoritative for every capability
action. This gap does not authorize self-service checkout, a customer entitlement-write endpoint,
or a guided onboarding wizard. Direct-route behavior must be deliberate and accessible; it may
explain unavailability or redirect, but must not expose package data or leave a broken page.

**Done when:** browser verification and focused frontend tests prove enrolled and unenrolled
Owner/Admin states reflow cleanly across desktop/narrow layouts; direct routes behave intentionally;
and a capability refresh changes the shell without relying on a new session/JWT contract.

### GAP-071 — Optional Modules discovery and commercial handoff are not yet a truthful product surface

**Status:** Deferred — needs a product/commercial-workflow decision before it can be scheduled; not
blocking for the request-first controlled pilot. During the pilot, entitlement stays on the
authorized internal path (`internal.entitlements.manage`, `InternalUser`-attributed).
**Severity:** P2
**Area:** Account/subscription experience and pilot operator workflow

Price Book configuration belongs in Business Settings only after entitlement. Package discovery and
commercial selection belong in the account/subscription area (for example, Subscription & Optional
Modules), without making a customer assemble Keep before using Requests. During the controlled
pilot, a real internal OpHalo operator assesses fit and uses the authorized internal entitlement
path; the resulting enrollment is `InternalUser`-attributed to that operator. The current founder
bootstrap/runbook constraint for the first internal operator remains an operational prerequisite.

**Guardrails:** Do not automatically enroll all Pilot accounts, blanket-backfill pilot accounts, or
attribute an operator grant to the customer Owner. Do not promise Trial evaluation, billing,
plan/add-on eligibility, expiry revocation, or self-service activation until a separate commercial
workflow decision is made. Disabling a package is non-destructive: retained history stays governed
by its existing read-only/audit rules.

**Done when:** the pilot operator workflow is usable and auditable; the account menu communicates
optional-module status truthfully without exposing a fake checkout; and the later commercial choice
has an explicitly approved server-authoritative contract.

### GAP-033 — Public intake does not establish sufficient customer trust or return continuity

**Status:** Resolved — commits `11c19d3d` (Slice A) and `89a776d8` (Slice B); earlier identity work R90b-1/2a/2b (`75f472f` and migration `20260718013055`)
**Severity:** P1
**Area:** `ophalo-web` public intake, Keep customer page mapper

Before asking for customer address/contact data, show business identity and configured public contact information when available; place factual privacy/use disclosures before relevant fields; keep email visible and optional; take a successful submission directly to its private tracker; and provide a real privacy-policy link. Public copy must not promise automatic tracker-link email, verification, or unsupported security properties.

Additionally, audit the private tracker's request-history event feed for public exposure: the public tracker endpoint must serialize an explicit allowlist of customer-relevant event types and message sources, not "all events minus a blocklist." Internal activity (financial review, tech assignment, internal notes, raw status thrash) must never reach `page.events`. Business-authored customer messages and customer-friendly lifecycle phrasing are in scope to show; anything else is excluded by default.

**Resolution:** Business-first identity/contact and configured-identity projection (never email) landed with R90b-1/2a/2b. Slice A replaced the post-submit auto-redirect with an ADR-446-compliant stable confirmation screen — "Request sent", reference code, explicit "Track this request" link to the private tracker, retained business identity and footer, welcome banner still shown on continue — and replaced the "private page"/"private link" copy that overstated the link-token model with neutral request-tracking-link language; a Terms link was added beside Privacy in the shared public footer. Slice B replaced the `Visibility == All` filter in `KeepCustomerPageMapper` with an explicit default-deny allowlist: an event reaches `page.events` only when it is stored customer-visible AND its type is `StatusChanged`, or `MessageAdded` from a `Customer`/`AccountUser` actor with a customer-safe `MessageIntent`; every other current type and any unknown/future enum value is excluded. Coverage: a mapper unit test enumerating every `KeepRequestEventType`, and an integration test proving internal notes and other internal-visibility events never reach `page.events`. Verification: full unit suite 1820/1820, architecture 14/14, `KeepCustomerPageTests` 18/18, event-adjacent integration 140/140; `ophalo-web` `tsc`/build clean plus desktop and business-identity browser review of both intake routes and the confirmation/tracker hand-off. Real-phone review folded into the BL089 launch pass.

### GAP-040 — Marketing site does not accurately represent the current product or launch posture

**Status:** Open
**Severity:** P1
**Area:** `ophalo-web` marketing and legal/support routes

Audit public routes, copy, images, links, metadata, and deployment behavior against shipped V1. Remove unsupported automatic-email, SMS, verification, response-time, revenue, or security claims; use representative non-private visuals; and verify desktop/mobile, keyboard, and production-host behavior.

### GAP-062 — Assembly editor drifts from the Price Book workspace and hides item identity

**Status:** Resolved — commit `07b7ea8` (clarity copy `f34292c`)
**Severity:** P1
**Area:** Price Book Offering/Assembly detail and edit form

The Offering/Assembly editor uses a narrow, one-off `max-w-2xl` layout while the wider Price Book workspace is available, making it feel unlike the rest of the product's operational forms. Its associated-item rows truncate catalog item names to protect quantity, optionality, and remove controls; an Owner/Admin cannot reliably see which item they are editing.

**Locked resolution:** Recompose Assembly Detail as a Price Book workspace form using the established page shell and an intentional wide content region. At desktop widths, associated-item rows must use a stable grid/column layout that gives the item identity the dominant flexible column and keeps quantity, optionality, and destructive action legible. At narrow widths, controls may stack below the identity, but the complete catalog item display name must wrap and remain visible. Remove name truncation from editable associated-item rows; do not replace it with hover-only title text or an overflow/ellipsis workaround.

**Acceptance criteria:**

- The Assembly Detail page aligns visually with the application's established workspace/form hierarchy rather than occupying an arbitrary narrow strip of canvas.
- Every associated catalog item name is fully readable at supported desktop, narrow PWA, and browser-zoom widths, including long names.
- Quantity, the Optional control, and Remove remain visibly associated with the correct item, keyboard reachable, and do not cause horizontal overflow.
- The optional-component explanatory copy and base-price note remain readable without becoming the dominant visual element.
- Focused frontend coverage includes a long item name and layout/accessible-name regressions; TypeScript, build, and CSS-token checks remain clean.

**Resolution:** `OfferingAssemblyDetail` now renders in the shared `mx-auto w-full max-w-[1440px] px-4 sm:px-6` workspace wrapper with a `max-w-4xl` content column. Associated-item rows use a `sm:grid` with a wrapping `minmax(0,1fr)` identity column and intrinsic-width Qty/Optional/Remove columns, stacking name-above-controls below `sm`; name truncation and title-tooltip fallback removed. Regression tests cover a long item name and the workspace width. TypeScript, build, and CSS-token checks clean.

### GAP-016 — New Request phone validation and correction path remains incomplete

**Status:** Resolved
**Severity:** P0
**Area:** Quick Capture, authenticated request API, and native parity

The ADR-444 normalized ten-digit North American policy is implemented across every client path
that exists in the repo: `PhoneNormalizer` (strip non-digits, drop a leading `1` on 11-digit
input, canonical = exactly 10) drives `KeepCustomer.Create` and `LookupKeepRequestByPhoneService`;
web Quick Capture gates lookup/submit on ten digits and provides the draft-preserving **Change**
path in `CaptureForm`; public intake slices a leading `1`. Stale "7–15 digit" / E.164 wording in
the `KeepCustomer` exception message, the `KeepCustomerConfiguration` column comment, and the
`KeepCustomerTests` comments/names was corrected to state the exact ten-digit bound. Native parity
is not actionable until the native project exists (ADR-236, Session 14).

### GAP-021 — Quick Capture rejects valid country-code input

**Status:** Resolved
**Severity:** P1
**Area:** `ophalo-app` Quick Capture lookup

`normalizeNaPhoneInput` (`quick-capture/utils.ts`) drops a leading `1` and caps at ten digits
before the UI gate, lookup, and return-to-draft path; `LookupGate`/`HandoffPanel` enforce the
ten-digit gate. Covered by `phoneFormat`, `LookupGate`, and `draft-preservation` tests.

### GAP-051 — Phone formatting remains incomplete outside the authenticated PWA

**Status:** In progress — public-web done; native parity deferred to Session 14 (ADR-236)
**Severity:** P1
**Area:** Native and public phone input/display

Authenticated PWA staff-facing formatting is complete. Public-web is done (BL147): the public
intake form already tolerates `1`/`+1` and formats as-you-type, and the configured business phone
is now rendered `(XXX) XXX-XXXX` on every public projection — the intake info endpoint
(token + slug) and the customer tracker (active + expired) — via the display-only
`PhoneDisplayFormatter`, with canonical storage, API round-trips, and `tel:` targets unchanged.
Native parity is the only remaining scope and cannot land until the native project exists
(Session 14, ADR-236).

### GAP-025 — Quick Capture hides request-phone-only customer continuity

**Status:** Needs decision — deliberately deferred from the next pilot coding session
**Severity:** P2
**Area:** Quick Capture identity lookup

ADR-492's explicit possible-customer flow is substantially implemented and remains the only
authorized response to a request-phone-only hit: no automatic attach, navigation, customer
creation, or canonical-phone backfill. It is **not** a customer phone-number change feature.

`KeepCustomer.CanonicalPhone` is currently immutable, so a customer whose permanently changed
number has never been deliberately attached to a prior request will still be created as a new
customer. A future, separately approved identity-lifecycle decision would need an editable current
unique phone plus audited verified historical aliases, with `KeepCustomerId` as the durable
identity. Do not introduce fuzzy matching on name, email, address, or request-phone history.

For the pilot, retain the narrow ADR-492 guardrail and investigate only a concrete regression in
that existing flow. Do not make GAP-025 the next coding session or broaden its scope without pilot
evidence and an approved identity-lifecycle decision. Minimize active-work disclosure on a
phone-only possible match because the number may be stale, shared, or recycled.

### GAP-019 — Request Detail needs durable shared responsive seams before further behavior changes

**Status:** Resolved — RD-019A (`ophalo-app`), behavior-preserving composition-seam extraction.
`RequestDetailContent` is now a coordinator delegating to `useRequestDetailLayout` (both width
rules + rail focus), `RequestDetailWorkCanvas` (layout-only canvas structure/order),
`RequestDetailActualWorkSection` (Actual Work region from injected state + callbacks), and
`RecordDetailsSection`. No API/DTO/authorization/mutation-policy/lifecycle/attention change.
**Severity:** P1
**Area:** `ophalo-app` Request Detail architecture

**Locked resolution:** Keep one **page-level coordinator** for authoritative request-detail state,
cache synchronization, navigation, overlays, and cross-feature policy. Shared feature controllers
may own bounded local form state, mutations, retry snapshots, and conflict handling, provided that
they consume the authoritative detail/version and return the authoritative replacement detail to the
page coordinator. Desktop and mobile composition must never implement business behavior separately.

Extract thin desktop and narrow/mobile composition wrappers plus coherent shared canvas regions.
Preserve the distinct viewport-width Actual Work workspace-route rule and container-width Request
Detail layout rule; they serve different purposes and must not be collapsed into one heuristic.
This slice is behavior-preserving: no visual redesign, API/DTO change, authorization change,
mutation-policy change, or changed lifecycle/attention semantics.

### GAP-058 — Actual Work review and request-completion actions compete on Request Detail

**Status:** Resolved — RD-058A (`c5796e0`), RD-058B-1 (`2ae07d5`), RD-058B-2 (`8e3127d`, confirm-dialog fix `85a1a57`)
**Severity:** P1
**Area:** Request Detail Actual Work review and lifecycle action hierarchy

**Progress:** The read-only Actual Work Review queue projection carries the factual request
lifecycle status; a row states both **Request: {lifecycle state}** and **Submitted visit awaiting
internal financial review** (RD-058A, commit `c5796e0`). RD-058B-1 reframed the review card as
**Internal financial review** with the persistent sub-line "Reviews the submitted visit's financial
details. Does not change the customer request.", renamed the action to **Complete internal financial
review**, relabelled per-visit state as **Financial review pending** / **Financial review
completed**, and made both success surfaces (Request Detail canvas banner and the wide-viewport
Actual Work workspace route) announce "Internal financial review completed. The customer request
status is unchanged." RD-058B-2 completed the action hierarchy: during active attention the
server-authored attention-resolution action is the only dominant action; the standalone Anchor
**Contact customer** action is removed unconditionally (contact stays in Customer Contact / a
server-routed contact-sheet primary); the non-primary alternate reads **Resolve another way…** and
opens the Why/Resolve-by guidance disclosure; **Mark work done** moved from the Anchor to a quiet
"Request lifecycle" block in the Work Canvas after Actual Work and before the composer (desktop and
mobile), still gated on the server-provided secondary authorization; both **Mark work done**
controls (and **Close request**) confirm through one focused `MutationConfirmDialog` — title
"Mark request as Work completed?", the full advisory in a constrained body ("Work completed · no
customer notification · no internal-review completion · attention/open draft unresolved"), Cancel
focused on open, Escape restores focus to the trigger, page not re-laid-out — replacing the inline
row that had expanded the Anchor and displaced the request identity; and the Anchor inner card is
bounded to `max-w-4xl mx-auto` to share the Work Canvas reading frame.

When a request is in **Actual Work Review**, the page simultaneously presents the request-level **Mark work done** action and the review-card **Mark visit reviewed** action. The request can still show an early lifecycle state such as **Received**, making it unclear whether the operator is reviewing recorded work, completing the customer request, or expected to do both. A mistaken completion can change the customer-facing lifecycle before the required financial review is complete.

**Locked resolution:** Make the two facts visually and semantically separate.

- Extend the read-only Actual Work Review queue projection with the factual request lifecycle status.
  A row states both **Request: {lifecycle state}** and **Submitted visit awaiting internal financial
  review**; a `Received` request must never imply that it has advanced simply because a visit awaits
  review.
- Rename the card action to **Complete internal financial review** and place persistent copy on the
  card: it reviews the submitted visit's financial details and **does not change the customer
  request**. On success, announce that internal financial review completed and request status is
  unchanged.
- Retain server-authored **Mark work done** for the request lifecycle. With active attention, it is
  a quiet, contextual lifecycle action below the attention and Actual Work/communication context,
  not a competing anchor action. Its confirmation must state that it marks the request as Work
  completed, does not notify the customer, does not complete internal financial review, and, where
  applicable, leaves attention or an open Actual Work draft unresolved.
- The attention-resolution action is the sole visually dominant action while attention is active.
  Channel-specific Call/Text/Email/Share actions remain in Customer Contact; do not duplicate a
  large `Contact customer` action in the anchor. A non-primary authorized alternate path is labelled
  **Resolve another way…**, not `Clear attention`, and must expose the server-authorized guidance.
- Align the Request Anchor and Work Canvas to one shared horizontal content boundary; keep the
  compact planning row in the anchor.

Do not hard-block request completion, couple completion to review, invent a client lifecycle policy,
or change server lifecycle authority as a presentation fix.

**Acceptance criteria:**

- An Owner/Admin can distinguish financial-review completion from customer-request completion before acting.
- A visit-review action cannot be mistaken for, or silently cause, a request status change; a request-completion action cannot be mistaken for review.
- Desktop/mobile, keyboard focus order, permission variants, and the `Received` plus actual-work-review state have focused regression coverage.
- The review queue, Request Detail, and confirmation copy distinguish request lifecycle, submitted
  visit, internal review, customer notification, active attention, and open-draft facts without
  implying that one action changes another.

### GAP-059 — Planned-work and internal-follow-up controls look disabled or unreadable

**Status:** Resolved — RD-059A (`cf9adaf`)
**Severity:** P1
**Area:** Request Detail schedule and follow-up controls

RD-059A applied the locked resolution to `TimingPanel` (Anchor `strip` row plus the
full-card and `bare` variants). Persistent labels are **Internal priority**, **Planned work date**,
and **Internal follow-up (optional)**. Enabled empty controls now read **Set planned date** and
**Set follow-up date** in normal-contrast ink with a leading calendar cue and no placeholder
ellipsis (previously low-contrast `Set planned work date…` / `Set internal follow-up…`). The
restrained configuration checkmark shows only for the current Internal priority selection
(including default Routine) and a persisted Planned work date; it never appears for an empty planned
date or the optional follow-up. Read-only values drop chevron/hover/button semantics and carry a
visible muted **Read only** caption. Keyboard: Enter/Space opens an editor and focus moves to its
first field; Escape (`preventDefault` + `stopPropagation`) and Cancel close it and restore focus to
the trigger; one-open-editor behavior is preserved; save and 409-conflict errors stay in the
relevant editor with `role="alert"` and the conflict path keeps the editor open with the field
disabled. Existing date/reason validation and mutation/version/conflict policy are unchanged; no
server or policy change. Coverage: new `TimingPanel.strip.test.tsx` (strip + full-card keyboard,
error, conflict, loading, empty-copy/contrast, checkmark, read-only), extended
`DetailPanels.priority.test.tsx` and `RequestDetailAnchor.test.tsx`. Full frontend suite 977
passed; tsc / `check:tokens` / `vite build` / `git diff --check` clean; desktop, narrow PWA,
keyboard, and browser-zoom evidence captured.

The custom disclosure buttons that open the **Planned work date** and **Internal follow-up** date
editors use placeholder-like low-contrast text and a weak affordance. In the observed Request
Detail state, they visually read as unavailable rather than actionable controls, making a core
scheduling/follow-up path easy to miss.

**Locked resolution:** Preserve the compact three-field planning row and existing mutation policy,
but distinguish an enabled disclosure button from a read-only value without relying on color.

- Persistent labels are **Internal priority**, **Planned work date**, and **Internal follow-up
  (optional)**. A checkmark is a restrained configuration cue, not a request-completion signal:
  show it for the current Internal priority selection (including the default Routine) and for a
  persisted Planned work date; do not show it for an empty planned date or optional follow-up.
- Enabled empty controls read **Set planned date** and **Set follow-up date** in normal-contrast text,
  with calendar/disclosure cues and no placeholder ellipses.
- Read-only values have no chevron/hover behavior and expose a visible **Read only** cue.
- Enter/Space opens an editor and focuses its first field; Escape closes it and restores focus to its
  trigger; normal Tab order reaches every form action. Errors remain associated with the relevant
  editor and are announced.

**Acceptance criteria:**

- At normal desktop and mobile widths, an Owner/Admin can identify both controls as available,
  distinguish configured priority/planned work from an empty value, and understand that follow-up
  is optional before opening an editor.
- Empty text, selected values, focus, hover, read-only, validation, loading, and mutation-error
  states meet the established contrast and accessibility treatment.
- Focused PWA coverage verifies keyboard open/focus/Escape/restore behavior and that enabled empty
  controls are not rendered with disabled semantics or appearance.

### GAP-047 — Internal-priority updates can fail silently on Request Detail

**Status:** Open
**Severity:** P1
**Area:** Request Detail triage mutation

Surface associated failure feedback for transport/API failures and stale-version conflicts. A failed or conflicted priority change must not appear saved; require refresh before further stale mutations.

### GAP-048 — Emailing a private request page bypasses deliberate share intent

**Status:** Open
**Severity:** P1
**Area:** Request Detail customer email/share path

Route email containing a private tracker through the explicit share workflow. Opening `mailto:` is not proof of delivery; only an informed owner confirmation records sharing. Preserve token secrecy, plain-email capability, and truthful `Needs Share` state.

### GAP-049 — Closed-request follow-up prefill can exceed the description limit

**Status:** Open
**Severity:** P1
**Area:** Request Detail follow-up creation

Reserve space for the provenance prefix and safely truncate copied source text so maximum-length closed requests can start a valid follow-up without changing the original record.

### GAP-063 — Owners and Admins cannot classify a request as Spam or Test in Request Detail

**Status:** Open
**Severity:** P1
**Area:** Request Detail Owner/Admin lifecycle controls

The server already supports an auditable, terminal Spam/Test classification through
`POST /keep/requests/{id}/classify` and returns the authoritative `availableActions.canClassify`
permission flag. Request Detail does not expose that authorized action, so staff cannot remove a
known spam submission or intentional test request through the product.

**Locked resolution:** For an Owner or Admin on an active request with `canClassify`, expose a
secondary lifecycle action offering **Mark as spam** and **Mark as test**. Require a clear,
accessible confirmation before submit because the classification is terminal; allow an optional
internal reason (maximum 500 characters). Replace authoritative detail with the response and show
the resulting terminal status and existing internal timeline event. Do not expose the action to
Operators/Viewers, send customer notification, alter the server authorization/state policy, or
provide unclassification/reopen from the client.

**Acceptance criteria:**

- Owner/Admin users can classify an eligible request as Spam or Test after confirmation; the UI
  refreshes to the server-returned terminal state.
- The action is absent for ineligible roles and terminal requests, including when a stale client
  view says it is available.
- The reason field, confirmation, API/transport failure, and stale-version conflict states are
  keyboard accessible and provide clear feedback without implying a successful mutation.

### GAP-027 — Request-list alerts compete and lifecycle state is hard to scan

**Status:** Resolved (Q-027A, `8ced025`) — the locked row grammar (one quiet lifecycle cue, one
server-ranked exception cue, one next-action line) was already in place; the remaining defect was
that non-overdue priority/urgent work rendered red. `RequestRow.severityToTone` now reserves the
red tone for server severity `"danger"` (genuine overdue/high-risk) and renders `"priority"` amber.
Server ranking/severity, the one-exception limit, terminal suppression, and quiet planned/future
timing are unchanged; selection stays visually distinct from severity; Office Review remains a
separate surface.
**Severity:** P1
**Area:** Request-list row hierarchy and lifecycle presentation

**Locked resolution:** Every Request row uses one compact grammar: one quiet lifecycle cue, at most
one server-ranked exception/attention cue, and one factual next-action line. Selection state is
independent of severity; do not make selected blue and alert red compete as equal row borders.
Reserve red for genuine overdue/high-risk work, keep planned/future timing quiet, and suppress
ordinary SLA/follow-up alarms for terminal work while retaining the approved unresolved-feedback
exception. The queue count and visible row urgency remain server-authoritative.

The Owner/Admin primary queue controls remain Attention, All work, and Mine; Office Review stays
separate from customer-promise risk. Implement this after GAP-019/058/059, with no client-side
re-ranking or broad queue redesign folded into the Request Detail slices.

### GAP-045 — Default Queue language does not explain work scope or prioritization

**Status:** Resolved (documentation-only) — the shipped UI already satisfies the substance. The
Owner/Admin primary tab is titled **All Work** with the supporting subtitle "Open requests and
feedback requiring review, ranked with customer promises needing attention first." Server
queue/ranking authority is unchanged. This landed with the attention-first landing work (GAP-057);
no further UI change is warranted.
**Severity:** P1
**Area:** Request-list orientation

Intent: replace implementation language with the locked Owner/Admin label **All Work** and clear
supporting copy explaining that open requests and review work are ranked with customer promises
needing attention first, with server queue/ranking authority unchanged.

The controlling decision is **UI-004 (production, 2026-08-21)**: title-case **All Work** plus that
exact subtitle. The earlier ADR-449 (2026-07-25) used lowercase "All work"; the tracker inherited
the stale casing — it is not a product gap. Do not change labels, copy placement, server ranking,
or navigation. UI-004's Office Review discoverability requirement is out of scope here and stays
with GAP-065.

### GAP-042 — Authenticated request work lacks visible business identity

**Status:** Open
**Severity:** P1
**Area:** Request List and Request Detail context

Add restrained, fresh business-name context to authenticated list/detail views without competing with the request/customer, duplicating stale labels, or exposing account identity publicly.

### GAP-041 — First queue selection blanks the work area

**Status:** Open
**Severity:** P1
**Area:** Request-list loading and queue tabs

Keep queue context and list geometry stable during first fetch, use an appropriate loading treatment, and complete tab keyboard behavior without showing prior-queue rows under a new label.

### GAP-046 — Request search and filters lack visible applied-state and recovery

**Status:** Open
**Severity:** P2
**Area:** Request-list search/filter accessibility

Show applied criteria, an accessible result/status announcement, and a clear/reset path. Preserve deliberate-submit search, cursor/query binding, and accurate cursor-page count language.

### GAP-043 — Request-list scale behavior is not a verified operating experience

**Status:** Open
**Severity:** P1
**Area:** Cursor pagination and scale UX

Make and document a V1 scale decision from representative pilot data. If retaining the cursor model, make page transitions, older/newer work, focus, end state, and result context clear without adding misleading offset/numbered pagination or infinite scroll.

### GAP-044 — Completed and cancelled work is not discoverable in the PWA

**Status:** Open
**Severity:** P1
**Area:** Request history access

Expose the existing authorized closed/cancelled/all-history API views through a clear PWA path. Keep active and terminal contexts distinct and preserve roles, protected cursors, filters, and detail-back navigation.

### GAP-026 — Request-list search has no clear affordance

**Status:** Open
**Severity:** P2
**Area:** Request-list search

Add an accessible clear control that restores the selected queue's unfiltered list by keyboard or pointer. Deliver with GAP-046 rather than as a separate interaction pattern.

### GAP-053 — Needs Attention reverses canonical row communication action order

**Status:** Open
**Severity:** P2
**Area:** Request-list row actions

Render **Update customer** before **Log contact** whenever both actions are allowed, including Needs Attention, Open Work, and narrow layouts. Share the ordering rule and cover visual plus focus order.

### GAP-065A — An active Actual Work Draft no longer hides prior submitted visits (UI slice)

**Status:** Resolved (fix `4fbda15`; recorded `767bee83`)
**Severity:** P1 (narrow UI slice of GAP-065)
**Area:** Request Detail — Actual Work section

`RequestDetailActualWorkSection` now renders the submitted `ActualWorkHistoryCard` whenever visit
history has content or errored, even while the current Actual Work capture state is an editable
Draft. The no-filler Draft behavior is preserved (empty history still renders nothing). Review
routing is unchanged: on a wide viewport each prior submitted visit still exposes **Open in
workspace** and routes with that exact visit ID; narrow screens keep the inline review card and add
no workspace route. Owner/Admin financial-review authorization is not broadened. Coverage:
`RequestDetailActualWorkSection.test.tsx` (6 focused tests); `src/pages/request-detail` suite 425
passed; tsc / `check:tokens` / `vite build` / `git diff --check` clean.

The broader GAP-065 queue cue, the persistent Office Review destination, and the server-authoritative
projection are now delivered — see the resolved GAP-065 entry.

### GAP-064 — A new customer request can arrive without reliably alerting accountable staff

**Status:** Needs decision
**Severity:** P1
**Area:** Public intake and staff notification reliability

An authenticated business-created request is already known to the staff member who entered it, but a
customer-originated public-intake request can be created without a reliable, timely alert to an
accountable Owner/Admin. The current desktop QR and mobile `sms:` patterns are **manual customer
contact handoffs**: they open the submitting operator's phone/Messages app and neither send nor
prove delivery to a staff recipient. They cannot be the primary safeguard against an unseen job.

**Decision required:** Define the smallest reliable staff-alert policy before implementation:

- Which customer-originated events require an immediate alert (at minimum, a newly created public
  request), which role or responsible person is the accountable recipient, and how Owner/Admin
  fallback works when that person is unavailable.
- Whether the pilot's primary channel is real device push, provider-delivered internal SMS, or a
  deliberately configured combination; define delivery failure, retry, and escalation rather than
  treating a launched native app as delivery.
- If automated internal SMS is selected, establish verified staff phone enrollment, explicit opt-in
  and opt-out handling, recipient de-duplication, after-hours/quiet-hours policy, message content
  minimization, provider cost/credentials, durable delivery attempts, and a safe fallback channel.
- Whether a desktop QR/mobile SMS-compose action is retained only as an optional **manual
  escalation** after the request is saved. It must identify the actual sender and recipients, require
  a deliberate send, and never claim that all Owners/Admins were notified.

**Done when:** A public-intake submission has a durable, privacy-safe routed-alert record and a
verified pilot path that reaches its accountable staff recipient or produces an actionable failure/
escalation state. The request list/badge remains the authoritative backlog; a manual QR or native
SMS launch is supplementary only. Coverage proves recipient selection, actor exclusion,
mute/eligibility/off-season behavior where applicable, duplicate suppression, failure handling,
and that no customer data beyond the minimum notification payload is exposed.

### GAP-065 — Owner/Admin internal financial-review work is hard to discover from requests

**Status:** Resolved
**Severity:** P1
**Area:** Request List, Office Review navigation, and Actual Work review context

An Owner/Admin can now open Request Detail once, enter any outstanding submitted visit directly from
a **Pending financial reviews (N)** card, review one visit, and deliberately continue to the next
via the wide-workspace pending-visit switcher; a quiet, server-authoritative request-row cue and the
persistent Office Review destination surface the work in the queue without touching request ranking,
attention, or the server review gate. Locked boundaries and the full delivery record are in
[BL138](build-log/138-gap-065-owner-admin-financial-review-discovery-and-delivery-plan.md); the
cross-module signal contract is [ADR-463](decisions/ADR-463-keeprequest-work-signal.md).

**Resolution:** all delivery slices committed — Slice 1B-server `faf7b64`, Slice 1B-client
`e27c48c`, Slice 2 `6ab880b`, Slice 3a `baaeff1`, Slice 3b `f231126` (+ `606203d` compact pane-row
amendment). Slice 3c closed documentation-only ([BL138](build-log/138-gap-065-owner-admin-financial-review-discovery-and-delivery-plan.md)
handoff). The cross-request one-row-per-visit review queue is explicitly out of scope and
unscheduled — it needs its own query, authorization, ranking, and empty-state decision.

### GAP-066 — Catalog Item detail is not yet a usable financial and operational-impact workspace

**Status:** Open
**Severity:** P1
**Area:** Price Book Catalog Item detail

The existing Catalog Item page has the correct item, price/cost, profitability, alias, edit, and
inactivation behavior, but presents them as a narrow sequence of raw fields on the canvas. It does
not match the financial-review workspace's clear hierarchy, and it gives an Owner/Admin no
at-a-glance answer to the operational question: where will changing or inactivating this item have
an effect?

**Locked direction:** Treat Catalog Item detail as a financial workspace. The visual order is
**item identity → economics → discoverability → operational impact**. Price Book uses the same
cool financial-workspace canvas as Actual Work financial review; request communication/data keeps
its distinct request canvas. Preserve existing mutation, price-version, alias, inactivation,
conflict, entitlement, and authorization behavior.

**Phased resolution:**

1. **Existing-data presentation slice.** Recompose the Owner/Admin page into a wide responsive
   workspace: item identity/status and action hierarchy in the header; an **Economics &
   profitability** card with Sell price, Direct cost, Gross profit, Margin %, and secondary Markup
   %; and a dedicated **Search aliases** card. `Update pricing & cost` is the primary financial
   action; Edit is secondary; Inactivate remains quiet/destructive. Use semantic margin tone:
   healthy positive margin green, thin/non-negative margin amber, negative margin red, and missing
   data neutral/unavailable. Do not create a fake “standard catalog rate” concept.
2. **Operational-impact slice (new read-model preflight required).** Add Owner/Admin-only,
   server-authoritative reverse relationships for active assemblies/offers and relevant nudge
   rules. Each section must provide truthful counts, useful empty states, and deep links to the
   affected records. Do not infer relationships in the client, expose unavailable relationships,
   or show decorative empty cards. The existing inactivation dependency check is not by itself a
   general impact projection.

**Guardrails:** Do not alter price/cost snapshots, reuse mutable current catalog values as history,
broaden Price Book entitlement/role access, or imply invoices, billing, inventory, or accounting
behavior. Association/nudge display must not delay, replace, or weaken the existing safe
inactivation dependency check.

**Done when:** An Owner/Admin can scan the economic decision, update pricing/cost safely, manage
field-search aliases, and—once the second slice is delivered—understand every live operational
relationship before changing an item. Desktop, narrow layout, missing-price/cost, zero-cost,
thin/negative-margin, conflict, and alias-management coverage remain correct.

### GAP-067 — Request workspace presentation lacks a coherent operational visual system

**Status:** Open
**Severity:** P2
**Area:** Request List and Request Detail presentation

The Request workspace carries inconsistent canvas warmth, card edges and spacing, metadata density,
queue alert treatment, and button hierarchy. In particular, repeated red row accents and badges
make routine work look urgent, while the selected row and actual SLA breach do not have sufficiently
distinct meanings. The detached Customer Need module also makes it possible to encounter an
attention action before the user has scanned what the customer needs.

**Locked direction:** Treat this as a presentation-only Request workspace coherence pass, using the
revised Request reference page retained by Christian for the implementation session. It is the
desktop visual source of truth for this gap; narrow behavior still requires responsive verification.
The exact implementation values are locked in the [Request Workspace Visual Token
Specification](ux-design/v2/request-workspace-visual-spec.md); do not substitute discretionary
palette, spacing, or hierarchy choices during implementation.
Use a clean, operational Slate-50 canvas (`#f8fafc` or the equivalent established token), distinct
from but not semantically dependent on the cool financial Price Book canvas. The app shell and
cards remain white with Slate-200 borders, rounded-xl geometry, and only a restrained shadow where
elevation is meaningful. Do not reintroduce the prior cream/amber page canvas.

**Current implementation state:** Slices 1–4 landed the aliases and partial component treatment,
but GAP-067 is not complete. The workbench still needs reference-verified composition: a work
canvas anchored 24 px from the queue divider at `min(100%, 1000px)`, Customer Need inside the
Request Anchor beneath planning, and the complete module/card spacing pass. Do not close this gap
or begin GAP-042 implementation until wide and narrow screenshot verification proves the retained
reference and every acceptance criterion below.

- **Queue state grammar:** preserve GAP-027's one lifecycle cue, one server-ranked exception cue,
  and one next-action line. Teal identifies selection; red is reserved for genuine active,
  unacknowledged overdue/high-risk work; amber covers customer replies and routine follow-ups;
  completed work stays quiet. Do not add client-side ranking, duplicate alert badges, or colored
  rails to every row. The one sanctioned exception to the amber reservation is the BL138 Slice 3b
  Owner/Admin financial-review metadata dot — a tiny, non-alert amber dot preceding muted
  `text-slate-600` "{N} visit(s) need financial review" text, rendered in both the default row and
  the compact pane row (beneath the `Next:` / action-signal line). It is server-authoritative and
  must remain metadata: never a badge, rail, ranked exception cue, attention treatment, or
  interactive control.
- **Request anchor:** use compact micro-labels and a responsive identity/contact/location/owner
  grid, followed by a distinct planning row for Internal priority, Planned work date, and Internal
  follow-up. Customer Need follows inside the anchor as a clearly bounded *neutral* Slate-50/
  Slate-200 summary—not an attention banner. Keep all existing detail fields, controls,
  permissions, and semantics available.
- **Action hierarchy:** each context has one primary operational action (for example, respond to a
  customer message) in teal-600/700 with white text. A consequential internal action, such as
  Review Visit Financials, may use restrained dark-slate emphasis in its own card; continuation,
  edit, and navigation actions are white outlined controls. Destructive actions remain visually
  distinct and require their existing protections. Do not let a financial-review action visually
  outrank the active customer-promise response merely through styling.
- **Attention and spacing:** the single active customer-message attention card uses a warm amber
  surface/border; Customer Need and ordinary content stay neutral. Keep 20–24 px separation
  between major cards, 16–20 px card padding, 12 px internal control gaps, and one consistent
  divider/border tone. Avoid a stack of heavy shadows or multiple competing colored panels.

**Guardrails:** This gap does not authorize changed request lifecycle, attention/ranking logic,
financial-review behavior, API contracts, permissions, data model, or a generic queue redesign.
Preserve responsive and keyboard behavior. Do not flatten the queue so far that a dispatcher loses
the factual next-action context required for fast triage.

**Done when:** Request List and Request Detail use a calm, consistent operational visual system;
selection, customer attention, genuine breach, completion, and primary action are distinguishable
at a glance; Customer Need is present in the request anchor; and browser verification confirms the
desktop and narrow layouts retain their locked behavior.

### GAP-037 — Pilot has no weekly, evidence-based value report

**Status:** Open
**Severity:** P1
**Area:** Founder/pilot operations

Provide a founder-only, account-timezone, copy-pasteable weekly summary of safe request-level signals. Exclude Spam/Test and demo/internal accounts; do not turn it into owner analytics, automated email, staff scoring, or unsupported business-outcome claims.

### GAP-038 — Pilot businesses lack an in-product feedback and help loop

**Status:** Open
**Severity:** P1
**Area:** Authenticated PWA pilot support

Add an authenticated, rate-limited, fail-soft feedback route to a private founder channel and a maintained Help & Updates page. Do not automatically attach customer PII, broad logs, or create a ticketing/CMS system.

### GAP-054 — Authenticated app-shell navigation and action hierarchy needs review

**Status:** Open
**Severity:** P2
**Area:** Authenticated desktop/mobile shell

Perform a role- and entitlement-aware desktop/mobile review of global versus page-local actions, profile grouping, active-route treatment, discoverability, keyboard behavior, and narrow layouts. Make only evidence-backed shared-shell changes and record browser verification.
