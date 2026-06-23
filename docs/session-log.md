# Session Log — OpHalo Foundation

**Last updated:** 2026-06-23 (G7b complete; G7c pre-work required next)
**Branch:** `main` (no remote yet)
**Current baseline:** 1220 tests (643 unit · 14 architecture · 563 integration).
**Next free ADR:** ADR-336
**Next batch: G7c — positive-comment visibility correction, ADR/deferred-ledger reconciliation, build-log/058, full regression, and G7 completion.**

---

## Session Protocol

This file is the current execution brief, not the historical build archive. Completed implementation
detail lives in `docs/build-log/`; authoritative decisions live in
`docs/decisions/decision-index.md`.

For every implementation slice:

- classify the work explicitly: discovery when pre-work is incomplete; mechanical implementation
  preflight when the current brief is marked pre-work complete;
- during preflight, use targeted `rg` only to confirm named signatures and enumerate compile-impact
  callers; do not rediscover already-locked architecture, scope, tests, or decisions;
- inspect current signatures and failure modes before calling existing code;
- always present the file-level gate before writing; when pre-work is complete it is a mechanical
  caller/signature gate, while incomplete pre-work also requires design decisions and approval;
- enforce the hard slice gate: at most 3 independent mutation handler families, 8 production files,
  and 12 total changed files including tests/fakes/docs; exact aliases sharing one handler/service
  count as one family only when all aliases are enumerated and parameterized-contract tested;
- preserve fail-closed account, row, action, and public-token behavior;
- add focused authorization/regression tests and run the proportionate broader suite;
- self-review for policy drift, accidental visibility expansion, untested direct-ID paths, stale
  documentation, and unrelated scope;
- commit only after Christian approves the completed diff.

## Pre-Session 6 Gap Resolution — IN PROGRESS

**Source of truth:** `docs/build-log/047-pre-session-6-phase-7-session-5-gap-audit.md`

**Required order:** G1 → G2 → G3 → G4 → G5 → G6 → G7 → G8. Do not validate or code Phase
8-B5 Session 6 until G8 records the final green gate.

### Completed gap references

- **G1 complete — 866 tests.** Account-safe composite foreign keys, canonical phone identity,
  origin-aware activity, and FirstResponseEvent FK. ADR-301–305; migrations
  `20260619235301_KeepG1AccountSafeSchema` and
  `20260620015428_KeepG1FirstResponseEventFK`; build-log/048.
- **G2 complete — 920 tests.** Shared intake validation, safe email preservation, and concurrent
  customer recovery. ADR-306–310; build-log/049.
- **G3a complete — 941 tests.** Intake-link ensure/status/transactional replacement and obsolete
  public-contract cleanup. ADR-311–314; build-log/050.
- **G3b complete — 986 tests.** Authenticated business-created requests, shared validation and
  intake commit helper, authenticated creation actor. ADR-315–318; build-log/051.
- **G4a complete — 995 tests.** KeepRequestVisibilityScope, KeepRequestRowQueryFactory,
  detail-row authorization for GetKeepRequestDetailService (AccountWide/MyWork). ADR-319–325
  locked (pre-work); 10 direct-ID row-auth integration tests. commit e57522a.
- **G4b complete — 1004 tests.** GetVisibleRequestForUpdateAsync added to
  IKeepRequestOperatePersistence/EfKeepRequestOperatePersistence; old GetRequestForUpdateAsync
  marked [Obsolete] on both interface and implementation. AcknowledgeAttentionService,
  AddBusinessUpdateService, AddInternalNoteService, ChangeKeepRequestStatusService,
  LogExternalContactService all migrated (Owner/Admin → AccountWide, Operator → MyWork,
  unknown role → 403). ExternalContact Operator test updated with Responsible participation.
  9 new G4b mutation-row-auth integration tests. G4c callers emit [Obsolete] warnings.
