# Session Log — OpHalo Foundation

**Last updated:** 2026-06-22 (G5a complete; G5b operational-mutation enforcement is next)
**Branch:** `main` (no remote yet)
**Current baseline:** 1109 tests (619 unit · 14 architecture · 476 integration); G5a added 121 focused
integration tests across G5a-1 and G5a-2 — not yet rolled into a full-suite baseline.
**Next free ADR:** ADR-336
**Next batch: G5b — operational-mutation expected-version enforcement.** Decisions are locked
(ADR-330–335); perform targeted discovery of mutation handler signatures, present the exact file-level
gate, and stop before coding.

---

## Session Protocol

This file is the current execution brief, not the historical build archive. Completed implementation
detail lives in `docs/build-log/`; authoritative decisions live in
`docs/decisions/decision-index.md`.

For every implementation slice:

- inspect current signatures and failure modes before calling existing code;
- present the mandatory file/design gate before writing unless that exact slice is marked
  `Pre-work complete` below;
- keep the slice within roughly 8–10 files and one coherent concern;
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

**Finding:** GAP-003
**Decisions:** ADR-319–325

### Why G4 exists

Current detail and tracked mutation loads are only account-scoped. Operator Default and Needs
Attention lists/counts are also broader than the locked participation policy. A same-account
Operator can therefore discover or mutate another Operator's assigned work through direct IDs.

G4 protects:

- customer contact information and sensitive request context;
- business-internal notes, history, workload, and complaint information;
- assignment, response, attention, completion, and impact-metric integrity;
- the account from excessive exposure after a stolen session, disgruntled employee, or code defect.

### Locked authorization model

Authorization has three separate server-authoritative gates:

1. **Account/feature/role permission** — may this authenticated active member use the feature?
2. **Request-row visibility** — may this person discover/open this request?
3. **Action permission** — may this person perform this exact action in the current state?

Passing a broader gate never implies passing a narrower one. UI action metadata is advisory;
endpoints re-evaluate current server state.

### Locked role and row behavior

- **Owner/Admin:** account-wide operational visibility under existing account, feature, and
  permission gates. Dashboard/list counts continue to include all account work.
- **Viewer:** trusted account-wide read-only observer under existing gates. Viewer receives no write
  affordances and remains subject to sensitive-field redaction, including negative-feedback policy.
  Future account UI must clearly explain that Viewer can read account-wide customer/request data.
- **Operator My Work:** full detail only with active eligible Responsible or Watching participation.
- **Operator Default and Needs Attention:** My Work only.
- **Operator Available:** separate intentional discovery surface containing active, non-terminal,
  eligible, effectively unassigned requests. It does not enter Default or Needs Attention.
- **Operator other assigned work:** no row access; direct IDs return `404 Not Found`.
- **Cross-account IDs:** `404 Not Found`.
- **Stale/detached/ineligible participation:** grants no access. An inactive, removed, invited, or
  Viewer Responsible does not prevent the request from being effectively Available.
- **Unknown/future roles:** fail closed; never use `role != Operator` as account-wide authorization.

### Locked Available contract

Route: `GET /keep/requests/available` with a dedicated response contract. Do not return a
role-dependent DTO from `view=unassigned`. Existing `view=unassigned` remains the Owner/Admin
full-summary surface.

Each Available item contains:

- `RequestId`
- `ReferenceCode`
- `CustomerName` — intentionally included so a field Operator can recognize who is waiting
- `Status`
- `CreatedAtUtc`
- `AttentionSinceUtc`
- `NextAttentionAtUtc`
- `PriorityBand`
- `AttentionLevel`
- bounded server-generated `DescriptionPreview` (up to 159 Unicode scalars + `…` when truncated)
- `CanSelfAssign`
- `CanWatch`

Explicitly excluded until participation:

- customer phone and email;
- full description;
- internal notes and full timeline;
- customer page token/link;
- feedback details;
- full participation and notification information;
- normal detail/list action sets.

### Locked policy architecture

- Application services explicitly choose a capability-oriented `KeepRequestVisibilityScope`:
  - `AccountWide`
  - `MyWork`
  - `ParticipationEntry` — narrow scope only for Operator self-assign/watch; combines already-
    participating work with active eligible Available work so existing participation can remain
    idempotent without granting normal detail/mutation access.
