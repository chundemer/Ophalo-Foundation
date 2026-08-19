# Build Log 127 — Owner/Admin Proposed Work Review: Step 1 Implementation Preflight

**Status:** Preflight — implementation not yet authorized
**Date:** 2026-08-17
**Related:** ADR-488, ADR-487, ADR-463, ADR-464, Build Log 126

## Scope

Step 1 only, per ADR-488: Owner/Admin **Mark reviewed** transition, dedicated review queue,
read-only newest-first scope history. No quotes, commercial documents, Actual Work, customer
acceptance, technician return/reject, or accounting export.

## 1. Backend transition: `SubmittedToOffice` → `OfficeReviewed`

New `ProposedScope` domain method, mirroring `Submit`'s shape:

```csharp
public Result MarkReviewed(Guid reviewedByAccountUserId, DateTime reviewedAtUtc, string? reviewNote)
{
    if (Status != ProposedScopeStatus.SubmittedToOffice)
        return Result.Failure(ProposedScopeErrors.NotSubmitted);

    // bounded-length/control-character validation on reviewNote, mirroring the off-catalog
    // description validation already in ProposedScopeLine

    Status = ProposedScopeStatus.OfficeReviewed;
    ReviewedByAccountUserId = reviewedByAccountUserId;
    ReviewedAtUtc = reviewedAtUtc;
    ReviewNote = reviewNote;
    ConcurrencyVersion = Guid.NewGuid();
    return Result.Success();
}
```

`ReviewedByAccountUserId`/`ReviewedAtUtc` already exist on the entity (unused since creation — see
`ProposedScope.cs:32-34`). New: `ReviewNote` (nullable, bounded string column) plus its EF mapping
in `ProposedScopeConfiguration`. New error: `ProposedScopeErrors.NotSubmitted` (distinct from
`NotDraft` — this transition's precondition is the opposite status).

Pure domain transition only — no signal side effect, same discipline as `Submit`.

## 2. Version/concurrency contract

Reuses the existing `X-Keep-ProposedScope-Version` header and `ProposedScopeVersionHeader.Parse`
verbatim — no new header contract. Mutation endpoint requires it; a version mismatch reloads
authoritative current state via the existing `ProposedScope.VersionMismatch` 409 mapping (ADR-488:
"never overwrites another review").

## 3. Optional bounded review note

New nullable `ReviewNote` field on `ProposedScope`, request-bound and immutable once set (this
transition is a one-shot terminal write for the row — no separate edit-note endpoint in Step 1).
Bound length (match the existing off-catalog description convention: reject control characters,
cap length — confirm exact cap against `ProposedScopeLine`'s `OffCatalogDescription` validation
during implementation, do not invent a new number ad hoc).

## 4. Aggregate signal resolution

New method on `KeepRequestWorkSignal` (currently has no public mutation — see
`KeepRequestWorkSignal.cs:35-39`), or a native SQL resolve mirroring the existing
`EfProposedScopeSubmissionPersistence.UpsertWorkSignalAsync` upsert but in the opposite direction:

```sql
UPDATE keep_request_work_signals
SET resolved_at_utc = @nowUtc, concurrency_version = @newVersion, updated_at_utc = @nowUtc
WHERE account_id = @accountId AND keep_request_id = @requestId
  AND source_module_key = @sourceModuleKey AND signal_key = @signalKey
  AND resolved_at_utc IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM proposed_scopes
      WHERE account_id = @accountId AND request_id = @requestId
        AND status = 'SubmittedToOffice' AND id <> @justReviewedScopeId
  )
```

The `NOT EXISTS` guard is the ADR-463 aggregate-state-driven rule: resolve only when no other
`SubmittedToOffice` scope remains on the request. Runs inside the same transaction as the
`MarkReviewed` domain-state write and its `SaveChangesAsync`, matching
`EfProposedScopeSubmissionPersistence`'s atomic-boundary pattern exactly (new
`IProposedScopeReviewPersistence`/`EfProposedScopeReviewPersistence`, parallel to the existing
submission persistence pair — not layered onto `IProposedScopeSubmissionPersistence`, since this is
a distinct atomic operation with its own outcome enum).

