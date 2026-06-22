# 055 — Gap G4e-3: Participation/feedback migration and G4/G4e completion

**Date:** 2026-06-21
**Commit:** recorded with this batch
**Baseline entering:** 1107 tests (619 unit · 14 architecture · 474 integration)
**Baseline leaving:** 1109 tests (619 unit · 14 architecture · 476 integration)
**Branch:** main
**Related:** ADR-326–329 (ADR-328 governs); build-log/053–054; session-log G4e

---

## Scope

G4e-3 migrated the final five request mutation services — participation, watcher, self-watch,
mute, and feedback-review — to derive their `AvailableActionsMetadata` response from the shared
`KeepRequestActionPolicy` evaluated **after** the successful mutation or valid no-op, replacing the
per-service hand-rolled metadata construction. With this batch every mutation and read surface that
returns request detail now consumes the one central policy, and the three superseded mapper helpers
were removed after proving zero callers. G4/G4e is complete.

No execution path, guard ordering, row scope, target eligibility, idempotency, or endpoint error
contract changed. Domain methods remain authoritative for executable eligibility, transition rules,
same-status/no-op semantics, and stable errors. The action decision is advisory response metadata,
not an execution lock; G5 owns stale concurrent intent.

## What changed

### Services migrated

Each service previously rebuilt the 11-field `AvailableActionsMetadata` inline using the old mapper
helpers (`CanAcknowledgeAttention`, `CanMarkFeedbackReviewed`, `ComputeAllowedStatuses`). Each now,
after the domain mutation completes (or valid no-op returns), resolves the current user's active
participant row from the reloaded read projection, builds a `KeepRequestActionContext`
(`CanWrite: true` — OffSeason/blocked already rejected upstream by the `IsReadOnly`/`IsBlocked`
gate; `ActiveParticipation`/`NotificationsEnabled` from the row), evaluates
`KeepRequestActionPolicy.Evaluate(request, actorContext)` against the resulting request state, and
maps via `KeepRequestDetailMapper.ToAvailableActionsMetadata`:

- `ManageResponsibleService` — `SetAsync`/`ClearAsync` share `BuildDetailAsync`.
- `ManageWatcherService` — `BuildDetailAsync`.
- `SelfWatchService` — `BuildDetailAsync` (watch + self-unwatch).
- `MuteService` — `BuildDetailAsync` (mute + unmute).
- `MarkFeedbackReviewedService` — inline response build in `ExecuteAsync`.

All pre-row role checks (Operator-cannot-assign-other / clear, Owner/Admin-only watcher and
feedback-review gates), row visibility scopes (AccountWide / ParticipationEntry / MyWork), terminal
guards, target-eligibility checks, the Operator already-assigned guard, and stable errors are
preserved in their existing order. No coarse pre-mutation capability rejection was added and no new
generic 403 path was introduced.

### Mapper helpers removed

After confirming zero remaining callers across `src/` and `tests/`, removed from
`KeepRequestDetailMapper`:

- `ComputeAllowedStatuses(KeepRequestStatus)` (string list) — superseded by the policy's enum-valued
  `AllowedStatuses`, mapped to slugs in `ToAvailableActionsMetadata`.
- `CanAcknowledgeAttention(bool, KeepRequest)` — superseded by `KeepRequestActionDecision.CanAcknowledgeAttention`.
- `CanMarkFeedbackReviewed(bool, bool, KeepRequest)` — superseded by `KeepRequestActionDecision.CanMarkFeedbackReviewed`.

Deleting the inline metadata formulas left `using OpHalo.Keep.Core.Entities.Enums;` unused in
`ManageWatcherService`, `SelfWatchService`, `MuteService`, and `MarkFeedbackReviewedService` (their
only remaining reference is the `currentUserRow?.ParticipationType` *member access*, which does not
need the namespace import); those four imports were removed. `ManageResponsibleService` keeps the
import — it still references the enum value `ParticipationType.Responsible` in its row guards. The
mapper keeps its enum usings (still required by the remaining mappers). No other cleanup performed.

### Decision XML correction

`KeepRequestActionDecision` summary corrected: the internal capabilities (SelfAssign,
ClearResponsible, ManageWatchers) are advisory shared policy vocabulary, **not** mutation execution
gates — row-before-load and target-specific service/domain checks remain authoritative for
execution. The stale claim that they are "consumed by mutation services in G4e-2/3" was removed.

## Tests

Added post-mutation `availableActions` assertions to two isolated existing participation success
paths:

- `SelfWatch_Operator_Returns200_SelfWatching` — resulting Watching/notifications-on state:
  `canWatch=false`, `canUnwatch=true`, `canMute=true`, `canUnmute=false`.
- `Mute_Operator_AfterWatch_Returns200_NotificationsDisabled` — resulting Watching/notifications-off
  state: `canWatch=false`, `canUnwatch=true`, `canMute=false`, `canUnmute=true`.

Two new `KeepRequestAvailableApiTests` for the review correction below (a new seed places the
operator under test as the Watcher on an otherwise-available request):

- `Operator_already_watching_row_is_available_but_canWatch_is_false` — Watching does not block
  availability; the row appears, `CanWatch=false`, `CanSelfAssign=true`.