- Infrastructure owns one composable `KeepRequestRowQueryFactory` translating the selected scope,
  server-derived `AccountId`, and current `AccountUserId` into EF queries.
- This is explicit query composition, not an implicit/global EF query filter.
- Participation eligibility joins the participant to the same-account `AccountUser` and requires
  active membership plus Owner/Admin/Operator role.
- Related events, participants, customer-sensitive projections, and tracked mutation entities are
  not loaded until the parent request passes row authorization.
- `KeepRequestActionPolicy` is a standalone Application policy. It owns the action-rule primitives
  consumed by mutation services and `AvailableActionsMetadata` construction. Authorization does not
  belong in `KeepRequestDetailMapper`.
- The old account-only mutation loader is marked obsolete during staged migration and removed after
  its final caller migrates. Any remaining caller blocks G4 completion.

### Correct mutation scopes

| Service/path | Owner/Admin | Operator |
|---|---|---|
| AcknowledgeAttention | `AccountWide` | `MyWork` |
| AddBusinessUpdate | `AccountWide` | `MyWork` |
| AddInternalNote | `AccountWide` | `MyWork` |
| ChangeKeepRequestStatus | `AccountWide` | `MyWork` |
| LogExternalContact | `AccountWide` | `MyWork` |
| ManageResponsible.Clear | `AccountWide` | forbidden upstream |
| ManageResponsible.Set | `AccountWide` | `ParticipationEntry`, self-assign rules only |
| ManageWatcher add/remove another user | `AccountWide` | forbidden upstream |
| MarkFeedbackReviewed | `AccountWide` | forbidden upstream |
| Mute/Unmute | `AccountWide` | `MyWork` |
| SelfWatch.Watch | `AccountWide` | `ParticipationEntry` |
| SelfWatch.Unwatch | `AccountWide` | `MyWork` |

ADR-105/111 explicitly allow an eligible Operator to acknowledge attention; it is not an
Owner/Admin-only action. G7 later blocks the generic acknowledgement path specifically for active
`UnresolvedFeedback`.

### G4 implementation slices

G4 is complete only after every slice is committed, all old loaders/helpers have zero callers, the
exit inventory names every migrated path, and the full suite is green.

#### G4a — Policy/query foundation and detail-row authorization — COMPLETE (commit e57522a, 995 tests)

#### G4b — Consequential Operator mutation-load migration — COMPLETE (1004 tests)

#### G4c — Participation/admin mutation-load migration and old-loader removal — COMPLETE (commit e82ebae, 1012 tests)

#### G4d — My Work lists/counts and dedicated Available surface — COMPLETE (1060 tests, build-log/052)

#### G4e — Shared action-policy migration and completion gate

**COMPLETE 2026-06-21. G4e-1 (74f26a8) · G4e-2 (f4e2d28) · G4e-3 (build-log/055). ADR-326–329. G4/G4e closed.**

The policy is a pure, deterministic, O(1) Application-layer policy with no EF, current-user,
HTTP, network, or clock dependency. It accepts a request plus this actor context:

- explicit `AccountUserRole`;
- `CanWrite`, already derived from operate permission and account operating mode;
- current active `ParticipationType?`;
- `NotificationsEnabled?`, meaningful only with active participation.

Unknown/future roles, `CanWrite == false`, and inconsistent actor context return one immutable
deny-all decision. Viewer is always deny-all even if a caller incorrectly supplies `CanWrite=true`.
Row authorization remains separate and evaluating this policy never grants row access.

The structured decision owns these primitives:

- change status, send business update, add internal note, acknowledge attention, and log external
  contact;
- assign responsible, self-assign responsible, clear responsible, and manage watchers;
- watch, unwatch, mute, and unmute;
- mark feedback reviewed;
- an ordered enum-valued allowed-status list.

One shared Application mapper converts the decision to `AvailableActionsMetadata` and status slugs.
Services must not reconstruct those 11 response fields independently. Internal-only responsibility
and watcher-management capabilities do not expand the existing detail response contract. Lists
consume the decision directly because their quick-action contract differs.

##### Locked action matrix

- Owner/Admin receive generally eligible write capabilities; Operator receives operational and
  self-participation capabilities; Viewer and unknown roles receive none.
- Non-terminal Owner/Admin/Operator work may change status, send business updates, log external
  contact, and—when attention exists—acknowledge attention. Internal notes remain available on
  terminal requests. Attention cleanup also remains available on terminal requests when active
  attention exists, preserving ADR-111; G7 later blocks generic acknowledgement specifically for
  `UnresolvedFeedback`.
