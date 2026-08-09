# Build Log 117 — Price Book Continuation Coding Plan

**Status:** Planning record — only Session 3.0 preflight is next; no implementation is authorized  
**Date:** 2026-08-09  
**Scope:** A reviewable, production-durable path from the completed catalog workspace to the locked
internal Price Book, Quotes & Materials foundation.  
**Related:** Build 116; Build 108; Build 114; ADR-456 through ADR-468; ADR-473 through ADR-475;
DEF-088; DEF-089.

## Outcome and boundary

The intended completion path is:

```text
Curated catalog and published prices
  -> office-owned offerings/static assemblies
  -> technician price-blind proposed scope, including one-off off-catalog capture
  -> visible office-review obligation and deliberate catalog curation
  -> office-owned internal quote and approval history
  -> actual work/material history
```

This is the internal Price Book foundation. It does **not** include customer quote delivery or
acceptance, e-signature, option groups, dynamic pricing, tax calculation, inventory/procurement,
technician price authority, generic CSV import, or a generic file-management system. Image storage
remains paused until this path is complete; image links are optional and no session below may depend
on upload capability to function.

Every implementation session remains subject to the normal gate: decision/preflight review, exact
file and mutation-family plan, focused automated proof, proportionate live verification, and a
documented handoff. A planned session is not permission to combine it with the next one.

## Session map

| Order | Session | Goal | Depends on | Completion gate |
| --- | --- | --- | --- | --- |
| **3.0** | **Continuation preflight and reconciliation** | Reconcile Build 108's model with the delivered catalog/publish APIs and confirm the exact seams, data model, endpoint families, UI boundaries, tests, and batch splits. Resolve the explicit catalog-promotion conflict below. | 2e complete | Validated plan; no code. |
| **3.1** | **Offering/Assembly domain foundation** | Add office-owned static offerings/assemblies, associated-item lines, activation, account isolation, and the explicit `Summed` / `AllInclusive` price treatment invariant. | 3.0 | Migration/domain/persistence proof; no technician workflow or quote UI. |
| **3.2** | **Offering/Assembly office management** | Authorized Owner/Admin API and workbench surface to create, edit, activate/inactivate, and inspect assemblies using published catalog items. | 3.1 | Only valid, account-owned catalog references can become active assemblies; no field selection yet. |
| **3.3** | **Proposed-scope and review-signal foundation** | Request-bound `ProposedScope`/line lifecycle, catalog/off-catalog line rules, submission, repeat-visit semantics, and aggregate `KeepRequestWorkSignal` behavior. | 3.0, 3.1 | No price data in field-facing reads; cross-account and aggregate-resolution proofs pass. |
| **3.4** | **Field scope capture — web/PWA** | Price-blind technician workflow using the fixed escape ladder, visible editable expanded assembly lines, notes/exceptions, off-catalog description/quantity, and submit/recovery UX. | 3.2, 3.3 | A technician can complete a scope without a catalog match and cannot see/edit price, cost, or margin. |
| **3.5** | **Office scope review and catalog curation** | Owner/Admin review of submitted scope, resolution of the review obligation, and explicit traceable promotion of a reviewed off-catalog item into normal catalog authority. | 3.3, 3.4 | No automatic catalog creation; promotion/retry/concurrency and reviewed-scope history are proven. |
| **3.6** | **Internal quote domain and API** | Request-bound office quote, immutable revisions/line snapshots, assembly expansion, round-half-up totals, audited overrides, and internal submit/approve lifecycle. | 3.1, 3.5 | Financial, authorization, and concurrency invariants pass; no customer delivery surface. |
| **3.7** | **Owner/Admin quote workbench** | Scope-to-quote workflow, line/section review, stated tax-included values, draft/submit/approve actions, conflict recovery, and history. | 3.6 | Office can create, revise, submit, approve, and inspect an internal quote without a customer-facing claim. |
| **3.8** | **Actual work/material foundation and field record** | Request-bound actual-use lines, one-off capture, correction/history rules, and the explicitly authorized catalog-curation traceability path. | 3.5, 3.7 | Actuals remain distinct from quoted scope and never silently alter an approved quote total. |
| **3.9** | **Internal Price Book completion verification** | Full cross-role and desktop/mobile acceptance, entitlement/degradation checks, migration/authorization/concurrency suites, documentation reconciliation, and deferred-boundary audit. | 3.1–3.8 | Internal Price Book foundation is complete; only then may the paused image-storage preflight resume. |

