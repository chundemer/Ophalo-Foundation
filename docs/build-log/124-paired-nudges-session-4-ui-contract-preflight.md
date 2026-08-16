# Build Log 124 — Paired Nudges Session 4: Owner/Admin Settings UI Contract Preflight

Locks the UI contract for the Price Book rule-management screen ahead of implementation. Build Log
123's bounded sequence step 4 ("Owner/Admin settings UI") named the API to use
(`ScopeNudgeRuleConfigApiService`/`ScopeNudgeRuleEndpoints`) but left the screen's layout and
interaction pattern unlocked. This entry closes that gap; no other implementation contract or API
shape changes.

## Locked decisions (2026-08-16)

### 1. Screen location

A new **Nudges** tab in `web/ophalo-app/src/pages/PriceBook.tsx`, alongside the existing Catalog
Items and Assemblies tabs. It uses the page's existing `isOwnerOrAdmin` role gate and Price Book
`entitled` gate — same boundary as the other two tabs, no separate route.

### 2. Tab content — rule list

The tab lists all account rules from `GET /keep/pricebook/scope-nudge-rules`, including rules with
an inactive/ineligible trigger or suggestion (per Build Log 123's `GET` contract — such targets are
never omitted). Each row shows:

- the trigger (`TriggerDisplayName`, marked visibly if `TriggerIsEligible` is false);
- the ordered suggestion list (`TargetDisplayName` per suggestion, each marked if `IsEligible` is
  false);
- a repair-needed indicator when the trigger or any suggestion is ineligible — this is visible
  admin-facing state, not silently dropped.

### 3. Create flow

An "Add nudge rule" action opens a modal:

1. Choose exactly one trigger (a catalog item or assembly picker — single selection, matching the
   domain's one-trigger invariant).
2. Add 1–3 ordered suggestions, with reordering support.
3. Submit calls `POST /keep/pricebook/scope-nudge-rules`. Server-side aggregate validation
   (`ScopeNudgeRule.Create`) remains authoritative; the modal does not duplicate eligibility or
   uniqueness checks client-side beyond basic required-field/count UX.

### 4. Edit flow

Editing an existing rule opens a modal scoped to suggestions only:

- the trigger is displayed but is not an editable control (visibly immutable — matches
  `PUT /keep/pricebook/scope-nudge-rules/{ruleId}` accepting no trigger fields);
- the suggestion list (1–3, ordered) can be changed and is submitted as a full atomic replacement
  via `PUT`.

### 5. Delete flow

Deleting a rule requires a confirmation step that identifies the trigger being removed (e.g. "Delete
the nudge rule for {TriggerDisplayName}?"). Confirmed delete calls
`DELETE /keep/pricebook/scope-nudge-rules/{ruleId}`.

### 6. No frontend-derived logic

The screen consumes Session 2's API responses as-is: no client-side eligibility computation, no
pricing display (the contract is price-free end to end), no client-side uniqueness/duplicate-trigger
enforcement beyond surfacing the server's `DuplicateTrigger` (409) and `TargetNotFound` (404) errors.

## Explicitly out of scope

Composer-side chips, retired-rule session state, and field nudge-read wiring belong to Session 5
(Build Log 123 step 5) and are not touched here. No API or domain changes; Session 2's
`ScopeNudgeRuleConfigApiService`/`ScopeNudgeRuleEndpoints` and response shapes are unchanged.
