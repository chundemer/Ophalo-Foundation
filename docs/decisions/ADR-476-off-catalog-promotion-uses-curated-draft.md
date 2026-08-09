# ADR-476 — Off-Catalog Promotion Uses a Curated Draft

**Status:** Locked  
**Date:** 2026-08-09  
**Related:** ADR-461; ADR-473; Build Logs 108, 112, 116, 117

## Decision

The normal Owner/Admin catalog-entry workflow remains atomic **Save & activate**. It does not
expose a user-visible Draft outcome.

`CatalogItem.Draft` is retained exclusively for deliberate, office-owned catalog curation from an
off-catalog `ProposedScopeLine` or `ActualWorkLine`. An Owner/Admin may explicitly create a
catalog candidate from either source after inspecting it; it is never automatic and a field user
never writes to the shared catalog.

A candidate is a non-selectable, non-quotable Draft. It is absent from field search, Common Items,
active assemblies, normal catalog browsing, and ordinary quote composition. The Owner/Admin may
finish its catalog information on their own schedule. Activation requires a complete catalog item
and an explicit initial pricing outcome under the ordinary published-price authority: a standalone
price or `NoStandalonePrice`, as applicable. Activation and the first published price snapshot
must commit atomically.

Promotion retains durable, typed provenance to its immutable source line. The source can be either
a proposed-scope line or an actual-work line; a single actual-work-only FK is not sufficient.
The implementation must enforce one successful promotion per source line, record actor/time/source/
target audit data, and handle concurrent promotion attempts deterministically. Discarding a Draft
candidate is explicit and auditable.

Reviewing a request scope and curating a reusable catalog item are separate decisions. Creating a
candidate neither automatically resolves nor indefinitely holds the request's proposed-scope
office-review signal; that signal resolves only under ADR-463's aggregate review rule.

## Rationale

An off-catalog line is enough to record a real job, but commonly lacks the researched SKU, correct
unit, category, cost, sell price, and presentation policy required for reusable catalog authority.
Making that raw field description immediately selectable by every technician is a worse failure
mode than a small, bounded curation lifecycle. Conversely, ordinary intentional catalog entry is a
single office action and should remain atomic rather than acquiring a general-purpose Draft UI.

## Consequences

- Supersedes Build 108/ADR-459 terminology and behavior that assumed a generic catalog Draft
  workflow. The only persisted Draft use is the curated promotion path above.
- `OffCatalogPromotedToDraft` terminology becomes target-neutral (for example,
  `OffCatalogPromotionCreated`); audit records describe the actual candidate and activation state.
- Session 3.5 owns the promotion API, Draft discovery/activation/discard UI, provenance schema,
  audit, and concurrency proof. Session 3.8 reuses the same contract for actual work.
- This decision does not make a Draft customer- or field-visible, and does not alter the
  always-available off-catalog capture escape hatch.
