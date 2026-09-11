# Production-Readiness Solution Audit

**Opened:** 2026-09-10 · **Context:** running alongside the announcements + feedback (GAP-038) work,
ahead of the HVAC supervised pilot.

## How this runs

- One vector per session. Do **not** attempt more than one vector in a single pass — each is a
  codebase-wide read and will otherwise be shallow.
- Each vector produces **findings only**, recorded in its section below: `file:line`, the concrete
  failure scenario, severity (`blocker` / `pilot-risk` / `hardening` / `ok`), and a recommendation.
- Remediation is **not** done here. Each confirmed blocker/pilot-risk becomes a workboard item or a
  DEF ticket and goes through the normal batch gate.
- Fan-out reads (enumerate endpoints, find query sites) go to `Explore` / `general-purpose`
  subagents; synthesis and the write-up stay in the driving session.
- Scope note: repository docs are authoritative (`workboard.md`, `decision-index.md`). `_reference/`
  is not in scope and is never edited.

## Status board

| # | Vector | Status | Session | Blockers | Pilot-risks |
| --- | --- | --- | --- | --- | --- |
| 1 | Multi-tenancy, security & entitlement gating | Done 2026-09-10 | 3× Explore fan-out | 0 | 1 (F1.6) |
| 2 | State concurrency & transactional integrity | Done 2026-09-10 | 3× Explore fan-out | 0 | 5 (F2.1–F2.5) |
| 3 | Backend performance & database optimization | Done 2026-09-10 | 3× Explore fan-out | 0 (3 GA-blockers) | 6 (F3.1–F3.6) |
| 4 | Client reflow, UX state & layout resilience | Done 2026-09-10 | 3× Explore fan-out | 2 (F4.1–F4.2) | 8 (F4.3–F4.10) |
| 5 | Edge cases & offline / network resilience | Done 2026-09-10 | plumbing pass | 1 (F5.1) | 3 (F5.2–F5.4) |
| 6 | Public surface & rate limiting | Not started | — | — | — |
| 7 | File size & solution architecture (+ dependency scan) | Not started | — | — | — |
| 8 | Auth & session security | Done 2026-09-11 | 3× Explore fan-out | 0 | 6 (F8.3–F8.5, F8.9–F8.11) |
| 9 | HTTP & transport hardening | Not started | — | — | — |
| 10 | Deploy & release safety | Not started | — | — | — |
| 11 | Multi-instance / horizontal-scaling readiness | Not started | — | — | — |

## Workboard mapping

Confirmed audit work has permanent identifiers: GAP-073 (F4.1–F4.2), GAP-074 (F1.6–F1.7), GAP-075 (Vector 1/2 hardening), GAP-076 (F2.4–F2.5), GAP-077 (F2.1), GAP-078 (F2.2–F2.3), GAP-079 (F3.1–F3.2/F3.4/F3.6), GAP-080 (F3 indexing), GAP-081 (F3.5/F3.13), GAP-082 (F3 hardening), GAP-083 (F4.3), GAP-084 (F4.4–F4.6/F4.13), GAP-085 (F4.7–F4.10/F4.16), GAP-086 (F4 hardening), and GAP-090 (pilot-exit search). Vector 5 adds GAP-091 (F5.1), GAP-092 (F5.4–F5.5); F5.2–F5.3 fold into GAP-073 and F5.6–F5.7 into GAP-075. Feedback/updates operational follow-ons are GAP-087 through GAP-089. Vector 8 adds GAP-093 (F8.3), GAP-094 (F8.4–F8.5/F8.11), GAP-095 (F8.9–F8.10), and GAP-096 (Vector 8 hardening); GAP-093/094/095 are now sequenced as supervised-pilot gates (see disposition).

---

## Vector 1 — Multi-tenancy, security & entitlement gating

**Checklist**

- [ ] Every DB query explicitly scopes by `account_id` / tenant context; route-param IDs cannot
      cross tenant boundaries (IDOR).
- [ ] Optional capability-package routes (e.g. Price Book) enforce
      `account_capability_package_enrollments` server-side in API middleware, not client UI.
- [ ] RBAC (Owner / Admin / Operator / Technician) enforced at the API route level.
- [ ] Price-blind rules (cost/margin hidden from technicians) stripped at the serialization layer,
      not via CSS.
- [ ] All incoming payloads pass strict schema validation before business logic / persistence.

**Method:** plumbing pass in the driving session + 3 parallel `Explore` agents (1A tenant
scoping / IDOR, 1B RBAC, 1C entitlement + validation + price-blind). Reviewed: `SessionAuthenticationHandler`,
`CurrentUser`, all `src/OpHalo.Api/**/*Endpoints*.cs` (~90 mutating routes), Keep + Foundation
persistence layers, the `UserAccessPolicy` / `RolePermissions` / `KeepRequestActionPolicy` stack,
`AccountFeatureAccessResolver`, and the actual-work / price-book serialization layer.

**What holds (no action needed)**

- Session auth: opaque token hashed at rest, Bearer-or-cookie, absolute expiry + revocation +
  sliding inactivity + Active-membership gate (Invited / Suspended / Removed fail closed).
- Tenant scoping: no EF global filter, but the manual `.Where(x => x.AccountId == accountId)`
  convention (accountId always from `ICurrentUser`, never a route param) is applied consistently
  across every inspected persistence method. Nested `{actualWorkId}/{lineId}` routes resolve the
  child in memory off an already-account-scoped aggregate root. Handoff / auth / mobile tokens are
  32-byte CSPRNG, SHA-256-hashed at rest, expiry + single-use.
- RBAC: centralized, not ad hoc — every mutating endpoint delegates to a service that enforces role
  via `UserAccessPolicy.HasPermission` + `RolePermissions`, layered with `KeepRequestActionPolicy`
  and explicit Owner/Admin checks on financial paths. Viewer and Operator are fail-closed on
  everything that matters.
- Entitlement gating (ADR-462): a server-side capability-package check (`IAccountFeatureAccessResolver`,
  fail-closed on unknown plan/key) runs before business logic on **100%** of in-scope route groups —
  Price Book, actual-work, offering-assembly, field-catalog, nudge-rules, quick-scope. A direct API
  call from a non-enrolled account gets `403`.
- Price-blind: field-facing actual-work reads carry no cost/margin fields on the DTO type at all
  (structural omission, not client hiding); the one financial-bearing read
  (`ActualWorkFinancialReadApiService.cs:392`) is hard-gated to Owner/Admin + `AccountingManage` +
  entitlement.
- Enum inputs across sampled endpoints fail closed (`Enum.TryParse` + `IsDefined` / explicit
  allowlist). Most free-text has a domain-level length cap (`KeepRequestErrors`, `CatalogItemErrors`,
  `ActualWorkErrors`, …).

**Findings**

| ID | Sev | Location | Issue | Scenario |
| --- | --- | --- | --- | --- |
| F1.6 | pilot-risk | `Keep.Core/Entities/ProposedScopeLine.cs:103-145`; `Keep.Core/Entities/ActualWorkLine.cs:80,137,146-153` | `ProposedScopeLine.Note` / `OffCatalogDescription` and `ActualWorkLine.Note` have **no length cap** and no `*TooLong` error; quantity checked only `> 0`. | A field user (or scripted client) POSTs multi-MB free-text via `POST /keep/pricebook/proposed-scopes/{id}/field-select` or `.../actual-work/{id}/lines` straight to persistence → unbounded row growth, response bloat, downstream render DoS. Fix: add caps + errors mirroring `KeepRequestErrors`. |
| F1.1 | hardening | `Keep.Infrastructure/.../EfKeepRequestDetailPersistence.cs:65-71, 73-97` | `GetAllEventsAsync` / `GetParticipantsAsync` filter only by `RequestId`, not `AccountId` (sibling write path *does* scope). | Not exploitable today — every one of ~20 callers first resolves the request through an account-scoped query and passes `request.Id`. Defense-in-depth: add `accountId` to both predicates so a future ungated caller can't leak account B's event timeline / staff roster. |
| F1.3 | hardening | `EfKeepCallHandoffPersistence.cs:16-28`; `EfKeepSmsHandoffPersistence.cs:16-28`; `EfKeepIntakeSmsHandoffPersistence.cs:91-104` | Handoff lookups resolve by token hash only; no `AccountId` check on the authenticated staff caller. | Tokens are 32-byte CSPRNG, hashed, expiry + soft-delete checked, per-type tables — unguessable in practice. A staff member in account A holding account B's raw handoff link could read B's customer phone. Add an `AccountId` equality check. |
| F1.2 | hardening | `KeepTokenService.GeneratePageToken` / `EfKeepRequestDetailPersistence.cs:126-143` | `KeepRequest.PageToken` (256-bit CSPRNG) is persisted and looked up in **plaintext**, unlike the hashed public-intake token. | DB-dump / log-leak exposure only. Store a hash. |
| F1.7 | hardening | `ActualWorkLine.cs:97-98,148-149`; `ProposedScopeLine.cs:122-123` | `ActualQuantity` / proposed-scope `quantity` validated only `> 0` — no upper bound or precision cap; raw `decimal` from the command. | `ActualQuantity = 1e28` flows into margin/total projections → overflow / absurd figures in Owner/Admin financial review. |
| F1.8 | hardening | `Api/Accounts/InternalEntitlementsEndpoints.cs:20-27` | `/internal/*` enroll/disable/reenable routes carry only `.RequireAuthorization()` at the route; the Internal-purpose + permission check is in the service. | Ordinary tenant user is denied by the service, but the route is still reachable. Attach a route-level policy so it isn't. |
| F1.9 | hardening | `InternalCapabilityPackageEnrollmentApiService.EnrollAsync` | Raw `featureKey` route string passed into the enrollment service without a visible allowlist check at that layer. | Operator typo / arbitrary key creates a junk enrollment row. Confirm the downstream service validates against `CapabilityPackageFeatureKeys`. |
| F1.10 | hardening | `Api/Keep/KeepEndpoints.cs:1487` (`ActualWorkCreateBody.RequestId`); `Api/Accounts/AccountEndpoints.cs:42-43` (invite email) | Missing `RequestId` binds to `Guid.Empty` with no endpoint guard; invite `Email` checked non-blank but not length-capped at the endpoint. | Low impact (FK lookup fails / domain rejects). Add explicit `Guid.Empty` guards and verify `SendInviteService` caps email length. |
| F1.4 | hardening | `AddInternalNoteService` | Gates on `keep.requests.operate` instead of the dedicated `keep.internal_notes.add` key. | Behaviourally identical today; drifts if the permission maps change. |
| F1.5 | hardening | `POST /keep/requests/{id}/share-intent` → `ClearShareIntentService` | Service name / route verb mismatch (POST → "Clear…"). Gating is correct. | Readability / maintenance only. |

