# Build Log 094 — Request List Filter-State Recovery Handoff

**Status:** Complete
**Date:** 2026-07-27
**Scope:** Session 3.3 / GAP-046 — frontend-only
**Controlling work to preserve:** ADR-447; Sessions 3.1, 3.2, and 3.2a

## Purpose

Make the submitted Request List criteria and the resulting state understandable and recoverable
without adding backend work, a fabricated total count, permanent visual noise, or per-keystroke
searching. The current page deliberately separates the typed search draft (`draftQ`) from the
submitted query (`q`); the UI must never imply that an unsubmitted draft is already filtering rows.

## Locked Decisions

### 1. Conditional criteria visibility

Render one quiet, informational applied-criteria line directly below the existing toolbar only when:

- an active operational query has a non-empty submitted `q` or non-empty `statusFilter`;
- history has a non-empty submitted `q`, or a non-default history scope/date scope; or
- `draftQ !== q`, so the visible input and submitted result set cannot be confused.

The line reports only **submitted** criteria, never the unsubmitted draft. It has no action button.
Use this copy pattern, omitting absent parts:

```text
Applied: Search “{q}” · Status: {selected status label}
Applied: Search “{q}” · {history scope label} · {history date label}
Applied: All active statuses
Applied: All history · All time
```

For the draft/submitted mismatch with no submitted query or status, use `Applied: All active
statuses` (or `Applied: All history · {history date label}` in history). This makes the actual result
set clear without treating the draft as applied. Do not render this line in an untouched default
operational queue or default All-history/All-time view.

### 2. Criteria-aware empty state

Keep `EMPTY_STATE` and `HISTORY_EMPTY_STATE` as the unfiltered/default tables. In `Requests.tsx`,
derive the dynamic filtered empty state from the submitted query parameters; do not duplicate every
search/status/date combination in those tables.

- An operational list with submitted `q` or `statusFilter` and zero rows uses heading
  `No matching requests`; its detail names the submitted search and/or selected status label.
- A history list with submitted `q` and/or a non-default scope/date criterion and zero rows uses
  heading `No matching history`; its detail names the submitted search plus the selected history
  scope and date label that apply.
- An unfiltered/default zero-row state keeps its existing queue/history copy exactly.
- A merely unsubmitted draft (`draftQ !== q`) does **not** turn a true empty queue into a filtered
  empty state, because it is not part of the executed request.

### 3. One existing polite announcement

Do not add a second live region. Extend the existing `role="region" aria-live="polite"` meaningful
heading in `RequestListContent` so it carries the same submitted criteria as the visible applied
line whenever result/empty state changes.

Use truthful cursor language only: `Showing 1–50 …`, never `of N` or a fabricated total. For
example, an announced range may be `Showing 1–50 results for “smith” · Status: Active`; an empty
heading may be `No matching requests for “smith” · Status: Active`. Preserve the current loading
announcement and do not announce an unsubmitted draft as a filter.

### 4. Clearing and recovery

Keep normal controls independently clearable:

- GAP-026's input X clears search only.
- The status `<select>` returns independently to `All active statuses`.

Only a criteria-aware empty state renders a visible `Clear filters` button. It is the recovery path,
not part of the applied-criteria line.

- In an operational view, it resets `q`, `draftQ`, `statusFilter`, `cursor`, and `cursorStack`.
- In history, it resets `q`, `draftQ`, `cursor`, and `cursorStack`, but preserves the selected history
  scope and date scope. Status remains irrelevant/hidden in history.

Focus the search input after this recovery action, matching the existing GAP-026 clear behavior.

### 5. History-specific criteria

History never shows or announces an active status filter. Its criteria wording uses the submitted
search plus the selected history scope and date scope. The existing History chrome, selected scope,
and selected date range remain in place after a history recovery action.

## Implementation Boundary

Expected production changes are limited to:

1. `web/ophalo-app/src/pages/Requests.tsx` — derive submitted-criteria display/announcement text,
   filtered-empty state, and the controller-owned combined-clear callback.
2. `web/ophalo-app/src/components/requests/RequestListToolbar.tsx` — render the conditional
   informational applied-criteria line from explicit controller props.
3. `web/ophalo-app/src/components/requests/RequestListContent.tsx` — render criteria-aware empty
   copy and the recovery action from explicit controller props; retain the one existing live region.

`requestsWorkspace.ts` may provide a small pure status-label helper only if that avoids duplicating
the existing option mapping; do not put JSX, React state, query logic, or dynamic empty-copy tables
there. No backend, API-client, DTO, mock, or route changes are authorized.

## Required Tests

Add a focused PWA test file (for example `Requests.filterState.test.tsx`) covering:

1. no criteria line in the untouched default queue; submitted search/status criteria appear with
   their human labels;
2. a changed-but-unsubmitted draft does not replace the submitted criteria in the line or live
   heading;
3. operational filtered-empty copy differs from the existing true-empty copy, and `Clear filters`
   clears search + status, resets paging, and returns focus to search;
4. history wording includes search/scope/date but no status; its recovery preserves scope/date and
   clears only search/paging;
5. result and empty announcements include criteria but retain truthful cursor ranges and no total.

Keep all existing Request List tests passing. Run the full frontend suite, `tsc --noEmit`, the
CSS-token guard, and `git diff --check`.

## Non-Goals

- No debounce, cancellation redesign, or request per keystroke.
- No global/permanent summary chip, second live region, exact total, or client-side result count.
- No changes to query keys, cursor fingerprint binding, polling, errors, tab/history resets, route
  state, authorization, or backend list behavior.
- No GAP-027 lifecycle work.

## Implementation Result

Implemented as locked in four production files plus one new focused test file. The controller derives
submitted-criteria text/suffixes, criteria-aware empty state, and recovery reset policy;
`RequestListToolbar` renders the conditional informational line; `RequestListContent` renders the
filtered-empty recovery action; and `requestsWorkspace.ts` provides the pure status-label lookup.

`Requests.filterState.test.tsx` has nine focused cases, including a nonempty filtered range, status
reset during operational recovery, and preservation of a non-default history date during history
recovery. Verification: full frontend suite 185/185, `tsc --noEmit`, CSS-token guard, and
`git diff --check` pass.
