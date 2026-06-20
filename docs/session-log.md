# Session Log — OpHalo Foundation

**Last updated:** 2026-06-19
**Branch:** `main` (no remote yet)

---

## Planned Session Queue

These are planned implementation sessions, not completion logs. When a session finishes, replace the
planned item with a normal completed entry that records exact files changed, tests run, bugs found,
and carry-forward notes.

**Implementation quality contract for every planned session:**

- read the current code first and follow existing service, persistence, mapper, error, DI, endpoint,
  and test patterns;
- write production-quality code that preserves locked ADR behavior and fail-closed security posture;
- keep scope bounded to the named session and explicitly report any needed work that belongs in a
  later session or deferred topic;
- add focused unit/integration coverage for new contracts, validation, authorization, and regression
  risk;
- run the targeted tests required by the session and the broader suite when feasible;
- self-review the diff before handoff for bugs, inconsistent naming, untested branches, accidental
  visibility expansion, unnecessary schema churn, and deferred work accidentally pulled in;
- report any discovered bugs, gaps, or decision conflicts in `docs/session-log.md` instead of
  silently guessing policy.

## Pre-Session 6 Gap Resolution — IN PROGRESS

**Purpose:** Correct Build Log 047's security, integrity, workflow, and pilot-readiness gaps before
validating or coding Phase 8-B5 Session 6.

**Source of truth:** `docs/build-log/047-pre-session-6-phase-7-session-5-gap-audit.md`

**Next free ADR:** ADR-306 (ADR-301–305 consumed by G1)

**Locked decisions:** ADR-295 through ADR-305. In particular:

- one durable public intake link is provisioned through the Keep setup boundary after the business
  profile exists; it is not rotated automatically;
- Owner/Admin may pause intake without changing the URL; replacement is exceptional and invalidates
  the old URL with a stale-link warning;
- `Spam` and `Test` classification is required before pilot but remains in its later V1 slice; do
  not pull it into these gap sessions;
- terminal customer pages expire 30 days from `Closed`/`Cancelled`; Session 6 implements close and
  the corrected cancellation path applies the same rule; active/Resolved pages never expire;
- remove the undeployed Continuity intake alias and ignored `emailNotificationsEnabled` field;
- Keep relationships use account-safe foreign keys with restricted deletion;
- positive feedback comments follow request-row visibility; negative comments remain
  Owner/Admin-only.

**Baseline:** Session 5D recorded 847/847 passing tests (494 unit, 14 architecture, 339 integration).
G1 raised this to 860 (503 unit, 14 arch, 343 integration).

**Next free ADR:** ADR-306

**Required order:** G1 → G2 → G3 → G4 → G5 → G6 → G7 → G8. Do not begin Session 6 until G8 records the
final green gate. A session may fix a directly blocking defect inside its named scope; broader
discoveries must be recorded and handed forward rather than silently expanding the session.

### Gap Session G1 — Keep schema, identity, and creation semantics — COMPLETE

**Tests:** 860 total (503 unit · 14 arch · 343 integration) — all green
**ADRs:** ADR-301 to ADR-305
**Exit record:** `docs/build-log/048-gap-g1-keep-account-safe-schema.md`
**Migration:** `20260619235301_KeepG1AccountSafeSchema`

**Design decisions (ADR-301–305):**
- D1 (ADR-301): Composite alternate keys on `KeepCustomer (AccountId, Id)`, `KeepRequest (AccountId, Id)`, `AccountUser (AccountId, Id)` enable account-safe composite FK references
- D2 (ADR-302): Composite AccountUser FKs (nullable, Restrict) on KeepRequest (3 user cols), KeepRequestEvent (4 user cols), KeepRequestParticipant (AccountUserId). "Stale participant" now uses an ineligible-role (Viewer) AccountUser, not a missing one
- D3 (ADR-303): `LastBusinessActivityAt` is nullable; customer-origin sets customer activity, business-origin is inverse; ranking fallback `LastBusinessActivityAt ?? LastCustomerActivityAt ?? CreatedAtUtc`
- D4 (ADR-304): `PhoneNormalizer.Normalize` strips non-ASCII digits, 7–15 digit bounds enforced; stores `PrimaryPhone` (display) + `CanonicalPhone` (identity); unique index on `(AccountId, CanonicalPhone)`
- D5 (ADR-305): `KeepRequest.Create` removed; replaced by `CreateFromCustomerIntake` (10 params) and `CreateByBusiness` (9 params, no first-response timer)

**Files changed:**

| File | Change |
|---|---|
| `src/OpHalo.Keep.Core/Domain/PhoneNormalizer.cs` | New — Normalize + IsValidLength |
| `src/OpHalo.Keep.Core/Entities/KeepCustomer.cs` | CanonicalPhone field + domain guard |
| `src/OpHalo.Keep.Core/Entities/KeepRequest.cs` | Named factories, nullable LastBusinessActivityAt |
| `src/OpHalo.Foundation.Infrastructure/.../AccountUserConfiguration.cs` | HasAlternateKey (AccountId, Id) |
| `src/OpHalo.Keep.Infrastructure/.../KeepPublicIntakeLinkConfiguration.cs` | FK to accounts, partial unique active index |
| `src/OpHalo.Keep.Infrastructure/.../KeepCustomerConfiguration.cs` | FK, AK, CanonicalPhone (max 15), renamed unique index |
| `src/OpHalo.Keep.Infrastructure/.../KeepRequestConfiguration.cs` | FK to accounts, composite FK to KeepCustomer, AK, nullable activity, AccountUser FKs |
| `src/OpHalo.Keep.Infrastructure/.../KeepRequestEventConfiguration.cs` | Composite FK to KeepRequest, 4× AccountUser FKs |
| `src/OpHalo.Keep.Infrastructure/.../KeepRequestParticipantConfiguration.cs` | Composite FK to KeepRequest, composite AccountUser FK |
| `src/OpHalo.Keep.Infrastructure/.../KeepResponsePolicyConfiguration.cs` | FK to accounts |
| `src/OpHalo.Keep.Application/PublicIntake/IKeepIntakePersistence.cs` | Renamed to `FindCustomerByCanonicalPhoneAsync` |
| `src/OpHalo.Keep.Infrastructure/Persistence/KeepIntakePersistence.cs` | Renamed + queries on CanonicalPhone |
| `src/OpHalo.Keep.Application/PublicIntake/CreateKeepPublicIntakeService.cs` | PhoneNormalizer before lookup; CreateFromCustomerIntake |
| `src/OpHalo.Keep.Application/Requests/KeepRequestSummary.cs` | LastBusinessActivityAtUtc → DateTime? |
| `src/OpHalo.Keep.Application/Requests/KeepRequestDetailResult.cs` | LastBusinessActivityAt → DateTime? |
| `src/OpHalo.Keep.Application/Requests/GetKeepRequestListService.cs` | Ranking fallback for sort + cursor |
| All integration/unit test files | KeepRequest.Create → CreateFromCustomerIntake / CreateByBusiness; stale participant seed fixed |
| `tests/OpHalo.UnitTests/Keep/KeepCustomerTests.cs` | 9 new canonical phone + length tests |
| `tests/OpHalo.UnitTests/Keep/KeepRequestTests.cs` | Updated helper; new origin-aware activity tests |
| `tests/OpHalo.IntegrationTests/Persistence/KeepPersistenceProofTests.cs` | Rewritten with two-phase account seed; 10 proof tests |
| `tests/OpHalo.IntegrationTests/Api/KeepRequestParticipationApiTests.cs` | _viewerAccountUserId field; stale seed uses Viewer not Guid.NewGuid |
| `src/OpHalo.Foundation.Infrastructure/Migrations/20260619235301_KeepG1AccountSafeSchema.cs` | Generated migration |

**Deferred:** `KeepRequest.FirstResponseEventId → KeepRequestEvent.Id` FK — circular dependency; document and address when first-response-event assignment is implemented.

### Gap Session G2 — Public-intake validation and concurrent customer recovery — PLANNED

**Findings:** GAP-007, GAP-008, GAP-009; completes application usage of GAP-006/GAP-010

**Goal:** Ensure malformed or concurrent public submissions never become avoidable 500s or damage
known customer data.

**Implementation scope:**

1. Add stable application/API validation for trimmed required fields, storage maxima (name 200,
   phone 50 submitted characters unless the G1 model deliberately narrows it, email 320,
   description 4000), canonical phone digit bounds, and email syntax when supplied.
2. Validation errors return explicit `400` responses without revealing account/token state and
   without database mutation. Public gate failures retain their collapsed unavailable contract.
3. Preserve an existing customer email when a later anonymous submission omits/blank-submits email.
   Replace only with a nonblank valid email. Anonymous omission is never a clear-email command.
4. Handle the named canonical-customer unique violation separately from page-token/reference-code
   collisions: roll back the failed transaction, clear failed tracked state, re-read the winning
   customer, apply safe contact-update rules, and retry request/event persistence with a small
   explicit bound.
5. Never auto-retry user intent after an unrelated database failure. Do not weaken the unique index
   or serialize all intake globally.
6. Ensure customer-origin creation writes correct activity semantics from G1.

**Required tests:**

- HTTP matrix for blank/whitespace values, every maximum and maximum+1, malformed optional email,
  conservative phone failures, no-mutation failures, and collapsed invalid-token behavior;
- repeat intake preserves known email when omitted and updates it when a valid nonblank replacement
  is supplied;
- equivalent formatted phones reuse one customer;
- two genuinely concurrent submissions for a previously unseen canonical phone both succeed as
  separate requests attached to one customer against PostgreSQL;
- regression tests distinguish customer-identity, page-token, and reference-code unique violations;
- full suite green.

