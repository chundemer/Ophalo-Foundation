# Session Log — OpHalo Foundation

**Last updated:** 2026-06-16
**Branch:** `main` (no remote yet)

---

## Phase 8-B2-delta — COMPLETE

**Tests:** 471/471 (280 unit · 14 arch · 177 integration)
**ADRs:** 116..117 implemented (see decision-index.md and build-log/031)

### Summary of what was built

Terminal lifecycle analytics primitives:

- `TerminatedAtUtc = now` set on `KeepRequest` whenever `ChangeStatus` or
  `AddBusinessUpdateWithStatus` transitions to `Closed` or `Cancelled`.
- `ClearAllAttentionForTerminal` private domain helper: clears any active attention
  (any `WaitingDirection`) on terminal transitions without creating `AttentionAcknowledged`
  and with `AttentionClearReason = null`. Distinct from `ClearBusinessWaitingAttention`
  (business-response path, business-waiting only).
- `AcknowledgeAttention` retains no terminal guard — fallback cleanup of terminal
  requests with lingering attention remains possible.
- 3 new integration tests (terminal sets TerminatedAtUtc, terminal auto-clears attention
  without AttentionAcknowledged, fallback acknowledge on terminal request works).

**Key files:**

| Layer | File |
|-------|------|
| Keep.Core | `KeepRequest.cs` (+terminal branch in ChangeStatus and AddBusinessUpdateWithStatus, +ClearAllAttentionForTerminal helper) |
| IntegrationTests | `ChangeKeepRequestStatusTests.cs` (2 new terminal tests) |
| IntegrationTests | `AcknowledgeAttentionTests.cs` (1 new fallback-acknowledge test) |

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
- **External contact logging/capture (ADR-115)** remains pre-go-live/deferred.
- **`businessName ?? string.Empty`** — persistence returns null if account missing post-auth; never expected in production.

---

## Next — Phase 8-B3 Customer Writes

Customer-side write surfaces: customer replies, status feedback, and opt-out/preference
management via the anonymous page-token-authenticated customer endpoint.

Out of scope until B3+:
- Notification delivery.
- Participant routing.
- External contact logging/capture (ADR-115, pre-go-live).
- Admin/internal write surfaces.
