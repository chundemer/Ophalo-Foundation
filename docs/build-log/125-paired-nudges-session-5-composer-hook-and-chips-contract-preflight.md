# Build Log 125 — Paired Nudges Session 5: Composer Hook and Chips Contract Preflight

Locks the field composer wiring ahead of implementation. Build Log 123's bounded sequence step 5
("composer hook and chips") and Build Log 122's product rules (trigger scope, repeat/dismiss,
Draft dedupe, manual acceptance scenarios) describe the target behavior but not the exact hook/
component contract; this entry closes that gap. No other implementation contract or API shape
changes.

## Locked decisions (2026-08-16)

### 1. Trigger-carrying commit vs. plain commit

`onCommitted: () => void` today is a bare callback shared by every composer mutation
(`ComposerQuickActions`, `ComposerSearchAndAdd`'s catalog-select/custom-add/assembly-expand,
`ComposerDraftList`'s edit/remove, `ComposerUndoToast`'s restore, and submit). It is replaced by an
optional-trigger form so `useProposedScopeCapture` can tell which commits are eligible to fire a
nudge read:

- `ComposerQuickActions`' dispatch success and `ComposerSearchAndAdd`'s `expandMutation` success
  pass the trigger (`{ catalogItemId }` or `{ offeringAssemblyId }`) they already have in scope.
