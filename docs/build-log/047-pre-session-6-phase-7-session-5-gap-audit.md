# Build Log 047 — Pre-Session 6 Phase 7 + Session 5 Gap Audit

**Date:** 2026-06-19  
**Status:** Audit complete; gap resolution required before Session 6  
**Audit scope:** Phase 7 intake-to-list foundation, completed Session 5 feedback review, and shared
Keep authorization/concurrency behavior discovered while planning Session 6  
**Implementation status:** No production code changed by this audit

---

## Purpose

Before implementing Session 6, validate that the earlier intake foundation and the newly completed
feedback-review workflow are safe foundations for closeout, stale-status checks, local database
initialization, notifications, and pilot UI.

This audit compares:

- locked ADR language;
- current domain/application/infrastructure/API behavior;
- migrations and database constraints;
- action metadata exposed to clients;
- test coverage and completion logs;
- later product-positioning and V1 scope locks that now place stronger requirements on Phase 7.

The goal is not to reopen deferred product breadth. The goal is to correct security, data-integrity,
workflow, and contract gaps before Session 6 compounds them.

---

## Verification Baseline

- Session 5D records **847/847 tests passing**.
- The user confirmed the current `dotnet build` and `dotnet test` pass.
- An independent full-suite rerun during this audit was attempted. The normal sandbox run failed
  because MSBuild could not create its IPC socket; the escalated run was not approved, and the
  single-node retry was interrupted. No failing product test was observed.
- Static inspection found gaps that the current green suite does not test.
- Working tree was clean before this audit document was added.

---

## Executive Outcome

Phase 7 successfully established the first vertical slice and most of its security posture:

- account resolution comes only from a high-entropy hashed intake token;
- public gate failures are safely collapsed;
- customer/request/event persistence is atomic in the ordinary path;
- page/reference token uniqueness is database-backed;
- public intake is access/feature/OffSeason gated;
- real session authentication now protects operator routes;
- PostgreSQL migrations and provider behavior are tested.

Session 5 also correctly implemented its core workflow:

- one-time Closed-only feedback;
- Owner/Admin-only review completion;
- durable review metadata and internal event;
- review aging metadata;
- OffSeason customer/write suppression;
- removal from review queues after completion.

However, the combined system is **not yet ready for Session 6 implementation**. The audit found:

- pilot-blocking intake/setup omissions;
- an intra-account row-scope authorization mismatch for Operators;
- missing optimistic concurrency across Keep writes;
- missing Keep relational foreign keys before database initialization;
- customer identity and concurrent-intake defects;
- public validation and edge-protection gaps;
- two concrete feedback-review workflow bypass/contradiction defects;
- two implemented-ADR documentation/contract mismatches.

Use a bounded Pre-Session 6 Gap Resolution Phase rather than one oversized Claude prompt.

## Gap Decision Log

### Pilot intake-link and abuse posture — locked 2026-06-19; revised 2026-06-20

- Keep automatically provisions one durable public intake link after the business profile/name is
  established during onboarding. Provisioning is presented as part of signup but is performed
  through the Keep setup boundary rather than coupling Foundation account registration to Keep.
- The share URL may include a business-name-derived slug for friendly display, but the opaque token
  remains the authority. A later business-name change must not invalidate an already shared URL;
  the current business name is rendered from account data.
- The link is expected to remain stable for websites, printed materials, QR codes, and old customer
  messages. It is never rotated automatically or as ordinary maintenance.
- V1 does not add an ordinary Owner/Admin pause/resume control or `PausedAtUtc`. The distributed link
  is a customer-acquisition asset and normal operation should continue capturing requests. Existing
  account/access/OffSeason gates remain system-level exceptions.
- Permanent replacement is exceptional recovery, not ordinary maintenance or the primary response
  to targeted public-form abuse. It atomically revokes the old link and creates its successor in one
  database transaction, returns the new raw URL once, invalidates the old URL, and must warn that
  previously shared/printed links need replacement.
