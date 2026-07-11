# Build Log 081 — Session 24: Request Detail 2-Column Workbench

**Started:** 2026-07-11
**Status:** Draft — planning / Claude handoff
**Session name:** S24 request-detail 2-column workbench
**Related ADRs:** ADR-377, ADR-380, ADR-382, ADR-383, ADR-433, ADR-434
**Next free ADR before this log:** ADR-435

---

## Purpose

This build doc converts the authenticated PWA request detail page from a desktop layout that feels
like three columns into a dedicated two-column workbench.

The current desktop composition effectively spends horizontal space on:

```text
global left nav | request main content | request action/context rail
```

That makes the active request feel compressed, especially now that detail contains richer lifecycle,
attention, contact, service-location, priority, team, and closeout controls.

The target state is:

```text
top app nav
request header
70% main work column | 30% context panel
```

This is a PWA layout/workflow change. It should not change backend contracts, permissions,
concurrency rules, request statuses, or mobile/native app behavior.

---

## Product Decisions To Lock Before Coding

### Decision 1 — Request detail uses a dedicated desktop shell

On desktop request-detail routes, hide or replace the persistent left sidebar with a horizontal top
navigation bar.

Rationale:

- Keeping the left sidebar visible means the screen still feels like three columns even if the detail
  component itself uses a 70/30 grid.
- The request detail page is the operator workbench. It needs width for the active job.
- The app only has a small number of top-level destinations, so the sidebar is low-value on detail.

Expected desktop top nav contents:

- OpHalo Keep logo/brand.
- Requests navigation.
- Getting Started navigation when visible for the current role.
- Settings navigation when visible for the current role.
- New Request action when allowed.
- Quiet role/account identity if currently shown in the sidebar.

Mobile remains unchanged at the shell level unless a separate mobile-nav cleanup is explicitly
approved.

### Decision 2 — Detail body uses a 70/30 split on desktop

Use a responsive desktop grid:

```text
minmax(0, 7fr) minmax(320px, 3fr)
```

or an equivalent 70/30 implementation that protects the side panel from becoming too narrow.

The right column may need an upper bound on very wide screens if the page becomes visually stretched,
but the first implementation should favor reclaiming detail workspace width rather than creating a
small fixed-width rail.

### Decision 3 — Main column owns the work loop

The 70% main column owns:

- request identity/status header content when not in the global header;
- customer description/original request;
- active attention guidance when present;
- send customer update composer;
- activity feed and filters;
- primary lifecycle action only when it is part of the immediate work loop.

The send-update composer should sit directly above activity so staff can read recent communication and
write the next customer update without jumping across columns.

### Decision 4 — Side column owns context and secondary controls

The 30% context panel owns:

- customer contact details;
- service location;
- customer intake signals and contact preference;
- editable internal business priority;
- responsible user;
- watchers;
- follow-up/planned timing;
- utilities and secondary actions.

The side panel is not a dumping ground. If an action is the recommended next step for active
attention, it should appear in the main work path or be promoted visually from the side panel.

### Decision 5 — Do not merge customer urgency and internal priority

Keep ADR-433 semantics:

- Customer urgency/contact preference is a read-only customer signal from intake.
- Business priority is an internal editable triage decision.

The UI should clarify their relationship instead of collapsing them into one field.

Recommended labels:

```text
Customer signal
Internal priority
```

### Decision 6 — Lifecycle actions are contextual

Preserve ADR-434:

- Show **Mark work done** only when server action metadata allows transition to `resolved` and the UI
  state is eligible for the normal no-active-attention path.
- Show **Close request** only when server action metadata allows transition to `closed`.
- Active attention wins the hierarchy. Do not let a lifecycle button imply that attention is handled.

The request header may host the current primary lifecycle action, but only when that action is truly
the next valid step.

### Decision 7 — Mobile remains single-column

This project does not introduce a mobile 70/30 equivalent.

Mobile should keep a stacked detail page, with the order adjusted only where needed to match the new
work model:

1. request header;
2. attention guidance when present;
3. customer description/original request;
4. primary next action / send update;
5. activity feed;
6. customer contact and service location;
7. priority, team, timing, and utilities.

Do not regress existing mobile affordances. If a component is moved for desktop, confirm mobile order
explicitly.

---

## Target Desktop Layout

