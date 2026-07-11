# ADR-433 — Business Priority vs. Intake Urgency

**Status:** Locked  
**Session:** S22p10  
**Next free ADR after this:** ADR-434

---

## Context

`IntakeUrgency` (Routine / Soon / Urgent) was added in S22p2 as the customer's self-reported urgency
signal from public intake. It is a verbatim capture of what the customer stated; the system must not
use it as a verified attention condition without operator review.

Operators need a way to triage and override the customer signal with the business's own assessment —
for example, a customer may mark a new AC request "Urgent" because it is not cooling fast enough, but
the technician may determine it is routine after reviewing the job type and their schedule.

---

## Decision

Add a separate `BusinessPriority?` field (nullable enum: `Routine = 1`, `Soon = 2`, `Urgent = 3`)
to `KeepRequest`. This field is owned by the business operator and is independent of `IntakeUrgency`.

**Key invariants:**

- `IntakeUrgency` is read-only after intake and always preserves the customer's original statement.
- `BusinessPriority` is null by default (unset = no business override).
- Queue ranking uses `BusinessPriority` when set; falls back to `IntakeUrgency` when null.
  Expressed as: `effectivePriority = BusinessPriority ?? IntakeUrgency`.
- Setting `BusinessPriority = Routine` explicitly suppresses the customer-urgent ranking fallback
  even when `IntakeUrgency = Urgent`, because the business has reviewed and downgraded the signal.
- Changing `BusinessPriority` does not clear attention, change status, send a customer message,
  or mutate `IntakeUrgency`.
- A `BusinessPriorityChanged` internal timeline event is written on every change (including clear).
  Event content carries a human-readable "Priority changed from X to Y" description.
- `Keep.RequestsOperate` permission is required; no separate permission introduced in V1.
- OffSeason accounts cannot set business priority (write-suppressed, consistent with all
  other operational mutations).
- Both `IntakeUrgency` and `BusinessPriority` are exposed on the operator request list summary
  and on the operator request detail. Frontend may show both signals when they differ.

---

## Label convention

- Frontend uses **`Priority`** (not "Urgency") for the business-editable field to make clear it is
  the business's operational triage decision, not the customer's statement.
- Customer-reported field retains the label **`Customer marked urgent`** or similar.

---

## Rejected alternatives

- **Reusing `IntakeUrgency`:** Rejected. Overwriting the customer's original statement with the
  business's assessment destroys audit fidelity and makes it impossible to distinguish "customer
  said urgent, we agreed" from "customer said urgent, we downgraded."
- **Using `PriorityBand`:** Rejected. `PriorityBand` is a system-level attention signal (Priority /
  Standard) driven by message intent and response-time rules, not an editable triage field.
- **Single field with "override" semantics tracked via event:** Rejected. Separate fields make the
  contract explicit and allow both signals to be surfaced side-by-side without reverse-engineering
  history from events.
