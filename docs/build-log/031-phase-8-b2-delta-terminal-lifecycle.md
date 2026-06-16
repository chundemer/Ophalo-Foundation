# Build Log 031 — Phase 8-B2-delta: Terminal Lifecycle + Analytics Primitives

**Phase:** 8-B2-delta
**Branch:** `main`
**Tests at delta closeout:** 471/471 (280 unit · 14 arch · 177 integration)

---

## Scope

Terminal lifecycle: when a Keep request transitions to `Closed` or `Cancelled`, set
`TerminatedAtUtc = now` so request duration can be derived from `CreatedAtUtc →
TerminatedAtUtc`. Auto-clear any active attention on terminal transition without creating
an `AttentionAcknowledged` event and without requiring an acknowledge reason. Preserve
fallback acknowledge on terminal requests with active attention (legacy cleanup scenario).

---

## Files

| File | Change |
|------|--------|
| `Keep.Core/Entities/KeepRequest.cs` | Added terminal branch in `ChangeStatus` and `AddBusinessUpdateWithStatus` (sets `TerminatedAtUtc`, calls `ClearAllAttentionForTerminal`); added private `ClearAllAttentionForTerminal` helper |
| `IntegrationTests/Api/ChangeKeepRequestStatusTests.cs` | Added `_resolvedWithAttentionRequestId` seed; 2 new tests (Tests 10–11) |
| `IntegrationTests/Api/AcknowledgeAttentionTests.cs` | Added `_terminalWithAttentionRequestId` seed; 1 new fallback-acknowledge test |
| `docs/decisions/decision-index.md` | ADR-116 and ADR-117 → Implemented |

---

## Key design decisions

### `TerminatedAtUtc` set in the domain, saved by EF change tracking (ADR-116)

`ChangeStatus` and `AddBusinessUpdateWithStatus` both set `TerminatedAtUtc = nowUtc` when
the target status is `Closed` or `Cancelled`. The entity is loaded as a tracked EF entity
via `GetRequestForUpdateAsync` (no `AsNoTracking`), so `CommitAsync → SaveChangesAsync`
persists all mutations including `TerminatedAtUtc` without any explicit column listing.

### `ClearAllAttentionForTerminal` is distinct from `ClearBusinessWaitingAttention`

`ClearBusinessWaitingAttention` fires on customer-visible responses and clears only
`WaitingDirection.Business` attention. `ClearAllAttentionForTerminal` fires on terminal
transitions and clears any attention regardless of direction — a closed/cancelled request
should have no active attention state regardless of who was waiting. Both helpers set
`AttentionClearReason = null`. The terminal helper does not create `AttentionAcknowledged`;
the `StatusChanged` event is the audit anchor for terminal transitions.

When both helpers would fire (terminal transition with a customer-visible message, e.g.
Cancelled with a message), `ClearAllAttentionForTerminal` runs first and `ClearBusinessWaitingAttention`
is a no-op (guard: `AttentionLevel == None`). Correct and harmless.

### `AcknowledgeAttention` retains no terminal guard (ADR-111 preserved)

Terminal requests that somehow carry active attention (legacy data, migration edge cases)
can still be acknowledged explicitly. The fallback path is tested via the
`_terminalWithAttentionRequestId` seed, which creates a closed request and then seeds
business-waiting attention via EF property manipulation to simulate the legacy scenario.

---

## Architecture compliance

- No Foundation→Keep dependency introduced.
- `ClearAllAttentionForTerminal` is a private domain method in `Keep.Core`. No service
  or infrastructure change required.
- Architecture tests: 14/14.

---

## Integration test coverage (3 new tests)

| Test | Asserts |
|------|---------|
| `TerminalTransition_SetsTerminatedAtUtc` | Resolved→Closed returns `terminatedAtUtc` as non-null string |
| `TerminalTransitionWithActiveAttention_AutoClearsWithoutAcknowledgedEvent` | All attention fields cleared, `attentionClearReason` null (not the reason string), no `attention_acknowledged` event in timeline |
| `AcknowledgeAttention_TerminalRequestWithActiveAttention_Returns200` | Fallback acknowledge on terminal request with seeded attention succeeds: attention cleared, `attentionClearReason` set to provided reason |

---

## Exit gate

- `dotnet build --no-incremental -warnaserror` → 0 errors, 0 warnings
- Architecture tests → 14/14
- Unit tests → 280/280
- Integration tests → 177/177
- All 471/471

---

## Watch-outs for B2-epsilon / B3+

- `GetParticipantsAsync` returns `DisplayName = AccountUser.Email`. B4 enriches with `User.Name`.
- `AllowedActions` always `[]` in `KeepCustomerPageResult`. B3/B4 owns this.
- `NewRequestUrl` always `null`. B4 decides.
- External contact logging/capture (ADR-115) remains pre-go-live/deferred.
- Customer write surfaces (B3) not yet started.
