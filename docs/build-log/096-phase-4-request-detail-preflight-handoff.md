# Build Log 096 — Phase 4 Request Detail Preflight and Handoff

**Status:** Superseded for current execution by [BL137](137-request-detail-and-queue-usability-handoff.md). Retained as the July 2026 preflight record.
**Date:** 2026-07-27
**Scope:** Phase 4 sequencing and Session 4.1 / GAP-019 only
**Controlling work to preserve:** Build 086; GAP-019, GAP-032, ADR-443, ADR-451; current Request Detail layouts

## Operational priority

Do not begin this refactor ahead of the remaining launch-evidence work:

1. Complete deployed verification for **GAP-052** (the customer-update notification-integrity flow).
2. Complete **Phase 5.1 / GAP-033** real-browser evidence: public intake, expired tracker, and OffSeason.

4.1 is deliberately a risk-reduction refactor, not a reason to delay those operational gates.

## Locked Phase 4 order

Implementation sessions run only in this order:

```text
4.1 / GAP-019  →  4.2 / GAP-047, GAP-048, GAP-049  →
4.3 / GAP-050, GAP-051  →  4.4 / GAP-042
```

Reserve one completed-session build log per slice:

| Build Log | Session | Purpose |
|---|---|---|
| 097 | 4.1 | Request Detail decomposition result |
| 098 | 4.2 | Mutation, sharing, and follow-up bounds result |
| 099 | 4.3 | Customer continuity and phone presentation result |
| 100 | 4.4 | Authenticated workspace identity result |

## 4.1 — Locked implementation slice

**Goal:** establish durable Request Detail presentation seams with **no intentional behavior
change**. This is a first pass, not the final structural shape of `RequestDetail.tsx`.

The July plan treated `web/ophalo-app/src/pages/RequestDetail.tsx` as the page's sole controller.
Current Actual Work, timing, and lifecycle components now legitimately own bounded local form state,
mutation/retry, and conflict handling. BL137 replaces this with one page-level coordinator for
authoritative detail/cache/navigation/overlay policy plus shared feature controllers that return
authoritative replacement detail. Desktop and mobile must still never implement business behavior
separately.

### Current surface to preserve

The current page already has these shared presentation components:

- `request-detail/RequestDetailDesktopLayout.tsx` and `RequestDetailMobileLayout.tsx` compose the
  same action/context panels at their intentionally different responsive placements.
- `DetailHero.tsx`, `CustomerContactStrip.tsx`, `UnifiedComposer.tsx`, `TimingPanel.tsx`,
  `FollowUpResolutionPanel.tsx`, `DetailPanels.tsx`, `BusinessSection.tsx`, `TeamSection.tsx`, and
  `TimelineEvent.tsx` carry the existing detail presentation and action surfaces.
- `RequestDetail.tsx` still contains the breadcrumb/queue navigation, loading and error states,
  main-column composition, activity filter/timeline wrapper, controller-owned overlays, and the
  `LogContactModal`, `ServiceLocationModal`, and `US_STATES` implementation.

### Seams to establish

Subject to the preflight's exact current line-level confirmation, extract only bounded
presentation wrappers such as:

| Target seam | Ownership | Must not own |
|---|---|---|
| `RequestDetailHeader` | Existing Requests back control, reference code, and Prev/Next markup. | Navigation decision or route state. |
| `RequestDetailContent` | Existing main-column composition and responsive placement call sites. | Querying, mutations, draft/panel state, or focus policy. |
| `RequestDetailActivity` | Existing activity filter controls, empty copy, and timeline markup. | Event filtering/sorting policy or filter state; receive the already-derived events/value/callback. |
| `RequestDetailStates` (or separate bounded loading/error components) | Existing skeleton and error/retry presentation. | Query lifecycle or retry policy; receive state/callbacks from the controller. |

Names may change only when the preflight proves an existing component supplies the same seam more
cleanly. Do not create a second device-specific Request Detail implementation, a `features/`
architecture, a context/reducer, or a prop-bag that conceals controller policy.

## Preservation guardrails

- No backend, DTO, API-client, route, query-key, cache, polling, permission, action-policy,
  lifecycle, or canonical phone-value change.
- Preserve exact desktop/mobile ordering and shared callbacks, including the direct-mobile versus
  desktop-QR handoff rules. Do not introduce delivery claims or expose a raw private page token.
- Preserve optimistic update, conflict, error, refresh, focus restoration, and loading/retry
  behavior exactly. Fixes for GAP-047, GAP-048, and GAP-049 belong solely to 4.2.
- Preserve `Needs Share`, ADR-451 notification preparation/confirmation behavior, customer/internal
  visibility boundaries, timeline filtering/order, and all current copy/classes/accessibility
  semantics.
- Preserve the current follow-up prefill exactly in 4.1, including its known bound issue; its safe
  truncation and explanatory copy are 4.2 work.
- Preserve current NA-phone formatting and canonical `tel:`/`sms:` targets; 4.3 completes the
  remaining GAP-051 scope.

## Explicitly deferred — do not leak into 4.1

- The two Request Detail form modals (`LogContactModal`, `ServiceLocationModal`) and `US_STATES`.
  Their extraction follows GAP-032's still-deferred dirty-close confirmation/backdrop policy and
  broader `KeepModal` adoption. Leaving them in the controller is intentional; 4.1 is not the
  final Request Detail file shape.
- GAP-032 modal behavior, focus-trap expansion, dirty-form protection, or modal-copy redesign.
- All 4.2 mutation/share/follow-up behavior fixes; all 4.3 related-work and phone work; all 4.4
  business-name/context work.
- Visual redesign, responsive redesign, new accessibility interactions, test/mock architecture
  cleanup, native-mobile work, and Request List state restoration.

## Preflight and batch gate

Before editing, report the actual source/test imports and an exact file list for Codex validation.
The plan is frontend-only and capped at **eight production files and twelve changed files total**.
If the named seams cannot fit coherently, stop and propose a split; do not take a batch exception.
The preflight must also confirm a clean working tree for Request Detail files and record baseline
test/check output.

## Required test surface and validation

Keep the existing focused Request Detail coverage intact:

- `request-detail/__tests__/BusinessSection.notify.test.tsx` and
  `NotifyCustomerPanel.test.tsx` for the ADR-451 notification flow;
- `CallHandoffQr.test.tsx` and `TextQrModal.test.tsx` for shared contact/handoff presentation;
- all current Request List and broader PWA tests, since detail cache/navigation contracts are shared.

Add a focused decomposition test only where it proves a seam preserves a real contract (for example,
header navigation callback forwarding or activity-filter callback forwarding). Do not bulk-rewrite
tests merely to mirror extracted markup.

Run, from `web/ophalo-app`:

```bash
pnpm test
pnpm typecheck
pnpm check:tokens
pnpm build
```

Then run `git diff --check`. Use the normal local narrow/wide visual review if available; expect no
intentional visual or interaction difference. Record the exact passing counts and any verified
manual comparison in Build Log 097.