- **G4c complete — 1012 tests.** ParticipationEntry implemented in KeepRequestRowQueryFactory as
  ApplyMyWork.Union(ApplyAvailable) — no predicate duplication. ManageResponsibleService (Set:
  AccountWide/ParticipationEntry, Clear: AccountWide), ManageWatcherService (AccountWide),
  MarkFeedbackReviewedService (AccountWide), MuteService (AccountWide/MyWork), SelfWatchService
  (Watch: AccountWide/ParticipationEntry, Unwatch: AccountWide/MyWork) all migrated; every
  role-to-scope selection explicit with unknown-role 403 failsafe. Obsolete
  GetRequestForUpdateAsync removed from interface, EF implementation, and create-business-request
  fake. 8 new G4c integration tests; 2 existing tests updated (409→404 for Operator
  invisible-mute and Operator self-assign to another eligible Responsible's request).
- **G4c corrective — 1013 tests.** Restored Operator already-assigned guard in
  ManageResponsibleService.SetAsync. A Watching Operator reaches an assigned request via the
  MyWork branch of ParticipationEntry; without the guard they could self-assign over an active
  eligible Responsible. Check verifies eligibility of the existing Responsible (stale/ineligible
  does not block). Regression test: Operator Watching + Owner Responsible → 409, no side
  effects. commit 1534f0c.
- **G4d complete — 1060 tests.** GetKeepRequestListService scope selection: Owner/Admin/Viewer →
  AccountWide, Operator → MyWork, unknown → 403; view=unassigned is Owner/Admin-only (Operator
  receives 403). GetAvailableKeepRequestsService (new): Operator-only, cursor-paginated Available
  summary with privacy-limited contract. KeepAvailableRequestQueryBinding (new): strict query
  binding rejecting unknown parameters. KeepRequestListPersistence: GetAvailableAsync with DB
  161-char prefix projection and DescriptionWasTruncated flag. KeepRequestErrors:
  RequestListUnknownParameter added, InvalidLimit message neutralized. Program.cs:
  GET /keep/requests/available registered. Unit tests: 5 stale Operator-unassigned affordance
  tests removed, 5 scope-selection tests added. Integration: KeepRequestListQueryApiTests updated
  (5 new + 1 replaced), KeepRequestAvailableApiTests (38 tests: role gates, exact field-set,
  excluded-field absence, terminal/non-terminal boundaries, Watching non-blocking, detached
  Responsible, Resolved inclusion, OffSeason suppression, preview at 159/160/161 scalars and
  whitespace/emoji, pagination, cursor tamper/cross-user/duplicate-parameter, limit/binder
  validation, Available count == viewCounts.unassigned, unavailable detail 404, no-side-effect
  proof with participant/event/request checks). build-log/052.
- **G4e-1 complete — 1106 tests.** Added the pure shared action policy, canonical deny-all actor
  context/decision, centralized detail-action metadata mapping, and read-surface adoption in detail,
  list, and business-create responses. Corrected list quick/contact actions to consume the shared
  decision, including OffSeason and feedback-review suppression. ADR-326–329; build-log/053;
  commit 74f26a8.
- **G4e-2 complete — 1107 tests.** Status-change, business-update, internal-note, acknowledgement,
  and external-contact services derive `AvailableActionsMetadata` from the shared policy evaluated
  post-mutation. No coarse pre-mutation rejection; guard ordering, terminal/no-op behavior, and
  endpoint errors preserved. build-log/054; commit f4e2d28.
- **G4e-3 complete — 1109 tests. G4/G4e CLOSED.** Migrated the final five mutation services
  (responsible, watcher, self-watch, mute, feedback-review) to the shared policy post-mutation.
  Removed superseded mapper helpers (`ComputeAllowedStatuses`, `CanAcknowledgeAttention`,
  `CanMarkFeedbackReviewed`) after proving zero callers; corrected the stale
  `KeepRequestActionDecision` XML claim. Review correction: Available list now gates per-row
  `CanWatch` on an internal SQL `CurrentUserIsWatching` flag so an already-Watching Operator is
  denied the watch affordance (policy parity; ADR-328 updated). Zero `new AvailableActionsMetadata`
  outside the central mapper. build-log/055.

Historical Phase 8-B1 through B5 Session 5 completion detail is intentionally omitted here. Use
build logs 025–046 and ADR-084–294 when needed; do not reload those histories for G4 unless a
specific signature or locked behavior requires a targeted read.

---

## G4 — Shared Request-Row and Action Authorization — COMPLETE

G4 established account-wide, MyWork, ParticipationEntry, and Available row authorization; migrated
detail, mutation, list/count, and Available surfaces; and centralized action metadata in the shared
policy. Final G4 baseline: 1109 tests. Authoritative detail: ADR-319–329 and build-logs/052–055.

## G5 — Entity-Wide KeepRequest Optimistic Concurrency — COMPLETE

G5 added the opaque application-managed request version, strict mutation header contract, version
exposure, atomic rotation across request/event/participant/customer writes, stable stale/save-race
conflicts, and three two-context race proofs. Final G5 baseline: 1182 tests
(619 unit · 14 architecture · 549 integration). Commits: `c7435b2`, `2c8390a`, `7b4796f`,
`ab6399b`, `4998aab`, `19099e3`, `9c2df85`, `bb8010e`, `3fb154f`, `0a9d570`.
Authoritative detail: ADR-330–335 and build-log/056.

## G6 — Cancelled Customer-Page Expiry Correction — COMPLETE

**Finding:** GAP-011 · **Decision:** ADR-297 (implemented in part)
**Commit:** `c120702`

- `KeepRequest.ChangeStatus` sets `ExpiresAtUtc = nowUtc.AddDays(30)` on Cancelled transitions
  via a named `CancelledPageRetentionDays = 30` constant. Closed expiry deferred to Session 6.
- No persistence, service, API, or migration changes required; existing G5 commit path and
  `ExpiresAtUtc` mapping already handled atomicity and response exposure.
- 6 new tests: 2 unit (success + rejection), 2 integration status (success lifecycle + stale-409),
  2 integration customer-page (ADR-120 defensive non-terminal theory).
- Full suite: 1188 tests (621 unit · 14 architecture · 553 integration). build-log/057.

## G7 — Feedback Review Hardening — IN PROGRESS

**Findings:** GAP-017, GAP-018, GAP-020 · **Decision:** ADR-300 reaffirms ADR-263

G7 is split into three independently compiling sessions:

- **G7a (complete; committed `a9e87bd`):** block generic acknowledgement of `UnresolvedFeedback` and
  remove its action affordance.
- **G7b (complete):** Owner/Admin outbound external-contact exception for exact
  Closed unresolved-feedback review state, including shared policy and list/detail affordance parity.
- **G7c (pre-work required after G7b):** positive-comment visibility correction, ADR/deferred-ledger
  reconciliation, build-log/058, full regression, and G7 completion.

### Locked G7-wide boundaries

- Mark feedback reviewed remains the only operation that completes unresolved-feedback review.
- The generic acknowledgement exception uses a dedicated 409
  `KeepRequest.AttentionRequiresFeedbackReview`; it must not masquerade as `AttentionNotRaised`.
- The G7b contact exception is outbound-only, matching the gap audit's direct-follow-up posture and
  DEF-064's exact candidate. It supports existing Phone/SMS/Email validation and remains an internal
  `ExternalContactLogged` event. Only Owner/Admin may use it, and only while the request is Closed
  with submitted negative, unreviewed feedback and active `UnresolvedFeedback` attention.
- Closed feedback follow-up updates factual `LastBusinessActivityAt`, but does not reopen/change
  status, count/set first response, clear/replace attention, mark feedback reviewed, notify the
  customer, or expose a customer-visible event. All other Closed cases and every Cancelled case
  retain `TerminalState`; Operator direct attempts remain forbidden.
- G7b must centralize the exact active-review predicate in Core so domain, service, and shared action
  policy cannot drift. List/detail affordances consume the shared policy; hard-coded terminal or
  post-close suppression must not hide the newly valid Owner/Admin contact action.
- Positive feedback comments are visible to every authenticated role that passes G4 row access.
  Negative comments remain Owner/Admin-only; Operator/Viewer retain safe feedback boolean/timestamp
  metadata. `FeedbackCommentVisible` continues to distinguish absent-but-readable from redacted.
  Feedback review notes remain Owner/Admin-only regardless of positive-comment visibility.
- ADR-282 is not implemented: navigation belongs to Session 6. G7c corrects its status/source but
  adds no navigation DTO, route, service, or second mutation command.

### G7a — Generic acknowledgement hardening — COMPLETE

- `KeepRequestErrors`: added `AttentionRequiresFeedbackReview` (code `KeepRequest.AttentionRequiresFeedbackReview`).
- `KeepRequest.AcknowledgeAttention`: rejects `AttentionReason.UnresolvedFeedback` with new error before any mutation.
- `KeepRequestActionPolicy`: `CanAcknowledgeAttention` excludes `UnresolvedFeedback` attention reason.
- `ErrorHttpMapper`: new error mapped to 409 Conflict.
- Unit tests: replaced stale ack-clears-feedback test with blocked-path + state-unchanged + MarkFeedbackReviewed-still-succeeds proof; split ADR-111 terminal test to separate UnresolvedFeedback (false) from other reasons (true).
- Integration: added G7a seed (Closed + negative feedback) and `AcknowledgeAttention_UnresolvedFeedbackAttention_Returns409AndLeavesStateUnchanged` (version/attention/events unchanged, affordances correct).
- Full suite: 1190 tests (622 unit · 14 architecture · 554 integration). Build clean. `git diff --check` clean.
- Commit: `a9e87bd`.

### G7b — Closed unresolved-feedback outbound contact exception — COMPLETE

**Purpose:** fix GAP-018 / DEF-064 without reopening general terminal external-contact logging.
Owner/Admin may log outbound external contact only while a request is in the exact active
unresolved-feedback review state. This is a review-follow-up affordance, not a reopen path.

**Files to modify:**

1. `src/OpHalo.Keep.Core/Entities/KeepRequest.cs`
   - Add one centralized O(1) predicate, preferably `public bool HasActiveUnresolvedFeedbackReview`,
     with the exact rule:
     - `Status == KeepRequestStatus.Closed`
     - `FeedbackSubmittedAtUtc.HasValue`
     - `FeedbackWasResolved == false`
     - `!FeedbackReviewedAtUtc.HasValue`
     - `AttentionLevel != AttentionLevel.None`
     - `AttentionReason == AttentionReason.UnresolvedFeedback`
   - Add a dedicated outbound-only domain method for the exception, e.g.
     `LogClosedFeedbackFollowUpExternalContact(...)`.
   - Reuse/extract existing outbound channel/outcome/summary validation from
     `LogOutboundExternalContact`; do not loosen normal terminal behavior.
   - Effects for the exception:
     - create the same internal `ExternalContactLogged` event shape;
     - update `LastBusinessActivityAt = nowUtc`;
     - `ExternalContactSetFirstResponse == false`;
     - `ExternalContactClearedAttention == false`;
     - do not change status, `TerminatedAtUtc`, `ExpiresAtUtc`, feedback review fields, attention
       fields, first-response fields, participants, or customer-visible events.
   - `LogOutboundExternalContact` and `LogInboundExternalContact` remain terminal-blocked for normal
     terminal requests. Cancelled always remains blocked.

2. `src/OpHalo.Keep.Application/Requests/KeepRequestActionPolicy.cs`
   - `CanLogExternalContact` becomes true when either:
     - request is non-terminal; or
     - actor is Owner/Admin and `request.HasActiveUnresolvedFeedbackReview`.
   - Operator remains false for the terminal exception. OffSeason/read-only still returns `DenyAll`
     before this logic.
   - `CanSendBusinessUpdate`, status transitions, acknowledgement, participation, watch/mute, and
     feedback-review policy are otherwise unchanged.

3. `src/OpHalo.Keep.Application/Requests/LogExternalContactService.cs`
   - Keep current auth, feature, OffSeason, row-auth, version-check, and parse ordering.
   - After row load/version check and direction/channel/outcome validation:
     - If `request.HasActiveUnresolvedFeedbackReview` and direction is outbound:
       - Owner/Admin call the new closed-feedback-follow-up domain method.
       - Operator returns `auth.forbidden` / 403, even if row-visible through participation.
     - If direction is inbound, keep the existing terminal-domain path so it returns
       `KeepRequest.TerminalState` / 409.
     - All non-exact Closed cases and every Cancelled case keep existing terminal behavior.
   - Commit remains the existing G5 versioned `CommitAsync` path with the exhaustive switch.

4. `src/OpHalo.Keep.Application/Requests/GetKeepRequestListService.cs`
   - Replace the local post-close predicate with `request.HasActiveUnresolvedFeedbackReview`.
   - Remove hard-coded terminal/post-close suppression where it hides a policy-approved Owner/Admin
     contact action.
   - For exact post-close rows, Owner/Admin quick actions should include:
     - `open_detail`
     - `review_feedback` when `CanMarkFeedbackReviewed`
     - `contact_customer` when contact methods exist and `CanLogExternalContact`
   - Contact actions for exact post-close rows should be emitted only when
     `actionDecision.CanLogExternalContact` is true. OffSeason still suppresses them through
     `CanWrite=false` in the policy decision.
   - Do not add `post_customer_update`, `acknowledge_attention`, status, assignment, watch, or mute
     actions to terminal rows.

5. `src/OpHalo.Keep.Application/Requests/KeepRequestDetailMapper.cs`
   - Detail `ContactActions` must also respect shared action policy. Use the existing
     `availableActions.CanLogExternalContact` value rather than only `canOperate`.
   - Result: Owner/Admin exact review state shows contact launchers; other terminal states and
     Operator/Viewer do not. `AvailableActions.CanLogExternalContact` remains policy-derived.

**Tests to add/update:**

1. `tests/OpHalo.UnitTests/Keep/KeepRequestExternalContactTests.cs`
   - Add domain success proof for `LogClosedFeedbackFollowUpExternalContact` on exact
     Closed + negative/unreviewed + active `UnresolvedFeedback`:
     - returns `ExternalContactLogged`;
     - updates `LastBusinessActivityAt`;
     - does not set first response;
     - does not clear attention;
     - leaves status/feedback/review/attention fields unchanged.
   - Add domain rejection proofs for ordinary Closed, positive feedback, already reviewed, cleared
     attention, and Cancelled.
   - Preserve existing tests proving normal `LogOutboundExternalContact` remains terminal-blocked.

2. `tests/OpHalo.UnitTests/Keep/KeepRequestActionPolicyTests.cs`
   - Add G7b policy tests:
     - Owner exact active review: `CanLogExternalContact == true`.
     - Admin exact active review: true.
     - Operator exact active review: false.
     - ordinary Closed / positive feedback / already reviewed / cleared attention / Cancelled: false.
     - OffSeason/read-only context: false via `DenyAll`.

3. `tests/OpHalo.UnitTests/Keep/KeepRequestListServiceTests.cs`
   - Add/adjust post-close list tests:
     - Owner exact active review with phone/email gets `review_feedback`, `contact_customer`, and
       concrete contact actions.
     - Operator or OffSeason exact active review gets no contact action and no review action.
     - ordinary Closed history rows still have no contact actions.

4. `tests/OpHalo.IntegrationTests/Api/KeepRequestExternalContactApiTests.cs`
   - Add exact G7b seeds: Closed + negative feedback + active `UnresolvedFeedback` for Owner/Admin
     success; a row-visible Operator variant if needed to prove 403 rather than 404.
   - Add Owner/Admin outbound success test:
     - sends current `X-Keep-Request-Version`;
     - receives 200 and rotated version;
     - response detail has `availableActions.canLogExternalContact == true`;
     - event timeline contains internal `external_contact_logged` with `externalContactSetFirstResponse=false`
       and `externalContactClearedAttention=false`;
     - fresh DB read proves status, feedback review fields, attention fields, first-response fields,
       and customer-visible page are unchanged except `LastBusinessActivityAt`.
   - Add Operator exact-state direct attempt returns 403.
   - Add inbound exact-state attempt returns 409 `KeepRequest.TerminalState`.
   - Keep/update existing ordinary terminal request test: ordinary Closed still returns 409
     `KeepRequest.TerminalState`.

5. `tests/OpHalo.IntegrationTests/Api/KeepRequestDetailB4Tests.cs` or the narrow existing detail
   fixture that owns Closed unresolved-feedback detail coverage.
   - Assert Owner/Admin exact active review detail has `availableActions.canLogExternalContact == true`
     and contact launchers.
   - Assert Operator/Viewer detail does not expose contact launchers for the same terminal review state.

6. `docs/session-log.md`
   - Mark G7b complete only after tests are green; do not add build-log/058 yet unless G7 is being
     completed. G7c owns the final G7 ledger/build-log sweep.

**G7b result:**

- `KeepRequest`: `HasActiveUnresolvedFeedbackReview` property (exact 6-field predicate); extracted
  `ValidateOutboundContact` private helper; new `LogClosedFeedbackFollowUpExternalContact` domain
  method (outbound-only, `setFirstResponse=false`, `clearedAttention=false`, only updates
  `LastBusinessActivityAt`). `LogOutboundExternalContact` refactored to use extracted helper.
- `KeepRequestActionPolicy`: `CanLogExternalContact = isNonTerminal || (isOwnerAdmin && request.HasActiveUnresolvedFeedbackReview)`.
- `LogExternalContactService`: outbound branch gates on `HasActiveUnresolvedFeedbackReview`; Owner/Admin
  call new domain method; Operator returns 403; inbound path unchanged (TerminalState).
- `GetKeepRequestListService`: `isPostClose` replaced with `r.HasActiveUnresolvedFeedbackReview`;
  `BuildQuickActions` post-close branch adds `contact_customer` when policy approves; `BuildContactActions`
  guard removes hard-coded `IsTerminal || isPostClose` (policy now owns terminal filtering).
- `KeepRequestDetailMapper`: `BuildContactActions` uses `availableActions.CanLogExternalContact` instead of `canOperate`.
- 2 existing list unit tests updated (missing `FeedbackSubmittedAtUtc`/`FeedbackWasResolved` fields on the predicate).
- 1 existing B5 integration test updated (`Closed_unresolved_feedback_row_exposes_only_review_feedback_and_open_detail` → now includes `contact_customer` and call action for Owner).
- 30 new tests (12 unit domain/policy/list + 4 EC integration + 4 detail B4 + 1 B5 update).
- Full suite: **1220 tests (643 unit · 14 architecture · 563 integration).** Build clean. `git diff --check` clean.

**Do not touch in G7b:**

- No new API route, DTO field, command, migration, persistence method, notification, customer-visible
  event, customer reply/follow-up delivery, status reopen, review completion, or navigation DTO.
- Do not mark feedback reviewed; only `MarkFeedbackReviewedService` completes review.
- Do not change positive-feedback comment visibility; G7c owns that.
- Do not change ADR/deferred statuses or create build-log/058 in this batch unless explicitly asked.

**Verification target:**

- Focused unit/integration suites for the five touched test files.
- Full unit + architecture.
- Run focused external-contact/list/detail integration tests.
- `git diff --check`.
- Expected count starts from G7a baseline: 1190 tests (622 unit · 14 architecture · 554 integration).

**Open decisions:** none. Product behavior is locked by G7-wide boundaries above.

## G8 — Edge Hardening, Ledger Reconciliation, Completion Gate — PLANNED

**Findings:** GAP-012, GAP-013, GAP-021; GAP-016 remains post-Session-6/pre-notification.

- Configure trusted forwarded-header/client-IP handling for Cloudflare → Railway/application;
  untrusted peers cannot choose rate-limiter partitions.
- Prove public-intake 10/minute, zero-queue, 429, partition isolation, and spoof resistance in a
  production-like test host.
- Add founder/internal targeted-abuse runbook without exposing raw customer IPs to businesses.
- Prove raw intake/page tokens do not enter application logs, traces, analytics, exceptions, or
  friction reports; document Cloudflare/Railway configuration that code cannot enforce.
- Decide customer page-token at-rest protection without breaking authorized staff re-sharing.
  Current detail returns the retrievable token. Explicitly choose and prove hash plus rotation/one-
  time disclosure, application encryption/key management, or documented accepted retrievable
  storage; define migration, lookup, recovery, and rotation semantics.
- Reconcile ADR/deferred statuses only after behavior exists.
- Run migration-from-zero, build, unit, architecture, integration, and full-suite gates and record
  exact counts/files/migrations/external deployment checks.

G8 explicitly excludes notifications, realtime, frontend UI, Spam/Test implementation, Turnstile,
SMS verification, and a general abuse platform.

---

## Active Carry-Forward Boundaries

- One durable public intake link; no ordinary pause/resume. Exceptional replacement is
  transactional and warns about stale links.
- Public-intake abuse posture before pilot: bounded validation, trusted-IP/rate-limit proof,
  Spam/Test classification in its planned V1 slice, token-safe logs, and internal emergency path.
- Hashed-source blocking, adaptive bot challenges, anomaly detection, and phone verification remain
  post-pilot/V1.1 unless evidence requires earlier work.
- Terminal customer pages expire 30 days after Closed/Cancelled; active and Resolved do not.
- Mobile is the on-the-road Operator surface; PWA is the Owner/Admin operational surface. Backend
  authorization remains identical regardless of client.
- No platform SMS is sent until consent/compliance posture is reviewed.
- Keep remains fresh-not-realtime for V1: refetch after writes, focus/resume sync, pull-to-refresh,
  bounded polling, and later push for urgent off-screen work.

## Operational Watch-Outs

- No GitHub remote is configured.
- Integration tests reset PostgreSQL schema and run migrations.
- Testing environment intentionally skips runtime rate limiting; G8 must use a production-like host.
- Deployment requires correct Cloudflare/Railway trusted-proxy and token-redaction configuration.
- Persistent local PostgreSQL setup/migration/smoke/reset runbook remains GAP-016 after Session 6 and
  before notifications.
