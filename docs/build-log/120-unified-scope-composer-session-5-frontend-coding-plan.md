# Build Log 120 — Unified Scope Composer Session 5 Frontend Coding Plan

**Status:** Approved handoff — ready for sequential implementation  
**Date:** 2026-08-16  
**Scope:** Replace the ADR-461 five-rung proposed-scope ladder in the web/PWA request detail with
the unified, phone-first composer locked by ADR-482 through ADR-485.  
**Prerequisites complete:** Quick scope-action configuration/field read; default-only assembly
expansion; empty-Draft submit enforcement; versioned remove/restore endpoint.  
**Required before production completion:** the bounded cleanup job for expired removed-line
snapshots remains a separate maintenance follow-up from Build 119. It does not block composer UI
implementation, but it blocks declaring Undo production-complete.

## Non-goals

- No inline scope construction from the request list.
- No request-list redesign, general Request Details redesign, native-mobile feature, offline queue,
  automatic replay, catalog-management changes, pricing visibility, quote workflow, or new backend
  business rules.
- A future list `Build scope` shortcut may open this same composer only after pilot evidence and a
  dedicated decision. It must never become an inline list-row editor.

## Shared implementation rules

- Consume only price-free field reads. Field users must never call or receive the Owner/Admin
  Quick-action configuration response in the composer.
- Every successful mutation replaces local scope state with an authoritative scope read. Do not
  optimistically append, remove, restore, or merge lines.
- A failed custom-item add keeps the entered description, quantity, note, and validation state;
  clear only after confirmed server success.
- A `409` closes any Undo toast, refreshes the scope, and says the scope changed and could not be
  restored. It never retries automatically. Server expiry has distinct "Undo period ended" copy.
- Repeated catalog-item or assembly selections render as separate, stacked Draft rows. Never
  aggregate their quantities visually or in local state.
- Phone composer uses a fixed `100dvh` container, fixed header/footer, and internal
  `flex-1 overflow-y-auto` body. The page behind it must not become the active scroller.

## Sequential Claude coding sessions

### Session 5A — Client-contract wiring and capture state

**Goal:** make the locked server contract consumable without changing the current ladder UI.

Implement:

1. Add typed web API-client methods/types for `GET /keep/pricebook/field/quick-scope-actions` and
   `POST /keep/pricebook/proposed-scopes/{scopeId}/lines/{lineId}/restore`, including the existing
   `X-Keep-ProposedScope-Version` contract.
2. Extend the proposed-scope capture hook with authoritative reload helpers and a single,
   reusable conflict/reconciliation path. Keep its create/resume/read-only semantics.
3. Add API-client/hook tests for successful reload, 409 refresh/no-retry behavior, and the
   field-read's price-blind shape.

Do not render Quick actions or alter the existing modal/rungs yet.

**Exit gate:** typecheck and focused tests pass; no price field is added to field-facing types; no
ladder behavior changes.

**Session 5A: complete (2026-08-16, commit `f5bbcb3`).** Added `getFieldQuickScopeActions` and
`restoreProposedScopeLine` to the typed web API client (`apiClient.ts`/`apiClient.types.ts`),
including the `X-Keep-ProposedScope-Version` header on restore and the price-blind field-read
shape. Extended `useProposedScopeCapture` with `conflictNotice`/`reconcileAfterConflict`/
`clearConflictNotice` — the single reusable 409/ambiguous-failure reload-and-notice path future
composer slices will call instead of duplicating it per surface; existing create/resume/read-only
semantics and `refetchScope` are unchanged. The ladder and `ProposedScopeCaptureModal` are
untouched. 4 files changed (2 production, 1 hook, 1 test); 13/13
`useProposedScopeCapture.test.ts` (including new apiClient-contract cases), `tsc --noEmit` clean,
`git diff --check` clean.

### Session 5B — Unified composer shell, search, and explicit custom add

**Goal:** create the replacement surface alongside the existing ladder, without deleting legacy
components yet.

Implement:

1. Introduce a focused `ProposedScopeComposer` with component boundaries for the composer shell,
   unified add surface, live Draft list, and sticky submit footer.
2. Use one deterministic Name/SKU/Alias search input. Render catalog results and the explicit
   `Add “…” as custom item` action from the same entry surface; typing alone never writes a line.
3. Add known catalog and custom-item mutations using the existing field-select endpoint. Preserve
   custom values through all non-success outcomes; after success, reload then clear the custom
   inputs.
