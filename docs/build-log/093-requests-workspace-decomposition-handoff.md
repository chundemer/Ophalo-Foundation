# Build Log 093 — Requests Workspace Decomposition Handoff

**Status:** Complete
**Date:** 2026-07-27
**Scope:** Session 3.2a — frontend-only, no-behavior refactor
**Controlling work to preserve:** Sessions 3.0c, 3.0d, 3.1, and 3.2; ADR-449 and ADR-450

## Purpose

`web/ophalo-app/src/pages/Requests.tsx` is now 912 lines and owns both request-list operating
policy and nearly all presentation. Continuing to add features there will make regression review
and ownership progressively harder.

This session creates stable presentation seams without changing the product. `Requests.tsx` remains
the controller; extracted components receive explicit data and callbacks. The result must behave and
render the same as committed Session 3.2.

## Preconditions

Before editing source, Claude must verify and report each item for Codex validation:

1. Session 3.2 (`f0a9aaf`) is present and the working tree contains no uncommitted Request List
   implementation change that would be mixed into this refactor.
2. The frontend test baseline, TypeScript check, CSS-token check, and `git diff --check` are clean.
3. Concrete current exports, types, and import paths are recorded. Use actual repository type names;
   do not copy pseudocode types from an earlier proposal.
4. The plan is at most eight production files and twelve changed files total. If not, stop and
   propose a cohesive split rather than exceeding the gate.

Preflight is read-only. Do not implement until Codex validates it.

## Locked Architecture

Keep this as the controller:

```text
web/ophalo-app/src/pages/Requests.tsx
```

It continues to own query/mutation wiring; all queue/history/search/filter/cursor state and reset
policy; API query keys/parameters/cache/polling; server-owned `listContext.isHistory` presentation
selection; Request Detail navigation and row-action/modal callbacks; and page-transition scroll,
`pendingPageFocusRef`, and focus-after-new-page-render timing.

Create only these support files unless a verified current seam makes one unnecessary:

```text
web/ophalo-app/src/pages/requestsWorkspace.ts
web/ophalo-app/src/components/requests/RequestsWorkspaceHeader.tsx
web/ophalo-app/src/components/requests/RequestQueueNavigation.tsx
web/ophalo-app/src/components/requests/RequestListToolbar.tsx
web/ophalo-app/src/components/requests/RequestListContent.tsx
web/ophalo-app/src/components/requests/RequestRowSkeleton.tsx
```

Together with the changed controller, this is seven production files. Do not introduce a new
`features/` architecture: this repository already uses `pages/` for controllers and `components/`
for reusable presentation.

| File | Owns | Does not own |
|---|---|---|
| `requestsWorkspace.ts` | Pure types, configuration, labels/empty-state helpers, date-parameter calculation. | React state, JSX, icons, API calls, effects. |
| `RequestsWorkspaceHeader.tsx` | Existing title/subtitle, onboarding/banner, summary-pill presentation. | Query/state policy or navigation. |
| `RequestQueueNavigation.tsx` | Existing tab/history chrome and roving-focus refs/keyboard handler; it calls controller selection callbacks. | Query construction, history interpretation, client sorting. |
| `RequestListToolbar.tsx` | Existing search, clear button, status filter, refresh/staleness controls and labels. | Submitted-query/cursor reset policy. |
| `RequestListContent.tsx` | Existing list-region markup, meaningful heading, skeleton/error/empty states, sections, rows, pager. | Fetching, page-transition state, direct navigation/mutation logic. |
| `RequestRowSkeleton.tsx` | Existing static skeleton markup/classes. | Queue-specific assumptions or loading policy. |

Pass refs/callbacks explicitly. `RequestListContent` may render the `role="region"` and focus
heading, but the controller retains the current transition policy: scroll the region immediately,
then focus its meaningful heading only after the new page renders. `RequestQueueNavigation` may own
tab refs/keyboard handling, but selection flows through the controller's reset/query policy.

Avoid an oversized prop-bag component. If one extracted component needs more than about twelve
unrelated props, split presentation responsibility or retain a small local calculation in the
controller; do not hide state policy in an object.

## Behavior Contract — Preserve Exactly

- No backend, DTO, API-client, query-key, cache, polling, authorization, URL, or route change.
- Owner/Admin `All work`, business-name heading, tab-scoped subtitle, and setup-query role gate
  remain exactly as Session 3.0d. Operator/Viewer behavior remains unchanged.
- `All work` partitions existing server order into `Needs attention` then `Open work`; never sort.
- Preserve the five-row skeleton, cached-tab behavior, roving-tabindex and Left/Right/Home/End,
  native Enter/Space activation, and clear-search focus/reset behavior.
- Preserve the 50-row cursor contract, range wording with no fabricated total, end-of-results,
  region scroll target, and deferred focus timing.
- Preserve the demoted Owner/Admin History entry point, every history scope/date mapping, and
  explicit Today UTC midnight/exclusive-upper-bound parameters.
- When a response exists, presentation follows `listContext.isHistory`; client history intent is
  only loading/navigation fallback and chrome selection.
- Preserve no-nested-controls row markup, meaningful focus heading, and the decision not to add
  `aria-controls`/`tabpanel` wiring.

## Explicit Non-Goals

- No reducer/state-machine redesign, App-level state lift, or Detail round-trip state restoration.
- No GAP-046 filter feature or GAP-027 lifecycle feature; those are later sessions.
- No visual/copy/CSS-token/responsive or keyboard-behavior redesign.
- No broad test rewrite. Existing page-level tests remain the behavioral contract.

## Required Verification

Keep the public `Requests.tsx` entry point and these contracts passing:

- `Requests.onboarding.test.tsx`: role-aware heading/setup and onboarding.
- `Requests.sections.test.tsx`: server-order-preserving All-work split.
- `Requests.queueTransition.test.tsx`: skeleton/cache, tabs, activation, clear search.
- `Requests.pagination.test.tsx`: range, pager/end state, scroll, deferred focus.
- `Requests.history.test.tsx`: history role gate, scopes, date params, context retention, and
  server-owned history presentation.
- Existing `RequestRow` tests: row-card semantics and actions.

Run the full frontend suite, `tsc --noEmit`, CSS-token guard, and `git diff --check`. Use the normal
local workflow to compare narrow and wide page layouts if available; expect no intentional visual
difference.

## Mechanical Order

1. Move only pure configuration/types/helpers into `requestsWorkspace.ts`; prove no import cycle.
2. Extract the static skeleton and list-content rendering, leaving query/effect logic in controller.
3. Extract navigation, toolbar, then header using explicit callbacks.
4. Remove relocated markup only; do not rename or consolidate state opportunistically.
5. Run focused Request List tests after each seam, then all required checks.

If a named boundary is awkward in current code, preserve these ownership rules and report the
minimal alternative in preflight. Do not infer approval for a new architecture or behavior change.

## Implementation Result

Implemented exactly as locked. `Requests.tsx` is now 432 lines (from 912) and remains the sole
controller for state, queries, mutations, navigation, and page-transition focus/scroll policy.
`requestsWorkspace.ts` contains only pure types/configuration/helpers; `buildSummaryPills` and its
icon JSX correctly remain in `RequestsWorkspaceHeader`. The five other extracted components own the
static skeleton, list content/pager, queue/history navigation, and toolbar presentation.

No API, query-key, cache, routing, permission, visual, or interaction behavior changed. The batch
contains seven production files, within the eight-file production and twelve-file total gates.
Verification: frontend suite 176/176, `tsc --noEmit`, CSS-token guard, and `git diff --check` pass.