Outcome enum mirrors `ProposedScopeSubmissionResult`: `Committed`, `NotFound`, `RequestTerminal`,
`NotSubmitted`, `VersionMismatch`.

## 5. Terminal-request block; Owner/Admin authorization

Reuses the exact `SELECT ... FOR UPDATE` terminal-check pattern from
`EfProposedScopeSubmissionPersistence.SubmitAsync` (`KeepRequest.IsTerminal`, client-materialized).

Authorization gate mirrors `OfferingAssemblyApiService.AuthorizeAsync` (Owner/Admin-only catalog
pattern), not `ProposedScopeApiService`'s technician three-gate:

- Gate 1 — account access, mutation semantics (Blocked and ReadOnly both deny).
- Gate 2 — Price Book entitlement (`CapabilityPackageFeatureKeys.PriceBookQuotesMaterials`).
- Gate 3 — single permission check against `PermissionKeys.Keep.PriceBookCatalogManage` (the
  existing Owner/Admin catalog-management key — reused rather than minting a new
  `ReviewOfficeMarkup`-style key; confirm this reuse against Christian before coding, since ADR-488
  doesn't name a permission key explicitly and a dedicated `keep.pricebook.review.manage` key is
  the plausible alternative).

No row-level `KeepRequestVisibilityScope` branching is needed for the mutation — Step 1 is
Owner/Admin-only, always account-wide, unlike technician `MyWork` scoping.

## 6. Dedicated request queue / count / row context