```text
+--------------------------------------------------------------------------------+
| OpHalo Keep        Requests      Getting Started      Settings      New Request |
+--------------------------------------------------------------------------------+
| < Requests   Kelley S   CK8DDC3WD   Work completed   Viewed 18h ago   [Close] |
+--------------------------------------------------------------------------------+
|                                                                                |
|  MAIN WORK COLUMN - 70%                    | CONTEXT PANEL - 30%               |
|                                            |                                    |
|  Customer description                      | Customer                          |
|  "Our sink in the kitchen..."              | Phone, email, preference          |
|                                            |                                    |
|  Needs attention guidance, if present      | Service location                  |
|                                            | Address, add/edit                 |
|  Send update to customer                   |                                    |
|  [ composer ]                              | Triage                            |
|  [ Send update ]                           | Customer signal                   |
|                                            | Internal priority dropdown        |
|  Activity                                  |                                    |
|  [ Communication | All activity ]          | Team                              |
|  timeline events                           | Responsible, watchers             |
|                                            |                                    |
|                                            | Timing / Utilities                |
|                                            | Follow-up, planned date, etc.     |
+--------------------------------------------------------------------------------+
```

---

## Current Implementation Notes For Claude

Primary files expected to change:

- `web/ophalo-app/src/App.tsx`
- `web/ophalo-app/src/pages/RequestDetail.tsx`
- potentially `web/ophalo-app/src/styles/app.css` if shared layout classes are preferred

Current relevant behavior:

- `App.tsx` renders a persistent desktop sidebar around all app routes.
- `RequestDetail.tsx` renders:
  - a breadcrumb/queue-navigation bar;
  - main detail content;
  - a desktop-only fixed-width right action rail;
  - mobile-only stacked actions and team context.
- `OriginalRequestCard` currently mixes customer description, contact, intake signals, priority, and
  service location in one card.
- `BusinessUpdateSection` currently lives in the action rail on desktop and before activity on mobile.

Claude should inspect the current file before coding because recent sessions may have changed the
exact component boundaries.

---

## Codeable Sessions For Claude

### S24a — Lock shell behavior and top navigation

Goal: Make request detail use full desktop width by replacing the persistent left sidebar with a top
navigation shell on detail routes.

Scope:

- Update `App.tsx` shell behavior for `route.page === "detail"`.
- Preserve the existing sidebar for list, settings, and getting-started routes unless a top-nav reuse
  is simpler and visually safe.
- Add a desktop top nav for detail routes with equivalent navigation destinations and role gating.
- Keep `New Request` available where it is currently available.
- Preserve mobile behavior.

Acceptance criteria:

- Desktop detail no longer shows the left sidebar.
- Desktop non-detail pages still have working navigation.
- Requests, Getting Started, Settings, and New Request routes/actions still work according to role.
- No backend/API behavior changes.
- TypeScript passes.

Suggested verification:

```text
cd web/ophalo-app
pnpm typecheck
```

### S24b — Convert request detail body to 70/30 desktop grid

Goal: Replace the fixed-width desktop action rail with a true responsive 70/30 layout.

Scope:

- In `RequestDetail.tsx`, convert the detail body wrapper from flex/fixed rail to desktop grid.
- Use a 70/30 ratio with a protected side-panel minimum width.
- Keep mobile stacked rendering.
- Preserve independent scrolling if the current UX depends on it, but avoid scroll traps.
- Ensure timeline and cards do not overflow the main column.

Acceptance criteria:

- Desktop body visually reads as two columns.
- Main column gets roughly 70% of the request-detail width.
- Context/action panel gets roughly 30% and remains usable at tablet/desktop breakpoints.
- Mobile remains one column.
- TypeScript passes.

### S24c — Split original request/context ownership

Goal: Move mixed metadata out of `OriginalRequestCard` so the main column contains the customer story
and the side panel contains context.

Scope:

- Keep customer description/original request in the main column.
- Move contact details, service location, customer signal, internal priority, and source/submitted
  metadata into side-panel sections.
- Preserve service-location add/edit behavior and optimistic priority behavior.
- Preserve contact launch tracking behavior.
- Preserve ADR-433 distinction between customer urgency and business priority.

Acceptance criteria:

- Customer description is no longer visually competing with contact/location metadata.
- Side panel clearly separates:
  - Customer;
  - Service location;
  - Triage / Customer signal / Internal priority.
- Add/edit location still updates detail state.
- Priority dropdown still updates detail state.
- Email/contact launch still opens the contact-log flow where applicable.
- TypeScript passes.

### S24d — Move send-update composer into the main work loop

Goal: Put customer update composition directly above activity on desktop.

Scope:

