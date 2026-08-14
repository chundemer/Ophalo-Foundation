# Build Log 118 — Technician Proposed-Scope Field Capture Preflight (Session 3.4)

**Status:** Preflight complete and locked; no implementation started
**Date:** 2026-08-13
**Scope:** Price-blind web/PWA technician workflow to capture a `ProposedScope` on an existing
`KeepRequest`, using the escape ladder (ADR-461) and the ADR-480 three-gate authority already
delivered through Session 3.3b.
**Related:** Build 116; Build 117; ADR-461; ADR-462; ADR-479; ADR-480; ADR-481.

## Why 3.3b is not enough

Session 3.3b delivered the complete `ProposedScope` mutation API (`create`, line `add`/`update`/
`remove`, `submit`) behind the ADR-480 three-gate stack, but it is a mutation-only surface with two
properties that block a safe field UI as-is:

1. **No read contract.** There is no `GET` for a `ProposedScope` anywhere — a technician cannot
   resume a draft, and the UI cannot render lines after a mutation (`AddLine` returns only an id/
   version).
2. **`AddLine` trusts caller-supplied display snapshots.** `DisplayNameSnapshot`/
   `UnitOfMeasureSnapshot`/`OfferingAssemblyNameSnapshot`/`DefaultQuantitySnapshot` and
   `CatalogItemId`/`OfferingAssemblyId` come straight from the request body with no account-scoped
   existence check (ADR-481's deliberate "caller supplies already-resolved values" design). A field
   UI that resolves these client-side and passes them through is not an authority boundary — any
   caller can hit the same endpoint directly with spoofed values or a cross-account id.

Both are addressed below, not by asking the browser to behave, but by adding server-authoritative
selection endpoints and retiring the endpoint that trusted the browser.

## Locked decisions

1. **Entitlement discovery.** The new by-request `ProposedScope` read doubles as the availability
   probe. 403 → the capture entry point renders nothing. No `CanCaptureProposedScope` flag is added
   to `AvailableActionsMetadata` — that would couple every Request Detail read to the Price Book
   entitlement resolver for a capability most requests never touch.
2. **Selection authority.** Raw `POST /keep/pricebook/proposed-scopes/{id}/lines` is **removed**
   from the technician-reachable surface (see "Retired in this session" below). Two new
   server-authoritative endpoints replace it:
   - `POST .../{id}/field-select` — single line (`KnownCatalogItem` or `OffCatalogItem`). For
     `KnownCatalogItem`, the server resolves the account-owned, Active `CatalogItem` by id and
     builds `DisplayNameSnapshot`/`UnitOfMeasureSnapshot` itself; an unknown or cross-account id is
     rejected, never accepted. `DisplayOrder` is never client-supplied (see decision 5).
   - `POST .../{id}/expand-assembly` — atomic. Creates the `PrimaryOffering` line plus every
     non-excluded `AssociatedItem` line from a server-resolved, operationally-eligible (ADR-479)
     `OfferingAssembly` in one transaction. No sequential per-item `POST`s, no partial-expansion
     state. Locking protocol in "Assembly-expansion locking protocol" below.
   - Existing `PATCH`/`DELETE .../lines/{lineId}` are unchanged and stay field-reachable —
     `Quantity`/`IsException`/`Note`/`DisplayOrder` (on an existing line, via `PATCH`) are
     legitimately technician-editable values, not trusted catalog data.
3. **Scope display.** Show the current open `Draft`. If none exists, show only the single most
   recent `SubmittedToOffice`/`OfficeReviewed` scope, read-only, as context. No full scope history
   in 3.4 — that belongs to Session 3.5's office-review surface.
4. **Concurrent scopes.** A technician may start a new `Draft` while a prior scope on the same
   request is `SubmittedToOffice` awaiting review. No create-time block is added — ADR-463 already
   supports multiple concurrently review-pending scopes per request.
5. **Display order.** Neither `field-select` nor `expand-assembly` accepts a client-supplied
   `DisplayOrder`. The server computes `MAX(DisplayOrder)` across the scope's current lines inside
   the same transaction and appends at `max+10, max+20, ...`. A removed line's slot is never reused.
6. **Off-catalog text.** `OffCatalogDescription` stores up to the existing 500-char DB limit as
   given. The server independently derives `DisplayNameSnapshot` from it: trim leading/trailing
   whitespace, reject any C0/C1 control character (0x00–0x1F, 0x7F–0x9F — this is a single-line
   field, so embedded tab/newline/CR are rejected too), then truncate to 200 chars. No broader
   Unicode sanitization: accented letters, non-Latin scripts, symbols, and punctuation in trade
   descriptions pass through untouched. A control character is rejected (validation error), not
   silently stripped.
7. **Intermittent network handling.** Every field-facing mutation (`field-select`,
   `expand-assembly`, line `PATCH`/`DELETE`, `submit`) disables its triggering control immediately
   on click and shows a pending state. On timeout/network failure, the client does not retry the
   same mutation — it re-fetches the draft and reconciles from that read. A manual retry after an
   ambiguous failure carries the now-stale `ExpectedVersion`; if the original call actually
   succeeded, the retry gets 409 and routes into the existing reload-and-notify path. No idempotency
   key needed — `ConcurrencyVersion` always advances on success.
8. **Off-catalog reporting.** No new `IsOffCatalogItem`-style column. `ProposedScopeLineType.
   OffCatalogItem` remains the sole authoritative tag; future office analytics query line type.
   Actual-work reporting stays out of scope for 3.4.

## Retired in this session

`POST /keep/pricebook/proposed-scopes/{proposedScopeId}/lines` (raw `AddLine`) and
`ProposedScopeApiService.AddLineAsync`/`AddProposedScopeLineApiCommand` are removed, not re-gated —
there is no current legitimate caller (no office-composition UI exists yet), so a placeholder
office-only permission would just move the unused-surface problem instead of closing it.
`EditProposedScopeService.AddLineAsync` stays as an internal Application-layer method, called only
by the new `field-select`/`expand-assembly` services with server-resolved snapshots, never exposed
via HTTP directly. A future office-composed-line feature (3.5+) gets its own endpoint and its own
authorization design at that time. Existing `ProposedScopeApiTests` coverage of the raw endpoint is
removed or repointed at the new endpoints in the same batch that retires it (3.4d), so there is no
window where the trusted-snapshot endpoint is reachable.

## Assembly-expansion locking protocol

"One transaction" alone does not prevent a read-then-stale-write race (the field UI's eligibility
read is uncommitted and can go stale before the expansion commits). Inside the one transaction, in
this order:

1. `SELECT ... FOR UPDATE` the `ProposedScope` row (mirrors `EfProposedScopeSubmissionPersistence`'s
   existing pattern) — locks against a concurrent expansion/edit/submit on the same scope and gives
   the version/status check a stable read.
2. `SELECT ... FOR UPDATE` the `OfferingAssembly` row, then every referenced `CatalogItem` row
   (primary + all associated items), locked in ascending id order — a fixed order any future
   multi-row locker on these tables should also follow, to avoid deadlocking two concurrent
   expansions with overlapping items. No existing `OfferingAssembly`/`CatalogItem` mutation path
   takes row locks today (ADR-479's direct-edit model relies on `ConcurrencyVersion` alone), so this
   is the first lock on those tables — no prior order to conflict with, but the rule must be stated
   for whatever locks them next.
3. Re-check operational eligibility (ADR-479's predicate) and each locked item's `ActiveState`
   **after** the locks are held, not from the pre-transaction read. Failure here (ineligible
   assembly, a component gone inactive, a missing required price) rejects with a typed conflict
   error and creates zero lines — never a partial set.
4. Only then: compute `MAX(DisplayOrder)`, insert `PrimaryOffering` + non-excluded `AssociatedItem`
   lines at `max+10, +20, ...`, bump `ConcurrencyVersion` once, commit.

Proof: a real two-transaction race test — start an expansion, hold it at the eligibility re-check,
deactivate the assembly's primary item on a second connection, commit the first, assert zero lines
persisted.

## Session map (3.4a–3.4g)

| Order | Session | Delivers | Mutation family? | Rough file count |
| --- | --- | --- | --- | --- |
| 3.4a | `ProposedScope` read API | By-request/by-id GET; "no scope yet" vs. most-recent-only contract | No | ~5 prod + 1 test |
| 3.4b | Field-safe catalog read API | Price-free Common Items/Categories/search reads, new gate, `IsCommonItem` filter | No | ~5 prod + 1 test |
| 3.4c | Field-safe assembly read API | Eligibility-filtered, price-free assembly list/detail | No | ~4 prod + 1 test |
| 3.4d | Server-authoritative `field-select` + retirement of raw `AddLine` | New command, off-catalog snapshot validation, removes raw endpoint | Yes (1) | ~5 prod + 1 test |
| 3.4e | Atomic `expand-assembly` | Locking protocol above, exclusion set, max-display-order append | Yes (2) | ~6 prod + 1–2 test |
| 3.4f | Frontend: entry point + ladder selection | Card, capture modal, ladder steps, layout wiring | — | TBD at implementation preflight |
| 3.4g | Frontend: draft management, submit, recovery | Line edit/remove UI, submit, 409/timeout recovery, read-only views | — | TBD at implementation preflight |

Each batch compiles and tests independently; no batch combines a backend mutation family with its
own frontend consumer. 3.4d must land its retirement of raw `AddLine` in the same commit as
`field-select`, not as a follow-up.

## Authorization and price-visibility proof (carried into every batch)

- Gate order for every new read/write, stated explicitly, not inherited by reference: authenticate
  → account-access (`Blocked`-only for reads; `Blocked`+`ReadOnly` for `field-select`/
  `expand-assembly`, matching `ProposedScopeApiService`'s existing mutation posture) → Price Book
  entitlement → `RequestsOperate` AND `ScopeCapture` → row-visibility (MyWork/AccountWide) → act.
  Row-visibility failure is 404, never 403/409 — 3.3b's own post-implementation review caught this
  exact class of bug twice.
- `field-select`/`expand-assembly` account-scope-check every referenced `CatalogItemId`/
  `OfferingAssemblyId` before creating a line.
- Field DTOs asserted price-free on the wire (raw JSON has no `sellPrice`/`cost`/`margin`/
  `calculatedSellPrice`/`pricingMode`/`priceStatus`/`marginStatus` key), not just by C# type
  absence.
- Regression: Operator-role calls to the existing Admin-gated `GET /keep/pricebook/catalog-items`,
  `/catalog-categories`, `/offering-assemblies{,/{id}}` still 403 — proves the new field-safe
  surface sits beside `PriceBookCatalogManage`, not inside a loosened version of it.

## Manual local acceptance

Disposable database only, no founder-account dummy data. Seed: entitled account; Operator with
request participation (MyWork) and one without (expect 404-hidden); catalog items including
`IsCommonItem = true` and searchable name/SKU/alias matches; one category; one
operationally-eligible assembly with required and optional `AssociatedItem`s; one ineligible/
inactive assembly (must not appear in the Primary Offering rung; a direct `expand-assembly` call
against it must be rejected). Acceptance path: capture via all four field-facing rungs across a
draft, exclude an optional assembly item at expansion time, edit/except a line, submit, confirm
read-only state and that a second Draft can start on the same request while the first is
`SubmittedToOffice`. Repeat on narrow-mobile viewport. Confirm Viewer sees no entry point.

## Source records

- [Build Log 116](116-price-book-continuation-and-field-scope-handoff.md): completed/unimplemented
  Price Book capability inventory.
- [Build Log 117](117-price-book-continuation-coding-plan.md): Session 3 map and reconciliation
  decisions.
- [ADR-461](../decisions/decision-index.md): five-rung escape ladder (indexed decision, no
  standalone file).
- [ADR-480](../decisions/ADR-480-proposed-scope-capture-permission-and-three-gate-authority.md):
  three-gate mutation authority.
- [ADR-481](../decisions/ADR-481-proposed-scope-line-snapshot-fixed-at-capture.md): snapshot timing.
