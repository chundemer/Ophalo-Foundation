# Session Log — OpHalo Foundation

**Next-session pointer only.** Scope, sequencing, gates, deferrals → [workboard](workboard.md).
Locked decisions → [decision-index](decisions/decision-index.md). Working guardrails → CLAUDE.md.
If a line here would need editing when the workboard changes, it belongs in the workboard, not here.

**Updated 2026-09-11.** GAP-093 is code-complete, awaiting Christian's diff review
([BL153](build-log/153-gap-093-session-account-lifecycle-gate.md)). Next session: after that review
lands, implement GAP-094, then GAP-095. Alternatively, start unstarted audit Vector 6, 7, 9, 10, or 11.

## Baseline

Controlled parallel field pilot ([BL131](build-log/131-next-week-parallel-field-pilot-plan.md)): the
existing system stays authoritative for estimates/invoices/payments/accounting; Keep is the factual
field record.

## Active

**GAP-038 — in-product feedback + Help & Updates loop.** Slices 038-1a (content backend), 038-1b-i
(Help content surface), 038-1b-ii (unread count + menu rows + trigger dots), and **038-1b-iii
(Requests-list highlight banner) all landed 2026-09-08** — see the BL149 038-1a / 038-1b-i /
038-1b-ii / 038-1b-iii completion records. All of 038-1b is done. **038-2a-i (feedback domain
model — `FeedbackSubmission` entity + enums) landed 2026-09-09** (BL149 038-2a-i completion record).
**038-2a-ii (feedback persistence + migration) landed 2026-09-09** (BL149 038-2a-ii completion
record). **038-2b (gated `POST /feedback` + `FeedbackSubmissionService` + `IFounderNotifier` /
`FounderNotifier` + per-`account_user` 10/hour rate limit + `FounderChannel` config +
`Feedback:Enabled` default-false route gate) landed 2026-09-09** (BL149 038-2b completion record).
038-2c was split at preflight into **038-2c-i** (feedback delivery worker) + **038-2c-ii** (D4
content-source failure alert retrofit). **038-2c-i landed 2026-09-10** (BL149 038-2c-i completion
record): `FeedbackDeliveryWorker` + `FeedbackMaintenanceBackgroundService` — 5/15/60/180-min retry
backoff + abandon-at-6, rate-limited backlog/abandoned founder alert (no body), 7-/30-day retention
sweep. **038-2c-ii landed 2026-09-10** (BL149 038-2c-ii completion record): `UpdatesFeedCache`
consecutive-failure counter + body-free `content_source_failure` founder alert on the 3rd+ failure,
per-instance 1/30-min throttle (separate bucket), awaited after `_fetchGate` release — 1 prod + 1
test file. All of 038-2c done. **038-2d** split three ways at preflight (2d-i backend activation guard →
2d-ii submit dialog + Help entry → 2d-iii menu entry points; BL149 "038-2d preflight + split").
**038-2d-i landed 2026-09-10** — production fail-fast when `Feedback:Enabled` is on without a valid
`FounderChannel:WebhookUrl`; flag on in `appsettings.Development.json` only. **038-2d-ii landed
2026-09-10** — `api.submitFeedback` + `FeedbackDialog` (verbatim ADR-500 retention line, 200/202
thanked identically, `ApiError` message map) + "Report a problem" `#/help` header entry.
**038-2d-iii landed 2026-09-10** — all-roles "Send feedback" rows in `AccountMenu` (positional
`itemsRef` math replaced with a DOM-order registrar) + `MobileNavMenu`, gated on
`VITE_FEEDBACK_ENABLED`; `ophalo-app` 1169/1169. **All of GAP-038 038-2 is code-complete.**
The only remaining GAP-038 work is the founder's paired activation in the pilot deploy window:
`Feedback__Enabled=true` + `FounderChannel__WebhookUrl` (Railway) + `VITE_FEEDBACK_ENABLED=true`
(PWA build), set together (038-2d-i fails the deploy on a half-configured pair).

**GAP-073 — request-detail composer draft safety (pilot gate).** [ADR-502](decisions/ADR-502-request-detail-composer-draft-safety.md);
[BL150](build-log/150-gap-073-request-detail-composer-draft-safety.md) carries the spec + the
073-1 (`3599ee3c`) + 073-2 completion records. **Slice 073-1 landed 2026-09-10** — 9-file frontend
gate (`useComposerDraft` shared sessionStorage store, `unloadGuard`, both composers rewired,
`readOnly`-on-409 + version-advance reset on both paths); browser-verified (mock workbench + real-API
two-tab 409). **Slice 073-2 landed 2026-09-10, awaiting Christian's diff review** — deletion-only
removal of the now-dead `customerUpdateDraft*` / `businessUpdateDraft*` prop chain from
`RequestDetail` / `RequestDetailContent` / `RequestDetailWorkCanvas` / `UnifiedComposer` + 6
pass-through test files (4 prod + 6 test; brief said 7 test — `UnifiedComposer.activateCustomerUpdate`
was already de-propped in 073-1). `tsc` clean, `check:tokens` pass, `ophalo-app` 1184/1184.
**All of GAP-073 is code-complete.** Hot blocker: none.