- Before pilot, harden trusted-client-IP resolution and prove the existing per-IP `429` behavior.
  Do not persist raw client IP on the normal request record.
- Before pilot, Owner/Admin can classify requests as distinct `Spam` or `Test` reasons. The request
  remains auditable but leaves operational queues, stops customer-page activity, and is excluded
  from response-time, intake, stale-work, and impact metrics. Operators cannot classify in V1.
- Before pilot, use bounded validation, trusted-IP/rate-limit proof, Spam/Test classification with
  metrics exclusion, token-safe logs, and a founder/internal emergency response runbook.
- Expanded controls (honeypot/timing checks, duplicate detection, broader thresholds, adaptive
  Turnstile, expiring keyed-hash source correlation/blocks, anomaly alerts, and optional phone
  verification) are post-pilot/V1.1 candidates. Pull them before broad launch only if pilot evidence
  shows repeated abuse that the pre-pilot controls cannot contain. Do not expose raw customer IPs to
  businesses. See updated `DEF-061`.

### Remaining gap decisions — locked 2026-06-19

- Customer pages expire 30 days after a request enters `Closed` or `Cancelled`, restoring the
  reference product's previously decided duration while preserving ADR-120's terminal-only rule.
  Active and `Resolved` requests do not expire. `Spam`/`Test` classification disables customer-page
  activity immediately.
- Remove the undeployed `/continuity/public-intake/...` alias and the accepted-but-ignored
  `emailNotificationsEnabled` field before frontend implementation. Add notification consent only
  with real behavior and approved product/compliance semantics.
- Enforce all Keep relationships with database foreign keys. Use account-aware composite
  relationships wherever a simple ID relationship could permit a cross-account reference, use
  restricted deletion to preserve history, and do not add EF navigation properties merely for
  convenience. PostgreSQL integration tests must prove cross-account rejection.
- Implement ADR-263 as written: positive feedback comments are visible to authenticated users who
  pass request-row access; full negative comments remain Owner/Admin-only and lower roles receive
  only safe metadata. The current mapper behavior is a defect, not a policy change.

---

## Finding Summary

| ID | Severity | Area | Finding | Required disposition |
|---|---|---|---|---|
| GAP-001 | Blocker | Keep setup | No production flow provisions, returns status for, or exceptionally replaces an account's durable public intake link | Implement before pilot/local end-to-end |
| GAP-002 | Blocker | Request intake | No authenticated business/manual request creation exists although pilot docs assume manual entry | Implement bounded business-created intake |
| GAP-003 | High / security | Row authorization | Operator default/list/detail/write scope is broader than locked responsible/watching/available policy | Correct before Session 6 |
| GAP-004 | High / integrity | Concurrency | Keep request writes have no optimistic concurrency; stale close/review/update can overwrite newer state | Add entity-wide OCC before Session 6 |
| GAP-005 | High / integrity | Schema | Keep tables carry account/request/customer IDs without relational foreign keys | Locked: add account-safe FKs with restricted deletion |
| GAP-006 | High / identity | Customer matching | Customer identity uses trimmed raw phone rather than normalized phone | Add canonical phone identity before real data |
| GAP-007 | High / reliability | Intake race | Concurrent first submissions for the same normalized phone can violate customer uniqueness and return 500 | Add bounded customer-race recovery |
| GAP-008 | High / public API | Validation | Oversized name/phone/email/description reaches EF and can return 500 instead of explicit 400 | Add bounded validation + tests |
| GAP-009 | Medium / data loss | Repeat intake | Repeat intake without email clears a previously known customer email | Preserve known email unless explicitly replaced |
| GAP-010 | Medium / semantics | Activity fields | Customer-created intake sets business activity but no customer activity | Correct origin/activity semantics before stale policy |
| GAP-011 | Medium / lifecycle | Customer-page expiry | Expiry is enforced when populated, but no normal workflow ever sets `ExpiresAtUtc` | Locked: 30 days from close/cancel; immediate for Spam/Test |
| GAP-012 | High / edge security | Rate limiting | Trusted-proxy headers are accepted without code-enforced proxy trust and 429 behavior is untested | Harden before staging |
| GAP-013 | Medium / secret handling | Bearer URLs | Public intake/page bearer tokens may appear in ordinary request-path logs | Add path/token redaction posture before staging |
| GAP-014 | Medium / contract | Legacy surface | Undeployed app still exposes Continuity alias and ignored email opt-in as legacy contracts | Locked: remove both before frontend |
| GAP-015 | Medium / link integrity | Intake link model | Model comment claims one active link per account, but schema enforces only slug/hash uniqueness | Locked: one durable active link per account |
| GAP-016 | Medium / operations | Runtime database | Connection validation is lazy and no persistent local full-stack database runbook exists | Complete after gap phase, before notifications |
| GAP-017 | High / workflow | Feedback review | Generic acknowledgement can clear unresolved feedback and make Mark reviewed unavailable | Block generic acknowledgement for `UnresolvedFeedback` |
| GAP-018 | Medium / workflow | Feedback follow-up | Structured external contact is blocked on Closed unresolved-feedback requests | Add narrow Owner/Admin exception already product-locked |
| GAP-019 | Medium / contract | Feedback navigation | ADR-282 is marked Implemented but no feedback next/previous navigation contract exists | Move implementation to Session 6 navigation |
| GAP-020 | Medium / visibility | Positive feedback | ADR-263 says positive comments are broadly visible, but mapper hides all comments from Operator/Viewer | Locked: implement ADR-263 as written |
| GAP-021 | Medium / docs | ADR ledger | Several delivered Phase 7 ADRs remain `Locked` rather than `Implemented` | Reconcile after fixes |

