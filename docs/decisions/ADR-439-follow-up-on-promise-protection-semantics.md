# ADR-439 — Follow Up On Promise Protection Semantics

**Status:** Locked  
**Date:** 2026-07-13  
**Related:** ADR-337, ADR-338, ADR-339, ADR-359, ADR-435, build-log/083

## Decision

`Follow Up On` is a lightweight, business-owned promise reminder attached to a customer request.

It exists to protect a customer promise that still needs a human check-in, decision, or
communication on a future date.

Tighter definition:

```text
Follow Up On is an internal staff-owned date that tells Keep:
"Do not let this request go quiet. Bring it back to the responsible staff member on this date
because the customer/business loop is not fully settled yet."
```

Follow Up On is request-specific promise protection. It is not mainly a calendar task, generic
personal reminder, dispatch schedule, booking, or proof that contact happened.

## Rules

- Owners/Admins may set Follow Up On where they have request access.
- Operators may set Follow Up On when they have operate access to the request.
- Viewers may not set Follow Up On.
- Customers may request timing or follow-up, but they do not directly create or edit internal Follow
  Up On.
- Internal Follow Up On is not shown on the customer page by default.
- Staff must explicitly communicate any customer-facing follow-up expectation through customer
  update/contact paths.
- Setting Follow Up On requires date and reason.
- Note is optional except `other`, which requires a note.
- Future Follow Up On remains quiet timing metadata and suppresses stale/status-check noise.
- Due/overdue Follow Up On is active operational attention unless a stronger active attention reason
  already owns the request.

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

## Rationale

Busy small-business owners and operators need follow-up creation to feel like "do not let me forget
this," not like case-management paperwork. Requiring date and reason keeps creation fast while still
giving Keep enough structure to resurface the promise and explain why it matters.

Customers can and should express expectations such as "call me Monday," but letting customers write
directly to Follow Up On would turn a request into an unconfirmed business promise. The staff-owned
field preserves the boundary: customer requests can inform the promise, but staff confirms the
business-owned follow-up plan.

## Consequences

- Follow Up On stays internal by default.
- Customer-facing "next update expected" language requires a separate explicit staff communication
  decision before it appears on public/customer pages.
- Due/overdue follow-up must be verified in backend attention/list ranking; if the current code does
  not promote due follow-up, build-log/083 must record and fix the gap.
