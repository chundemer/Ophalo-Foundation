# BL150 — GAP-073: Request-detail composer draft safety

**Status:** Slice 073-1 landed (`3599ee3c`). Slice 073-2 (dead-prop cleanup) landed 2026-09-10,
awaiting diff review — GAP-073 code-complete. Decisions locked in
[ADR-502](../decisions/ADR-502-request-detail-composer-draft-safety.md).

**Scope reference:** [workboard](../workboard.md) Next item 3 (AUDIT-V4-A); audit
[production-readiness audit](../audits/production-readiness-audit.md) Vector 4 F4.1–F4.2,
Vector 5 F5.2–F5.3. **Supervised-pilot gate** — before staff send customer updates in the pilot.

## Problem

Confirmed in code (`web/ophalo-app`):

1. **Wrong-customer send.** `RequestDetail.tsx:609-610` lifts `businessUpdateDraft` /
   `businessUpdateDraftStatus`; `UnifiedComposer.tsx:85` holds `note` locally. `RequestDetail` is
   mounted without `key={requestId}` at `RequestWorkbenchShell.tsx:261` (pane) and `:274` (narrow);
   Prev/Next (`App.tsx:563`, `RequestWorkbenchShell.tsx:267/280`) swaps the hash route without
   remounting. Both drafts therefore render in the next request's composer. "Post update & prepare
   SMS" (`BusinessSection.tsx`) then sends A's text to B's customer.
2. **Instructed data loss.** On `409`, `BusinessSection.tsx:537` sets the textarea
   `disabled={conflictDisabled}` and `UnifiedComposer.tsx:230` does the same for the note; the
   banner says "Refresh to see the latest state. Your message is saved here." The draft is
   React-state only — no persistence, and `rg beforeunload` over `ophalo-app` = 0 hits. Cmd-R
   destroys it; `disabled` blocks selecting the text to copy out.

## Locked decisions (ADR-502)

| # | Resolution |
|---|------------|
| D1 | **One shared per-`requestId` external store** holding `{message, status, note}` in `sessionStorage` key `keep:composer-draft:{requestId}`, read via `useSyncExternalStore` behind a `useComposerDraft(requestId)` hook, consumed by `BusinessUpdateSection` and `UnifiedComposer`. **Not two independent stateful hook instances** — those would clobber each other's newer field on write/clear (stale-object last-write-wins). Store mutations are `setField` / `clearFields`; the store re-serializes its own current state synchronously. Existing 4-level prop-drill removed; internal-note text moves onto the store. **No `key={requestId}` on `RequestDetail`** — isolate the draft, not the whole subtree. |
| D2 | Clear **only the successfully submitted field** on `2xx` (message+status on post; note on note-save). No TTL, no other auto-clear. Conflict / validation / network errors never clear. |
| D3 | `409` → textareas + status select go **`readOnly`** (select: `aria-disabled` + ignored `onChange`), submit stays `disabled`. Conflict flag resets when `detail.version` advances. |
| D4 | **Shared unload dirty registry** (module `Set` + one lazy **`beforeunload`** listener calling `preventDefault()`/`returnValue`) + `useUnloadGuard(id, isDirty)` hook. **`beforeunload` only** — `pagehide`/`visibilitychange` cannot raise the leave prompt and a crash runs no handler; the store already persists synchronously so there is no flush for `pagehide` to do. **Wired to the two composers only.** No SPA Prev/Next interstitial (per-request drafts make it unnecessary). Existing sheet guards not migrated — fast-follow. |
| D5 | Short ADR locking the above; explicitly display-safety, not mutation queueing — inside ADR-403 / ADR-484. **Done: ADR-502.** |

Out of scope (stays GAP-085): resetting the ~10 non-composer `conflictDisabled` /
`noteConflictDisabled` / `priorityConflictDisabled` flags that are set and never reset
(audit F4.9 / F4.10 / F4.16), and the accessible-announcement gap on write failures (F4.10).

## Implementation preflight (mechanical — to run at the start of the coding session)

Confirm these symbols still exist and enumerate compile-impact callers before the file gate:

- `web/ophalo-app/src/pages/RequestDetail.tsx` — `businessUpdateDraft`, `businessUpdateDraftStatus`
  state + the props passed at `:837-840`.
- `web/ophalo-app/src/pages/request-detail/RequestDetailContent.tsx` — `customerUpdateDraft` prop
  thread (`:31-33`, `:380-383`).
- `web/ophalo-app/src/pages/request-detail/RequestDetailWorkCanvas.tsx` — same prop thread to
  `UnifiedComposer`.
- `web/ophalo-app/src/pages/request-detail/UnifiedComposer.tsx` — `customerUpdateDraft` /
  `onCustomerUpdateDraftChange` / `customerUpdateDraftStatus` / `onCustomerUpdateDraftStatusChange`
  props (`:18-21`, `:46-49`); local `note` / `noteSubmitting` / `noteConflictDisabled` (`:85-88`);
  `submitNote` (`:92`); `nHandle` / `activateCustomerUpdate` imperative handle.