---

## Detailed Phase 7 Findings

### GAP-001 — Public intake link setup is missing

**Evidence**

- `KeepPublicIntakeLink`, token generation/hashing, lookup, revoke domain behavior, and public route
  exist.
- Tests manually seed the link.
- New-account registration explicitly deferred link creation to a later Keep setup flow (`DEF-007`).
- No authenticated endpoint/service currently lets an Owner/Admin ensure the initial link, retrieve
  setup status, or exceptionally replace it.
- Because only the hash is stored, losing the raw link requires exceptional replacement; there is
  no replacement flow.

**Impact**

A real newly created business cannot use the Phase 7 public intake vertical without direct database
seeding. This blocks onboarding and the core product promise.

**Required fix**

Add an Owner/Admin Keep intake-link setup contract:

- read setup/status without returning stored raw secrets;
- create initial link and return the raw token/share URL once;
- exceptionally replace atomically in one database transaction (revoke old, create successor,
  return raw token/share URL once, and roll back to the old link if successor creation fails);
- no ordinary pause/resume/disable control and no `PausedAtUtc` in V1;
- stable, normalized slug handling if slug remains in the model;
- audit actor/time;
- tests for role, account scope, duplicate/concurrent setup, transactional replacement/rollback,
  old-token rejection, and OffSeason posture.

Do not make Foundation registration depend directly on Keep. A post-registration Keep setup
service/API preserves bounded-context direction.

### GAP-002 — Authenticated manual/business-created request intake is missing

**Evidence**

- Only anonymous public intake creates a request.
- `KeepRequestOrigin.Business` exists but no production caller uses it.
- Founder/pilot docs say manual entry is allowed and feedback/rework decisions refer to manually
  creating a new request.

**Impact**

Phone calls, voicemails, referrals, walk-ins, and staff-entered requests cannot be captured honestly.
Using the public form would incorrectly classify the request as customer-created and start
first-response semantics that do not match reality.

**Required fix**

Add a bounded authenticated manual-create workflow:

- Owner/Admin/Operator under the corrected row/operate policy;
- server-derived account, `Origin=Business`;
- normalized customer matching shared with public intake;
- no false first-response timer at creation;
- request-created event with correct actor metadata;
- return detail/customer page token;
- no scheduling, estimating, CRM, or job-management expansion.

