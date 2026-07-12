# ADR-436 — Staff Operational Signal Clarity

**Status:** Locked  
**Session:** S24 request workbench signal clarity  
**Next free ADR after this:** ADR-437

---

## Context

Keep surfaces many operational signals to staff:

- customer urgency from intake;
- internal business priority;
- active attention;
- overdue response windows;
- ready-to-close state;
- unresolved feedback;
- unassigned work;
- missing operational context such as service location.

If these appear as standalone colored pills, staff can see that something is important without
knowing why it matters or what action is expected. That creates hesitation in the request-list cockpit
and can make customer-reported signals look like business-owned decisions.

ADR-433 already separates customer intake urgency from internal business priority. ADR-426 already
requires request detail attention guidance to explain resolution context. ADR-435 defines the request
list as an action cockpit. This ADR generalizes the rule across staff-facing Keep surfaces.

---

## Decision

Every staff-facing operational alert, attention state, urgency signal, or priority cue must answer
two questions:

```text
Why am I seeing this?
What should I do next?
```

The amount of explanation depends on the surface density, but an unexplained colored pill is not a
complete operational signal.

### Request List

The request list remains compact. It should use short human-readable signal copy plus an optional
next-action cue.

Preferred examples:

```text
Customer marked urgent · Next: Log contact or Update customer
Team marked urgent · Next: Assign owner
Customer replied · Next: Update customer
Response overdue · Next: Update customer
```

Rules:

- Use human source/reason copy, not only category labels such as `Customer signal: Urgent`.
- Preserve ADR-433: customer-reported urgency and internal business priority remain distinct.
- Derive `Next:` copy only from server-provided quick-action metadata.
- The verbs in `Next:` copy should match the visible row action button labels.
- Separate signal and next-action text with a quiet separator such as `·`.
- If the server does not provide a safe next action, omit the cue or use a conservative review cue
  such as `Open detail to review`.

### Request Detail

Request detail can use fuller guidance.

Preferred shape:

```text
Why
The customer selected urgent during intake.

Resolve by
Review the request, then contact or update the customer if needed.
```

Rules:

- Existing Needs Attention guidance should continue to show `Why` and `Resolve by`.
- Customer signal / internal priority / service-location / timing / closeout cues in the side panel
  should also provide enough reason and next-step context when they are highlighted as operational
  signals.
- Detail may use richer explanation than the list, but it must preserve the same source boundaries.

### Native Mobile

Native mobile may be shorter and action-first, but still must not show unexplained alerts.

Examples:

```text
Customer replied
Update or call

Customer marked urgent
Call or log contact
```

---

## Rationale

Service businesses need to act quickly without guessing. A request-list row should not make an owner
or admin wonder whether `Urgent` means a customer selected urgency, the business raised priority, a
response window is overdue, or a complaint/cancellation is present.

Explaining the source and next action turns signals into operational guidance while preserving server
authority. The client may format the copy, but it must not invent permissions, effects, or unsafe
actions.

---

## Consequences

- GAP-008's list-row urgency fix becomes one instance of a broader staff-signal clarity rule.
- Request detail must be audited for non-attention signals, especially customer signal/internal
  priority and any highlighted side-panel cues.
- Future alert badges, notification rows, mobile list chips, and detail cards should include reason
  and next-action context appropriate to their density.
- Backend-authored guidance remains preferred for attention/action semantics; client copy must fall
  back conservatively when metadata is incomplete.