## Mandatory 3.0 reconciliation decisions

Session 3.0 must return a written answer to each item before any Session 3.x code begins.

1. **Off-catalog promotion:** Build 108/ADR-459 use the phrase “catalog draft,” while 2e deliberately
   exposes no user-visible draft and creates active items atomically. **Resolved by ADR-476:** a
   Draft exists only as an explicit Owner/Admin curation candidate from a typed off-catalog source;
   activation still requires atomic initial publication.
2. **Assembly lifecycle and authority:** Define the minimal Owner/Admin create/edit/activate/
   inactivate behavior and the handling of a referenced catalog item becoming inactive or changing
   price. Existing submitted scopes and quote revisions must remain immutable.
3. **Scope review presentation:** ADR-463 locks the work-signal data contract but defers its exact
   request-list/detail presentation. Define the smallest actionable office-review surface without
   changing Keep's customer-attention model.
4. **Field contract:** Confirm the exact line fields, permitted edits to assembly-expanded lines,
   notes/exceptions, off-catalog validation, submission/edit boundaries, and request-state policy.
   **Open:** ADR-477 is a proposed, not-yet-promoted field-presentable pricing path. It must first
   resolve the ADR-460 account-level gate and authorization-constraint warning treatment. Until
   then, field DTOs omit price, cost, margin, tax, inventory, and pricing-formula detail.
5. **Quote transition contract:** Confirm how an Owner/Admin starts a quote from reviewed scope or
   directly from a request, how lines retain source traceability, and which price changes require a
   new revision. ADR-465/467/468 remain controlling. If ADR-477 is explicitly promoted, this item
   also defines its human versus policy approval-basis contract.
6. **Actual-work boundary:** Confirm who records/corrects actuals, whether they are available before
   or after quote approval, and how an off-catalog actual can be curated without rewriting history.
7. **Client order:** Define the web/PWA sequence first and separately validate the native contract;
   do not assume a web interaction or API response can be copied to native without review.

## Session design rules

- **Assemblies are the first bundle capability.** They are office-built static compositions; one
  selected primary offering expands into visible ordinary lines. They are not a field-facing opaque
  bundle picker, a nested rules engine, or a universal item property.
- **Off-catalog is an escape hatch, not a catalog write.** It must be always available to the field,
  require description and quantity, and create an office-review obligation. Only an authorized,
  explicit office action can curate it into the shared catalog.
- **Quotes remain office-owned.** A submitted field scope is neither a quote nor customer approval.
  An Approved quote is internal approval, not customer acceptance. ADR-477 proposes a bounded
  exception but is not implementation authority unless explicitly promoted.
- **Actuals and quotes are separate history.** Actual work may inform future catalog curation but
  cannot silently rewrite catalog prices or an approved quote.
- **No scope expansion by image work.** Image evidence can later attach through an opaque reference;
  neither the schema nor the user flow should require it now.

## After Session 3.9

1. Resume Build Log 115's separate Request Field Evidence preflight, if still needed.
2. Reconsider customer quote delivery only through the separate ADR-475/DEF-088 sequence.
3. Reconsider reusable scope recipes, anchors/modifiers, and guided checks only from real workflow
   evidence under ADR-474/DEF-089.
4. Keep dynamic pricing, tax engines, inventory, accounting/procurement sync, and technician price
   authority deferred unless a separate decision explicitly promotes them.

## Source records

- [Build Log 116](116-price-book-continuation-and-field-scope-handoff.md): complete inventory of
  delivered and unimplemented Price Book capability.
- [Build Log 108](108-price-book-quotes-materials-erd-preflight.md): original entity model and
  financial/authorization rules.
- [Build Log 114](114-price-book-model-alignment.md): assembly-first sequencing.
- [ADR-473](../decisions/ADR-473-direct-request-bound-quote-workflow.md): internal quote boundary.
- [ADR-474](../decisions/ADR-474-emergent-scope-recipes-and-grouped-quote-history.md): later recipe
  evolution, distinct from static assemblies.