- `Row_watched_by_another_user_has_canWatch_true_for_current_operator` — a non-participant current
  user sees `CanWatch=true`.

The existing `OffSeason_canSelfAssign_and_canWatch_are_false` asserts `CanWatch=false` across **all**
rows, covering the operator-watching row in OffSeason. Existing `KeepFeedbackReviewApiTests` already
prove post-review `canMarkFeedbackReviewed=false`; no duplicate coverage added.

## Review correction — Available `CanWatch` parity

Review found that the first-pass inventory claim "Available guarantees no participation row" was
false: `ApplyAvailable` admits Watching rows, so an Operator already Watching an Available request
was offered `CanWatch=true` while `KeepRequestActionPolicy` returns `CanWatch=false` for that actor
(re-watch is a domain no-op, so this was an affordance-correctness divergence, not privilege
escalation). Fixed by adding the internal SQL-computed `CurrentUserIsWatching` flag to the narrow
`KeepRequestAvailableRow` projection and gating per-row `CanWatch = canWrite && !CurrentUserIsWatching`.
The API contract (`KeepRequestAvailableItem`) is unchanged — the flag is never serialized.
Files: `GetAvailableKeepRequestsService.cs`, `IKeepRequestListPersistence.cs`,
`KeepRequestListPersistence.cs`, `KeepRequestAvailableApiTests.cs`.

## G4 / G4e final inventory

Every surface that returns the full `AvailableActionsMetadata` contract derives it from
`KeepRequestActionPolicy` through `KeepRequestDetailMapper.ToAvailableActionsMetadata`. Surfaces by
visibility scope:

**Full-entity surfaces (policy-evaluated):**

- `GetKeepRequestDetailService` — authenticated detail load (AccountWide / MyWork row scope); the
  events/history block is part of this detail result, not a separate surface.
- `GetKeepRequestListService` — standard list **and** `viewCounts` (counts are computed in this same
  service). Per-row quick/contact write affordances consume the shared decision; row-open, contact
  presence, overdue-response suppression, ranking, severity, and labels remain presentation concerns.
- `CreateBusinessRequestService` — returns a detail result on business creation.
- Operational mutations (G4e-2), evaluated post-mutation: `ChangeKeepRequestStatusService`,
  `AddBusinessUpdateService`, `AddInternalNoteService`, `AcknowledgeAttentionService`,
  `LogExternalContactService`.
- Participation/feedback mutations (G4e-3), evaluated post-mutation: `ManageResponsibleService`,
  `ManageWatcherService`, `SelfWatchService`, `MuteService`, `MarkFeedbackReviewedService`.

These cover every mutation scope (AccountWide, ParticipationEntry, MyWork) and the standard read
scopes.

**Documented exception — `GetAvailableKeepRequestsService` (Operator-only Available list, G4d):**

This surface does **not** return `AvailableActionsMetadata` and does **not** evaluate
`KeepRequestActionPolicy`. It returns a privacy-limited `KeepRequestAvailableItem` projection whose
only write affordances are the 2-field pair `CanSelfAssign` / `CanWatch`. It is an intentional
narrow-projection exception that reproduces the policy's coarse primitives over the bounded row,
without loading the full `KeepRequest` entity or its participant set:

- `KeepRequestRowQueryFactory.ApplyAvailable` guarantees every returned row is **non-terminal** and
  has **no active eligible Responsible**, and the viewing user is an active eligible Operator. On
  such a row the viewing user can therefore never be the eligible Responsible (that would make the
  row unavailable), so the user is either a non-participant **or a Watcher** — `ApplyAvailable`
  explicitly does not exclude Watching rows.
- `CanSelfAssign = canWrite` (`canOperate && !isOffSeason`): the policy's
  `CanSelfAssignResponsible = isOperator && isNonTerminal` reduces to a constant under these
  guarantees.
- `CanWatch = canWrite && !CurrentUserIsWatching`: the policy's `CanWatch = isNonTerminal &&
  participation == null` is **not** a constant — an Operator already Watching an Available row would
  otherwise be offered a redundant watch affordance that the policy denies. To match it, the narrow
  persistence projection now carries an internal SQL-computed `CurrentUserIsWatching` flag (a
  correlated `EXISTS` over the current user's active Watching participant row). It is never exposed
  in the API contract; it only gates `CanWatch`. Because the user is never the Responsible on an
  Available row, this flag fully captures the policy's "no current participation" condition.
- The surface does not expose the broader 11-field contract, so the central-mapper invariant (no
  independent reconstruction of `AvailableActionsMetadata`) is not violated.

This is the narrow reading of ADR-328's "Lists consume the same decision": the standard detail-shaped
list (`GetKeepRequestListService`) loads enough state to evaluate and consume the shared decision; the
Available summary trades full-entity loading for a privacy-limited projection that reproduces the
equivalent coarse `CanSelfAssign` / `CanWatch` primitives — including the Watching exclusion — over
the bounded row.

Zero `new AvailableActionsMetadata` construction exists outside the central mapper
(`rg "new AvailableActionsMetadata" src/` → none). Zero callers remain for the three removed helpers.

## Verification

- Full solution build: 0 warnings, 0 errors.
- Unit: 619 passed. Architecture: 14 passed. Integration: 476 passed. Total 1109.
- `git diff --check`: clean.