**Disposition:** F1.6 → workboard item (bounded-slice: add length caps + `*TooLong` errors + rejection
tests to the two line entities; likely folds in F1.7 quantity bounds). F1.1–F1.3, F1.8–F1.10 →
batch into one "tenant-scoping & internal-route defense-in-depth" hardening slice. F1.4 / F1.5 →
opportunistic cleanup, no ticket.

---

## Vector 2 — State concurrency & transactional integrity

**Checklist**

- [ ] Mutations carry an expected `version` / timestamp; stale concurrent writes rejected with
      `409` and unsaved client text preserved during recovery.
- [ ] Multi-step operations (account creation on `/auth/exchange`, contact-event logging, draft-visit
      conversion) run inside a DB transaction; partial failure rolls back fully.
- [ ] State-mutating `POST`/`PUT` use idempotency keys or submit lockouts against double-submit.
- [ ] Disabling a package / subscription is a soft revocation — submitted visits, financial
      snapshots, and activity history remain intact and immutable.

**Method:** plumbing pass + 3 parallel `Explore` agents (2A optimistic concurrency, 2B atomic
transactions, 2C idempotency + soft revocation / immutability).

**What holds (no action needed)**

- **Optimistic concurrency — DB-enforced, textbook.** All 8 shared aggregates (KeepRequest,
  ActualWork, ProposedScope, CatalogItem, CatalogCategory, OfferingAssembly, enrollment,
  PriceBookAccountState) use an EF `IsConcurrencyToken` on an app-rotated `Guid ConcurrencyVersion`
  → `UPDATE … WHERE concurrency_version = @original`; two racers cannot both win, loser gets
  `DbUpdateConcurrencyException` → stable **409** in every adapter. Expected version carried as a
  strict, fail-closed request header (absent → 400 required, malformed → 400 invalid). Child-collection
  edits rotate the parent token. Cross-aggregate ops add `BeginTransactionAsync` +
  `SELECT … FOR UPDATE` in a consistent lock order; price publishing serialized by an account-level
  publish lock; work-signal rows use atomic `INSERT … ON CONFLICT`.
- **Transactions — heavy paths are atomic.** Visit submit, review, financial resolution/disposition,
  replacement/supersession, price-book publish (Serializable), invite accept (tx + savepoint for
  the user-create race), public intake commit, `/auth/exchange` new-account graph (two-phase save
  for the circular Account↔AccountUser FK) all wrap every step — including cross-service signal
  reconciliation on the shared request-scoped `OpHaloDbContext` — in one transaction.
- **Soft revocation & immutability — strong, consistent.** Enrollment disable is a pure state flip
  (`Status → Disabled`, row kept, re-grant reuses it); no Keep/financial data is read or altered
  when a feature is turned off (access recomputed live). Member remove/suspend is a soft
  membership-status change — `UserId` / `Email` retained, authored `KeepRequestEvent`s, notes,
  ActualWork recorder/performer links and participation rows all survive. **Every** account-scoped
  entity carrying audit or financial history is `OnDelete(Restrict)` to `Account` — an account with
  data cannot be deleted and no parent→child cascade wipes history. ActualWork is immutable after
  Submit: every field/line mutator guards `Status != Draft`; post-submit changes go only through
  `MarkReviewed` / `Supersede` (both single-shot) or append-only financial-resolution entities;
  line snapshots are fixed at creation.
- No `Idempotency-Key` infrastructure exists anywhere in the repo — context for F2.4 / F2.5 below.

**Findings**

| ID | Sev | Location | Issue | Scenario |
| --- | --- | --- | --- | --- |
| F2.1 | pilot-risk | `ExchangeAuthService.CreateContinuationAsync` (called :130 / :161); `ConsumeCodeAsync` :119 / :152 | On the `/auth/exchange` **continuation** branches (multi-workspace, name-required), the magic-link code is consumed by a standalone autocommit `ExecuteUpdateAsync` **before** a separate `PostAuthContinuation` insert — no shared transaction. | If the continuation insert fails: code is burned, no continuation row, and the continuation is the only path to a session on these branches. Retrying the same link → `AlreadyConsumed`; user must request a fresh email. Fix: one `BeginTransactionAsync` wrapping consume + insert (the new-account graph path already does this). |
| F2.2 | pilot-risk | `ErrorHttpMapper.CreateProblem:437-460` | The **409 conflict response body is opaque** — `title:"Conflict."` + a code, nothing else. It does not return the current server `ConcurrencyVersion` or entity state. Success responses *do* return the new version. | On an aggregate shared between office and field (ActualWork, ProposedScope), a user who hits a 409 must issue a fresh GET and lose their in-progress edit to recover — no merge affordance. Return current version + state on the conflict path. |
| F2.3 | pilot-risk | `CatalogItem.cs:237-241` (`ApplyPublishedPrice` deliberately does **not** rotate `ConcurrencyVersion`); `PriceBookEndpoints.cs:108` | A `publish-price` does not invalidate a concurrent in-flight catalog-item **header** edit's expected version. | An operator editing an item's header keeps a valid optimistic token straight through a price publish they never saw; their save succeeds on stale context. Either rotate the token on price publish, or document + surface the intentional split so header edits re-fetch. |
| F2.4 | pilot-risk | `CreateKeepPublicIntakeService.cs:166-208` | `POST /keep/public-intake/token/{token}` and `/slug/{slug}` have **no double-submission protection** — the retry loop guards only token/reference-code collision; rate limiting throttles but does not dedupe. | Customer double-taps submit → two `KeepRequest` rows + two `RequestCreated` events + a duplicate queue item for one job. Add a dedup window on (account, canonical phone, description hash) or an idempotency token from the form. |
| F2.5 | pilot-risk | `KeepEndpoints.cs:185-198`; `CreateBusinessRequestService` | `POST /keep/requests` (staff create) has **no server-side double-submission protection** — creates carry no version header; only existence checks (collision guards, not dedup). Client submit-lockout is the only guard. | Staff double-click → two requests for the same job. Lower volume than F2.4 (authenticated, likely has a client lockout) but same class. |
| F2.6 | hardening | `ExchangeAuthService.HandleNewAccountAsync:226` | Session issue runs after the account graph is committed (outside its tx). | Documented persist-first design: on failure the account is durable and consistent, frontend gets 503 → `/signin`, plain re-signin works. Acceptable; noted for completeness. |
| F2.7 | hardening | `FeedbackSubmissionService.SubmitAsync` :85/:97/:110; `EfFeedbackPersistence` | Persist `Pending` row → external founder POST → persist delivery metadata/scrub, no transaction. | If the second save fails or the process crashes between them: the row keeps the **raw unscrubbed body + contextJson** until a later successful attempt or the 30-day sweep, and the founder gets a duplicate message (retry worker re-POSTs same `Id`). DB-before-external ordering is correct; row is immediately retry-eligible. |
| F2.8 | hardening | `KeepEndpoints.cs:282` (`share-intent`), `:294` (`sms-handoff`), `:330` (`call-handoff`) | These mutations take no `X-Keep-Request-Version` header. | Not a lost-update (fresh load + EF token on the appended event) but the action can proceed on a stale client view with no precondition. |
| F2.9 | hardening | `InternalEntitlementsEndpoints.cs:57/68` | Expected version comes from the JSON **body** (`ConcurrencyVersion`), not a header. Omitted field → `Guid.Empty` → fails the match → **409** instead of a 400 "version required". | Client that forgets the field gets a misleading conflict, not a validation error. Align with the header pattern used everywhere else. |
| F2.10 | hardening | `EfInvitePersistence.CommitSendInviteAsync:74-86` | Unlike `CommitAcceptInviteAsync`, this has no unique-violation catch. Two **concurrent** `POST /accounts/me/invite` both read `existing == null`, both `Add`; the partial unique index rejects the second → unhandled exception → **500**. | Sequential double-submit is fine (resend path). Concurrent → 500 instead of 200/409. Add the catch. |
| F2.11 | hardening | `FeedbackEndpoints.cs:31`; `FeedbackSubmissionService` | `POST /feedback` is append-only; a double-tap inserts two rows. Rate-limited per `account_user`. | Low consequence; optional client idempotency key or short-window per-user dedup. |
| F2.12 | hardening | `FeedbackSubmissionConfiguration.cs:55-68` | `FeedbackSubmission` cascade-deletes from **both** `Account` and `AccountUser`. | No hard-delete path reaches it today (member remove is a soft flip; `AccountUser → Account` is Restrict). Latent: if a hard-delete path is ever added, that user's feedback vanishes silently. Switch to `Restrict` or document the constraint. |
| F2.13 | hardening | `PriceBookVersionLineConfiguration.cs:80-85` | `PriceBookVersion → PriceBookVersionLine` is `Cascade`. | Financial history is snapshot-based (figures copied onto `ActualWorkLine` at creation, no update path) and `ActualWorkLine → PriceBookVersionLine` is Restrict, so submitted-visit financials are safe. Only relevant if a published-version hard-delete path is ever added. |

