# Build Log 139 — Request UI Upgrade 1.1 Implementation

**Status:** Production implementation complete; product-owner visual acceptance pending
**Date:** 2026-09-03
**Authority:** [Keep Request UI Upgrade 1.1](../ux-design/v2/request-ui-upgrade-1.1.md)

## Delivered outcome

The qualifying desktop Request workspace now composes the retained Queue, a protected Active
Request Work column, and a 300 px Request Memory rail. The selected Request owns one center/right
scroll surface, with a compact sticky identity, Customer Need, lifecycle primary, and frequent
actions strip inside that surface. The Queue implementation and its filters, counts, search,
Office Review, Views, History, selection, and independent scroll behavior were not replaced.

The frequent-action strip directly exposes permission-gated Contact customer; Call, Text/SMS, and
Email launch paths; Business page and Customer request page sharing; Actual Work with price-book
iconography; and authoritative pending financial-review continuation. The existing Contact customer
sheet remains the durable log boundary: launching a phone, text, or email workflow does not create
an event until the user submits direction, channel, follow-up, and summary.

Request Memory defaults to Communications and provides Customer/Internal filters, durable contact
logging, and a direct Add internal note action. Request history exposes the permitted full event
lineage. Details contains customer contact, service location, owner/team, planning, submitted visit
history, and lower record details. The selected tab persists in session storage across Request
selection, and the tab set implements standard arrow/Home/End keyboard movement.

## Preserved contracts

- Existing server-authored action and role gates remain the source of truth.
- Existing request-version concurrency, mutation replacement, and recovery paths remain intact.
- Customer-message attention continues to own the integrated response composer.
- Actual Work capture remains price-blind; the new dollar/price-book icon is navigation language,
  not financial disclosure.
- Narrow Request Detail retains its focused mobile composition and action rail.
- The Business page destination comes from the existing Owner/Admin intake contract; the client
  does not invent or guess a public slug.

## Verification

- `pnpm typecheck` — passed.
- Full frontend Vitest suite — 108 files, 1,025 tests passed.
- `pnpm build` — passed, including font copy and CSS token validation; only the existing Vite
  bundle-size advisory remains.
- `git diff --check` — passed.

New focused coverage pins the three-column frame and shared scroll surface, compact action strip,
contact/share/work shortcuts, long Customer Need expansion, communication filtering, complete
history, tab persistence, keyboard tab navigation, and Request Memory activation of Internal note.

## Remaining acceptance

This commit is mechanically verified but does not claim the product-owner visual pass. Compare the
authenticated workspace with representative dense Requests at 1366×768, 1440×900, and 1920×1080,
plus 100%, 125%, and 150% zoom. Any resulting work should be bounded visual refinement; a change to
action meaning, auditability, privacy, authorization, or the locked column model returns to product
review.
