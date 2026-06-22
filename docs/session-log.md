# Session Log — OpHalo Foundation

**Last updated:** 2026-06-22 (G7a implemented; pending commit approval)
**Branch:** `main` (no remote yet)
**Current baseline:** 1190 tests (622 unit · 14 architecture · 554 integration).
**Next free ADR:** ADR-336
**Next batch: G7b — Owner/Admin outbound external-contact exception for Closed unresolved-feedback review state — PRE-WORK REQUIRED.**

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

- **G7a (complete; pending approval):** block generic acknowledgement of `UnresolvedFeedback` and
  remove its action affordance.
- **G7b (pre-work required after G7a):** Owner/Admin outbound external-contact exception for exact
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

### G7a — Generic acknowledgement hardening — COMPLETE (pending commit approval)

- `KeepRequestErrors`: added `AttentionRequiresFeedbackReview` (code `KeepRequest.AttentionRequiresFeedbackReview`).
- `KeepRequest.AcknowledgeAttention`: rejects `AttentionReason.UnresolvedFeedback` with new error before any mutation.
- `KeepRequestActionPolicy`: `CanAcknowledgeAttention` excludes `UnresolvedFeedback` attention reason.
- `ErrorHttpMapper`: new error mapped to 409 Conflict.
- Unit tests: replaced stale ack-clears-feedback test with blocked-path + state-unchanged + MarkFeedbackReviewed-still-succeeds proof; split ADR-111 terminal test to separate UnresolvedFeedback (false) from other reasons (true).
- Integration: added G7a seed (Closed + negative feedback) and `AcknowledgeAttention_UnresolvedFeedbackAttention_Returns409AndLeavesStateUnchanged` (version/attention/events unchanged, affordances correct).
- Full suite: 1190 tests (622 unit · 14 architecture · 554 integration). Build clean. `git diff --check` clean.
- Commit: pending approval.

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
