# 132 — UI-001 Queue + Workbench Shell Preflight

**Status:** Implementation-ready preflight — route/shell approach and focused-fallback identity
locked below (2026-08-21). No production code, tests, routing, or layouts changed in this pass.

**Governing decisions:** UI-001 (locked 2026-08-21), UI-002, UI-003, UI-004 + amendment (locked
2026-08-21), UI-005. Source: `docs/ux-design/v2/keep-ui-production-decision-register.md`,
`docs/ux-design/v2/keep-ui-design-model-v2.md` §5/§13, `docs/ux-design/v2/keep-component-spec-v2.md`,
`docs/ux-design/v2/keep-review-rubric-v2.md`.

**Prior handoff:** `docs/session-log.md` — Keep UI V2 Production Upgrade, Sequenced delivery plan,
Step 5. Reuses the completed current-page Office Review control (commit `452da70`) as the
behavioral contract.

## 1. Current architecture (as-is)

`web/ophalo-app/src/App.tsx` treats `requests` (list) and `detail` (`#/request/{id}`) as **mutually
exclusive top-level routes** (`AppRoute` union, `App.tsx:28-34`; render gates at `App.tsx:446-521`).
There is no shared two-pane shell today:

- `route.page === "requests"` renders `<Requests>` full-page (`web/ophalo-app/src/pages/Requests.tsx`,
  572 lines) — list only, no selected-request pane.
- `route.page === "detail"` renders `<RequestDetail requestId .../>` full-page
  (`web/ophalo-app/src/pages/RequestDetail.tsx`, exported at line 384) — detail only, no queue pane.
- Navigation between them is a full route swap via `navigate()`/`selectRequest()`
  (`App.tsx:164-221`), not a pane update.
- `RequestNavContext` (`App.tsx:37-39`, `{requestIds: string[]}`) already carries prev/next
  request-id context into `RequestDetail` for its own header prev/next controls
  (`App.tsx:212-219`, passed as `prevId`/`nextId`).

This confirms UI-001's core gap: **no master-detail shell exists**. Step 5 is a genuine new shell,
not a resize of an existing one.

## 2. Reusable Office Review / queue-navigation contract (from 452da70)

`RequestQueueNavigation` (`web/ophalo-app/src/components/requests/RequestQueueNavigation.tsx`, 463
lines) already implements, and Step 5 must reuse without modification to its behavioral contract:

- `OfficeReviewState` union (`loading` / `error+retry` / `ready{aggregate, members}`) — lines 16-23.
- `OfficeReviewControl` (lines 187-289): collapsed `"Office Review · N pending"`, active-member
  naming, actionable-first/empty-collapsed member ordering, structured loading placeholder (not a
  blank bar), navy/neutral treatment, aggregate-gated visibility (`aggregate <= 0` → `null`).
- `DisclosureButton` (lines 50-126): plain disclosure/group semantics (not ARIA menu), Escape +
  outside-pointerdown dismissal with focus return, shared `openDisclosure` state so Office
  Review/Views are mutually exclusive (lines 25, 310).
- `ViewsControl` (lines 128-186): Watching-only secondary view, active-naming pattern.
- Roving-tabindex primary-tab keyboard handling (`handleTabKeyDown`, lines 314-335).
- Data source in `Requests.tsx`: `officeReviewMembers` from `getOfficeReviewMembersForRole(role)`
  (`requestsWorkspace.ts:73`), `officeReview: OfficeReviewState` built at `Requests.tsx:295-302`
  from `GET /keep/pricebook/actual-work/review-queue/count` (Slice A-1) plus existing Ready to
  Close / Feedback Review view counts — never a guessed zero, never `.length`.

**What changes only in layout, not logic (per component-spec-v2 and the UI-004 amendment target
composition):**

- Row 1 primary-tab presentation: horizontal tablist (current) → two-row grid at 320–360px
  (Owner/Admin: Needs Attention full-width row 1, All Work | My Work row 2; Operator: My Work
  full-width row 1, Needs Attention | Available row 2). This is new markup inside
  `RequestQueueNavigation`'s row-1 block (`lines 343-398`), gated on a new "pane width" mode prop
  — not a rewrite of tab selection/keyboard logic.
