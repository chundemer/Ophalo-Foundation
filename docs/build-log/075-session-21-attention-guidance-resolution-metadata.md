# Build Log 075 — Session 21: Attention Guidance Resolution Metadata

**Started:** 2026-07-08
**Status:** Draft — frontend placeholder shipped; backend contract gap recorded
**Session name:** S21 attention guidance resolution metadata
**Next free ADR before this log:** ADR-426
**Next free ADR after this log:** ADR-427

---

## Purpose

The Keep request detail page now needs to explain active attention in plain operational language:

```text
Why is this request in Needs Attention?
Resolve by doing what?
```

This belongs in the center request-detail column, directly after the original request context and
before latest update/activity. The right rail remains for actions, not explanation.

The frontend can map `attentionReason`, `attentionLevel`, `waitingDirection`, events, and
`availableActions` into useful guidance for V1. That is good enough for the current PWA pass, but it
is not the final backend contract.

---

## Locked Product Decisions

### ADR-426 — Detail attention guidance needs server-authored resolution metadata

Request detail must eventually expose backend-authored guidance/effect metadata for attention
resolution.

The current detail DTO exposes:

- `attentionReason`, `attentionLevel`, `waitingDirection`, timing fields, feedback fields;
- raw request events;
- `availableActions` permission gates;
- `contactActions`.

That is enough for the frontend to render a conservative **Needs attention / Resolve by** card, but
not enough to guarantee that every displayed resolution action will clear attention, count first
response, change status, or complete feedback review.

The list endpoint already has `KeepQuickAction` metadata with:

- `clearsAttention`;
- `countsFirstResponse`;
- `changesStatus`;
- `effectSummaryCode`.

The detail endpoint should either reuse that concept or return a detail-specific
`attentionGuidance` / `recommendedActions` contract.

Required future backend outcome:

- Server authors the reason label, why text/code, and one or more resolution actions.
- Each action includes whether it clears attention, counts first response, changes status, or only
  records context.
- `UnresolvedFeedback` remains special: it should route to feedback review, not normal attention
  acknowledgement.
- `ScheduleChangeRequest` / `TimingChangeRequested` copy must stay Keep-safe: confirm timing and
  protect the promise, without turning Keep into scheduling software.
- `CallRequested` must continue to separate opening a phone/SMS/email surface from durable logging.
- Unknown/future attention reasons must fail safe to "review request" guidance.

Frontend interim rule:

- The PWA may display **Resolve by** using a local exhaustive mapping.
- Copy must stay advisory, not a backend guarantee.
- Do not expose backend implementation-gap language to staff users.

---

## Customer Page Language Decision

Staff-facing PWA copy should prefer **customer page** over **tracker link** where the user is deciding
what to share or where a customer will see updates.

Reason:

- New service businesses understand "customer page" faster than "tracker link."
- Customers are not adopting Keep terminology; they are opening a page about their request.
- The underlying URL/page-token/share-intent contract remains unchanged.
- Existing ADRs may still use "tracker link" as historical/backend terminology.

Acceptable implementation:

- UI labels: "Customer page", "Share page", "Copy customer page link", "Visible on the customer page."
- Event copy: "Customer page shared with customer."
- Code variable names may remain `pageToken` and URL internals may still use `/keep/r/{pageToken}`.

---

## Frontend Interim Shape

The request detail center column should render:

1. Hero identity/status card.
2. Original request card.
3. **Needs attention** card when `attentionLevel != none`.
4. Latest update card.
5. Activity.

The attention card should include:

- attention badge/reason;
- **Why**: plain-language reason;
- latest relevant customer/source message when available;
- **Resolve by**: the next safe operational action;
- after-handled cleanup copy only when allowed, e.g. acknowledge attention or mark feedback reviewed.

The card should avoid:

- "recommended next step" as the primary label;
- implying backend SMS/email is sent;
- implying Keep owns scheduling;
- making acknowledgement look like the customer-facing resolution.

---

## Backend Follow-Up Slice

Recommended next session:

1. Audit `KeepRequestDetailResult`, `KeepRequestSummary.KeepQuickAction`, and
   `GetKeepRequestListService.BuildQuickActions`.
2. Decide whether to add `QuickActions` to detail or add a new `AttentionGuidanceMetadata`.
3. Encode an exhaustive attention-reason mapping server-side.
4. Add tests for every `AttentionReason`.
5. Update PWA to consume server metadata and keep the frontend fallback only for old/unknown API
   responses.