New Price-Book-owned query, **not** an addition to Core's `ActiveViewKind` enum
(`IKeepRequestListPersistence.cs`) — ADR-488 explicitly keeps this queue separate from
`KeepRequest.AttentionLevel`/`AttentionReason` and from customer-attention views, so it does not
belong in `GetKeepRequestListService`'s Core-owned view machinery. New
`IProposedWorkReviewQueuePersistence` (Keep.Application/PriceBook) joining
`keep_request_work_signals` (active `proposed_scope_needs_office_review` rows) to
`proposed_scopes`/`keep_requests`, returning a narrow row projection matching the
`KeepRequestAvailableRow` convention: request id, reference code, customer name, submitted age
(oldest outstanding `SubmittedToOffice` scope's `SubmittedAtUtc` on that request), submitting
technician display name, concise line-count/summary. Cursor-paginated like the existing Available
view. Truthful count = query result count, not signal-table count (a request could have the signal
row but zero currently-`SubmittedToOffice` scopes only in an impossible state given resolution
semantics — still assert via the join, never trust the signal row alone).

## 7. Newest-first read-only history

New method on `IProposedScopePersistence` (or a dedicated
`IProposedScopeHistoryPersistence`, confirm which during implementation preflight-mechanical pass):
`GetHistoryForRequestAsync(accountId, requestId, ct)` returning all `ProposedScope` rows
(`SubmittedToOffice` and `OfficeReviewed`, excluding `Draft` — a request's live Draft is not
history) ordered by `SubmittedAtUtc DESC`. Reachable by any authorized reader (ADR-488: "authorized
readers retain read-only scope history" even on a terminal request) — this is a read path with no
terminal-request block, unlike the mutation.

## 8. API DTOs / endpoints

New endpoints in `ProposedScopeEndpoints.cs` or a new `ProposedWorkReviewEndpoints.cs` (lean toward
a new file: this is an Owner/Admin surface with its own service/auth composition, distinct enough
from the technician-capture file's stated scope in its own doc comment):

- `POST /keep/pricebook/proposed-scopes/{proposedScopeId:guid}/mark-reviewed` — body: optional
  `{ ReviewNote }`; requires `X-Keep-ProposedScope-Version`; returns
  `ProposedScopeTransitionResponse`-shaped `{ ConcurrencyVersion }` (reuse existing response
  record).
- `GET /keep/pricebook/proposed-work-review/queue` — cursor-paginated, returns rows +
  `PageInfo` + truthful count, matching the `KeepRequestAvailableRow`/cursor convention.
- `GET /keep/pricebook/proposed-scopes/by-request/{requestId:guid}/history` — newest-first, full
  list, no pagination needed at Step 1 volume (confirm against Christian if a request could
  plausibly accumulate enough scopes to need paging — ADR-464 implies one row per field visit, so
  likely small).

New error `ProposedScope.NotSubmitted` needs an `ErrorHttpMapper` entry (409, matching
`NotDraft`'s treatment).

## 9. UI surfaces

- Owner/Admin dedicated **Proposed Work Review** queue view (new nav entry or tab), row-level
  context per §6.
- Request detail: read-only newest-first scope history list, reusing existing scope/line display
  components in read-only mode only (ADR-488 — must not expose the technician composer or imply
  line editing).
- **Mark reviewed** action with optional note field and version-conflict reload-and-retry handling.

## 10. Migration / persistence changes

- Add `ReviewNote` (nullable, bounded `varchar`) column to `proposed_scopes`.
- No new tables. `keep_request_work_signals` schema unchanged (resolution is an `UPDATE`, not a new
  column).
- EF configuration update in `ProposedScopeConfiguration`; new migration via
  `dotnet ef migrations add` (Christian runs, per repo convention).

## 11. Test matrix

- Domain: `MarkReviewed` — success from `SubmittedToOffice`; failure from `Draft`/`OfficeReviewed`;
  review-note validation (bounded length, control characters).
- Persistence/integration: commit path; `NotFound`; `VersionMismatch`; `RequestTerminal` (row-locked
  concurrent terminal transition); `NotSubmitted`; signal resolves only when zero other
  `SubmittedToOffice` scopes remain on the request (two concurrently pending scopes — reviewing one
  leaves signal active); signal reopening/resolution round-trip across a later visit (ADR-464).
- Authorization: Owner/Admin permitted; Operator/Viewer forbidden; entitlement-gated (403 without
  Price Book enrollment); Blocked/ReadOnly account-access denial.
- Queue: truthful count matches join result; row context fields populated; cursor pagination
  boundary; empty-queue state.
- History: newest-first ordering; includes `SubmittedToOffice` and `OfficeReviewed`, excludes
  `Draft`; readable on a terminal request.
- API contract: version header required/invalid/mismatch; error-code-to-HTTP-status mapping for the
  new `NotSubmitted` error.

## Open decisions requiring Christian's confirmation before code

1. **Permission key** — reuse `PermissionKeys.Keep.PriceBookCatalogManage` for Mark Reviewed, or
   mint a dedicated key? (§5)
2. **Endpoint/service file placement** — new `ProposedWorkReviewEndpoints.cs`/`...ApiService.cs`
   pair, or extend the existing `ProposedScopeEndpoints.cs`/`ProposedScopeApiService.cs`? (§8)
3. **History pagination** — confirm unpaginated newest-first list is acceptable at Step 1, or
   requires cursor pagination like the queue. (§7, §8)
4. **Review-note bound** — exact length cap and character-validation rule to reuse or define. (§3)

## Batch-size check

Estimated: 1 mutation handler family (Mark reviewed) + 2 read surfaces (queue, history) = well
under the 3-family gate. Production files: ~2 Core (entity + error), ~3 Application (service,
persistence interface, review-persistence outcome), ~2 Infrastructure (EF persistence, config), ~2
Api (endpoints/DTOs) ≈ 9; plus queue/history read services add a few more. Likely lands near or
slightly over the 8-production-file soft count once queue+history are included — flag for a
possible two-slice split (mutation first, queue+history second) at the mechanical preflight pass
rather than deciding now.
