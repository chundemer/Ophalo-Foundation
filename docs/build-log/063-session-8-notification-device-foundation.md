# Build Log 063 — Session 8 Part 1 Notification/Device Foundation

**Started:** 2026-06-24  
**Completed:** 2026-06-26
**Status:** S8a–S8e complete; Session 8 closed
**Next free ADR before this log:** ADR-351  

Session 8 Part 1 builds the narrow V1 staff notification/device foundation after Session 7
pilot-safety hardening. It does not build a full notification platform.

---

## Scope

Primary goal: let Keep wake the right staff member for immediate routed work while keeping badges
server-derived and customer data out of push payloads.

In scope:

- account-user-scoped device registration/revocation;
- raw push-token storage with safe fingerprints and token-to-user rebinding protection;
- personal badge count endpoint;
- push adapter interface plus no-op/test delivery implementation;
- non-sensitive payload/display-text contract;
- candidate routing/suppression foundation;
- limited explicit post-commit hooks for selected push-worthy mutation paths.

Out of scope:

- real APNs/FCM adapters and credentials;
- notification ledger, delivery table, outbox, retries, dead-letter, suppression log, or analytics;
- full notification preference matrix, quiet hours, temporary personal silence, urgent mute bypass;
- backend customer SMS/email or customer-facing platform notifications;
- SSE/WebSockets/realtime;
- Demo/InternalTest account classification or scenario packs;
- Planned For-aware push escalation.

---

## Locked Decisions

### ADR-351 — Session 8 is staff push/badge infrastructure, not customer contact delivery

Native `tel:`, `sms:`, and `mailto:` launchers help staff contact customers from inside the app.
APNs/FCM push wakes staff when Keep needs attention off-screen. These are opposite directions and
must not be treated as substitutes.

V1 customer contact remains business-initiated through explicit actions. Keep does not send backend
SMS/email to customers.

### ADR-352 — First delivery slice uses a push adapter abstraction plus no-op delivery

Do not implement real APNs/FCM delivery in the first Session 8 pass. Build the provider interface
and no-op/test adapter first.

The interface must already support real-provider semantics:

- target platform/device token;
- minimal deep-link payload;
- collapse key/thread id;
- priority;
- TTL/expiry;
- delivery result/failure shape.

Real APNs/FCM adapters wait until native bundle IDs, Apple team/key/cert posture, Firebase project,
service account, environment config, and account delivery eligibility gates are known.

### ADR-353 — Demo/InternalTest suppression is a hard gate before real delivery

Session 8 does not implement Demo/InternalTest account classification. While delivery is no-op, this
does not block the foundation.

Before any real APNs/FCM adapter can be enabled, production delivery must be gated so Demo/InternalTest
accounts cannot send real pushes by default. Session 8 should leave an obvious delivery-eligibility
check point for this later gate.

### ADR-354 — V1 notification hooks are explicit application-layer post-commit calls

Use explicit application-layer calls after successful persistence/version rotation. Domain methods do
not send, enqueue, or know about notifications. Do not use EF `SaveChanges` interceptors for delivery.
Do not build a domain-event or multi-channel notification bus in Session 8.

This is a V1 clarity tradeoff with maintenance discipline debt. Before real delivery is enabled,
there must be a push-worthy mutation coverage checklist and tests proving each covered path generates
a candidate or is explicitly badge/list-only.

If a second delivery channel appears later, revisit the architecture rather than stretching the
explicit-call notifier into a hidden multi-channel platform.

### ADR-355 — Devices are dedicated AccountUser-scoped notification records

Do not reuse `AccountSession`. Add a dedicated `AccountUserDevice` or equivalent table scoped to
`AccountId + AccountUserId`.

Required V1 fields:

- `Id`
- `AccountId`
- `AccountUserId`
- `AppInstallationId`
- `Platform`
- `PushToken`
- `PushTokenFingerprint`
- `TokenLastFour`
- `Status` (`Active`, `Revoked`, `FailedPermanent`)
- `AppVersion`
- `DeviceName`
- `CreatedAtUtc`
- `LastSeenAtUtc`
- `RevokedAtUtc`
- `LastDeliveryFailureAtUtc`
- `LastDeliveryFailureReason` (provider code string, capped around 200 chars)

Store raw push tokens from day one because APNs/FCM require the original token for delivery. Treat
tokens as sensitive: never log or return them; use fingerprints/last-four diagnostics only. Prefer
encrypted storage if existing infrastructure supports it; otherwise design the column so later
encryption does not require a contract rewrite.

