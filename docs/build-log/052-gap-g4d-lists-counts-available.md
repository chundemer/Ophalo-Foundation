# 052 — Gap G4d: My Work lists/counts and dedicated Available surface

**Date:** 2026-06-21
**Baseline entering:** 1013 tests (568 unit · 14 architecture · 431 integration)
**Baseline leaving:** 1060 tests (573 unit · 14 architecture · 473 integration)
**Branch:** main
**Related:** build-log/047 (gap audit), session-log G4d

---

## What changed

### Scope

G4d completes the list/count and Available surface work within the G4 row-authorization project:

- `GetKeepRequestListService` — scope selection is now role-explicit: Owner/Admin/Viewer → `AccountWide`; Operator → `MyWork`; unknown role → 403. `view=unassigned` is Owner/Admin-only (Operator now receives 403 instead of a filtered list). The `defaultCount` is composed from the same `scopedBase` + exact Default filter as rows (row/count parity). `ActiveViewKind.Unassigned` now uses `ApplyAvailable` (effective unassignment — no active eligible Responsible) rather than the old "no non-detached Responsible" predicate.
- `GetAvailableKeepRequestsService` — new Application service returning a cursor-paginated Available summary list for Operators. Role gate: Operator only (Owner/Admin/Viewer → 403). Cursor fingerprint = `SHA256("available:v1:{accountId}:{accountUserId}")`, binding to route version, account, and user. Cursor `TryDecode` validates `CreatedAtTick >= 0 && <= DateTime.MaxValue.Ticks` and `LastId != Guid.Empty`. Preview truncation in service layer: collect up to 159 Unicode scalars from the DB-provided 161-char prefix, then append `…` if DB returned the full 161 (i.e., description was longer); short descriptions returned verbatim.
- `KeepAvailableRequestQueryBinding` — new minimal custom binder. Only `cursor` and `limit` are recognized; any other query parameter returns `RequestListUnknownParameter`. Non-numeric `limit` returns `RequestListInvalidLimit` before the service is reached.
- `IKeepRequestListPersistence` / `KeepRequestListPersistence` — `GetAvailableAsync` added. DB projection reads `r.Description.Length > 160 ? r.Description.Substring(0, 161) : r.Description` (EF translates to SQL CASE WHEN/SUBSTRING) plus explicit `DescriptionWasTruncated` flag. `ApplyAvailable` from `KeepRequestRowQueryFactory` filters to non-terminal, active requests with no active eligible Responsible participant. Pagination uses `(CreatedAtUtc, Id)` keyset.
- `KeepRequestErrors` — added `RequestListUnknownParameter`; `RequestListInvalidLimit` message changed to "The limit is outside the allowed range." (neutral for both 1–100 list and 1–50 Available).
- `Program.cs` — `GET /keep/requests/available` registered, `KeepAvailableRequestQueryBinding` wired.

### Tests

**Unit tests** (`KeepRequestListServiceTests`):
- Removed 5 stale G4c tests that expected Operator to receive a filtered list from `view=unassigned`; G4d intentionally makes that view Owner/Admin-only. Tests removed: `canSelfAssignFromList_true_for_operator_in_unassigned_view_no_responsible`, `canSelfAssignFromList_false_when_active_responsible_present`, `canSelfAssignFromList_false_for_stale_responsible`, `canSelfAssignFromList_false_in_offseason`, `rowContext_needs_attention_wins_over_unassigned_available`.
- Kept valid: `canSelfAssignFromList_false_for_operator_outside_unassigned_view`, `canSelfAssignFromList_false_for_owner_in_unassigned_view`.
- Added 5 scope-selection tests: `scope_AccountWide_for_owner`, `scope_AccountWide_for_admin`, `scope_AccountWide_for_viewer`, `scope_MyWork_for_operator`, `unknown_role_returns_forbidden`.

**Integration tests** (`KeepRequestListQueryApiTests`):
- Replaced `Operator_unassigned_view_returns_200` with `Operator_unassigned_view_returns_403`.
- Added: `Viewer_unassigned_view_returns_403`, `Owner_unassigned_view_returns_200`, `Operator_default_list_scoped_to_MyWork_returns_no_unassigned_requests`, `Operator_viewCounts_unassigned_reflects_Available_count`.

