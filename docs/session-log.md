# Session Log — OpHalo Foundation

**Last updated:** 2026-07-27
**Deployment posture:** Not pilot-ready.
**Source of truth for acceptance criteria:** `docs/pilot-readiness-bug-tracker.md`.

This log records current operational blockers and the active work queue. Historical implementation
evidence belongs in `docs/build-log/`; locked decisions belong in
`docs/pilot-readiness-decision-questions.md` and the decision index.

## Product-Direction Context — Read, Do Not Expand Current Scope

The first HVAC-contractor pilot discussion identified an emerging **asset-aware continuity**
direction for Keep: future contractor workflows may link a known equipment asset, QR-based service
intake, quote/approval, and retained work history. The complete discovery record is
`docs/build-log/091-pilot-discussion-contractor-asset-workflow.md`.

This is a staged product-direction decision, not authorization to expand the current implementation
queue. Keep's active request-list recovery and pilot-readiness work remains the priority. Do not
implement equipment assets, QR tagging, quotes, accounting/fleet replacement, property-manager
subscriptions, or sensor telemetry unless a separately scoped session explicitly promotes it.

## Immediate Production Access And Reliability Blockers

- **GAP-039b (P0): error capture and safe customer references.** Use Sentry's free errors-only
  offering for the browser and API. It is the selected pilot diagnostic tool because it provides
  grouped, release-aware browser/API crash capture and founder email alerts without a recurring
  vendor cost. Do **not** build a generic application `Errors`/exception database table: it would
  duplicate monitoring work, risk retaining PII/capability tokens, and is not a reliable record
  during a database outage. Health/configuration checks and the smoke-test tool are complete; no
  paid observability, replay, performance tracing, broad telemetry, or persistent staging
  environment before revenue.
- **Verify deployed routing and release configuration.** The QR-handoff host correction (OPS-009,
  commit `ce1ec40`) is deployed and Scan to call has been confirmed on a real device. Continue to
  validate the intended public/deep-link contract, Vercel environment variables, DNS/domain/cookie
  topology, and API deployment configuration before pilot access. **Confirmed domain contract:**
  `app.ophalo.com` = authenticated `ophalo-app` staff application; `www.ophalo.com` = public
  `ophalo-web` tracker and QR-resolver pages (`/keep/share-call`, `/keep/share-sms`, `/keep/r`);
  `api.ophalo.com` = API. See OPS-009 below — the API's `App:PublicBaseUrl` binding (not
  `AppBaseUrl`) is now the single source for these public-resolver URLs.
- **GAP-020 (P0): opaque desktop call-handoff.** The production QR now resolves on a real device.
  Remaining release evidence is cross-device verification: dialer/fallback, expiry/invalid-token
  behavior, cache headers, iOS Safari, Android Chrome, and a non-`localhost` phone-reachable
  environment.
- **GAP-016 (P0): complete phone-validation parity.** Native parity and the remaining manual
  browser/device verification are still required for the ADR-444 phone-input contract.
- **Requests onboarding-banner progression.** The Owner/Admin banner now identifies setup work,
  but its primary CTA remains “Set up request page” after that step is complete. Advance the CTA to
  the next incomplete core action (Quick Capture for the first customer request), then hide the
  banner after the public request page and first request are complete. Team remains optional.
- **GAP-052 / customer-update notification integrity (P0).** The safety gate, durable
  prepare/confirm contract, responsive PWA flow, and production migration are implemented and
  committed (`94f61af`, `7c2f9e5`, `a1d9221`). Page-only updates no longer clear first response or
  customer-waiting attention; remaining work is deployed workflow verification.
- **Request List operating-contract recovery (P1).** ADR-449 and ADR-450 lock truthful Owner/Admin
  queue language/sections and row context. The existing safe activity-preview retrieval remains
  useful, but activity must supplement—not replace—the original request summary; internal-note
  presence requires a server-authorized cue without exposing note text.

## Open Product And Pilot-Readiness Work

### Quick Capture, Input, And Modal Safety

- GAP-017 through GAP-019: complete service-location creation/disclosure behavior, the intended
  customer-self-service handoff posture, and Request Detail layout decomposition.
