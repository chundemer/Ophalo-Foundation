# Session Log — OpHalo Foundation

**Last updated:** 2026-06-17
**Branch:** `main` (no remote yet)

---

## Phase 8-B4 Service Request Detail Enrichment — COMPLETE

**Tests:** 510/510 (280 unit · 14 arch · 216 integration)
**ADRs:** 145..163 implemented
**Build logs:** `docs/build-log/036-phase-8-b4-service-request-detail-enrichment-decisions.md` (decisions) · `docs/build-log/037-phase-8-b4-service-request-detail-enrichment-implementation.md` (implementation)

### Summary of what was built

B4: no-migration read-model enrichment slice for the authenticated service request detail page.

- `ContactActionItem(Type, Available, Target)` — new record in `KeepRequestDetailResult.cs`.
- `KeepRequestDetailResult` — two new fields: `FeedbackCommentVisible` (bool) and `ContactActions`
  (IReadOnlyList<ContactActionItem>).
- `KeepRequestDetailMapper.ToDetailResult` — extended with `AccountUserRole role` + `bool canOperate`.
  Computes `FeedbackCommentVisible = role is Owner or Admin`. Redacts `FeedbackComment = null` for
  Operator/Viewer. `BuildContactActions` returns `[]` for non-operate users; otherwise includes `call`
  when phone exists and `email` when email exists.
- `EfKeepRequestDetailPersistence.GetParticipantsAsync` — updated AccountUser query to project
  `User.Name` via `AccountUser.User` navigation (EF LEFT JOIN). `DisplayName = nonblank UserName ?? Email`.
- `GetKeepRequestDetailService` — passes `userSnapshot.Role` and `canOperate` to mapper.
- `ChangeKeepRequestStatusService`, `AddBusinessUpdateService`, `AddInternalNoteService`,
  `AcknowledgeAttentionService` — each passes `userSnapshot.Role` and `canOperate: true` (all gate on operate).
- `KeepRequestDetailB4Tests` (new file) — 8 integration tests: contact actions, feedback visibility by
  role, participant display enrichment, unresolved-feedback attention mapping.
- `KeepCustomerPageTests` — 2 new boundary tests: operator fields not exposed, expired page
  `newRequestUrl = null`.

**Key files:**

| Layer | File |
|-------|------|
| Keep.Application | `Requests/KeepRequestDetailResult.cs` (ContactActionItem + 2 new fields) |
| Keep.Application | `Requests/KeepRequestDetailMapper.cs` (role+canOperate params, redaction, contact actions) |
| Keep.Application | `Requests/IKeepRequestDetailPersistence.cs` (doc comments updated) |
| Keep.Infrastructure | `Persistence/EfKeepRequestDetailPersistence.cs` (User.Name join) |
| Keep.Application | `Requests/GetKeepRequestDetailService.cs` (pass role + canOperate) |
| Keep.Application | `Requests/ChangeKeepRequestStatusService.cs` (pass role + true) |
| Keep.Application | `Requests/AddBusinessUpdateService.cs` (pass role + true) |
| Keep.Application | `Requests/AddInternalNoteService.cs` (pass role + true) |
| Keep.Application | `Requests/AcknowledgeAttentionService.cs` (pass role + true) |
| IntegrationTests | `Api/KeepRequestDetailB4Tests.cs` (new, 8 tests) |
| IntegrationTests | `Api/KeepCustomerPageTests.cs` (2 new boundary tests) |

---

## Phase 8-B3+ Closed-Request Feedback — COMPLETE

**Tests:** 500/500 (280 unit · 14 arch · 206 integration)
**ADRs:** 135..144 implemented
**Build log:** `docs/build-log/035-phase-8-b3-plus-feedback-implementation.md`

### Summary of what was built

Closed-request customer feedback — the resolution loop after a request is marked `Closed`.

- `KeepRequestErrors` — 4 new error codes: `FeedbackResolutionRequired`, `FeedbackCommentTooLong`,
  `FeedbackUnavailable`, `FeedbackAlreadySubmitted`.
- `KeepRequest.SubmitFeedback(wasResolved, comment, priorityResponseTargetMinutes, nowUtc)` —
  domain method. Closed-only, one-time. Negative feedback raises priority `UnresolvedFeedback`
  business-waiting attention without reopening. Returns non-generic `Result`.
- `KeepCustomerPageMapper.ComputeAllowedActions` — second param `feedbackAlreadySubmitted`.
  Closed + no feedback → `["feedback"]`; Closed + feedback submitted → `[]`.
- `IKeepCustomerWritePersistence.CommitFeedbackAsync(request, ct)` — no event param (ADR-137).
- `SubmitFeedbackCommand` / `SubmitFeedbackService` — guard → expiry short-circuit → tracked
  reload → domain method → `CommitFeedbackAsync` → rebuild context inline with `request with`
  → `GetCustomerVisibleEventsAsync` → `BuildActiveResult`.
- `EfKeepCustomerWritePersistence.CommitFeedbackAsync` — `SaveChangesAsync` only, no event.
- `FeedbackBody(bool? WasResolved, string? Comment)` — `WasResolved` is nullable so null
  deserialization signals missing flag; validated at endpoint layer before service.
- `Program.cs` — `POST /keep/r/{pageToken}/feedback` route, `HandleFeedback` local function,
  DI for `SubmitFeedbackService`.
