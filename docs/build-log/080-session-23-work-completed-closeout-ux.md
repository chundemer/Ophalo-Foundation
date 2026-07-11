# Build Log 080 — Session 23: Work Completed And Closeout UX

**Started:** 2026-07-10
**Status:** Draft — decision locked; targeted PWA implementation started
**Session name:** S23 work-completed / closeout ceremony / request lifecycle language
**Next free ADR before this log:** ADR-434
**Next free ADR after this log:** ADR-435

---

## Purpose

This log captures the gap found while reviewing the request lifecycle before moving into
pre-deployment cleanup.

The backend already has the lifecycle states and permission gates:

```text
active operational work -> resolved -> closed -> optional feedback review
```

The product gap is that the PWA hides the key human actions behind generic status controls:

- Operators need a single obvious action for "the work is done/performed."
- Owner/Admin users need an obvious closeout ceremony after work is completed.
- The request list/detail language should not expose "Resolved" as the primary staff-facing phrase
  when the business meaning is "work completed."

This is a UX/workflow gap, not a new backend lifecycle-state gap.

Historical decisions already separated operator completion from Owner/Admin closeout. This session
refines ADR-193 and ADR-384 by locking the visible labels and detail-page affordances.

---

## Locked Decision

### ADR-434 — Work completed and closeout are separate V1 actions

See `docs/decisions/ADR-434-work-completed-and-closeout-ux.md`.

Summary:

- Backend/API status `resolved` remains unchanged.
- Staff-facing UI displays `resolved` as **Work completed**.
- Operators get a single **Mark work done** action when server action metadata allows transition to
  `resolved` and no active attention is present.
- If active attention is present, the UI should make attention resolution primary. The backend may
  still allow `resolved`, but the primary **Mark work done** affordance must not make completion look
  like attention cleanup.
- Owner/Admin closeout remains separate and moves eligible `resolved` requests to `closed`.
- `Ready to Close` and **Close request** require `resolved` with no active attention.
- `Closed` remains the terminal state that enables one-time customer feedback.
- Customers do not directly close or reopen requests in V1.

---

## Questions Answered

### Do we need a new status for "work completed"?

No.

The existing `KeepRequestStatus.Resolved` already means the business believes the work/request is
complete. Adding a second enum for `WorkCompleted` would duplicate the lifecycle concept and create
unnecessary migration/reporting risk.

Instead:

```text
API/domain: resolved
PWA/native label: Work completed
```

### Should Operators close requests?

No.

Operators should be able to mark the work done when policy allows. Closeout remains Owner/Admin
because it is a management/accountability step that archives the active request and opens the
post-close feedback window.

### Should customers close requests?

Not in V1.

Customer feedback remains post-close and one-time. Negative feedback creates Owner/Admin review work
without automatically reopening the request. Customer-confirmed close, reopen-from-feedback,
auto-close, and linked rework/callback requests remain future lifecycle decisions.

### Where should "Ready to Close" appear?

The existing Owner/Admin `Ready to Close` request-list view remains the queue-level surface.

Request detail should also show a clear closeout action when `availableActions.canClose == true`;
the action should not require discovering `Closed` inside the generic status dropdown.

### What does the operator button do?

The operator-facing button uses the existing status mutation:

```text
PATCH /keep/requests/{requestId}/status
status: "resolved"
X-Keep-Request-Version: {detail.version}
```

It sends no customer message by default. If a customer-facing update is needed, the existing customer
update composer remains available.

### Can work be marked done while the request still needs attention?

Backend: yes, when the server exposes `resolved` in `allowedStatuses`.

V1 UI: do not present this as the normal primary path.

This matches the older closeout posture:

- **Mark work done** means the operator/staff believes the work has been performed.
- It does not clear attention.
- It does not count as a customer response unless a customer-visible message is also sent through an
  existing update/status-message path.
- If active attention remains, the request should still appear in attention surfaces and must not
  appear as eligible closeout work.
- Owner/Admin cannot close it until the attention is handled or cleared through the proper workflow.

This is important for real service work: the field job may be complete while the business still owes
the customer a reply, confirmation, cancellation follow-up, timing clarification, or feedback review.

However, because the app is now closer to pilot UX, active attention must win the detail-page
hierarchy. When attention is active, the primary surface should be the **Needs attention** guidance
and its recommended resolution action, not an unqualified **Mark work done** button.

Accepted V1 UI behavior:

- If `attentionLevel == "none"` and `allowedStatuses` includes `resolved`, show the normal primary
  **Mark work done** card.
- If active attention exists and `allowedStatuses` includes `resolved`, hide or demote the normal
  card. If the action remains available, the copy must be explicit, e.g. **Mark work done, attention
  remains**, with helper text explaining that it will not clear attention and will not make the
  request ready to close.
- If the user needs to reply/contact/acknowledge/review feedback, the attention card and related
  action should stay above work completion in the action order.

---

## Implementation Shape

### PWA request detail

Add a primary work-completion card in the action rail/mobile action stack when:

```text
availableActions.canChangeStatus == true
allowedStatuses includes "resolved"
current status != "resolved"
attentionLevel == "none"
```

Button label:

```text
Mark work done
```

Success:

- update request detail state with the returned DTO;
- status badge now reads **Work completed**;
- Owner/Admin can see the row in `Ready to Close` only if no active attention blocks closeout.

When active attention exists:

- do not show the normal primary **Mark work done** card;
- keep Needs Attention guidance and the recommended resolution action visually ahead of completion;
- if a completion action is still exposed, use explicit warning copy:

```text
Mark work done, attention remains
```

and explain:

```text
This records that the work was performed, but this request still needs attention before it can be
closed.
```