4. Use a full-screen fixed phone presentation and constrained desktop dialog presentation. Focus
   the unified input only after the writable dialog is available.

The current ladder remains reachable only as temporary legacy code while this session is reviewed;
the new composer must not include steps, `Not here`, categories, or off-catalog as a separate rung.

**Exit gate:** a new empty Draft supports known-item search/add and explicit custom add; no
price/cost/margin UI or payload is present; phone keyboard/opening behavior is manually checked.

**Session 5B: complete (2026-08-16).** New `ProposedScopeComposer`/`ComposerSearchAndAdd`/
`ComposerDraftList` alongside the untouched five-rung ladder — `ProposedScopeCaptureModal` and its
rungs are not imported or modified, and no wiring was added to `RequestDetailContent`. One
deterministic Name/SKU/Alias input renders catalog results and an explicit `Add "…" as custom item`
action together; typing never writes a line; a known or custom pick goes through
`fieldSelectProposedScopeLine` and clears only after confirmed success, preserving entered
description/quantity/note on any failure or conflict. Phone gets a fixed `100dvh` full-screen
presentation, desktop a constrained centered dialog, via `KeepModal`. `KeepModal` gained an optional
`initialFocus` ref prop (backward-compatible; existing callers unaffected) so the unified input, not
the close button, receives initial focus once the dialog is available. The sticky
`Submit scope to office` footer is a disabled structural placeholder — wiring is Session 5D. No
price/cost/margin UI or payload. 5 files changed (4 new, 1 modified — `KeepModal.tsx`); 6 new
`ProposedScopeComposer` component tests plus the existing 6 ladder tests and 11 `KeepModal` tests
all pass unchanged (15/15 focused run), `tsc --noEmit` and `git diff --check` clean. Manual phone/
desktop keyboard-and-presentation check still outstanding — no live device/browser check performed
this session.

### Session 5C — Quick actions, assembly expansion, and visible Draft

**Goal:** complete the three coequal first-screen addition paths and make the current scope clear.

Implement:

1. Render the ordered field Quick actions (zero to six) in the unified composer; handle an empty
   set without substituting all catalog items or assemblies.
2. Dispatch Common Items through field-select and assemblies through the existing default-only
   expand-assembly endpoint. Do not recreate the optional-item chooser.
3. Render the authoritative Draft as separate stacked rows, including duplicate selections and
   assembly-expanded default lines. Show quantity, unit, note summary, source context where
   available, and stable pending states.
4. Map target-unavailable responses to the locked office-updated/unavailable notice followed by
   authoritative reconciliation.

**Exit gate:** the ADR-484 assembly-plus-delta and clean-slate journeys pass at phone width; double
selection visibly produces two rows; no client-side merge or inferred Quick actions exist.

**Session 5C: complete (commit `d5b08ee`, review fixes included).** `ComposerQuickActions` renders
the zero-to-six ordered field Quick actions and dispatches Common Items through field-select and
assemblies through the default-only expand-assembly endpoint, with `ExpandAssemblyNotOperationallyEligible`
and `LineCatalogItemNotFound` checked ahead of generic status handling so a real 409 isn't misrouted
to the target-unavailable notice. `ComposerDraftList` renders the authoritative Draft as separate
stacked rows keyed by line id — no client-side merge. `reconcileAfterConflict` distinguishes a failed
authoritative reload from a successful one (`PROPOSED_SCOPE_RECONCILE_RELOAD_FAILURE_NOTICE`,
`retryReconciliation`) instead of claiming success when the reload itself fails. Review fixes also
added sr-only labeling for the unified search input and field-associated `aria-invalid`/`aria-describedby`
validation errors. 8 files, 515 lines changed, full focused suite green.

### Session 5D — Line editing, delete/Undo, submit, and recovery

**Goal:** finish the Draft workbench and its failure behavior.

Implement:

1. Add touch-safe inline line editing for positive decimal quantity, note, and existing permitted
   exception state. Keep the server as validation authority.
2. Delete only after confirmed server success, then present the five-second Undo toast with the
   returned version. Restore uses the versioned restore endpoint; 409 and expiry follow the shared
   rules above.
3. Implement the sticky `Submit scope to office` footer. It is disabled/explained for an empty
   Draft and renders the locked submitted outcome after success.
4. Make failures accessible and actionable: preserve safe inputs, retain/recover focus, refresh on
   ambiguous network failures/conflicts, and never auto-retry or queue a mutation.

