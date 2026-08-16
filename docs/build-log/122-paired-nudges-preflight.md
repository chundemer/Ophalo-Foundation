# Build Log 122 — Paired Nudges: Phase 2 Preflight

**Status:** Product model locked; implementation contract completed in Build Log 123
**Date:** 2026-08-16
**Scope:** Define the Owner/Admin-curated trigger-to-suggestion model, timing, cap, eligibility,
persistence/API shape, price-blind technician UI, failure behavior, and manual field-acceptance
scenarios for Paired Nudges. Deferred by build-log/121 until Phase 1 (unified scope composer) was
committed and manually accepted.
**Related:** ADR-479 (read-time eligibility), ADR-483/485 (Quick scope actions, composer contract),
build-log/119 (Quick scope action persistence precedent), build-log/121

## Locked decisions

### 1. Trigger scope

A configured `ScopeNudgeRule` triggers from exactly one of: a catalog-item add to Draft, or an
assembly expansion. Same polymorphic-target pattern as `QuickScopeAction` — one rule, one trigger
target, discriminator by which optional reference is set. No compound or multi-condition triggers in
V1.

### 2. Persistence shape

Account-owned polymorphic parent/child pair, not a single denormalized row:

- `ScopeNudgeRule` — `AccountId`, exactly one of `TriggerCatalogItemId` / `TriggerOfferingAssemblyId`
  set (database check constraint, matching `QuickScopeAction`'s exclusive-presence pattern).
- `ScopeNudgeSuggestion` — child rows, 1–3 per rule, each with an `Order` (1–3) and exactly one of
  `SuggestedCatalogItemId` / `SuggestedOfferingAssemblyId` set. Set-level invariants (count bound,
  per-rule order uniqueness) enforced by a validator type analogous to
  `QuickScopeActionSetValidator`, not by the child entity alone.

Both target types (trigger and suggestion) carry no stored eligibility/lifecycle state — read-time
computed predicate only (ADR-479), consistent with `QuickScopeAction`.

### 3. Repeat/dismiss behavior

A rule fires at most once per open composer session. Once the technician accepts or dismisses a
rule's nudge, its rule ID is retired for the rest of that session in `useProposedScopeCapture`
client-side memory. No server persistence or write backs this — a fresh composer open (new session)
resets it. This is intentionally weaker than a durable "seen" record: it only needs to survive one
open Draft-editing session, not a reload or a different device.

### 4. Draft dedupe

Suppress a suggested catalog item when an active Draft line has the same `CatalogItemId`; suppress a
suggested assembly when an active Draft line has the same `OfferingAssemblyId`. Identity match only
— no equivalence inference from an assembly's expanded component lines. This is deliberately the
entire V1 dedupe rule; do not add "all components already present" logic.

## Implementation guardrails

- **No recursive rule evaluation on expansion.** An `expand-assembly` mutation evaluates only a
  matching `OfferingAssembly` trigger rule for the assembly itself. It does not turn around and
  evaluate catalog-item trigger rules for the assembly's newly created component Draft lines. One
  trigger evaluation per mutation, on the thing the technician directly acted on.
- **Client-side-only dismissal state.** Retired/dismissed rule IDs live only in
  `useProposedScopeCapture` session memory. No persistence, no API write, for temporary dismissal.
- **Timing and blocking behavior.** Suggestions are price-blind, non-blocking, and appear only after
  the triggering add/expansion mutation succeeds and the authoritative Draft reload completes — never
  optimistically before the server confirms the line exists.
- **Eligibility filtering.** Both trigger matching and suggestion filtering use read-time
  active/operational-eligibility checks (Active `CatalogItem`, operationally eligible
  `OfferingAssembly`), the same predicate used elsewhere in the composer (ADR-479). A suggestion or
  trigger target that has since become ineligible is silently excluded from evaluation, not
  surfaced as a broken nudge. Build Log 123 separately locks the Owner/Admin configuration read,
  where now-ineligible targets remain visible and marked for repair.

### Delete behavior (`ScopeNudgeRule`/`ScopeNudgeSuggestion` → `CatalogItem`/`OfferingAssembly`)

Checked existing FK behavior before proposing one: every existing reference to `CatalogItem` or
`OfferingAssembly` — `QuickScopeAction` (both target columns), `OfferingAssemblyItem`'s
`CatalogItemId`, `CatalogItemAlias`, `CatalogCategory`'s parent self-reference, and
`OfferingAssembly`'s own FKs — uses `DeleteBehavior.Restrict`. `OfferingAssemblyItem`'s FK to its
owning `OfferingAssembly` is the one `ClientCascade` in the area, and that's a component-row
relationship, not a reference to the catalog/assembly identity itself. Neither `CatalogItem` nor
`OfferingAssembly` exposes a hard-delete path — only `Activate`/deactivate lifecycle methods — so
there is no delete to cascade from in practice.

Proposed for implementation: `Restrict` on both `ScopeNudgeRule`'s and `ScopeNudgeSuggestion`'s FKs
to `CatalogItem`/`OfferingAssembly`, consistent with every existing reference to those entities. Not
locking this as final in this preflight per Christian's instruction — restate and confirm at
implementation time alongside the migration.

## Implementation-contract completion

[Build Log 123](123-paired-nudges-implementation-contract-preflight.md) locks the Owner/Admin
configuration API/UI, the scope-bound field nudge-read, and the five bounded implementation
sessions. This log remains the authority for the product behavior above; Build Log 123 supplies the
contract needed to implement it.

## Manual field-acceptance scenarios (for the implementation session to prove)

1. Adding a catalog item with a configured rule surfaces 1–3 price-blind suggestion chips after the
   Draft reload completes.
2. Expanding an assembly with a configured rule surfaces its suggestions; the assembly's own
   component lines do not separately trigger their own catalog-item rules.
3. Accepting a suggestion adds it as an editable Draft line and the chip clears.
4. Dismissing a nudge retires it for the rest of the open session; re-adding the same trigger item
   in the same session does not resurface it.
5. Closing and reopening the composer (new session) allows a previously dismissed rule to fire
   again.
6. A suggested item that's already an active Draft line (by `CatalogItemId`/`OfferingAssemblyId`
   identity) is not shown, even though its rule is otherwise eligible.
7. A trigger or suggestion target that has since become inactive/ineligible does not fire or
   appear, with no error surfaced to the technician.

## Explicitly out of scope for this preflight

The `ScopeNudgeRule`/`ScopeNudgeSuggestion` EF migration, configuration API/UI, field-read endpoint,
and composer suggestion-chip UI are each deferred to their separately approved implementation
sessions in Build Log 123. Neither preflight authorizes production code by itself.