**Disposition:**
- F2.4 + F2.5 → workboard item: server-side double-submit protection for the two request-creation
  paths (dedup window or accepted idempotency token). Pilot-risk.
- F2.1 → workboard item (small): wrap the `/auth/exchange` continuation consume + insert in one
  transaction. Pilot-risk.
- F2.2 + F2.3 → workboard item: concurrency-conflict recovery — return current version/state on 409,
  resolve the `publish-price` token-rotation split. Pilot-risk.
- F2.6–F2.13 → fold into the AUDIT-V1-B hardening slice (or a Vector-2 sibling): feedback
  persist/notify atomicity, missing version headers, invite 500, cascade tightening.

---

## Vector 3 — Backend performance & database optimization

**Checklist**

- [ ] List endpoints (Request Queue, Activity Logs, Price Book catalog) free of N+1 ORM patterns —
      eager loading / join constraints / batch fetch.
- [ ] Compound indexes exist for high-frequency filters (`account_id + status`,
      `account_id + priority`, `account_id + updated_at`).
- [ ] Every list endpoint enforces a hard record cap; no unbounded arrays into memory.
- [ ] Background tasks (SMS/email, webhooks) have retry backoff, dead-lettering / abandon, and
      explicit error handlers — a failed notification never crashes the API.

**Method:** plumbing pass + 3 parallel `Explore` agents (3A N+1 / query efficiency, 3B indexing,
3C pagination caps + worker resilience). **"GA-blocker"** below = holds at pilot scale (one HVAC
business, low-hundreds of requests) but is a real design defect that must be fixed before broad
scale-out.

**What holds (no action needed)**

- Lazy loading is OFF, every audited read is `AsNoTracking` with an explicit query — no hidden N+1.
- The **history** list, `/available`, `/lookup`, `/related-work`, `/me/badge`, catalog list,
  offering-assembly list are all properly keyset-paginated, DTO-projected, and clamped (page size
  1..100 / 1..50, out-of-range → 400).
- List related-data is batched (`participant summaries`, 3-tier preview events, note presence,
  financial-review counts) — all `WHERE id IN (page ids)`, gated on `page.Count > 0`, never per-row.
- Feedback delivery poll index `(delivery_state, next_attempt_at_utc)` is well-formed; the retention
  sweep uses batched `FOR UPDATE SKIP LOCKED`.
- `FeedbackMaintenanceBackgroundService` and `RemovedLineSnapshotCleanupService`: loop-body
  try/catch, fresh per-iteration DI scope, `OperationCanceledException` handled on shutdown,
  jittered interval, no self-overlap, multi-replica-safe batched deletes.
- `GET /updates` feed is hard-capped (JSON schema: 100 entries / 50 guides / ≤12k-char bodies; 4 MiB
  read cap).

**Findings**

| ID | Sev | Location | Issue | Scenario |
| --- | --- | --- | --- | --- |
| F3.1 | pilot-risk (GA-blocker) | `KeepRequestListPersistence.GetActiveViewRequestsAsync:69-177`; `GetKeepRequestListService.cs:367-434, 613` | **Active-view request list is unbounded** — `ToListAsync()` of full `KeepRequest` entities (every column) for the whole account, no SQL `LIMIT`; sort, DTO build, and cursor `Skip` all happen in memory. The cursor is a linear scan, so page 5 re-runs the entire load + sort. Affects `default`, `assigned_to_me`, `watching`, `needs_attention`, `feedback_review`, `needs_status_check`, `ready_to_close`. | Fine at hundreds of active requests, degrades linearly with account age. The correct pattern (DB-side keyset + `Take(limit+1)`) already exists in the same file's history path. |
| F3.2 | pilot-risk (GA-blocker) | `EfActualWorkFinancialReviewPersistence.GetUnreviewedQueueAsync:12-29`; `ActualWorkFinancialReadApiService.cs:170` | **Actual-work review-queue is unbounded** — no `Take`, no `limit` parameter in the contract, no cursor, `.Include(x => x.Lines)`. Grows in exactly the dimension (unreviewed backlog) that expands when the office falls behind. Also has **no supporting index** — `(account_id, request_id)` has the wrong second column for `WHERE account_id AND status='Submitted' AND reviewed_at_utc IS NULL AND superseded_at_utc IS NULL ORDER BY submitted_at_utc`. | Needs a pagination contract added (not just a clamp) + a partial index. |
| F3.3 | pilot-risk | `KeepRequestEventConfiguration` — index is `(request_id)` single-column | `keep_request_events` needs `(request_id, occurred_at_utc)`. It serves the `ORDER BY occurred_at_utc` on detail-open **and** the `GROUP BY request_id … MAX(occurred_at_utc)` + self-join in the preview-event query that runs **3× per list-page render**. | Event rows accumulate faster than request rows (dozens per request). The Keep index most likely to bite during pilot as history grows. `CREATE INDEX … (request_id, occurred_at_utc); DROP … (request_id)`. |
| F3.4 | pilot-risk | `EfKeepRequestDetailPersistence.GetAllEventsAsync:65-71` | Loads the **entire** request event timeline as full entities (incl. `Content` message bodies), no `Take`, no projection — on the detail read **and** rebuilt on ~20 mutation paths (add-note, status-change, add-update, …). | Every write to a chatty long-lived request pays the full timeline read + transfer. Add a bounded window / projection; pairs with F3.3. |
| F3.5 | pilot-risk (GA-blocker if API > 1 replica) | `EfFeedbackPersistence.GetDueForRetryAsync:32-40`; `FeedbackDeliveryWorker.cs:84-99, 93` | `FeedbackDeliveryWorker` claims due rows with a plain `SELECT … LIMIT 100` — **no `FOR UPDATE SKIP LOCKED`, no lease/claim marker**. Single-instance assumption. Also: no per-pass timeout on the sequential founder-channel calls, and `NotifyAsync` is not wrapped — a throwing notifier aborts the pass before the retention sweep. | On 1 replica (the documented pilot posture, [Vector 11]) it is correct. On ≥2 replicas: every replica loads the same rows, `AttemptCount` burns N× faster → premature `Abandoned` + duplicate founder alerts. A hung founder channel stalls all retries + retention indefinitely. |
| F3.6 | pilot-risk | `KeepRequestListPersistence.GetViewCountsAsync:229-326`; `GetKeepRequestListService.cs:461` | Every list call fires **7 sequential `CountAsync`** round-trips for the view-count badges (several with correlated participant `EXISTS`), on top of ~8–12 other serial awaits in the same request. | Fixed count (not result-size-dependent) but 7 added serial DB waits per page load. Collapse to one grouped-aggregate query or `Task.WhenAll` on a second connection. |
| F3.7 | hardening | `ActualWorkHistoryReadApiService.cs:179`; `ActualWorkFinancialReadApiService.cs:224, 311` | Actor-display-name resolution is N+1 — `await …GetActorDisplayNameAsync(id)` inside `foreach`; `IKeepRequestOperatePersistence` exposes only the singular seam. N = distinct staff on the visit/request (~1–5, unbounded by team size). Memoized per id. | Add `GetActorDisplayNamesAsync(IReadOnlyList<Guid>)` with `WHERE Id IN (…)`. |
| F3.8 | hardening | participant/recorder/performer candidates; `GET /accounts/me/members`; catalog categories; nudge-rule lists; per-request actual-work history & pending-reviews | Unbounded (no `Take`) but naturally small — bounded by team size or per-request row counts. | Add defensive caps; low urgency. |
| F3.9 | hardening | `keep_requests`, `keep_request_events`, `keep_actual_works` configs | Redundant single-column indexes: `ix_keep_requests_account_id` and `ix_keep_request_events_account_id` (both covered by the `(account_id, …)` unique AK prefix); the `(account_id, superseded_by_actual_work_id)` conv-FK index (covered by the partial unique). `ix_keep_requests_account_attention`'s 3rd column is unreachable (every consumer does `attention_level != None`, an inequality on col 2). | Pure write-amplification + planner noise. Drop before scale. |
| F3.10 | hardening | history list; catalog browse | `ORDER BY terminated_at_utc DESC, id` (history keyset) and `ORDER BY display_name, id` (catalog browse) are uncovered → sort per page. Both are keyset-paginated so pages stay small; spills as volume climbs. | Cheap partial/compound indexes; add now. |
| F3.11 | hardening | `ux_keep_actual_works_open_draft`; `ix_keep_request_participants_request_id` | Partial unique indexes omit `deleted_at_utc IS NULL`. Safe today (ActualWork discard is a hard delete; participants are reattached, not soft-deleted). Latent: a soft-deleted row would hold the unique slot while invisible. | Add the predicate. |
| F3.12 | hardening | `feedback_submissions` | The pending-metrics query (`created_at_utc <= @t`) and the retention sweep (`delivered_at_utc`, `created_at_utc` by state) are unindexed. Background / low-frequency, `SKIP LOCKED` batched. | Two small partial indexes if wanted. |
| F3.13 | hardening | `src/OpHalo.Worker` | Dead scaffold — unmodified `dotnet new worker` template (logs a line/second, does nothing), **never deployed** (Dockerfile builds `OpHalo.Api` only), but drags `Keep.Infrastructure` + Npgsql + EF + `AWSSDK.S3` into its build. All real background work runs in-process in every API replica. | Delete it, **or** adopt it as the real background host — which would also give `FeedbackDeliveryWorker` a natural single-instance home and resolve F3.5. |
| F3.14 | hardening | `RemovedLineSnapshotCleanupService` | No per-run timeout. Batched delete is inherently bounded — low concern. | — |

