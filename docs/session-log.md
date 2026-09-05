# Session Log — OpHalo Foundation

**Last updated:** 2026-09-05 — **GAP-039 Batches 1, 2a, 2b, and 2c are implemented and accepted.
All GAP-039 code slices and the BL140 Batch 3 production console configuration are complete;
Batch 4 production-candidate verification is founder-owned and currently paused before controlled
error generation.** Pilot Onboarding Upgrade Session 0 (release-gate/surface audit) and Session 1
(server-owned Proposed Work release gate) are **complete** — implemented, tested, and merged to
`main` (`eb33f6a5`, plus follow-up `226778af`) — **not yet deployed.** See the Deferred next work
bullet below for the deploy step still required.
Next Claude implementation batch after that merge is deployed is **Session 2 —
automatic Pilot package provisioning** (ADR-496); GAP-033 is not next unless Christian explicitly
reprioritizes it. **GAP-068 (multi-workspace sign-in dead end + invited-user display name, P0) has
completed Session 0 discovery — [ADR-497](decisions/ADR-497-post-auth-continuation-multi-workspace-signin-and-display-name.md)
and [BL143](build-log/143-multi-workspace-signin-and-invited-name-handoff.md) are written. Slice 1
(`PostAuthContinuation` foundation, additive-only) is complete and accepted; Slice 2a
(MultipleMembers code issuance at `/auth/signin`/`/auth/start`, enumeration-safe) is complete and
accepted (`5403f445`); Slice 2b (`/auth/exchange` name-blank/MultipleMembers branching,
`POST /auth/continue`, `CompleteAuthContinuationService`, shared `AuthSessionIssuer`) is complete
and accepted — full end-to-end continuation redemption now works; see BL143 for delivery evidence.
Slice 3 (invite acceptance name gate) is next.** Request
UI Upgrade 1.1 implementation is complete (locked contract
[Request UI Upgrade 1.1](ux-design/v2/request-ui-upgrade-1.1.md), delivery evidence
[BL139](build-log/139-request-ui-upgrade-1.1-implementation.md)); its product-owner visual
acceptance pass is still outstanding.

**Purpose:** active handoff only. Completed implementation detail belongs in Git history and the
relevant build log.

## Authoritative sources

- Release priority and acceptance status: [pilot-readiness-bug-tracker.md](pilot-readiness-bug-tracker.md)
- Product decisions: [decision index](decisions/README.md) and individual ADRs
- Request Detail / queue execution sequence: [BL137](build-log/137-request-detail-and-queue-usability-handoff.md)
- Current Request Detail interaction contract: [Request UI Upgrade 1.1](ux-design/v2/request-ui-upgrade-1.1.md)
- Actual Work closeout/replacement contract: [ADR-494](decisions/ADR-494-actual-work-paper-compatible-pilot-upgrade.md), [ADR-493](decisions/ADR-493-actual-work-office-financial-resolution-and-billing-revisions.md), and [BL136 P](build-log/136-P-preflight.md)

## Current repository state

- GAP-065 is complete: Request Detail pending-review discovery, the wide financial-review
  continuation flow, server-authoritative request-row counts, the Owner/Admin row cue, and the
  persistent Actual Work Review destination. Detailed implementation and commit history are in
  [BL138](build-log/138-gap-065-owner-admin-financial-review-discovery-and-delivery-plan.md).
- Request UI Upgrade 1.1 now supplies the three-column desktop composition, compact sticky Request
  strip, frequent communication/share/work actions, and persistent Request Memory rail. Full
  frontend tests and the production build pass. The first visual-review refinement grouped the
  toolbar, demoted lifecycle completion while operational work remains, added authoritative
  financial-blocker CTAs, and moved communication actions above the right-rail timeline.
- The controlled pilot keeps the contractor's existing system authoritative for estimates,
  invoices, payments, and accounting. Keep is the factual field record; the existing-ticket
  workflow remains the outage fallback.
