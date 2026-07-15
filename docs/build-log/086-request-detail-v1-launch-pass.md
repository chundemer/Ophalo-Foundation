# Build Log 086 — Request Detail V1 Launch Pass

**Prepared:** 2026-07-15  
**Status:** Decisions locked — implementation pending  
**Scope:** Authenticated PWA Request Detail only

---

## Purpose

The initial PWA build is complete. This launch-pass slice improves the authenticated Request Detail
workspace for busy business owners without changing the request lifecycle, permissions, customer
visibility rules, or backend data model.

The page must make the customer update loop obvious and fast, keep private work clearly private,
and remove unnecessary scrolling and form clutter. It must remain usable with real-world long data,
slow loading, keyboard operation, and small screens.

## Locked Product Decisions

### Customer update is the default primary workflow

- The left-column composer opens on **Customer update** by default.
- Customer update remains the primary purpose of Request Detail: it is customer-visible and uses
  the Keep teal action treatment.
- The composer shows a persistent, plain-language boundary: **Visible to customer**.
- Existing customer-update status-change behavior, validation, permissions, and optimistic
  concurrency behavior remain unchanged.

### Internal notes share the composer, but never the visibility boundary

- **Internal note** becomes the second tab in the same left-column composer.
- It is visually and textually distinct: **Internal only — never visible to customer**.
- Internal actions use neutral/navy styling, not the customer-update teal treatment.
- Customer-update and internal-note text are stored as separate in-memory drafts. Switching tabs
  never loses either draft.
- Do not combine these mutations into one generic save action. They have different recipients,
  validation, audit events, and safety requirements.

### Timing is summary-first and expands inline

- Follow-up and Planned timing render as compact status rows by default.
- An unset value displays a quiet action such as **Set follow-up** or **Set planned date**.
- A set value shows the date and applicable follow-up reason/note at a glance.
- Selecting a row expands its existing form inline; only one timing editor is open at a time.
- Do not use floating popovers for timing forms. They are unreliable with virtual keyboards and
  add avoidable accessibility and viewport complexity.
- Do not render the full empty follow-up and planned-date forms permanently in the sidebar.

### The sidebar serves actions first, then reference context

- The desktop sidebar has two clear groups:
  1. **Actions:** work completion, external contact, and timing;
  2. **Context:** customer, service location, triage, team/watchers, and source metadata.
- Reduce small all-caps section labels and visual card fragmentation. Grouping must improve scan
  speed without hiding available request information.
- The private note composer moves out of the sidebar.

### Work completion is safe and state-aware

- **Mark work done** remains available only where existing server action metadata allows it.
- It is not promoted as the primary recommended action while a request is **Received**.
- For Received requests, guide the operator to review the request and send a customer update or
  log contact as appropriate; do not make either action mandatory.
- For Scheduled or Active requests, work completion can be promoted when appropriate.
- For Resolved requests, permitted closeout remains the recommended lifecycle action.
- Clicking **Mark work done** enters a compact inline confirmation state: **Confirm work is
  completed?** with a Cancel affordance. No confirmation modal is required. The confirmation
  resets after a short timeout or Escape.
- This remains a client-side safety affordance only; lifecycle authorization stays server-driven.

### Readability and contrast are launch requirements

- Use a readable base scale across the workbench rather than a separate "large desktop mode":
  - body and form control text: at least 14px;
  - secondary/helper text: 13px with comfortable line-height;
  - section labels: 12px semibold, with restrained tracking;
  - 10–11px text only for genuinely nonessential metadata, never instructions or field labels.
- Preserve the entered spelling and capitalization of customer names. Do not auto-title-case
  customer data.
- Queue rows may truncate safely; the detail hero must wrap customer names cleanly rather than
  hide identity information.
- Audit actual foreground/background combinations against WCAG AA, including muted labels,
  teal/amber text on white, badges, borders, focus states, disabled controls, and error/success
  states. Do not approve contrast based on visual impression alone.
- Verify at 200% browser zoom and with long names, addresses, emails, and notes.

### Help is contextual, not intrusive

- The active composer shows a concise desktop keyboard hint: **Cmd/Ctrl + Enter** sends or saves
  the focused composer.