- `ComposerSearchAndAdd`'s `addMutation` success passes the trigger only for `selection.kind ===
  "catalog"`; the off-catalog/custom path passes no trigger.
- `ComposerDraftList` (edit, remove), `ComposerUndoToast` (restore), and submit continue to call the
  plain no-trigger form. Only a successful catalog-item add or assembly expansion invokes the nudge
  read — never custom adds, edits, removes, undo, or a reload with no associated mutation.

### 2. Ordering: reload before nudge read

The existing authoritative Draft reload (`refetchScope`'s logic) always runs first and must
complete before the nudge read fires — matches Build Log 123's "after ... the authoritative Draft
reload completes." A reload failure short-circuits the same way it does today; no nudge read is
attempted.

### 3. Nudge state and single-panel replacement

`useProposedScopeCapture` gains one nudge state slot (at most one visible suggestion set):

```text
nudge: { ruleId: string; suggestions: ScopeNudgeSuggestionFieldRow[] } | null
```

- A trigger-carrying commit that completes its reload calls the nudge-read endpoint with that one
  trigger. A **non-empty** result replaces `nudge` with the new rule/suggestions, regardless of
  whether a different, still-unaddressed panel was showing — the newest successful trigger wins.
- An **empty** result (`RuleId: null` or no surviving suggestions) leaves the currently visible
  `nudge` state unchanged. It does not clear an existing panel and does not replace it.
- A plain no-trigger commit (edit, remove, undo, submit) does not touch `nudge` — an already-visible
  chip panel survives a Draft edit unless a later trigger-carrying commit replaces it or the
  technician explicitly accepts/dismisses it.
- A nudge-read request failure is silent: `nudge` is left unchanged, no error is surfaced to the
  technician (matches the existing "ineligible target fires nothing, no error" posture in Build Log
  122).

To make "newest successful trigger wins" true under overlapping asynchronous reload/read chains,
the hook maintains a monotonically increasing trigger/read generation. A nudge-read result may
update state only when its generation is still current and the composer session is still open; an
older response that arrives later is discarded. `closeModal` invalidates the current generation in
addition to clearing visible nudge state, so a request that completes after close cannot revive a
panel in the next session.

### 4. Retirement is explicit only

A rule's ID is added to the session-only `retiredRuleIds` set **only** when the technician accepts
one of its chips or explicitly dismisses the panel — never by being replaced or by an empty result.
A replaced-but-not-retired rule can still surface again later in the same session if its trigger
fires again and the nudge read returns a non-empty result. The nudge-read call itself still excludes
already-retired rules being newly displayed: if a trigger's rule is retired, its (non-empty) result
is not shown and `nudge` is left unchanged, same as an empty result.

An accepted or dismissed rule is retired for the rest of the open composer session and cannot surface
again from a later re-add of its trigger. Closing and reopening the composer starts a new session and
therefore permits that rule to fire again.

### 5. Accept / dismiss behavior

- **Accept** (tapping one suggestion chip): dispatches the existing `field-select` (catalog target)
  or `expand-assembly` (assembly target) mutation for that suggestion with default quantity 1 and no
  note/optional-item exclusions. Only after that mutation succeeds does it add `ruleId` to
  `retiredRuleIds`, clear `nudge`, and trigger a plain (no-trigger) reload so the new Draft line
  appears — matches Build Log 123: "invokes the existing field-select or expand-assembly mutation and
  retires the rule." Accepting one chip retires the whole rule; the remaining un-accepted suggestions
  from that firing are not offered again this session.
- **Dismiss** (single action for the whole panel, not per-chip): adds `ruleId` to `retiredRuleIds`
  and clears `nudge`. No API write.
- A conflict (409) during an accept mutation follows the existing `onConflict`/reconciliation path;
  `nudge` is cleared regardless (the Draft state it was computed against is now stale).
- For a non-409 accept failure, do not retire or clear the panel: preserve it and use the existing
  safe mutation-error/retry posture. While an accept mutation is pending, disable every chip and the
  Dismiss control so the same panel cannot be accepted twice or dismissed concurrently.

### 6. Session boundary

`retiredRuleIds` and any visible `nudge` state are cleared when the modal closes (`closeModal`), not
on hook unmount — the hook persists for the page's lifetime, but the composer "session" is one open
period, matching Build Log 122's "closing and reopening the composer (new session) allows a
previously dismissed rule to fire again."

### 7. Chip panel placement

One nudge-chip panel renders in `ProposedScopeComposer`, between the add controls
(`ComposerQuickActions`/`ComposerSearchAndAdd`) and the Draft list — not attached to a specific
Draft line. Price-blind: suggestion chips show only `DisplayName`, no price/pricing affordance.

### 8. New API surface (frontend only — no backend change)

`apiClient.types.ts` gains `ScopeNudgeSuggestionFieldRowResponse` and `ScopeNudgeFieldResultResponse`
mirroring Build Log 123's `ScopeNudgeFieldResult`/`ScopeNudgeSuggestionFieldRow`. `apiClient.ts`
gains `getScopeNudgeFieldSuggestions(proposedScopeId, { triggerCatalogItemId } |
{ triggerOfferingAssemblyId })` calling `GET
/keep/pricebook/proposed-scopes/{proposedScopeId}/nudge-suggestions`.

## Explicitly out of scope

No API, domain, or Owner/Admin UI changes — Session 2–4's contracts and screens are unchanged. No
offline/durable nudge state; the session-only in-memory model is final for V1 per Build Log 122.

## Required verification matrix

The implementation must prove the following focused behaviors in addition to proportionate existing
composer regression coverage:

1. A catalog-item trigger shows its price-blind suggestions after authoritative Draft reload.
2. An assembly trigger shows its price-blind suggestions after authoritative Draft reload.
3. An off-catalog/custom add, Draft edit, Draft removal, Undo restore, and reload without an eligible
   mutation never request nudges.
4. Accepting a catalog suggestion uses field-select; accepting an assembly suggestion uses
   expand-assembly; each retires and clears the complete rule only after success.
5. Dismiss retires with no write; close and reopen resets retirement; a retired rule does not surface
   again within the same open session.
6. Empty results and nudge-read failures leave an existing panel untouched; an ordinary accept failure
   also preserves it, while a 409 follows reconciliation and clears it.
7. A later non-empty trigger result replaces the visible panel, while a late result from an older
   trigger/read generation is ignored; closing the composer prevents a pending result from restoring
   a panel.