**Non-goals:** Honeypot/Turnstile, SMS verification, spam scoring, fuzzy customer matching, address
identity, and `Spam`/`Test` classification.

### Gap Session G3 — Keep onboarding setup, durable intake controls, and manual intake — PLANNED

**Findings:** GAP-001, GAP-002, GAP-014
**ADRs:** ADR-295, ADR-298

**Goal:** Let a real account obtain its durable intake URL and let authenticated staff capture phone,
voicemail, referral, walk-in, and other business-origin requests without database seeding.

**Intake-link contract:**

1. Add an authenticated, idempotent Keep onboarding/setup operation that ensures the account's
   single active link exists after the business profile/name is established. Foundation domain and
   registration services must not depend directly on Keep; the client/onboarding composition calls
   the Keep setup boundary automatically after authentication.
2. Owner/Admin can read setup/status, create/ensure the initial link, pause, resume, and exceptionally
   replace it. Setup controls remain usable in OffSeason, while public intake stays unavailable.
3. Return the raw token/full share URL only when initially created or replaced. Store only its hash.
   Status responses must never pretend the raw URL can be reconstructed from storage.
4. Pause/resume retains the same token and URL. Permanent replacement atomically invalidates the old
   token, creates the successor, returns the new URL once, and exposes an explicit warning contract
   that old printed/shared links are now stale. Do not label ordinary UI behavior "revoke."
5. Generate the stored slug only after business name exists. It is display/routing context, never
   authority; business-name changes must not invalidate an existing URL. Branded frontend routes
   remain outside V1 per ADR-288.
6. Resolve duplicate/concurrent ensure calls idempotently under the G1 one-active-link constraint.

**Manual-create contract:**

1. Add one authenticated business-created request workflow for Owner/Admin/Operator; Viewer is
   forbidden. Account and actor come only from the authenticated session.
2. Reuse G1/G2 customer normalization, matching, validation, safe contact updates, and bounded
   customer-race recovery. Do not fork a second identity implementation.
3. Persist `Origin=Business`, business activity at creation, no fabricated customer activity, and no
   false customer-first-response timer at creation.
4. Create the normal request-created event with authenticated actor metadata and return request
   detail plus the customer page token/URL contract needed by the operator.
5. Respect access/feature/permission/OffSeason write policy. Do not add scheduling, estimating,
   dispatch, CRM, or job-management behavior.

**Legacy cleanup:**

- remove `/continuity/public-intake/...` and its tests;
- remove `emailNotificationsEnabled` from the public request DTO and tests;
- retain only the canonical Keep route; do not introduce replacement compatibility aliases.

**Required tests:**

- account/profile → idempotent Keep setup → public request end-to-end;
- Owner/Admin authorization, Operator/Viewer restrictions on setup, account isolation, OffSeason
  setup versus public-submit behavior;
- create/ensure concurrency, pause/resume same-token behavior, replacement old-token rejection, and
  raw token never persisted or returned by status;
- manual creation by each allowed/forbidden role, account derivation, origin/activity/first-response
  semantics, customer reuse, concurrent first-customer behavior, and OffSeason denial;
- removed alias returns 404 and removed request field is rejected under the API's unknown-field
  posture (or explicitly proven ignored by serializer only if global JSON behavior cannot reject it;
  record the exact contract rather than guessing);
- full suite green.

### Gap Session G4 — Shared request-row authorization — PLANNED

**Finding:** GAP-003

**Goal:** Make one server-authoritative row policy govern lists, counts, detail, and every request
write before Session 6 adds more queues and destructive actions.

**Locked policy:**

- Owner/Admin: account-wide access under normal account/feature/permission gates;
- Operator: active Responsible, active Watching, or intentionally eligible unassigned/Available
  request only;
- Viewer: preserve the already locked explicit read-only policy; do not accidentally grant writes;
- cross-account and same-account-but-invisible direct IDs fail closed as Not Found;
- action-specific rules may be narrower than row visibility, but never broader.

**Implementation scope:**

1. Introduce one shared request-row access policy/projection used by persistence/application paths
   for default/list views, counts, detail, and all existing writes.
2. Treat stale/detached/ineligible participation consistently with the established Session 3/4
   routing rules. Do not make stale participation a hidden authorization grant.
3. Preserve intentional Available discovery and self-assignment behavior without turning all
   unassigned rows into unrestricted mutation targets.
4. Keep account identifiers server-derived and prevent existence leakage through direct IDs.
5. Recompute all action metadata through the same policy; clients must not receive actions that the
   endpoint will reject.

**Required tests:**

- direct-ID HTTP tests—not list-only tests—for another Operator's assigned request, watching,
  responsible, eligible unassigned, stale participant, detached participant, cross-account, and
  Viewer cases;
- every existing request mutation is covered at least once for invisible-row denial;
- list rows, view counts, detail availability, and action flags agree for the same fixtures;
- Owner/Admin account-wide behavior remains intact;
- full suite green.

**Exit record:** Name the single policy entry point and enumerate every read/write service migrated
to it. Any service left outside it blocks G4 completion.

### Gap Session G5 — Entity-wide KeepRequest optimistic concurrency — PLANNED

**Finding:** GAP-004; closes DEF-074 after implementation

**Goal:** Prevent stale business actions from overwriting newer customer/business state, especially
before Session 6 close/cancel/review actions.

**Implementation scope:**

1. Add one database-managed optimistic concurrency token to `KeepRequest` (prefer PostgreSQL `xmin`
   if supported cleanly by the repository's pinned EF/Npgsql versions; otherwise use one explicit
   equivalent consistently). Do not build a feedback-only concurrency patch.
2. Expose an opaque `version` on list/detail results wherever consequential actions are offered.
   The external contract must not expose provider-specific implementation details.
3. Require an expected version for destructive/final state-changing commands and define the shared
   command contract that Session 6 close/cancel will use. Bring existing consequential writes into
   the same pattern where stale user intent can overwrite state; document any deliberately excluded
   append-only operation.
4. Map `DbUpdateConcurrencyException` to stable `409 RequestChanged`. Never automatically retry user
   intent. Return enough safe information for a client to refetch; preserving an unsent client draft
   remains frontend work.
5. Ensure customer writes participate in the same entity token so activity arriving after an
   operator view invalidates a stale consequential command.

**Required tests:**

- EF/persistence proof that two loaded copies cannot both update successfully;
- HTTP stale-version tests for representative business writes and customer-activity-versus-business
  action races;
- stable 409 code/shape tests and no unintended secondary events on conflict;
- sequential valid writes return a new opaque version;
- current sequential duplicate feedback-review semantics remain distinct from true concurrency
  conflict;
- full suite green.

**Non-goals:** Automatic merge/retry, distributed locks, event sourcing, client draft UI, or a
provider-specific version field in the public API.

### Gap Session G6 — Cancelled customer-page expiry correction — PLANNED

**Finding:** GAP-011
**ADR:** ADR-297

**Goal:** Ensure the existing cancellation path starts the locked customer-page retention window,
without pulling Session 6 closeout behavior forward.

**Implementation scope:**

1. When the business transitions a request to `Cancelled`, atomically set
   `ExpiresAtUtc = nowUtc + 30 days` alongside `TerminatedAtUtc`, terminal attention cleanup, and the
   existing cancellation event.
2. Cancellation must use the G4 row policy and G5 expected-version contract. A stale cancellation
   returns `409 RequestChanged` without changing status, expiry, attention, or history.
3. Keep the existing required customer-visible cancellation message and terminal customer-page
   read-only behavior. The page remains safely readable until expiry, then returns the established
   `410` tombstone context.
4. Do not implement Session 6's dedicated close command, ready-to-close queue, close-and-next,
   navigation, or closeout warnings here. Session 6 must apply the same 30-day rule when its close
   path is implemented and must validate whether terminal commands move out of the generic status
   endpoint as part of its already locked ADR-089 contract.
5. `Spam`/`Test` immediate customer-page disablement remains in the later V1 classification slice,
   not this correction.

**Required tests:**

- cancellation sets expiry exactly 30 days from the injected/test clock and persists it with the
  terminal state/event;
- the cancelled page is readable but has no write actions before expiry and returns safe `410`
  context at expiry;
- stale-version cancellation has no side effects;
- non-terminal transitions do not set expiry and active/Resolved pages ignore stale populated
  expiry defensively per ADR-120;
- full suite green.

### Gap Session G7 — Feedback review hardening — PLANNED

**Findings:** GAP-017, GAP-018, GAP-020; GAP-019 is carried to Session 6 navigation
**ADR:** ADR-300; reaffirms ADR-263

**Goal:** Remove the alternate feedback-clearing path, support structured recovery contact, and make
feedback visibility match policy.

**Implementation scope:**

1. Domain-level generic acknowledgement must reject active `UnresolvedFeedback`; action metadata
   must hide acknowledgement in that state. `Mark feedback reviewed` remains the only review
   completion path.
2. Allow Owner/Admin external-contact logs only on `Closed` requests with unreviewed negative
   feedback and active `UnresolvedFeedback`. The log is internal-only, does not reopen/change status,
   count first response, clear attention, mark reviewed, notify the customer, or become customer-page
   history. All other terminal external-contact cases, including Cancelled, remain blocked.
3. Positive feedback comments are visible to any authenticated user who passes the G4 row policy.
   Full negative comments remain Owner/Admin-only; Operator/Viewer receive only already-approved safe
   boolean/timestamp metadata.
4. Correct ADR-282's status/source language now: navigation was not delivered in Session 5. Do not
   create another mutation endpoint. Session 6 must include `feedback_review` in its shared stateless
   next/previous context alongside closeout/status-check contexts.

**Required tests:**

