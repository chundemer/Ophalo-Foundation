# 053 — Gap G4e-1: Action-policy foundation and read-surface migration

**Date:** 2026-06-21
**Commit:** 74f26a8
**Baseline entering:** 1060 tests (573 unit · 14 architecture · 473 integration)
**Baseline leaving:** 1106 tests (619 unit · 14 architecture · 473 integration)
**Branch:** main
**Related:** ADR-319–329; build-log/047; build-log/052; session-log G4e

---

## Scope

G4e-1 established the shared Application-layer action policy and migrated the three read/response
surfaces approved for the first bounded batch. Mutation services remain deliberately staged for
G4e-2 and G4e-3.

## What changed

### Shared policy foundation

- Added `KeepRequestActionContext`: explicit actor role, server-derived `CanWrite`, active
  participation, and nullable notification state.
- Added `KeepRequestActionDecision`: operational, responsibility, watcher/notification,
  feedback-review, and ordered enum-valued status-transition capabilities.
- Added `KeepRequestActionPolicy`: pure deterministic O(1) evaluation with no EF, current-user,
  HTTP, network, or clock dependency.
- Added canonical immutable `DenyAll`. Viewer, unknown/future roles, disabled writes, undefined
  participation enum values, and inconsistent participation/notification combinations fail closed.
- Preserved capability-specific terminal behavior: internal notes, active-attention cleanup, and
  qualifying Closed unresolved-feedback review are not erased by a blanket terminal denial.
- `AllowedStatuses` now means actual transitions and excludes the current status. Same-status
  command behavior remains domain-authoritative and unchanged.

### Central metadata mapping

- Added `KeepRequestDetailMapper.ToAvailableActionsMetadata` as the single conversion from the
  enum-valued shared decision to the existing 11-field response contract and status slugs.
- Retained the old action helpers temporarily because G4e-2/G4e-3 mutation services still consume
  them. They must be removed after their final caller migrates.

### Read and response surfaces

- `GetKeepRequestDetailService` now builds actor context from role, write posture, and current active
  participation, evaluates the shared policy, and maps the decision centrally.
- `CreateBusinessRequestService` evaluates the shared policy for the authenticated creator after the
  successful commit; OffSeason and Viewer remain rejected by existing upstream gates.
- `GetKeepRequestListService` evaluates the policy per row. Assignment/self-assignment metadata,
  customer update, contact, acknowledgement, and feedback-review actions consume matching decision
  properties. Row opening, contact-data presence, first-response-overdue suppression, ranking,
  severity, labels, and row context remain read/presentation concerns.

## Review correction before commit

The initial implementation evaluated the list policy but still emitted several actions from the old
`canOperate`/state branches. Pre-commit review found the split-brain behavior. The final committed
implementation corrected it:

- `ReviewFeedback` requires `CanMarkFeedbackReviewed`;
- `ContactCustomer` and call/email actions require `CanLogExternalContact` plus contact data;
- `post_customer_update` requires `CanSendBusinessUpdate`;
- acknowledgement requires `CanAcknowledgeAttention` plus the existing first-response-overdue
  presentation suppression;
- OffSeason list responses expose no write quick actions or contact actions.

## Tests

Added 46 unit tests:

- 39 `KeepRequestActionPolicyTests` covering deny-all guards, role capabilities, terminal behavior,
  attention, responsibility, watch/unwatch, mute/unmute, feedback-review eligibility, and every
  ordered status-transition set;
- 3 list parity tests covering OffSeason write-action suppression and policy-gated post-close review;
- 3 detail parity tests covering current-status-excluding response slugs and terminal empty lists;
- 1 business-create parity test covering centralized action metadata mapping.

Final verification:

- Unit: 619 passed
- Architecture: 14 passed
- Integration: 473 passed
- Total: 1106 passed, 0 failed
- `git diff --check`: clean

## Decisions applied

No new implementation-time decisions were introduced. The batch implements ADR-326–329:

- shared pure Application policy and fail-closed context;
- capability-specific terminal behavior and actual-transition status semantics;
- one detail-action metadata mapper and policy-driven list affordances;
- bounded sequential G4e migration with endpoint/domain authority preserved.

Spam/Test remain separate later V1 classifications under DEF-061. G5 remains responsible for
optimistic concurrency; the action policy is not an execution lock.

## Exit state and carry-forward

- G4e-1 is complete at commit 74f26a8.
- G4e-2 is next: migrate status change, business update, internal note, attention acknowledgement,
  and external-contact mutation services while preserving guard order and stable errors.
- G4e-3 then migrates responsibility, watcher, self-watch, mute, and feedback-review services.
- Ten mutation services still directly construct `AvailableActionsMetadata`; this is expected until
  G4e-2/G4e-3 complete.
- Superseded `KeepRequestDetailMapper` action helpers remain expected temporary callers and block
  final G4 completion if any remain after G4e-3.
