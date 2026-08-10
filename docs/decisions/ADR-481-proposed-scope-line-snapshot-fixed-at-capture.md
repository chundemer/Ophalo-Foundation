# ADR-481 — Proposed-Scope Line Snapshot Is Fixed At Capture, Never Live-Recomputed

**Status:** Locked  
**Date:** 2026-08-10  
**Related:** ADR-479 (extends); ADR-480; ADR-461; build-log/108 (supersedes snapshot-timing text); build-log/117

## Decision

Extends ADR-479's rule — "an existing technician draft retains the assembly expansion it selected;
a later live assembly or catalog change never silently rewrites that draft" — down to the
`ProposedScopeLine` field level.

When a line is created, from any escape-ladder rung (ADR-461) including an assembly's default
associated items or the off-catalog path, its display/scope fields (`DisplayNameSnapshot`,
`UnitOfMeasureSnapshot`, `OfferingAssemblyNameSnapshot`, `DefaultQuantitySnapshot`) and its initial
`Quantity` are captured once, at that moment, from whatever the technician actually saw. They are
**not** re-derived from the live `CatalogItem`/`OfferingAssembly` records on subsequent reads or on
every edit while the parent `ProposedScope.Status` is `Draft`.

A later catalog rename, price change, deactivation, or assembly-structure edit never silently
alters an already-created line's captured values. An authorized user with the ADR-480 three-gate
authority may explicitly edit a line (change `Quantity`, exclude an optional associated item,
etc.) while the scope remains `Draft` — that is an intentional edit, not an automatic system
rewrite, and stays available under the existing "mutable while Draft" rule. This is a role-based
authority, not an author-only restriction: `ProposedScopeLine` carries no per-line author field to
enforce one, and an Owner/Admin holding ADR-480's authority may assist or correct an Operator's
draft the same way any other Price Book mutation already permits Owner/Admin to act.

This corrects the literal build-log/108 ERD wording for these four fields — "recomputed from the
live records on every edit while still Draft" — which predates ADR-479 and conflicts with its
already-locked direction. Submission (`Draft` → `SubmittedToOffice`) still performs the status-gated
freeze the ERD describes (the whole scope/line rows become immutable at that transition); what
changes is that the values being frozen were already fixed at line-creation time, not recomputed
moments before submission.

Any discrepancy between a captured line and current catalog/assembly state (a renamed item, a
now-inactive component, an assembly that has become operationally ineligible per ADR-479) is a
read-time computed comparison surfaced to Owner/Admin during office review (Session 3.5) — the same
computed-eligibility-not-cascade-write pattern ADR-479 established for assemblies — never a
mutation of the in-progress or submitted scope.

## Rationale

An automatic background rewrite of a technician's already-selected lines — even while the parent
scope is nominally still `Draft` — means the system, not the technician, decides what was proposed.
That is precisely the failure ADR-479 was written to prevent one layer up, at the assembly. Leaving
the field/line layer on the older "live recompute" ERD language would silently reopen the same
problem one layer down. It would also make `IsException` unverifiable: that field exists
specifically to record whether the technician diverged from what they were shown at selection
time, and a comparison value that keeps moving underneath it defeats its purpose.

## Consequences

- Session 3.3 implements `ProposedScopeLine` creation to write these four snapshot fields plus
  initial `Quantity` once, from the live record at that instant, with no subsequent
  recompute-on-read or recompute-on-edit path.
- Session 3.5's office review reads live catalog/assembly state independently and computes a
  comparison/staleness signal, rather than relying on any snapshot-refresh mechanism.
- Build Log 108's ERD field list and column set for `ProposedScopeLine` are unchanged; only the
  snapshot-timing description for these four fields is superseded by this ADR.