- domain and HTTP regression tests proving generic acknowledgement cannot clear unresolved feedback;
- action metadata matrix for acknowledgement and mark-reviewed;
- external-contact role/state/effect matrix, including customer-page and timeline non-exposure;
- Owner/Admin/Operator/Viewer positive-versus-negative comment visibility under G4 row access;
- OffSeason behavior remains read-only/forbidden as previously locked;
- full suite green.

### Gap Session G8 — Edge hardening, ledger reconciliation, and completion gate — PLANNED

**Findings:** GAP-012, GAP-013, GAP-021; records GAP-011/GAP-016 carry-forward

**Goal:** Prove the public edge and documentation are safe and accurate, then establish the only
allowed entry gate into Session 6.

**Implementation scope:**

1. Configure forwarded-header/client-IP handling for the actual Cloudflare → Railway/application
   chain. Trust forwarding headers only from configured trusted proxies/networks; untrusted peers
   fall back to the connection address and cannot choose limiter partitions.
2. Prove public-intake `10/minute`, zero-queue, `429` behavior in a production-like rate-limit test
   host. Cover partition isolation and spoofed forwarding headers. Do not expand into Turnstile,
   honeypots, SMS verification, or a general abuse platform.
3. Ensure raw intake/page bearer tokens do not appear in application request logs, traces, analytics,
   exceptions, or pilot-friction context. Prefer route templates/redacted values and safe IDs only
   after authorization. Document the required Cloudflare/Railway access-log configuration that code
   cannot enforce and verify what is locally testable.
4. Reconcile decision statuses and sources only after behavior exists: Phase 7 ADRs, ADR-263,
   ADR-282, ADR-295..300, DEF-007, DEF-074, and relevant deferred entries. Do not mark Session 6
   navigation or close/cancel expiry implemented early.
5. Record the locked 30-day terminal expiry as a required Session 6 close/corrected-cancel contract.
6. Keep GAP-016 as a post-Session-6, pre-notification milestone: persistent local PostgreSQL setup,
   zero-to-latest migration, seed/smoke/reset runbook, and eager startup configuration validation.
7. Perform final self-review for accidental notification, realtime, archive, scheduling, SMS,
   analytics, or spam-platform scope.

**Completion gate:**

- G1 migration inspected and migration-from-zero proven;
- real account can ensure/pause/resume/replace intake and submit publicly;
- allowed staff can create a business-origin request;
- row access and OCC fail closed under direct-ID and race tests;
- feedback review has no alternate clearing path;
- trusted IP/429 and application token-redaction behavior are proven;
- `dotnet build --verbosity minimal`, unit, architecture, integration, and full `dotnet test` pass;
- exact final counts, commands, files, migration names, discovered fixes, and remaining external
  deployment checks are recorded here;
- G6 cancellation expiry behavior is proven and Session 6 retains close-path ownership;
- only then mark the gap phase complete and begin a fresh validation of the Session 6 plan against
  the corrected code/contracts.

**Explicitly not part of G8:** implementing Session 6, local persistent DB milestone GAP-016,
notifications/push, frontend UI, `Spam`/`Test` classification, or V1.1 anti-abuse controls.

## Phase 8-B5 Session 5C — List/Customer-Page OffSeason and UI-Ready Metadata — COMPLETE

**Tests:** 847 total (494 unit · 14 arch · 339 integration) — all green
**Next free ADR:** ADR-295 (no new ADRs consumed)

### What was built

Session 5C wires aging metadata onto list summaries and enforces the OffSeason posture on the
customer page.

| File | Change |
|---|---|
| `Keep.Application/Requests/KeepRequestSummary.cs` | `+FeedbackReviewAgeBucket: string?` + `+FeedbackReviewDueAtUtc: DateTime?` on `KeepRequestSummary` record |
| `Keep.Application/Requests/GetKeepRequestListService.cs` | `+using OpHalo.Keep.Core.Domain`; `ToSummary`: computes `feedbackReviewAgeBucket`/`feedbackReviewDueAtUtc` when `isPostClose && r.FeedbackSubmittedAtUtc.HasValue`; populates the two new fields; `+MapFeedbackReviewAgeBucket` private static switch method |
| `Keep.Application/Requests/KeepCustomerPageMapper.cs` | `BuildActiveResult`: passes `context.IsOffSeason` to `ComputeAllowedActions`; `ComputeAllowedActions`: added `bool isOffSeason` param; `Closed + isOffSeason` case returns `[]` before feedback-submitted guard (ADR-277) |
| `IntegrationTests/Api/KeepOffSeasonTests.cs` | `+ClosedFeedbackPageToken` const; seeds closed request with unreviewed negative feedback in `InitializeAsync`; `+GetCustomerPage_OffSeason_ClosedWithPendingFeedback_AllowedActionsEmpty` test |
| `IntegrationTests/Api/KeepFeedbackReviewApiTests.cs` | `+FeedbackReview_ListView_IncludesAgingMetadata` test — verifies all `feedback_review` rows carry `feedbackReviewAgeBucket` (string in `new`/`aging`/`overdue`) and `feedbackReviewDueAtUtc` (datetime string) |

### Already done from earlier sessions (no code needed in 5C)

- **`feedback_review` list/count excludes reviewed requests** — `MarkFeedbackReviewed` clears
  `UnresolvedFeedback` attention (`AttentionLevel → None`); the existing filter
  `AttentionLevel != None` already excludes them. Same for `default` Owner/Admin view and
  `GetViewCountsAsync`.
- **`POST /keep/r/{pageToken}/feedback` blocked in OffSeason** — already in `SubmitFeedbackService`
  (`context.IsOffSeason → OffSeasonUnavailable`); tested in 2C/5B.
- **`CanMarkFeedbackReviewed` false in OffSeason** — `GetKeepRequestDetailService` sets
  `canWrite = canOperate && !isOffSeason`; the flag propagates through `CanMarkFeedbackReviewed`.

### Design decisions confirmed this session

- **D1 — Aging metadata on list summaries**: `FeedbackReviewAgeBucket` and `FeedbackReviewDueAtUtc`
  added to `KeepRequestSummary`. Computed from `FeedbackSubmittedAtUtc` using `FeedbackReviewPolicy`.
  `isPostClose` is the correct gate (Closed + UnresolvedFeedback attention raised implies negative +
  unreviewed by invariant). Null for all non-feedback-review rows.
- **D2 — OffSeason closed customer page returns `AllowedActions = []`**: Do not advertise an action
  the server will reject. `Closed + isOffSeason` case precedes the feedback-submitted check in
  `ComputeAllowedActions` so it fires regardless of whether feedback has already been submitted.
  Active-status pages are unchanged (OffSeason scoped to closed pages per ADR-277).

### Self-review findings

- No bugs found.
- `isPostClose` correctly implies unreviewed + negative by domain invariant — no redundant field
  checks needed in the aging gate.
- `MapFeedbackReviewAgeBucket` is exhaustive with a default throw.
- OffSeason customer page test seeds the closed + negative feedback state using the established
  `ChangeStatus + SubmitFeedback` domain method pattern from `KeepFeedbackReviewApiTests`.

---

## Phase 8-B5 Session 5D — Integration Verification, Docs, Decision Index — COMPLETE

**Tests:** 847 total (494 unit · 14 arch · 339 integration) — all green
**Next free ADR:** ADR-295 (no new ADRs consumed)

### What was done

Session 5D is the completion gate for Sessions 5A–5C.

**Verification pass — all clean:**
- Full test suite: 847/847 pass
- All Session 5 artifacts confirmed present: `MarkFeedbackReviewedService`, `POST /keep/requests/{requestId}/feedback-review` endpoint, migration `20260619155507_AddFeedbackReviewFields`, `FeedbackReviewAgeBucket`/`FeedbackReviewDueAtUtc` on list summaries, `Closed + isOffSeason` customer-page guard
- `feedback_review` view exclusion handled automatically by existing `AttentionLevel != None` filter

**Decision index:** ADR-261..286 updated from `Locked | build-log/042` → `Implemented | Sessions 5A-5C` (26 entries).

**Deferred tracker:** DEF-074 reviewed — no status change. Concurrency gap remains deferred pending cross-cutting OCC slice.

### Files changed

| File | Change |
|---|---|
| `docs/decisions/decision-index.md` | ADR-261..286 marked Implemented |
| `docs/build-log/046-phase-8-b5-session-5d-integration-verification.md` | New |
| `docs/session-log.md` | Rewritten |

---

## Phase 8-B5 Session 5B — Mark-Feedback-Reviewed Service/API — COMPLETE

**Tests:** 845 total (494 unit · 14 arch · 337 integration) — all green
**Next free ADR:** ADR-295 (no new ADRs consumed)

### What was built

Session 5B wires the domain `MarkFeedbackReviewed` method from 5A into the full application and API stack.

| File | Change |
|---|---|
| `Keep.Application/Requests/MuteService.cs` | `BuildDetailAsync` +`DateTime nowUtc` param; `CanMarkFeedbackReviewed`; `nowUtc` to `ToDetailResult` (was interrupted in prior checkpoint) |
| `Keep.Application/Requests/MarkFeedbackReviewedService.cs` | New — Owner/Admin-only; OffSeason blocked (`RequestImplementsAllowedInOffSeason: false`); `nowUtc` captured once; delegates to `KeepRequest.MarkFeedbackReviewed`; `CommitAsync`; returns updated detail result |
| `Api/Keep/FeedbackReviewRequest.cs` | New — `FeedbackReviewRequestBody(string? Note)` |
| `Api/Helpers/ErrorHttpMapper.cs` | +3 entries: `FeedbackReviewUnavailable→409`, `FeedbackAlreadyReviewed→409`, `FeedbackReviewNoteTooLong→400` |
| `Api/Program.cs` | `AddScoped<MarkFeedbackReviewedService>`; `POST /keep/requests/{requestId}/feedback-review` endpoint |
| `IntegrationTests/Api/KeepFeedbackReviewApiTests.cs` | New — 10 tests: Owner success (attention cleared, action false), Admin+note, Operator 403, Viewer 403, unauthenticated 401, already-reviewed 409, positive-feedback 409, no-feedback 409, note-too-long 400, customer-page exclusion |
| `IntegrationTests/Api/KeepOffSeasonTests.cs` | +1 test: `MarkFeedbackReviewed_OffSeason_Returns403` |