**Won't scale past pilot (flag for pilot exit, not a pilot fix)**

- `filters.Q` runs 5× `LOWER(col) LIKE '%q%'` OR-ed, including `description` (varchar 4000) and
  `feedback_comment` (2000); catalog search does the same on item/alias text. Unindexable without
  `pg_trgm` GIN. A single account at a few thousand requests turns every filtered list render into
  multiple full-text substring scans. Add `pg_trgm` GIN on `keep_requests (customer_name,
  reference_code, description)` and catalog equivalents before GA.

**Disposition:**
- F3.1 + F3.2 + F3.4 → workboard item: **server-side pagination for the active request list, the
  event timeline, and the review-queue** — port the existing keyset pattern. Pilot-risk / GA-blocker.
- F3.3 + F3.10 + F3.9 → workboard item: **index tune** — add `(request_id, occurred_at_utc)`,
  history + catalog-browse compounds; drop the 3 redundant indexes; one migration.
- F3.2 review-queue index folds into the pagination item or the index item.
- F3.5 → workboard item: `FeedbackDeliveryWorker` — `FOR UPDATE SKIP LOCKED` claim + per-pass
  timeout + wrap `NotifyAsync`. (Or resolve structurally via F3.13.) Gate before any multi-replica
  deploy — cross-ref [Vector 11].
- F3.6 → fold into the pagination item (same file, same read path).
- F3.7, F3.8, F3.11–F3.14 → AUDIT-V1-B-style hardening slice.
- `pg_trgm` search → deferred-topics, pilot-exit.

---

## Vector 4 — Client reflow, UX state & layout resilience

**Checklist**

- [ ] When optional capability packages / onboarding steps are absent, the DOM omits the unused
      tabs / toolbars / panels — no empty gaps or `visibility:hidden` whitespace.
- [ ] Active package enrollments and role permissions resolve during `/auth/exchange` and update the
      local store without a full page reload.
- [ ] Major sections (Queue, Workbench, Memory Rail) wrapped in error boundaries — a sub-component
      crash does not blank the screen.
- [ ] Optimistic UI reverts and shows an accessible toast when the API write fails.

**Method:** plumbing pass + 3 parallel `Explore` agents (4A error boundaries, 4B conditional reflow
+ claims resolution, 4C optimistic UI + 409 recovery). Frontend = `web/ophalo-app`; auth entry =
`web/ophalo-web`. **This is the weakest vector — the first with genuine pilot blockers.**

**What holds (no action needed)**

- Request-detail mutations are almost all **non-optimistic** (await server → replace with server
  truth), consistently — so there is essentially no "failed write left on screen looking saved"
  class of bug. The one optimistic surface (internal priority) reverts correctly on error with a
  `role="alert"`.
- `PrimaryActionControl` is a strong reference implementation: payload snapshot for exact-replay
  Retry, `ConnectionFailureBanner`, retry success announced via a root-mounted `liveAnnouncer`.
- The **Actual Work** and **Proposed Scope** composers have a genuinely well-built shared 409
  recovery path (`reconcileAfterConflict`): refetch authoritative state, distinct reload-failure
  notice with Retry, `role="status" aria-live="polite"`, blurred text fields survive via
  autosave-on-blur + re-key from server.
- Layout reflow for an absent Price Book package: nav item and request-detail pricing cards
  conditionally render to `null` (no empty shells / dead toolbars); `#/pricebook` deep-links get
  dedicated full-screen states. Onboarding hides cleanly.
- Auth handoff (`exchange` / `continue`, cookie-based, full-document nav into the SPA), the
  `401 → signin` redirect with a module-level loop guard, and mid-session removal all degrade
  cleanly. `RequestDetailStates` / `RequestListContent` have proper 403 / 404 / generic error
  states. No `React.lazy`, so no chunk-load-without-boundary gap.

**Findings**

