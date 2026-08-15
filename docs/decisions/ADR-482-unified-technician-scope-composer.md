# ADR-482 — Unified Technician Scope Composer

**Status:** Locked  
**Date:** 2026-08-14  
**Supersedes:** ADR-461  
**Related:** ADR-456, ADR-457, ADR-473, ADR-476, ADR-480, ADR-481; Build Logs 107, 118

## Decision

Technician proposed-scope capture is a unified, touch-first composer. It is not a multi-step
catalog-selection workflow.

The five-rung progressive escape ladder in ADR-461 is retired. The technician must never have to
advance through Primary Offering, Common Items, Categories, Search, and Off-Catalog in order; no
step number, `Not here`, or forced classification/browsing path remains in the field experience.

The first and only capture screen has all of these coequal entry paths:

1. A deterministic Name/SKU/Alias catalog search.
2. An always-available `Add "…" as custom item` action for non-empty entered text.
3. Optional quick accelerators for configured Primary Offerings and Common Items.

Selecting a Primary Offering still expands its office-defined associated items. Selecting a catalog
result adds that known catalog item. Adding a custom item appends a single-use off-catalog line to
the draft immediately, with a sensible default quantity and an optional inline note. It never
requires a second catalog-search, category, or escape action.

The composer shows the current scope as a live editable list. Quantity, note, and removal affordances
must be touch-safe and inline where practical; quantity controls must support decimals and the
line's unit convention rather than assume a universal integer `- / +` stepper. A remove action must
have a clear accessible label and recoverable undo behavior where feasible.

Scope is a primary request-work action, not another low-priority card in the request-detail right
panel. Authorized users must see the appropriate capture/resume/view action prominently in the
request work area. The exact broader Request Details layout and the request-list action placement
are deliberately deferred; this decision authorizes only the scope-entry action hierarchy needed
for the unified composer.

## Preserved boundaries

- Field users remain price-blind: no price, cost, margin, tax, inventory, formula, or quote
  authority is exposed.
- The existing three-gate authority model remains unchanged (ADR-480).
- Off-catalog lines remain single-use and subject to office review before any catalog promotion
  (ADR-476).
- Owner/Admin retain customer-facing pricing, quote preparation, approval, and catalog maintenance.
- Catalog matching remains deterministic; this change does not introduce AI, fuzzy, or semantic
  matching.
- Existing request-bound scope, assembly expansion, snapshot, concurrency, and submit/review
  invariants remain in force.

## Consequences and required follow-up

1. Pause further work that extends, polishes, or validates the existing five-rung UI.
2. Replace the ladder/rung UI, state, and stepper tests with a single-composer implementation and
   tests for direct catalog selection, direct off-catalog entry, quick accelerators, line editing,
   submit, recovery, and price blindness.
3. Reassess field-read/search API shapes only as needed to power the unified composer. Do not change
   backend authority, snapshots, or mutation invariants without a focused preflight.
4. Move the proposed-scope entry affordance out of the right-panel card treatment into the primary
   request-work action hierarchy as part of this scope-focused implementation.
5. Do not broaden this effort into a general Request Details redesign or a request-queue redesign.
   Those are separate follow-up decisions after the unified scope capture is complete and reviewed.

## Rationale

The ladder made system catalog organization the technician's task. In field conditions, recording
work must be immediate: a technician should be able to find a known item or record an unfamiliar
one from the same first screen. The resulting workflow reduces taps and guessing while retaining
the office controls that protect published pricing, formal quotes, and catalog quality.