- Office Review strip width: intrinsic/content-width (current, `OfficeReviewControl` label span) →
  full pane width in the Queue pane. Styling-only change to the trigger/container, not to
  `OfficeReviewState` handling.
- Views/History placement: same row as tabs, wrap-together fallback (current, lines 378-397) →
  dedicated row below Office Review, Views left / History right (Queue pane). This is a container
  reflow, not new control logic.

None of `OfficeReviewState`, the count-source query, the disclosure mutual-exclusion state machine,
or the actionable/empty member split needs to change for Step 5.

## 3. New surfaces required

### 3.1 Shell / layout

- New component, e.g. `web/ophalo-app/src/components/requests/RequestWorkbenchShell.tsx`: owns the
  bounded-width measurement (320–360 CSS-px Queue pane vs. protected Workbench minimum) and renders
  either the two-pane layout or the existing focused one-pane drill-down. Per UI-001, this is a
  **container/minimum-width rule, not a fixed breakpoint** — needs a `ResizeObserver` (or
  equivalent) on the workspace container, not a CSS media query alone, since 100/125/150% browser
  zoom must be evaluated at their effective CSS-px width.
- **Locked route/shell approach (2026-08-21):** retain the existing `AppRoute` union unchanged —
  `#/requests` and `#/request/{id}` remain the only two durable states (UI-002 unchanged, no
  optional-selection route, no `?id=`). The shell reinterprets what each route renders based on
  protected-workbench-minimum width:
  - `#/requests` on a wide workspace → Queue pane + Priority Preview in Pane 2.
  - `#/request/{id}` on a wide workspace → Queue pane + that request's Workbench in Pane 2.
  - Either route when the workspace does not protect a usable Workbench minimum → today's existing
    focused full-page `Requests` or `RequestDetail` presentation, unchanged (see focused-fallback
    lock below).
  This means `navigate()`/`selectRequest()` (`App.tsx:164-221`) do not change shape; only the
  render gates at `App.tsx:446-521` become width-aware instead of route-exclusive.

### 3.2 Priority Preview (UI-003)

Does not exist yet in any form — `grep` for "Priority Preview" in `web/ophalo-app/src` returns no
matches (verified in this preflight pass). New component required: read-only, non-mutating,
server-ranked, branches on attention/no-attention/filtered-empty/empty per UI-003. Must not touch
`history.pushState`, mark-viewed, or activity/audit endpoints.

### 3.3 Request Anchor / Work Canvas reuse (UI-005)

`RequestDetailDesktopLayout` (`web/ophalo-app/src/pages/request-detail/RequestDetailDesktopLayout.tsx`)
and its panel set (`DetailPanels.tsx`: `LogContactCard`, `MarkHandledCard`, `WorkControlsGroup`,
`FeedbackSummaryCard`, `CustomerPanel`, `ServiceLocationPanel`, `TriagePanel`, `SourceMetaPanel`,
plus `RequestDetailHeader`) are the existing sticky-header + scrollable-body implementation and are
the reuse target for the Workbench pane's Request Anchor + Work Canvas. Confirm during
implementation preflight whether `RequestDetailDesktopLayout` can mount inside a narrower pane
width unchanged, or needs its own width-aware adjustments — out of scope to determine further
without a sizing spike.

## 4. Durable routing / Back-Forward/refresh (UI-002)

