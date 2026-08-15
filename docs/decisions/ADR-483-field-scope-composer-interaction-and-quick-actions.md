# ADR-483 — Field Scope Composer Interaction and Quick Actions

**Status:** Locked  
**Date:** 2026-08-15  
**Extends:** ADR-482  
**Related:** ADR-456, ADR-457, ADR-461, ADR-473, ADR-476, ADR-480, ADR-481

## Decision

The unified technician scope composer in ADR-482 follows this interaction contract.

### Empty draft and explicit additions

Every newly created proposed-scope draft starts empty. Keep does not automatically add a baseline
assembly, fee, labor line, or other guessed work based on a request type, intake source, business
default, or trade. A technician only sees lines they explicitly add.

The first screen offers three coequal ways to add scope:

1. Select a deterministic Name/SKU/Alias catalog-search result.
2. Select an office-curated Quick scope action.
3. Select the visible `Add "…" as custom item` action for non-empty entered text.

Typing alone never creates a line. The technician explicitly selects the custom-item action, which
adds one single-use off-catalog line with a sensible default quantity and an optional inline note.

### Assemblies and draft-line control

A Quick scope action may be an assembly or a Common Item. Selecting an assembly immediately expands
it into ordinary draft lines, including its associated items; it never creates an opaque or locked
field-facing bundle. While the scope is a Draft, every added/expanded line may be edited or removed
where the existing line-type rules permit it. Submission keeps the established immutable/read-only
scope boundary; this draft flexibility does not permit post-submit field edits.

### Quick scope actions

Quick scope actions are optional, account-owned field accelerators. They are not inferred from all
active assemblies/catalog items, a fixed trade taxonomy, or a new universal request archetype.

The eventual Owner/Admin configuration records an explicit field-quick-action flag and display
order for a deliberately short set of eligible assemblies and/or Common Items. The precise settings
surface and any hard numeric limit require a focused configuration preflight, but the field composer
must present a small ordered set rather than an unbounded catalog-derived chip list.

### Field ergonomics and boundaries

- The composer is a single mobile-safe workbench: live draft, unified search/type input, optional
  Quick scope actions, and a persistent submit affordance when appropriate.
- All field catalog matching stays deterministic. This decision adds no AI, fuzzy, or semantic
  matching.
- Quantity editing supports the line's unit convention and decimal values; no universal integer-only
  stepper is required or assumed.
- Removal is immediate with a clear touch-safe, accessible action and a short recoverable Undo path
  where feasible; no confirmation modal is required for ordinary draft-line removal.
- Field users remain price-blind and cannot promote custom lines, maintain catalog data, or prepare
  customer-facing pricing/quotes.

## Deferred

This decision deliberately does not choose the visual layout, component styling, mobile breakpoint
behavior, keyboard/focus behavior, or exact Request Details placement of the primary scope action.
Those must be decided in a focused composer UI-design/preflight discussion before implementation.
It also does not introduce request categories/archetypes, request-type-driven defaults, or a broader
Request Details/request-queue redesign.

## Rationale

Assemblies save technician time only when they are optional one-tap anchors for predictable work;
the technician then records the job-specific delta without being forced to remove software guesses
or navigate catalog structure. An explicit, curated accelerator list keeps that speed benefit while
remaining trade-neutral and usable for businesses with substantially different pricing and service
models.
