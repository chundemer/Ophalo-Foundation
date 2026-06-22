# 054 — Gap G4e-2: Operational-mutation shared-policy migration

**Date:** 2026-06-21
**Commit:** recorded with this batch
**Baseline entering:** 1106 tests (619 unit · 14 architecture · 473 integration)
**Baseline leaving:** 1107 tests (619 unit · 14 architecture · 474 integration)
**Branch:** main
**Related:** ADR-326–329 (ADR-328 governs); build-log/053; session-log G4e

---

## Scope

G4e-2 migrated the five operational-mutation services to derive their `AvailableActionsMetadata`
response from the shared `KeepRequestActionPolicy` evaluated **after** the successful mutation or
valid no-op, replacing per-service hand-rolled metadata construction. No execution path, guard
ordering, validation precedence, or endpoint error contract changed: domain methods remain
authoritative for executable mutation eligibility, transition rules, same-status/no-op semantics,
and stable errors. The action decision is advisory response metadata, not an execution lock.

## What changed

### Services migrated

Each service previously rebuilt the 11-field `AvailableActionsMetadata` inline (duplicating policy
booleans). Each now, after the domain mutation completes, builds a `KeepRequestActionContext`
(`CanWrite: true` — OffSeason/blocked already rejected upstream by the `IsReadOnly`/`IsBlocked`
gate), evaluates `KeepRequestActionPolicy.Evaluate(request, actorContext)` against the resulting
request state, and maps via `KeepRequestDetailMapper.ToAvailableActionsMetadata`:

- `AcknowledgeAttentionService` (migrated pre-interruption)
- `AddBusinessUpdateService` (migrated pre-interruption)
- `AddInternalNoteService` — preserves the D8 carve-out (internal notes allowed on terminal),
  now owned by the policy.
- `ChangeKeepRequestStatusService`
- `LogExternalContactService`

No coarse pre-mutation capability rejection was added; established 409/422/400/403 outcomes and
guard ordering are unchanged.

### Tests

- Added `ChangeKeepRequestStatusTests.TerminalTransition_AvailableActionsReflectResultingTerminalState`:
  a Resolved → Closed transition asserting the response metadata is evaluated against the resulting
  terminal state (`canChangeStatus`/`canSendBusinessUpdate`/`canLogExternalContact` false,
  `allowedStatuses` empty, `canAddInternalNote` true). The test uses a **dedicated, isolated**
  Resolved seed (`STATUS004`) mutated by no other test, and asserts a real `status_changed` →
  `closed` event so it cannot pass as an already-Closed same-status no-op under nondeterministic
  xUnit ordering.

## Verification

- Build: `OpHalo.Keep.Application` — 0 warnings, 0 errors.
- Unit: 619 passed
- Architecture: 14 passed
- Integration: 474 passed (was 473; +1 new regression)
- Total: 1107 passed, 0 failed
- `git diff --check`: clean

## Decisions applied

No new decisions. Implements ADR-328: one Application mapper converts the shared decision to
`AvailableActionsMetadata`; operational mutation services evaluate updated state for response
metadata after successful mutation or valid no-op; domain methods retain execution authority.

## Exit state and carry-forward

- G4e-2 complete. Five of ten mutation services now consume the shared policy.
- G4e-3 is next and requires its own file-level gate before coding: migrate responsible, watcher,
  self-watch, mute, and feedback-review services; remove the superseded `KeepRequestDetailMapper`
  action helpers once zero callers remain; complete the G4 path inventory and final verification gate.
- The superseded mapper helpers (`CanAcknowledgeAttention`, `CanMarkFeedbackReviewed`,
  `ComputeAllowedStatuses`) still have G4e-3 callers and remain expected until then.