### Design decisions confirmed this session

- **Concurrency race deferred (DEF-074):** `GetRequestForUpdateAsync` has no row lock or concurrency token. Two concurrent Owner/Admin requests could both pass the unreviewed guard and commit. In practice the risk for V1 pilot is low; the correct fix (Npgsql `xmin` OCC or EF `RowVersion` on `KeepRequest`) is a cross-cutting infrastructure concern that should be applied to all write paths together. Added to deferred tracker.
- **OffSeason returns `Forbidden` (not `KeepRequest.ReadOnly`):** ADR-276 lists `KeepRequest.ReadOnly` aspirationally; the entire implemented convention (10+ write services) returns generic `auth.forbidden` for OffSeason/blocked account state. `MarkFeedbackReviewedService` follows the uniform convention. ADR-276 superseded on this point.

---

## Phase 8-B5 Session 5A — Domain, Schema, Migration, Aging Policy — COMPLETE

**Tests:** 834 total (494 unit · 14 arch · 326 integration) — all green
**Next free ADR:** ADR-295 (no new ADRs consumed)
**Migration:** `20260619155507_AddFeedbackReviewFields` — three nullable columns on `keep_requests`

### What was built

Session 5A adds the durable foundation for post-close negative-feedback review.

| File | Change |
|---|---|
| `Keep.Core/Entities/KeepRequest.cs` | `+MarkFeedbackReviewed(reviewer, note, now)` domain method: eligibility requires Closed + unreviewed negative feedback + active UnresolvedFeedback attention; clears attention with reason `"feedback_reviewed"`; stores reviewer/timestamp/note; returns `FeedbackReviewed` event |
| `Keep.Core/Entities/KeepRequestEvent.cs` | `+CreateFeedbackReviewed` factory — Visibility=Internal, ActorType=AccountUser, Content=trimmed note or null; no new event-table columns |
| `Keep.Core/Entities/Enums/KeepRequestEventType.cs` | `+FeedbackReviewed = 11` |
| `Keep.Core/Entities/Enums/FeedbackReviewAgeBucket.cs` | New — `New`, `Aging`, `Overdue` |
| `Keep.Core/Domain/FeedbackReviewPolicy.cs` | New — pilot thresholds (new <24h, aging 24–72h, overdue >72h); `ComputeAgeBucket(submittedAtUtc, nowUtc)` and `ComputeReviewDueAtUtc(submittedAtUtc)` |
| `Keep.Core/Errors/KeepRequestErrors.cs` | `+FeedbackReviewUnavailable`, `+FeedbackAlreadyReviewed`, `+FeedbackReviewNoteTooLong` (ADR-276) |
| `Keep.Application/Requests/KeepRequestDetailMapper.cs` | `+"feedback_reviewed"` string mapping for `FeedbackReviewed = 11` in `MapEventType` |
| `Keep.Infrastructure/Configurations/KeepRequestConfiguration.cs` | `+FeedbackReviewedAtUtc` (nullable DateTimeOffset), `+FeedbackReviewedByAccountUserId` (nullable Guid), `+FeedbackReviewNote` (HasMaxLength 2000, nullable) |
| `Foundation.Infrastructure/Migrations/20260619155507_AddFeedbackReviewFields.cs` | New migration — three nullable columns |
| `Foundation.Infrastructure/Migrations/OpHaloDbContextModelSnapshot.cs` | Snapshot updated |
| `UnitTests/Keep/KeepRequestFeedbackReviewTests.cs` | New — 31 unit tests: success paths, all eligibility failures, D1 edge case (attention cleared externally → FeedbackReviewUnavailable), note boundaries, ArgumentException guards, all aging bucket boundaries (exactly 72h = Aging) |
| `docs/build-log/042-...decisions.md` | 5A pre-implementation gate clarifications appended |

### Decisions confirmed this session (from build-log gate)

- **D1 — Eligibility requires active `UnresolvedFeedback` attention.** If an Owner/Admin first uses `AcknowledgeAttention` on a closed unresolved-feedback request, feedback review returns `FeedbackReviewUnavailable` — the review state was already cleared. Flagged as a pilot interaction to monitor, not fixed by weakening the eligibility rule.
- **D2 — `FeedbackReviewPolicy` lives in `Keep.Core/Domain/`.** Pure policy; pilot thresholds centralized so account preference settings can replace them later without moving bucket semantics.
- **D3 — `FeedbackReviewed` event uses `Content` for optional note; no new event-table columns.**
- **`KeepCustomerPageMapper`** deliberately excludes `FeedbackReviewed` — Internal events are already filtered by `Visibility == All` before `MapEvent` is reached.

---

## Phase 8-B5 Session 4D — Integration Verification, Docs, Decision Index, Deferred Tracker — COMPLETE

**Tests:** 803 total (463 unit · 14 arch · 326 integration) — all green
**Next free ADR:** ADR-295

### What was done

Session 4D is the completion gate for Sessions 4A–4C.

**Verification pass — all clean:**
- Full test suite: 803/803 pass
- No Session 5 code present (`POST feedback-review` endpoint not registered; no `FeedbackReviewedAtUtc`/`FeedbackReviewedByAccountUserId`/`FeedbackReviewNote` fields)
- `RequestListViewNotYetAvailable` retained in errors + ErrorHttpMapper but never returned by any service path
- Cursor sentinels `HistorySortSentinel = 0` and `FeedbackReviewSortSentinel = 99` present; no collision with B5 ranking groups (1–8)
- HMAC key wired in `KeepApiWebFactory` (32-byte all-zeros test key)
- OffSeason list posture correct: reads open, `isOffSeason` suppresses notification eligibility and `CanSelfAssignFromList`/`CanAssignFromList`

**Decision index:** ADR-237..260 updated from `Locked | build-log/041` → `Implemented | Sessions 4A-4C` (24 entries). ADR-287 was already `Implemented`.

**Deferred tracker:**
- DEF-034 → `Implemented — Sessions 4A-4C`
- DEF-045 → `Implemented — Session 4C`

### Files changed

| File | Change |
|---|---|
| `docs/decisions/decision-index.md` | ADR-237..260 marked Implemented |
| `docs/deferred-topics.md` | DEF-034, DEF-045 closed |
| `docs/build-log/045-phase-8-b5-session-4d-integration-verification.md` | New |
| `docs/session-log.md` | Rewritten |

---

## Phase 8-B5 Session 4C — Operator Unassigned Surface + Self-Assign Re-enable, Row Context — COMPLETE

**Tests:** 803 total (463 unit · 14 arch · 326 integration) — all green
**Next free ADR:** ADR-288

### What was built

Session 4C removed the 4B operator gate on `view=unassigned`, replaced the blunt Operator block in
`ManageResponsibleService.SetAsync` with targeted self-assign logic, added `RowContext` and
`CanSelfAssignFromList` to the list surface, and opened the unassigned view count to all roles.

| File | Change |
|---|---|
| `Keep.Core/Errors/KeepRequestErrors.cs` | `ParticipationOperatorCannotAssignOther` message updated (remove "or self-assign") |
| `Keep.Application/Requests/KeepRequestSummary.cs` | `+RowContext: string` on `KeepRequestSummary`; `+CanSelfAssignFromList: bool` on `KeepRequestParticipationInfo` |
| `Keep.Application/Requests/GetKeepRequestListService.cs` | `ToSummary`: adds `isUnassigned`, `canSelfAssignFromList`, `ComputeRowContext` call; `BuildParticipationInfo`: 3rd param `canSelfAssignFromList`; new `ComputeRowContext` static method |
| `Keep.Application/Requests/ManageResponsibleService.cs` | `SetAsync`: replaced blunt Operator block with `isOperator && target != self → 403`; after loading participants: `isOperator && existingResponsible != self → 409`; self-already-responsible falls through to ADR-230 no-op |
| `Keep.Infrastructure/Persistence/KeepRequestListPersistence.cs` | `GetViewCountsAsync`: removed `isOwnerOrAdmin ? count : 0` gate; all roles get real unassigned count |
| `UnitTests/Keep/KeepRequestListServiceTests.cs` | Updated stale test (`not_yet_available_for_operator` → `returns_200_for_operator`); added `ParticipantSummaryMap` to `FakeRequestListPersistence`; +14 new tests (rowContext cases, CanSelfAssignFromList positive/negative, stale responsible, offseason, view boundary) |
| `IntegrationTests/Api/KeepRequestListQueryApiTests.cs` | Added Operator user seeding + `_operatorCookie`; `GetAsAsync` helper; `Operator_unassigned_view_returns_200`; `rowContext_field_present_on_each_row`; extended `ListRequestBody` with `string? RowContext` |
| `IntegrationTests/Api/KeepRequestParticipationApiTests.cs` | Updated `SetResponsible_Operator_Returns403_OperatorCannotAssignOther` to target owner (not self); added 3 new request IDs + seeds; 3 new tests: `SelfAssign_UnassignedRequest_Returns200`, `SelfAssign_AlreadyAssigned_Returns409`, `SelfAssign_AlreadyResponsible_IsNoOp_Returns200` |

### Decisions locked this session

