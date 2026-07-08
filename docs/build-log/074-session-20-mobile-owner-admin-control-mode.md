# Build Log 074 — Session 20: Mobile Owner/Admin Control Mode

**Started:** 2026-07-07
**Status:** Draft — product decision locked; implementation sequencing pending
**Session name:** S20 mobile Owner/Admin control mode
**Next free ADR before this log:** ADR-418
**Next free ADR after this log:** ADR-425

---

## Purpose

Session 20 records the product decision to add an Owner/Admin-oriented control mode to the native
OpHalo Keep mobile app.

The decision comes from the Keep command-center migration work in Session 19 and the concern that
Owners/Admins should not need a laptop to protect the customer promise while they are on the go.
The PWA remains the full-screen command center, but the native app should expose the urgent
whole-business control jobs that are natural on a phone.

This is not a reversal of the mobile product split. Native mobile stays phone-first and
interruption-tolerant. The new mode is a narrow Owner/Admin expression of the same Keep promise loop:

```text
triage -> assign -> contact -> update -> closeout -> feedback review
```

The goal is:

```text
Let an Owner/Admin answer "who needs us right now?" and take the next step from a phone.
```

---

## Product Context

Grounding sources:

- `docs/keep-product-positioning.md`
  - Keep promise: "No customer slips through the cracks."
  - Boundary: "Keep does not manage the work. Keep manages the promise."
  - Keep is not scheduling, not CRM, and not a field-service operating system.
- `docs/build-log/070-session-16-native-mobile-foundation.md`
  - Owner/Admin/Operator may sign in to native; Viewer remains deferred.
- `docs/build-log/071-session-17-review-safe-native-product-foundation.md`
  - Native V1 is field-first.
  - Native actions are explicit, server-driven, and customer-safe.
  - Native contact launch does not prove contact and does not mutate state without explicit logging.
- `docs/build-log/073-session-19-keep-workbench-ux-migration.md`
  - PWA is the Owner/Admin command center.
  - Request list is an action queue, not a CRM table.
  - Keep Web and Keep Mobile share one Keep identity at different densities.
- ADR-291
  - V1 contact posture uses native launchers plus explicit logs.
  - Keep does not send platform SMS or ingest SMS replies in V1.

---

## Locked Decisions

### ADR-418 — Native gets Owner/Admin Control mode; PWA remains the full command center

Add a native Owner/Admin control mode for on-the-go business-wide Keep work.

The PWA remains the full-screen command center for broad review, settings, team management, intake
link controls, and deeper operational maintenance. Native does not try to duplicate the whole PWA.
It carries the urgent command-center jobs an Owner/Admin needs away from a desk.

Reason: Owners/Admins are often in the field, between calls, or away from a laptop. Keep's promise
is broken if the business can only see or act on at-risk requests from a PC.

### ADR-419 — Mobile Control is a promise loop, not a mobile CRM/admin console

Mobile Owner/Admin Control includes only the work needed to keep customer requests from getting
lost:

- Needs Attention;
- Unassigned / Available;
- Assigned to Me or My Promises;
- Ready to Close;
- Feedback Review;
- Quick Capture / New Request;
- request detail actions needed for contact, updates, internal notes, status, responsibility,
  watching/muting, tracker sharing, closeout, and feedback review where server policy allows.

Mobile Control does not include V1 team management, account settings, intake-link configuration,
business profile editing, broad historical/reporting views, billing, CRM pipelines, calendar
ownership, estimates, invoices, or dispatch-board behavior.

Reason: Keep protects the communication promise around work. Native Control should answer "who needs
us right now?" rather than become the whole office in a phone shell.

### ADR-420 — Owner/Admin mobile navigation gains Control; Operator navigation remains field-first

Role-aware native navigation should expose Control only to Owner/Admin roles.

Preferred V1 shape:

- Owner/Admin: `My Work`, `Control`, `Capture`, `Account`
- Operator: `My Work`, `Available`, `Capture`, `Account`

The exact tab label may be refined in UX implementation, but it should be plain and short. Avoid
large desktop language such as "Command Center" as a phone tab label unless testing shows it fits.
`Control` is the current preferred label.

Reason: Operators need a narrow field-work flow. Owners/Admins need a compact whole-business control
surface without losing quick access to their own work.

### ADR-421 — Native contact/share is the preferred phone affordance; Keep still does not send backend SMS/email

Owner/Admin Control should lean into native phone capabilities:

- launch phone calls;
- launch SMS/Messages;
- launch email;
- use the native share sheet for customer tracker links;
- on return, ask for explicit confirmation/logging where state/audit should change.

Opening an OS composer, call sheet, or share sheet does not prove customer contact and does not by
itself clear attention, count first response, mark shared, or mutate the request. Durable state
changes still require explicit user confirmation through existing or approved Keep mutation
contracts.

Keep V1 still does not send backend customer SMS/email, ingest SMS replies, or own the business's
communication channel. Native makes the existing business habit easier and safer to record.

