# Keep Review Rubric V2

**Status:** Active V2 review gate for UI-001 through UI-013.

## Production question

Does this surface let its intended user understand the current promise, take the next safe action,
and recover from a problem without the page reading as a prototype or generic SaaS UI?

## Required review dimensions

Every V2 surface must be reviewed with realistic populated data for:

- purpose and first-viewport hierarchy;
- action truth, customer/internal disclosure, and server-authorized availability;
- queue/detail scanability and selected-state clarity where applicable;
- visual-token, typography, semantic-color, and contrast compliance;
- keyboard/focus, zoom, screen-reader feedback, and touch targets;
- wide, narrow, and mobile containment;
- loading, empty, filtered-empty, error, permission, mutation, and conflict recovery;
- direct route, refresh, and browser Back/Forward behavior for request work;
- first-time customer trust for public intake and customer request pages.

## Locked operator checks

- At 100%, 125%, and 150% browser zoom, the queue/workbench either meets its usable-width contract or
  switches cleanly to focused drill-down; it never becomes a cramped pseudo-desktop.
- `#/requests` is a side-effect-free Priority Preview when unselected; explicit selection alone changes
  the durable request route.
- Queue tab labels, role defaults, More Views, active-session restoration, filtered-empty recovery, and
  non-disruptive refresh behavior conform to UI-004.
- The selected request’s phone, service location, responsible owner, identity, status/attention reason,
  and authorized current action are reachable without scrolling past the original customer description.
- Work Canvas preserves original customer wording, omits empty work-context placeholders, and discloses
  customer-visible versus internal writing.
- There is one enabled local-task primary at rest; amber never presents as a filled action; Close is red
  and confirmed.
- Field capture is price-blind, permitted field actions are 48px+, external contact resumption does not
  auto-log, and text entry does not collide with a general mobile action bar.
- Drawers, dialogs, and focused workspaces respect one-overlay, focus, back/cancel, draft, and error
  rules.
- Loading preserves the relevant layout; empty and filtered-empty states remain distinct; failures are
  localized; and background refresh neither reorders active scanning nor overwrites active input.
- A `409` preserves local input, blocks stale submit, refreshes authority, and requires explicit
  re-review before resubmission. In-memory input is labeled Unsaved changes, never Draft.
- Public intake is business-first, non-enumerating when unavailable, uses only established fields and
  claims, and provides the defined customer-page recovery path.
- Customer Request pages expose only authorized public data, never service location, staff/office or
  financial data; closed/Cancelled/expired states follow their defined input and feedback boundaries.
- Boundary-valid populated data, visible focus, keyboard-only traversal, screen-reader announcements,
  contrast, iOS-safe editable text size, 44px general targets, and 48px persistent field targets are
  reviewed with evidence.
- Wide and narrow shells retain the one route and one request engine; selection, resize, and navigation
  never leak state between Requests and invoke discard protection when unsaved input would be displaced.

## V2 gate construction

The existing `../keep-review-rubric.md` remains the active gate for its covered customer/list/detail
surfaces. Do not transplant old class-count checks into a new operator shell without verifying that they
still test the intended user outcome.