### GAP-003 — Operator row visibility is inconsistently enforced

**Evidence**

- Locked policy: Operators see responsible/watching work plus intentional unassigned/Available
  access.
- Current default and `needs_attention` persistence queries are account-wide for non-admin roles.
- Detail and write services generally enforce account/permission but do not consistently enforce
  request-level visibility.

**Impact**

An Operator can see or mutate another Operator's assigned work, contrary to the routing/visibility
contract. Session 6 role-aware queues would inherit the flaw.

**Required fix**

Create one shared request-row access policy used by lists, counts, detail, and writes:

- Owner/Admin: account-wide;
- Operator: active Responsible, active Watching, or eligible unassigned request;
- Viewer: preserve explicit read-only policy;
- cross-account/invisible rows return Not Found;
- destructive actions may be narrower (for example, cancellation requires Responsible).

Add direct-ID HTTP tests, not only list tests.

### GAP-004 — Keep request writes lack optimistic concurrency

**Evidence**

- `GetRequestForUpdateAsync` loads a normal tracked entity.
- no row-version/`xmin` concurrency token is configured;
- Session 5 explicitly deferred true-race protection under `DEF-074`;
- Session 6 closeout requires protection against customer activity arriving after an Admin viewed
  the row.

**Impact**

Concurrent customer/business writes can silently overwrite attention, activity, review, or terminal
state. Sequential domain guards do not close the true race window.

**Required fix**

Implement entity-wide KeepRequest optimistic concurrency:

- database-managed token (Npgsql `xmin` or an explicit equivalent);
- opaque API `version` on list/detail where consequential actions are offered;
- required expected version for close/cancel and other final/destructive actions;
- EF concurrency exceptions mapped to stable `409 RequestChanged`;
- no automatic retry of user intent;
- preserve drafts and refetch at client layer later;
- race-focused integration tests.

### GAP-005 — Keep relational foreign keys are absent

**Evidence**

The initial Keep migration creates UUID columns and indexes but no foreign keys among:

- public intake link → account;
- customer → account;
- request → account/customer;
- event → account/request;
- later participant/policy records and their parents.

**Impact**

Application bugs can persist orphaned or cross-account-inconsistent graphs. This is cheapest to fix
before a real database contains data.

**Required decision/fix**

Perform a complete Keep-schema relationship audit. Prefer account-safe relationships, potentially
including composite account/id keys where a simple FK would still permit a request to reference a
customer from another account. Use `Restrict`/appropriate delete behavior and integration tests for
cross-account rejection. Do not add EF navigation properties merely for convenience if scalar-key
relationships suffice.

### GAP-006 — Customer identity is not phone-normalized

**Evidence**

- Phase 7 discovery says identity is `(AccountId, normalized phone)`.
- implementation uses `CustomerPhone.Trim()` and a unique index on raw `PrimaryPhone`.
- `555-1234`, `(555) 1234`, and `5551234` become different customers.

**Impact**

Repeat-customer history fragments immediately and duplicate-customer races become more likely.

**Required fix**

Introduce one shared conservative phone normalizer and store both:

- display/submitted phone;
- canonical identity phone used by the account-scoped unique index and lookup.

Do not invent country conversion without account/customer country data. At minimum normalize common
formatting consistently and validate a bounded digit length. Use the same normalizer for public and
manual intake and phone search.

### GAP-007 — Concurrent same-customer intake can return 500

**Evidence**

Two requests for a previously unseen `(account, phone)` can both read no customer. The first insert
wins; the second hits `ix_keep_customers_account_phone`. Persistence catches only page-token and
reference-code constraints, so the customer collision propagates.

**Required fix**

Handle the named customer-identity unique violation separately:

- roll back/clear failed tracked state;
- re-read the winning customer;
- apply safe contact update rules;
- retry request/event commit with bounded attempts;
- test two real concurrent submissions against PostgreSQL.

### GAP-008 — Public intake lacks bounded field validation

**Evidence**