**Integration tests** (`KeepRequestAvailableApiTests` — 38 tests, new file):
- Role gates: 401, 403 for non-Operator, 200 for active Operator.
- Exact field-set: all 12 permitted properties present on an item; no extras.
- Excluded-field absence: phone, email, description, events, notes, feedback, pageToken, participants, actions, timeline absent from raw JSON.
- Active/terminal boundaries: non-terminal requests appear; Closed and Cancelled excluded; Resolved (non-terminal) appears.
- Responsible treatment: Viewer-role (ineligible) does not block; eligible Operator Responsible blocks; detached Responsible (DetachedAtUtc set) does not block; Watching participant never blocks.
- OffSeason: `canSelfAssign` and `canWatch` are false; requests still appear.
- Preview at scalar boundaries: 159 chars → verbatim (wasTruncated=false); 160 chars → verbatim at exact boundary; 161 chars → 159 scalars + `…` (wasTruncated=true). Also: 170-char long verbatim test, whitespace normalization, non-BMP emoji counted as one scalar.
- Pagination: default limit 20, `hasMore` + `nextCursor` when results exceed limit, second page resumes without overlap.
- Cursor validation: junk cursor → 400; tampered valid cursor (flipped last char) → 400; cross-Operator replay (cursor fingerprinted to accountUserId) → 400. All return `RequestListInvalidCursor`.
- Duplicate parameter: `?limit=10&limit=20` → 400 `RequestListDuplicateParameter`.
- Limit validation: 0 → 400, 51 → 400, 50 → 200.
- Binder validation: unknown query parameter → 400 `RequestListUnknownParameter`; non-numeric limit → 400.
- Count parity: Available item count == `viewCounts.unassigned` from the Operator list endpoint (both use `ApplyAvailable`).
- Direct-ID isolation: Operator GET `/keep/requests/{id}` for an available (no-participation) request → 404.
- No-side-effect proof: participant count, event count, and request `UpdatedAtUtc` all unchanged after a read.

**Seed fix:** The cancelled test request requires a two-phase save. `ChangeStatus` with a non-null message triggers D1 first-response logic, setting `FirstResponseEventId = statusEvent.Id`; inserting both the request and the status event in a single `SaveChangesAsync` produces a circular FK dependency (request → event → request). Fix: phase 1 inserts the request (FirstResponseEventId = null) + created event; phase 2 calls `ChangeStatus` on the now-tracked entity and inserts the status event, allowing EF to order INSERT event → UPDATE request FK without circularity. Pattern validated by `KeepPersistenceProofTests` test 13.

---

## Design decisions confirmed (gate)

All decisions were approved at the G4d pre-implementation gate; these are captured here as implementation confirmation, not new decisions.

- `IKeepRequestListCursorProtector` injected directly into `GetAvailableKeepRequestsService`; cursor mechanics are Application concern and persistence must not expose signing infrastructure.
- Cursor fingerprint includes route version, account, and user; excludes limit.
- Available limit: 1–50 (list limit remains 1–100).
- `ActiveViewKind.Unassigned` uses `ApplyAvailable` going forward.
- DB over-reads 161 chars; service layer handles scalar-safe truncation.

---

## Exit state

- `GetRequestForUpdateAsync` (old account-only mutation loader) has zero callers and was removed in G4c.
- `view=unassigned` is Owner/Admin-only for list and count.
- `GET /keep/requests/available` is Operator-only with the locked privacy-limited Available contract.
- All three row scopes (`AccountWide`, `MyWork`, `ParticipationEntry`) are in production use.
- G4e (standalone `KeepRequestActionPolicy` and action-metadata migration) remains pending.

---

## Risks / carry-forward

- G4e action policy migration is the remaining G4 gate. Until complete, action metadata construction remains in `KeepRequestDetailMapper`; that is a known temporary state.
- OffSeason affordance suppression for Available (`canSelfAssign`, `canWatch = false`) is tested; the Available route remains accessible during OffSeason (read-only discovery is appropriate).