| ID | Sev | Location | Issue | Scenario |
| --- | --- | --- | --- | --- |
| F4.1 | **blocker** | `RequestDetail.tsx:609-610` (draft state); `App.tsx:557`, `RequestWorkbenchShell.tsx:261` (no `key={requestId}`, no reset effect) | The customer-reply draft and internal-note text live in `RequestDetail` state; `RequestDetail` is **not keyed by `requestId`** and nothing resets the draft when the request changes. | Operator drafts "Hi Jane, we'll be there Tuesday…" on request A, clicks **Next**, the text is now in request B's composer → "Post & prepare SMS" posts A's message to **B's customer page and texts B's customer**. Wrong-customer communication, no conflict needed. Adding `key` without persistence instead *silently discards* the draft. |
| F4.2 | **blocker** | `BusinessSection.tsx:341-342, 487-489, 537`; `UnifiedComposer.tsx:31-32, 230` | On a 409 the composer sets `disabled={conflictDisabled}` (not `readOnly`) and shows *"…Refresh to see the latest state. Your message is saved here."* The draft is only in React state — no `sessionStorage`, no `beforeunload` guard anywhere in the app. | Operator writes a 600-word reply to an upset customer, hits Post, gets a 409 (teammate changed status). Banner says "Refresh." They press Cmd-R → the entire reply is gone. **The app instructed the destructive action** and the disabled field can't even be selected to copy the text out. |
| F4.3 | pilot-risk (blocker-severity if hit) | `ErrorBoundary.tsx:15, 28-30`; single use at `main.tsx:50` | One app-wide `ErrorBoundary`; `hasError` **never resets**; only affordance is a Reload button that reloads the **same hash route**. No region isolation. | A deterministic render crash on `#/request/<id>` (bad payload shape, unexpected enum) → fallback → Reload → same crash, forever. No path back to the list without hand-editing the URL. Separately: any render error anywhere white-screens the whole workbench and loses queue scroll / filters / composer state. |
| F4.4 | pilot-risk | `App.tsx:171-177, 547-551` | The `["me"]` query is consumed without an `isError` branch; `role` falls back to `"unknown"` which renders a permanent "Loading…" spinner. | `/auth/me` returns 500 / times out after `AuthGuard` already passed from cache → the shell **hangs on a spinner forever**, no retry. |
| F4.5 | pilot-risk | `AuthGuard.tsx:6-30` | `retry: false`; any error with `data` undefined → `redirectToSignInOnce()`. Transient 500 / network blip is indistinguishable from a real 401. | An API outage bounces every active user to the sign-in flow — looks like a mass logout. |
| F4.6 | pilot-risk | `main.tsx` — `initSentry()` (:13), `createRoot(...!)` (:25), `void bootstrap()` (:59, no `.catch`) | Only the `VITE_PUBLIC_BASE_URL` config branch is guarded; every other bootstrap throw / rejection white-screens with no fallback (the `ErrorBoundary` never mounts). `main.config-failure.test.tsx` gives false confidence — it only covers the one handled branch. | Missing `#root`, a throwing `initSentry`, or a failed dev-mock import → blank page. |
| F4.7 | pilot-risk | `useActualWorkCapture.ts:110`, `useProposedScopeCapture.ts:68`; `ActualWorkCard.tsx:45-47` | A non-403 **error** on the per-request capability probe renders `status:"error"` identically to `"hidden"` → `return null`. `RequestDetailActualWorkSection` surfaces history errors but not probe errors. | A transient 500 on `getActualWorkHistory` makes the entire "Record completed work" entry point **silently vanish** for an enrolled operator — no message, only a manual refresh recovers it. |
| F4.8 | pilot-risk | `App.tsx:187-194` (`["capabilityPackages"]` `staleTime: 5min`, no `invalidateQueries` anywhere) vs the live per-request 403 probe | The two entitlement signals diverge; role/enrollment changes have no invalidation and no in-app "your access changed" affordance. | Enable Price Book mid-session → detail cards work immediately, nav pill + `#/pricebook` route stay locked for up to 5 min + a window-focus cycle. Downgrade admin→operator → stale nav pill still shows, click → 403 → a "couldn't check access / try again" message that misdescribes a permission change as an outage. |
| F4.9 | pilot-risk | `conflictDisabled` / `noteConflictDisabled` / `priorityConflictDisabled` — set in ~10 places, reset in **zero** (`PrimaryActionControl.tsx:220`, `BusinessSection.tsx:110/290/488`, `UnifiedComposer.tsx:106`, `NotifyCustomerPanel.tsx:92/116`, `DetailPanels.tsx:829`, `RequestDetail.tsx:127/413`) | Once a 409 fires, the control is dead for the lifetime of the mounted component. `refetchOnWindowFocus` can silently refresh `detail` underneath it. | Operator gets a 409 on "Close request," alt-tabs away and back (detail refetches to current state), returns to a permanently greyed-out button with a stale "Refresh" message. Only a full browser reload fixes it → F4.1 / F4.2 for any composer. |
| F4.10 | pilot-risk | `TeamSection.tsx:82, 313, 508`; `RequestDetail.tsx:571`; `ActualWorkComposer.tsx:795/981/1430/1530/1782/2107`; conditionally-mounted `aria-live` at `PrimaryActionControl.tsx:261`, `UnifiedComposer.tsx:207`, `BusinessSection.tsx:510` | Write-failure errors are shown visually but not announced — either no `role` at all, or a conditionally-mounted `aria-live` region (which does not fire; `liveAnnouncer.ts` exists precisely to work around this). | A screen-reader operator reassigns the owner, it 409s, "Updated by another team member" appears — nothing is spoken, the sheet just sits there. Fix is in-repo: route through `announcePolite()`. |
| F4.11 | hardening | `main.tsx:15-22` | `QueryClient` has no global `QueryCache` / `MutationCache` `onError`. | Failed mutations with no local handler fail silently. |
| F4.12 | hardening | `ExchangeClient.tsx:57-59`, `CompleteSignInScreen.tsx:61-63` | Auth redirect falls back to `http://localhost:5173` if `NEXT_PUBLIC_APP_BASE_URL` is unset — opposite of the app's fail-loud `ConfigurationError`. | A successful prod sign-in with the var unset silently sends users to localhost. |
| F4.13 | hardening | `AuthGuard.tsx:6`, `App.tsx:171`, `Requests.tsx:285` | Three `["me"]` observers, three different `staleTime` / `retry` configs. Identity freshness is currently an accident of the inconsistency, not a policy; generates redundant `/auth/me` traffic. | Consolidate to one `useMe()` hook. |
| F4.14 | hardening | `ProposedScopeComposer.tsx:76`, `ActualWorkComposer.tsx:252`, `ComposerSearchAndAdd.tsx:130` | `onError: () => onConflict()` treats *any* error (network, 400) as a concurrency conflict. | A dropped connection on submit surfaces the "reload failed" notice instead of "check your connection." Misleading, not data-losing. |
| F4.15 | hardening | `PriceBook.tsx:376-389` | `#/pricebook` not-entitled copy always says "isn't included in your plan / talk to your account owner" — wrong for an operator/viewer deep-linking the URL (their `["capabilityPackages"]` query is disabled so `entitled` is always false). | Misleading if a URL is shared; no nav path leads there for those roles. |
| F4.16 | hardening | `RequestDetail.tsx:622` holds `refetch`, never passed to conflict handlers | Outside the two composers with `reconcileAfterConflict`, every 409 handler says "Refresh" with no button to do it. | Compounds F4.1 / F4.2. |
| F4.17 | hardening | `AccountMenu` — no workspace switcher | Multi-workspace users must sign out / back in to switch. | Acceptable for pilot; roadmap note. |

**Disposition:**
- **F4.1 + F4.2 are pilot blockers** — wrong-customer communication and instructed data loss on
  customer-facing text. Recommend a **Now / Next** slot, not Deferred: `key={requestId}` +
  per-request `sessionStorage` draft persistence + `readOnly` (not `disabled`) on conflict +
  `beforeunload` / Prev-Next dirty guard reusing the existing `showDiscardConfirm` alertdialog
  pattern. One coherent frontend slice.
- F4.3 → workboard item: reset `ErrorBoundary` on route change + wrap the workbench / rail /
  account-menu / modals in independent boundaries with a "back to list" affordance.
- F4.4 + F4.5 + F4.6 → workboard item: identity-load error handling (retry on transient, don't
  treat 500 as 401, guard `bootstrap()`).
- F4.7 + F4.8 → workboard item: capability-probe error state (show a retry strip, don't silently
  hide) + entitlement-signal invalidation on `["me"]` refresh.
- F4.9 + F4.10 + F4.16 → workboard item: 409 recovery outside the two good composers (reset the
  disabled flag on version change, in-app refresh button, announce via `announcePolite`).
- F4.11–F4.15, F4.17 → hardening slice.

---

## Vector 5 — Edge cases & offline / network resilience

**Checklist**

- [ ] Field workflows (draft visit entries) survive high-latency / dropped connections — drafts
      persist locally so work is not lost.
- [ ] Dirty-state forms (long customer reply, line-item entry) warn on navigate-away
      (`beforeunload` + in-app route guard).
- [ ] Timestamps stored UTC at the DB layer, converted to the business timezone
      (`America/Chicago`) at the view layer.

**Method:** plumbing pass over the mobile field app (`mobile/ophalo-mobile`), the web workbench
(`web/ophalo-app`), and the backend clock/timestamp seam. Targeted `rg` for `beforeunload`,
local-draft persistence (`AsyncStorage` / `sessionStorage` / query persister), network-state
gating, and every date-formatting call site; read `modal.tsx` (Quick Capture), `RequestDetail.tsx`
dirty guards, `request-detail/helpers.ts`, and `SystemClock.cs`. Heavy overlap with Vector 4
(F4.1 / F4.2 draft loss) — those are not re-litigated here, only extended.

**What holds (no action needed)**

- **Backend timestamp discipline is clean.** `SystemClock.UtcNow => DateTime.UtcNow`; entities
  uniformly name persisted instants `...AtUtc`; only `SystemClock` and the (unwired) worker touch
  wall-clock; API contracts expose `...Utc`-suffixed ISO-8601 strings. UTC-at-the-DB-layer is
  satisfied — the gap is entirely on the view side.
- Mobile Quick Capture **gates** on connectivity rather than letting a submit silently fail:
  `canCreate` requires `isOnline`, Save is disabled, and a visible "No connection — save disabled
  until online" banner shows. `createMutation` distinguishes 4xx ("check the fields") from
  connection errors ("check your connection").
- `formatEventTime`'s relative window ("just now" / "2h ago") is timezone-safe — pure epoch math.
- The web in-app dirty-guard pattern (`showDiscardConfirm` alertdialog + `contentInert` +
  `onDirtyChange` registrar) is a solid, accessible, reusable pattern. The gap is **coverage**
  (the reply composer, `beforeunload`), not design.
- `PrimaryActionControl` exact-replay retry from a payload snapshot (Vector 4) already covers the
  single highest-value mutation against a transient drop.

**Findings**