- GAP-021 through GAP-023 and GAP-025 through GAP-027: finish international/country-code lookup
  compatibility, address-draft preservation, customer-selection draft safety, customer recognition
  from request-list phone search, clear search affordance, and the remaining lifecycle-row decision.
- GAP-028 through GAP-031 and the scoped GAP-024 modal-accessibility work are complete. GAP-032's
  shared modal/focus foundation is complete for Quick Capture and desktop call handoff; dirty-close
  policy and Request Detail modal extraction remain deliberately deferred.

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
| 0.4 | GAP-039b — Error capture and safe customer references | **Claude implementation handoff:** first provision two free Sentry projects (browser/PWA and API) and place only their DSNs in the respective deployment environment variables; do not put DSNs in source control. Then wire only unhandled errors: browser render/async failures and API unhandled exceptions. Attach the existing release identity and server correlation ID when available. Before send, remove authorization headers, cookies, magic-link codes, public-intake/page tokens and capability URLs, customer request text, names, phone numbers, emails, and free-text form/request data. Disable session replay, tracing/performance, profiling, logs/telemetry, and user-identifying context. Configure actionable new-issue/regression email to the founder, with a conservative free-tier quota/spend alert. Keep Railway/Vercel logs as the correlated investigation source. Return a safe opaque error reference (never exception text) only on unexpected user-visible failures where support needs one; preserve existing expected `ProblemDetails` contracts. Add focused tests proving scrubbers remove sentinel secrets/tokens/PII and that release/correlation metadata is attached. Do not create an application error/exception table, a dashboard, a paid Sentry plan, or a persistent staging environment. **External prerequisite:** the project DSNs and the founder alert destination must be supplied before wiring/deploy verification. |
| 0.5 | GAP-039c — Deploy smoke checks and runbook | **Complete** (`fd34af3`, corrected by `8b6f392`). Added the dependency-free Node smoke script, regression tests, and runbook. Routine mode checks health, sign-in trigger, `/auth/me`, and request-list load with a local-only smoke-session cookie; full mode uses a separately obtained email code and deliberately skips a new sign-in trigger so it cannot invalidate that code. Local mock coverage passes. First live script execution remains a non-blocking operational check; manual deployed-app smoke testing is complete. |
| 0.6 | GAP-020 deployment verification | **Complete.** Manual device verification found scanning the desktop call QR 404'd. **Root cause (OPS-009):** `KeepEndpoints.cs` built SMS/call handoff URLs from `App:AppBaseUrl` (resolving to `app.ophalo.com`, the authenticated `ophalo-app` staff host), but the public resolver pages (`/keep/share-call`, `/keep/share-sms`) are served by the separate `ophalo-web` deployment at `www.ophalo.com`. **Fixed** (`ce1ec40`): both handoff URL builders now use the existing `IOptions<MagicLinkSettings>.PublicBaseUrl` binding (`App:PublicBaseUrl`/`App__PublicBaseUrl`), matching the pattern already used for intake-sms handoffs and magic links; removed the dead `App:AppBaseUrl` config key and its `app.ophalo.com` fallback. New assertions in `KeepCallHandoffApiTests.cs` prove both handoff URLs use the configured public base URL and never the app host. 8/8 focused, 626/626 Keep-scoped integration tests pass; `git diff --check` clean. Christian confirmed the desktop QR now scans and resolves correctly in production, 2026-07-27. |
| 0.6b | Scan to text contact handoff | **Next scoped enhancement.** Add `Scan to text` beside `Scan to call` in the Request Detail contact strip. Reuse the existing opaque SMS-handoff token/resolver and the established QR modal pattern; desktop displays an editable SMS draft and QR handoff, while mobile launches direct `sms:`. Scanning/launching must never record contact, clear attention, or claim delivery; retain the separate explicit external-contact logging/confirmation posture. Do not alter the QR token security model or fold this work into OPS-009. |
| 0.7 | Authenticated Sign in redirect consistency | **Complete** (`b81cbb6`). `/start` and `/signin` now share the `/auth/me` redirect logic; an authenticated visitor to `/signin` goes to the app, while an unauthenticated visitor sees the sign-in form. Production browser verification on 2026-07-24 confirmed sign-in email delivery, authenticated redirect to Requests, and unauthenticated redirect from the app to Sign in. `ophalo-web` still has no test runner, so this retains manual regression coverage. |
| 0.8 | Requests onboarding-banner next action | **Complete.** `RequestsOnboardingBanner`'s primary CTA now reflects the next incomplete core step: "Set up request page" → Settings `public-profile` while the request page isn't ready, then "Add your first request" → Quick Capture once it is. Banner-hide gating in `Requests.tsx` (both core steps complete, team optional) was already correct and untouched. `reviewCustomerPageComplete`/`shareIntakePageComplete` remain unused as completion signals. 6/6 focused tests pass (`Requests.onboarding.test.tsx`, updated fixtures + new CTA-progression test). |
| 0.9 | Customer-page intent and hierarchy | **Narrowed after verification, complete.** Preflight found the core start-new-request vs. existing-request-tracking hierarchy (explicit headlines, copy, layout, primary action) was already implemented on both `IntakeForm.tsx` and the tracker (`TrackerStatusCard.tsx`/`TrackerActionCard.tsx`): "Send update or question" primary, share demoted, cancellation visually separated. Only gap: added intake-page reassurance copy — "Already have a request? Check the private link {Business} sent you." — placed low, below the submit CTA; does not imply a public recovery mechanism. **Deferred:** the reciprocal "start another request" link on the tracker page requires exposing a business intake slug/URL in the `/keep/r/{pageToken}` API response, which `CustomerPageData` does not currently carry; not added this session (no backend contract change without an explicit decision) and `websiteUrl` was not substituted. Revisit only after a deliberate API/privacy decision on exposing that field. |
| 0.10 | Post-go-live workbench navigation UX | **Deferred as DEF-084.** Top-level app navigation currently remounts Requests, Getting Started, and Settings; this can look like a refresh and resets local page state. Requests queries have their own first-visit/loading and cached-return behavior. Do not change this during go-live stabilization. Revisit only with pilot evidence, preserving authentication/failure visibility and making an explicit decision before retaining unsaved Settings drafts. |
| 0.11 | GAP-052a — Customer-update notification integrity: safety gate | **Safety gate complete** (`94f61af`). A page-only update (`AddBusinessUpdate`/`AddBusinessUpdateWithStatus`) no longer sets first response or clears business-waiting attention. Detailed voicemail (`LogOutboundExternalContact`) no longer counts first response or clears attention; it now advances `NextAttentionAtUtc` to the next business day in the account's timezone, never pulling an already-later commitment earlier. Supersedes the voicemail effects in ADR-169/198/213/214 globally. The durable obligation/attestation continuation is complete in 0.11b. |
| 0.11b | GAP-052a (cont.) — Notification obligation/attestation domain/API | **Complete** (`7c2f9e5`). First pass shipped only an unlinked `ConfirmUpdateNotification` attestation and was rejected in review: it never validated the client-supplied `relatedUpdateEventId`, and had no durable prepared-by-actor record to enforce ADR-451's same-actor rule. Corrected to a real two-phase obligation model: `KeepRequest.PrepareUpdateNotification` (Sms/Email only) records a durable pending obligation — `PendingNotificationRelatedEventId`/`Channel`/`PreparedByAccountUserId`/`PreparedAtUtc` (new columns on `keep_requests`, mirroring the `NeedsShare` flat-field pattern) — plus a `NotificationPrepared` audit event; `ConfirmUpdateNotification` now requires an exact-matching unconfirmed pending obligation (same related event, same channel, same preparing actor) or fails closed (`NotificationNotPrepared` / `NotificationConfirmerMismatch`), and clears the pending pointer on success (blocking replay). New `IKeepRequestOperatePersistence.IsCustomerVisibleBusinessUpdateEventAsync` enforces server-side, at prepare time, that the related event is a same-request/same-account/customer-visible (`Visibility=All`, `MessageIntent=BusinessUpdate`) event — confirm trusts the already-validated pending pointer rather than re-querying. Also fixed a real pre-existing gap in `LogOutboundExternalContact` where `CallRequested` attention was clearable by text/email (now only a completed live call satisfies it). New `PrepareUpdateNotificationService` + `ConfirmUpdateNotificationService`, `POST /keep/requests/{requestId}/notification-preparation` + `.../notification-confirmation`. **Batch-size exception (Christian-approved):** 3 mutation-handler families (CallRequested fix, Prepare, Confirm — at the hard cap) across 15 hand-written production files (was 7 quoted initially) + migration artifacts + 5 test-fake interface-stub updates, forced by (a) the exhaustive `KeepRequestEventType` switch invariant in `KeepRequestDetailMapper`/`KeepCustomerPageMapper`, and (b) the corrected durable-obligation design Christian required after reviewing the first pass. 22 new domain unit tests (Prepare guards/overwrite, Confirm no-prep/wrong-event/wrong-channel/wrong-actor/replay/happy-path) + 14 new integration tests (random/wrong-request/wrong-event-type related-event rejection, cross-actor confirmer-mismatch, replay, full prepare→confirm happy paths) added to the existing `KeepRequestExternalContactTests.cs`/`KeepRequestExternalContactApiTests.cs` (no new test files). Keep-scoped suites: 962/962 unit, 626/626 integration passing; `git diff --check` clean. **OPS-008:** the `20260726213949_AddNotificationPrepareConfirmObligation` migration (adds `pending_notification_*` columns) shipped in code but was not applied to the production database before deploy, causing every request-list query to fail (`42703: column k.pending_notification_channel does not exist`) starting 2026-07-27. The migration has since been applied to production and the outage is resolved. |
| 0.12 | GAP-052b — Customer-update notification integrity: responsive PWA | **Complete** (`a1d9221`). Built the Owner/Admin post → prepare → confirm flow on `NotifyCustomerPanel.tsx`: desktop opaque SMS QR (reuses the existing `createSmsHandoff` primitive, not the ShareLinkModal ceremony), direct mobile `sms:`, `mailto:` for email, preference-guided but not hard-blocked channel choice, and an explicit "I sent it — Confirm" step — opening the draft never itself confirms. The panel only appears when a submission actually created a customer-visible event (`messageIntent: business_update`, `visibility: all`), wired from `BusinessSection.tsx`. Added a small, Christian-approved read-shape addition on top of the 0.11b contract: `KeepRequestDetailResult.PendingNotification` (`relatedUpdateEventId`, `channel`, `preparedAtUtc`, `canConfirmAsCurrentUser`, no raw preparer ID) so the panel resumes truthfully after reload/navigation and shows a correct non-confirmable state when another teammate holds the pending obligation — same-actor confirmation was already enforced server-side in 0.11b. Queue refresh is inherited for free from existing per-visit refetch (no separate cache-invalidation work needed). Out of scope, deferred: the historical "Notified via SMS/email" timeline badge (the underlying `relatedEventId` is already exposed on events for that future follow-up), templates, and multi-pending-notification design. 12 new focused frontend tests (`NotifyCustomerPanel.test.tsx`, `BusinessSection.notify.test.tsx`) plus 3 new assertions in the existing `KeepRequestExternalContactApiTests.cs` for the new field; full suites 962/962 unit, 626/626 integration, 134/134 frontend passing; `tsc --noEmit` and `git diff --check` clean. |