- GAP-039 Batch 1 is complete ([BL141](build-log/141-gap-039-batch-1-api-telemetry-boundary-and-error-capture.md)):
  the API host has an errors-only redacted Sentry boundary (`Sentry.AspNetCore` 6.10.0,
  `SentryTelemetryScrubber` allowlist rebuild + residual-token discard,
  `RequestContextSentryEventProcessor` for the correlation-id / authenticated-only account-id
  tags), `Sentry__Dsn` is a production startup requirement, and the SDK is a full no-op without a
  DSN. No response, health, or logging behavior changed. Full unit + integration + architecture
  suites pass.
- GAP-039 Batch 2c is complete: `@sentry/react` 10.73.0 + `@sentry/vite-plugin` 5.4.0 (exact
  pins). `src/lib/sentry.ts` `initSentry()` runs before render in `main.tsx` — errors-only, no
  tracing/replay, `maxBreadcrumbs: 0`; a no-op without `VITE_SENTRY_DSN`. `src/lib/sentryScrub.ts`
  is the browser `beforeSend`: a fresh allowlisted event (release, environment, safe pathname,
  exception type + sanitized frame metadata) with opaque-token/query/fragment detection scoped to
  the retained pathname and frame filename/function only (never the Sentry event id or release
  SHA), discarding the whole event if the invariant fails. `ErrorBoundary` forwards React-caught
  render errors through that path; the user-facing fallback is unchanged.
  `scripts/resolveDeployment.ts` is the fail-closed build gate: `OPHALO_DEPLOY_ENV`
  (`production`/`preview` only, else the build throws) is authoritative and independent of Vercel
  System Environment Variables; a classified build requires `VERCEL_ENV` to match and a non-local
  `VERCEL_GIT_COMMIT_SHA`, production additionally requires DSN + `SENTRY_AUTH_TOKEN`/`ORG`/
  `PROJECT`. Source-map upload runs only for a classified build with complete upload config
  (`build.sourcemap: "hidden"` + `filesToDeleteAfterUpload: ["./dist/**/*.map"]`, no
  `errorHandler`); a local build generates no maps. Local build verified: `dist` has zero `.map`
  files and no `sourceMappingURL` comments. Full app suite 1064 passed.
- GAP-039 BL140 Batch 3 founder console configuration is complete (credentials are recorded only
  in provider consoles): separate `ophalo-api` and `workbench-pwa` Sentry projects exist; Railway
  Production has `Sentry__Dsn` and healthcheck path `/health/ready`; Vercel Production has the
  Workbench DSN, organization CI token, organization/project identifiers, explicit
  `OPHALO_DEPLOY_ENV=production`, and System Environment Variables enabled. The first classified
  Workbench deployment succeeded and its Vercel log confirmed upload of two source-map artifacts
  to Sentry release `c37542adb4a8875fc209edd17ce2896757dfb73b`; Sentry shows that release.
  New-issue and resolved-issue-regression founder-email alert rules are live for both projects, and
  each alert's test notification reached the founder inbox. The non-secret configuration/rotation
  record is [Sentry Configuration Runbook](runbook/sentry-configuration.md).

## Next implementation sequence

BL140 "Batch 2" exceeded the CLAUDE.md batch-size gate (~15 production files) and was split into
three independently-compiling slices, all now delivered against
[ADR-495](decisions/ADR-495-gap-039-redacted-error-capture-and-release-safety.md) and the
[BL140](build-log/140-gap-039-sentry-implementation-handoff.md) handoff:

- **Batch 2a — `VITE_PUBLIC_BASE_URL` shared accessor + fail-safe UI + non-request-detail consumers.**
  DONE, accepted. New `src/lib/publicBaseUrl.ts` (throw-free; `publicBaseUrlResult` typed
  valid/invalid + `getPublicBaseUrl()`) and `src/components/ConfigurationError.tsx` (static safe
  screen). `main.tsx` renders it before mocks or `<App>` load when config is invalid. Converted
  `lib/redirectToSignIn.ts`, `pages/settings/PublicLinkSection.tsx`, `components/ShareLinkModal.tsx`,
  `components/QuickCapture.tsx`. `env.d.ts` marks `VITE_PUBLIC_BASE_URL` optional. Tests: accessor
  (valid/trailing-slash/base-path/missing/malformed/bad-scheme), `main.tsx` gate. Full app suite
  1040 passed; production build passes.