- Owner/Admin may assign or clear responsibility and manage another user's watcher participation on
  non-terminal requests. Operator may only self-assign. Self-assignment remains subject to row,
  target-eligibility, effectively-unassigned/idempotency, and active-eligible-existing-responsible
  guards; the policy boolean is not permission to steal another user's assignment.
- On non-terminal requests, an actor with no active participation may watch; an active Watcher may
  unwatch; an active Responsible or Watcher may mute/unmute according to the current notification
  flag. Responsible and Watching remain mutually exclusive. Missing/inconsistent notification state
  fails closed.
- Feedback review requires Owner/Admin, write permission, Closed status, submitted explicitly
  unresolved feedback, no prior review, and active `UnresolvedFeedback` attention.
- Closed/Cancelled disable status change, business update, external contact, responsibility,
  watcher, and notification actions. They do not create a blanket deny-all because internal-note,
  attention-cleanup, and qualifying feedback-review behavior is deliberate.

`AllowedStatuses` means actual transitions and excludes the current status:

| Current | Allowed targets |
|---|---|
| Received | Scheduled, InProgress, PendingCustomer, Resolved, Cancelled |
| Scheduled | InProgress, PendingCustomer, Resolved, Cancelled |
| InProgress | Scheduled, PendingCustomer, Resolved, Cancelled |
| PendingCustomer | Scheduled, InProgress, Resolved, Cancelled |
| Resolved | InProgress, PendingCustomer, Closed, Cancelled |
| Closed / Cancelled | none |

Same-status command semantics do not use this list as a rigid gate: no-message remains a successful
no-op (including terminal), and a same-status message remains permitted on non-terminal work.

Spam/Test remain separate future V1 classifications under DEF-061, not lifecycle statuses and not
new G4e capabilities. `Test` supports learning, onboarding, employee training, and demos; `Spam`
covers nonsense, abusive, inappropriate, accidental, or otherwise non-operational public intake.

##### Consumption and list boundaries

- Existing authentication, feature, operating-mode, role, and row gates retain their established
  order.
- Domain methods remain authoritative for executable mutation eligibility, payload and target
  validation, transition rules, same-status/no-op semantics, guard ordering, and exact stable errors.
  G4e-2 must not add a coarse pre-mutation policy-boolean rejection that changes established 409 or
  validation outcomes to generic 403, or duplicates domain validation in Application.
- After a successful operational mutation or valid no-op, evaluate `KeepRequestActionPolicy`
  against the resulting request and actor-participation state, then construct response metadata
  through the shared mapper. The action decision remains advisory metadata rather than an execution
  lock; G5 handles stale concurrent intent.
- Application-only structural gates and target-specific service checks remain explicit where the
  domain does not own them, but they must preserve established row visibility and error contracts.
- List customer-update, acknowledgement, feedback-review, assignment, self-assignment,
  notification, and contact affordances consume matching decision properties. `OpenDetail` remains
  row/read authorization. Phone/email presence and first-response-overdue quick-action suppression
  remain presentation conditions; ranking, severity, labels, and row context remain list logic.

##### Bounded implementation batches

1. **G4e-1 — foundation and read surfaces — COMPLETE (commit 74f26a8, 1106 tests,
   build-log/053):**
   `KeepRequestActionContext`, `KeepRequestActionDecision`, `KeepRequestActionPolicy` added.
   `KeepRequestDetailMapper.ToAvailableActionsMetadata` added (maps decision → 11-field response
   contract). `GetKeepRequestDetailService`, `GetKeepRequestListService`, and
   `CreateBusinessRequestService` migrated to policy. `BuildQuickActions` and `BuildContactActions`
   in `GetKeepRequestListService` corrected: `ReviewFeedback` gated on `CanMarkFeedbackReviewed`,
   `ContactCustomer` and `post_customer_update` gated on `CanLogExternalContact`/`CanSendBusinessUpdate`,
   `BuildContactActions` updated to accept and check the decision.
   Unit tests: 46 new — 39 policy matrix (`KeepRequestActionPolicyTests`); 3 list parity
   (OffSeason suppresses all write actions, isPostClose owner shows/suppresses review_feedback in
   OffSeason); 1 business-create parity (AvailableActions maps shared decision); 3 detail parity
   (`KeepRequestDetailServiceTests`: AllowedStatuses excludes current status for Received,
   InProgress, Closed). Old mapper helpers retained — still needed by G4e-2/3 mutation callers.
