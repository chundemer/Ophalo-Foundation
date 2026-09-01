# Build Log 137 — Request Detail And Queue Usability Handoff

**Status:** Decisions locked — ready for bounded implementation sessions
**Date:** 2026-09-01
**Scope:** GAP-019, GAP-058, GAP-059, then GAP-027 queue-row presentation
**Authority:** [Pilot Readiness Bug Tracker](../pilot-readiness-bug-tracker.md),
[Request Detail Workbench specification](../ux-design/v2/request-detail-workbench-signoff-spec.md),
ADR-434, ADR-443, ADR-452, and current server-authorized Request Detail contracts.

## Product outcome

An Owner/Admin must be able to scan a request and immediately understand:

1. what needs attention;
2. the one recommended next action;
3. what each available action changes, and what it deliberately does not change.

The implementation must not make a submitted visit, internal financial review, request Work
completed state, customer notification, active attention, or an open Actual Work draft appear to be
the same fact.

## Locked interaction rules

- The server-authorized attention-resolution action is the only visually dominant action while
  attention is active.
- Call, Text, Email, Share Link, and explicit contact logging remain Customer Contact utilities;
  do not render a duplicate large `Contact customer` Anchor action when attention has another
  recommended action.
- The alternate authorized attention path reads **Resolve another way…** and opens the
  server-authorized guidance; it is never a casual `Clear attention` dismissal.
- `Mark work done` remains server-authored request-lifecycle work. With active attention it moves
  below attention and Actual Work/communication as a quiet contextual lifecycle action. It is not
  blocked or coupled to review.
- Its confirmation states: Work completed changes the request lifecycle; it does not notify the
  customer or complete internal financial review; active attention and/or an open Actual Work draft
  remain when applicable.
- Actual Work card language is factual: **Draft — not submitted**, **Submitted visit**, **Internal
  financial review pending**, and **Internal financial review completed**. Never collapse these
  labels into request lifecycle language.
- The internal review card heading is **Internal financial review**; its action is **Complete
  internal financial review**; persistent copy and success status say that request status is
  unchanged.
- The Actual Work Review queue exposes both the request lifecycle state and the submitted-visit
  review state. `Request: Received` plus `Submitted visit awaiting internal financial review` is a
  valid and expected combination.
- The Request Anchor and Work Canvas share one horizontal content boundary.
- The planning row remains in the Anchor. Its persistent labels are **Internal priority**,
  **Planned work date**, and **Internal follow-up**. Enabled empty actions use **Set planned date**
  and **Set follow-up date** in normal contrast, without ellipses. Read-only values visibly say
  **Read only** and carry no interactive chevron/hover affordance.
- Request rows use one quiet lifecycle cue, at most one server-ranked exception/attention cue, and
  one factual next-action line. Selection is independent from severity; reserve red for genuine
  overdue/high-risk work. Office Review remains distinct from customer-promise risk.

## Architecture boundary for GAP-019

Use one page-level Request Detail coordinator for authoritative detail state, cache replacement,
navigation, overlays, and cross-feature policy. Shared feature controllers may own bounded local
form state, API calls, retry snapshots, and conflict handling when they consume the current
authoritative version and return replacement detail to the page coordinator.

Desktop and mobile composition are layout-only. They must not have separate action-policy,
authorization, lifecycle, attention, or concurrency implementations. Preserve both responsive
measurements: viewport width selects the focused Actual Work workspace route; Request Detail
container width selects its own composition. They are deliberately not interchangeable.

## Claude session order

Run one session at a time. Before editing, read this build log, the tracker entries in scope, the
current source/tests named by the preflight, and `docs/session-log.md`. Report the exact file list,
existing behavior, and any conflict with this contract. Do not start the next session until the
prior session is committed and accepted.

### RD-019A — Shared Request Detail composition seams

**Goal:** Decompose Request Detail without intentionally changing behavior or visual hierarchy.

**Scope:** Extract thin wide/narrow composition wrappers and coherent shared canvas regions from
`RequestDetailContent`. Retain the page-level coordinator and existing bounded feature controllers.
Move no API semantics, permissions, lifecycle policy, attention ranking, customer-delivery claim,
or mutation behavior.

