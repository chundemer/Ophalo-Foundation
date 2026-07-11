# ADR-434 — Work Completed And Closeout UX

**Status:** Locked  
**Session:** S23 work-completed closeout gap  
**Next free ADR after this:** ADR-435

---

## Context

Keep already has lifecycle support for request completion and closeout:

- Operators and eligible staff can move active work to backend/API status `resolved`.
- Owner/Admin users can close a `resolved` request when no active attention blocks closeout.
- `Closed` is terminal and enables one-time customer feedback.
- Negative closed feedback creates Owner/Admin review work without reopening the request.
- Existing decisions already require `Ready to close` to exclude active attention.

The gap is not the domain model. The gap is that the PWA currently makes staff discover these
workflows through generic status controls and backend-flavored language.

For service businesses, "Resolved" is less natural than "Work completed" or "work done." Operators
need a field-action button. Owner/Admin users need a closeout action.

This ADR refines ADR-193 and ADR-384. It preserves their lifecycle boundary while locking the
staff-facing labels and request-detail affordances.

---

## Decision

Use the existing backend lifecycle. Do not add a new status.

### Naming

Staff-facing UI labels backend/API status `resolved` as:

```text
Work completed
```

The backend enum, API slug, persistence, tests, and contracts remain:

```text
KeepRequestStatus.Resolved
resolved
```

### Operator work-completion action

Request detail must expose a single primary action for eligible operators/staff:

```text
Mark work done
```

Render it only from server-owned action metadata:

```text
availableActions.canChangeStatus == true
availableActions.allowedStatuses includes "resolved"
current status != "resolved"
attentionLevel == "none"
```

The action uses the existing status endpoint:

```text
PATCH /keep/requests/{requestId}/status
body: { "status": "resolved" }
X-Keep-Request-Version: {detail.version}
```

It does not send a customer-facing message by default.

It also does not clear attention.

The backend may expose `resolved` as an allowed transition while active attention remains. That
flexibility is allowed for V1 because field work can be physically performed while customer/business
follow-up is still owed.

The V1 UI must not make that path casual. Active attention wins the page hierarchy.

Active attention must remain visible and must be handled through the appropriate workflow:

- send/update/log contact where the customer still needs a response;
- acknowledge/mark handled only where that action is valid;
- review feedback for `UnresolvedFeedback`;
- otherwise follow server-authored attention guidance when available.

When active attention exists:

- do not show the normal primary **Mark work done** card;
- keep Needs Attention guidance and the recommended attention-resolution action above completion;
- if work completion remains exposed, demote it and use explicit warning copy such as:

```text
Mark work done, attention remains
```

with helper text:

```text
This records that the work was performed, but this request still needs attention before it can be
closed.
```

### Owner/Admin closeout action

Closeout remains a separate Owner/Admin action:

```text
Close request
```

Render it only when:

```text
availableActions.canClose == true
availableActions.allowedStatuses includes "closed"
```

Equivalently for V1 policy, closeout requires `resolved` / **Work completed** with no active
attention. A work-completed request with active attention must not show **Close request** and must not
appear in `Ready to Close`.

The action uses the existing status endpoint with `status: "closed"`. If opened from the
`ready_to_close` queue, the client may pass `navView=ready_to_close` to preserve existing navigation.

### Customer boundary

Customers do not directly close, reopen, or mark work completed in V1.

Customer feedback remains available after `Closed`, one time per request, while the page is unexpired.
Negative feedback creates Owner/Admin review work and does not reopen the request automatically.

---

## Rationale

This keeps the domain lifecycle stable while making the human workflow legible:

```text
Operator/staff: Mark work done
Owner/Admin: Close request
Customer: Leave post-close feedback
```

Separating work completion from closeout preserves accountability. Operators can say the field work
is performed; Owner/Admin decides when the business is ready to close the record and expose the
feedback loop.

Preserving attention across work completion protects the promise loop. A job can be physically done
while the customer still needs a status reply, timing/cancellation clarification, or another
attention resolution. Closeout is therefore blocked until the attention path is handled.

Making attention primary in the UI avoids the opposite failure mode: an operator sees a large
completion button, clicks it, and assumes the customer/business issue is also handled. Completion and
attention resolution are related but not the same action.

The UI label avoids service-business ambiguity without forcing a backend rename. `resolved` remains a
safe API term, and **Work completed** becomes the staff-facing phrase.

---

## Rejected Alternatives

### Add a new `WorkCompleted` backend status

Rejected. It duplicates `Resolved`, adds schema/API/reporting churn, and creates ambiguity between
two states that mean the same V1 lifecycle point.

### Rename backend `Resolved` to `WorkCompleted`

Rejected for V1. The existing status is already wired through domain rules, API contracts, tests,
history, customer-page behavior, list filtering, and closeout eligibility. A UI label is enough.

### Let Operators close requests

Rejected. Closeout is management/accountability work and opens post-close feedback. Operators may
complete work; Owner/Admin closes.

### Let customers close requests

Rejected for V1. Customer-confirmed close, reopen-from-feedback, auto-close, and linked rework are
future lifecycle decisions that affect reporting, permissions, notifications, and customer-page
semantics.

---

## Consequences

- PWA/native clients should prefer **Work completed** wherever staff see `resolved`.
- Existing status filters may keep query value `resolved` while showing label **Work completed**.
- Request detail should expose explicit work-completion and closeout actions instead of relying only
  on generic status dropdown discovery.
- Work-completed requests with active attention remain attention work; they are not ready-to-close.
- The primary **Mark work done** card is a no-active-attention affordance. Active-attention states
  require attention-first UI and any completion affordance must be demoted/explicit.
- Backend action metadata remains authoritative; clients must not infer eligibility locally.
- Existing closed-feedback and feedback-review behavior remains unchanged.