Registration must handle token-to-user rebinding: if the same token/fingerprint is active for another
`AccountUserId`, revoke the old active binding atomically with the upsert.

### ADR-356 — Device API uses personal `/me/devices/{appInstallationId}` upsert/revoke

Use `/me` for personal account-user-scoped resources.

`PUT /me/devices/{appInstallationId}` registers or upserts the current install. The caller never
supplies `AccountUserId`; it comes from the authenticated session.

Rules:

- validate `appInstallationId` as strict UUID v4 or equivalent safe GUID format before lookup/logging;
- upsert key is `(AccountUserId, AppInstallationId)`;
- body requires `platform` (`ios` or `android`) and `pushToken`;
- body may include `appVersion` and `deviceName`;
- require `platform` on every PUT;
- reject platform changes for an existing install id;
- no optimistic-concurrency header; upsert idempotency is the concurrency model;
- OffSeason does not block registration/revocation;
- APIs never return raw push tokens.

`DELETE /me/devices/{appInstallationId}` revokes the current install and returns `204 No Content`.
Delete is idempotent: missing or already-revoked rows still return `204`.

### ADR-357 — OS badge is a tight personal interrupt count, not unread or workload

`GET /me/badge` returns the authenticated user's personal OS badge count for the active account.
It is server-derived from current request state, not a durable notification ledger and not unread.
Opening a row does not decrement the badge; only underlying work-state changes do.

Response shape:

```json
{
  "count": 3,
  "computedAtUtc": "2026-06-24T18:42:00Z"
}
```

No V1 category breakdown. Future breakdown, if needed, should be additive, preferably an array of
`{ kind, count }` entries rather than flat fields.

Badge predicate:

- if account is OffSeason, return `0`;
- otherwise count visible rows where `AttentionLevel > None` and status is not
  `Closed`, `Cancelled`, `Spam`, or `Test`;
- also count the narrow exception `AttentionLevel = UnresolvedFeedback` and `Status = Closed` for
  Owner/Admin-visible rows;
- muted rows still count because mute suppresses push only, not work accountability;
- do not count needs-status-check, future Follow Up On, future Planned For, ready-to-close backlog,
  customer-page viewed metadata, internal notes, passive history, or invisible rows.

The badge query must reuse existing visibility/scoping concepts and list indexes. It will be called
on app foreground/resume, after mutations, and after opening a push.

### ADR-358 — Push payloads and display text are non-sensitive enum-derived hints

Payloads are minimal, non-sensitive, and deep-link only. They are never the source of truth; clients
refetch API state after opening.

Allowed payload/data fields:

```json
{
  "type": "keep_request_attention",
  "accountId": "...",
  "requestId": "...",
  "eventKind": "customer_message",
  "deepLink": "ophalo://keep/requests/{requestId}",
  "badge": 3
}
```

Allowed adapter metadata:

- collapse key/thread id;
- priority;
- TTL/expiry.

Forbidden in payload and visible display text:

- customer name;
- phone/email;
- message text;
- request description/snippets;
- internal notes;
- feedback comments;
- customer page token/link;
- public intake token/link;
- raw push token;
- dynamic customer/request data;
- message counts;
- sensitive operational/business details.

Visible title/body use a static server-side enum-to-string mapping derived only from `eventKind`.
No interpolation, request data, customer data, or client-authored display text.

Examples:

- `call_requested` -> "A customer requested a callback"
- `customer_message` -> "New message from a customer"
- `cancellation_requested` -> "A customer requested a cancellation"
- `timing_change_requested` -> "A customer wants to change timing"
- `assignment` -> "A request was assigned to you"
- `unresolved_feedback` -> "Customer feedback needs review"

Collapse key/thread id is `{accountId}:{requestId}`.

Priority/TTL derive from `eventKind` at the adapter/candidate layer:

- high priority, short TTL around 1-2 hours: `call_requested`, `cancellation_requested`,
  `timing_change_requested`;
- normal priority, standard TTL around 6-12 hours: `customer_message`, `new_request`,
  `assignment`, unresolved feedback review unless later promoted.

### ADR-359 — V1 push routing is event-type driven; Planned For remains list triage

Do not make V1 push routing time-dependent on `PlannedForDate`. Planned For and Follow Up On labels
shape list priority and operator triage, not push routing.

V1 push-worthy by default:

- new customer-created request needing first response;
- `call_requested`;
- `cancellation_requested`;
- `timing_change_requested`;
- assignment/transfer to you when action is expected;
- unresolved negative feedback/review to Owner/Admin;
- due/overdue Follow Up On for routed users.