- `web/ophalo-app/src/pages/request-detail/BusinessSection.tsx` — `BusinessUpdateSection` props
  `draft` / `onDraftChange` / `draftStatus` / `onDraftStatusChange` (`:348-352`); `message` /
  `setMessage` aliases (`:370-373`); `conflictDisabled` (`:375`); `BUSINESS_UPDATE_CONFLICT_MESSAGE`
  (`:341`); textarea `disabled` at `:537`, select at `:567`.
- Existing storage precedent: `RequestDetailContent.tsx:64/116`, `RequestMemoryRail.tsx:29/58`
  (sessionStorage try/guard style), `hooks/useUpdatesFeed.ts:92-124` (localStorage hook style).
- Tests to update: `UnifiedComposer.activateCustomerUpdate.test.tsx`, `BusinessSection.notify.test.tsx`.

## Anticipated file gate (to be re-stated at preflight)

Production (`web/ophalo-app/src`):

1. **New** `hooks/useComposerDraft.ts` (+ its store module, may be the same file) — one shared
   per-`requestId` store of `{message, status, note}`, `sessionStorage`-backed, `try/catch`
   guarded, `useSyncExternalStore` read, `setField` / `clearFields` writes that re-serialize the
   store's own current state (no whole-object writes from stale component copies).
2. **New** `lib/unloadGuard.ts` (or `hooks/useUnloadGuard.ts`) — module dirty registry + single
   lazy **`beforeunload`** listener (`preventDefault()`/`returnValue`) + `useUnloadGuard(id, isDirty)`.
   No `pagehide`/`visibilitychange`.
3. **Modified** `pages/request-detail/UnifiedComposer.tsx` — consume `useComposerDraft` for `note`;
   drop the drilled customer-update props; `readOnly` on note conflict; `useUnloadGuard`.
4. **Modified** `pages/request-detail/BusinessSection.tsx` — `BusinessUpdateSection` consumes
   `useComposerDraft` for message/status; `readOnly` (not `disabled`) on conflict; version-advance
   reset; `useUnloadGuard`; clear-on-success.
5. **Modified** `pages/request-detail/RequestDetailWorkCanvas.tsx` — drop the customer-update draft
   prop pass-through.
6. **Modified** `pages/request-detail/RequestDetailContent.tsx` — drop the customer-update draft
   prop pass-through.
7. **Modified** `pages/RequestDetail.tsx` — remove `businessUpdateDraft` /
   `businessUpdateDraftStatus` state and the props.

Tests: `__tests__/useComposerDraft.test.ts` (new) — including a **concurrent-consumer test**
(one composer sets `note`, the other then sets `message`; both fields survive) and a
field-scoped clear-on-success test; `__tests__/unloadGuard.test.ts` (new) — `beforeunload`
`preventDefault` fires only while an id is registered dirty; updates to
`UnifiedComposer.activateCustomerUpdate.test.tsx` and `BusinessSection.notify.test.tsx`;
a per-request draft-isolation regression test (draft on A not visible on B, still there on return);
a `readOnly`-on-409 test (text selectable, submit disabled, resets on version advance).

Estimate: 7 production files + ~4–6 test files — one batch under the 8-production / 12-total gate.
If preflight shows the prop-thread touches more intermediate components than listed, split the
prop-drill removal (files 5–7) from the behavior change (files 1–4, 3, 4).

## Verification plan

- `tsc --noEmit`; `check:tokens`.
- Focused suites: `src/pages/request-detail`, `src/hooks`, plus the two named composer tests.
- Browser verification (Christian): draft typed on request A is gone from request B's composer on
  Next and still present on return to A; 409 leaves the text selectable; Cmd-R with a dirty
  composer prompts the browser warning and the text is restored after reload; successful post
  clears the message but a half-written note survives.

## Slice 073-1 — delivered (pending review)

Frontend only (`web/ophalo-app`), no API/domain/migration. Exactly the 9-file preflight gate.

- **New** `src/hooks/useComposerDraft.ts` — one shared per-`requestId` external store of
  `{message, status, note}`, `sessionStorage`-backed (`keep:composer-draft:{requestId}`), read via
  `useSyncExternalStore`. `setField` / `clearFields` re-serialize the store's own current state, so
  the two consumers never clobber each other's newer field. All storage access `try/catch`-guarded;
  the entry is removed when every field is empty. Test-only `__resetComposerDrafts` / `seedComposerDraft`.
- **New** `src/lib/unloadGuard.ts` — module dirty-id `Set` + one lazily-attached `beforeunload`
  listener (`preventDefault()` + `returnValue`); `useUnloadGuard(id, isDirty)` registers/deregisters.
  No `pagehide`/`visibilitychange`. Test-only `__unloadGuardState` / `__resetUnloadGuard`.
