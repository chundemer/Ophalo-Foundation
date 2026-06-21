# Session Log — OpHalo Foundation

**Last updated:** 2026-06-21 (G4c corrective committed)
**Branch:** `main` (no remote yet)
**Current baseline:** 1013 tests (568 unit · 14 architecture · 431 integration)
**Next free ADR:** ADR-326
**Pre-work not complete** — G4d requires a fresh pre-implementation gate before any code is written.

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

Historical Phase 8-B1 through B5 Session 5 completion detail is intentionally omitted here. Use
build logs 025–046 and ADR-084–294 when needed; do not reload those histories for G4 unless a
specific signature or locked behavior requires a targeted read.

---

## G4 — Shared Request-Row and Action Authorization — IN PROGRESS

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
- bounded server-generated `DescriptionPreview`
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

Customer name and a bounded preview are a deliberate usability/privacy balance. Free-form preview
text may contain incidental customer detail; the G4d gate must propose an exact maximum and safe
server-side truncation behavior before coding that DTO.

Full detail requires an explicit audited self-assign or watch. Reads never silently assign, watch,
or otherwise change ownership. Available-summary access is not general mutation authority.

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

Scope:

- add `KeepRequestVisibilityScope` with exactly `AccountWide`, `MyWork`, and
  `ParticipationEntry`;
- add shared `KeepRequestRowQueryFactory` with explicit exhaustive scope handling;
- change detail persistence lookup to require a selected scope and current AccountUser ID;
- map Owner/Admin/Viewer detail reads to `AccountWide`, Operator detail reads to `MyWork`;
- preserve account, membership, feature, OffSeason-read, and field-redaction gates;
- authorize the request before loading events/participants/business detail;
- add direct-ID tests for Owner/Admin, Viewer, Operator Responsible, Operator Watching, same-account
  invisible, stale/ineligible, detached, unknown ID, and cross-account cases;
- do not modify lists, counts, mutation loads, Available DTO/route, or action metadata in G4a.

Expected files (verify names/signatures before writing):

- new `Keep.Application/Requests/KeepRequestVisibilityScope.cs`
- new `Keep.Infrastructure/Persistence/KeepRequestRowQueryFactory.cs`
- `Keep.Application/Requests/IKeepRequestDetailPersistence.cs`
- `Keep.Infrastructure/Persistence/EfKeepRequestDetailPersistence.cs`
- `Keep.Application/Requests/GetKeepRequestDetailService.cs`
- new `tests/OpHalo.IntegrationTests/Api/KeepRequestDetailRowAuthApiTests.cs`

Gate: targeted tests, architecture tests, build, and full suite; report exact counts. No migration.

#### G4b — Consequential Operator mutation-load migration — COMPLETE (1004 tests)

Committed. See completed gap references above for detail.

#### G4c — Participation/admin mutation-load migration and old-loader removal — COMPLETE (commit e82ebae, 1012 tests)

Committed. See completed gap references above for detail.

#### G4d — My Work lists/counts and dedicated Available surface

**Pre-work not complete. Claude must present a targeted gate after G4c commits.**

Scope:

- Default and Needs Attention use `MyWork` for Operator and `AccountWide` for Owner/Admin/Viewer;
- default/Needs Attention counts use the same scopes as their rows;
- available count means active eligible effectively-unassigned work for Operator;
- add dedicated Available DTO/route with the locked fields and exclusions above;
- keep `view=unassigned` Owner/Admin full-summary behavior;
- preserve deterministic ordering and define pagination/response wrapper in the gate before code;
- prove no related sensitive projection is loaded for Available output;
- test list/count/detail agreement, field absence, active/terminal boundaries, stale Responsible
  treatment, and no GET side effects.

Open implementation details for the G4d gate only:

- exact `DescriptionPreview` maximum/truncation semantics;
- Available response wrapper, limit/cursor contract, and deterministic cursor sort;
- whether Owner/Admin may call the Available-summary route in addition to their full Unassigned view.

#### G4e — Shared action-policy migration and completion gate

**Pre-work not complete. Split into bounded batches if the gate exceeds 8–10 files.**

- add/finalize standalone `KeepRequestActionPolicy`;
- migrate every `AvailableActionsMetadata` constructor and relevant mutation endpoint to the same
  primitive rules, including detail, all migrated write services, participation services,
  feedback-review service, and `CreateBusinessRequestService` response construction;
- remove superseded action helpers from `KeepRequestDetailMapper` after zero callers remain;
- prove OffSeason, role, row, participation, terminal state, and action metadata agree;
- enumerate every list/count/detail/write/action path in the G4 exit record;
- run build, unit, architecture, integration, and full test suite and record exact counts.

---

## G5 — Entity-Wide KeepRequest Optimistic Concurrency — PLANNED

**Finding:** GAP-004

Goal: prevent stale business intent from overwriting newer customer/business state.

Locked direction:

- application-managed opaque concurrency version; do not expose PostgreSQL `xmin`;
- every relevant `KeepRequest` mutation changes the token;
- consequential commands require expected version;
- `DbUpdateConcurrencyException` maps to stable `409 KeepRequest.RequestChanged`;
- never automatically retry user intent; client refetches;
- customer writes participate in the same token;
- append-only exclusions must be explicitly justified;
- test two-copy races, customer-versus-business races, no secondary event on conflict, and new
  version after valid sequential writes.

No automatic merge, distributed lock, event sourcing, or frontend draft recovery in G5.

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