- `ErrorHttpMapper` — 5 new explicit entries (CustomerMessageTooLong + 4 feedback codes).
- 13 integration tests per ADR-143.
- `CustomerMessageTests` test 15 updated: Closed without feedback now returns `["feedback"]`,
  not `[]` (ADR-139 correction).

**Session-log correction applied:** Session log previously stated "Negative feedback requires
a comment." ADR-135 is authoritative — comment is optional even when `wasResolved=false`.
Server does not require it; only UI may strongly prompt.

**No new migration needed** — feedback columns were already present in `Phase8KeepDataModel`.

---

## Phase 8-B3-beta — COMPLETE

**Tests:** 487/487 (280 unit · 14 arch · 193 integration)
**ADRs:** 118..134 locked (no new ADRs; all locked in build-log/032)
**Build log:** `docs/build-log/034-phase-8-b3-beta-customer-message-api.md`

### Summary of what was built

B3-beta: customer-submitted messages API layer.

- `KeepCustomerPageMapper` — shared static mapper for read and write services.
- `GetKeepCustomerPageService` refactored: drops `IClock`, injects
  `KeepPublicCustomerAccessGuard` + `IKeepRequestDetailPersistence`.
- `AddCustomerMessageCommand` + `AddCustomerMessageService`.
- `EfKeepCustomerWritePersistence` — implements `IKeepCustomerWritePersistence`.
- `CustomerMessageBody` request record.
- `Program.cs`: `customer-write` rate limit, 6 customer message routes.
- 16 integration tests per ADR-131.

**AllowedActions (pre-B3+):**
- Active states → `["message","question","update_request","schedule_change_request","change_or_cancel_request","issue"]`
- Closed → `["feedback"]` (updated by B3+)
- Cancelled → `[]`
- Expired → `null`

---

## Phase 8-B3-alpha — COMPLETE

**Tests:** 471/471 (280 unit · 14 arch · 177 integration)
**ADRs:** 118..134 locked (decision gate, build-log/032).

B3-alpha: low-level foundations for customer-submitted messages.

---

## Phase 8-B2-delta — COMPLETE

**Tests:** 471/471 (280 unit · 14 arch · 177 integration)
**ADRs:** 116..117 implemented (see decision-index.md and build-log/031)

Terminal lifecycle analytics primitives: `TerminatedAtUtc`, `ClearAllAttentionForTerminal`.

---

## Phase 8-B2-gamma — COMPLETE

**Tests:** 468/468 (280 unit · 14 arch · 174 integration)
**ADRs:** 111..114 implemented.

`POST /keep/requests/{requestId}/attention/acknowledge` + attention-clearing in
business-update and status-change paths.

---

## Phase 8-B2-beta — COMPLETE

**Tests:** 454/454 (280 unit · 14 arch · 160 integration)
**ADRs:** 108..110

`POST /keep/requests/{requestId}/business-updates` + `POST /keep/requests/{requestId}/internal-notes`

---

## Phase 8-B2-alpha — COMPLETE

**Status:** 436/436 tests passing.
`PATCH /keep/requests/{requestId}/status`

---

## Phase 8-B1-β — COMPLETE

Keep request detail + customer page read surfaces. ADRs 099..101.

---

## Phase 8-B1-α — COMPLETE

Keep domain model + EF schema. ADRs 094..098.

---

## Phase 5E-C — COMPLETE

Member management API + integration tests.

---

## Watch-outs carried forward

- `docs/deferred-topics.md` holds the full deferred backlog.
- **ADR-058** superseded by ADR-061..064.
- **AnonymousCurrentUser** kept for potential worker/test use; not in production.
- **SystemClock** FQDN in Program.cs: `OpHalo.Foundation.Infrastructure.Services.SystemClock`.
- **Schema-drop reset** in integration test factory: `DROP SCHEMA public CASCADE` + recreate + `MigrateAsync`.
- **Migration generation** always: `--startup-project src/OpHalo.Keep.Infrastructure`.
- **No GitHub remote yet.**
- **`Results.Problem` extension shape:** extension dict entries land at the top level of ProblemDetails JSON, not under an `"extensions"` key. Test assertions must use `body.GetProperty("code")`.
- **External contact logging/capture (ADR-115)** remains pre-go-live/deferred.
- **`businessName ?? string.Empty`** — persistence returns null if account missing post-auth; never expected in production.
- **`KeepResponsePolicy` defaults** (first=60, standard=240, priority=60 min) apply when no policy row exists for an account; silent fallback by design.
- **Negative feedback on Closed raises attention** — intentional exception to terminal-no-attention posture (ADR-138).
- **Feedback `WasResolved` is `bool?` at API layer** — null signals missing flag, validated before service. Domain method takes `bool`.
- **Always rebuild before running tests** — `dotnet test --no-build` can run stale assemblies that mask real failures.
- **Next free ADR: ADR-164.**
- **B4 mapper signature:** `ToDetailResult` now takes `AccountUserRole role` and `bool canOperate` — all callers updated; write services pass `canOperate: true`.
- **Participant `DisplayName`** computed in persistence (two-query approach retained; User.Name projected in the AccountUsers query via EF navigation LEFT JOIN).

---

## Next — Phase 8-C (Request List)

B4 is complete. The natural next slice is Phase 8-C: the operator request list with filtering,
sorting, and the `unresolved_feedback` attention surface for closed requests. ADR-153 locks
the list behavior; implementation is a new phase. Confirm with Christian before beginning.
