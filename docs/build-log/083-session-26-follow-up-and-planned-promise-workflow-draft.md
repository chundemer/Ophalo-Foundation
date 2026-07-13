# Build Log 083 — Session 26 Draft: Follow-up and Planned-for Promise Workflow

**Started:** Drafted 2026-07-12  
**Status:** Draft / next-session planning  
**Related ADRs:** ADR-337, ADR-338, ADR-339, ADR-359, ADR-406, ADR-435  
**Next free ADR:** ADR-439

---

## Why This Session Exists

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

## Existing Product Semantics

### Follow Up On

ADR-337 says Follow Up On is:

- staff-owned active-request communication follow-up;
- not scheduling;
- date-only in V1;
- internal by default;
- should resurface as follow-up attention on/after the date;
- should make explicit customer update/contact easy.

### Planned For

ADR-338 says Planned For is:

- lightweight staff-owned active-request timing context;
- not dispatch, scheduling, booking, or customer appointment management;
- date-only and internal by default;
- useful for scan labels such as `Planned today`, `Planned tomorrow`, `Planned Fri`, `date passed`;
- should make explicit customer updates easy but must not notify automatically.

### Important distinction

```text
Follow Up On = we need to check back / communicate / verify something.
Planned For = we intend to do or resume work around this date.
```

Both protect the customer promise, but they should not collapse into one generic date field.

---

## Session 25 Observations To Carry Forward

Implemented during Session 25:

- request list displays timing chips from server `timing` metadata;
- request detail promotes `Follow-up & planned timing`;
- planned date now has an obvious `Remove planned date` affordance;
- request detail shows a top banner when Follow Up On or Planned For is today:

```text
Protecting a promise today
```

Open concern:

The banner tells the business that today matters, but it still does not answer:

```text
What did you do, and should this promise be cleared or moved?
```

---

## Gap Statement

Keep needs a first-class "complete the follow-up" workflow.

The workflow must combine activity capture with timing cleanup:

- record contact, update, or internal note;
- clear Follow Up On when the promise is handled;
- optionally set the next Follow Up On date when the promise remains open;
- avoid false audit language such as sent/delivered/done unless the user explicitly records that
  action.

Planned For also needs a clearer completion/reset story, but it is not identical to Follow Up On.

---

## Proposed UX Direction

### Detail top banner

When Follow Up On or Planned For is today, the detail banner should include an action, not just
awareness.

Possible primary actions:

```text
Record follow-up
Review timing
Log what happened
```

Preferred direction:

```text
Record follow-up
```

Reason: it names the operator task and keeps the workflow anchored in the promise loop.

### Follow-up completion drawer/modal

`Record follow-up` should open a focused workflow.

Possible options:

- `Logged a call, text, email, or in-person contact`
- `Sent customer update`
- `Added internal note only`
- `Still waiting / move follow-up`

Possible completion outcomes:

- clear Follow Up On;
- clear attention if the logged activity qualifies under existing external-contact policy;
- set a new Follow Up On date;
- leave Follow Up On in place if not actually handled.

### Request list row

The list should not use a full banner inside rows. Use compact chips:

```text
Promise today · Follow-up: Waiting on customer
Promise today · Planned for today
```

If both are today:

```text
Promise today · Follow-up: Waiting on customer · Planned today
```

The list chip should sit in the signal/status line before `Next:`.

---

## Product Questions For Tomorrow

1. Should `Record follow-up` be a new backend endpoint, or a composed UI workflow using existing
   endpoints?

2. Should completing a Follow Up On always require an activity record?

   Suggested answer: yes. Clearing a follow-up without recording anything loses the audit trail.

3. Which activity types should be allowed in the follow-up completion flow?

   Candidate V1 set:

   - external contact;
   - customer update;
   - internal note;
   - reschedule follow-up.

4. Should Planned For have a separate `Mark planned work handled` flow?

   Suggested answer: not yet. V1 can make Planned For removable/changeable and today-visible, while
   Follow Up On gets the completion workflow first.

5. Should due Follow Up On raise actual attention, or remain timing metadata plus banner/list chip?

   ADR-337 says it should resurface as follow-up attention on/after the date. Confirm whether current
   backend behavior does this fully; if not, create a backend gap.

---

## Candidate Implementation Slices

### S26a — Documentation and decision lock

- Lock workflow language:
  - `Record follow-up`;
  - `Clear follow-up`;
  - `Move follow-up`;
  - `Planned for`.
- Decide whether completing follow-up requires an activity record.
- Decide whether Planned For gets a completion workflow or remains date context.

### S26b — Request list promise-today chip

- Promote today Follow Up On / Planned For into a distinct list chip.
- Keep future timing chips quieter.
- Add test/mocks for today labels.

### S26c — Detail banner action

- Add `Record follow-up` action to the `Protecting a promise today` banner.
- Route to a focused panel/modal.

### S26d — Follow-up completion workflow

- Compose existing activity actions or add a narrow backend command.
- Ensure follow-up clearing writes durable activity.
- Allow "still needs follow-up" with new date.

### S26e — Backend verification

- Confirm due Follow Up On appears in attention/list ranking as ADR-337 expects.
- Add tests if missing.

---

## Non-Goals

- No customer self-scheduling.
- No dispatch calendar.
- No automatic customer notifications from timing changes.
- No backend SMS.
- No proof-of-send semantics.
- No broad recurring task engine.

---

## Draft Recommendation

Start S26 with Follow Up On, not Planned For.

Follow Up On is the cleaner promise-completion workflow because it is explicitly about communication
follow-up. Planned For can remain timing context until the follow-up workflow is solid.

The first production-worthy shape should be:

```text
Promise today banner -> Record follow-up -> record activity -> clear or move follow-up
```

That is the missing loop.