**D1 — rowContext priority:**
```
feedback_review      → isPostClose (Closed + UnresolvedFeedback + attention raised)
closed_history       → Closed (not isPostClose)
cancelled_history    → Cancelled
needs_attention      → AttentionLevel != None
first_response       → firstResponsePending || firstResponseOverdue
waiting_on_customer  → PendingCustomer
unassigned_available → canSelfAssignFromList (view=unassigned && Operator && !offSeason && !terminal && isUnassigned)
active_work          → default
```
`needs_attention` wins over `unassigned_available` for rowContext; `CanSelfAssignFromList` is still
true alongside `needs_attention` (claim affordance and urgency signal are separate).

**D2 — Operator self-assign error handling:**
- Operator assigns another user → 403 `ParticipationOperatorCannotAssignOther`
- Operator self-assign, no active Responsible → 200 (allowed)
- Operator self-assign, another active Responsible exists → 409 `ParticipationRequestAlreadyAssigned`
- Operator self-assign, already self-assigned → no-op 200 (ADR-230)

**D3 — CanSelfAssignFromList:**
- Formula: `view=="unassigned" && !isOwnerOrAdmin && canOperate && !isOffSeason && !r.IsTerminal && isUnassigned`
- `isUnassigned = participation is null || participation.ResponsibleCount == 0`
- Stale Responsible has `DetachedAtUtc == null` → counts in `ResponsibleCount` → NOT self-assignable (also excluded from unassigned DB query, so belt-and-suspenders)
- Write endpoint recomputes eligibility independently; list flag is UI metadata only

### Watch-outs added this session

- **Stale Responsible not self-assignable** — stale rows have `DetachedAtUtc == null`, so `ResponsibleCount > 0` and `isUnassigned = false`. The unassigned DB view already excludes these requests.
- **`RequestListViewNotYetAvailable`** error code remains in `KeepRequestErrors.cs` and `ErrorHttpMapper.cs` for future use; no longer returned by the list service (unassigned gate removed in 4C).

---

## Phase 8-B5 Session 4B — Named Views, Filters/Search, History Visibility, View Counts, Sorting — COMPLETE

**Tests:** 784 total (449 unit · 14 arch · 321 integration) — all green
**Next free ADR:** ADR-288

### What was built

Session 4B replaced the two 4A placeholder gates (`HasFilters → FilterNotYetAvailable` and `view != default → ViewNotYetAvailable`) with real server-side execution across 7 files.

| File | Change |
|---|---|
| `Keep.Core/Errors/KeepRequestErrors.cs` | +3 errors: `RequestListInvalidStatus`, `RequestListInvalidAttentionReason`, `RequestListHistoryViewForbidden` |
| `Keep.Application/Requests/IKeepRequestListPersistence.cs` | +3 methods: `GetActiveViewRequestsAsync`, `GetHistoryRequestsAsync`, `GetViewCountsAsync`; +3 types: `ActiveViewKind`, `HistoryViewKind`, `KeepRequestListFilters` |
| `Keep.Application/Requests/GetKeepRequestListService.cs` | Complete rewrite: named view dispatch, status/attentionReason slug validation, contradiction updates (`feedback_review + non-Closed` and `closedFrom/closedTo + non-history`), history keyset pagination, in-memory B5 ranking sort, `FeedbackReviewComparer`, view counts always populated, `IsHistory`/`IsSearch` in list context |
| `Keep.Infrastructure/Persistence/KeepRequestListPersistence.cs` | Complete rewrite: `GetActiveViewRequestsAsync` (EXISTS subqueries for participant views), `GetHistoryRequestsAsync` (keyset cursor, TerminatedAtUtc guard), `GetViewCountsAsync` (6 sequential counts, role-aware), `ApplyCommonFilters` (q search with FeedbackComment gated by `IsOwnerOrAdmin`) |
| `Api/Helpers/ErrorHttpMapper.cs` | +3 explicit entries for `RequestListInvalidStatus` (400), `RequestListInvalidAttentionReason` (400), `RequestListHistoryViewForbidden` (403) |
| `UnitTests/Keep/KeepRequestListServiceTests.cs` | Updated: removed 4A filter gate tests; renamed 4 persistence-contract tests to use `IsOwnerOrAdmin`; changed `Assert.Null(ViewCounts)` to `Assert.NotNull`; added 15 new tests (invalid slug, filter/search pass-through, closedFrom, dispatch routing, list-context flags, view counts) |
| `IntegrationTests/Api/KeepRequestListQueryApiTests.cs` | Updated: removed 4A placeholder assertions; replaced 5 stale tests with 4B contracts; added 5 new tests (named view 200, history view 200, closedFrom contradictory, invalid status 400, invalid attentionReason 400, q 200) |

### Named view dispatch

| View | Kind | DB filter | Sort |
|---|---|---|---|
| `default` | Active | open + Owner/Admin unresolved feedback closed | in-memory B5 ranking |
| `assigned_to_me` | Active | active + Responsible EXISTS | in-memory B5 ranking |
| `watching` | Active | active + Watching EXISTS | in-memory B5 ranking |
| `unassigned` | Active | active + no Responsible | in-memory B5 ranking |
| `needs_attention` | Active | active + `AttentionLevel != None` | in-memory B5 ranking |
| `feedback_review` | Active | closed + UnresolvedFeedback + attention raised | Owner/Admin only; FeedbackReviewComparer (AttentionSinceUtc ASC) |
| `closed_history` | History | `TerminatedAtUtc != null && Closed` | DB keyset `TerminatedAtUtc DESC, Id ASC` |
| `cancelled_history` | History | `TerminatedAtUtc != null && Cancelled` | DB keyset |
| `all_history` | History | `TerminatedAtUtc != null && (Closed or Cancelled)` | DB keyset |

### Cursor sentinel values

- `HistorySortSentinel = 0` — marks a history keyset cursor (RankingOrder field)
- `FeedbackReviewSortSentinel = 99` — marks a feedback_review cursor; SecondaryTick = AttentionSinceUtc ticks

### Validation order (updated, locked)

`NormalizeView` → unknown view → `ValidateDateFormats` → status slug → attentionReason slug → `ValidateContradictions` → view role auth → Operator unassigned gate → limit range → cursor decode/fingerprint

Key changes from 4A: `HasFilters` gate removed; status/attentionReason slug validation inserted before contradictions; history view role auth (`RequestListHistoryViewForbidden → 403`) inserted after contradictions.

### Role access

- Owner/Admin: all views accessible
- Operator: `feedback_review`, `closed_history`, `cancelled_history`, `all_history` → 403; `unassigned` → `ViewNotYetAvailable` (4B); all other active views → 200
- Viewer: treated as non-admin; same view restrictions as Operator

### 4B placeholders remaining (for 4C)

- Operator `view=unassigned` returns `RequestListViewNotYetAvailable` (4C adds eligibility filtering and removes gate)
- `GetViewCountsAsync`: Operator unassigned count returns 0 (same gate)

---

## Phase 8-B5 Session 4A — Query Contract, Validation, Response Shape, Cursor/Page Primitives — COMPLETE

**Tests:** 765 total (434 unit · 14 arch · 317 integration) — all green
**Next free ADR:** ADR-288

### What was built

Session 4A implemented the query contract, cursor/page primitives, explicit validation, and response shape for `GET /keep/requests`. The no-query default command-center behavior is preserved; all new query params are optional. Server-side filter/search execution is deferred to 4B.

**12 files written / updated:**

| File | Change |
|---|---|
| `Keep.Core/Errors/KeepRequestErrors.cs` | +10 errors: 7 list query errors + `RequestListInvalidAssignedAccountUserId`, `RequestListUnknownParameter`, `RequestListDuplicateParameter` |
| `Keep.Application/Requests/KeepRequestListQuery.cs` | New — query contract record (View, Status, AttentionReason, AssignedAccountUserId, Q, CreatedFrom, CreatedTo, ClosedFrom, ClosedTo, Limit, Cursor) |
| `Keep.Application/Requests/IKeepRequestListCursorProtector.cs` | New — `Protect(string) string` + `TryUnprotect(string, out string?) bool` interface |
| `Keep.Application/Requests/KeepRequestListCursor.cs` | New — `Encode`, `TryDecode`, `ComputeFingerprint` static helpers; `KeepRequestListCursorPayload` record |
| `Keep.Application/Requests/GetKeepRequestListResult.cs` | Updated — added `KeepRequestPageInfo`, `KeepRequestViewCounts?`, `KeepRequestListContext`; result ctor updated to 4 params |
| `Keep.Application/Requests/GetKeepRequestListService.cs` | Updated — `IKeepRequestListCursorProtector` as 7th constructor param; `ExecuteAsync(KeepRequestListQuery? query = null, CancellationToken ct = default)`; full validation pipeline + cursor skip + limit+1 slicing |
| `Keep.Infrastructure/Cursors/HmacKeepRequestListCursorProtector.cs` | New — HMAC-SHA256 with `FixedTimeEquals`; Base64Url encode/decode; reads `Keep:RequestListCursorSigningKey` from config |
| `Api/Helpers/ErrorHttpMapper.cs` | +10 explicit 400 entries for all new request-list error codes |
| `Api/Keep/KeepRequestListQueryBinding.cs` | New — case-insensitive key normalization; unknown param, duplicate param, invalid limit, invalid GUID detection |
| `Api/Program.cs` | Updated `GET /keep/requests` to bind via `KeepRequestListQueryBinding`; lazy DI factory for `IKeepRequestListCursorProtector`; removed duplicate `using OpHalo.Api.Keep` |
| `IntegrationTests/Api/KeepApiWebFactory.cs` | +`Keep:RequestListCursorSigningKey` in `AddInMemoryCollection` (32-byte all-zeros deterministic key) |
| `UnitTests/Keep/KeepRequestListServiceTests.cs` | Rewrite — updated `BuildSut` with `FakeCursorProtector`; +tests for all validation paths, pagination, cursor follow, fingerprint equivalence |
| `IntegrationTests/Api/KeepRequestListQueryApiTests.cs` | New — 25 HTTP tests: auth gate, 200 shape, all 400 codes, pagination, cursor follow, HMAC tamper (sig + payload), view=default equivalence |

