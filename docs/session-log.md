# Session Log — OpHalo Foundation

**Last updated:** 2026-09-03 — **Customer request page (Keep tracker) received a two-column layout,
contact card, and link-token access copy correction (commit `75f472f`). Next implementation batch
is GAP-039 — production observability (Sentry); GAP-033 (public-intake trust) follows it.** Request
UI Upgrade 1.1 implementation is complete (locked contract
[Request UI Upgrade 1.1](ux-design/v2/request-ui-upgrade-1.1.md), delivery evidence
[BL139](build-log/139-request-ui-upgrade-1.1-implementation.md)); its product-owner visual
acceptance pass is still outstanding.

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

**Next implementation batch: GAP-039 — production failures and pilot health are not observable
enough to earn trust.** Full scope is
[GAP-039](pilot-readiness-bug-tracker.md#gap-039--production-failures-and-pilot-health-are-not-observable-enough-to-earn-trust)
(P0, Active Work). Locked decision: Sentry for redacted server + browser error capture with
release/environment identity and founder-email alerting; no session replay, behavioral analytics,
warehouse, or owner analytics dashboard in this slice. Its five-step implementation order (telemetry
boundary and redaction; Sentry ASP.NET integration; Sentry React integration in the authenticated
PWA plus `VITE_PUBLIC_BASE_URL` startup validation; Railway health-check + alert-rule config +
runbook; production-candidate verification with controlled failures and redaction checks) is in the
tracker entry. Release-safety gate — "required before any customer-facing production pilot" — and
precedes GAP-033.

**GAP-033 — public-intake trust and tracker access truthfulness — follows GAP-039.**
Full scope is [GAP-033](pilot-readiness-bug-tracker.md#gap-033--public-intake-does-not-establish-sufficient-customer-trust-or-return-continuity)
(P1, `ophalo-web`). It corrects the public request journey so it does not overstate what the
link-token model provides and does not collect personal data before establishing trust:

- Show business identity and configured public contact before asking for customer address/contact
  data; place factual privacy/use disclosures before the relevant fields.
- Keep email visible and optional; land a successful submission directly on its tracker; ship a
  real privacy-policy link.
- Remove public copy that promises automatic tracker-link email, verification, or unsupported
  security properties. (Tracker-page access copy was corrected ahead of this batch in commit
  `75f472f`; the intake form still uses "private page"/"private link" wording.)
- Audit the public tracker event-feed for exposure: serialize an explicit allowlist of
  customer-relevant event types and message sources; internal activity must never reach
  `page.events`.

Tracker Implementation Order for this pair is GAP-039 → GAP-033 → GAP-040 (`pilot-readiness-bug-tracker.md`).

Request UI Upgrade 1.1 still needs the product-owner visual acceptance pass (dense Requests at
1366×768, 1440×900, 1920×1080; 100/125/150% zoom; Queue operational, frequent actions reachable,
center work column dominant, Request Memory readable without horizontal page scroll). That is a
product-owner review task, not a coding batch, and is independent of GAP-033.

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
- **Workbench background brand alignment:** `ophalo-app` uses an off-spec cool gray page
  background. Align it to Canvas `#F8F6F1` (`--ophalo-canvas`) per
  [BRAND.md](brand-kit/BRAND.md), with a token audit for other hardcoded grays. Internal staff
  surface only — not a pilot blocker; run as its own small pass after the customer request page
  ships.

## Guardrails

- The responsive staff PWA is the active field surface; native parity is not implied.
- Do not infer authority for pricing, invoicing, payments, QuickBooks, inventory, or fleet from
  Request Detail work.
- Use disposable local data for mutable acceptance; never seed founder production data.
- Preflight current code and the controlling tracker/ADR/build log. Stop for product direction when
  server data or authorization cannot truthfully support the requested UI.