Failure:

- stale version: show refresh/conflict copy and disable repeated submission;
- other errors: show retry copy.

### PWA request list

- Status badge maps `resolved` to **Work completed**.
- Status filter maps `resolved` to **Work completed**.
- `Ready to Close` remains Owner/Admin-only.
- `Ready to Close` includes only `resolved` rows with `attentionLevel == none`.
- Resolved/work-completed rows with active attention remain attention work, not closeout work.

### Owner/Admin closeout detail follow-up

The next polish pass should add an explicit closeout card when:

```text
availableActions.canClose == true
allowedStatuses includes "closed"
```

Button label:

```text
Close request
```

This should call the same status endpoint with `status: "closed"`. If the user arrived from
`view=ready_to_close`, preserve `navView=ready_to_close` so existing close-and-next navigation can
continue to work.

If `status == "resolved"` but active attention remains, show no closeout button. The detail page
should keep directing the user through the appropriate attention-resolution surface instead.

### Mobile ordering

Native and narrow PWA layouts should use the same hierarchy:

1. Needs Attention guidance and recommended attention-resolution action.
2. **Mark work done** only when no active attention remains.
3. **Close request** only for Owner/Admin after work completed and attention cleared.
4. Customer update / log contact / notes and other secondary work controls.

---

## Initial Local Implementation Note

An initial PWA patch was started in this session:

- `web/ophalo-app/src/pages/RequestDetail.tsx`
  - maps `resolved` to **Work completed**;
  - treats `resolved`/`closed` as success badge statuses;
  - adds `WorkDoneCard` with **Mark work done**.
- `web/ophalo-app/src/components/RequestRow.tsx`
  - maps row status `resolved` to **Work completed**.
- `web/ophalo-app/src/pages/Requests.tsx`
  - maps the status filter label to **Work completed**.

Validation run:

```text
pnpm typecheck
```

Result: clean.

Follow-up needed: adjust the started `WorkDoneCard` implementation so it follows the refined
attention-first rule above. The current local patch was created before this refinement and may show
the normal **Mark work done** card whenever `allowedStatuses` includes `resolved`.

---

## Proposed Code Slices

These slices are intentionally small because `RequestDetail.tsx` is already a pre-deployment
decomposition target. Finish or explicitly defer the lifecycle UX before starting build-log 077 file
splitting.

### S23a — PWA status language and attention-safe Mark Work Done

Scope: authenticated PWA workbench only.

Tasks:

- Keep staff-facing `resolved` labels as **Work completed**.
- Adjust the started `WorkDoneCard` so the normal primary card renders only when:

```text
availableActions.canChangeStatus == true
allowedStatuses includes "resolved"
current status != "resolved"
attentionLevel == "none"
```

- In active-attention states, do not show the normal primary **Mark work done** card.
- Keep Needs Attention guidance and recommended resolution actions visually above completion.
- Preserve the existing status endpoint contract; do not add a backend endpoint.

Verification:

```text
cd web/ophalo-app
pnpm typecheck
```

### S23b — PWA Owner/Admin Close Request card

Scope: authenticated PWA request detail.

Tasks:

- Add an explicit closeout card when:

```text
availableActions.canClose == true
allowedStatuses includes "closed"
current status == "resolved"
attentionLevel == "none"
```

- Card copy:

```text
Ready to close
Close this request when the business is done managing it. The customer can still leave one-time
feedback from their request page.
Close request
```

- Call the existing status endpoint with `status: "closed"` and current `detail.version`.
- Preserve `navView=ready_to_close` / close-and-next behavior if the current detail context already
  supports it. If not, record the navigation gap instead of widening this slice.

Verification:

```text
cd web/ophalo-app
pnpm typecheck
```

### S23c — Ready-to-close row/detail polish

Scope: authenticated PWA request list/detail copy only.

Tasks:

- Rows in `Ready to Close` should read as **Work completed** and communicate **Ready for closeout**.
- If existing row metadata indicates customer activity after work completion, show a warning cue
  without claiming customer acceptance.
- Do not add backend metadata in this slice.

Verification:

```text
cd web/ophalo-app
pnpm typecheck
```

### S23d — Native/mobile carry-forward audit

Scope: discovery first.

Tasks:

- Audit mobile request detail for `resolved` labels, Mark Completed / Mark work done behavior, and
  Owner/Admin closeout action availability.
- Apply the same attention-first hierarchy only if existing mobile contracts already expose the
  required status/action/attention metadata.
- If not, record the follow-up here and defer implementation.

Verification:

- Run the existing mobile type/lint command if clear from the repo.
- Otherwise document that mobile verification was not run.

---

## Out Of Scope

- New backend status enum or migration.
- Changing attention-clearing semantics for `resolved`.
- Backend restriction that prevents `resolved` while attention exists. This session locks UI
  hierarchy first; backend tightening can be considered only if pilot behavior proves the flexible
  state is harmful.
- Customer direct close/reopen.
- Auto-close after customer confirmation or inactivity.
- Formal rework/callback linked request model.
- Batch closeout.
- Feedback analytics, review prompts, or public review generation.

---

## Exit Criteria

This gap is closed when:

1. Operators have a visible single-click **Mark work done** action on eligible no-attention request detail.
2. Owner/Admin users have a visible **Close request** action on eligible work-completed request detail.
3. List/detail/filter labels use **Work completed** for `resolved`.
4. Work-completed rows with active attention stay out of `Ready to Close` and do not show **Close request**.
5. Active-attention request detail keeps attention resolution visually ahead of work completion.
6. Existing backend policy remains authoritative through `availableActions` and `allowedStatuses`.
7. TypeScript and relevant request-detail tests/checks pass.