**GAP-091 — Quick Capture draft persistence (pilot gate).** [BL152](build-log/152-gap-091-quick-capture-draft-persistence.md)
carries the spec + completion record. **Implemented 2026-09-11, awaiting Christian's diff
review** — per-user AsyncStorage autosave/restore/discard + `beforeRemove` dismiss guard in
`mobile/ophalo-mobile/app/modal.tsx`; logout/401 draft cleanup in `AuthContext.tsx`; new
`@react-native-async-storage/async-storage` dependency. `tsc` clean, `vitest` 33/33.
**GAP-091 is code-complete.** Hot blocker: none.

**GAP-093 — session-layer account-lifecycle gate (pilot gate).** [BL153](build-log/153-gap-093-session-account-lifecycle-gate.md)
carries the preflight + completion record. **Implemented 2026-09-11, awaiting Christian's diff
review** — `SessionData`/`SessionStore`/`SessionAuthenticationHandler` fail closed on
`Account.LifecycleState != Active` immediately after the existing membership gate; 4 prod + 1 test
file, no drift from preflight. `AuthApiTests` 37/37 (2 new regressions), unit 1909/1909,
architecture 14/14. **GAP-093 is code-complete.** Hot blocker: none.

**`ophalo-web` Next maintenance.** Separate, implementation-ready maintenance record:
[BL151](build-log/151-ophalo-web-next-16-3-maintenance-preflight.md). Upgrade only Next `16.2.9` →
`16.3.4`, add the `node: 22.x` engine pin, regenerate the web lockfile and generated `next-env.d.ts`,
then pass local build/typecheck and founder-owned Vercel preview acceptance. React 19.3 and .NET
SDK/Docker reproducibility are intentionally separate follow-ups; do not batch this with GAP-073.

**Deferred operational to-do — Help & Updates feed bootstrap.** After GAP-073, upload the canonical
`docs/content/updates.json` to the existing production R2 bucket as `platform/updates.json`.
Production logs prove R2 is reachable but the object is missing; upload + one valid feed read resets
the alert streak. No deploy or code change.

## Next

Code order is locked in the workboard Next list: GAP-073 → GAP-091 → GAP-093 → GAP-094 → GAP-095 →
GAP-040 → GAP-063 → GAP-048 → GAP-049 → GAP-092 → GAP-072 → GAP-047. GAP-073 protects
customer-reply/internal-note drafts; GAP-091 prevents field-work loss in Quick Capture; GAP-093
restores a global account-lifecycle fail-closed backstop; GAP-094 prevents auth mail-bombing and
sign-in-invalidation DoS; GAP-095 closes externally observable auth enumeration. The latter three
are now supervised-pilot gates, placed after the code-complete work-loss gates and before marketing
or routine workflow gates. GAP-092 remains a pilot-risk correction. Audit Vectors 1–5 are complete
(2026-09-10); Vector 8 discovery is complete (2026-09-11, findings in
[production-readiness audit](audits/production-readiness-audit.md) Vector 8) — GAP-096 remains a
pre-GA hardening batch with no pilot urgency. Vectors 6, 7, 9, 10, 11 are not started (Vector 6's
F6.1 landed as `dc50f134` outside the full-vector pass). GAP-038 discovery ADR locked ([ADR-500](decisions/ADR-500-in-product-feedback-and-help-updates-loop.md));
route/compatibility policy locked ([ADR-501](decisions/ADR-501-api-route-and-compatibility-policy.md):
no `/api/v1` prefix, flat routes, domain-owned — GAP-038's `GET /updates` + `POST /feedback` are
Foundation-owned; ADR-500 amended). Preflight done 2026-09-07: all named frontend surfaces
(`MobileNavMenu`, `RequestListContent`, `App.tsx` hash router, `--ophalo-accent`/`--ophalo-attention`
tokens) and backend patterns (minimal-API endpoints, typed HttpClient, `OpHaloDbContext`) confirmed;
no founder-channel webhook and no CSP config exist yet. Failed-delivery scope settled: bounded retry
+ backlog alert, no operator UI (inside the ADR-293 boundary). Implementation
build-log started ([BL149](build-log/149-gap-038-in-product-feedback-help-updates-implementation.md)):
D1–D8 open decisions drafted with recommendations (R2 content source, backend-proxied guide images
to drop the CSP dependency, `snarkdown`+`dompurify`, 5s/5min proxy, minimal `feedback` table + retry
BackgroundService, proposed visual values, two shell entry points, 038-1/038-2 slice split).
D1–D8 resolved after a review pass (delete-on-success → null-body-on-success + 7-day metadata sweep;
explicit retry backoff 1/5/15/60/180 min; generic webhook notifier; guide-image proxy constrained
to a fixed R2 prefix + MIME/size caps; CSP baseline deferred to its own DEF ticket). D5 is
persist-first, at-least-once, scrub-on-success (`503`/`202` cases specified). The original three
slices are 038-1a content backend (no founder-channel infra) → 038-1b content frontend → 038-2
feedback path; 038-2 is now gate-split as recorded in Active. **038-1a and all 038-1b slices landed
2026-09-08** (see the BL149 completion records). R2 has no restorable S3-style object versioning;
repo history is the rollback path. GAP-054 slice 054-1 landed as `4f1caffb`; it is no longer a
GAP-038 blocker. GAP-072 still needs a discovery ADR.

## Hot blocker

GAP-039 Batch 4 (founder-owned ops: Sentry/Railway/Vercel DSNs, healthcheck, founder alert,
production-candidate gate — [BL140](build-log/140-gap-039-sentry-implementation-handoff.md)) is owed
before any customer-facing pilot. Runs in parallel with the coding queue.