- Render `BusinessUpdateSection` in the main column above the Activity card on desktop.
- Keep it before Activity on mobile.
- Avoid duplicate composers at a single breakpoint.
- Preserve draft state while navigating within the same detail render.
- Preserve active-attention highlighting semantics from existing `highlights.sendUpdate`.

Acceptance criteria:

- Staff can read/write customer communication in one vertical flow.
- Active attention still promotes send-update when appropriate.
- The right panel no longer uses the update composer as a generic rail item.
- TypeScript passes.

### S24e — Rebalance actions, lifecycle, and utilities

Goal: Make the right panel context-first while keeping valid actions discoverable.

Scope:

- Keep ADR-434 lifecycle behavior:
  - `Mark work done` for eligible no-attention completion path.
  - `Close request` only when `canClose` allows it.
  - active attention remains primary.
- Decide whether `WorkDoneCard` / `CloseRequestCard` belongs in the request header, main column, or
  top of side panel based on current state.
- Keep `LogContactCard`, `MarkHandledCard`, `FeedbackReviewSection`, and team controls available
  without making the panel feel like a random stack of cards.
- Prefer grouped side-panel sections with compact headings.

Acceptance criteria:

- The next valid primary action is obvious.
- Attention-resolution actions remain visually ahead of work completion when attention exists.
- Closeout does not appear for ineligible requests.
- Utilities are grouped and scannable.
- TypeScript passes.

### S24f — Responsive QA and polish

Goal: Verify the new layout at realistic widths and clean up visual regressions.

Scope:

- Test desktop, narrow desktop/tablet, and mobile widths.
- Check long customer names, long emails, long addresses, and long descriptions.
- Check requests with:
  - active attention;
  - no attention;
  - work completed / ready to close;
  - missing service location;
  - customer urgency and business priority both present;
  - no customer email or no phone.
- Confirm no text overlap or clipped action controls.

Acceptance criteria:

- No incoherent overlapping UI.
- Main column and side panel keep stable widths.
- Mobile order is coherent and single-column.
- TypeScript passes.
- If a local browser/dev server is available, capture manual screenshots or note inspected viewport
  sizes in this build log.

---

## Suggested Implementation Order

Claude should complete these sessions in order:

1. S24a shell/top-nav behavior.
2. S24b 70/30 grid.
3. S24c context split.
4. S24d composer/activity proximity.
5. S24e action rebalance.
6. S24f QA/polish.

Do not combine S24a through S24e into one large patch unless explicitly directed. Each session should
end with a working, typechecked app state.

---

## Non-Goals

- Do not change backend request status names or enums.
- Do not change API contracts.
- Do not merge customer urgency and internal business priority.
- Do not redesign the request list.
- Do not redesign native mobile request detail.
- Do not add new permission logic in the client.
- Do not remove existing server-driven action gating.
- Do not create a marketing/landing-page style hero.

---

## Open Questions

These should be resolved before or during S24a/S24b:

1. Should the top nav be detail-only for now, or should it become the global desktop shell across the
   PWA?
   - Recommendation: detail-only first, because it minimizes blast radius.

2. Should primary lifecycle actions live in the request header or as the first item in the main work
   column?
   - Recommendation: header when the action is a clean state transition such as **Close request**;
     main column/attention area when the action needs explanatory context.

3. Should `Log contact` remain a side-panel utility or be promoted near the update composer?
   - Recommendation: promote only when attention guidance says contact logging is the primary
     resolution path; otherwise keep it in utilities.

4. Should side-panel sections be sticky?
   - Recommendation: not in the first pass. Stabilize the layout first, then consider sticky context
     after viewport testing.

---

## Claude Handoff Prompt

Use this prompt when starting implementation:

```text
You are implementing Build Log 081, Session 24, for the OpHalo Keep PWA.

Read docs/build-log/081-session-24-request-detail-2-column-workbench.md first.
Then implement only the named sub-session I give you.

Respect these locked decisions:
- Desktop request detail should become a true 2-column workbench.
- Detail desktop should not keep the persistent left sidebar if that leaves a three-column feel.
- Main column owns customer story, attention guidance, update composer, and activity.
- Side panel owns contact, location, customer signal, internal priority, team, timing, and utilities.
- Do not merge customer urgency and business priority.
- Preserve ADR-434 work-completed/closeout behavior.
- Mobile remains single-column.

Do not make backend/API changes unless the sub-session explicitly asks for them.
Run web/ophalo-app typecheck before finishing when dependencies are available.
```

