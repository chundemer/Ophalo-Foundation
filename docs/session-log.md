# Session Log — OpHalo Foundation

**Last updated:** 2026-08-21
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

### Locked decision — Actual Work Draft ownership (2026-08-20)

**Pilot blocker:** [GAP-055 — Actual Work capture is incorrectly blocked by dispatch assignment](pilot-readiness-bug-tracker.md#gap-055--actual-work-capture-is-incorrectly-blocked-by-dispatch-assignment).

The prior active-Responsible-only recorder rule is superseded. **First-recorder ownership is
locked:** any active member with `RequestsOperate + ActualWorkCapture` may create the one open
Draft; creation preserves immutable `CreatedByUserId` authorship and sets the exclusive current
`RecorderAccountUserId`. Only the recorder may mutate, expand, read Draft-bound nudges, discard, or
submit. An Owner/Admin may transfer an unsubmitted Draft through a reason-required immutable audit
event; submitted visits stay immutable, silent takeover is prohibited, and Request assignment stays
dispatch context.

**Plan:** perform a mechanical ownership-remediation preflight for the explicit recorder field and
transfer audit, then audit/replace every active-Responsible gate and affected UI copy in bounded
migration, authorization/API, transfer, and frontend/test batches. [GAP-055](pilot-readiness-bug-tracker.md#gap-055--actual-work-capture-is-incorrectly-blocked-by-dispatch-assignment)
is the authoritative acceptance and sequencing record. **5d-ii-d was paused pending this
correction; the correction is now complete (Batches A–D below) and 5d-ii-d is unpaused.**

**GAP-055 remediation batches:**

| Batch | Status | Durable record |
|---|---|---|
| A — domain + migration (`RecorderAccountUserId`, `TransferRecorder`) | Complete | commit `b3b3d41` |
| B — Draft mutation/expand authorization swap | Complete | commit `d26b955` |
| C — history/nudge read seams, `IActiveResponsibleCheck` removed | Complete | commit `72ce6a5` |
| D — Owner/Admin transfer API + audit event | Complete | commit `c7ce822` |

A/B/C replace every active-Responsible authorization check with a direct
`RecorderAccountUserId` comparison across create, edit, discard, submit, assembly expansion, and
nudge read; the open Draft is now also visible read-only to Owner/Admin (not just its recorder) via
a new `IsRecorder` flag on the history read response. D adds
`POST /keep/pricebook/actual-work/{actualWorkId}/transfer-recorder` — Owner/Admin-only,
reason-required, recording an immutable `ActualWorkDraftRecorderTransfer` audit row atomically with
the `RecorderAccountUserId` change. **GAP-055 remediation and 5d-ii-d are both complete** (commit
`6543f81`); **slice 6 — Owner/Admin review mutation** is split into 6A (domain/persistence/
migration, complete, commit `5a24e43`) and 6B (review API/endpoints/DI, next approved slice).

Prior implementation rules, now superseded:

- The request's active Responsible user was the sole field recorder; Owner/Admin was price-blind
  while using field capture.
- One Draft per request; Drafts were editable/discardable only by the active Responsible user;
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
| 5d-ii-d — nudges frontend + assembly search grouping fix | Complete | Build Log 129; commit `6543f81` |

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
   5. **5d-ii-d — frontend:** Complete. Build Log 129, "5d-ii-d implementation preflight"; commit
      `6543f81`. 3 production files (`apiClient.types.ts`, `apiClient.ts`,
      `ActualWorkComposer.tsx`), 1 test file; 0 new mutation families (reuses `addActualWorkLine`/
      `expandActualWorkAssembly`). Nudge state/fetch inline in `ActualWorkComposer.tsx`, mirroring
      `ComposerNudgePanel`'s UX only (session-only chips, client-side Dismiss), not its file split.
      Same commit also fixed a pre-existing 5d-i-b usability gap found during manual verification:
      `ActualWorkSearchAndAdd`'s search results listed assemblies after every catalog match in one
      undifferentiated scroll box, making them practically undiscoverable; now mirrors Proposed
      Scope's grouped "Matching assemblies" / "Matching catalog items" layout. 17/17 focused tests
      passing.
4. **6 — Owner/Admin review mutation:** Complete, split two ways (Build Log 129, "6 preflight —
   locked decisions"). Owner/Admin + `RequestsOperate` + Price Book entitlement, no new permission
   key; nullable `ReviewedAtUtc`/`ReviewedByAccountUserId`/`ReviewNote` on `ActualWork`;
   single-shot; per-request signal resolve atomic with the review; not blocked on terminal
   requests.
   1. **6A — domain, persistence, migration:** Complete. Build Log 129, "6A implementation notes".
      6 production files, 2 test files. 34/34 unit + 8/8 persistence integration tests passing.
   2. **6B — review API service, endpoints, DI:** Complete. Build Log 129, "6B implementation
      notes". 4 production files (added `ErrorHttpMapper.cs` beyond the original 3-file estimate),
      1 test file. 8/8 new API tests; 130/130 Actual Work integration + 49/49 unit tests passing,
      no regressions.
5. **7 — Owner/Admin financial read:** Complete. Build Log 129, "7 implementation notes". Two
   read-only GETs: `GET /keep/pricebook/actual-work/review-queue` (account-wide unreviewed-review
   queue, per-visit rows, request context only) and
   `GET /keep/pricebook/actual-work/{id}/financial-detail` (factual record plus immutable snapshot
   totals, Standard/Expected Direct Cost, margin). Per-visit aggregation only, no per-request
   rollup. A line is incomplete when either `SellPriceSnapshot` or
   `StandardExpectedDirectCostSnapshot` is null (not `PriceBookVersionLineId` alone); incomplete
   data forces all-null visit totals, never a partial sum or fabricated $0. Same authorization gate
   as 6B (Owner/Admin + `RequestsOperate` + Price Book entitlement, no new permission). 5 production
   files, 1 new interface + 1 new EF class + 1 new API service + 2 endpoint-layer edits, 2 test
   files; 6/6 unit + 12/12 API integration tests passing, 142/142 Actual Work integration + 55/55
   Actual Work unit tests passing overall, no regressions.
6. **8 — Owner/Admin review UI:** split into 8A (Requests-workspace review-queue tab, read-only)
   and 8B (request-detail financial review card + review mutation).
   1. **8A — Requests-workspace review-queue tab:** Complete. `apiClient.types.ts`/`apiClient.ts`
      wiring for all three financial-read/review endpoints (previously unused by the frontend);
      new client-only `actual_work_review` tab (not a `RequestView` — backed directly by
      `GET .../review-queue`, distinct row shape from `RequestListContent`); new
      `ActualWorkReviewQueueList` component; `RequestQueueNavigation` threads a client-side
      queue-length badge (no count endpoint). 6 production files, 1 extended test file
      (`Requests.queueTransition.test.tsx`, +2 tests). TypeScript clean, `git diff --check`
      clean, extended file 8/8 passing, full focused `Requests.*` suite 45/45 passing.
   2. **Contract patch (Slice 7/8A correction):** Complete. `ActualWorkFinancialDetailResult` had
      no `ConcurrencyVersion`, so 8B's review card would have had no valid expected version to
      send to `POST .../review`. Added `ConcurrencyVersion` to the record, serialized it from the
      `financial-detail` endpoint, added it to the frontend type. 3 production files
      (`ActualWorkFinancialReadApiService.cs`, `KeepEndpoints.cs`, `apiClient.types.ts`), 1 test
      file extended (`ActualWorkFinancialReadApiTests.cs`: existing detail test now asserts the
      returned version matches the DB row; new `FinancialDetail_ConcurrencyVersion_CanBeUsedToReview`
      proves the real round trip — fetch financial-detail, POST that version to `/review`, 200).
      13/13 passing in that class, `ActualWorkReviewApiTests` 8/8 unaffected. No standalone
      frontend apiClient contract test added — the backend round trip proves the wire contract;
      8B's review-card test is where a client-side assertion has a real consumer (financial-detail
      supplies `concurrencyVersion`, the review mutation must send that exact value).
   3. **8B — request-detail financial review card + review mutation:** Preflight complete, **build
      paused** — Christian is starting a UI/UX redesign and does not want 8B built ahead of it only
      to be rebuilt after. Resume 8B from this preflight once the redesign lands; re-validate
      against the redesigned request-detail layout before implementing, don't implement from this
      preflight unchanged if the layout has moved.
      - No "financial detail for a request" endpoint exists — only per-`actualWorkId`. Plan was to
        reuse `useActualWorkHistory`'s already-fetched `submittedVisits[].id` (== `actualWorkId`)
        rather than add a new request-scoped read, then fetch financial-detail per visit for
        Owner/Admin — one card, one row per visit, mirroring `ActualWorkHistoryCard`'s existing
        multi-visit shape.
      - `role`/`isOwnerOrAdmin` is not threaded into `request-detail/*` anywhere today — no
        Owner/Admin-gated card exists there yet. Plan was a single `isOwnerOrAdmin: boolean` prop
        from `RequestDetail.tsx`'s existing `meQuery.data?.accountRole` into `RequestDetailContent`
        only (not the shared `RequestDetailLayoutProps` — mobile/desktop layout components don't
        need it).
      - Quiet-hide on 403 for Owner/Admin too (entitlement/blocked-account gate can still fail) —
        mirrors `useActualWorkHistory`'s convention; skip the fetch outright for non-Owner/Admin.
      - `ActualWork.AlreadyReviewed` (409, distinguishable via `ApiError.code`) is expected/routine
        on a shared review queue, not exceptional — plan was to re-fetch that visit's detail and
        show the real reviewer/note rather than a generic conflict error.
      - Planned file list (re-verify against the redesign before implementing): new
        `useActualWorkFinancialReview.ts` hook, new `ActualWorkFinancialReviewCard.tsx`, edits to
        `RequestDetailContent.tsx` and `RequestDetail.tsx`. 1 mutation family, 4 production files.
        Test file: add `ActualWorkFinancialReviewCard.test.tsx` (new file warranted here, unlike
        8A) proving financial-detail supplies `concurrencyVersion` and the review call sends that
        exact value.

Every slice needs its own exact file/test count and validated preflight. Do not bundle later
financial/review work into field capture merely because files overlap.

## Active work — Keep UI V2 Production Upgrade

**Product boundary:** [docs/ux-design/v2/](ux-design/v2/) — README, decision register (UI-001–UI-013
locked), design model, component spec, review rubric. Separate track from Direct Actual Work above.

Full-codebase implementation preflight is complete (architecture map, decision-to-code mapping for
UI-001–UI-013, migration sequence). The implementation sequence below deliberately separates the
small, current-page Office Review control from the later UI-001 Queue + Workbench shell. They share
one role and data contract, but they are not the same layout task.

### Locked decision — Owner/Admin Office Review amendment (2026-08-21)

Supersedes plain More-Views grouping for Owner/Admin's Ready to Close / Feedback Review / Actual
Work Review. The amendment is recorded in the current V2 documentation changes and must be committed
separately from later frontend work:

- Owner/Admin primary tabs stay exactly Needs Attention, All Work, My Work — unchanged.
- Add a conditional, persistent **Office Review** control below the primary tabs, above
  search/filter. It aggregates Ready to Close + Feedback Review + Actual Work Review counts,
  uses navy/neutral treatment (never amber — reserved for customer-promise risk), and opens an
  accessible chooser naming each queue and its count. It is hidden only after the aggregate is
  authoritatively known to be zero; while loading, reserve a compact placeholder rather than
  guessing zero.
- Watching becomes a separate quiet Views/utility control for Owner/Admin (not part of Office
  Review). Operator is unchanged: primary My Work/Needs Attention/Available, Watching stays behind
  its quiet Views control.
- The behavior and data contract applies to both presentations. On today's full-width
  `#/requests` page, Office Review is a compact, content-width control — never a stretched,
  full-page faux input. In UI-001's future 320–360 CSS-px Queue pane, the same component becomes
  pane-width and primary tabs use a two-row grid: Owner/Admin row 1 Needs Attention; row 2 All Work
  | My Work. Operator row 1 My Work; row 2 Needs Attention | Available. No horizontal scrolling or
  clipped labels.

**Why:** no authoritative server count existed for Actual Work Review (only a full-row-list
endpoint) — the prior client badge already leaned on that list's `.length`, which the Office Review
aggregate must not repeat.

### Delivered slices

1. **Slice A-1 — Actual Work Review authoritative count (backend):** Complete, commit `1e35335`.
   `IActualWorkFinancialReviewPersistence.CountUnreviewedAsync`,
   `EfActualWorkFinancialReviewPersistence` (COUNT query, no row projection),
   `ActualWorkFinancialReadApiService.GetReviewQueueCountAsync` (same Owner/Admin gate as the
   existing queue read), new `GET /keep/pricebook/actual-work/review-queue/count` →
   `{ count: int }`. 4 production files, 1 test file (5 new HTTP integration tests: account-scoped
   count, zero case, Operator 403, missing-entitlement 403, unauthenticated 401). 18/18
   `ActualWorkFinancialReadApiTests` passing. No migration, no mutation handler.
2. **Earlier documentation amendment:** Uncommitted and superseded in part. The four V2 documents
   contain the Office Review, Views, and narrow Queue-pane rules, but their old containment wording
   incorrectly prohibited the current full-width implementation. Step 1 below corrects that wording
   before frontend work resumes.

### Rejected attempt — Slice A-2 full-width frontend (2026-08-21)

A-2 was implemented and reviewed twice (Office Review, Views/History split, a two-row grid via a
real CSS container query, accessible disclosure semantics). The failure was structural, not a
product-rule failure: it applied the Queue-pane layout unchanged to a 1,100px+ full-width page. That
made Office Review a stretched faux input and left Views/History disconnected from the controls.

**Disposition:** not committed. Preserved as `stash@{0}` on `main`, labeled
`slice-a2-full-width-superseded-frontend` — do not discard or apply wholesale. Its label wording,
active-context naming, accessible disclosure pattern, and loading-state work are reference material
for the new compact current-page slice and for the later Queue-pane variant.

### Sequenced delivery plan

1. **Correct the V2 presentation contract — complete (2026-08-21).** The decision register, design
   model, component spec, and review rubric now distinguish the current compact full-width
   Requests-page variant from the later bounded Queue-pane variant while preserving one shared
   role/count/action contract.
2. **Preflight the current-page Office Review slice — complete (2026-08-21).** Preflight was
   reviewed and corrected twice (locked primary-tab order/labels, Row 1/Row 2 layout and
   unclipped-popover containment, disclosure mutual exclusion, error-vs-loading Office Review
   state) before implementation began.
3. **Review the preflight before code — complete (2026-08-21).** Accepted after corrections; the
   accepted preflight produced a compact Office Review control directly below the primary tabs,
   keeps Views/History purposeful, and does not use a full-width Queue-pane treatment.
4. **Implement and verify the current-page slice — complete (2026-08-21).** Delivered: locked
   Owner/Admin primary-tab order (Needs Attention, All Work, My Work) and the single "My Work"
   label on both roles; Watching moved to a **Views** disclosure; Ready to Close/Feedback
   Review/Actual Work Review moved to an Owner/Admin-only **Office Review** disclosure sourced
   from authoritative counts only (Actual Work Review via the Slice A-1 count endpoint, never
   review-queue `.length`); Row 1 (primary tabs + Views/History sharing space, wrapping together
   when space doesn't permit) and Row 2 (Office Review, its own row) with `overflow-x-auto`
   scoped to the tablist only so neither popover clips; mutual-exclusion disclosure state with
   correct focus-return semantics; `aria-controls`-based disclosure relationship (not
   `aria-haspopup`, which implies a menu); an explicit `loading` / `error+retry` / `ready`
   Office Review state (error is distinct from loading, never a guessed zero); removed the
   duplicate "Ready to close" header pill now superseded by Office Review. Fixed one latent
   pre-existing bug found along the way: the new-session landing tab now actually lands on
   Needs Attention (the already-locked decision), which the old tab ordering never satisfied.
   Visual verification at narrow current-page widths and 100/125/150% zoom completed by
   Christian. Not committed yet — code and documentation are queued as separate commits.
5. **Build UI-001 later as its own migration slice — next.** Reuse the Office Review component
   with its Queue-pane presentation (pane-width strip, dedicated Views/History row, and
   320–360px two-row primary tab grid). Do not let that future shell block the current
   operational fix, which is already delivered.

### Current stop point

Step 4 is complete and verified (typecheck, full frontend suite, CSS token check, visual
verification). Awaiting commit of the V2/session-log documentation (this update) separately from
the frontend implementation commit.

Step 5 (UI-001's bounded Queue + Workbench shell) is its own migration slice, sequenced and
preflighted separately — see `docs/build-log/132-ui-001-queue-workbench-shell-preflight.md` for the
locked architecture (retained-route shell, reused focused fallback) and 6-step build order.

**UI-001 migration progress:**
- Step 1 (measurement/sizing spike) — **complete, 2026-08-21**
  (`docs/build-log/133-ui-001-step1-measurement-spike-preflight.md`). Protected Workbench minimum
  locked at 640 CSS-px; protected application-workspace minimum locked at 1001 CSS-px, recorded in
  `docs/ux-design/v2/keep-ui-design-model-v2.md` §13.
- Step 2 (lock retained-route shell approach) — already resolved as part of the build-log 132
  preflight (§3.1/§6); no separate implementation action needed.
- **Step 3 (first functional wide shell: Queue pane + UI-003-compliant Priority Preview) — next.**
  Needs its own exact preflight before implementation; not started.
- Steps 4–6 (Queue-pane layout variant, embedded Workbench, one-pane fallback verification) —
  not started.

### Verified baseline

`dotnet build` on `OpHalo.Api`/`OpHalo.IntegrationTests` clean; 18/18 focused
`ActualWorkFinancialReadApiTests` passing (13 pre-existing + 5 new), committed at `1e35335`.

Frontend (uncommitted, this session): `tsc --noEmit` clean; `vitest run` full suite 515/515
passing (49 files); CSS token check clean. 6 production files, 6 test files (5 modified + 1 new
`requestsWorkspace.test.ts`) changed, 12 total.

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
