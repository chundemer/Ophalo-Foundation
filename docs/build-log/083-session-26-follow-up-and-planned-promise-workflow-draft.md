# Build Log 083 — Follow-up And Planned Promise Workflow

**Started:** Drafted 2026-07-12; decision lock 2026-07-13  
**Status:** Ready for implementation preflight  
**Related ADRs:** ADR-337, ADR-338, ADR-339, ADR-359, ADR-406, ADR-435, ADR-439, ADR-440, ADR-441  
**Next free ADR:** ADR-442

---

## Purpose

Late Session 25 review exposed that Follow Up On and Planned For are technically present but not yet
good enough as a promise workflow.

The product problem is not just "clear a date." The real workflow is:

```text
I said this needed follow-up or work timing.
Today arrived.
I did something.
Keep should help me record what happened and either clear or reset the promise.
```

Current implementation has useful pieces:

- set Follow Up On;
- clear Follow Up On;
- set Planned For;
- clear Planned For;
- log external contact;
- add internal note;
- send customer update;
- list/detail timing markers.

But those pieces are still disconnected. A business user can see a date, but Keep does not yet guide
the promised action through a clean completion path.

---

## Locked Product Semantics

### Follow Up On

ADR-439 locks Follow Up On as lightweight, business-owned promise protection attached to a request.

Tighter definition:

```text
Follow Up On is an internal staff-owned date that tells Keep:
"Do not let this request go quiet. Bring it back to the responsible staff member on this date
because the customer/business loop is not fully settled yet."
```

Rules:

- Owners/Admins may set Follow Up On where they have request access.
- Operators may set Follow Up On when they have operate access to the request.
- Customers may request timing/follow-up but do not directly create or edit internal Follow Up On.
- Internal Follow Up On is not shown on the customer page by default.
- Setting Follow Up On requires date + reason; note is optional except `other`, which requires note.
- Future Follow Up On remains quiet timing metadata and suppresses stale/status-check noise.
- Due/overdue Follow Up On becomes active operational attention unless a stronger active attention
  reason already owns the request.

### Follow-up Completion

ADR-440 locks Follow Up On completion and missed follow-up handling.

Rules:

- Completing Follow Up On requires a lightweight completion reason, not mandatory free text.
- Silent clear is not the default completion path.
- Follow-up can be completed, moved, or left active after recorded activity.
- Customer updates and external contact logs are valid audit records when those paths are used.
- Internal/no-longer-needed completion creates an internal completion event.
- Missed follow-ups remain visible as overdue active promise work until completed or moved.
- Missed follow-ups are recorded for metrics; they are never hidden or expired away.
- Completion should use a narrow backend command so audit activity and timing state commit together.

Use `Overdue follow-up` / `Follow-up overdue` in product UI. Avoid `expired`.

### Planned For

ADR-441 locks Planned For as internal timing context only in V1.

Rules:

- Planned For can be set, changed, or removed.
- Planned For does not get a completion workflow.
- Future Planned For is a quiet timing chip.
- Planned today is visible timing context but not customer-promise attention.
- Past Planned For stays visible as `Planned date passed`.
- Past Planned For should prompt staff to mark work done, move the planned date, or remove it.
- Active requests with past Planned For may feed needs-status-check/review until resolved.

---

## Purpose Matrix

What Follow Up On is for:

| Use | Example |
|---|---|
| Customer check-in | Customer asked us to check back Friday. |
| Business delay follow-up | Parts arrive Thursday; follow up after that. |
| Waiting on third party | Waiting on supplier response; check again Monday. |
| Timing uncertainty | Weather blocked work; revisit tomorrow. |
| Promise made during contact | Told customer we'd update them next week. |
| Internal review before customer update | Owner needs to confirm estimate before replying. |

What Follow Up On is not for:

| Not for | Why |
|---|---|
| Scheduling a job | That is Planned For / future dispatch territory. |
| Booking appointments | Keep is not a calendar or scheduling system. |
| Generic personal tasks | Follow Up On should belong to a customer request. |
| Proof that contact happened | External contact and customer update history own that evidence. |
| Automatic customer notification | Follow-up is internal unless staff explicitly updates the customer. |
| Closing the request | It may lead to work done or closeout, but it is not closure itself. |

---

## Surface Scope

This is PWA command-center work for initial pilot.

| Surface | Decision |
|---|---|
| PWA desktop detail | Full follow-up completion workflow. |
| PWA mobile-width detail | Full workflow, frictionless/tap-first. |
| PWA request list | Shows due/overdue/past timing signals and routes into detail. |
| Native mobile | Deferred from initial pilot; planned for early post-pilot/early release. |
| Customer page | No internal Follow Up On / Planned For display by default. |

