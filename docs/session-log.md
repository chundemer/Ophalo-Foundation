# Session Log — OpHalo Foundation

**Last updated:** 2026-09-02 — **GAP-067 revised Request reference page is locked for the later
presentation pass;
GAP-065 Slice 1B-server (`faf7b64`), Slice 1B-client
(`e27c48c`), Slice 2 (`6ab880b`), and Slice 3a (`baaeff1`, server-authoritative Owner/Admin
request-row financial-review count cue + ADR-463 amendment) are committed.**
Submitted visits remain separate immutable field records;
the office experience, not the factual model, will become request-scoped. Delivery is deliberately
multi-session: (1) Owner/Admin Request Detail **Pending financial reviews (N)** task card with a
direct route per submitted/unreviewed visit; (2) wide workspace pending-visit switcher and
post-success **Review next pending visit** continuation, with dirty-switch protection; (3) a
server-authoritative Owner/Admin request-row count cue and persistent Office Review discovery,
with any cross-request queue preflighted separately. No billing/invoice grouping entity, automatic
navigation, generic batch-review action, client-inferred review state, lifecycle/ranking change,
or authorization broadening is authorized by this decision. See [GAP-065](pilot-readiness-bug-tracker.md#gap-065--owneradmin-internal-financial-review-work-is-hard-to-discover-from-requests).
The implementation contract and Slice 1A stop gate are in [BL138](build-log/138-gap-065-owner-admin-financial-review-discovery-and-delivery-plan.md).

**GAP-066 Catalog Item financial-workspace direction is locked.** The existing-data UI slice may
recompose identity, economics/profitability, aliases, and action hierarchy on the shared cool
financial canvas without changing behavior. Associated Assemblies and Nudges are deliberately a
later Owner/Admin-only server-authoritative impact-read slice; do not mock, client-infer, or
decorate unsupported relationships. See [GAP-066](pilot-readiness-bug-tracker.md#gap-066--catalog-item-detail-is-not-yet-a-usable-financial-and-operational-impact-workspace).

**GAP-067 Request workspace visual reference is locked.** Christian will retain the revised Request
page mockup as the desktop implementation reference. It confirms a Slate-50 operational canvas;
white, Slate-200 bordered cards; a compact request anchor with neutral Customer Need and a retained
planning row; amber only for active customer attention; teal customer-response primary actions;
dark-slate contextual financial-review emphasis; quiet outlined continuation actions; and a
consistent 20–24 px major-card rhythm. It preserves the locked GAP-027 row grammar and all
behavior, authority, ranking, lifecycle, and financial-review semantics. See
[GAP-067](pilot-readiness-bug-tracker.md#gap-067--request-workspace-presentation-lacks-a-coherent-operational-visual-system).

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

**GAP-065 Slice 1B-server is complete and committed (`faf7b64`, 2026-09-02).** New Owner/Admin-only
`GET /keep/pricebook/actual-work/request/{requestId}/pending-financial-reviews` on
`ActualWorkFinancialReadApiService`, request-scoped unreviewed-queue predicate, bounded batched
resolution/disposition-by-visit-ids reads, no-price/cost DTO with three-value `reviewStatus` folded
from effective resolutions + the disposition fact. No migration, no DI change, no client files.
16 new tests; `ActualWorkFinancialRead` + `FinancialResolutionPersistence` classes 59/59,
architecture 14/14. (Unrelated worktree hunk: the `requestStatus.ts` frontend crash guard + its
test remain uncommitted for a separate standalone UI bugfix commit.)

**GAP-065 Slice 1B-client is implemented and committed (`e27c48c`, 2026-09-02).**
New `useActualWorkPendingReviews(requestId, enabled)` hook with its own `reload()`;
`ActualWorkPendingReviewsCard` (`Pending financial reviews (N)`) rendered by
`RequestDetailActualWorkSection` above the Actual Work module, gated on `canReviewActualWork`, both
viewports, renders with an open Draft (GAP-065A). Wide row → `onNavigateToActualWorkspace`
deep link; narrow row → `RequestDetailContent` holds a `pendingFocusVisitId`, `ActualWorkReviewCard`
scrolls-and-focuses that visit's inline card once loaded (race-free, per-visit anchor id). Single
`handleFinancialReviewChanged` → `pendingReviews.reload()`, fired at every readiness-changing
outcome: review/resolution/no-charge **success and `reconciled`**, review-blocked branches, manual
retry, and the replacement success branch. Locked zero-line copy: "Record no-charge disposition".
7 production + 5 test files; frontend suite 1004/1004, typecheck clean. Four review corrections
applied (reconcile-outcome refresh, retry refresh, narrow-focus race, copy). No API / permission /
migration change. BL138 §"Slice 1B-client — implemented" carries the detail.

**GAP-065 Slice 2 is committed (`6ab880b`, 2026-09-02).** Frontend only: the wide
Owner/Admin workspace now composes the server-authoritative `useActualWorkPendingReviews` for a
compact `PendingVisitSwitcher` (rendered only for 2+ pending visits), switches visits via
`onSwitchVisit` → `history.replaceState` (exact-visit URL kept, no Back-stack entry), offers a
post-review **Review next pending visit** (first remaining server-ordered pending visit, no
wraparound) + **Back to request**, and guards switch/back/next behind an inline discard confirm
whenever the reviewer note or any resolution / no-charge / correction form holds unsaved input.
No API / permission / migration change. 6 production + 3 test files; `tsc` clean; frontend suite
1014/1014. Detail in [BL138 §"Slice 2 — implemented"](build-log/138-gap-065-owner-admin-financial-review-discovery-and-delivery-plan.md).
(Unrelated uncommitted worktree change present: `CatalogItemDetail.test.tsx` — GAP-066 catalog
work, keep out of the Slice 2 commit.)

**GAP-065 Slice 3a is committed (`baaeff1`, 2026-09-02).** Server only: `KeepRequestSummary`
gains `pendingFinancialReviewCount`; `GetKeepRequestListService` folds an exact server-authoritative
per-request count from a bounded batched `IKeepRequestListPersistence.GetPendingFinancialReviewCountsAsync`
projection, gated identically to the Actual Work Review destination (Owner/Admin + `RequestsOperate`
+ `AccountingManage` + Price Book entitlement + office-financial Off Season account access that is
neither Blocked nor read-only). No migration, no DI registration change. ADR-463 amended;
decision-index updated. 4 production + 3 test files; unit + architecture suites green, touched
integration classes green. Detail in
[BL138 §"Slice 3a — implemented"](build-log/138-gap-065-owner-admin-financial-review-discovery-and-delivery-plan.md).

**GAP-065 Slice 3b is committed (`f231126` + `606203d` pane-mode amendment, 2026-09-02).**
Frontend only: client `KeepRequestSummary` gains `pendingFinancialReviewCount: number`;
`RequestRow` renders a quiet, non-interactive cue when the server-authoritative count is > 0 — a
tiny amber dot + muted `text-slate-600` "1 visit needs financial review" / "N visits need financial
review", no badge/rail/link/button/hover, no ranking or attention change. Rendered in the default
row **and** the compact `paneMode` row (beneath the `Next:` / action-signal line) — a Christian-
approved scoped exception to the 2026-08-24 compact-row rule, since the wide two-pane queue is the
normal operational surface. No API / permission / routing / migration change. Frontend suite
1017/1017, `tsc` clean. Detail in
[BL138 §"Slice 3b — implemented"](build-log/138-gap-065-owner-admin-financial-review-discovery-and-delivery-plan.md).
**Slice 3c is closed documentation-only** — the existing "Actual Work Review" Office Review tab
already satisfies BL138's persistent-destination requirement. GAP-065 delivery is complete.

**Next batch: GAP-042** (authenticated request work lacks visible business identity) — see tracker
order below.

**Tracker order after the GAP-065 slices:** GAP-042, GAP-041, GAP-046, GAP-043, GAP-044, GAP-026,
GAP-053, then GAP-067. Do not pull GAP-047, GAP-048, GAP-049, filters, history, pagination, or generic queue
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