### Phase 1 — Shared UI Safety Foundations

| Order | Session | Scope and completion gate |
|---|---|---|
| 1.1 | GAP-028 — CSS token validation | **Complete** (`5dd45c7`). `BusinessSection.tsx` (`--ophalo-teal`) and `ShareLinkModal.tsx` (`--muted`) referenced undefined tokens; replaced with the approved `--keep-accent`/`--ophalo-canvas`. Added `web/ophalo-app/scripts/check-css-tokens.mjs`, wired into `build`, which fails on any undefined `var(--...)` reference in `ophalo-app/src` and on drift between `app.css`'s inlined `:root` block and `web/shared/styles/ophalo-tokens.css`. 6/6 new focused tests pass (`check-css-tokens.test.mjs`); confirmed the guard catches a reintroduced undefined-token regression. |
| 1.2 | GAP-029 — Status language and badges | **Complete** (`b1e67a4`). Added `web/ophalo-app/src/lib/requestStatus.ts` as the single status label/badge-variant source, imported by `RequestRow.tsx`, `request-detail/helpers.ts` (re-exported for `DetailHero.tsx`/`TimelineEvent.tsx`/`BusinessSection.tsx`), and `quick-capture/LookupResultView.tsx` (now uses `KeepBadge` instead of a fixed slate span). Retired all three per-surface duplicates, including detail's broken substring-based badge-variant heuristic. Locked labels preserved (`in_progress`→Active, `resolved`→Work completed, ADR-425/434); `pending_customer`→"Pending Customer" and `closed`→success variant (matches `resolved`, per ADR-050) were this session's terminology decisions, confirmed with Christian. 20 new focused tests (`requestStatus.test.ts`) lock all 9 statuses' label/variant plus fallback behavior; full suite 97/97 passing, `tsc --noEmit` clean. |
| 1.3 | GAP-030 / GAP-031 — Transient UI and error boundary | **Complete** (`101e9e9`). Added `web/ophalo-app/src/hooks/useCopyFeedback.ts` — catches clipboard rejection, tracks copied/failed id, replaces its timer on reuse, clears on unmount, and guards against a clipboard promise settling after unmount via `isMountedRef` (no state/timer work once gone). Refactored all six copy-timeout call sites onto it (`ShareLinkModal.tsx`, `RequestDetail.tsx` phone copy, `PublicLinkSection.tsx` raw/slug URL, `DetailPanels.tsx` phone/email), fixing two prior unhandled-rejection risks; added unmount cleanup for `RequestDetail.tsx`'s separate review-success timer; `CustomerPanel`'s icon-only copy buttons gained a dynamic `aria-label` plus an `aria-live="polite"` failure status line. Added root `ErrorBoundary.tsx` wrapping `<App />` in `main.tsx` — plain recovery card, Reload-only action, no exception message/data/stack trace ever rendered. 9 new focused tests (hook timer-reuse/unmount/post-unmount-settle cases; boundary render-throw/no-leaked-text/single-Reload-action cases); full suite 105/105 passing, `tsc --noEmit` clean. |
| 1.4 | GAP-032 / GAP-024 — Modal and focus contract | **Complete** (`d8574f2`). Added `web/ophalo-app/src/components/keep/KeepModal.tsx` — dialog semantics, initial focus (falls back to the panel itself, `tabIndex={-1}`, when there's no focusable child), a Tab/Shift+Tab focus trap, Escape-to-close, an explicit `backdropClosable` policy, and focus restoration to the pre-open trigger only when it's still connected and focusable. Applied only to `QuickCapture.tsx` and `CustomerContactStrip.tsx`'s `CallQrModal`, per this session's narrowed scope — GAP-032's dirty-close confirmation and the `RequestDetail.tsx` modal extraction remain deferred. Caught and fixed a real a11y bug along the way: an early draft nested the dialog panel inside the `aria-hidden` backdrop, hiding the whole dialog from the accessibility tree (surfaced by the existing Quick Capture regression suite); the decorative dim layer is now a sibling of the panel, not an ancestor. 11 new focused tests (`KeepModal.test.tsx`) cover initial focus, no-focusable-child fallback, Tab-trap wrap both directions, Escape, backdrop-click policy, and safe no-op focus-restoration when the prior trigger was removed or disabled; full suite 116/116 passing, `tsc --noEmit` clean. |

### Phase 2 — Quick Capture Reliability

| Order | Session | Scope and completion gate |
|---|---|---|
| 2.1 | GAP-016 / GAP-021 — Phone-entry contract | **Partially advanced (GAP-051 slice only).** Authenticated `ophalo-app` staff-facing phone entry/display now formats as-you-type and on read-only summaries as `(555) 555-5555`, matching the public intake form's readability, via shared `normalizeNaPhoneInput`/`formatNaPhone` utilities (`web/ophalo-app/src/components/quick-capture/utils.ts`) applied across `HandoffPanel`, `LookupGate`, `CaptureForm`, `LookupResultView`, `ShareLinkModal`, `RequestDetail`, `DetailPanels`, and (added in a follow-up fix) the business's own Customer-facing phone field in `CompanySection.tsx`. Canonical 10-digit values, API payloads, lookup, `tel:`/`sms:` targets, and copy actions are unchanged. See `docs/pilot-readiness-bug-tracker.md` GAP-051 for full detail. **Not done:** native parity and full country-code lookup compatibility remain open — this session does not close GAP-016, GAP-021, or Session 2.1. |
| 2.1b | GAP-016 — `ophalo-web` public-form leading-`1`/`+1` normalization parity | **Complete.** `ophalo-web`'s intake `formatPhoneAsYouType` (`IntakeForm.tsx`) silently truncated the real last digit of an 11-digit `+1`-prefixed typed/pasted number instead of dropping the country code, because it sliced to 10 digits before stripping a leading `1`. Fixed to strip a leading `1` first, mirroring the rule already locked and shipped in `ophalo-app`'s `normalizeNaPhoneInput`. Backend `PhoneNormalizer`/`KeepRequestInputValidator` was already correct and unaffected — this was a client-side data-integrity bug, not a validation gap. `ophalo-web` has no test runner; verified manually by Christian. Remaining GAP-016 work: native parity (blocked on the native app not yet existing) and the manual device-verification pass (2.4). |
| 2.2 | GAP-017 / GAP-022 / GAP-023 — Service location and draft safety | Complete address-at-creation behavior, prevent silent address loss, and make change-phone/customer-selection drafts safe. |
| 2.3 | GAP-018 / GAP-025 — Self-service handoff and customer recognition | Correct the entry/posture for public-intake handoff and make Quick Capture recognize the customer found through request-list phone search. |
| 2.4 | GAP-016 phone-input verification | **Manual device task.** Complete the tracker’s browser and real-device verification for the finished ADR-444 phone-input contract. |

### Phase 3 — Request List Operating Experience

| Order | Session | Scope and completion gate |
|---|---|---|
| 3.0 | GAP-007a — Request List routine action recovery | **Complete** (`de9a0c2`). Routine non-terminal rows with no attention-driven promotion now retain no fabricated `Next:` cue but render up to two server-authoritative modal actions: **Update customer**, then **Log contact**. Existing attention/closeout/feedback promotion, server permission/concurrency authority, and the two-button cap are unchanged. Focused row tests cover both-action ordering and one-action/no-invention behavior; `tsc --noEmit`, full PWA tests, token check, and `git diff --check` pass. `add_internal_note` remains detail-accessible until a separately designed compact-menu pass. |
| 3.0b | GAP-007b — Request List safe latest-activity retrieval | **Complete** (`9c35dec`). The bounded, server-selected activity-preview retrieval is available for each list page. ADR-450 supersedes only its prior display rule: original request context must remain stable; safe latest activity is secondary; note presence is a separate server-authorized cue. |
| 3.0c | ADR-450 — Request List row-context contract | Add the bounded row read-model/DTO support for original-summary expansion, separate safe latest-activity context, and server-authorized internal-note presence. Preserve list authorization and exclude internal-note/feedback content. Owner/Admin completion test: the owner can understand the original need and see that team context exists without opening every request. |
| 3.0d | ADR-449 — Owner/Admin work-queue hierarchy | Implement `Requests for {Business name}` / `All work` and server-authoritative Needs attention then Open work sections with zero-state disappearance, quiet section labels, and truthful page-scoped counts. Owner/Admin completion test: urgent customer promises are visible first without tab hopping, while calm work remains available and visually quieter. |
| 3.0e | Customer-update template strategy | **Deferred decision.** Do not build starter, custom, or business-managed message templates while Request List recovery and GAP-052 notification integrity remain open. Revisit only after the Request List is working as locked and pilot evidence shows repeated owner-authored update language. |
| 3.1 | GAP-041 / GAP-026 — First-load queue and search affordance | Remove the page-refresh-like first queue transition and make search discoverable without changing queue contracts. |
| 3.2 | GAP-043 / GAP-044 — Paging and history | Verify the existing cursor model with realistic data, make its controls accessible, and expose authorized closed/cancelled history through the existing protected contract. |
| 3.3 | GAP-045 / GAP-046 / GAP-027 — Queue orientation and filters | Surface and recover applied filter state, complete the remaining queue-orientation/filter behavior, and retain the ADR-449 hierarchy already delivered in 3.0d. |

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
- Before every production candidate, run the repository checks and the controlled smoke test
  (`scripts/production-smoke-test.mjs`, see `docs/runbook/production-smoke-test.md`); verify
  health/readiness, release identity, error capture, alert routing, and telemetry redaction.
- Do not onboard the excited pilot client until the production sign-in flow and the required
  end-to-end pilot checklist are verified.