2. **G4e-2 — operational mutations — COMPLETE (1107 tests, build-log/054):** status
   change, business update, internal note, acknowledgement, and external-contact services now derive
   `AvailableActionsMetadata` from the shared policy evaluated post-mutation. No coarse pre-mutation
   capability rejection added; domain guard ordering, terminal same-status/no-op behavior, validation
   precedence, and endpoint errors preserved. New regression: terminal-transition response metadata
   reflects resulting Closed state (isolated seed; asserts a real status_changed event).
3. **G4e-3 — participation, feedback, and completion — COMPLETE (1109 tests, build-log/055):**
   responsible, watcher, self-watch, mute, and feedback-review response metadata migrated to the
   shared policy post-mutation; the three superseded mapper helpers removed after zero-caller proof;
   `KeepRequestActionDecision` XML corrected. G4 path/action inventory recorded in build-log/055;
   zero `new AvailableActionsMetadata` outside the central mapper. **G4/G4e closed.**

##### G4e-3 file-level implementation gate — APPROVED

No product or architecture decisions remain open for this batch. ADR-326–329 and the clarified
ADR-328 boundary apply:

- preserve every existing authentication, account, feature, role, row-scope, terminal, target,
  idempotency, and domain guard in its current order with its current stable error;
- do not replace pre-row role checks or target-specific service/domain rules with coarse action-policy
  booleans and do not add a new generic 403 path;
- after a successful participation/feedback mutation or valid no-op, use the already reloaded
  participant projection and resulting request state to construct `KeepRequestActionContext` with
  `CanWrite=true`, evaluate `KeepRequestActionPolicy`, and map through
  `KeepRequestDetailMapper.ToAvailableActionsMetadata`;
- retain `CanSelfAssignResponsible`, `CanClearResponsible`, and `CanManageWatchers` as coarse shared
  policy vocabulary. They do not replace row-before-load or target-specific execution checks and do
  not expand the current `AvailableActionsMetadata` contract;
- after all five callers migrate, remove `ComputeAllowedStatuses`, `CanAcknowledgeAttention`, and
  `CanMarkFeedbackReviewed` from `KeepRequestDetailMapper` only after proving zero callers;
- remove imports made unused by deleting the inline metadata formulas; make no unrelated cleanup.

Exact files for the bounded batch:

1. `src/OpHalo.Keep.Application/Requests/ManageResponsibleService.cs`
2. `src/OpHalo.Keep.Application/Requests/ManageWatcherService.cs`
3. `src/OpHalo.Keep.Application/Requests/SelfWatchService.cs`
4. `src/OpHalo.Keep.Application/Requests/MuteService.cs`
5. `src/OpHalo.Keep.Application/Requests/MarkFeedbackReviewedService.cs`
6. `src/OpHalo.Keep.Application/Requests/KeepRequestDetailMapper.cs`
7. `src/OpHalo.Keep.Application/Requests/KeepRequestActionDecision.cs` — correct the stale XML
   claim that internal-only capabilities are consumed as mutation execution gates
8. `tests/OpHalo.IntegrationTests/Api/KeepRequestParticipationApiTests.cs` — add post-mutation
   action-metadata assertions to isolated existing self-watch and mute success paths
9. `docs/build-log/055-gap-g4e-shared-action-policy-completion.md` — record G4e-3 and the complete
   G4 path/action inventory, zero-caller proof, corrections, and exact verification
10. `docs/session-log.md` — mark G4/G4e complete, record exact counts/commit state, and make G5 the
    next fresh pre-implementation gate

Existing `KeepFeedbackReviewApiTests` already prove post-review
`canMarkFeedbackReviewed=false`; do not add duplicate coverage unless implementation reveals a real
gap. Final checks must show zero direct `new AvailableActionsMetadata` construction outside the one
central mapper and zero callers of the three removed helpers, followed by build, unit, architecture,
integration, and full-suite verification with exact counts.

##### Final verification gate

- pure policy matrix tests cover role, write state, lifecycle status, attention, feedback,
  participation, notifications, unknown roles, and inconsistent context;
- terminal exceptions and ordered status transitions are explicit, while existing same-status
  command tests remain green;