- **Modified** `src/pages/request-detail/BusinessSection.tsx` — `BusinessUpdateSection` drops the
  `draft`/`onDraftChange`/`draftStatus`/`onDraftStatusChange` props, reads message + status from
  `useComposerDraft`; 409 sets the message textarea `readOnly` (was `disabled`) and the status
  `<select>` `aria-disabled` + ignored `onChange`; new `useEffect([detail.version])` clears the
  conflict flag + conflict message on version advance; success path `clearDraftFields(["message","status"])`;
  `useUnloadGuard(\`composer-update:${requestId}\`, hasText)`.
- **Modified** `src/pages/request-detail/UnifiedComposer.tsx` — internal-note text from
  `useComposerDraft` (local `useState` removed); 409 → note textarea `readOnly`; per Christian's
  clarification the same `useEffect([detail.version])` conflict reset is on the **note path too**,
  not only BusinessSection; success `clearDraftFields(["note"])`; `useUnloadGuard(\`composer-note:${requestId}\`, hasText)`.
  Drilled `customerUpdateDraft*` props made **optional and unused** (deleted in 073-2); no longer
  passed down to `BusinessUpdateSection`.
- **Modified** `src/pages/request-detail/__tests__/BusinessSection.notify.test.tsx` — 4 render sites
  drop the `draft*` props and `seedComposerDraft("req-77", …)` before render; `beforeEach` resets
  the store + unload guard + `sessionStorage`.
- **Modified** `src/pages/request-detail/__tests__/UnifiedComposer.activateCustomerUpdate.test.tsx` —
  drilled props removed from the render helper; `beforeEach` resets; **+1 test**: the internal-note
  draft persists to `sessionStorage` and is restored on a fresh remount.
- **New** `src/hooks/__tests__/useComposerDraft.test.ts` — 5 tests incl. the concurrent-consumer
  no-clobber test, per-request isolation, field-scoped `clearFields`, storage removal when empty.
- **New** `src/lib/__tests__/unloadGuard.test.ts` — 6 tests: no prompt when clean, prompt while
  dirty, stops on clean, deregister on unmount, still prompts while any one guard is dirty, single listener.
- **New** `src/pages/request-detail/__tests__/ComposerDraftSafety.test.tsx` — 3 tests: A→B→A draft
  isolation; **both** composer modes proven for 409 → `readOnly` (not disabled) + text kept + submit
  blocked + recovery on `detail.version` advance. The customer-update case also asserts D3's
  select-specific rule: on 409 the status `<select>` carries `aria-disabled="true"` (not `disabled`),
  a selection attempt leaves its drafted value unchanged, and it recovers (no `aria-disabled`, value
  intact, selectable) after the version advance.

Verification: `tsc --noEmit` clean; `check:tokens` pass; `git diff --check` clean. Focused suites
(`src/pages/request-detail`, `src/hooks`, `src/lib`, `src/components/requests`) 637/637; full
`ophalo-app` suite **1184/1184** (1169 + 15 new). No eslint step exists for `ophalo-app`
(`tsc` + `check:tokens` + `vitest` are the gates). **Browser verification completed 2026-09-10
(Christian)** — mock workbench for draft isolation on Prev/Next, `beforeunload` prompt + restore on
Cmd-R, and field-scoped clear-on-success; real API (two-tab stale-version conflict) for the 409
`readOnly`/copy-out/version-advance recovery on both composer paths.

## Slice 073-2 — delivered (pending review)

Deletion-only removal of the now-dead customer-update draft prop chain that 073-1 orphaned. No
behavior change; the props were already unused and made optional in 073-1.

Production (`web/ophalo-app/src`):

- `pages/RequestDetail.tsx` — removed `businessUpdateDraft` / `businessUpdateDraftStatus` `useState`
  and the 4 props passed to `RequestDetailContent`.
- `pages/request-detail/RequestDetailContent.tsx` — removed the 4 prop-type lines and the 4
  pass-throughs to `RequestDetailWorkCanvas`.
- `pages/request-detail/RequestDetailWorkCanvas.tsx` — removed the 4 prop-type lines, 4 destructures,
  and the 4 attributes on `<UnifiedComposer>`.
- `pages/request-detail/UnifiedComposer.tsx` — removed the 4 optional `customerUpdateDraft*`
  prop-type lines (and the now-obsolete GAP-073 comment).

Tests (dead props dropped from render helpers / mock prop objects only, no assertion changes):
`RequestDetailWorkCanvas.test.tsx`, `RequestDetailContent.canvasFrame.test.tsx`,
`RequestDetailContent.responsiveShrink.test.tsx`, `RequestDetailContent.pendingReviewsRefresh.test.tsx`,
`RequestDetailContent.actualWorkHistoryRefresh.test.tsx`,
`RequestDetailContent.actualWorkWorkspaceRoute.test.tsx`.

Drift from the anticipated gate: **4 prod + 6 test files** (brief anticipated 7 test) —
`UnifiedComposer.activateCustomerUpdate.test.tsx` was already de-propped in 073-1, so it drops off.
10 total changed files, within the 8-prod / 12-total gate.

Verification: `tsc --noEmit` clean; `check:tokens` pass; `git diff --check` clean; full `ophalo-app`
suite **1184/1184** (unchanged — no tests added or removed).
