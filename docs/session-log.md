# Session Log — OpHalo Foundation

**Last updated:** 2026-06-16
**Branch:** `main` (no remote yet)

---

## Phase 8-B2-gamma — COMPLETE

**Tests:** 468/468 (280 unit · 14 arch · 174 integration)
**ADRs:** 111..114 implemented; 113 gamma portion implemented, external-contact clause locked/deferred (ADR-115)

### Summary of what was built

Operator attention acknowledgement endpoint plus attention-clearing behaviour wired into
the existing business-update and status-change paths.

- `POST /keep/requests/{requestId}/attention/acknowledge` — dismisses active attention with a
  required internal reason; creates `AttentionAcknowledged` (Visibility=Internal); does not
  set first-response; does not update `LastBusinessActivityAt`
- `ClearBusinessWaitingAttention` domain helper wired into `AddBusinessUpdate`,
  `AddBusinessUpdateWithStatus`, and `ChangeStatus` (message-path only)
- `CanAcknowledgeAttention` wired in mapper: `hasOperatePermission && AttentionLevel != None`
- 11 integration tests (including the cross-account 404 pattern, silent-status vs business-update
  attention-clearing contrast)

**Key files:**

| Layer | File |
|-------|------|
| Keep.Core | `KeepRequestErrors.cs` (+AttentionReasonRequired, AttentionReasonTooLong, AttentionNotRaised) |
| Keep.Core | `KeepRequestEvent.cs` (+CreateAttentionAcknowledged) |
| Keep.Core | `KeepRequest.cs` (+AcknowledgeAttention, +ClearBusinessWaitingAttention, wired in ChangeStatus/AddBusinessUpdate/AddBusinessUpdateWithStatus) |
| Keep.Application | `AcknowledgeAttentionService.cs` (NEW) |
| Keep.Application | `KeepRequestDetailMapper.cs` (+CanAcknowledgeAttention, +AcknowledgeReasonMaxLength in ValidationHints) |
| OpHalo.Api | `AcknowledgeAttentionRequest.cs` (NEW) |
| OpHalo.Api | `ErrorHttpMapper.cs` (+3 mappings) |
| OpHalo.Api | `Program.cs` (+DI + 1 route) |
| IntegrationTests | `AcknowledgeAttentionTests.cs` (NEW — 11 tests) |

---

## Phase 8-B2-beta — COMPLETE

**Tests:** 454/454 (280 unit · 14 arch · 160 integration)
**ADRs:** 108..110 (see decision-index.md and build-log/029)

### Summary of what was built

Two new operator write endpoints:
- `POST /keep/requests/{requestId}/business-updates` — customer-visible message with optional `setStatus`
- `POST /keep/requests/{requestId}/internal-notes` — operator-only note

Full stack for both: domain methods, event factories, error codes, application services,
API request types, DI + routes, error mappings, 21 focused B2-beta integration tests.

Shared mapper (`KeepRequestDetailMapper`) extracted to eliminate duplication across all
read/write services that return `KeepRequestDetailResult`.

First-response wiring corrected: now set in `ChangeStatus` (message path) and
`AddBusinessUpdate`/`AddBusinessUpdateWithStatus`.

---

## Phase 8-B2-alpha — COMPLETE

**Status:** 436/436 tests passing. Build log 028 and decision index (ADR-102..107) shipped.

`PATCH /keep/requests/{requestId}/status` — authenticated operator status-change endpoint.
Full stack: domain method, outcome wrapper, persistence interfaces, EF implementations,
API endpoint, permission key, EF migration, 9 integration tests.

---

## Phase 8-B1-β — COMPLETE

Keep request detail + customer page read surfaces. 427/427 tests before B2-alpha.
ADRs 099..101. See build-log/026.

---

## Phase 8-B1-α — COMPLETE

Keep domain model + EF schema. ADRs 094..098. See build-log/025.

---

## Phase 5E-C — COMPLETE

Member management API + integration tests. See build-log/023.

---

## Watch-outs carried forward

- `docs/deferred-topics.md` holds the full deferred backlog.
- **ADR-058** superseded by ADR-061..064.
- **AnonymousCurrentUser** kept for potential worker/test use; not in production.
- **SystemClock** FQDN in Program.cs: `OpHalo.Foundation.Infrastructure.Services.SystemClock`.
- **Schema-drop reset** in integration test factory: `DROP SCHEMA public CASCADE` + recreate + `MigrateAsync`.
- **Migration generation** always: `--startup-project src/OpHalo.Keep.Infrastructure`.
- **No GitHub remote yet.**
- **B1-β watch-outs still active for B2+:**
  - `GetParticipantsAsync` returns `DisplayName = AccountUser.Email`. B4 enriches with `User.Name`.
  - `AllowedActions` always `[]` in `KeepCustomerPageResult`. B3/B4 owns this.
  - `NewRequestUrl` always `null`. B4 decides.
  - Customer page events sorted ascending — let frontend reverse if needed.
- **`Results.Problem` extension shape:** extension dict entries land at the top level of ProblemDetails JSON, not under an `"extensions"` key. Test assertions must use `body.GetProperty("code")`.
- **`TerminatedAtUtc` not set by ChangeStatus/AddBusinessUpdateWithStatus** when target is Closed/Cancelled. Scoped to B2-delta terminal lifecycle.
- **Terminal transitions do not auto-clear active attention.** Scoped to B2-delta.
- **`businessName ?? string.Empty`** — persistence returns null if account missing post-auth; never expected in production.

---

## Next — Phase 8-B2-delta Terminal Lifecycle + Analytics Primitives

**Pre-work complete**

Scope confirmed in session log / build-log/030:

- When status transitions to `Closed` or `Cancelled`, set `TerminatedAtUtc = now`.
- When terminal transition happens with active attention, auto-clear attention:
  - `AttentionLevel = None`, `WaitingDirection = None`, `AttentionReason = null`
  - `PriorityBand = Standard`
  - `AttentionSinceUtc = null`, `NextAttentionAtUtc = null`
  - `AttentionClearedAtUtc = now`, `AttentionClearedByAccountUserId = actor`
  - `AttentionClearReason = null`
- Do not create `AttentionAcknowledged` for terminal auto-clear.
- Do not require an acknowledge reason for terminal auto-clear.
- Keep the terminal `StatusChanged` event as the audit anchor.
- Preserve fallback acknowledge on terminal requests if active attention exists.

Tests/docs expected:
- Terminal status transition sets `TerminatedAtUtc`.
- Terminal transition with active attention auto-clears attention fields.
- Terminal auto-clear does not create `AttentionAcknowledged`.
- Terminal fallback acknowledge works if active attention remains on a terminal request.
- Add build log `031-phase-8-b2-delta-terminal-lifecycle.md`.

Files expected to change:
- `Keep.Core/Entities/KeepRequest.cs` — `ChangeStatus` (terminal path sets `TerminatedAtUtc` + calls auto-clear)
- `IntegrationTests/Api/ChangeKeepRequestStatusTests.cs` — new terminal tests
- Possibly `ChangeKeepRequestStatusService.cs` if any service-layer coordination is needed
- `docs/decisions/decision-index.md` — ADR-116 → Implemented

Out of scope for delta:
- Notification delivery.
- Participant routing.
- External contact logging/capture (ADR-115, pre-go-live).
- Customer writes.
