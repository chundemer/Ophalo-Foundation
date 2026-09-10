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
| 4 | Client reflow, UX state & layout resilience | Not started | — | — | — |
| 5 | Edge cases & offline / network resilience | Not started | — | — | — |
| 6 | Public surface & rate limiting | Not started | — | — | — |
| 7 | File size & solution architecture (+ dependency scan) | Not started | — | — | — |
| 8 | Auth & session security | Not started | — | — | — |
| 9 | HTTP & transport hardening | Not started | — | — | — |
| 10 | Deploy & release safety | Not started | — | — | — |
| 11 | Multi-instance / horizontal-scaling readiness | Not started | — | — | — |

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

**Findings**

_None recorded yet._

---

## Vector 5 — Edge cases & offline / network resilience

**Checklist**

- [ ] Field workflows (draft visit entries) survive high-latency / dropped connections — drafts
      persist locally so work is not lost.
- [ ] Dirty-state forms (long customer reply, line-item entry) warn on navigate-away
      (`beforeunload` + in-app route guard).
- [ ] Timestamps stored UTC at the DB layer, converted to the business timezone
      (`America/Chicago`) at the view layer.

**Findings**

_None recorded yet._

---

## Vector 6 — Public surface & rate limiting

**Checklist**

- [ ] Public intake forms (`/keep/s/[business-slug]`, magic-link exchange) have IP-based +
      token-bucket rate limiting against spam / abuse.
- [ ] Public pages expose zero internal metadata — no internal notes, pricing margins, tech
      assignment history, or raw internal DB IDs.
- [ ] `GET /updates/guides/img/{name}` (and any other name/path-parameter file serving) rejects
      path traversal and only serves from the allowlisted asset store.

**Findings**

_None recorded yet._

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

- [ ] Session tokens are opaque, high-entropy, server-side; storage uses a hash, not the raw token.
- [ ] Sessions have a bounded lifetime + idle expiry; renewal path is safe.
- [ ] Sessions are revoked (or re-evaluated) on role change, member removal, and account
      deactivation — a removed member cannot keep acting on a live session.
- [ ] Magic-link and invite tokens are single-use, short-expiry, and rate-limited per
      email / IP; consumed links cannot be replayed.
- [ ] `/auth/exchange` and sign-in request endpoints have abuse protection (see Vector 6) and do not
      leak whether an email exists.
- [ ] Account-creation-on-exchange runs in one transaction (cross-refs Vector 2).

**Findings**

_None recorded yet._

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
