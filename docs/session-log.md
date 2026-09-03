# Session Log — OpHalo Foundation

**Last updated:** 2026-09-03 — **Request UI Upgrade 1.1 production implementation and its first
authenticated hierarchy refinement are complete; final product-owner visual acceptance remains.** The locked contract is
[Request UI Upgrade 1.1](ux-design/v2/request-ui-upgrade-1.1.md), and delivery evidence is in
[BL139](build-log/139-request-ui-upgrade-1.1-implementation.md).

**Purpose:** active handoff only. Completed implementation detail belongs in Git history and the
relevant build log.

## Authoritative sources

- Release priority and acceptance status: [pilot-readiness-bug-tracker.md](pilot-readiness-bug-tracker.md)
- Product decisions: [decision index](decisions/README.md) and individual ADRs
- Request Detail / queue execution sequence: [BL137](build-log/137-request-detail-and-queue-usability-handoff.md)
- Current Request Detail interaction contract: [Request UI Upgrade 1.1](ux-design/v2/request-ui-upgrade-1.1.md)
- Actual Work closeout/replacement contract: [ADR-494](decisions/ADR-494-actual-work-paper-compatible-pilot-upgrade.md), [ADR-493](decisions/ADR-493-actual-work-office-financial-resolution-and-billing-revisions.md), and [BL136 P](build-log/136-P-preflight.md)

## Current repository state

- GAP-065 is complete: Request Detail pending-review discovery, the wide financial-review
  continuation flow, server-authoritative request-row counts, the Owner/Admin row cue, and the
  persistent Actual Work Review destination. Detailed implementation and commit history are in
  [BL138](build-log/138-gap-065-owner-admin-financial-review-discovery-and-delivery-plan.md).
- Request UI Upgrade 1.1 now supplies the three-column desktop composition, compact sticky Request
  strip, frequent communication/share/work actions, and persistent Request Memory rail. Full
  frontend tests and the production build pass. The first visual-review refinement grouped the
  toolbar, demoted lifecycle completion while operational work remains, added authoritative
  financial-blocker CTAs, and moved communication actions above the right-rail timeline.
- The controlled pilot keeps the contractor's existing system authoritative for estimates,
  invoices, payments, and accounting. Keep is the factual field record; the existing-ticket
  workflow remains the outage fallback.

## Next implementation sequence

**Next session: perform the product-owner visual acceptance pass for Request UI Upgrade 1.1.** Use
representative dense Requests at 1366×768, 1440×900, and 1920×1080 and verify 100%, 125%, and 150%
zoom. Confirm the Queue remains fully operational, Customer Need and every authorized frequent
action remain reachable, the center work column is dominant, and Request Memory is readable
without horizontal page scroll. Treat findings as bounded refinement unless they change the locked
interaction or authority model.

Do not begin GAP-042 implementation until GAP-067 passes that screenshot/acceptance review. Its
read-only placement preflight remains valid: business name is `meQuery.data?.businessName` from the
authenticated `/me` endpoint and belongs in shell chrome, outside Request Anchor identity.

## Deferred next work

- **4g pilot request-close advisory:** preflight after the above safety/usability sequence. It is an
  advisory on outstanding Actual Work with a structured `Close anyway` pilot exception; it is not a
  hard Resolved→Closed gate. See BL136.
- **Pilot/release gates:** production observability (GAP-039), public-intake trust (GAP-033), phone
  integrity (GAP-016/021/051), then the remaining tracker order.
- **Minimum Office Closeout:** Billing Revision, handoff, and correction/adjustment design resume
  only after the controlled-pilot and rehearsal gates; see [BL135](build-log/135-minimum-office-closeout-mechanical-preflight.md).

## Guardrails

- The responsive staff PWA is the active field surface; native parity is not implied.
- Do not infer authority for pricing, invoicing, payments, QuickBooks, inventory, or fleet from
  Request Detail work.
- Use disposable local data for mutable acceptance; never seed founder production data.
- Preflight current code and the controlling tracker/ADR/build log. Stop for product direction when
  server data or authorization cannot truthfully support the requested UI.