PWA mobile requirements:

- one obvious `Record follow-up` action;
- bottom sheet or focused full-screen panel on small screens;
- tap-first completion reasons;
- optional text, not mandatory typing;
- quick-pick date options plus date picker when moving follow-up;
- return to detail with updated state and clear confirmation;
- usable one-handed between jobs.

---

## Implementation Slices

Hard gate unless explicitly split: at most 3 mutation families, 8 production files, and 12 total
changed files including tests/docs per slice.

### S83a — Backend verification and attention gap closure

Goal: confirm and, if needed, implement ADR-439 due/overdue Follow Up On attention behavior.

Preflight:

- Inspect current Follow Up On domain/application/list/detail behavior.
- Confirm whether due/overdue Follow Up On sets or derives active operational attention/ranking.
- Confirm stronger attention reasons remain primary.
- Confirm future Follow Up On suppresses needs-status-check/stale noise.

Expected changes if gap exists:

- backend attention/list/detail mapping or service logic;
- focused unit/integration tests for future, due today, overdue, stronger-attention coexistence, and
  move/clear effects.

Verification:

- focused Keep timing/list/detail tests;
- broader relevant unit/integration suite if attention/list logic changes.

### S83b — Follow-up completion backend command

Goal: add the narrow atomic command from ADR-440.

Candidate route:

```text
POST /keep/requests/{requestId}/follow-up-resolution
```

Candidate outcomes:

```text
complete
move
keep_active
```

Contract:

- requires `X-Keep-Request-Version`;
- returns updated `KeepRequestDetailResult`;
- preserves account/role/row/action authorization;
- fails without partial activity/clear state;
- records internal completion/move/keep-active events;
- does not expose customer-visible copy or notify customers;
- does not implement Planned For completion.

Tests:

- owner/admin success;
- operator with operate access success;
- viewer forbidden;
- cross-account/unknown fail-closed;
- stale version conflict;
- complete clears Follow Up On and records event;
- move sets new date/reason and records event;
- keep_active records activity and leaves Follow Up On active;
- due/overdue attention clears only when follow-up attention was the active reason.

### S83c — PWA detail follow-up completion workflow

Goal: implement the command-center detail workflow with mobile-width PWA as first-class.

Entry points:

- `Record follow-up` in due/overdue detail banner.
- Timing panel affordance when Follow Up On is active.

UX:

- desktop: focused modal/drawer;
- mobile width: bottom sheet or focused full-screen panel;
- tap-first reason/outcome controls;
- optional note;
- move flow with quick-pick dates plus date input/picker;
- clear confirmation after success.

Constraints:

- use server-provided capabilities and `version`;
- preserve drafts on conflict/error;
- do not show internal Follow Up On on customer page;
- do not add customer notifications;
- do not make Planned For completable.

Verification:

- TypeScript;
- PWA build;
- responsive manual check or screenshot/checklist for desktop and mobile width.

### S83d — Request list timing signals

Goal: make list scan signals match ADR-439/440/441 without implementing inline completion.

Behavior:

- future Follow Up On remains quiet timing metadata;
- due today Follow Up On promoted as active promise signal;
- overdue Follow Up On shown as `Overdue follow-up`;
- past Planned For shown as `Planned date passed`;
- list actions route to detail workflow rather than completing inline.

Tests:

- mock/server labels for future, today, overdue, and past planned;
- no customer-visible exposure of internal dates;
- stronger attention reason remains primary where applicable.

### S83e — Closeout docs and early native follow-up note

Goal: document what landed and what remains deferred.

Update:

- build-log 083 completion notes;
- decision-index only if new ADRs are added beyond ADR-439..441;
- session-log next session state;
- deferred/native note that native command-center follow-up completion is early post-pilot/early
  release, not initial pilot.

---

## Non-Goals

- No customer self-scheduling.
- No customer direct editing of Follow Up On.
- No customer-page display of internal Follow Up On or Planned For by default.
- No dispatch calendar.
- No automatic customer notifications from timing changes.
- No backend SMS.
- No proof-of-send semantics.
- No broad recurring task engine.
- No native mobile command-center implementation in initial pilot.

---

## Acceptance Summary

The production-worthy shape is:

```text
Due/overdue promise -> Record follow-up -> choose lightweight reason/outcome
-> complete, move, or keep active -> durable audit + updated detail state
```

Planned For remains:

```text
Internal timing context -> planned today / planned date passed
-> mark work done, move planned date, or remove planned date
```