### Bugs found and fixed during implementation

Five design issues were caught during review before each file was written:

1. **Date validation shadowed by filter gate** — `createdFrom=banana` was returning `FilterNotYetAvailable` instead of `InvalidDateFormat` because the filter gate ran first. Fixed by inserting `ValidateDateFormats` before the filter gate in the validation pipeline.

2. **Fingerprint divergence for null vs "default" view** — `GET /keep/requests` and `GET /keep/requests?view=default` produced different fingerprints, making cursors invalid across equivalent queries. Fixed in `KeepRequestListCursor.ComputeFingerprint`: `string.IsNullOrWhiteSpace(view) ? "default" : view.Trim().ToLowerInvariant()`.

3. **Contradiction check shadowed by view-executability gate** — `view=closed_history&status=received` returned `ViewNotYetAvailable` before the contradiction check could run. Fixed by inserting `ValidateContradictions` before the view-executability gate.

4. **Too much parsing in Program.cs** — limit parsing, GUID parsing, unknown-param and duplicate-param detection were accumulating in the endpoint handler. Extracted into `KeepRequestListQueryBinding`.

5. **Wrong errors for binder cases** — `assignedAccountUserId=banana` returned `FilterNotYetAvailable`; `?limit=10&limit=20` silently took first; unknown params were silently ignored; case-variant duplicates were not detected. Fixed by adding three new error codes and using a case-insensitive dictionary with `TryAdd`.

6. **Integration test `code` field** — `ProblemBody.Extensions` deserializes as a nested dict, but `code` lands at the top level of ProblemDetails JSON (existing watch-out, ADR-irrelevant). Fixed `GetErrorCodeAsync` to use `JsonElement.GetProperty("code")`.

### Validation order (locked)

`NormalizeView` → unknown view → `ValidateDateFormats` → `ValidateContradictions` → `HasFilters` (4A gate) → view executability (4A gate) → limit range → cursor decode/fingerprint

### Cursor contract (locked)

- Format: `base64url(UTF8(payloadJson)) + "." + base64url(HMAC-SHA256(key, payloadBytes))`
- Fingerprint: SHA-256 of canonical normalized query JSON (view/status/attentionReason lowercased; null-view = "default"; excludes limit/cursor)
- Config key: `Keep:RequestListCursorSigningKey` (base64 string; read lazily from `IConfiguration`; fail-hard if missing outside Testing)
- Unit tests: `FakeCursorProtector` (plain Base64, no HMAC); HMAC integrity covered by integration tests

### listContext (4A default path)

`{ view: "default", isDefaultCommandCenter: true, isHistory: false, isSearch: false }` — named views return view-specific context in 4B.

### viewCounts

`null` in 4A; real count queries wired in 4B.

---

## Phase 8-B5 Session 3D — Assignment/Watch/Mute Completion Gate — COMPLETE

**Tests:** 702 (396 unit · 14 arch · 292 integration) — all green
**Next free ADR:** ADR-261

### What was done

Session 3D was a verification and documentation gate for Sessions 3A–3C.

**Verification pass — all clean:**
- Full test suite: 702/702 pass
- Migration (`20260618102937_AddParticipationChangedEventFields`): exactly 7 nullable event fields + filtered unique Responsible index; no extra schema changes
- Customer-page exclusion: `CustomerPage_ExcludesParticipationChangedEvents` test present and passing
- OffSeason blocking: all 4 write services have `RequestImplementsAllowedInOffSeason: false` and `|| decision.IsReadOnly` in `AuthAsync`; fires before request load or target user ID validation
- Deferred boundary: no notification delivery/outbox/device tokens, no Operator queue, no list-level clear/watch/mute, no auto stale-participant cleanup in any participation service
- Endpoints: exactly 9 registered (1 GET candidates + 8 writes); no list-level controls
- ADR-222..235: all marked `Implemented` in decision index

**Gap closed:**
OffSeason integration tests for participation endpoints were absent (`KeepOffSeasonTests.cs` covered 5 write services but not any of the 8 participation write endpoints). Added 4 representative tests: `PutResponsible_OffSeason_Returns403`, `PutWatcher_OffSeason_Returns403`, `PutWatch_OffSeason_Returns403`, `PutMute_OffSeason_Returns403`.

**Doc updates:**
- `DEF-033` updated to `Implemented — Sessions 3A-3D` with full implementation summary
- This session log

### Files changed

| File | Change |
|---|---|
| `IntegrationTests/Api/KeepOffSeasonTests.cs` | +4 OffSeason participation write tests |
| `docs/deferred-topics.md` | DEF-033 → Implemented |
| `docs/session-log.md` | Rewritten for 3D |

---

## Phase 8-B5 Session 4 — Filters, Search, Closed History, Pagination — DECISIONS LOCKED

**Status:** Decision gate complete. Ready for bounded implementation sessions.
**ADRs:** 237..260
**Next free ADR:** ADR-261
**Build log:** `docs/build-log/041-phase-8-b5-session-4-filters-search-closed-history-pagination-decisions.md`

### What was decided

Session 4 is a backend/foundation list-navigation slice. It extends `GET /keep/requests` with
named views, server-side filters/search, cursor pagination, role-aware operational counts,
closed/cancelled/all history for Owner/Admin, row/list context metadata, and explicit query
validation while preserving no-query default command-center behavior.

### Deferred from Session 4

- realtime list/count invalidation;
- exact arbitrary totals for search/history/filter combinations;
- broad Operator terminal-history access;
- mark feedback reviewed;
- archive/unarchive;
- contextual customer/address history summaries;
- internal/team-memory search;
- web/PWA and native mobile UI.

---

## Phase 8-B5 Session 3C — Participation Read Models, Timeline, List Assignment Metadata — COMPLETE

**Tests:** 698 (396 unit · 14 arch · 288 integration) — all green
**Next free ADR:** ADR-261

### What was built

Session 3C completed the participation read-model surface:

- **`AvailableActionsMetadata`** expanded to 4 participation flags: `CanWatch`, `CanUnwatch`, `CanMute`, `CanUnmute` — with precise semantics replacing the original 2-flag approach (`CanWatch` means "can start watching, not currently participating"; `CanUnwatch/Mute/Unmute` are state-specific).
- **`CurrentUserDetailParticipation`** record on `KeepRequestDetailResult` — `participationType` + `notificationsEnabled` for the requesting user.
- **`participants[].isEligible`** derived from live membership in the mapper.
- **`KeepRequestEventItem`** participation event metadata fields: `participationAction`, `participationTargetAccountUserId`, `participationTargetDisplayName`, `participationPreviousResponsibleAccountUserId`, `participationInternalNote`.
- **`KeepRequestParticipationInfo`** on list summaries: `responsibleDisplayName`, `responsibleIsStale`, `canAssignFromList`.
- **`GetActorDisplayNameAsync`** fixed to return `User.Name ?? Email` (with `.Trim()`), consistent with `GetParticipantTargetAsync` and `GetParticipantCandidatesAsync`.

### Design decisions locked this session (refinements)

- **`CanWatch`**: `!isTerminal && currentUserRow is null` — "can start watching", false if already participating as anything.
- **`CanUnwatch`**: `!isTerminal && currentUserRow?.ParticipationType == Watching` — false for Responsible (DELETE /watch cannot clear responsibility).
- **`CanMute`**: `!isTerminal && currentUserRow is not null && currentUserRow.NotificationsEnabled` — only when participating and currently unmuted.
- **`CanUnmute`**: `!isTerminal && currentUserRow is not null && !currentUserRow.NotificationsEnabled` — only when participating and currently muted.
- **`CanAssignResponsible`**: `isOwnerOrAdmin && canWrite && !isTerminal` (unchanged).
- **`GetActorDisplayNameAsync` trim**: Returns `UserName.Trim()` or `Email.Trim()` for consistency.

### Files changed

