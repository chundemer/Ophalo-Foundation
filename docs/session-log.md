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
| 5c — submitted-history UI | Complete | Build Log 129; commit `3cd9ec5` (5a auth fix), `9bf6266` |
| 5d-i-a — assembly expansion, backend | Complete | Build Log 129; commit `2a2d0de` |
| 5d-ii-a1 — nudges domain + persistence contract | Complete | Build Log 129; commit `aa76a9e` |
| 5d-ii-a2 — nudges EF persistence + migration | Complete | Build Log 129; commit `6984768` |
| 5d-ii-b — nudges Owner/Admin config API | Complete | Build Log 129; commit `a1d6478` |
| 5d-ii-c — nudges technician field-read API | Complete | Build Log 129; commit `b4cf5b0` |

5b shipped with both lifecycle corrections already fixed and regression-tested in the same commit:
the submitted confirmation no longer unmounts on the post-submit history refresh, and a create-time
`409 DraftAlreadyOpenForRequest` reconciles to the authoritative Draft with the shared conflict
notice. `useActualWorkCapture.test.ts`/`ActualWorkComposer.test.tsx` cover both cases (24/24
passing, verified 2026-08-20).

5c's preflight found 5a's read gate over-restrictive: it required `RequestsOperate` AND
`ActualWorkCapture`, so a Viewer (`RequestsView` + account-wide visibility) got 403 instead of the
locked "every request-visible reader" intent. Corrected in `3cd9ec5`: the read gate now requires
only `RequestsView`, with `Owner`/`Admin`/`Viewer` → `AccountWide` and `Operator` → `MyWork`;
`canCaptureActualWork` (and therefore `openDraft` visibility) is computed separately from
`RequestsOperate` + `ActualWorkCapture` + active-Responsible. `ActualWorkHistoryApiTests` (9/9)
covers the Viewer-200/read-only/no-Draft case. 5c itself (`9bf6266`) is a standalone,
price-blind, read-only submitted-visit history card — its own probe (`useActualWorkHistory`), not
a reuse of `useActualWorkCapture` (which discards `submittedVisits` for non-capturing callers).
Explicit empty state, outcome-code-to-label mapping, quiet 403 hide, compact retry on other
failures; a successful submit now also refreshes this card via `actualWorkHistory.retry()`. 144/144
request-detail frontend tests passing.

**Business-completeness correction — field assist is required before pilot readiness.** 5b's
composer supports individual catalog search and custom/off-catalog lines, but it omitted assembly
expansion and contextual nudges. That is a product gap, not deferred UI polish: a technician needs
support to find and record the complete factual work performed, rather than repeatedly searching
for individual components or falling back to vague custom entries. **5d — Actual Work field
assist: assembly expansion and nudges** is now required after 5c and before the office-review
slices. It must remain price-blind and factual—no automatic additions, financial exposure, or
Proposed Scope recommendation leakage—and needs its own exact file/test-count mechanical preflight
before any code. That preflight must settle assembly snapshot/duplicate behavior and the nudge
eligibility, deduplication, dismissal, explicit-add, and Draft-concurrency contracts.

### Next approved slices

1. **5d-i-a — Actual Work assembly expansion, backend:** Complete. Build Log 129, "5d-i-a
   implementation notes"; commit pending review. Atomic `expand-assembly` transaction
   (`IActualWorkAssemblyExpansionPersistence`/`EfActualWorkAssemblyExpansionPersistence`), skip-
   and-report duplicate handling, inclusion-list optional-item contract, active-Responsible check
   moved inside the locked transaction (no pre-transaction tracked load). 7 production files, 8
   new/modified tests, 66/66 Actual Work integration tests passing.
2. **5d-i-b — Actual Work assembly expansion, frontend:** Complete. `ActualWorkComposer` now
   expands `OfferingAssembly` search results with optional items defaulted out, reports skipped
   duplicate components, and reconciles a stale Draft token through the existing conflict path.
   `FieldScopeSearch` now accepts `RequestsOperate` plus either capture permission. Build Log 129,
   “5d-i-b implementation notes”; commit pending. 26 focused frontend tests, TypeScript check,
   and 44 focused HTTP integration tests passing.
3. **5d-ii — Actual Work nudges:** preflight complete, split five ways (Build Log 129, "5d-ii
   preflight — locked decisions"). Separate, Owner/Admin-configured Actual Work rule set (same
   price-blind association shape as Proposed Scope's `ScopeNudgeRule`, not the same rows/table —
   factual-completion intent, not upsell), stateless eligibility filtering against the Draft, no
   persisted dismissal, explicit add via the ordinary single-line add path. Config CRUD gate:
   `PriceBookCatalogManage` (locked). Five independently gated sessions, in order:
   1. **5d-ii-a1 — domain + application persistence contract:** Complete. Build Log 129, "5d-ii-a1
      implementation notes". `ActualWorkNudgeRule`, `ActualWorkNudgeSuggestion`,
      `ActualWorkNudgeSuggestionSetValidator`, `ActualWorkNudgeRuleErrors`,
      `IActualWorkNudgeRulePersistence`. 5 production files, 2 new test files, 15/15 passing.
   2. **5d-ii-a2 — EF persistence, mappings, migration, persistence tests:** Complete. Build Log
      129, "5d-ii-a2 implementation notes". `ActualWorkNudgeRuleConfiguration`,
      `ActualWorkNudgeSuggestionConfiguration`, `EfActualWorkNudgeRulePersistence`, migration
      `20260820191413_ActualWorkNudgeRule`. 3 production files, 1 new test file, 9/9 persistence
      tests passing against real PostgreSQL.
   3. **5d-ii-b — Owner/Admin config API:** Complete. Build Log 129, "5d-ii-b implementation
      notes". Create/Update/Delete + list, mirroring `ScopeNudgeRuleConfigApiService`/
      `ScopeNudgeRuleEndpoints` exactly, gated `PriceBookCatalogManage`. 5 production files, 1 new
      test file, 17/17 passing.
   4. **5d-ii-c — technician field-read API:** Complete. Build Log 129, "5d-ii-c implementation
      notes". `ActualWorkNudgeFieldReadApiService`, `ActualWorkNudgeFieldReadEndpoints`
      (`GET /keep/pricebook/actual-work/{actualWorkId}/nudge-suggestions`). Authorization mirrors
      `ActualWorkDraftApiService`'s active-Responsible gate (not ScopeNudge's row-visibility read);
      dedupe suppresses only catalog-item suggestions already on the Draft (no assembly provenance
      retained); account posture is Blocked-only. Add reuses
      `ActualWorkDraftApiService.AddLineAsync`; no new mutation handler. 4 production files, 1 new
      test file, 11/11 passing.
   5. **5d-ii-d — frontend.** Fetch/render nudge suggestions in `ActualWorkComposer`, tap-to-add.
4. **6 — Owner/Admin review mutation:** mark reviewed with reviewer/time/optional internal note,
   then atomically resolve the Actual Work signal only when no submitted visit remains unreviewed.
5. **7 — Owner/Admin financial read:** immutable snapshot totals, expected direct cost, margin,
   and explicit incomplete-financial-data cues.
6. **8 — Owner/Admin review UI:** existing Requests-workspace tab plus request-detail review card.

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
