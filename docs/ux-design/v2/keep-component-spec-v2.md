# Keep Component Spec V2

**Status:** V2 component contract. Exact component recipes may be implemented only after their
governing decision is locked in the Decision Register.

## Scope

V2 will specify the reusable operator and customer components required by the production UI upgrade:

- Request Queue row, selected row, attention/quiet row variants, and queue states;
- Request Workbench header, context strip, customer-need region, and activity stream;
- customer-visible update and internal-note composition;
- action bars for focused field work;
- inline edit, drawer, dialog, confirmation, draft, error, and conflict recipes;
- customer intake and existing customer-request trust states.

## Current source and migration rule

Use `../keep-component-spec.md` for currently locked primitives, token references, and existing
customer-surface recipes. Do not duplicate a recipe merely to change class names.

## Locked operator recipes to build next

| Recipe | Governing rule | Required behavior |
|---|---|---|
| Request Queue | UI-001, UI-004 | Future 320–360 CSS-pixel bounded wide pane: three primary tabs by role, two-row grid at the bounded width; Views (Watching only) + History in their own utility row below Office Review; selected row; filtered-empty and quiet refresh treatment. The current full-width page uses the same role/view behavior but normal horizontal tabs and its compact Office Review variant. |
| Office Review control | UI-004 amendment | Owner/Admin-only; conditional on authoritative aggregate > 0; navy/neutral, never amber; structured loading placeholder (not a blank bar); collapsed reads "Office Review · N pending", names the active member once one is open; opens to actionable (non-zero) members first with zero-count members collapsed into one quiet line; plain disclosure/group, not an ARIA menu. Current full-width variant is intrinsic/content width directly below primary tabs; future Queue-pane variant is pane width. |
| Priority Preview | UI-003 | read-only, non-mutating, applied-context summary with explicit Open request or recovery action |
| Request Anchor | UI-005 | compact sticky identity/status/action/context strip; phone, service location, owner accessible without scroll |
| Work Canvas | UI-005 | attention guidance, original customer need, conditional actionable work context, composition, then history |
| Action treatment | UI-006 | one local primary at rest; semantic navy/teal/outline/red/amber treatment with visibility disclosure |
| Field action area | UI-007 | only permitted actions, 48px+ targets, keyboard-aware unpinning, no price data in field capture |
| Containment primitives | UI-008 | inline, drawer, dialog, focused workspace; one overlay; managed focus and explicit discard protection |
| State and recovery | UI-009 | layout-preserving loading; empty versus filtered-empty; localized failure; session-only unsaved input; explicit retry and conflict re-review |
| Public intake | UI-010 | business-first trust, established minimum fields, non-enumerating unavailable state, truthful confirmation and link recovery |
| Customer Request page | UI-011 | business-first public capability view; public updates and bounded Add details; terminal, expired, and unavailable states; no internal data |
| Quality and migration seams | UI-012, UI-013 | accessible state announcements and focus/contrast recipes; presentation-only shell variants over one request engine |

## Required recipe fields

Every new V2 recipe must specify:

```text
purpose and user job
permitted states and server authority boundary
DOM/content order
visual-volume level
desktop and narrow/mobile containment
keyboard, focus, touch-target, and screen-reader behavior
loading, empty, error, conflict, and success treatment
explicit “never” boundaries
```