Badge/list-only by default:

- ordinary `customer_message`;
- `question`;
- `update_request`;
- `information_added`;
- future Follow Up On;
- future Planned For;
- needs-status-check;
- ready-to-close;
- customer-page viewed metadata;
- normal internal notes/history.

If a customer message/intent raises a specifically push-worthy attention/reason in the domain, push
based on that reason. Otherwise keep it list/badge.

`call_requested` remains push-worthy even when Keep lacks a customer phone number. The mobile client
decides whether to show a `Call customer` action based on available contact info.

### ADR-360 — Request mute suppresses push only

Request mute suppresses push delivery for that recipient only. It does not remove the row from badge
count or list visibility.

Routing order for request-specific push candidates:

1. active eligible Responsible user, if present and unmuted;
2. otherwise active eligible unmuted Watchers;
3. otherwise active eligible Owner/Admin fallback.

Apply final suppressions:

- actor exclusion;
- Viewer;
- inactive/non-active membership;
- stale/ineligible participants;
- user cannot see the row;
- OffSeason;
- terminal rows except Closed + unresolved feedback review to Owner/Admin;
- badge/list-only event kinds.

Muted Responsible recipients are skipped for push routing, allowing fallback to eligible
Watchers/Owner/Admin, but remain accountable in badge/list.

No V1 mute override for urgent events. Suppression logging, soft/hard mute, urgent bypass, temporary
personal silence interaction, and Owner/Admin escalation rules remain later work.

### ADR-361 — Badge is fetched through `GET /me/badge`, not mutation DTOs

Session 8 adds only `GET /me/badge`. Do not add badge fields to existing mutation, detail, or list
responses.

Client refresh pattern:

- foreground/resume -> `GET /me/badge`;
- after successful mutation -> `GET /me/badge`;
- after opening push -> fetch request/list state and `GET /me/badge`.

When real APNs/FCM sets a native badge value in push payloads, that value is a delivery-time hint only
and does not replace the client badge refresh.

### ADR-362 — Store devices, not notification history

Session 8 does not create a notification ledger, candidate table, delivery table,
outbox/retry/dead-letter table, suppression log, or delivery analytics.

Candidate generation is tested through the fake/no-op adapter. Badge count is derived from request
state, not notification rows.

Device row metadata is sufficient for V1:

- permanent token failure later sets `Status = FailedPermanent` and records provider error reason;
- transient failure later records `LastDeliveryFailureAtUtc/Reason` but keeps `Status = Active`;
- no retry loop in V1;
- no-op adapter in Session 8 does not update delivery failure metadata;
- `LastSeenAtUtc` updates only on device registration/upsert, not on delivery attempts;
- real adapter session owns APNs/FCM permanent-vs-transient error mapping.

---

## Claude Coding Sessions

### S8a — Device table + registration/revoke API

**Goal:** Create durable device registration foundation.

Read:

- `docs/session-log.md`
- this build log through S8a
- current auth/session/account-user persistence and API endpoint patterns
- current EF entity/config/migration patterns
- token-safe logging/redaction utilities from Session 7

Implement:

- `AccountUserDevice` or equivalent entity/config/migration.
- `PUT /me/devices/{appInstallationId}` register/upsert.
- `DELETE /me/devices/{appInstallationId}` revoke.
- Required UUID v4 `appInstallationId`.
- Required `platform` on every PUT; reject platform mismatch for existing install id.
- Raw token storage plus fingerprint/last-four.
- Status enum: `Active`, `Revoked`, `FailedPermanent`.
- `CreatedAtUtc`, `LastSeenAtUtc`, `RevokedAtUtc`, `LastDeliveryFailureAtUtc`,
  `LastDeliveryFailureReason`.
- Token-to-user rebinding: same token under another account user revokes old active binding
  atomically.
- Safe response metadata only; never return raw push token.
- Focused tests for success, idempotent upsert, platform mismatch, token rebinding, idempotent
  delete, auth, OffSeason not blocking, and token redaction.
- Add a small test helper/builder for device rows if useful for later slices.

Do not implement:

- badge endpoint;
- notification candidates;
- APNs/FCM adapter;
- notification history/outbox.

Completion gate:

```text
focused device API tests green
migration inspected
raw tokens are not logged or returned
dotnet build
```

### S8b — Badge endpoint

**Goal:** Add server-derived personal OS badge count.

Read:

- this build log through S8b
- request list visibility/scoping and count logic
- OffSeason/account access snapshot patterns
- feedback/unresolved-feedback attention fields

Implement:

- `GET /me/badge`.
- Response `{ count, computedAtUtc }`.
- Predicate from ADR-357/ADR-360:
  - OffSeason -> `0`;
  - visible rows with `AttentionLevel > None` and non-terminal status;
  - Closed + UnresolvedFeedback Owner/Admin exception;
  - muted rows still count;
  - needs-status-check excluded.
- Reuse existing visibility/scoping concepts and list indexes.
- Focused tests for Owner/Admin, Operator scoped visibility, Viewer, OffSeason zero, muted row
  still counted, terminal exclusion, Closed unresolved-feedback exception, and needs-status-check
  exclusion.

Do not implement:

- badge fields on mutation/detail/list responses;
- category breakdown;
- notification candidates.

Completion gate:

```text
focused badge tests green
query shape reviewed against list indexes/scopes
dotnet build
```

### S8c — Push abstraction + no-op adapter + candidate/routing foundation

**Goal:** Add compile-ready notification foundation before mutation hooks.

Preflight warning: this is the highest-risk slice for file-gate pressure. Count production files
explicitly before edits. If tight, defer some suppression-rule coverage or mapping detail to S8d
while keeping the interface and DI stable.

Read:

- this build log through S8c
- device entity/persistence from S8a
- badge service from S8b
- request participation/visibility/action policy
- customer intent and feedback event/attention fields

Implement:

- Push adapter interface shaped for APNs/FCM:
  - target platform/token;
  - payload;
  - display title/body;
  - collapse key/thread id;
  - priority;
  - TTL/expiry;
  - result/failure shape.
- No-op/test adapter.
- Static eventKind -> display text mapping.
- Minimal payload/deep-link builder.
- Candidate/routing/suppression service if it fits the file gate.
- DI/service registration completed so S8d can inject the post-commit notifier.
- Focused unit tests for mapping/payload forbidden-data posture and included routing rules.

Do not implement:

- real APNs/FCM;
- mutation service hooks;
- notification history/outbox/suppression logs.

Completion gate:

```text
DI compiles with no-op adapter
focused candidate/routing tests green for included rules
dotnet build
```

### S8d — Limited push-worthy mutation hooks

**Goal:** Prove explicit post-commit notification calls on selected mutation paths.

In scope:

- `call_requested`;
- `cancellation_requested`;
- `timing_change_requested`;
- assignment/transfer-to-you;
- unresolved feedback review to Owner/Admin.

Explicitly deferred, not forgotten:

- new customer-created request needing first response;
- due/overdue Follow Up On;
- Planned For-aware push escalation;
- full P6e coverage checklist before real APNs/FCM adapter.

Implement:

- Explicit application-layer calls only after successful commit.
- Fail-soft behavior: candidate generation/delivery failure does not fail request mutation.
- Candidate tests for each in-scope path.
- Negative tests for actor exclusion, mute push suppression with badge/list unaffected, Viewer,
  OffSeason, and no-device/no-candidate outcomes where feasible inside gate.

Do not implement:

- real APNs/FCM;
- notification history/outbox/suppression log;
- additional mutation hooks beyond the locked list.

Completion gate:

```text
in-scope mutation candidate tests green
fail-soft proof green
coverage checklist records deferred push-worthy paths
dotnet build
```

### S8e — Session 8 ledger and regression gate

**Goal:** Reconcile docs and prove the foundation is ready for the later real-adapter slice.

Scope:

- Update `docs/session-log.md`, `docs/deferred-topics.md`, and `docs/decisions/decision-index.md`
  for completed slices.
- Confirm DEF-012/DEF-021 reflect implemented device/badge/no-op foundation and remaining real
  APNs/FCM/full-platform work.
- Confirm DEF-077, DEF-079, DEF-080 remain deferred.
- Run proportionate broader tests and full suite if feasible.

Completion gate:

```text
docs reconciled
decision index next ADR is correct
focused + broader regression gate green, or reason recorded
next work points to real-adapter or remaining hook coverage slice
```

**Outcome (2026-06-26):**

- DEF-012 updated: V1 foundation (device registration, badge, no-op adapter, hooks) marked
  implemented; remaining deferred items listed.
- DEF-021 updated: V1 foundation complete; real APNs/FCM gated on DEF-079 (account classification).
- DEF-077, DEF-079, DEF-080 confirmed deferred — no change.
- Decision index confirmed: ADR-351 through ADR-362 all present; next free ID ADR-363 is correct.
- Full suite: **864 unit · 14 arch · 676 integration = 1,554 total, 0 failures.**
- Session 8 foundation is ready for the real-adapter slice (gated on DEF-079).