| File | Change |
|---|---|
| `Keep.Application/Requests/KeepRequestDetailResult.cs` | `AvailableActionsMetadata` +`CanUnwatch`/`CanUnmute`; `CurrentUserDetailParticipation` record; `KeepRequestParticipantItem` +`IsEligible`; `KeepRequestEventItem` +5 participation fields |
| `Keep.Application/Requests/IKeepRequestDetailPersistence.cs` | `KeepParticipantProjection` +`MembershipStatus` |
| `Keep.Application/Requests/IKeepRequestListPersistence.cs` | `KeepRequestParticipantSummary` +`ResponsibleDisplayName`/`ResponsibleIsStale`; `GetParticipantSummariesAsync` +`accountId` param |
| `Keep.Application/Requests/KeepRequestSummary.cs` | `KeepRequestParticipationInfo` +`ResponsibleDisplayName`/`ResponsibleIsStale`/`CanAssignFromList` |
| `Keep.Application/Requests/KeepRequestDetailMapper.cs` | `ToDetailResult` +`currentUserId`; `MapParticipant` +`IsEligible`; `MapEvent` +participation fields; `MapParticipationAction` added |
| `Keep.Application/Requests/GetKeepRequestDetailService.cs` | 4-flag AvailableActions; `canWrite` gates; `currentUser.UserId` to mapper |
| `Keep.Application/Requests/AcknowledgeAttentionService.cs` | 4-flag AvailableActions; `currentUser.UserId` to mapper |
| `Keep.Application/Requests/ManageResponsibleService.cs` | `BuildDetailAsync` 4-flag AvailableActions |
| `Keep.Application/Requests/ManageWatcherService.cs` | `BuildDetailAsync` 4-flag AvailableActions |
| `Keep.Application/Requests/SelfWatchService.cs` | `BuildDetailAsync` 4-flag AvailableActions |
| `Keep.Application/Requests/MuteService.cs` | `BuildDetailAsync` 4-flag AvailableActions |
| `Keep.Application/Requests/AddInternalNoteService.cs` | 4-flag AvailableActions; `currentUser.UserId` to mapper |
| `Keep.Application/Requests/AddBusinessUpdateService.cs` | +using Enums; 4-flag AvailableActions; `currentUser.UserId` to mapper |
| `Keep.Application/Requests/ChangeKeepRequestStatusService.cs` | +2 usings; 4-flag AvailableActions; `currentUser.UserId` to mapper |
| `Keep.Application/Requests/LogExternalContactService.cs` | +using Enums; 4-flag AvailableActions; `currentUser.UserId` to mapper |
| `Keep.Application/Requests/GetKeepRequestListService.cs` | `BuildParticipationInfo` +`responsibleDisplayName`/`responsibleIsStale`/`canAssignFromList`; passes `currentUser.AccountId` to persistence |
| `Keep.Infrastructure/Persistence/EfKeepRequestDetailPersistence.cs` | Fetches `MembershipStatus` in participant query |
| `Keep.Infrastructure/Persistence/EfKeepRequestOperatePersistence.cs` | `GetActorDisplayNameAsync` → `User.Name.Trim() ?? Email.Trim()` |
| `Keep.Infrastructure/Persistence/KeepRequestListPersistence.cs` | `GetParticipantSummariesAsync` — account-scoped, effective count, stale logic |
| `UnitTests/Keep/KeepRequestListServiceTests.cs` | `FakeRequestListPersistence.GetParticipantSummariesAsync` +`accountId` param |
| `IntegrationTests/Api/KeepRequestParticipationApiTests.cs` | +8 integration tests covering 3C read-model assertions |

---

## Phase 8-B5 Session 3B — Participation API/Application Services — COMPLETE

**Tests:** 690 (396 unit · 14 arch · 280 integration) — all green
**Next free ADR:** ADR-237

### What was built

Five application-layer services + 9 API endpoints + 29 integration tests covering all participation write and candidate-lookup surfaces.

**Bugs found and fixed during 3B testing:**
- `MapEventType` in `KeepRequestDetailMapper` missing `ParticipationChanged = 10` → added `"participation_changed"` mapping.
- Minimal API `MapDelete` with nullable body (`ClearResponsibleRequestBody?`, `WatcherRequestBody?`) causes startup failure (inferred body not allowed on DELETE). Fixed with explicit `[FromBody]` attribute on both DELETE endpoints.

### Files changed

| File | Change |
|---|---|
| `docs/deferred-topics.md` | Updated DEF-045 to document Operator self-assign block in 3B |
| `Keep.Core/Errors/KeepRequestErrors.cs` | Added 4 application-layer participation errors |
| `Keep.Application/Requests/IKeepRequestOperatePersistence.cs` | Added `GetParticipantsForUpdateAsync`, `GetParticipantTargetAsync`, `GetParticipantCandidatesAsync`, `CommitParticipationAsync` + `ParticipantTargetInfo` + `ParticipantCandidateRecord` types |
| `Keep.Application/Requests/KeepRequestDetailMapper.cs` | `MapRole` → `internal`; added `"participation_changed"` to `MapEventType` switch |
| `Keep.Infrastructure/Persistence/EfKeepRequestOperatePersistence.cs` | Implemented 4 new methods |
| `Keep.Application/Requests/ManageResponsibleService.cs` | New — `SetAsync` (Owner/Admin), `ClearAsync` (Owner/Admin); Operator blocked for both |
| `Keep.Application/Requests/ManageWatcherService.cs` | New — `AddAsync`, `RemoveAsync` (Owner/Admin, stale-safe policy) |
| `Keep.Application/Requests/SelfWatchService.cs` | New — `WatchAsync`, `UnwatchAsync` (all operators) |
| `Keep.Application/Requests/MuteService.cs` | New — `MuteAsync`, `UnmuteAsync` (all operators) |
| `Keep.Application/Requests/GetParticipantCandidatesService.cs` | New — Owner/Admin only, OffSeason read posture |
| `Api/Helpers/ErrorHttpMapper.cs` | Added 9 participation error HTTP mappings |
| `Api/Keep/ParticipationRequest.cs` | New — `SetResponsibleRequestBody`, `ClearResponsibleRequestBody`, `WatcherRequestBody` |
| `Api/Program.cs` | Registered 5 services; added 9 endpoints; `[FromBody]` fix on 2 DELETE endpoints |
| `IntegrationTests/Api/KeepRequestParticipationApiTests.cs` | New — 29 integration tests across all 5 services + candidates |

---

## Phase 8-B5 Session 3A — Domain + Persistence Invariants — COMPLETE

**Tests:** 396 unit · 14 arch · 251 integration (661 total — all green)
**Next free ADR:** ADR-236 (no new ADRs consumed in 3A)
**Migration:** `20260618102937_AddParticipationChangedEventFields` — 7 nullable columns on `keep_request_events`; filtered unique index on `keep_request_participants`

**Files changed:**

| File | Change |
|---|---|
| `Keep.Core/Entities/Enums/ParticipationAction.cs` | New: 9-value enum (`ResponsibleAssigned`..`Unmuted`) |
| `Keep.Core/Entities/Enums/ParticipationNotificationIntentKind.cs` | New: `Assignment = 1`, `WatcherAdded = 2` |
| `Keep.Core/Entities/Enums/KeepRequestEventType.cs` | Added `ParticipationChanged = 10` |
| `Keep.Core/Entities/KeepRequestParticipant.cs` | Added `Detach`, `Reactivate`, `SetNotificationsEnabled` mutation methods |
| `Keep.Core/Entities/KeepRequestEvent.cs` | Added 7 participation fields + `CreateParticipationChanged` factory with intent-pairing validation |
| `Keep.Core/Errors/KeepRequestErrors.cs` | Added `ParticipationNoteTooLong`, `ParticipationMuteRequiresActiveParticipation`, `ParticipationCannotUnwatchResponsible`, `ParticipationResponsibleCannotWatch`, `ParticipationStateCorrupt` |
| `Keep.Core/Domain/ParticipationChangeOutcome.cs` | New: factory-only outcome record |
| `Keep.Core/Domain/KeepRequestParticipationService.cs` | New: domain service — 8 methods, participant-set invariant validation |
| `Keep.Infrastructure/.../KeepRequestEventConfiguration.cs` | EF config for 7 new nullable participation fields |
| `Keep.Infrastructure/.../KeepRequestParticipantConfiguration.cs` | Filtered unique index for one-active-Responsible per request |
| `Foundation.Infrastructure/Migrations/20260618102937_AddParticipationChangedEventFields.cs` | Migration |
| `UnitTests/Keep/KeepRequestParticipationTests.cs` | New: 41 unit tests |

---

## Phase 8-B5 Session 3 Discovery Checkpoint — Assignment / Watch / Mute

**Pre-work complete**
**Status:** Decisions ADR-222..235 locked. Ready for bounded implementation sessions.
**Next free ADR:** ADR-236
**Build logs:** `docs/build-log/040-phase-8-b5-session-3-assignment-watch-mute-decisions.md` (decisions) · `docs/build-log/039-phase-8-b5-claude-coding-sessions.md` (3A-3D implementation split)

Locked implementation split:

| Session | Goal |
|---|---|
| 3A | Domain + persistence invariants for participation changes and `ParticipationChanged` event metadata |
| 3B | API/application services plus compact eligible participant lookup |
| 3C | Detail/list read models, internal timeline metadata, list responsible-name/Owner-Admin assignment metadata |
| 3D | Cross-slice verification, docs, and completion gate |

---

## Phase 8-B5 Session 1 — COMPLETE

**Pre-work complete**
**Tests:** 537/537 (299 unit · 14 arch · 224 integration)
**ADRs in scope:** 164..218
**Next free ADR:** 219
**Build logs:** `docs/build-log/038-phase-8-b5-request-list-triage-external-contact-decisions.md` (decisions) · `docs/build-log/039-phase-8-b5-claude-coding-sessions.md` (implementation spec)

---

### Session 2A — External Contact Schema + Domain — COMPLETE

**Tests:** 355 unit · 14 arch (integration tests pending migration apply)
**Migration:** `20260617224828_AddExternalContactEventFields` — 5 nullable columns on `keep_request_events`
**Next free ADR:** ADR-219 (unchanged)

---

### Session 2D — External Contact Completion Gate — COMPLETE

**Tests:** 355 unit · 14 arch · 251 integration (620 total — all green)
**Next free ADR:** ADR-222
**No new code written** — verification and docs only.

---

### Session 2C — OffSeason Freeze — COMPLETE

**Tests:** 355 unit · 14 arch · 251 integration (620 total)
**ADR implemented:** ADR-221
**Next free ADR:** ADR-222

---

### Session 2B — External Contact API + Detail Timeline — COMPLETE

**Tests:** 355 unit · 14 arch · 240 integration (609 total)
**ADRs implemented:** 197, 199, 200, 202, 203, 207, 209, 211, 215, 216; new ADR-219, ADR-220
**Next free ADR:** ADR-221

---

### Session 2 discovery checkpoint — External Contact Logging