| ID | Sev | Location | Issue | Scenario |
| --- | --- | --- | --- | --- |
| F5.1 | **blocker** (field-work loss) | `mobile/ophalo-mobile/app/modal.tsx:44-124, 154-158` | Quick Capture holds phone / name / email / description / full service address in component state only. Offline → Save is **disabled** with no local save and no send-when-online queue; there is no `beforeRemove` / dirty guard on the modal. | A tech captures a job in a basement / mechanical room / rural site with no signal. They cannot save; if they background the app, swipe the modal away, or tab out to check something, **every field is gone**. This is the core field-resilience checklist item and the app has no answer to it. |
| F5.2 | pilot-risk (blocker-severity if hit) | whole of `web/ophalo-app` — `rg beforeunload` = **zero hits**; `visibilitychange` unused for drafts | No `beforeunload` / pagehide guard anywhere. In-app route changes are guarded (`onDirtyChange` → `setDirty` → `showDiscardConfirm`) but only for a subset of forms (contact log, location, financial-resolution, no-charge, replace-visit) and only against SPA navigation. | Operator writes a long customer reply or an internal note, then hits Cmd-R / closes the tab / the OS reloads the tab / a crash → the draft is gone with no prompt. Extends F4.2 beyond the 409 case to every unload path. |
| F5.3 | **blocker** | `RequestDetail.tsx` reply/note composer — not registered with the `setDirty` guard, not persisted | The customer-reply draft and internal-note text are outside every guard described above and are not written to `sessionStorage`. Same defect as F4.1 seen from the offline/unload angle. | Covered by GAP-073 (AUDIT-V4-A) — listed here so the timezone/offline slice does not miss it. |
| F5.4 | pilot-risk | `web/ophalo-app/src/pages/request-detail/helpers.ts:33-58`; `TimelineEvent.tsx`, `ActualWorkHistoryCard.tsx`, `ActualWorkComposer.tsx` (submitted visits), `RequestCommunicationsWorkspace.tsx`, `Help.tsx` — every `toLocale*` / `Intl.DateTimeFormat` call | The business timezone is a required, IANA-validated field on `Account` (ADR-073), is editable in `CompanySection`, and is returned by the API — but **no render path reads it**. Every absolute timestamp is formatted with **no `timeZone` option**, i.e. in the viewer's device timezone. Consumed only by the settings editor and test fixtures. | Two staff in different zones see different clock times for the same event; a tech whose phone auto-updated its timezone while travelling sees every displayed time shift. There is no single authoritative "when did the customer say this" across the team. |
| F5.5 | pilot-risk (day-boundary) | `helpers.ts:60-84` — `formatDateOnly`, `isDateOnlyToday`, `isDateOnlyPast` | "Today" / "overdue" for follow-up promise dates is computed from device-local `now`, not the business timezone. ADR-451 explicitly defines the follow-up promise in the **business** timezone. | A tech one zone east of Central sees a follow-up flip to "overdue" up to a day early (west: a day late). Drives the attention indicator and queue-sort cues, so the whole team's sense of "what's late" drifts with each device. |
| F5.6 | hardening | `web/ophalo-app/index.html:7` (manifest only, no SW); no React Query persister / `gcTime` config | The workbench ships an installable PWA manifest but has no service worker, no offline cache, and no query persistence. A mid-session connection drop blanks the data on the next navigation. | Acceptable under the "desktop/tablet workbench, not a field app" posture — but that non-goal is nowhere written down, so it will be mistaken for a bug during the pilot. Document it (cross-ref Vector 10/11). |
| F5.7 | hardening | `mobile/ophalo-mobile/src/hooks/useNetworkState.ts:8-14` | Only `state.isConnected === false` is treated as offline; `null` / unknown is treated as online. | On a captive portal / "connected, no internet", Quick Capture Save stays enabled, the create fails, and the user gets the generic "check your connection" error instead of the offline banner + disabled state. Minor. |

**Disposition:**
- **F5.1 is a field-work-loss blocker** → **GAP-091**: AsyncStorage-backed Quick Capture draft
  (autosave on change, restore on reopen, explicit "discard draft" + `beforeRemove` confirm when
  dirty). Fold into the S17f real Quick Capture form build rather than bolting onto the current
  placeholder-adjacent modal. Pilot gate before field techs rely on capture.
- **F5.2 + F5.3** → fold into **GAP-073** (AUDIT-V4-A, already a pilot gate). Extend its scope
  note: not just `key={requestId}` + `sessionStorage` on the one composer, but a shared dirty
  registry driving a global `beforeunload` / pagehide guard.
- **F5.4 + F5.5** → **GAP-092**: business-timezone display. A `useBusinessTimeZone()` off the
  setup payload + a shared `formatInBusinessTz` / `businessToday` helper; route every formatter and
  every date-only "today / overdue" comparison through it. Pilot-risk (not a blocker if every
  pilot device is correctly set to Central), but ADR-451 already assumes business-TZ semantics.
- **F5.6** → deploy/scaling doc note (Vector 10 / 11): record "workbench is online-only, no
  offline cache" as a deliberate non-goal.
- **F5.7** → fold into **GAP-075** (frontend/defense-in-depth hardening set).

---

## Vector 6 — Public surface & rate limiting

**Checklist**

- [ ] Public intake forms (`/keep/s/[business-slug]`, magic-link exchange) have IP-based +
      token-bucket rate limiting against spam / abuse.
- [x] Public pages expose zero internal metadata — no internal notes, pricing margins, tech
      assignment history, or raw internal DB IDs. (F6.2 remains open — a dormant, low-impact
      externally observable internal-ID exposure, not a leak of guarded content.)
- [x] `GET /updates/guides/img/{name}` (and any other name/path-parameter file serving) rejects
      path traversal and only serves from the allowlisted asset store.

**Coverage swept (2026-09-11):** every `Map{Get,Post,Put,Delete,Patch}` under `src/OpHalo.Api`
(165 call sites across 20 files) classified as authorized / rate-limited / neither, group-aware
(`MapGroup("/auth").RequireRateLimiting("auth")` covers `/start`, `/signin`, `/exchange`,
`/continue`, `/mobile-handoff/redeem`; `/me`, `/logout` add `RequireAuthorization()` on top). The
only anonymous surface in the API is the documented public-intake / customer-page / customer-message
cluster in `KeepEndpoints.cs` plus `POST /accounts/invite/accept` (rate-limited under `"auth"`).
Every anonymous route is rate-limited **except** `GET /keep/r/{pageToken}` (F6.1).

**Findings**

| ID | Severity | Location | Finding | Impact |
| --- | --- | --- | --- | --- |
| F6.1 | pilot-risk | `src/OpHalo.Api/Keep/KeepEndpoints.cs:1150-1163` | `GET /keep/r/{pageToken}` (the anonymous customer-facing request page) has no `.RequireRateLimiting(...)`, unlike every other anonymous route in the API (`public-intake`, `customer-write`, `auth`). It is also not read-only: `GetKeepCustomerPageService.ExecuteAsync` loads a tracked entity and calls `RecordCustomerPageView` (debounced 5 min) + `CommitPageViewAsync` on every non-expired hit. | `PageToken` is high-entropy, so brute-force guessing isn't realistic, but an attacker who already has (or leaks) one token — or is scripting a load test — can hit this route at unlimited rate: unbounded DB reads (guard evaluation, tracked-entity load, event-timeline query) plus a real write path on every request, with none of the throttling every sibling anonymous route has. |
| F6.2 | hardening (open) | `src/OpHalo.Api/Keep/KeepEndpoints.cs:1227-1229, 1257-1259` | `HandlePublicIntake` / `HandlePublicIntakeBySlug` return `{ RequestId, ReferenceCode, PageToken }` to the anonymous caller on `201 Created`. `RequestId` is the raw internal `KeepRequest` primary key; the customer-facing frontend (`web/ophalo-web/src/app/keep/intake/[token]/IntakeForm.tsx:134-199`) only ever reads `pageToken` and `referenceCode` off the response — `requestId` is dead on the consuming side. | A dormant, low-impact externally observable internal-ID exposure — not an active leak of protected content (no internal notes/pricing/routing ride along), but an unused raw internal DB ID handed to an anonymous caller, contrary to the checklist's "no raw internal DB IDs" bar and the `KeepCustomerPageResult` cluster's own stated design ("no internal IDs... safe for public exposure"). Deferred out of the AUDIT-V6-A/F6.1 slice: `tests/OpHalo.IntegrationTests/Api/KeepPublicIntakeSlugApiTests.cs:198,214,278` and `KeepIntakeApiTests.cs:127,643` assert on / query by `body.RequestId`, so dropping it needs those repointed to `PageToken`/`ReferenceCode` — a tiny separate DTO-contract follow-up. |

**Verified clean, no finding:**
- `ToPublicIntakeInfoResponse` (`businessName`, `logoUrl`, `websiteUrl`, `phone` only) and
  `KeepCustomerPageResult` (`KeepCustomerPageMapper.BuildExpiredResult` / `BuildActiveResult`,
  `src/OpHalo.Keep.Application/Requests/KeepCustomerPageMapper.cs:17-58`) carry no internal IDs,
  account/user IDs, notes, or pricing; the customer-visible event timeline is filtered by
  `Visibility == All` at both the application mapper and the persistence query
  (`EfKeepCustomerWritePersistence.cs:55`) — defense in depth.
- `GET /updates/guides/img/{name}` sits behind `RequireAuthorization()` (not actually a public
  surface) and is doubly closed against traversal: the route regex
  (`UpdatesEndpoints.cs:17`, `^[A-Za-z0-9][A-Za-z0-9._-]{0,127}\.(png|jpe?g|webp)$`) admits no `/`
  or `..`, and the resolved key is `GuideImagePrefix + name`
  (`R2UpdatesContentSource.cs:95`, prefix `platform/updates/guides/img/`) — traversal is closed by
  the regex alone, and the prefix keeps every resolved key inside the allowlisted asset path.

---

## Vector 7 — File size & solution architecture (+ dependency scan)

**Checklist**

- [ ] No production source file is large enough to be a maintenance / review hazard; oversized files
      have a split recommendation.
- [ ] Layer boundaries hold (Foundation ⇏ Keep; Core ⇏ Application/Infrastructure; Application ⇏
      Infrastructure; single `OpHalo.Api` host) — confirmed by architecture tests.