**Must preserve:** existing desktop/mobile order, one Work Canvas scroll surface, QR/direct-mobile
handoff distinction, cache replacement, conflict recovery, focus/scroll policy, and both width
measurements.

**Proof:** focused composition tests plus current Request Detail tests; typecheck, token check,
build, diff check, and narrow/wide manual comparison. No intentional screenshot difference.

### RD-058A — Actual Work Review queue facts

**Goal:** Make the review queue truthful about both its submitted visit and its linked request.

**Scope:** Add the factual request lifecycle status to the read-only Actual Work Review queue
projection, persistence/API response, generated frontend type, and queue row. Render explicit
request lifecycle plus `Submitted visit awaiting internal financial review`; retain account scope,
FIFO order, financial totals, count endpoint, and Owner/Admin authorization.

**Must not do:** change request status, review authorization, queue membership/ranking, count
semantics, or introduce a lifecycle gate.

**Proof:** application/unit and API/persistence coverage for `Received` plus review-pending,
terminal/lifecycle display mapping, and no count/list drift.

### RD-058B — Request Detail action hierarchy and review clarity

**Goal:** Ensure an Owner/Admin cannot confuse internal review, request completion, attention
resolution, contact intent, or customer notification.

**Scope:** Implement the locked review-card wording/action/success state; make attention resolution
the only visual primary during active attention; remove duplicate large Anchor contact action;
render the alternate authorized attention path as `Resolve another way…`; relocate active-attention
`Mark work done` to a quiet contextual lifecycle position; add factual confirmation advisory copy.
Align Anchor and Work Canvas content boundaries. Surface draft/submitted/review states with the
locked factual vocabulary.

**Must not do:** hard-block `Mark work done`, couple it to review, infer server action policy,
claim customer delivery, or render price/cost data in factual field capture.

**Proof:** focused desktop and narrow/mobile tests for active attention, no attention, `Received`
plus review pending, Owner/Admin and unauthorized variants, focus order, confirmation wording,
review success, and unchanged request lifecycle after review.

### RD-059A — Internal Planning controls

**Goal:** Make authorized timing controls visibly actionable and fully keyboard recoverable.

**Scope:** Apply the locked labels/action copy/read-only treatment. On open, focus the first editor
field; Escape closes and restores trigger focus; expose errors through the relevant editor and a
live/alert treatment. Preserve existing date/reason validation, mutation/version/conflict policy,
and one-open-editor behavior.

**Must not do:** replace the controls with a new scheduling system, change Follow Up On semantics,
allow unauthorized editing, or render a read-only value as an enabled action.

**Proof:** focused tests for empty/set/read-only/loading/error/conflict controls, Enter/Space,
focus transfer, Escape/restore, Tab sequence, selected-value readability, and desktop/narrow
layouts.

### Q-027A — Owner/Admin queue-row hierarchy and badges

**Goal:** Make the queue scannable without competing visual alerts.

**Scope:** Implement the locked row grammar using existing server ranking and lifecycle data:
one compact lifecycle badge, at most one server-ranked exception/attention badge, and one factual
next-action line. Ensure selection treatment is distinct from severity; red is reserved for
genuine overdue/high-risk work. Preserve quiet planned/future timing and the terminal-state
suppression rules. Keep Office Review badges/controls separate from Needs Attention.

**Must not do:** client-side re-rank, change queue counts, merge Office Review into Needs Attention,
add a lifecycle stage strip, or bundle filters/history/pagination changes.

**Proof:** visual and accessible-name/focus-order coverage across received, active,
waiting-on-customer, work-completed, closed, closed-with-unresolved-feedback, selected rows,
overdue rows, and Office Review. Verify queue count/visible urgency agreement against the existing
server contract.

## Session gates

- Maximum one bounded change set per session; split before exceeding three mutation families,
  eight production files, or twelve files total.
- Use no production or founder data for acceptance.
- Run the focused test suite first, then the relevant frontend/backend suite, typecheck, token
  check, build where applicable, and `git diff --check`.
- Capture desktop, narrow PWA, keyboard, and browser-zoom evidence before closing RD-058B,
  RD-059A, and Q-027A.
- Update the tracker, session log, and this build log with the commit and evidence after each
  accepted session; remove completed handoff detail from the session log rather than accumulating
  it there.