Reason: this resolves the practical PWA limitation around customer texting without violating the V1
contact boundary or introducing A2P/SMS compliance scope.

### ADR-422 — Mobile Control uses existing server policy and endpoints first

The first implementation should reuse existing Keep endpoints, DTOs, server-derived action metadata,
and row/detail authorization wherever possible.

Native must not infer broad Owner/Admin authority locally, fetch cross-account data, or create a
client-only command-center model. If a backend gap blocks a necessary Control action, record the gap
as a narrow contract slice with focused tests rather than widening the client.

Reason: current Keep authorization separates account visibility, row visibility, and action
permission. Mobile Control must remain a client density change, not a security shortcut.

### ADR-423 — PWA remains required and responsive

Adding native Owner/Admin Control does not lower the bar for the PWA command center.

The PWA must still be responsive at narrow widths for Owners/Admins who open it from a mobile
browser, especially before native Control ships. It also remains the owner/admin surface for the
full-screen workflows that do not belong in native V1.

Reason: some users will still open `app.ophalo.com` from a phone. Native and PWA should reinforce
each other rather than one becoming an excuse for the other to be desktop-only.

### ADR-424 — Settings and administrative maintenance stay PWA-owned in V1

Native V1 Account remains a utility/review surface plus role/account context. Owner/Admin Control
does not pull in:

- team/member management;
- role changes and invites;
- account/business profile editing;
- response policy configuration;
- intake-link setup/replacement;
- billing/commercial state management;
- account deletion flow beyond the existing review-safe link posture.

Reason: these tasks are lower-frequency, more error-sensitive, and better suited to the PWA until
pilot usage proves a specific mobile need.

---

## Proposed Implementation Slices

### S20a — UX/Data Preflight

Inventory the native app's existing screens, hooks, query keys, and API client coverage for:

- request list views (`default`, `assigned_to_me`, `needs_attention`, `watching`,
  `ready_to_close`, `feedback_review`);
- available/unassigned work;
- request detail action metadata;
- closeout and feedback review actions;
- native contact/share flows and explicit logging.

Done when the implementation session can identify which Control rows/actions already work from
existing contracts and which narrow gaps, if any, need backend work.

### S20b — Role-Aware Navigation

Add Owner/Admin role-aware navigation:

- Owner/Admin sees `Control`;
- Operator sees `Available`;
- Viewer remains restricted per ADR-402;
- route guards remain explicit for deep-linked request detail.

Done when Owner/Admin and Operator accounts see the correct tabs after `/auth/me` bootstrap without
duplicating auth logic.

### S20c — Control List

Build the Control screen as a compact action queue:

- summary chips for waiting/attention/unassigned/closeout where existing counts support them;
- segmented filters or compact tabs for the Control views;
- rows optimized for phone scanning: customer, attention, owner/unassigned, last touch, status, and
  next action;
- no CRM pipeline, calendar, or report language.

Done when Owners/Admins can identify at-risk requests and open detail from a phone without horizontal
scroll or desktop-only affordances.

### S20d — Control Detail Actions

Expose the approved Owner/Admin detail actions already supported by server metadata:

- contact launch and explicit contact log;
- customer update;
- internal note;
- status/closeout where allowed;
- responsibility/watch/mute where allowed;
- tracker share and mark-shared/share-intent confirmation;
- feedback review where allowed.

Done when actions render from `availableActions`, carry `X-Keep-Request-Version` where required, and
conflict/network handling preserves user drafts.

### S20e — Native Contact/Share Hardening

Tune phone-native behavior:

- SMS launch uses OS surfaces only;
- tracker link uses native share sheet where available;
- return-to-app confirmation is explicit and non-blocking;
- no mutation occurs from launch alone;
- copy/text explains that Keep records the contact only when the user confirms/logs it.

Done when native improves the customer-contact workflow without adding backend SMS/email.

---

## Acceptance Gates

- Owner/Admin can use native to see urgent whole-business work without a laptop.
- Operator field flow remains simple and is not polluted with owner/admin controls.
- PWA command center remains responsive and production-grade.
- All Control actions are server-authoritative and role/action metadata driven.
- No backend SMS/email, CRM pipeline, scheduling calendar, or administrative settings scope is added.
- Native contact/share behavior uses OS affordances and explicit logging/confirmation only.
- Mobile UI passes practical phone QA at 320, 375, 390, 430, 768, and representative iOS/Android
  device sizes.

---

## Open Implementation Questions

1. Should Owner/Admin `Control` include `Available` as a segment inside Control, or keep Available as
   a separate tab for Owners/Admins who also work in the field?
2. Which exact server count fields can support Control summary chips without adding backend scope?
3. Does mobile already expose every needed closeout/feedback-review mutation, or are narrow API
   client additions required?
4. Should `Control` deep links ever be push targets in S18/S19, or should push continue to land on
   request detail/My Work first?

