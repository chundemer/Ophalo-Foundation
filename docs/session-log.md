# Session Log — OpHalo Foundation

**Last updated:** 2026-09-06 — planning-doc reconciliation, no code change. This brief and
[pilot-readiness-bug-tracker.md](pilot-readiness-bug-tracker.md) were re-aligned with merged work:
GAP-039 browser/API Sentry code is merged (founder-owned console/alert verification still owed),
and GAP-065, GAP-068, and GAP-033 are done. GAP-025 is deferred; GAP-070/GAP-071 re-rated P2.

**Purpose:** short next-session brief only. Status/severity live in the tracker; decisions in ADRs;
delivery history in `docs/build-log/`.

## Current baseline

- Pilot posture: controlled parallel field pilot ([BL131](build-log/131-next-week-parallel-field-pilot-plan.md)).
  The contractor's existing system stays authoritative for estimates, invoices, payments, and
  accounting; Keep is the factual field record; the existing-ticket workflow is the outage fallback.
- Done and merged: BL142 pilot onboarding upgrade (ADR-496, Sessions 0–4);
  GAP-065 financial-review discovery ([BL138](build-log/138-gap-065-owner-admin-financial-review-discovery-and-delivery-plan.md));
  GAP-068 multi-workspace sign-in + invited name (ADR-497, Slices 1–4, verification `2cda916d` / [BL143](build-log/143-multi-workspace-signin-and-invited-name-handoff.md));
  GAP-033 public-intake trust ([BL145](build-log/145-gap-033-public-intake-trust-and-event-feed-allowlist.md)).
- Phone integrity: GAP-016/021 resolved; GAP-051 public-web done ([BL147](build-log/147-gap-051-public-web-business-phone-display.md));
  native parity deferred to Session 14 (ADR-236).

## Next coding session

**GAP-069 — bounded Data Protection key-ring persistence + trusted forwarded-headers hardening**
for the Railway production API. Separate from GAP-068. Requires a Railway persistent-volume /
external key-store choice recorded in a runbook. See the GAP-069 tracker entry.

## Founder-owned prerequisite (operational, not a coding session)

GAP-039 browser/API Sentry is merged (`d7d0ee22`, `fd34af34`, `baf07265`, `a69a8edf`, `70e75a3f`);
[`docs/runbook/sentry-configuration.md`](runbook/sentry-configuration.md) and the production
smoke-test script are in-repo. Still owed before any customer-facing pilot: Sentry / Railway /
Vercel console DSN, healthcheck, and founder-email alert configuration; alert-delivery
verification; and the Batch 4 production-candidate gate
([BL140](build-log/140-gap-039-sentry-implementation-handoff.md)).

## Do not start yet

- **GAP-042** — blocked until GAP-067 passes its wide/narrow screenshot acceptance.
- **GAP-070 / GAP-071** optional-module UI — deferred; entitlement stays on the authorized internal
  path; a product/commercial-workflow decision is required before scheduling.
- **GAP-025** — deferred; ADR-492 is a narrow historical request-phone continuity guardrail only.
  Touch it only to fix a concrete regression in the existing possible-customer flow.
- **Minimum Office Closeout** ([BL135](build-log/135-minimum-office-closeout-mechanical-preflight.md)) —
  resumes after the controlled-pilot and rehearsal gates.

## Outstanding acceptance passes (review tasks, not coding)

- Request UI Upgrade 1.1 — product-owner visual acceptance ([BL139](build-log/139-request-ui-upgrade-1.1-implementation.md)).
- Settings & Getting Started V2 — §5 screenshot acceptance for Public Link & Profile and Team ([BL144](build-log/144-settings-and-getting-started-v2-upgrade.md)).

## Deferred — carry into the workboard at migration

- 4g pilot request-close advisory (soft "Close anyway" exception) — preflight after the
  safety/usability sequence ([BL136-P](build-log/136-P-preflight.md)).
- Price Book direct-cost visibility on the Catalog Items list — after Settings V2 acceptance.
- Workbench background brand alignment (`ophalo-app` cool-gray → Canvas `#F8F6F1`,
  [BRAND.md](brand-kit/BRAND.md)) — small internal-surface pass, not a pilot blocker.

## Guardrails

- The responsive staff PWA is the active field surface; native parity is not implied.
- Do not infer authority for pricing, invoicing, payments, QuickBooks, inventory, or fleet from
  Request Detail work.
- Use disposable local data for mutable acceptance; never seed founder production data.
- Preflight current code and the controlling tracker/ADR/build log. Stop for product direction when
  server data or authorization cannot truthfully support the requested UI.