Application validation checks only required fields. EF limits are name 200, phone 50, email 320,
description 4000. Oversized input reaches SaveChanges and can become a 500. Email shape and phone
minimum/maximum are also unchecked.

**Required fix**

Add explicit, stable public validation errors and HTTP tests for:

- trimmed required values;
- all storage maxima;
- conservative phone validity;
- email syntax when provided;
- no database mutation on failure.

Keep errors distinct from public gate unavailability; validation does not reveal account/token
state.

### GAP-009 — Repeat intake can erase customer email

**Evidence**

`KeepCustomer.UpdateContactInfo(name, email)` always assigns `Email = email?.Trim()`. A later request
that omits email replaces a known email with null.

**Required fix**

For intake upsert, preserve existing email when the new submission omits it. Replace only when a
nonblank email is provided. A deliberate clear-email workflow, if ever needed, belongs to an
authenticated customer-management action rather than anonymous intake omission.

### GAP-010 — Intake activity timestamps do not reflect origin

**Evidence**

`KeepRequest.Create` defaults to `Origin=Customer`, sets `LastBusinessActivityAt=now`, and leaves
`LastCustomerActivityAt=null`.

**Impact**

The fields say the business acted when only the customer submitted. Session 6 stale/status-check
and post-completion customer-activity logic needs trustworthy semantics.

**Required decision/fix**

Make creation origin-aware:

- Customer origin records customer activity at creation;
- Business origin records business activity at creation;
- stale policy falls back to `CreatedAtUtc` when a side has never acted;
- revisit nullability/read contracts rather than preserving a misleading non-null value solely for
  convenience.

### GAP-011 — Customer-page expiry has no setting workflow

**Evidence**

Terminal-only expiry enforcement and 410 responses exist, but `KeepRequest.Create` leaves
`ExpiresAtUtc` null and no normal status/close/cancel workflow sets it.

**Impact**

Customer bearer pages remain readable indefinitely.

**Required disposition**

Lock a terminal page-retention duration before implementing dedicated close/cancel. Session 6 owns
the close path and the pre-Session 6 cancellation correction owns cancel; both should set expiry
consistently. Keep active/Resolved pages readable regardless of any stale expiry value, preserving
ADR-120.

### GAP-012 — Rate-limit trust boundary and tests are incomplete

**Evidence**

- `GetClientIp` trusts `CF-Connecting-IP` and then `X-Forwarded-For` directly.
- safety depends on a deployment statement that ingress is Cloudflare-only; code does not verify a
  trusted proxy.
- rate limiting is skipped in `Testing`, so current HTTP tests do not prove the 10/minute 429
  contract.

**Required fix**

- configure trusted-forwarder behavior for the actual deployment chain;
- ignore spoofable forwarding headers from untrusted peers;
- add a production-like rate-limit test host or policy test proving partition and 429 behavior;
- retain later spam/honeypot work in its locked V1 slice rather than expanding this fix into a full
  abuse platform.

### GAP-013 — Bearer tokens need log redaction

**Evidence**

Intake and customer-page bearer tokens are route segments. Default request logs and infrastructure
access logs may capture full paths.

**Required fix**

Before staging, define and verify token-safe logging:

- no raw route token in application logs, traces, analytics, exception context, or founder-friction
  reports;
- review Railway/proxy access-log behavior;
- log safe route templates, request/reference IDs only after authorization, or redacted hashes.

### GAP-014 — Legacy alias and ignored opt-in may be accidental contracts

**Evidence**

- no production/cutover database or deployed Continuity client exists;
- `/continuity/public-intake/...` is still exposed;
- `emailNotificationsEnabled` is accepted but ignored.

**Risk**

New clients may accidentally depend on legacy naming or imply an email promise that Keep does not
honor.

**Required decision**

Before frontend implementation, choose deliberately:

- remove both until real compatibility is required; or
- retain with explicit API documentation and ensure UI never presents ignored opt-in as effective.

Recommendation: remove the Continuity alias in a greenfield product and remove/withhold the ignored
field until notification preference behavior exists.

