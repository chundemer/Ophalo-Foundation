# Session Log — OpHalo Foundation

**Next-session pointer only.** Scope, sequencing, gates, deferrals → [workboard](workboard.md).
Locked decisions → [decision-index](decisions/decision-index.md). Working guardrails → CLAUDE.md.
If a line here would need editing when the workboard changes, it belongs in the workboard, not here.

**Updated 2026-09-13.** GAP-094, GAP-095, and GAP-098 are reviewed, merged, and deployed to `main`.
Feedback / Help & Updates is code-complete; 038-R0 (enforced schema validation) and 038-R1
(`platform/updates.json` published to R2, verified via a clean production `#/help` read with no new
`content_source_failure` alerts) are done. GAP-098's founder-alert identity enrichment and honest
confirmation copy are done ([BL156](build-log/156-gap-098-feedback-founder-identity-preflight.md)).
Next: run **038-R3** — verify authenticated Help, banner dismissal, and one harmless feedback
submission reaching the founder channel, and get explicit founder acceptance of the single-webhook
outage posture. Follow [the founder operations guide](runbook/feedback-help-updates-operations.md).
After that verification, start the **Proposed Work & Commercial Quotes** decision session from the
[workboard decision queue](workboard.md#decision-queue). GAP-040 marketing copy remains deliberately
deferred until the underlying application feature set stabilizes.

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
The remaining release splits are: **038-R0** run the enforced `pnpm validate:updates` schema check;
**038-R1** upload `docs/content/updates.json` to R2 at `platform/updates.json`; **038-R2** set
`VITE_FEEDBACK_ENABLED=true` on the Vercel **Production** environment and redeploy — found missing
during 038-R3 verification 2026-09-11 ("Report a problem" was visible in dev but absent from live
production `#/help`), fixed the same day (var set + redeploy), "Report a problem" now confirmed
visible in production; **038-R3** verify authenticated Help, banner dismissal, and one harmless
feedback submission reaching the founder channel. Production API feedback is already configured. R3
also requires explicit founder acceptance of the single-webhook outage posture; do not scale beyond
one API replica without the deferred worker/cache hardening. The standalone publisher/preview
convenience is future GAP-087, not a release prerequisite.

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
was reviewed, committed as `b7c815e4`, and pushed to `main` on 2026-09-11. It makes
`SessionData`/`SessionStore`/`SessionAuthenticationHandler` fail closed on
`Account.LifecycleState != Active` immediately after the existing membership gate; 4 prod + 1 test
file, no drift from preflight. `AuthApiTests` 37/37 (2 new regressions), unit 1909/1909,
architecture 14/14. Hot blocker: none.

**GAP-094 — auth-code / invite issuance rate limiting (pilot gate).** Both public magic-link and
authenticated-invite slices are reviewed, merged, and deployed. [BL154](build-log/154-gap-094-auth-issuance-rate-limiting.md)
holds the decisions, proof, and migration detail. Hot blocker: none.

**`ophalo-web` Next maintenance.** Separate, implementation-ready maintenance record:
[BL151](build-log/151-ophalo-web-next-16-3-maintenance-preflight.md). Upgrade only Next `16.2.9` →
`16.3.4`, add the `node: 22.x` engine pin, regenerate the web lockfile and generated `next-env.d.ts`,
then pass local build/typecheck and founder-owned Vercel preview acceptance. React 19.3 and .NET
SDK/Docker reproducibility are intentionally separate follow-ups; do not batch this with GAP-073.

**Feedback production release.** 038-R0 (enforced schema validation) and 038-R1 (R2 feed publish,
verified 2026-09-11) are done. 038-R2 was found incomplete during 038-R3 verification 2026-09-11
(`VITE_FEEDBACK_ENABLED` missing from Vercel Production) and fixed the same day.
Remaining: **038-R3** (authenticated end-to-end verification and founder outage-posture acceptance).
Two gaps surfaced during 038-R3 discussion 2026-09-11, deliberately not addressed until R3 finishes:
**GAP-087** (workboard) now also covers stale-entry pruning/lifecycle, not just the publisher UI; new
**GAP-097** (workboard) covers guide search/categorization/deep-link discoverability.
The runbook carries access, rollback, guide, and feedback-triage steps. Do not create another
feedback batch unless operational verification exposes a concrete defect.

## Next

Complete 038-R3 above. Then start Proposed Work & Commercial Quotes discovery;
the first deliverable is a decision record, not implementation. The workboard remains authoritative
for all other pilot gates, deferrals, and audit work.

## Hot blocker

GAP-039 Batch 4 (founder-owned ops: Sentry/Railway/Vercel DSNs, healthcheck, founder alert,
production-candidate gate — [BL140](build-log/140-gap-039-sentry-implementation-handoff.md)) is owed
before any customer-facing pilot. Runs in parallel with the coding queue.
