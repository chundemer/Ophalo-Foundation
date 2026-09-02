# Session Log — OpHalo Foundation

**Last updated:** 2026-09-02 — **GAP-065 multi-visit financial-review workflow direction is
locked; no implementation has started.** Submitted visits remain separate immutable field records;
the office experience, not the factual model, will become request-scoped. Delivery is deliberately
multi-session: (1) Owner/Admin Request Detail **Pending financial reviews (N)** task card with a
direct route per submitted/unreviewed visit; (2) wide workspace pending-visit switcher and
post-success **Review next pending visit** continuation, with dirty-switch protection; (3) a
server-authoritative Owner/Admin request-row count cue and persistent Office Review discovery,
with any cross-request queue preflighted separately. No billing/invoice grouping entity, automatic
navigation, generic batch-review action, client-inferred review state, lifecycle/ranking change,
or authorization broadening is authorized by this decision. See [GAP-065](pilot-readiness-bug-tracker.md#gap-065--owneradmin-internal-financial-review-work-is-hard-to-discover-from-requests).
The implementation contract and Slice 1A stop gate are in [BL138](build-log/138-gap-065-owner-admin-financial-review-discovery-and-delivery-plan.md).

BL136 4f-ii **presentation upgrade** landed locally: the wide
Owner/Admin Actual Work financial-review view is now a dedicated two-column workspace
(`ActualWorkFinancialReviewWorkspace.tsx`) — context rail + KPI cards with semantic margin tone +
line-item breakdown table + review card, on a cool `--keep-workspace-canvas` shell, reusing the
shared `LogContactModal` for Call/Text/Email. No API/permission/copy-semantics change; narrow path
and non-reviewer view unchanged. 6 files, frontend suite 988/988. **Open:** `KeepRequestDetailResult`
has no request-title field distinct from `customerName` — needs Christian's direction. Detail in
[BL136 §4f-ii](build-log/136-actual-work-paper-compatible-pilot-upgrade.md).

GAP-045 (Default Queue language) is closed documentation-only (title-case **All Work** tab + subtitle
already satisfy it; UI-004 controls, ADR-449 lowercase was stale). Next tracker item is GAP-042
(authenticated request work lacks visible business identity).

**Purpose:** active handoff only. Completed implementation detail belongs in Git history and the
relevant build log.

## Authoritative sources

- Release priority and acceptance status: [pilot-readiness-bug-tracker.md](pilot-readiness-bug-tracker.md)
- Product decisions: [decision index](decisions/README.md) and individual ADRs
- Request Detail / queue execution sequence: [BL137](build-log/137-request-detail-and-queue-usability-handoff.md)
- Request Detail interaction contract: [Workbench signoff specification](ux-design/v2/request-detail-workbench-signoff-spec.md)
- Actual Work closeout/replacement contract: [ADR-494](decisions/ADR-494-actual-work-paper-compatible-pilot-upgrade.md), [ADR-493](decisions/ADR-493-actual-work-office-financial-resolution-and-billing-revisions.md), and [BL136 P](build-log/136-P-preflight.md)

## Current repository state

- `main` includes RD-019A and RD-058A and is ahead of `origin/main`. Every Claude session must confirm current
  worktree/branch state before editing.
- The 4e–4f Actual Work pilot work is complete locally through item-picker drawer polish and
  keyboard navigation (`1fe8580`), plus the 4f-ii financial-review workspace presentation upgrade
  (2026-09-01). It remains code-only after the 4e-i migration; durable detail is
  in [BL136](build-log/136-actual-work-paper-compatible-pilot-upgrade.md).
- The controlled pilot keeps the contractor's existing system authoritative for estimates,
  invoices, payments, and accounting. Keep is the factual field record; the existing-ticket
  workflow remains the outage fallback.

## Next implementation sequence

The BL137 Request Detail / queue usability sequence (RD-019A, RD-058A, RD-058B-1/2, RD-059A,
Q-027A) is **complete**. Per-item detail lives in Git history and the
[BL137](build-log/137-request-detail-and-queue-usability-handoff.md) resolution notes; GAP-019,
GAP-058, GAP-059, and GAP-027 are resolved.

**GAP-045 is resolved (documentation-only).** The shipped UI already meets its substance — the
Owner/Admin **All Work** tab plus the subtitle "Open requests and feedback requiring review, ranked
with customer promises needing attention first." UI-004 (2026-08-21) controls the title-case label;
ADR-449's lowercase "All work" was stale. No labels, copy placement, server ranking, or navigation
changed. UI-004's Office Review discoverability requirement stays with GAP-065, not GAP-045.

**Next batch: GAP-065 Slice 1B-server.** Slice 1A discovery is complete and accepted (2026-09-02);
findings and the corrected file-level gate are in [BL138 §Slice 1A findings](build-log/138-gap-065-owner-admin-financial-review-discovery-and-delivery-plan.md).
1B-server only, awaiting explicit go: new Owner/Admin-only
`GET /keep/pricebook/actual-work/request/{requestId}/pending-financial-reviews` on
`ActualWorkFinancialReadApiService`, request-scoped variant of the unreviewed-queue query, plus a
bounded batched resolution/disposition-by-visit-ids read; no-price/cost DTO with a three-value
`reviewStatus` (`ReadyToReview` / `NeedsCostPriceResolution` / `NeedsNoChargeDisposition`) folded
from effective resolutions + the disposition fact; no migration; auth + query + status-derivation
tests. Stop for the reviewed-diff gate. 1B-client (task card, narrow scroll-and-focus, cross-hook
refresh wiring) is a separate session.

**Tracker order after the GAP-065 slices:** GAP-042, GAP-041, GAP-046, GAP-043, GAP-044, GAP-026,
GAP-053. Do not pull GAP-047, GAP-048, GAP-049, filters, history, pagination, or generic queue
redesign into these sessions.

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