- Escape closes dialogs, drawers, and the inline work-completion confirmation.
- A small keyboard-shortcuts help surface may open with `?` only when focus is not inside an input,
  textarea, or select.
- Do not add global single-letter shortcuts such as `n` or `c`; they are too easy to trigger while
  typing.
- Primary operations use visible labels. Tooltips explain unfamiliar icon-only controls; they do
  not compensate for unclear primary UI.
- A short, dismissible first-use explanation belongs on the queue, not as repeated Request Detail
  coach marks.

### Mobile and loading states are first-class

- On mobile, the unified composer remains above Activity. It must not be pushed below context
  panels.
- Detail loading uses a layout-preserving two-column/stacked skeleton instead of a lone
  `Loading…` label.
- Detail load failure provides a clear Retry action in addition to safe explanatory copy.
- Keyboard focus order, visible focus rings, modal focus handling, and touch targets must work at
  desktop and mobile breakpoints.

## Recommended Request Detail Structure

```text
Main workspace                              Actions and context
──────────────────────────────────────      ─────────────────────────────
Customer / request hero                     Actions
Customer description                        - Work completion / closeout
[ Customer update | Internal note ]         - Log external contact
Composer                                    - Timing summary
Activity

                                             Context
                                             - Customer and location
                                             - Triage and team
                                             - Source metadata
```

## Hard Boundaries

- No request lifecycle redesign, new status, permission change, or client-side action-policy
  inference.
- No new database schema or API contract unless an implementation preflight identifies a genuine
  missing seam.
- No combining customer messages, internal notes, timing edits, and status changes into a single
  generic save transaction.
- No change to public tracker visibility: internal notes and internal timing remain private.
- No modal confirmation for routine work completion; use the locked inline confirmation.
- No speculative "large desktop" UI mode or automatic customer-name capitalization.

## Claude Implementation Sessions

Run these in order. Each session must read this build log, `docs/session-log.md`, and only the
files named by its preflight. Confirm the exact file gate and existing tests before editing. Keep
the normal hard slice gate: at most three mutation families, eight production files, and twelve
changed files total unless Christian explicitly approves a split exception.

### R86a — Unified communication composer

**Goal:** Move Internal Note from the sidebar into the left-column Customer Update composer without
losing the customer/internal safety boundary or either draft.

**Scope:**

- build an accessible two-tab Customer Update / Internal Note composer;
- Customer Update is the default tab and retains existing status-change behavior;
- preserve independent in-memory customer-update and internal-note drafts across tab switches;
- preserve existing mutation endpoints, version/conflict behavior, event visibility, and character
  limits;
- add persistent visibility copy, appropriate teal/navy treatments, Cmd/Ctrl+Enter submit only for
  the focused composer, and a programmatic label for the internal-note textarea;
- remove the duplicate desktop/mobile sidebar note composer only after the new composer is proven.

**Likely seams:** `RequestDetail.tsx`, `request-detail/BusinessSection.tsx`, and a focused new
request-detail composer component if preflight shows that extraction reduces risk.

**Exit evidence:** focused composer tests/checks cover default tab, safe visibility copy, independent
draft preservation, keyboard-submit guards, successful save/send, conflict/error retention, and
desktop/mobile placement.

### R86b — Summary-first timing and action/context sidebar

**Goal:** Remove permanently expanded empty timing forms and the long mixed-purpose sidebar stack.

**Scope:**

- render Follow-up and Planned timing as compact summaries by default;
- expand one existing timing editor inline at a time, preserving existing validation, clear, and
  resolution behavior;
- group desktop sidebar content into Actions and Context; move the note composer out of it;
- preserve all permitted customer, location, triage, team/watchers, utility, and source information;
- ensure mobile keeps the composer above Activity and context below it.

**Likely seams:** `request-detail/TimingPanel.tsx`, `RequestDetail.tsx`, and existing team/context
components only if required for grouping.

**Exit evidence:** timing default/expanded/clear/conflict flows, one-editor behavior, active timing
summary, desktop grouping, and mobile ordering are checked without API or lifecycle changes.

### R86c — State-aware work completion and action hierarchy

**Goal:** Make work completion safe and prevent Received requests from visually implying that work is
already complete.

**Scope:**

- keep server `availableActions` and status-transition policy authoritative;
- demote Work Done as the recommended action for Received; retain it only where existing policy
  allows it;
