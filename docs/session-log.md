# Session Log — OpHalo Foundation

**Last updated:** 2026-08-20
**Deployment posture:** Not pilot-ready.
**Purpose:** current operational handoff only — not an implementation archive.

## Authoritative sources

- Acceptance status and prioritization: [pilot-readiness-bug-tracker.md](pilot-readiness-bug-tracker.md)
- Product decisions: [decision index](decisions/README.md) and the individual ADRs
- Implementation contracts and durable evidence: [build logs](build-log/)
- Historical session narratives removed from this working index: Git history before this
  consolidation. Do not restore them here; link the relevant ADR/build log instead.

## Current delivery target

The controlled parallel field pilot is the active target. It is a price-blind, per-visit Direct
Actual Work loop with a retained paper/software fallback for connectivity failure; the contractor's
current billing/accounting process remains authoritative until a later, separately approved handoff.
See [Build Log 131](build-log/131-next-week-parallel-field-pilot-plan.md) and
[Build Log 129](build-log/129-direct-actual-work-and-accounting-handoff-preflight.md).

Do not infer implementation authority for equipment assets, QR tagging, customer quotes,
invoicing, payments, QuickBooks sync, inventory, fleet replacement, or native-mobile work.
Those directions remain subject to their own ADR/build-log decisions.

## Active work — Direct Actual Work

**Product boundary:** [ADR-487](decisions/ADR-487-commercial-baselines-actual-work-and-accounting-closeout.md)
and [Build Log 129](build-log/129-direct-actual-work-and-accounting-handoff-preflight.md).

Locked pilot rules:

- The request's active Responsible user is the sole field recorder; Owner/Admin is price-blind
  while using field capture.
- One Draft per request; Drafts are editable/discardable only by the active Responsible user;
  submitted visits are immutable.
- Zero-line submission requires a non-blank completion note and one truthful outcome:
  `DiagnosticOnly`, `NoWorkAuthorized`, or `NoAccess`.
- Field surfaces are price-blind. Sales price, expected direct cost, totals, and margin belong only
  to the later Owner/Admin financial-review work.
- Submission raises/reopens the additive Actual Work office-review signal. Signal resolution waits
  for the later review mutation and must remain aggregate-state-driven.

### Delivered slices

| Slice | Status | Durable record |
|---|---|---|
| 1 — domain | Complete | Build Log 129; commit `fb9e6a1` |
| 2 — persistence/migration | Complete | Build Log 129; commit `29dedf0` |
| 3 — draft API/authorization | Complete | Build Log 129; commit `068f9aa` |
| 4 — atomic submit/review signal | Complete | Build Log 129; commit `2e4e88c` |
| 5a — price-blind history read | Complete | Build Log 129; commit `7c71086` |
| 5b — capture composer | Complete | Build Log 129; commit `3f3dda8` |

5b shipped with both lifecycle corrections already fixed and regression-tested in the same commit:
the submitted confirmation no longer unmounts on the post-submit history refresh, and a create-time
`409 DraftAlreadyOpenForRequest` reconciles to the authoritative Draft with the shared conflict
notice. `useActualWorkCapture.test.ts`/`ActualWorkComposer.test.tsx` cover both cases (24/24
passing, verified 2026-08-20).

### Next approved slices

1. **5c — submitted-history UI:** standalone, price-blind submitted-visit history. Do not expose
   financial/catalog identifiers or recorder/time attribution.
2. **6 — Owner/Admin review mutation:** mark reviewed with reviewer/time/optional internal note,
   then atomically resolve the Actual Work signal only when no submitted visit remains unreviewed.
3. **7 — Owner/Admin financial read:** immutable snapshot totals, expected direct cost, margin,
   and explicit incomplete-financial-data cues.
4. **8 — Owner/Admin review UI:** existing Requests-workspace tab plus request-detail review card.

Every slice needs its own exact file/test count and validated preflight. Do not bundle later
financial/review work into field capture merely because files overlap.

## Pilot-wide operational constraints

- The authenticated staff PWA is the active field surface. The Expo/native track is separate and
  not implied by PWA work.
- Price Book access requires the account capability package. Use disposable local data for mutable
  acceptance; do not seed dummy catalog data into the founder's production account.
- Public routing is: `app.ophalo.com` for authenticated staff, `www.ophalo.com` for public tracker
  and QR resolver pages, and `api.ophalo.com` for the API.
- Sentry's free errors-only offering is the selected crash diagnostic path. Do not build a generic
  application exception table or introduce paid observability/replay/tracing before revenue.

## Remaining pilot/release work

The tracker is the single status source for all GAP items. Current categories include:

- deployed public-intake/tracker and phone-input/device evidence;
- production routing, cookies, DNS, environment, health, release identity, alerting, and telemetry
  redaction verification;
- Pilot Feedback/Help & Updates, founder value reporting, and public marketing/support accuracy;
- intentionally deferred Request Detail modal dirty-close/extraction work and native parity.

Read the selected tracker item and its controlling ADR/build log before proposing code. Completed
tracker rows and prior queue sequencing are historical evidence, not active work orders.

## Working-session rules

1. Preflight: inspect the controlling tracker/ADR/build log and current code; provide exact files,
   data flow, open decisions, tests, and verification commands. Do not implement in this step.
2. Validation: independently verify the preflight. The only outcomes are approved, correct and
   resubmit, or a framed product decision.
3. Implementation: make one reviewable change set, add focused regression coverage, and run
   proportionate checks. Stop for a decision when new production scope appears.

## Release rules

- Finish or explicitly defer every selected P0/P1 tracker item before inviting a pilot customer.
- Before a production candidate, run repository checks and the controlled smoke test
  (`scripts/production-smoke-test.mjs`; see [the runbook](runbook/production-smoke-test.md)).
- Verify health/readiness, release identity, error capture, alert routing, and telemetry redaction.
- Do not onboard until production sign-in and the required end-to-end pilot checklist are verified.