- service/API tests prove endpoint and post-mutation metadata agreement, including OffSeason;
- list tests prove policy-driven actions and presentation-only suppression;
- zero callers remain for superseded mapper helpers and zero services duplicate
  `AvailableActionsMetadata` construction;
- build, unit, architecture, integration, and full suites pass with exact counts recorded.

---

## G5 — Entity-Wide KeepRequest Optimistic Concurrency — DECISIONS LOCKED

**Finding:** GAP-004

Goal: prevent stale business intent from overwriting newer customer/business state.

### Locked contract and model

- `KeepRequest.ConcurrencyVersion` is a required application-managed PostgreSQL `uuid`, represented
  in .NET as `Guid`. New requests receive `Guid.NewGuid()`; the API exposes the opaque value as
  `version`. Clients compare and return it but never interpret ordering or meaning. Do not use
  PostgreSQL `xmin`, timestamps, incrementing integers, database-generated row versions, triggers,
  or a database default.
- The migration adds the column nullable, gives every existing row its own nonempty
  `gen_random_uuid()`, then makes it non-null. EF maps it with `IsConcurrencyToken()` and
  `ValueGeneratedNever()`. No index is required.
- The request row, its timeline events, and its participants form one concurrency aggregate. Every
  successful non-no-op write rotates the request version: status and lifecycle changes, business
  updates, notes, attention acknowledgement, external contact, customer messages, feedback
  submission/review, responsible/watcher/self-watch changes, and mute/unmute. Event-only and
  participation-only writes update the tracked request in the same atomic commit. There are no
  current append-only exclusions.
- Reads, failed writes, and valid idempotent no-ops do not rotate the version. Creates require no
  expected-version header and return their initially generated version.

### Locked API and error behavior

- Existing-request mutations require exactly one `X-Keep-Request-Version` header. A dedicated API
  helper trims surrounding whitespace and accepts only the GUID `D` shape; blank, malformed,
  `Guid.Empty`, duplicate/comma-combined, wildcard, quoted, or braced values fail closed. Missing
  returns `400 KeepRequest.ExpectedVersionRequired`; invalid returns
  `400 KeepRequest.ExpectedVersionInvalid`. Application/Core remain unaware of HTTP headers.
- Authenticated detail, standard list rows, Available rows, business-created detail, the customer
  page, and every successful mutation response expose `version`. A changing write returns the new
  version; a no-op returns the unchanged version. Conflict/error responses do not disclose the
  current version. Projections carry `Guid`; API serialization owns the wire representation, and no
  extra query is made solely to retrieve it.
- Authenticated ordering is auth → account/user/feature/OffSeason → role/row visibility → expected
  version comparison → domain validation/mutation → commit. Invisible and cross-account requests
  remain 404 rather than leaking a 409. Customer ordering is public token/account/expiry/OffSeason
  guard → tracked request load → expected version comparison → domain mutation → commit.
- A valid stale token or an EF race maps to `409 KeepRequest.RequestChanged` with message
  `The request changed. Refresh and try again.` The response exposes no current version, actor, or
  state details. Version mismatch occurs before domain validation only after row access succeeds;
  current-version requests retain existing domain error contracts.

### Locked enforcement and persistence behavior

- Application compares the expected version after authorized tracked load. Persistence rotates the
  version immediately before its atomic `SaveChangesAsync`; the original EF concurrency value stays
  in the update predicate. Participation and customer commit methods receive the tracked request so
  they cannot save aggregate changes without rotating it.
- Persistence catches only `DbUpdateConcurrencyException` and returns a small typed commit outcome:
  `Committed` or `Conflict`. Application maps `Conflict` to `KeepRequest.RequestChanged`; unrelated
  database failures continue to propagate. No-op paths do not call commit.
- A conflict produces no request/event/participant/audit side effects and no usable replacement
  token. Never automatically retry, reload-and-reapply, merge, or lock user intent. The client must
  refetch and make a new decision.
- Header parsing is explicit in every existing-request mutation handler rather than hidden in
  middleware, endpoint filters, or `HttpContext.Items`. Existing authorization middleware still
  runs first, and CORS must permit `X-Keep-Request-Version`.

### Required verification

- Prove missing and every invalid header shape return the named 400 errors; a current version
  succeeds and rotates; a valid stale version returns the stable 409 without side effects.
- Prove invisible/cross-account requests remain 404 before comparison, current-version domain
  validation remains unchanged, and idempotent no-ops retain the same version.
