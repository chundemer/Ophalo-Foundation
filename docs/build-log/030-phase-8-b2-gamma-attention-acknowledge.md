# Build Log 030 — Phase 8-B2-gamma: Attention Acknowledge

**Phase:** 8-B2-gamma
**Branch:** `main`
**Tests at gamma closeout:** 468/468 (280 unit · 14 arch · 174 integration)

---

## Scope

Operator attention acknowledgement: the ability to dismiss active attention state on a Keep
request with a required reason, without customer notification or first-response side effects.
Also verifies that customer-visible business responses clear business-waiting attention, while
silent status changes and internal notes do not.

This was a closeout session — the implementation was largely present in the working tree from
prior work. The session verified the code against the acceptance data in the session log,
identified the missing cross-account 404 test, added it, and produced this build log.

---

## Files

| File | Change |
|------|--------|
| `Keep.Core/Entities/KeepRequestEvent.cs` | Added `CreateAttentionAcknowledged` factory (Visibility=Internal, EventType=AttentionAcknowledged) |
| `Keep.Core/Entities/KeepRequest.cs` | Added `AcknowledgeAttention` (reason validated, attention cleared, event returned, first-response unchanged); added private `ClearBusinessWaitingAttention` helper (called from `AddBusinessUpdate`, `AddBusinessUpdateWithStatus`, and `ChangeStatus` when message non-null) |
| `Keep.Core/Errors/KeepRequestErrors.cs` | Added `AttentionReasonRequired`, `AttentionReasonTooLong`, `AttentionNotRaised` |
| `Keep.Application/Requests/AcknowledgeAttentionService.cs` | New — full auth stack + domain call + read-model load, pattern matches ChangeKeepRequestStatusService |
| `Keep.Application/Requests/KeepRequestDetailMapper.cs` | Added `CanAcknowledgeAttention(bool canOperate, KeepRequest request)` and `AcknowledgeReasonMaxLength: 500` in `ValidationHints` |
| `Api/Keep/AcknowledgeAttentionRequest.cs` | New — `AcknowledgeAttentionRequestBody(string Reason)` |
| `Api/Helpers/ErrorHttpMapper.cs` | Added `AttentionReasonRequired`→400, `AttentionReasonTooLong`→400, `AttentionNotRaised`→409 |
| `Api/Program.cs` | DI registration; `POST /keep/requests/{requestId:guid}/attention/acknowledge` route |
| `IntegrationTests/Api/AcknowledgeAttentionTests.cs` | New — 11 integration tests (see below) |

---

## Key design decisions

### `CanAcknowledgeAttention` formula (ADR-111)
`hasOperatePermission && request.AttentionLevel != AttentionLevel.None`.
Terminal status is not part of the formula — a Closed/Cancelled request that somehow
retained active attention can still be cleaned up. Viewer cannot acknowledge (no
`RequestsOperate` permission). The available actions field reflects current attention
state after every write so it resets to false after a successful acknowledge.

### Acknowledge is internal; no first-response; no `LastBusinessActivityAt` (ADR-112)
Acknowledging attention is an operator housekeeping action, not a customer communication.
It creates `AttentionAcknowledged` with `Visibility = Internal` so it never appears on
the customer timeline. It does not update `LastBusinessActivityAt` (same reasoning as
`AddInternalNote`). It requires a reason so the audit record is always interpretable.
Reason cap is 500 characters — tighter than business updates (4000) and notes (4000)
because acknowledge reasons are audit annotations, not free-form messages.

### Business responses clear business-waiting attention; silent actions do not (ADR-113, gamma portion)
`ClearBusinessWaitingAttention` is a private domain helper called when:
- `AddBusinessUpdate` — standalone customer-visible update
- `AddBusinessUpdateWithStatus` — combined update + status
- `ChangeStatus` — when `message` is non-null (the message is customer-visible)

It is NOT called by:
- `AddInternalNote` — internal, not customer-visible
- `ChangeStatus` when `message` is null — silent status reclassification

`ClearBusinessWaitingAttention` leaves `AttentionClearReason = null`. The visible business
message itself is the explanatory action; a separate reason field would be redundant.
External contact logging (phone/SMS/email/in-person) will also clear business-waiting
attention when it counts as customer contact, but that capture workflow is deferred to the
pre-go-live external-contact slice (ADR-115).

### Error contract (ADR-114)
Anonymous → 401 (consistent with all other operator writes).
No operate permission → 403.
Unknown or cross-account request → 404 (existence leak prevention).
Blank/whitespace reason → 400 `KeepRequest.AttentionReasonRequired`.
Reason >500 chars → 400 `KeepRequest.AttentionReasonTooLong`.
No active attention (AttentionLevel == None) → 409 `KeepRequest.AttentionNotRaised`.

---

## Architecture compliance

- No Foundation→Keep dependency introduced.
- `AcknowledgeAttentionService` lives in `Keep.Application`. `ClearBusinessWaitingAttention`
  is a private domain helper in `Keep.Core`. Neither layer references Infrastructure.
- Architecture tests: 14/14.

---

## Integration test coverage (11 tests)

| Test | Asserts |
|------|---------|
| `AcknowledgeAttention_Anonymous_Returns401` | No cookie → 401 |
| `AcknowledgeAttention_ViewerRole_Returns403` | Viewer (no `RequestsOperate`) → 403 |
| `AcknowledgeAttention_UnknownRequestId_Returns404` | Random GUID → 404 |
| `AcknowledgeAttention_CrossAccountRequest_Returns404` | Account B operator → Account A request → 404 |
| `AcknowledgeAttention_MissingReason_Returns400` | Blank reason → `KeepRequest.AttentionReasonRequired` |
| `AcknowledgeAttention_ReasonTooLong_Returns400` | >500 chars → `KeepRequest.AttentionReasonTooLong` |
| `AcknowledgeAttention_NoActiveAttention_Returns409` | No active attention → `KeepRequest.AttentionNotRaised` |
| `AcknowledgeAttention_ActiveAttention_Returns200AndClearsAttention` | All attention fields cleared, `AttentionAcknowledged` event internal, first-response fields null |
| `GetDetail_AttentionActionReflectsOperatePermissionAndActiveAttention` | Owner sees `canAcknowledgeAttention=true` on attention request; Viewer sees false |
| `BusinessUpdate_ClearsBusinessWaitingAttention` | Business update clears all attention fields, `attentionClearReason` null |
| `SilentStatusChange_DoesNotClearBusinessWaitingAttention` | Silent PATCH /status preserves attention state and `canAcknowledgeAttention=true` |

---

## Exit gate

- `dotnet build --no-incremental -warnaserror` → 0 errors, 0 warnings
- Architecture tests → 14/14
- Unit tests → 280/280
- Integration tests → 174/174
- All 468/468

---

## Risks / watch-outs for B2-delta

- `TerminatedAtUtc` is not set by any terminal transition. B2-delta adds this.
- Terminal transitions with active attention do not auto-clear attention. B2-delta adds this.
- `GetParticipantsAsync` returns `DisplayName = AccountUser.Email`. B4 enriches with `User.Name`.