- **Batch 2b — request-detail consumer conversions. DONE, accepted.** Converted
  `pages/RequestDetail.tsx` (2 uses), `pages/request-detail/DetailPanels.tsx`, `DetailHero.tsx`,
  `NotifyCustomerPanel.tsx` to `getPublicBaseUrl()`. The request-detail test env stubs
  (`NotifyCustomerPanel.test.tsx`, `CallHandoffQr.test.tsx`) now `vi.mock` the accessor rather than
  `vi.stubEnv` (the accessor parses at module load). No raw `import.meta.env.VITE_PUBLIC_BASE_URL`
  read remains outside `publicBaseUrl.ts`. Full app suite 1040 passed; production build passes.
- **Batch 2c — `@sentry/react` init + private source-map upload. DONE, accepted.** See the
  Batch 2c bullet under "Current repository state" above for the delivered shape. The two
  preflight questions are resolved: (1) environment/release come from `OPHALO_DEPLOY_ENV` (explicit,
  system-var-independent) corroborated by `VERCEL_ENV` + `VERCEL_GIT_COMMIT_SHA`, never
  `import.meta.env.PROD`; (2) `filesToDeleteAfterUpload` physically removes every `.map` from the
  build output after upload — proven locally by `dist` containing zero `.map` files.

Remaining in GAP-039 — **founder-owned, release-safety gate, required before any customer-facing
production pilot:** BL140 Batch 4 production-candidate verification. It is paused at the safe
smoke-login provision step: `support@ophalo.com` is available as the dedicated alias, but must be
explicitly invited as a lowest-permission member of the founder's internal-only account before use.
After that, verify a controlled browser error and a deliberately safe authenticated API error;
inspect release/environment/correlation/redaction and founder-email delivery; prove deployed
source maps are absent; exercise the invalid-public-base-URL fail-safe; test preview separation if
preview capture is enabled; and record the evidence plus named incident roles in the runbook. The
API controlled-error route remains an explicit implementation/operational decision: there is no
permanent production failure endpoint. This gate precedes GAP-033.