### GAP-015 — Active-link cardinality is ambiguous

**Evidence**

The entity comment says active slug/hash indexes enforce one active link per account. They do not;
multiple active links with different slug/hash values are allowed.

**Required decision**

For V1, prefer one active public intake link per account and enforce a partial unique account index.
If future per-location/source links are desired, model their purpose/source explicitly rather than
accidentally allowing indistinguishable duplicates.

### GAP-016 — Local/runtime database readiness remains incomplete

**Evidence**

- connection-string validation occurs on first DbContext scope rather than host startup;
- Testcontainers proves migrations, but there is no persistent local development database setup,
  reset, seed, and full-stack smoke-test guide.

**Required fix/milestone**

After gap resolution and Session 6, but before notification implementation:

- initialize an empty local PostgreSQL database;
- apply all migrations from zero;
- seed a real account/link/request workflow;
- run API smoke tests manually;
- delete/recreate to prove repeatable initialization;
- add startup configuration validation and concise operator documentation.

---

## Detailed Session 5 Findings

### GAP-017 — Generic attention acknowledgement bypasses feedback review

**Evidence**

- `CanAcknowledgeAttention` returns true for any raised attention, including Closed
  `UnresolvedFeedback`.
- `AcknowledgeAttention` clears the attention reason.
- `MarkFeedbackReviewed` requires active `UnresolvedFeedback`, so it then returns
  `FeedbackReviewUnavailable`.
- Session 5 tests intentionally covered the resulting unavailable state but did not prevent the
  bypass.

**Required fix (already product-locked)**

- generic acknowledge rejects `UnresolvedFeedback` in the domain;
- action metadata hides acknowledge for that state;
- Owner/Admin uses only Mark feedback reviewed;
- add domain and HTTP regression tests.

### GAP-018 — Structured post-feedback contact is blocked

**Evidence**

- contact launchers appear on detail;
- external-contact logging rejects all terminal requests;
- unresolved feedback exists only on Closed requests;
- ADR-280/283 expects direct follow-up context, but structured logging cannot represent it.

**Required fix (already product-locked)**

Allow Owner/Admin external-contact logs only for Closed, unreviewed negative feedback with active
`UnresolvedFeedback` attention. Such contact:

- stays internal;
- does not reopen or change status;
- does not count first response;
- does not clear attention;
- does not mark feedback reviewed;
- remains blocked for all other terminal cases.

### GAP-019 — Feedback navigation is marked implemented but absent

**Evidence**

ADR-282 says feedback review supports context-based next/previous. No navigation DTO/service/API
exists. Session 5 only implemented list + open detail + mark reviewed.

**Required fix**

- correct ADR implementation status/source language;
- include feedback-review context in the Session 6 stateless navigation contract alongside
  closeout and status-check queues;
- do not create a second mutation endpoint.

### GAP-020 — Positive-feedback visibility contradicts ADR-263

**Evidence**

ADR-263 says positive feedback is broadly visible to authenticated viewers with request access and
negative comments are Owner/Admin-only. `KeepRequestDetailMapper` gates every feedback comment to
Owner/Admin, regardless of positive/negative value.

**Required fix**

Recommendation:

- positive comment visible to any user who passes corrected request-row visibility;
- negative comment Owner/Admin-only;
- lower roles may receive safe boolean/timestamp metadata without the negative comment;
- add Owner/Admin/Operator/Viewer tests for positive and negative cases.

---

## Documentation / Decision Ledger Findings

### GAP-021 — Phase 7 ADR statuses need reconciliation

ADR-051, ADR-052, ADR-053, ADR-054, and ADR-059 remain `Locked` even though their applicable
runtime behavior was delivered or later superseded. Reconcile after the gap decisions:

- mark implemented behavior accurately;
- retain `Superseded` where real session auth replaced interim auth;
- update/remove legacy alias and ignored email-opt-in language if GAP-014 changes the contract;
- update DEF-007 after intake-link setup exists;
- close/update DEF-074 after optimistic concurrency exists;
- record feedback-review navigation as implemented only when Session 6 delivers it.

