# Build Log 043 — Keep V1 Product Scope + Freshness Lock

**Phase:** V1 product lock / pre-go-live implementation guide
**Date:** 2026-06-19
**Status:** Decisions locked. Use this as the current V1 boundary before new Keep coding sessions.
**Build logs preceding this:** `041-phase-8-b5-session-4-filters-search-closed-history-pagination-decisions.md`, `042-phase-8-b5-session-5-feedback-review-completion-decisions.md`
**ADRs locked:** 288..292
**Follow-up pilot-support lock:** `044-pilot-support-feedback-and-updates.md` (ADR-293..294)
**Next free ADR:** ADR-295

---

## Purpose

This log locks the Keep V1 product boundary after the B5 request-list, external-contact,
assignment/watch, filter/history, and feedback-review decisions.

Keep V1 is an active request accountability loop, not just an inbox. It must protect the customer
promise by capturing requests, surfacing attention, waking the right person up, making contact fast,
preserving team memory, and helping Owner/Admin close the loop.

The implementation goal through go-live:

```text
Keep must feel fresh and accountable without paying the realtime infrastructure tax early.
```

---

## Locked Decisions

### ADR-288 — Keep V1 product boundary is locked

Keep V1 includes the operational loop required to prevent dropped customer promises.

In V1:

- public intake and customer request pages;
- authenticated operator/admin request list and detail;
- deterministic triage views, search, filters, closed/cancelled history, and pagination;
- assignment/watch/mute routing and a prominent Operator Available/Unassigned surface;
- external-contact logging for calls, native texts, email, in-person, and other contact;
- customer-visible Keep updates and customer replies/issues;
- closed-request feedback;
- Owner/Admin feedback review completion;
- ready-to-close and stale status-check queues before go-live;
- basic push notifications, deep links, and server-derived badge counts;
- spam/test/garbage intake handling and metrics exclusion before go-live.
- in-app pilot feedback/report-friction hook and Pilot Updates page before go-live.

Out of V1:

- SSE/WebSocket live list streaming;
- full notification preference matrix, quiet hours, durable outbox/retries/dead-letter, and delivery
  analytics;
- backend-owned SMS sending;
- identity-bound customer portals;
- attachments;
- branded customer URLs;
- full analytics/SLA/reporting platform;
- archive/unarchive workflow;
- full signal/projection engine;
- internal/team-memory search;
- formal callback/rework linked-request model.
- full helpdesk/ticketing system, CMS, public status page, roadmap portal, or feature-voting board.

Reason: V1 should prove Keep's core accountability loop before broadening into platform depth.

### ADR-289 — Basic push and badges are V1 core; heavy notification infrastructure is later

Basic native push and badge counts are V1 product requirements.

V1 notification/device slice:

- stores device registrations in an `AccountUserDevice` or equivalent notification-device table;
- supports multiple devices per user, token rotation/revocation, platform, and basic failure/last-seen
  metadata;
- derives badge counts from current actionable request state rather than a separate badge ledger;
- sends minimal native push payloads that deep-link into API-refreshed request/list state;
- applies actor exclusion, role/membership eligibility, request participation, mute state, and
  OffSeason suppression;
- delivers fail-soft: request writes must not fail because APNs/FCM delivery failed.

Deferred notification depth:

- custom user/account preference matrix;
- quiet hours/business-hours delivery policy;
- durable notification rows, outbox, retries, dead-letter, delivery/open analytics;
- email/push channel fallback matrices;
- delegated notification admin controls;
- customer notification preferences/opt-out beyond existing public surfaces.

Reason: If Keep is silent, the request loop can still fail. But V1 does not need a full notification
platform to wake the right person up.

### ADR-290 — V1 is fresh, not realtime

Keep V1 does not implement SSE, WebSockets, or live list streaming.

V1 freshness matrix:

```text
Operator write        -> refetch authoritative request/list state after success
App/tab focus         -> force sync active list/detail
Mobile app resume     -> force sync active list/detail and badge counts
User gesture          -> pull-to-refresh
Active list viewing   -> 30-60 second polling on operational list views
Urgent off-screen     -> native push + badge update + deep link
```

Rules:

- clients may use optimistic affordances locally, but backend state remains authoritative;
- `viewCounts` and badges are server-derived snapshots, not realtime subscriptions;
- polling must pause or slow when the app is backgrounded;
- push notifications wake users up; polling keeps an already-open surface from looking abandoned;
- all clients must handle stale cursors and refetch after mutations.

Deferred realtime work:

- `/keep/requests/stream`;
- SSE/WebSocket connection lifecycle;
- event replay/missed-event backlog;
- event ordering guarantees;
- cross-user instant row updates;
- presence/read receipts.

Reason: polling plus push covers most V1 operational value with far less complexity, better mobile
battery/network behavior, and simpler auth/session renewal.

### ADR-291 — V1 contact posture uses native launchers plus explicit logs

V1 may launch native device contact surfaces:

```text
tel:
mailto:
sms:
```

Rules:

- tapping call/email/text launches an affordance only;
- launching a native app does not prove contact occurred;
- only explicit external-contact logging creates durable audit/timeline evidence;
- logged outbound call/email/text can count first response or clear attention only through the
  backend effect matrix and operator confirmation;
- customer SMS replies do not enter Keep in V1;
- Keep does not send platform SMS in V1.

Reason: V1 should reduce operator friction without taking on Twilio/Telnyx-style cost, compliance,
delivery, and reply-ingestion complexity.

### ADR-292 — Go-live coding order

Use this order unless a security/privacy/compliance issue, schema blocker, or true pilot blocker
forces a change:

1. Complete Session 4 list navigation: views, filters, search, history, pagination, counts, query
   validation.
2. Complete Session 5 feedback review: mark reviewed, review metadata, aging, queue/detail action
   contracts.
3. Design and implement Session 6 closeout hygiene: ready-to-close queue, stale status-check queue,
   customer-activity warnings, close-and-next ergonomics.
4. Implement V1 notification/device foundation: `AccountUserDevice`, derived badges, minimal push,
   deep links, fail-soft dispatcher.
5. Implement spam/test/garbage intake handling and metrics exclusion.
6. Implement pilot support surfaces: 1-tap Report Friction and Pilot Updates.
7. Build web/native UI against server action metadata and freshness matrix.
8. Run pilot readiness QA, onboarding playbook, and operator app-switching orientation.

Do not pull in:

- SSE/WebSockets;
- backend-owned SMS;
- full notification preferences/outbox/retry platform;
- attachments;
- branded URLs;
- customer identity portals;
- full analytics/reporting.
- helpdesk/ticketing dashboard, CMS, public status page, roadmap portal, or feature-voting system.

Reason: this order finishes the accountability loop before adding platform breadth.

---

## Coding Session Guidance

Every implementation session until go-live should check this file first.

Required carry-forward rules:

- server action metadata remains authoritative for UI affordances and action effects;
- customer-visible, internal-only, external-affordance, clears-attention, counts-first-response, and
  changes-status effects must be explicit;
- OffSeason/read-only blocks normal writes and suppresses request notifications;
- Viewer stays read-only and notification-ineligible by default;
- Operator default list hides unassigned work, but Available/Unassigned must be prominent in UI;
- feedback review remains Owner/Admin-only;
- ready-to-close excludes active attention and must warn about recent customer activity or unresolved
  feedback;
- notification delivery is fail-soft and never rolls back the business write;
- Report Friction posts to OpHalo API first; clients never contain private Slack/Discord webhook
  secrets;
- Pilot Updates content stays plain, operational, and founder-maintained;
- realtime streaming is not part of V1 freshness.

Verification expectations:

- focused unit/integration tests for every new backend contract;
- HTTP tests for auth, role, OffSeason, invalid input, and no-mutation-on-denied-access paths;
- client tests or QA notes for refetch-after-write, focus/resume sync, pull-to-refresh, and polling
  intervals once UI work begins;
- pilot-support tests or QA notes for friction submission, no sensitive customer data
  auto-attachment, founder-channel forwarding failure behavior, and Pilot Updates rendering;
- no customer page exposure of internal/operator-only fields;
- docs/session-log updated with exact tests run and any follow-up DEF/ADR changes.