- [ ] No god-objects / god-services concentrating unrelated responsibilities.
- [ ] `dotnet list package --vulnerable --include-transitive` and `pnpm audit` (all three web/mobile
      workspaces) are clean or have documented accepted risk.
- [ ] All dependencies pinned (no floating ranges in what ships); `pnpm-lock.yaml` / `packages.lock`
      committed and current.
- [ ] Dockerfile pins an explicit base-image digest/tag and runs the API as a non-root user.

**Initial size scan (2026-09-10, non-generated files)**

Backend (`src/**/*.cs`, excludes `Migrations/`):

| Lines | File |
| --- | --- |
| 1605 | `src/OpHalo.Keep.Core/Entities/KeepRequest.cs` |
| 1555 | `src/OpHalo.Api/Keep/KeepEndpoints.cs` |
| 1442 | `src/OpHalo.Keep.Application/Requests/GetKeepRequestListService.cs` |
| 900 | `src/OpHalo.Keep.Core/Entities/KeepRequestEvent.cs` |
| 681 | `src/OpHalo.Keep.Infrastructure/Persistence/KeepRequestListPersistence.cs` |
| 642 | `src/OpHalo.Keep.Application/PriceBook/ActualWorkDraftApiService.cs` |
| 615 | `src/OpHalo.Keep.Application/Requests/KeepRequestDetailMapper.cs` |
| 509 | `src/OpHalo.Api/Program.cs` |

Frontend (`web/**`, excludes tests / node_modules):

| Lines | File |
| --- | --- |
| 2135 | `web/ophalo-app/src/pages/request-detail/ActualWorkComposer.tsx` |
| 1560 | `web/ophalo-app/src/lib/apiClient.types.ts` |
| 1281 | `web/ophalo-app/src/mocks/fixtures.ts` |
| 1270 | `web/ophalo-app/src/pages/PriceBook.tsx` |
| 1159 | `web/ophalo-app/src/pages/request-detail/DetailPanels.tsx` |
| 1135 | `web/ophalo-app/src/lib/apiClient.ts` |
| 855 | `web/ophalo-app/src/pages/RequestDetail.tsx` |
| 746 | `web/ophalo-app/src/App.tsx` |

Mobile: `mobile/ophalo-mobile/app/requests/[id].tsx` — 1588.

Auto-generated EF `*.Designer.cs` / model snapshot files (~4.9k lines each) are excluded — not a
maintenance concern.

**Findings**

_None recorded yet._

---

## Vector 8 — Auth & session security

**Checklist**

- [x] Session tokens are opaque, high-entropy, server-side; storage uses a hash, not the raw token.
- [x] Sessions have a bounded lifetime + idle expiry; renewal path is safe. (bounds hold; renewal
      never rotates the token value — F8.2)
- [x] Sessions are revoked (or re-evaluated) on role change, member removal, and account
      deactivation — a removed member cannot keep acting on a live session. (member-level holds;
      account-level lifecycle gap — F8.3)
- [x] Magic-link and invite tokens are single-use, short-expiry, and rate-limited per
      email / IP; consumed links cannot be replayed. (single-use/atomicity holds; per-email rate
      limiting does not exist — F8.4/F8.5)
- [x] `/auth/exchange` and sign-in request endpoints have abuse protection (see Vector 6) and do not
      leak whether an email exists. (IP rate limit exists but is coarse; timing + metadata leaks —
      F8.9/F8.10/F8.11)
- [x] Account-creation-on-exchange runs in one transaction (cross-refs Vector 2). (holds)

**Method:** plumbing pass in the driving session + 3 parallel `Explore` agents (8A session
lifecycle — role-change/removal/deactivation revocation, expiry bounds, renewal path; 8B magic-link
+ invite token entropy/storage/single-use/rate-limiting; 8C `/auth/exchange` abuse protection,
email enumeration, account-creation atomicity). Reviewed: `SessionAuthenticationHandler`,
`ICurrentUser`, `MemberManagementService`, `SessionStore`, `AuthConstants`, `AccountAccessPolicy`,
`MagicLinkCodeGenerator`, `InviteTokenGenerator`, `StartAuthService`, `SignInAuthService`,
`ExchangeAuthService`, `SendInviteService`, `AcceptInviteService`, `RedeemMobileHandoffService`,
`CompleteAuthContinuationService`, `EfAuthCodePersistence`, `EfInvitePersistence`,
`EfPostAuthContinuationPersistence`, `EfMobileHandoffCodePersistence`, `ClientIpResolver`, and the
`"auth"` rate-limit policy + `AuthEndpoints.cs` / `AccountEndpoints.cs` route wiring.

**What holds (no action needed)**

- Session tokens: 32-byte CSPRNG, SHA-256-hashed at rest (already confirmed Vector 1; reconfirmed).
- Role is never carried in session claims and is re-read from persistence on every request
  (`SessionAuthenticationHandler.cs:110-115`, `ICurrentUser` has no `Role` property) — a role
  change takes effect on the very next request with no stale-privilege window. Documented intent:
  `MemberManagementService.cs:23`.
- Member suspend/remove explicitly revoke live sessions (`MemberManagementService.cs:187-188,
  281-288`); revocation is best-effort/logged-not-thrown, backstopped by the per-request
  membership-status gate.
- Session expiry composes correctly: `SessionAbsoluteExpiryDays = 60` and
  `SessionInactivityWindowDays = 30` (`AuthConstants.cs:10,16`) are independent `NoResult` gates in
  `SessionAuthenticationHandler.cs:79-85` — sliding renewal can never outlive the absolute cap.
- Magic-link/invite/continuation/mobile-handoff tokens: 256-bit CSPRNG, SHA-256-hashed at rest,
  never logged (`MagicLinkCodeGenerator.cs:17-24`, `InviteTokenGenerator.cs:16-23`).
- Single-use consumption is atomic (conditioned `ExecuteUpdateAsync … WHERE ConsumedAtUtc == null`)
  across all four token classes — no TOCTOU replay window:
  `EfAuthCodePersistence.ConsumeCodeAsync:85-97`, `EfInvitePersistence.CommitAcceptInviteAsync:146-160`,
  `EfPostAuthContinuationPersistence.ConsumeAsync:45-54`, `EfMobileHandoffCodePersistence.ConsumeAsync:21-30`.
  Mobile handoff also unifies not-found/expired/consumed into one generic error
  (`MobileHandoffCodeErrors.cs:7-8`) — the pattern the rest of the stack should match (F8.7).
- `/auth/start` and `/auth/signin` return a neutral `Result.Success()` body for unknown/ineligible
  emails (`StartAuthService.cs:60-61`, `SignInAuthService.cs:41-43`); the rate limiter is live in
  production on every non-Testing environment (`Program.cs:426`), and `ClientIpResolver` only trusts
  forwarded-IP headers from a configured trusted-proxy CIDR, closing the obvious spoof.
- Account creation on new-account exchange is one transaction: code consumption +
  User/Account/AccountUser/entitlements insert inside a single `BeginTransactionAsync`/`CommitAsync`
  block with correct two-phase handling of the circular Account↔AccountUser FK and a mapped
  unique-violation → `AccountErrors.EmailAlreadyInUse`
  (`EfAuthCodePersistence.CommitNewAccountExchangeAsync:181-229`). Session issuance runs outside
  that transaction by design; a failure there leaves a valid, sign-in-able account, not an orphan.
- `CompleteAuthContinuationService` re-validates membership and atomically consumes its token —
  no atomicity gap.

**Findings**