**Status:** Decisions ADR-196..218 locked. Ready for implementation in bounded sessions.

---

## Phase 8-B4 Service Request Detail Enrichment — COMPLETE

**Tests:** 510/510 (280 unit · 14 arch · 216 integration)
**ADRs:** 145..163 implemented
**Build logs:** `docs/build-log/036-phase-8-b4-service-request-detail-enrichment-decisions.md` · `docs/build-log/037-phase-8-b4-service-request-detail-enrichment-implementation.md`

---

## Phase 8-B3+ Closed-Request Feedback — COMPLETE

**Tests:** 500/500 · ADRs 135..144 · `docs/build-log/035-phase-8-b3-plus-feedback-implementation.md`

---

## Phase 8-B3-beta — COMPLETE

**Tests:** 487/487 · ADRs 118..134

---

## Phase 8-B3-alpha — COMPLETE

**Tests:** 471/471 · ADRs 118..134 (decision gate)

---

## Phase 8-B2-delta — COMPLETE

**Tests:** 471/471 · ADRs 116..117

---

## Phase 8-B2-gamma — COMPLETE

**Tests:** 468/468 · ADRs 111..114

---

## Phase 8-B2-beta — COMPLETE

**Tests:** 454/454 · ADRs 108..110

---

## Phase 8-B2-alpha — COMPLETE

**Tests:** 436/436 · `PATCH /keep/requests/{requestId}/status`

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
- **`Results.Problem` extension shape:** extension dict entries land at the top level of ProblemDetails JSON, not under an `"extensions"` key. Test assertions must use `ReadFromJsonAsync<JsonElement>()` then `.GetProperty("code").GetString()`.
- **External contact logging/capture** implemented in B5 Sessions 2A-2C.
- **`businessName ?? string.Empty`** — persistence returns null if account missing post-auth; never expected in production.
- **`KeepResponsePolicy` defaults** (first=60, standard=240, priority=60 min) apply when no policy row exists for an account; silent fallback by design.
- **Negative feedback on Closed raises attention** — intentional exception to terminal-no-attention posture (ADR-138).
- **Feedback `WasResolved` is `bool?` at API layer** — null signals missing flag, validated before service. Domain method takes `bool`.
- **Always use `dotnet build --verbosity minimal`** — `dotnet build -q` is passed to MSBuild as `-q` (question build) and fails; `--verbosity minimal` is the correct quiet mode.
- **Next free ADR: ADR-301.**
- **B4 mapper signature:** `ToDetailResult` now takes `AccountUserRole role`, `bool canOperate`, `Guid currentUserId` — all callers updated; write services pass `canOperate: true`.
- **Participant `DisplayName`** computed in persistence (two-query approach retained; User.Name projected in the AccountUsers query via EF navigation LEFT JOIN).
- **`KeepRequestStatus.Scheduled = 7`** — added to `MapStatus` in B5 service rewrite.
- **`FirstResponseDueAtUtc` gap** — fixed in B5. `KeepRequest.Create` now requires `firstResponseTargetMinutes` (required param, position 10). All call sites updated.
- **Post-close ranking fix** — `isPostClose` must be checked before priority band in `ComputeRankingGroup`.
- **`post_customer_update.ClearsAttention`** — state-aware, not static.
- **`ComputeSeverity` first-response-pending** — fixed in B5 completion. Now returns `"attention"` for first-response pending.
- **`CanLogExternalContact` OffSeason gap** — Fixed in 2C.
- **`ExternalContactInvalidDirection` added in 2B** — was omitted from the 2A error set despite being in ADR-207 scope.
- **External contact `ExternalContactChannel` in DTO** — both `CommunicationChannel` and `ExternalContactChannel` on `KeepRequestEventItem`.
- **Session 3B: Operator self-assign blocked** — DEF-045; any Operator `PUT /responsible` returns 403 `ParticipationOperatorCannotAssignOther`. Unblocked in 4C.
- **Session 3B: `GetActorDisplayNameAsync`** — fixed in 3C to use `User.Name.Trim() ?? Email.Trim()`, matching `GetParticipantTargetAsync` convention.
- **Session 3C: 4-flag participation metadata** — `CanWatch` (not yet participating), `CanUnwatch` (currently watching), `CanMute` (participating + notifications on), `CanUnmute` (participating + notifications off). All 4 require `!IsTerminal`. `CanAssignResponsible` requires `isOwnerOrAdmin && canWrite && !IsTerminal`.
- **Minimal API DELETE body** — nullable body parameters on `MapDelete` endpoints require `[FromBody]`; without it the app fails to start at route data source initialization. Fixed on `DELETE /responsible` and `DELETE /watchers/{id}`.
- **`MapEventType` must be exhaustive** — `ParticipationChanged = 10` was missing; found during 3B testing. Pattern: every new `KeepRequestEventType` value must be added to `MapEventType` before integration tests run against any service that commits that event type.
- **OffSeason participation write blocking** — all 4 participation write services (`ManageResponsibleService`, `ManageWatcherService`, `SelfWatchService`, `MuteService`) use `RequestImplementsAllowedInOffSeason: false` and `|| decision.IsReadOnly` in `AuthAsync`; fires before request load or target user ID validation. Covered by `KeepOffSeasonTests` from 3D onward.
- **4A validation order** — updated in 4B; current locked order: `NormalizeView` → unknown view → `ValidateDateFormats` → status slug → attentionReason slug → `ValidateContradictions` → view role auth → Operator unassigned gate → limit range → cursor decode/fingerprint.
- **4A cursor fingerprint normalization** — null view and "default" produce identical fingerprints. Cursor from `GET /keep/requests` is reusable with `GET /keep/requests?view=default`.
- **4A `KeepRequestListQueryBinding`** — static class in `OpHalo.Api.Keep`; handles HTTP structural concerns; `Program.cs` stays thin. ADR-287.
- **4A HMAC key** — `Keep:RequestListCursorSigningKey` must be present in all environments; test factory uses 32-byte all-zeros key.
- **4B cursor sentinels** — `HistorySortSentinel = 0` (history keyset cursor), `FeedbackReviewSortSentinel = 99` (feedback_review in-memory cursor). Sentinel values must not collide with B5 ranking groups (1–8).
- **4B history keyset** — `WHERE TerminatedAtUtc != null` guard on history queries even though terminal records should always have this set (data invariant protection). `ORDER BY TerminatedAtUtc DESC, Id ASC`. Keyset: `WHERE terminated_at < cursorAt OR (terminated_at = cursorAt AND id > cursorId)`.
- **4B Operator unassigned gate removed in 4C** — `view=unassigned` now accessible to Operators; `GetViewCountsAsync` now returns real unassigned count for all roles.
- **4B `feedback_review` authorization** — `RequestListHistoryViewForbidden → 403` for Operators (not 400). Applied to: `feedback_review`, `closed_history`, `cancelled_history`, `all_history`. These view names are public API contract and drive the explicit 403.
- **4B `FeedbackComment` visibility** — included in Q search only when `filters.IsOwnerOrAdmin = true` in the EF LINQ predicate. Operator/Viewer Q search does not scan feedback comments.
- **4B view counts always populated** — `GetViewCountsAsync` is called on every request in 4B; `viewCounts` is no longer null in the response.
- **4C `RowContext`** — string field on `KeepRequestSummary`; computed by `ComputeRowContext` in `ToSummary`; priority order: feedback_review → closed_history → cancelled_history → needs_attention → first_response → waiting_on_customer → unassigned_available → active_work.
- **4C `CanSelfAssignFromList`** — bool on `KeepRequestParticipationInfo`; `view=="unassigned" && !isOwnerOrAdmin && canOperate && !isOffSeason && !r.IsTerminal && isUnassigned`; true alongside `needs_attention` rowContext (separate signals).
- **4C stale Responsible** — stale rows have `DetachedAtUtc==null`; excluded from unassigned DB view; `ResponsibleCount > 0` → not self-assignable.
- **4C `ParticipationOperatorCannotAssignOther`** — now fires only for `target != self`; self-assign path proceeds.
- **4C `RequestListViewNotYetAvailable`** — error code retained in `KeepRequestErrors.cs` and `ErrorHttpMapper.cs` but no longer returned by the list service.
- **5B OffSeason returns `Forbidden`** — all Keep write services (including `MarkFeedbackReviewedService`) return generic `auth.forbidden` for OffSeason/blocked account, not `KeepRequest.ReadOnly`. ADR-276's listed error was aspirational; uniform Forbidden convention is the implemented contract.
- **5B concurrency gap (DEF-074)** — `KeepRequest` has no concurrency token; concurrent feedback reviews could both commit. Domain guard catches sequential duplicate via `FeedbackAlreadyReviewed`; true-race window deferred to cross-cutting OCC slice.
- **`MarkFeedbackReviewedService` is Owner/Admin only** — role check on `userSnapshot.Role` fires immediately after the user snapshot load, before account snapshot and permission policy checks. This differs from Operator-accessible services where role restrictions are expressed in domain or command guards.
- **5C `FeedbackReviewAgeBucket`/`FeedbackReviewDueAtUtc` on list summaries** — added to `KeepRequestSummary`; null for all non-feedback-review rows; `isPostClose` is the correct gate (Closed + UnresolvedFeedback + attention raised implies unreviewed + negative by domain invariant).
- **5C OffSeason closed customer page** — `ComputeAllowedActions` `Closed + isOffSeason` case precedes the `feedbackAlreadySubmitted` check. Active-status pages are unchanged.
- **`feedback_review` view excludes reviewed requests** — `MarkFeedbackReviewed` clears `UnresolvedFeedback` attention; existing `AttentionLevel != None` filter handles exclusion automatically.
- **Phase 8-B5 complete.** Next phase TBD from build plan.