- promote it appropriately for Scheduled/Active work and preserve Resolved closeout behavior;
- add the locked inline two-step confirmation, Cancel, Escape reset, and timeout reset;
- retain existing conflict/error handling and avoid a full modal.

**Likely seams:** `request-detail/BusinessSection.tsx`, `RequestDetail.tsx`, and attention-guidance
helpers/components if preflight confirms a presentation-only adjustment.

**Exit evidence:** Received/Scheduled/Active/Resolved state checks, confirmation/cancel/Escape/timeout
behavior, one mutation only after confirmation, and existing authorization/concurrency behavior pass.

### R86d — Resilience, readable type, and accessibility polish

**Goal:** Make Request Detail stable and readable under real launch conditions.

**Scope:**

- replace the text-only detail loading state with a layout-preserving desktop/mobile skeleton;
- add a Retry action to load failure without weakening safe error handling;
- apply the locked Request Detail type scale and contrast/focus improvements;
- ensure long identity/contact/location values wrap or truncate in the right place;
- audit Escape behavior for Request Detail transient UI and preserve keyboard focus order/touch targets.

**Likely seams:** `RequestDetail.tsx`, `styles/app.css`, hero/context components, and modal/drawer
components only where preflight identifies a missing Request Detail Escape/focus seam.

**Exit evidence:** loading/error Retry behavior, contrast report, keyboard/focus review, long-data
checks, 320px/768px/1024px/1366px layouts, and 200% zoom evidence.

### R86e — Integrated verification and Build 086 closeout

**Goal:** Verify the assembled page without adding unrelated features.

**Scope:**

- run the focused PWA/backend tests affected by R86a–R86d plus `ophalo-app` TypeScript/build checks;
- complete manual desktop/mobile, long-data, keyboard, accessibility, and conflict-path passes;
- reconcile this build log and `docs/session-log.md` with exact evidence and explicit deferrals;
- do not begin Build 087 in this session.

**Exit evidence:** Build 086 is marked complete only after all locked acceptance criteria are either
verified or explicitly deferred with Christian’s approval.

## Implementation Preflight

Before implementation, confirm and preserve:

- server-supplied `availableActions` and permitted status transitions;
- version/concurrency handling for customer updates, notes, timing, and status changes;
- existing timeline events and customer/internal visibility boundaries;
- focus behavior and Escape handling of current modals/drawers;
- responsive behavior at mobile, tablet, 1366px desktop, and 200% zoom.

## Acceptance Criteria

- Customer Update is selected by default and always identifies its customer-visible audience.
- Internal Note is available beside it, clearly internal, and its draft survives tab switches.
- Both drafts survive tab switches independently; neither is sent or saved by switching tabs.
- Cmd/Ctrl+Enter only submits the focused composer; Escape safely closes eligible transient UI.
- Received requests do not visually promote work completion as the recommended first action.
- Marking work done requires the inline second confirmation and preserves existing server action
  authorization and conflict handling.
- Unset timing consumes only compact summary-row space; one timing editor expands inline when
  selected.
- Desktop sidebar groups actions and context without the previous long stack of permanently open
  forms and the internal-note textarea.
- Mobile places the composer before Activity and context.
- Detail loading presents a stable skeleton; load failure offers Retry.
- No normal instructional or form-label text is below 12px; normal foreground/background text and
  interactive states pass the agreed contrast audit.
- Request Detail remains operable by keyboard, with visible focus states and no lost composer
  drafts.

## Verification Plan

- Add focused PWA tests for composer tab default, draft preservation, visibility copy, keyboard
  submit guards, Received action hierarchy, inline completion confirmation, and timing disclosure.
- Add responsive/manual checks at 320px, 768px, 1024px, 1366px, and 200% zoom.
- Test long customer names, postal addresses, email addresses, descriptions, and max-length notes.
- Run automated accessibility checks plus manual keyboard and screen-reader-label review.
- Run `ophalo-app` TypeScript/build checks and existing relevant request-detail tests before
  declaring this slice complete.

## Deferred Until a Later Page Pass

- Queue-level, dismissible first-use workflow guidance.
- A global shortcut reference beyond the small Request Detail help surface.
- Broader PWA and marketing-site typography/token consolidation outside the Request Detail scope.