| ID | Sev | Location | Issue | Scenario |
| --- | --- | --- | --- | --- |
| F8.3 | pilot-risk | `SessionAuthenticationHandler.cs:89`; `AccountAccessPolicy.cs:12-16`; `Program.cs:153` | Member-level Active-status is gated inside `SessionAuthenticationHandler` (fail-closed); account-level lifecycle (`Suspended`/`Closed`) is only checked by `IAccountAccessPolicy`, called opt-in from ~50+ individual Application services — it is not wired into the auth handler or any global middleware. | A suspended/closed account's members keep authenticated access on any endpoint that forgets (or hasn't yet been made) to call `AccountAccessPolicy.Evaluate` — no fail-closed backstop symmetric with the membership gate. |
| F8.4 | pilot-risk | `Program.cs:324-336`; `AuthEndpoints.cs:17-22`; `AccountEndpoints.cs:19` | The `"auth"` rate-limit policy is a single fixed-window IP partition (10 req/min) shared across `/auth/start`, `/auth/signin`, `/auth/exchange`, `/auth/continue`, `/auth/mobile-handoff/redeem`, and `/accounts/invite/accept` — no per-email throttle anywhere. | A small pool of attacker IPs mail-bombs a victim's inbox with unlimited magic-link emails via `/auth/start`/`/auth/signin`; separately, a NAT'd legitimate network can exhaust the shared budget on one route and get 429'd on another. |
| F8.5 | pilot-risk | `AccountEndpoints.cs:18,24`; `SendInviteService.cs:100-110` | `POST /accounts/me/invite` and `/accounts/me/members/{id}/resend-invite` carry no `.RequireRateLimiting(...)` at all; resend of an already-`Invited` row explicitly skips the seat-limit check with no time cooldown. | Any Owner/Admin session (or a compromised one) loops `ResendInvite` against an arbitrary email with zero throttling — mail-bombs an inbox from the company's own sending domain/reputation. |
| F8.9 | pilot-risk | `SignInAuthService.cs:42-43,68,80-85`; `StartAuthService.cs:60-61,104-124,147-167,205-225` | The neutral (unknown-email) branch returns after one indexed query (~ms); every other branch inline-awaits a write transaction plus an outbound email send before returning the same 200. | Response latency differs by ~100–500 ms between "email registered" and "email unknown," a practical timing oracle that defeats the same-status/same-body enumeration defense. |
| F8.10 | pilot-risk | `AuthEndpoints.cs:83-91`; `ExchangeAuthService.cs:72,74-81` | On `/auth/exchange` failure, a genuinely-unknown code omits `entryContext`; a stale/used code (`Expired`/`AlreadyConsumed`/`CannotConsumeInvalidated`) includes `entryContext: "new_account" \| "existing_member" \| "multiple_members"` in the ProblemDetails body. | Anyone who ever possessed a since-used/expired link for an address (forwarded mail, shared inbox, browser history) can confirm the code was ever valid and learn the target's account-type classification without completing auth. |
| F8.11 | pilot-risk | `Program.cs:324-336`; `EfAuthCodePersistence.CommitSignInCodeAsync:61-73` | `/auth/signin` has no per-email limiter or failed-attempt lockout, and every call invalidates all prior unconsumed codes for that `TargetAccountUserId`. | Knowing only a victim's email, an attacker repeatedly POSTs `/auth/signin` (under the 10/min IP cap, or from rotating IPs) to continuously invalidate the victim's outstanding magic link before it can be used — an indefinite sign-in-denial DoS needing no account access. |
| F8.1 | hardening | `AuthConstants.cs:10,16` | `SessionAbsoluteExpiryDays = 60` / `SessionInactivityWindowDays = 30` — generous for a bearer token with no MFA re-check. | Correctly bounded, not a defect; a stolen/leaked token has up to 60 days of blast radius. Worth tightening post-pilot. |
| F8.2 | hardening | `SessionStore.TryUpdateLastActivity` (`SessionStore.cs:53-62`) | Sliding renewal only updates `LastActivityAtUtc`/`LastSeenAtUtc`; the token value/hash is never rotated for the life of the session. | No fixation bug, but no mechanism ever shortens a leaked token's remaining validity short of explicit revocation. |
| F8.6 | hardening | `StartAuthService.cs:96,140,195`; `SignInAuthService.cs:55,61` | Magic-link codes are valid for 24 hours — long for an email-delivered bearer credential. | Increases exposure to inbox compromise/forwarding/shared-inbox/shoulder-surfing versus a typical 10–15 min TTL. Not exploitable on its own (high entropy, single-use, hashed). |
| F8.7 | hardening | `ExchangeAuthService.cs:74-81`; `EfInvitePersistence.CommitAcceptInviteAsync:112-113` | Magic-link exchange and invite accept return distinct `Expired`/`AlreadyConsumed`/`CannotConsumeInvalidated` errors instead of one generic invalid-code response, unlike the mobile-handoff pattern that already unifies these. | Low-severity (attacker already needs the raw code/token); inconsistent with the better pattern proven elsewhere in this codebase. |
| F8.8 | hardening | `EfAuthCodePersistence.cs:65-72` vs. `:159-167` | `CommitSignInCodeAsync`'s invalidation branch filters only on `TargetAccountUserId`; the sibling `CommitStartCodeAsync` branch also matches `EntryContext`. Safe today only because `TargetAccountUserId` is non-null solely for `ExistingMember` codes. | Latent-consistency risk: a future code type carrying a non-null `TargetAccountUserId` under a different `EntryContext` would be silently cross-invalidated. |
| F8.12 | hardening (accepted risk, ADR-365) | `StartAuthService.cs:24-26,181-187` | Pilot-capacity 409 (`Account.PilotFull`) only fires for genuinely-unregistered emails; any known email returns 200 regardless of status. Once the pilot cap fills, this lets an attacker binary-search email registration. | Documented deliberate UX trade-off (waitlist prompt) — flagging to reconfirm it still holds once pilot caps are expected to fill in production. |
| F8.13 | hardening | `StartAuthService.cs:121-124,164-167,222-225`; `SignInAuthService.cs:95-98` | All non-cancellation exceptions from `IEmailSender.SendAsync` are caught, logged as a warning, and still return 200 (correct for enumeration protection) — but there is no alerting signal. | A total Resend outage silently returns 200 to every start/sign-in caller with zero links delivered; only a per-request log line marks it. |

**Disposition:** F8.3 → **GAP-093** (session-layer account-lifecycle gate, pilot-risk, standalone —
hoist the `AccountAccessPolicy` suspend/close check into `SessionAuthenticationHandler` or an
equivalent global filter so it fails closed the same way the membership gate does). F8.4, F8.5,
F8.11 → **GAP-094** (auth-code and invite issuance rate limiting: per-email/per-account partition
alongside the existing per-IP `"auth"` policy on `/auth/start`, `/auth/signin`,
`/accounts/me/invite`, and `/accounts/me/members/{id}/resend-invite`; note F8.11's invalidation-DoS
and F8.4's mail-bomb scenario share this one root cause). F8.9, F8.10 → **GAP-095** (auth-response
enumeration hardening: move outbound email dispatch off the request path or pad neutral-branch
latency to remove the timing oracle; drop `entryContext` from `/auth/exchange` failure responses for
stale/used codes). F8.1, F8.2, F8.6, F8.7, F8.8, F8.12, F8.13 → **GAP-096** (Vector 8 hardening
batch — pre-GA session/token lifetime tightening, error-message unification, defensive
`EntryContext` guard, reconfirm the ADR-365 pilot-full trade-off, email-delivery-failure alerting;
no user-visible urgency, batch opportunistically). **Sequencing decision, 2026-09-11:** GAP-093,
GAP-094, and GAP-095 are supervised-pilot gates, ordered after GAP-073/091 and before GAP-040:
account-level fail-closed revocation first, then issuance abuse prevention, then enumeration
hardening. GAP-096 remains outside the pilot-gate queue.

---

## Vector 9 — HTTP & transport hardening

**Checklist**

- [ ] Response security headers present: HSTS, `X-Content-Type-Options: nosniff`, frame/`frame-ancestors`
      protection, `Referrer-Policy`. (CSP is tracked separately as a deferred DEF ticket.)
- [ ] CORS is an explicit origin allowlist — no `*` with credentials, no reflected origin.
- [ ] Session cookies set `Secure` + `HttpOnly` + `SameSite`; no sensitive data in
      non-`HttpOnly` cookies or `localStorage`.
- [ ] Production error responses return a stable error contract with no stack traces, framework
      detail, SQL, or internal IDs (`ErrorHttpMapper.cs`).
- [ ] `4xx` vs `5xx` discipline — expected domain rejections are not logged/counted as server errors.
- [ ] No client-bundle env leakage — only intentionally public `VITE_` / `NEXT_PUBLIC_` vars reach
      the browser; no secrets, internal URLs, or source maps exposing internals in production.

**Findings**

_None recorded yet._

---

## Vector 10 — Deploy & release safety

**Checklist**

- [ ] Migration ordering vs code deploy is defined and safe (expand/contract for destructive
      changes; new code tolerates the pre-migration schema or migration runs first).
- [ ] Documented rollback plan for a bad API deploy and for a bad migration.
- [ ] Post-deploy smoke check (health, auth, one read, one write) is defined.
- [ ] Feature-flag hygiene: paired-config fail-fast (the `Feedback:Enabled` +
      `FounderChannel:WebhookUrl` pattern) applied consistently to every flag that needs external
      config; committed production defaults are safe.
- [ ] `ProductionConfigurationValidator` covers every required production setting; startup fails
      loudly on a missing/invalid one.
- [ ] Single API host assumption is documented where it matters (cross-refs Vector 11).

**Findings**

_None recorded yet._

---

## Vector 11 — Multi-instance / horizontal-scaling readiness

**Checklist**

- [ ] Inventory every in-process / per-instance store and state holder: `UpdatesFeedCache`,
      `FounderAlertThrottle` buckets, feedback per-`account_user` rate-limit counters, any
      `IMemoryCache` / static state.
- [ ] For each: state the behavior if 2+ API instances run (double alerts, halved rate limits,
      cache divergence) and whether that is acceptable for the pilot.
- [ ] Background workers (`FeedbackDeliveryWorker`, `FeedbackMaintenanceBackgroundService`) are
      safe under >1 instance — row locking (`SKIP LOCKED`) or a documented single-worker constraint.
- [ ] Session store, rate limiting that must be authoritative, and idempotency keys are
      DB-backed (shared), not in-process.
- [ ] Decision recorded: pilot runs exactly one API instance (documented) **or** the above are
      made instance-safe.

**Findings**

_None recorded yet._