**GAP-033 — public-intake trust and tracker access truthfulness — follows GAP-039.**
Full scope is [GAP-033](pilot-readiness-bug-tracker.md#gap-033--public-intake-does-not-establish-sufficient-customer-trust-or-return-continuity)
(P1, `ophalo-web`). It corrects the public request journey so it does not overstate what the
link-token model provides and does not collect personal data before establishing trust:

- Show business identity and configured public contact before asking for customer address/contact
  data; place factual privacy/use disclosures before the relevant fields.
- Keep email visible and optional; land a successful submission directly on its tracker; ship a
  real privacy-policy link.
- Remove public copy that promises automatic tracker-link email, verification, or unsupported
  security properties. (Tracker-page access copy was corrected ahead of this batch in commit
  `75f472f`; the intake form still uses "private page"/"private link" wording.)
- Audit the public tracker event-feed for exposure: serialize an explicit allowlist of
  customer-relevant event types and message sources; internal activity must never reach
  `page.events`.

Tracker Implementation Order for this pair is GAP-039 → GAP-033 → GAP-040 (`pilot-readiness-bug-tracker.md`).

Request UI Upgrade 1.1 still needs the product-owner visual acceptance pass (dense Requests at
1366×768, 1440×900, 1920×1080; 100/125/150% zoom; Queue operational, frequent actions reachable,
center work column dominant, Request Memory readable without horizontal page scroll). That is a
product-owner review task, not a coding batch, and is independent of GAP-033.

Do not begin GAP-042 implementation until GAP-067 passes that screenshot/acceptance review. Its
read-only placement preflight remains valid: business name is `meQuery.data?.businessName` from the
authenticated `/me` endpoint and belongs in shell chrome, outside Request Anchor identity.

## Deferred next work

- **Pilot onboarding upgrade:** [ADR-496](decisions/ADR-496-pilot-package-provisioning-and-release-visibility.md)
  locks automatic Price Book package enrollment for newly provisioned Pilot accounts, separates
  package entitlement from unreleased Proposed Work/Quote visibility, and replaces checklist-led
  first-run guidance with a request-first path. Price Book is visible immediately; Proposed Work
  and Quotes are unavailable until onboarding is complete and signed off. Automated enrollment uses
  a migration-backed `SystemProvisioning` audit provenance, not a bootstrap pseudo-user or the new
  customer owner. [BL142](build-log/142-pilot-onboarding-upgrade-handoff.md) is the session list —
  now locked to release-gate-first sequencing (renumbered Session 1 = server-owned release gate,
  Session 2 = automatic Pilot provisioning; deploying provisioning before the gate is complete and
  deployed would breach ADR-496).
  **Session 0 (read-only audit): done. Session 1: complete, merged to `main`, not yet deployed.**
  `IReleaseGatePolicy`/`ConfigurationReleaseGatePolicy` (global config gate, fail-closed via
  `bool.TryParse`, no checked-in override) gates `ProposedScopeApiService`,
  `ProposedScopeReadApiService`, `FieldProposedScopeSelectionApiService` (field-select),
  `FieldExpandAssemblyApiService` (expand-assembly), `ScopeNudgeFieldReadApiService` (Paired
  Nudges field read), and `QuickScopeActionFieldReadApiService` (field quick-scope-action read) —
  every state-changing/scope-exposing Proposed Work HTTP route. Locked classification (2026-09-04):
  `QuickScopeActionConfigApiService`/`ScopeNudgeRuleConfigApiService` stay catalog-only (Owner/Admin
  config, `PriceBookCatalogManage`-gated, same pre-release-visible posture as Price Book catalog
  itself); `FieldScopeSearchApiService` stays ungated — its gate 3 is `ScopeCapture OR
  ActualWorkCapture`, so it is the shared, price-free catalog/assembly search behind both the
  Proposed Work composer and the already-released Actual Work capture flow, and creates no
  Proposed Work state itself (the state-changing endpoints it feeds are the now-gated field-select/
  expand-assembly). Merged to `main` (`eb33f6a5`); not yet deployed. 6/6 focused release-gate
  integration tests pass; 90/90 pre-existing ProposedScope/ScopeNudge/QuickScopeAction/
  FieldScopeSearch integration tests unaffected; 14/14 architecture tests pass. Do not start
  Session 2 (provisioning) mutation work until this merge is deployed.
- **4g pilot request-close advisory:** preflight after the above safety/usability sequence. It is an
  advisory on outstanding Actual Work with a structured `Close anyway` pilot exception; it is not a
  hard Resolved→Closed gate. See BL136.
- **Pilot/release gates:** production observability (GAP-039, complete), pilot onboarding upgrade
  Session 1 deploy then Session 2 automatic Pilot provisioning (ADR-496, next), public-intake
  trust (GAP-033), phone integrity (GAP-016/021/051), then the remaining tracker order.
- **Minimum Office Closeout:** Billing Revision, handoff, and correction/adjustment design resume
  only after the controlled-pilot and rehearsal gates; see [BL135](build-log/135-minimum-office-closeout-mechanical-preflight.md).
- **Settings & Getting Started V2 UI upgrade:** pure visual restyle of the Owner/Admin Getting
  Started + Settings surfaces to V2 doctrine, plus a lightweight (non-checklist) first-run readiness
  moment on Getting Started. Preserves ADR-428 IA, defaults, and day-zero model. Locked contract:
  [settings-and-getting-started-ui-upgrade.md](ux-design/v2/settings-and-getting-started-ui-upgrade.md).
  Three frontend-only slices (A: Getting Started + Settings shell +
  Response Policy; B: Public Link & Profile; C: Team). Sequenced **after** GAP-039 → GAP-033.
  **Slice A delivered** (not yet product-owner visual-accepted): `keep-settings-frame` +
  `keep-field` shared recipes in `app.css`; `Home.tsx` `OwnerHome` rebuilt as the readiness panel
  + subordinate optional rows (reads the shared `["intake"]` query, no new fetch/mutation);
  `Settings.tsx` shell on the 880px frame with section-shaped loading/error placeholders;
  `PolicySection.tsx` inputs on `keep-field`. New `Home.readiness.test.tsx` (7 cases, pins the
  no-checklist/no-meter contract); `Settings.v2Shell.test.tsx` updated. Full app suite 1071
  passed; production build passes.
  **Slice B delivered** (not yet product-owner visual-accepted): `CompanySection.tsx` inputs/select
  on `keep-field`; `PublicLinkSection.tsx` — customer preview reframed as the Keep-teal moment and
  all `slate-*` drift converted to tokens, replace-link warning moved into an attention callout,
  confirmation + edit-name inputs on `keep-field`. Field sets, "Branding & trust anchors" grouping,
  Save company, Edit link name, and the Replace-link destructive flow (stale-link warning, one-time
  raw successor URL) are all logic-unchanged. `PublicLinkSection.logo.test.tsx` gains a preview
  V2-treatment / no-`slate` assertion. Full app suite 1072 passed; production build passes.
  **Slice C delivered** (not yet product-owner visual-accepted): `TeamSection.tsx` invite row
  (email input + role select + button) moved to the shared `keep-field` recipe with 44px targets
  and clean narrow stacking (`flex-col sm:flex-row`, full-width controls below `sm`). Member rows,
  the serif `keep-row-title` `<h2>`, the solo-owner reassurance copy, `seatUsage` server display,
  and every roster/invite/role/resend/suspend/remove flow are logic-unchanged. New
  `TeamSection.recipe.test.tsx` (4 cases: invite-row `keep-field`, tokenized list-row container /
  no `slate`-`emerald`, solo-owner copy, server seat-usage value). Focused settings + Home suites
  38 passed; production build passes.
  The §5 screenshot-acceptance pass across all three slices is still pending.
- **Price Book direct-cost visibility:** next after the Settings & Getting Started V2 UI upgrade.
  The Catalog Items workspace currently exposes current sell price but not current direct cost, so
  an Owner/Admin must open an item to determine whether the price book has a cost. Extend the
  authorized Catalog Items **list read contract** with the current published price-line direct
  cost (the same internal standard/direct cost already shown on Catalog Item Detail); update the
  frontend list type and render a **Direct cost** column adjacent to Sell price on desktop plus a
  clearly labeled secondary value in the narrow card layout. Show `—` when no current cost exists.
  Preserve existing Owner/Admin Price Book authorization and entitlement checks; do not expose
  cost in field workflows, change mutation/version/snapshot semantics, imply supplier "last paid"
  cost, or introduce inventory/accounting behavior. Add focused API/persistence serialization and
  frontend table/card coverage, including missing-cost behavior.
- **Workbench background brand alignment:** `ophalo-app` uses an off-spec cool gray page
  background. Align it to Canvas `#F8F6F1` (`--ophalo-canvas`) per
  [BRAND.md](brand-kit/BRAND.md), with a token audit for other hardcoded grays. Internal staff
  surface only — not a pilot blocker; run as its own small pass after the customer request page
  ships.

## Guardrails

- The responsive staff PWA is the active field surface; native parity is not implied.
- Do not infer authority for pricing, invoicing, payments, QuickBooks, inventory, or fleet from
  Request Detail work.
- Use disposable local data for mutable acceptance; never seed founder production data.
- Preflight current code and the controlling tracker/ADR/build log. Stop for product direction when
  server data or authorization cannot truthfully support the requested UI.