- Use separate DbContexts to prove first-writer-wins races, including operator-versus-customer and
  participation-versus-request writes. Prove the losing operation appends no secondary event or
  participant/audit change.
- Verify every designated read/mutation surface returns the correct version and test both a new
  database migration and unique, nonempty backfill of existing rows.

### Bounded implementation batches

1. **G5a — Foundation:** entity/version initialization, EF mapping and migration, errors, strict
   header parser, version response/projection exposure, and focused tests. Split into two reviewable
   sub-batches:
   - **G5a-1 (complete):** `KeepRequest.ConcurrencyVersion` + factory init, EF concurrency-token
     mapping, `KeepG5ConcurrencyVersion` migration (nullable → `gen_random_uuid()` backfill →
     non-null), `ExpectedVersionRequired`/`ExpectedVersionInvalid`/`RequestChanged` errors + HTTP
     mapping, strict `X-Keep-Request-Version` parser (defined, unwired), 15 focused tests. No
     rotation and no mutation-route header enforcement yet. Committed as `c7435b2`; 38 focused
     checks passed (14 parser cases + 1 real-PostgreSQL migration upgrade + 23 existing persistence
     regressions), with clean Api/Infrastructure builds and `git diff --check`.
   - **G5a-2 (complete):** `version` (Guid) added to `KeepRequestDetailResult`, `KeepRequestSummary`,
     `KeepRequestAvailableRow/Item`, and `KeepCustomerPageResult/Context`; mapper and guard wired;
     expired tombstone returns null. `AvailableItemBody` DTO updated; field-set test extended.
     6 new version-contract `[Fact]`s added to 5 existing fixtures (detail, list, available,
     business-create, customer page). Standalone 300-line fixture removed per gate instruction.
     121 focused integration tests pass (15 G5a-1 + 6 G5a-2 version contracts + 100 regression).
2. **G5b — Operational mutations (next):** status, updates, notes, contacts, attention, and
   feedback-review expected-version enforcement and commit handling.
3. **G5c — Participation mutations:** responsible, managed watchers, self-watch, and mute/unmute
   enforcement with atomic request-version rotation.
4. **G5d — Customer writes and completion:** customer messages/feedback, cross-path race tests, full
   regression suite, build log, and completion documentation.

Each batch must compile and pass focused tests independently, with targeted discovery and a fresh
file-level gate before coding. G5 is complete only after all four batches pass. No automatic merge,
distributed lock, event sourcing, or frontend draft recovery is included.

### G5a-2 Claude handoff — targeted discovery only, then stop

G5a-1 is committed and authoritative. Do not reopen its token representation, migration, errors,
or strict header syntax. For G5a-2, inspect only the named declarations/constructor sites below;
do not read the large list service or integration-test files in full.

Required propagation paths:

1. **Authenticated detail and business-created detail (shared result):**
   - `src/OpHalo.Keep.Application/Requests/KeepRequestDetailResult.cs` — add non-null `Guid Version`.
   - `src/OpHalo.Keep.Application/Requests/KeepRequestDetailMapper.cs` — in `ToDetailResult`, map
     `request.ConcurrencyVersion` directly. `POST /keep/requests` already returns this shared detail
     result, so do not create a second business-create DTO or query.
2. **Standard list rows (entity-sourced):**
   - `src/OpHalo.Keep.Application/Requests/KeepRequestSummary.cs` — add non-null `Guid Version`.
   - `src/OpHalo.Keep.Application/Requests/GetKeepRequestListService.cs` — inspect only the
     `ToSummary` method and its constructor call; map `request.ConcurrencyVersion`. Do not alter
     ranking, cursor, filtering, counts, scopes, or persistence signatures.
3. **Operator Available rows (narrow SQL projection):**
   - `src/OpHalo.Keep.Application/Requests/IKeepRequestListPersistence.cs` — add non-null
     `Guid Version` to `KeepRequestAvailableRow`.
   - `src/OpHalo.Keep.Infrastructure/Persistence/KeepRequestListPersistence.cs` — inspect only
     `GetAvailableRequestsAsync` around its final `Select`; project `r.ConcurrencyVersion` in the
     same query.
   - `src/OpHalo.Keep.Application/Requests/GetAvailableKeepRequestsService.cs` — inspect only the
     row-to-item mapping and `KeepRequestAvailableItem`; carry the same non-null `Guid Version`.
     Preserve the narrow projection and do not load a full entity or add a follow-up query.