Already correct at the route level: `getRouteFromLocation()` (`App.tsx:47+`) parses `#/request/{id}`
directly from `window.location.hash`, and a `popstate`-driven `setRoute` effect exists
(`App.tsx:130`). UI-001 must **preserve** this — direct load, refresh, and Back/Forward already
resolve to `{page: "detail", requestId}`. The shell change is only about what renders for that
route (embedded pane vs. today's full page), not about introducing new routing logic. No `?id=`
parameter should be introduced (locked prohibition, UI-002).

## 5. Migration sequencing (locked order, 2026-08-21 — not started)

1. **Measurement/sizing spike:** container-width measurement approach for the bounded Queue pane
   vs. one-pane drill-down threshold, including a **proposed protected-Workbench-minimum value**
   (a concrete CSS-px number, derived from `RequestDetailDesktopLayout`'s existing content
   requirements); validate at 100/125/150% zoom with populated data per UI-001's explicit review
   requirement. This step produces a measurement result and a proposed minimum, not shippable UI —
   it does not render for real users and is not gated by UI-003.
2. **Lock the retained-route shell approach** documented in §3.1 above as the implementation
   contract before any shell component is written.
3. **First functional wide shell:** Queue pane + a minimal, UI-003-compliant Priority Preview in
   Pane 2 for `#/requests`. This is the first user-visible state of the shell and must satisfy
   UI-003 in full (server-ranked attention/no-attention/filtered-empty/empty branches, no route or
   activity/audit side effect) — a placeholder or "select an item" Pane 2 is not an acceptable
   intermediate state, locked instruction. `#/request/{id}` continues to render via the existing
   focused full-page `RequestDetail` fallback until step 5 lands.
4. **Queue-pane layout variant:** extend `RequestQueueNavigation` with the pane-width mode (two-row
   primary grid, full-width Office Review, dedicated Views/History row) per the UI-004 amendment
   target composition. Reuse `OfficeReviewState`/data flow unchanged.
5. **Embed/adapt the real Request Detail Workbench** in its own bounded slice: mount
   `RequestDetailDesktopLayout`/Anchor+Canvas inside Pane 2 for `#/request/{id}` on a wide
   workspace, reconciling `RequestNavContext` prev/next with the Queue pane's own list. This is the
   step most likely to need its own sub-slices under the batch-size gate and must not be bundled
   with step 3 or 4.
6. **One-pane fallback and final route verification:** confirm the locked focused-fallback identity
   (§6) renders correctly below the protected-minimum threshold for both routes, and that direct
   load/refresh/Back/Forward resolve correctly across the width boundary (e.g. resizing below
   threshold mid-session).

## 6. Decisions locked (2026-08-21)

- **Route/render coupling — locked:** the `AppRoute` union is retained unchanged (§3.1). No
  optional-selection route, no `?id=`, no change to `navigate()`/`selectRequest()` shape. Only the
  render gates in `App.tsx` become width-aware.
- **Focused one-pane fallback identity — locked:** the fallback is today's existing
  `Requests`/`RequestDetail` full-page presentations, reused as-is. UI-001 does not authorize a
  third narrow/focused layout. `RequestWorkbenchShell` renders one of (a) wide two-pane shell or
  (b) the existing full-page component for the active route — it does not itself own a narrow
  layout.

## 7. Remaining open item — resolved 2026-08-21

- **`RequestDetailDesktopLayout` narrow-pane fitness — resolved by the Step 1 spike:** confirmed
  protected Workbench minimum is **640 CSS-px** (`docs/build-log/133-ui-001-step1-measurement-spike-preflight.md`,
  reviewed and approved by Christian), now locked in `keep-ui-design-model-v2.md` §13 along with the
  derived protected application-workspace minimum of **1001 CSS-px** (360 Queue + 640 Workbench + 1px
  border). Full re-verification of `RequestDetailDesktopLayout` still happens when it is actually
  embedded in Step 5, since the spike measured it standalone in a temporary harness, not inside the
  real shell.

## 8. Focused tests / visual verification (to run during implementation, not now)

- Existing `Requests.*.test.tsx` and `RequestQueueNavigation` tests must stay green through the
  pane-width mode addition (regression, not new coverage).
- New: shell width-threshold unit tests (pane vs. one-pane fallback) at representative CSS-px
  values.
- New: Priority Preview branch coverage (attention / no-attention / filtered-empty / empty), each
  asserting no route/history/mutation side effect.
- New: two-row primary grid contract test at Owner/Admin and Operator, asserting label text,
  no horizontal scroll/clip, and touch-target size.
- Manual: 100/125/150% zoom visual verification of both the bounded two-pane shell and the
  one-pane fallback with populated data (per UI-001 and the V2 review rubric).
- Manual: direct-route load, refresh, and Back/Forward for `#/request/{id}` inside the new shell.

## 9. Explicit out-of-scope for Step 5

- Manual collapsible-queue control (explicitly excluded by UI-001 in this first redesign release).
- Any change to Office Review's count source, aggregation, or disclosure state machine.
- Action semantics (UI-006), form containment (UI-007/UI-008), state/recovery (UI-009), public
  intake/customer pages (UI-010/UI-011) — unaffected by this shell slice.
- Mobile/narrow-screen layout beyond the already-locked one-pane fallback.