**Exit gate:** ADR-484 direct-custom, removal/Undo, decimal-quantity, connection-interruption, and
concurrent-change journeys pass. Verify keyboard-open behavior in iOS Safari and Android Chrome.

**Session 5D: complete (2026-08-16).** `ComposerDraftList` gained inline per-line edit (quantity,
note, and — only for an `AssociatedItem` line — the `isException` toggle, since
`ProposedScope.LineIsExceptionOnlyForAssociatedItem` makes that field illegal on every other line
type) and Remove, both dispatched through the existing `PATCH`/`DELETE .../lines/{lineId}` endpoints
with the server as sole validation authority; edit/remove/Quick actions/search are hidden once the
scope is read-only (submitted or non-Draft). A new `ComposerUndoToast` presents the five-second
versioned Undo after a confirmed delete, restoring via the version the delete response itself
returned — never the scope's pre-delete `concurrencyVersion` — since an intervening edit or Quick
action could otherwise make Undo silently reuse a stale version; a dedicated test asserts the restore
call uses the delete-response version, not the composer's pre-delete one. `RestoreExpired`/
`RestoreLineAlreadyExists` map to a distinct "can no longer be undone" notice through the shared
`onConflict` reconciliation path rather than a generic conflict message. The sticky
`Submit scope to office` footer is wired to the existing `submit` endpoint: disabled with an inline
explanation for an empty Draft, and replaced by the locked `Submitted to office — awaiting review`
outcome text on success (ADR-485's exact field-outcome wording). No wiring into `RequestDetailContent`
(still Session 5E scope). 4 files (1 new — `ComposerUndoToast.tsx`; 3 modified), 23/23 focused
`ProposedScopeComposer` tests (14 new + 9 existing), 162/162 across the full `request-detail`/`keep`
focused suite, `tsc --noEmit` and `git diff --check` clean. Manual iOS Safari/Android Chrome
keyboard-open check not yet performed — carried forward as an outstanding manual-verification item
alongside 5B/5C's.

**Session 5D review fixes: complete (2026-08-16).** `ComposerDraftList`'s quantity input was marking
itself `aria-invalid` for every edit failure, not just `LineQuantityMustBePositive` — the same issue
already avoided in `ComposerSearchAndAdd`. Fixed with a dedicated `quantityInvalid` flag separate
from the general error message, so generic failures still announce via `role="alert"` without
mismarking the field. Test fixtures corrected to the real `ErrorHttpMapper` contract:
`RestoreExpired` is 422, not 409; added the companion `RestoreLineAlreadyExists` (409) case proving
both map to the same "can no longer be undone" notice. Added a `vi.useFakeTimers({ shouldAdvanceTime:
true })` test proving the Undo toast disappears after five seconds with no restore call issued.

### Session 5E — Primary action placement, legacy removal, and release gate

**Goal:** make the replacement the only field workflow and finish safely.

Implement:

1. Move `Capture scope` / `Resume draft` / `View scope` to the agreed primary request-work area
   directly below the request header across desktop and PWA layouts. This is the only request-detail
   placement change authorized in Session 5.
2. Preserve submitted/reviewed read-only scope viewing and existing authorization-hidden behavior.
3. Remove `ProposedScopeCaptureModal`'s five rung components, step text, `Not here` progression,
   category browsing, optional-item pre-expansion path, stale API-client comments, and their
   superseded tests only after replacement coverage is green.
4. Run full frontend tests/typecheck and relevant backend/integration tests. Update session log and
   decision references with actual test evidence. Confirm the snapshot-cleanup maintenance work is
   tracked as a release dependency.

**Exit gate:** no reachable ladder UI/code path remains; all seven ADR-484 journeys and ADR-485
touch/recovery rules are verified; no scope entry is added to the request list or native app.

## Claude start prompt

```text
Implement only the next named Session 5 slice in Build Log 120. Read ADR-482, ADR-483, ADR-484,
ADR-485, Build Log 119, this plan, and the current proposed-scope web code before editing.

Do not broaden the work into request-list entry, native mobile, offline support, catalog management,
pricing, quotes, or a general request-detail redesign. Preserve price blindness, the ADR-480 gates,
server-authoritative versions, immutable submitted scopes, and the existing row-visibility policy.

Keep the slice bounded. Add focused tests, run the relevant checks, self-review the diff, and report
any prerequisite or contract inconsistency rather than silently inventing a client-only workaround.
```
