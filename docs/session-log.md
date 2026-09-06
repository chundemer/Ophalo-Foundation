# Session Log — OpHalo Foundation

**Updated 2026-09-06** — status and sequencing live in the [workboard](workboard.md); this is the
next-session pointer only.

## Baseline

- Controlled parallel field pilot ([BL131](build-log/131-next-week-parallel-field-pilot-plan.md)): existing system remains authoritative for estimates/invoices/payments/accounting; Keep is the factual field record.

## Canonical release-safety order

The workboard is authoritative: GAP-039 (founder-owned ops) → GAP-069 → GAP-040 → the Request
Detail sequence. GAP-063, GAP-048, and GAP-049 are launch gates; see its checklist for what each blocks.

## Next coding session

**GAP-069** — bounded Data Protection key-ring persistence + trusted forwarded-headers hardening
(Railway API). Needs a persistent-volume / external key-store choice recorded in a runbook.

## Founder-owned prerequisite (operational, not a coding session)

GAP-039 Sentry code is merged; runbook + smoke-test are in-repo. Owed before any customer-facing
pilot: Sentry / Railway / Vercel console DSN + healthcheck + founder-email alert configuration,
alert-delivery verification, and the Batch 4 production-candidate gate
([BL140](build-log/140-gap-039-sentry-implementation-handoff.md)).

## Do not start yet

- **GAP-042** — until GAP-067 passes its wide/narrow screenshot acceptance.
- **GAP-070 / GAP-071** — deferred; entitlement stays on the authorized internal path; needs a commercial-workflow decision.
- **GAP-025** — deferred; touch only to fix a concrete regression in the existing ADR-492 possible-customer flow.
- **Minimum Office Closeout** ([BL135](build-log/135-minimum-office-closeout-mechanical-preflight.md)) — after the controlled-pilot and rehearsal gates.

## Guardrails

- The responsive staff PWA is the active field surface; native parity is not implied.
- Do not infer pricing / invoicing / payments / QuickBooks / inventory / fleet authority from Request Detail work.
- Use disposable local data for mutable acceptance; never seed founder production data.
- Preflight current code and the controlling workboard/ADR/build log; stop for product direction when server data or authorization cannot truthfully support the requested UI.