4. **Public customer page:**
   - `src/OpHalo.Keep.Application/Requests/KeepPublicCustomerContext.cs` — carry the request's
     non-null concurrency version internally.
   - `src/OpHalo.Keep.Application/Requests/KeepPublicCustomerAccessGuard.cs` — inspect only the
     existing request projection/context construction and select `ConcurrencyVersion` there.
   - `src/OpHalo.Keep.Application/Requests/KeepCustomerPageResult.cs` — expose nullable
     `Guid? Version` because the same result type represents an active 200 and safe expired 410.
   - `src/OpHalo.Keep.Application/Requests/KeepCustomerPageMapper.cs` — active results map the
     context version; expired tombstones explicitly set `Version: null`. Do not expose a version in
     the 410 body merely because the guard internally loaded one.

Contract and scope guardrails:

- JSON property name is the normal serializer output `version`; keep `Guid` typed through Core,
  persistence projections, and Application records. Do not pre-format it as a string.
- All values come from the already-loaded entity/projection. No extra query may exist solely to
  retrieve a version.
- Detail-result reuse means existing detail-shaped mutation responses will also serialize the
  current version. That is expected, but G5a-2 must not rotate it, compare it, or require a header.
- Do not wire `KeepRequestVersionHeader`, alter mutation command/service signatures, change commit
  methods, catch concurrency exceptions, or add CORS behavior; those belong to G5b–d.
- Do not add `version` to the public-intake creation receipt (`requestId`, `referenceCode`,
  `pageToken`). The customer obtains an actionable version from the active customer-page response.
- Preserve all existing auth, row scope, safe-tombstone, cursor, action-policy, and serialization
  behavior.

Focused verification must prove the exact wire contract using existing test fixtures rather than a
new broad matrix:

- authenticated detail: `tests/OpHalo.IntegrationTests/Api/KeepRequestDetailTests.cs`;
- business-created detail: `tests/OpHalo.IntegrationTests/Api/KeepBusinessRequestApiTests.cs`;
- standard list row: `tests/OpHalo.IntegrationTests/Api/KeepRequestListQueryApiTests.cs`;
- Available row: `tests/OpHalo.IntegrationTests/Api/KeepRequestAvailableApiTests.cs`;
- active customer page has the persisted nonempty version and expired 410 has JSON `version: null`:
  `tests/OpHalo.IntegrationTests/Api/KeepCustomerPageTests.cs`.

Before editing, present the exact files and constructor/signature changes discovered, identify any
test fakes/record constructor callers that must compile, state focused test commands, and stop for
approval. After implementation, run those focused tests plus affected unit tests, architecture
tests if project references changed, clean builds, and `git diff --check`; do not claim a new global
baseline without a full-suite run.

## G6 — Cancelled Customer-Page Expiry Correction — PLANNED

**Finding:** GAP-011 · **Decision:** ADR-297

- Cancellation atomically sets `ExpiresAtUtc = now + 30 days` with terminal timestamp, attention
  cleanup, and cancellation event.
- Use G4 row policy and G5 expected version; stale cancellation returns 409 with no side effects.
- Cancelled page remains safely readable/read-only until expiry, then established safe 410 context.
- Non-terminal and Resolved pages ignore stale populated expiry defensively.
- Do not pull Session 6 close command/navigation/close-and-next behavior into G6.
- Spam/Test immediate disablement remains its later V1 classification slice.

## G7 — Feedback Review Hardening — PLANNED

**Findings:** GAP-017, GAP-018, GAP-020 · **Decision:** ADR-300 reaffirms ADR-263

- Generic acknowledgement rejects active `UnresolvedFeedback`; metadata hides that action.
- Mark feedback reviewed remains the only review-completion path.
- Owner/Admin may log internal-only external contact on Closed requests only while unreviewed
  negative feedback and active UnresolvedFeedback are present; it does not reopen, notify, count
  first response, clear attention, or mark reviewed. Cancelled remains blocked.
- Positive comments follow G4 row visibility; full negative comments remain Owner/Admin-only.
- Correct ADR-282 status/source; Session 6 still owns feedback navigation.
- Test domain/HTTP bypass prevention, action matrix, contact effects/non-exposure, role comment
  visibility, and OffSeason behavior.

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