---

## Recommended Gap Resolution Phase

Do not give Claude all findings as one coding task.

### Gap A — Phase 7 data model and public-intake integrity

Scope:

- GAP-005 relational FK/schema audit;
- GAP-006 canonical customer phone identity;
- GAP-007 concurrent same-customer intake recovery;
- GAP-008 bounded public validation;
- GAP-009 preserve known email;
- GAP-010 origin-correct activity fields;
- GAP-015 active-link cardinality decision/index.

Gate:

- focused domain/unit tests;
- real PostgreSQL constraint/race tests;
- migration contains only approved greenfield schema corrections;
- full suite green.

### Gap B — Keep setup and manual intake

Scope:

- GAP-001 Owner/Admin intake-link ensure/status/exceptional transactional replacement;
- GAP-002 authenticated business-created requests;
- GAP-014 legacy alias/ignored opt-in cleanup after explicit decision.

Gate:

- end-to-end account → setup link → public request;
- end-to-end authenticated manual request with `Origin=Business`;
- role/account/OffSeason tests;
- old replaced token rejected and failed replacement preserves the old link;
- raw token never persisted.

### Gap C — Shared authorization and concurrency

Scope:

- GAP-003 one shared request-row policy;
- GAP-004 entity-wide optimistic concurrency and stable 409;
- direct-ID and true-race tests.

Gate:

- Operators cannot read/write another Operator's assigned request;
- watching and intentional unassigned access behave exactly as locked;
- stale writes cannot overwrite newer customer state;
- full suite green.

### Gap D — Session 5 feedback hardening

Scope:

- GAP-017 acknowledgement bypass;
- GAP-018 Closed unresolved-feedback contact exception;
- GAP-020 positive/negative comment visibility;
- GAP-019 documentation carry-forward to Session 6 navigation.

Gate:

- focused domain/action-metadata/HTTP tests;
- customer page remains free of internal contact/review events;
- mark reviewed remains the only review-completion path;
- full suite green.

### Gap E — Edge/deployment hardening and documentation

Scope:

- GAP-012 trusted IP/rate-limit behavior;
- GAP-013 bearer-token log redaction;
- GAP-021 ADR/deferred tracker reconciliation;
- document GAP-011 as a required Session 6 close/cancel dependency;
- retain GAP-016 as the post-Session-6, pre-notification local database milestone.

Gate:

- 429 behavior proven;
- untrusted forwarding headers cannot choose arbitrary limiter partitions;
- no raw bearer tokens in verified application logs;
- ledgers match actual implementation;
- final full build/test pass recorded.

---

## Explicit Non-Goals For The Gap Phase

- Session 6 closeout/status-check implementation itself;
- notification devices, push, badge delivery, or scheduled reminder decisions;
- SSE/WebSockets or the reference polling/signal engine;
- spam/test classification beyond existing validation/rate-limit hardening (`DEF-061` stays in its
  planned V1 slice);
- archive/reopen/rework/callback workflow;
- attachments, SMS delivery, customer identity portals, full CRM, scheduling, estimates, invoices,
  or analytics;
- web/native UI implementation.

---

## Final Exit Gate Before Session 6

- [ ] Gap A complete and migration inspected.
- [ ] Gap B complete; a real account can obtain/share/exceptionally replace intake and manually
      capture a request.
- [ ] Gap C complete; row access and concurrency fail closed.
- [ ] Gap D complete; feedback review has no alternate clearing path.
- [ ] Gap E complete except the explicitly post-Session-6 local DB milestone.
- [ ] Terminal customer-page expiry decision is carried into Session 6 close/cancel design.
- [ ] Full build, unit, architecture, and integration suites pass.
- [ ] Self-review confirms no notification, realtime, archive, scheduling, or analytics scope was
      pulled forward.
- [ ] `docs/session-log.md`, decision index, and deferred tracker reflect exact completed behavior.

Only after this gate should the Session 6 decision/build guide be finalized and handed to Claude.
