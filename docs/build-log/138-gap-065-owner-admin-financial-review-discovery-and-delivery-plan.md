# Build Log 138 — GAP-065 Owner/Admin Financial Review Discovery And Delivery Plan

**Status:** Slice 1A discovery complete and accepted (2026-09-02). **Slice 1B-server implemented and
committed (`faf7b64`, 2026-09-02)** — the privileged request-scoped projection, the request-scoped
pending read, the bounded batched resolution/disposition reads, the endpoint, and auth / query /
status-derivation / batched-read tests. **Slice 1B-client implemented and committed (`e27c48c`,
2026-09-02) — the hook, the `Pending financial reviews (N)` card, wide/narrow entry, and the
cross-hook refresh wiring (Option 1) with four review corrections; frontend suite 1004/1004.** See
§"Slice 1B-client — implemented" below.
**Date:** 2026-09-02
**Related:** [GAP-065](../pilot-readiness-bug-tracker.md#gap-065--owneradmin-internal-financial-review-work-is-hard-to-discover-from-requests), [BL136](136-actual-work-paper-compatible-pilot-upgrade.md), [ADR-494](../decisions/ADR-494-actual-work-paper-compatible-pilot-upgrade.md), [ADR-493](../decisions/ADR-493-actual-work-office-financial-resolution-and-billing-revisions.md)

## Purpose

GAP-065 removes the office-navigation friction that currently makes a submitted visit difficult to
find and review. An Owner/Admin can today open Request Detail, expand passive Visit history, then
use a small **Open in workspace** link for one visit. That is not an adequate entry point for
active financial-review work, especially when a request has more than one submitted visit.

This build log preserves the already-locked product decisions and defines the discovery gate for
the first implementation slice. It does not authorize production code, a database change, an
invoice/billing feature, or an authorization change.

## Locked domain and workflow boundary

1. An Actual Work visit remains an immutable, dated field record. Visits on the same request or
   service account are never merged. Their submitted price/cost snapshots, line performers, visit
   notes, corrections/supersession lineage, and audit evidence remain per visit.
2. Financial review remains per visit. A future customer charge may group reviewed visits, but
   Keep has no Billing Revision/invoice-bundle entity in this scope. Do not imply one exists.
3. **Submitted and unreviewed is an active office task; reviewed is history.** A Draft is not
   review work, but an active Draft must not hide a prior submitted/unreviewed visit.
4. Owner/Admin financial-review discovery is separate from customer-promise Attention, request
   lifecycle, request ranking, and ordinary queue counts. No presentation change may create a
   client-side lifecycle or review policy.

## Existing technical truth

### Ordinary Request Detail history is intentionally insufficient

The request-visible `GET` history path projects `ActualWorkSubmittedVisitEntry`. It includes an
ID, submitted timestamp, line history/count, visit note, and supersession lineage. It does **not**
include `reviewedAtUtc`, financial completeness/blockers, a pending-review flag/count, or review
readiness.

That omission is material. The history read is available to normally request-visible callers, so
Slice 1 must not simply add Owner/Admin financial-review facts to its shared response and expose
them to Operators/Viewers. It also cannot infer them from `status === Submitted`, local Draft
presence, or general queue membership.

Relevant current seams:

- `src/OpHalo.Keep.Application/PriceBook/ActualWorkHistoryReadApiService.cs` — ordinary submitted
  history projection.
- `web/ophalo-app/src/lib/apiClient.types.ts` — `ActualWorkSubmittedVisitEntry` client contract.
- `web/ophalo-app/src/pages/request-detail/ActualWorkHistoryCard.tsx` — passive history and the
  current wide-only **Open in workspace** route.

### Owner/Admin financial detail has the needed facts, but only after entry

`ActualWorkFinancialDetailResult` contains `reviewedAtUtc`, incompleteness/blockers, totals, and
line financial state for an exact visit. It powers the existing financial-review card/workspace.
It is not a safe substitute for a Request Detail list/card: fetching every visit detail simply to
derive task state would be inefficient, coupling-prone, and risks broadening the read surface.

The database-side `ActualWorkNeedsOfficeReview` signal already has the required aggregate
semantics: it stays raised until no submitted, unreviewed, non-superseded visit remains for the
request. The discovery pass must determine the smallest authorized way to project the per-visit
facts necessary for an Owner/Admin UI; it must not duplicate or weaken that predicate in React.

Relevant current seams:

- `web/ophalo-app/src/pages/request-detail/useActualWorkFinancialReview.ts`
- `web/ophalo-app/src/pages/request-detail/ActualWorkFinancialReviewWorkspace.tsx`
- `src/OpHalo.Keep.Infrastructure/Persistence/EfActualWorkReviewSignalReconciliation.cs`

## Delivery plan

### Slice 1A — mechanical discovery and implementation gate (this session)

No production code. Establish and record:

1. The exact request route/controller/service used by the ordinary history read, the financial
   detail read, and the account-wide Office Review queue.
2. The existing Owner/Admin authorization capability/policy that is appropriate for a small
   request-scoped review-task projection. Reuse an existing policy if it is truthful; do not
   invent a role check in the client.
3. The smallest server-authoritative, Owner/Admin-only contract required by the Request Detail
   task card. At minimum, assess whether each live submitted visit needs: ID, submitted time, line
   count, review status, financial readiness/blocker summary, and safe recorder/technician display
   name. Exclude price/cost amounts unless a separately approved UI requires them.
4. Whether the aggregate work signal can be reused as a request-level presence/count source, and
   where the canonical per-visit predicate must live. A signal alone is insufficient for the card
   rows.
5. The exact frontend owner/capability gate and refresh/invalidation path after review, financial
   resolution, supersession, or replacement.
6. A file-level implementation gate, migration determination, focused test inventory, and batch
   split. Stop for approval before edits.

The likely outcome is a small privileged server read/projection plus a Request Detail presentation
slice, but that is a hypothesis—not permission to implement it without the gate.

### Slice 1B — Request Detail direct entry (after approved gate)

For Owner/Admin only, render **Pending financial reviews (N)** in the Actual Work region before
passive Visit history. Render one live, submitted/unreviewed/non-superseded visit per row with:

- submitted timestamp;
- recorder/technician identity only when the authoritative projection can state it truthfully;
- line count;
- a factual status: **Ready to review** when financial data is complete, or **Needs cost/price
  resolution** when it is not; and
- a direct **Review financials** action to that exact visit's existing workspace deep link.

Only a visit with `reviewedAtUtc` may display **Review complete**. Keep reviewed and superseded
visits in passive history. Keep the completed GAP-065A behavior: an active Draft never hides
previous submitted history or the task card.

### Slice 2 — request-scoped workspace continuation (separate session)

Retain exact-visit URLs. Add a compact pending-visit switcher in the Owner/Admin wide workspace;
the selected visit is explicit and pending visits dominate any audit/history list. After success,
show a confirmation plus **Review next pending visit** and **Back to request**. Never
auto-navigate. Switching with a dirty reviewer note or in-progress resolution input requires a
discard confirmation.

### Slice 3 — queue discoverability (separate preflight and sessions)

Add a quiet, server-authoritative Owner/Admin request-row count cue such as **2 visits need
review**. Normal request-row navigation continues to open Request Detail. Make the existing
account-wide Office/Actual Work Review destination clearly named and persistent with a truthful
empty state. A cross-request, one-row-per-visit review queue is later work and requires its own
read model, authorization, ranking, and empty-state decision.

## Explicit deferrals and prohibitions

- No merging visits or editing submitted factual records.
- No Billing Revision, invoice, payment, accounting, payroll, or commission feature.
- No generic **Review all** / batch-complete control until eligibility, reviewer-note semantics,
  resolution handling, and audit evidence are separately specified.
- No automatic navigation after an individual review.
- No review cue for Operators/Viewers, Drafts, reviewed/superseded visits, or terminal requests
  with no outstanding submitted visit.
- No client-inferred review state, request ranking/count/Attention change, or authorization
  broadening.

## Slice 1 acceptance criteria

The approved Slice 1B must let an Owner/Admin open Request Detail once and route directly to any
outstanding visit without expanding passive history. The UI must distinguish multiple outstanding
visits and must remain truthful after review, resolution, correction/supersession, and refresh.
An Operator/Viewer must not receive financial-review state through the new projection. Focused
coverage must prove multi-visit, reviewed, superseded, Draft-plus-history, role, and post-mutation
refresh cases.

## Slice 1A findings and corrected implementation gate (2026-09-02, accepted)

### Existing routes, services, and seams

| Concern | Route | Service | Persistence |
| --- | --- | --- | --- |
| Ordinary submitted history (request-visible, incl. Operator `MyWork`) | `GET /keep/pricebook/actual-work/request/{requestId}/history` | `ActualWorkHistoryReadApiService` | `IActualWorkPersistence.GetSubmittedVisitsForRequestAsync` |
| Owner/Admin single-visit financial detail | `GET /keep/pricebook/actual-work/{actualWorkId}/financial-detail` | `ActualWorkFinancialReadApiService.GetFinancialDetailAsync` | `IActualWorkFinancialReviewPersistence` + resolution/disposition seams |
| Owner/Admin account-wide review queue + count | `GET /keep/pricebook/actual-work/review-queue`, `.../review-queue/count` | `ActualWorkFinancialReadApiService` | `IActualWorkFinancialReviewPersistence.GetUnreviewedQueueAsync` / `CountUnreviewedAsync` |
| Existing exact-visit workspace deep link | hash route `#/request/{requestId}/actual-work/{visitId}` (`visit` = `new` \| `draft` \| visit id) | `ActualWorkWorkspacePage` -> `useActualWorkWorkspace` | — |

The workspace route is wide-viewport only: below 1001px `ActualWorkWorkspacePage` deliberately
exits back to Request Detail and financial review renders inline on the page.

### Authorization — reuse, do not invent

`ActualWorkFinancialReadApiService.AuthorizeAsync` is already the exact gate Slice 1B needs:
authenticated -> non-blocked / non-read-only account access -> `PriceBookQuotesMaterials`
entitlement -> explicit **Owner/Admin** role check -> `RequestsOperate` -> `AccountingManage`.
Identical composition to `ActualWorkReviewApiService`. The new request-scoped read belongs on
**this service**. It must **not** go on `ActualWorkHistoryReadApiService`, whose gate is only
`RequestsView` and which serves Operators/Viewers under a `MyWork` visibility scope — adding
review facts to its response would leak them (BL138 boundary 4).

Client role gate already exists: `RequestDetail.tsx` derives
`accountRole in {owner, admin}` from the `["me"]` query and threads it down as the
`canReviewActualWork` prop (aliased `n`). Reuse it; add no new client role logic.

### Smallest server-authoritative contract (Slice 1B-server)

New endpoint `GET /keep/pricebook/actual-work/request/{requestId}/pending-financial-reviews`,
Owner/Admin-only, served by a new
`ActualWorkFinancialReadApiService.GetPendingReviewsForRequestAsync(requestId, ct)`.

Per-row DTO `ActualWorkRequestPendingReviewEntry`, one row per **live** submitted / unreviewed /
non-superseded visit on the request:

- `actualWorkId`
- `submittedAtUtc`
- `lineCount`
- `recorderDisplayName` — resolved now via
  `IKeepRequestOperatePersistence.GetActorDisplayNameAsync(visit.RecorderAccountUserId)`; this is
  the recorder, which is truthful. Per-line technician attribution is out of scope for Slice 1.
- `reviewStatus`: one of **three** values (corrections below), never `ReviewComplete` — a reviewed
  visit is excluded by the query and lives in passive history:
  - `ReadyToReview`
  - `NeedsCostPriceResolution`
  - `NeedsNoChargeDisposition`

No sell price, cost, margin, or line-level amounts. Result wrapper carries the row count so the
card header `Pending financial reviews (N)` needs no second call.

#### Correction 1 — readiness must apply effective financial resolutions (not the queue shortcut)

The account-wide queue (`ActualWorkFinancialReadApiService.ToQueueEntry`) projects with
`NoResolutions` and its own comment concedes a visit with resolved blockers still reads
"pessimistically incomplete" there. The request-scoped card **must not** reuse that shortcut. For
`ReadyToReview` / `NeedsCostPriceResolution` to be truthful, the projection must load and fold each
visit's effective `ActualWorkLineFinancialResolution` rows —
`ActualWorkFinancialProjection.ProjectVisit(lines, resolutions)` with the real resolution list,
exactly as `GetFinancialDetailAsync` already does per visit.

**Seam gap:** `IActualWorkFinancialResolutionPersistence` today exposes only per-visit
`GetResolutionsForVisitAsync` / `GetDispositionsForVisitAsync`. The 1B-server preflight must add a
**bounded, request-scoped batched read** — resolutions and dispositions for the set of pending
visit ids in one query each (`WHERE account_id = @a AND actual_work_id = ANY(@ids)`, grouped in
memory by `ActualWorkId`). Do **not** silently issue an unbounded per-visit N+1, and do **not**
fall back to the queue's snapshot-only calculation.

#### Correction 2 — zero-line visits need a third status

`ActualWorkFinancialProjection.ProjectVisit` on a zero-line visit yields
`HasIncompleteFinancialData = false`, so it would wrongly read `ReadyToReview`. But a zero-line
submitted visit with no `NoCharge` office disposition is blocked at review by a **distinct** gate
(`ActualWorkReviewResult.BlockedZeroLineDisposition` ->
`ActualWorkErrors.ReviewBlockedZeroLineDispositionRequired`; `RecordDispositionAsync` requires
`visit.Lines.Count == 0`). The projection must therefore include the **disposition fact**
(mirroring `ActualWorkFinancialDetailResult.HasNoChargeDisposition`, which is always false when the
visit has lines) and determine:

| Condition | `reviewStatus` |
| --- | --- |
| `Lines.Count == 0 && !hasNoChargeDisposition` | `NeedsNoChargeDisposition` |
| `Lines.Count > 0 && effective-projection HasIncompleteFinancialData` | `NeedsCostPriceResolution` |
| otherwise (lines complete, or zero-line with a `NoCharge` disposition) | `ReadyToReview` |

Client copy for the third state: **"Record no-charge disposition"**.

### Predicate ownership

The canonical "owes office review" predicate exists server-side in two identical places:
`EfActualWorkFinancialReviewPersistence` (`Status == Submitted && ReviewedAtUtc == null &&
SupersededAtUtc == null`) and `EfActualWorkReviewSignalReconciliation.OpenOutstandingReviewPredicate`
(same, plus `deleted_at_utc IS NULL`). Slice 1B-server adds a **third consumer**: a new
`GetPendingReviewsForRequestAsync(accountId, requestId, ct)` on `IActualWorkFinancialReviewPersistence`
that is the existing `GetUnreviewedQueueAsync` query **plus `visit.RequestId == requestId`**. React
must not re-derive this predicate. The `ActualWorkNeedsOfficeReview` work signal is a request-level
presence bit and is **not currently on any Request Detail payload**; Slice 1B does not need it
(the card's own row list is the presence proof). Slice 3's request-row count cue is where that
signal / a dedicated projection is revisited.

### Migration

None. Pure read over existing columns.

### Frontend gate and refresh path (Slice 1B-client)

Client gating: new card rendered in `RequestDetailActualWorkSection` **above**
`ActualWorkHistoryCard`, gated on `canReviewActualWork` (`n`); non-reviewers see nothing new.
GAP-065A behavior preserved: the card renders even while an editable Draft is open.

**Narrow-viewport `Review financials` (locked):** below 1001px, do **not** open the workspace
route and do **not** defer. The action scrolls to and moves focus into that exact unreviewed
visit's existing inline financial-review card, so the admin does not hunt among multiple cards.
No new narrow workspace or selection model. Wide viewport keeps using
`onNavigateToActualWorkspace(requestId, visitId)` (route above).

**Refresh path — the earlier finding was wrong and is corrected here.**
`RequestDetail.tsx#handleActualWorkReviewSuccess` currently invalidates **only**
`["actual-work-review-queue"]` and `["actual-work-review-queue-count"]`. It does **not** invalidate
history. Separately, `useActualWorkFinancialReview` is **not** a React Query consumer: it holds its
own `state` and a local `reload()` that re-fetches `getActualWorkFinancialDetail` per visit, and
every state-changing path already calls that local `reload()` —
`review`, `resolveLine`, `recordNoChargeDisposition`, and the `mapMutationError` reconcile branches
(409/404, `ReviewBlockedIncomplete`, `ReviewBlockedZeroLine`); `replace` returns
`{ kind: "replaced" }` and `RequestDetailContent#handleReplaceVisit` then calls
`actualWorkHistory.reload()` and re-probes capture.

There is therefore **no shared query-key invalidation point** to hang the new query on. The
1B-client preflight must nail down one of:

1. the pending-review hook exposes its own `reload()` (mirroring `useActualWorkFinancialReview`),
   and `RequestDetailContent` — which already composes all three Actual Work hooks and coordinates
   cross-hook refresh — fires it; or
2. the pending-review hook is a React Query query keyed `["actual-work-pending-reviews", requestId]`
   and `RequestDetailContent` invalidates that key.

Either way the refresh must be wired at **every** outcome that changes row membership or readiness,
via an explicit `onFinancialReviewChanged`-style callback threaded
`RequestDetailContent -> RequestDetailActualWorkSection -> ActualWorkReviewCard`, fired on:

- **financial resolution success** — `NeedsCostPriceResolution` can become `ReadyToReview`;
- **no-charge disposition success** — when it changes readiness;
- **review completion** — the row disappears;
- **replacement / supersession** — the row disappears or its successor row appears/changes
  (`handleReplaceVisit` success branch);
- **reconcile / retry paths** where the authoritative detail read changed
  (`mapMutationError` 409/404 and `ReviewBlocked*` branches, `onRetryReview`).

Adding the key to `handleActualWorkReviewSuccess` alone is explicitly **not** sufficient.

### Slice 1B-client refresh ownership — DECIDED (2026-09-02, 1B-client preflight)

**Chosen: Option 1 — the pending-review hook exposes its own `reload()`; `RequestDetailContent`
fires it.** Option 2 (React Query key + `queryClient.invalidateQueries`) is rejected: none of the
three Actual Work hooks `RequestDetailContent` already composes (`useActualWorkCapture`,
`useActualWorkHistory` with `retry`, `useActualWorkFinancialReview` with `reload`/`retry`) is a
React Query consumer, `RequestDetailContent` holds no `queryClient`, and BL138 already establishes
that `RequestDetail.tsx#handleActualWorkReviewSuccess` is the wrong hang point. A fourth hook that
matched the sibling pattern keeps the coordinator uniform.

Concrete wiring the 1B-client batch must implement:

1. **New hook `useActualWorkPendingReviews(requestId, enabled)`** — local `useState` +
   `useEffect(reload)`, mirroring `useActualWorkHistory`. `enabled` is
   `props.canReviewActualWork === true` (a 403 still degrades to a `hidden` state as a backstop).
   It calls the new `GET .../request/{requestId}/pending-financial-reviews`. Returns
   `{ state, reload }`. It must **not** re-derive the submitted/unreviewed/non-superseded predicate
   or the three-value status — both are server-authoritative.
2. **`RequestDetailContent` composes it** alongside the other three and owns a single
   `handleFinancialReviewChanged` callback that calls `pendingReviews.reload()` (and nothing else —
   `useActualWorkFinancialReview` already self-reloads its own detail state, and history refresh
   stays on the existing `handleReviewSuccess`).
3. **New callback `onFinancialReviewChanged` threaded
   `RequestDetailContent -> RequestDetailActualWorkSection -> ActualWorkReviewCard` (the per-visit
   `Visit`).** Today `ActualWorkReviewCard`'s `Visit` fires `onReviewSuccess()` only on
   `onReview` -> `outcome.kind === "success"`. The new callback fires on **every** outcome that can
   change the pending card's row membership or a row's `reviewStatus`:
   - `onReview` -> `{ kind: "success" }` — the row disappears;
   - `onResolveLine` -> `{ kind: "success" }` — `NeedsCostPriceResolution` may become
     `ReadyToReview`;
   - `onRecordNoChargeDisposition` -> `{ kind: "success" }` — `NeedsNoChargeDisposition` becomes
     `ReadyToReview`;
   - any `{ kind: "reconciled" }`, `{ kind: "review-blocked-incomplete" }`,
     `{ kind: "review-blocked-zero-line" }` from `mapMutationError` — the authoritative detail read
     changed under the card, so the pending projection may have too;
   - `handleReplaceVisit` success branch (`outcome.kind === "replaced"`) — the source row
     disappears and a successor row appears; wire it in `RequestDetailContent` next to the existing
     `actualWorkHistory.retry()` call, not in the card.
   `onRetryReview` (manual retry) should also call it.
4. **Narrow-viewport `Review financials` (BL138 locked):** below 1001px the row action scrolls to
   and focuses that exact visit's inline review card. `ActualWorkReviewCard` currently exposes only
   the region-level `id="focus-panel-actual-work-review"`; 1B-client must add a **per-visit anchor
   id** on the `Visit` element (e.g. `id={`actual-work-review-visit-${visit.id}`}`) and the pending
   card's narrow handler does `scrollIntoView` + `focus()` on it. Wide viewport keeps
   `onNavigateToActualWorkspace(requestId, visitId)`.
5. **Card placement:** rendered by `RequestDetailActualWorkSection` above `ActualWorkHistoryCard`,
   gated on `canReviewActualWork`; renders even while an editable Draft is open (GAP-065A).

### Batch split (accepted)

Slice 1B as one change is over the batch gate. Split:

- **Slice 1B-server** — the privileged request-scoped projection (readiness folded from effective
  resolutions + the disposition fact, three-value `reviewStatus`), the new request-scoped pending
  read on `IActualWorkFinancialReviewPersistence`, the new bounded batched
  resolution/disposition-by-visit-ids read on `IActualWorkFinancialResolutionPersistence`, the
  endpoint, the API contract type, and authorization/query/contract tests. No client files.
- **Slice 1B-client** — the read hook, the `Pending financial reviews (N)` task card, wide-route
  navigation, narrow-viewport scroll-and-focus behavior, the cross-hook refresh wiring above, and
  UI tests.

### Focused test inventory

- **1B-server:** auth matrix (Owner/Admin pass; Operator, Viewer, unauthenticated, wrong
  entitlement, blocked/read-only account all fail); multiple pending visits on one request;
  reviewed visit excluded; superseded visit excluded; draft present alongside pending history;
  request-scoped filter (a pending visit on another request is excluded); count matches row list;
  **status derivation** — lined visit with complete snapshots -> `ReadyToReview`; lined visit with
  a missing component and **no** resolution -> `NeedsCostPriceResolution`; lined visit whose
  missing component **has** an effective resolution -> `ReadyToReview` (proves Correction 1, the
  queue shortcut is not reused); zero-line visit with no disposition ->
  `NeedsNoChargeDisposition`; zero-line visit with a `NoCharge` disposition -> `ReadyToReview`;
  **batched read** — resolutions/dispositions for N pending visits load in one query each, no
  per-visit N+1.
- **1B-client:** card hidden without `canReviewActualWork`; row renders submitted time / line count
  / recorder / status; wide row click navigates to the workspace deep link; narrow row click
  scrolls to and focuses the matching inline review card; card refreshes after resolution, review,
  and replacement outcomes; card still renders with an open editable Draft.

## Slice 1B-client — implemented (`e27c48c`, 2026-09-02)

Built per §"Slice 1B-client refresh ownership — DECIDED" and the frontend gate section, plus four
review corrections. **7 production + 5 test files; frontend suite 1004/1004, `tsc` clean.**

- **`useActualWorkPendingReviews(requestId, enabled)`** — local `useState` + `useEffect(reload)`,
  mirrors `useActualWorkHistory`; `enabled = canReviewActualWork === true`; 403 → `hidden` backstop.
  Returns `{ state, reload }`. No client re-derivation of the predicate or the three-value status.
- **`ActualWorkPendingReviewsCard`** — `Pending financial reviews (N)` header + one row per pending
  visit (submitted time, line count, recorder, status), `Review financials` action. Rendered by
  `RequestDetailActualWorkSection` above the Actual Work module, gated on `canReviewActualWork`,
  both viewports, renders with an open Draft (GAP-065A). Self-hides on loading / hidden / empty;
  retry on error. Locked zero-line copy: **"Record no-charge disposition"** (correction 4).
- **Wide entry** → `onNavigateToActualWorkspace(requestId, actualWorkId)`. **Narrow entry** →
  `RequestDetailContent` holds `pendingFocusVisitId` (set on click, no click-time DOM lookup);
  `ActualWorkReviewCard` carries a per-visit anchor `id="actual-work-review-visit-${visit.id}"`
  (`tabIndex=-1`, `scroll-mt-4`, focus ring) and an effect scrolls + `focus()`s it once its visits
  are loaded, then calls `onFocusVisitHandled` to clear the request — race-free regardless of mount
  order, self-clearing if the target visit is no longer pending (correction 3).
- **Refresh** — single `handleFinancialReviewChanged` → `pendingReviews.reload()` in
  `RequestDetailContent`, threaded as `onFinancialReviewChanged` through the section to
  `ActualWorkReviewCard`, fired on: review success; resolution / no-charge **success and
  `reconciled`** (correction 1); `review-blocked-incomplete` / `review-blocked-zero-line`;
  `onRetryReview` manual retry (correction 2); and the `handleReplaceVisit` `replaced` branch (wired
  in `RequestDetailContent`, not the card).
- **Contract types** added to hand-maintained `apiClient.types.ts`
  (`ActualWorkPendingReviewStatus`, `ActualWorkRequestPendingReviewEntry`,
  `ActualWorkRequestPendingReviewsResult`) + `api.getActualWorkPendingReviewsForRequest`.
- Test infra: `Element.prototype.scrollIntoView` stub is local to `ActualWorkReviewCard.test.tsx`
  (a global stub in `src/test/setup.ts` broke an `ActualWorkComposer` assertion — reverted).

## Slice 2 — implemented (`6ab880b`, 2026-09-02)

Frontend only, per the accepted Slice 2 gate. **6 production + 3 test files (1 new); `tsc` clean;
frontend suite 1014/1014.** No API / permission / migration / server change.

- **Switcher data** — `ActualWorkWorkspacePage` composes `useActualWorkPendingReviews(requestId,
  canReviewActualWork)` (server-authoritative; the same hook 1B-client added). Keyed to the
  request, so a visit switch does not refetch it. No client re-derivation.
- **Switcher UI** — `PendingVisitSwitcher` inside `ActualWorkFinancialReviewWorkspace`, a `<nav>`
  band below the header, rendered **only for `pendingItems.length >= 2`**. Current visit is
  `aria-current` and inert; every other row calls the switch path for that exact `actualWorkId`.
- **Switch mechanism** — new `onSwitchVisit(actualWorkId)` prop; `App.tsx` does
  `history.replaceState` to `#/request/{id}/actual-work/{visitId}` + `setRoute` (exact-visit URL
  kept, no Back-stack entry — GAP-061 pattern). The page passes `key={visit.id}` to the workspace
  so it remounts cleanly per visit.
- **"Review next pending visit"** — page computes `nextPendingVisitId` = first `pendingItems` entry
  whose `actualWorkId !== reviewVisitId` (server order, **no wraparound**; `null` when none). The
  workspace's post-review block shows **Review next pending visit** (when non-null) + **Back to
  request**; both route through the same guarded nav. Never auto-navigates.
- **Dirty-switch protection** — optional `onDirtyChange` added to `FinancialResolutionForm`,
  `NoChargeDispositionForm`, `ReplaceVisitForm` (fires on any non-empty field incl. correction
  reason text; unmount reports `false`). The workspace aggregates those into a stable keyed
  registry + the reviewer-note diff → `isDirty`; switch / back / next while dirty opens an inline
  `role="alertdialog"` discard confirm ("Keep editing" / "Discard and continue"). `isDirty` is
  forced false once `reviewed` (forms unmounted). Browser refresh is deliberately not guarded.
- **Refresh** — `reloadPendingUnlessHidden` wraps `review` / `resolveLine` /
  `recordNoChargeDisposition` in the page and reloads the projection on every non-`hidden` outcome
  (BL138 §3 list); the `replaced` branch of `handleReplace` reloads too.
- Wide-only (unchanged): the page still redirects a narrow deep-link to Request Detail.
- **Known limitation:** while a switched-to visit's financial detail loads, the page briefly shows
  the price-blind read-only view (no switcher) before the workspace re-renders.

Files: `App.tsx`, `ActualWorkWorkspacePage.tsx`, `ActualWorkFinancialReviewWorkspace.tsx`,
`FinancialResolutionForm.tsx`, `NoChargeDispositionForm.tsx`, `ReplaceVisitForm.tsx`;
tests `ActualWorkFinancialReviewWorkspace.slice2.test.tsx` (new — switcher visibility, switch,
note-dirty + form-dirty guard, keep-editing, review-next target, no-wraparound), plus additions to
`ActualWorkWorkspacePage.officeRegion.test.tsx` (page-level switcher + post-mutation reload) and a
one-line `onSwitchVisit` prop in `ActualWorkWorkspacePage.test.tsx`.

## Slice 3 — preflight decisions (2026-09-02, accepted)

Split into three micro-batches:

- **3a (server):** the quiet Owner/Admin request-row count cue — server projection + API contract +
  ADR-463 amendment. **Implemented, see below.**
- **3b (client):** the frontend type + Owner/Admin-gated `RequestRow` metadata line + tests.
- **3c (documentation-only):** the existing `actual_work_review` "Actual Work Review" tab in the
  Office Review group already satisfies BL138's "clearly named, persistent destination with a
  truthful empty state" requirement (server-authoritative count badge, Owner/Admin gating,
  `ActualWorkReviewQueueList` empty state "Nothing to review / Submitted visits awaiting review
  will appear here."). No label, copy, navigation, or authorization change — the locked UI-004
  label "Actual Work Review" is kept. Closed as documentation-only.

Cue shape: **exact server-authoritative count** (not the presence bit). Gating: **identical to the
Actual Work Review destination** — Owner/Admin + `RequestsOperate` + `AccountingManage` + Price Book
entitlement + account access under the office-financial Off Season context
(`RequestImplementsAllowedInOffSeason: false`, neither Blocked nor read-only). An account that
cannot open the destination — entitlement disabled with retained history, or Off Season — must show
neither the cue nor a dead link. Quiet, non-interactive; no ranking/Attention/count/routing change;
never for Operators/Viewers.

## Slice 3a — implemented locally, pending commit (2026-09-02)

Server only. **4 production + 3 test files; unit suite green, architecture 14/14, touched
integration classes green.** No migration, no DI registration change (the interface is extended,
not new). The commit hash is recorded here in the later hash-follow-up commit.

- **Contract** — `KeepRequestSummary` gains `int PendingFinancialReviewCount` (serialises as
  `pendingFinancialReviewCount`; 0 for every caller that has not cleared the full gate).
  `IKeepRequestListPersistence.GetPendingFinancialReviewCountsAsync(accountId, requestIds, ct)`
  returns a per-request-id count dictionary for the caller's already-sliced page only; a request
  with no pending visit is absent (caller reads a missing key as 0).
- **Gate** — `GetKeepRequestListService` computes `canSeeFinancialReviewCue` next to
  `canViewInternalNotes`: `isOwnerOrAdmin && canOperate && !accessDecision.IsBlocked &&
  !accessDecision.IsReadOnly && IsPermitted(AccountingManage) && await
  featureAccessResolver.IsEnabledAsync(..., CapabilityPackageFeatureKeys.PriceBookQuotesMaterials)`.
  `accessDecision` is a **second** `accountAccessPolicy.Evaluate` with
  `RequestImplementsAllowedInOffSeason: false` — the list's own gate uses `true` and only rejects
  Blocked, but the destination is read-only in Off Season, so the cue must match that. New ctor
  dependency `IAccountFeatureAccessResolver` (already DI-registered). A denied gate never triggers
  the count query.
- **Fold** — `ApplyPagePreviewsAsync` gains a `canSeeFinancialReviewCue` parameter; when true it
  calls the new read with the sliced page ids and folds `PendingFinancialReviewCount` via
  `GetValueOrDefault`. The early-return guard now also checks the new map.
- **Query** — `KeepRequestListPersistence.GetPendingFinancialReviewCountsAsync`: EF `GroupBy` over
  `Set<ActualWork>()` with `AccountId == accountId && requestIds.Contains(RequestId) && Status ==
  Submitted && ReviewedAtUtc == null && SupersededAtUtc == null` (soft-delete via the global query
  filter) — mirrors `EfActualWorkFinancialReviewPersistence.GetUnreviewedQueueAsync` exactly, never
  re-derives the predicate elsewhere.
- **ADR-463 amended** (§"Amendment — 2026-09-02") + decision-index row updated.

Files: `KeepRequestSummary.cs`, `IKeepRequestListPersistence.cs`, `GetKeepRequestListService.cs`,
`KeepRequestListPersistence.cs`; tests `KeepRequestListServiceTests.cs` (fake extension + 5 gate
cases), `KeepPersistenceProofTests.cs` (2 real-query proofs: state exclusion + account scope),
`KeepRequestListQueryApiTests.cs` (1 end-to-end contract test: entitled Owner sees `2`,
Operator/Viewer see `0`).

## Handoff instruction

Slice 1B-server (`faf7b64`), Slice 1B-client (`e27c48c`), and Slice 2 (`6ab880b`) are committed.
**Slice 3a is implemented locally, pending commit** (its hash lands in the later hash-follow-up).
Slice 3c is closed documentation-only. The remaining slice is **Slice 3b** — the client type plus
the Owner/Admin-gated `RequestRow` "{N} visit(s) need financial review" metadata line (quiet,
non-interactive) and focused frontend tests.
