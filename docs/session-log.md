# Session Log — OpHalo Foundation

**Last updated:** 2026-09-01 — RD-059A (Internal Planning controls) implementation complete pending
commit; GAP-059 addressed. Next session is Q-027A (Owner/Admin queue row hierarchy). Handoff detail
in [BL137](build-log/137-request-detail-and-queue-usability-handoff.md).

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
  keyboard navigation (`1fe8580`). It remains code-only after the 4e-i migration; durable detail is
  in [BL136](build-log/136-actual-work-paper-compatible-pilot-upgrade.md).
- The controlled pilot keeps the contractor's existing system authoritative for estimates,
  invoices, payments, and accounting. Keep is the factual field record; the existing-ticket
  workflow remains the outage fallback.

## Next implementation sequence

Run the Claude sessions in this exact order, one accepted commit at a time:

1. **RD-019A** — behavior-preserving Request Detail composition seams. ✅ complete.
2. **RD-058A** — Actual Work Review queue exposes factual request lifecycle status. ✅ complete
   (commit `c5796e0`): queue source row + entry carry the request lifecycle slug via the shared
   exhaustive `KeepRequestDetailMapper.MapStatus`; membership/FIFO order/count unchanged; queue row
   renders `Request: {statusLabel(...)}` plus `Submitted visit awaiting internal financial review`.
   Integration `ActualWorkFinancialReadApiTests` 28 passed; new `ActualWorkReviewQueueList.test.tsx`;
   tsc/`check:tokens`/`vite build`/`git diff --check` clean. GAP-058 remains open for the RD-058B
   Request Detail action-hierarchy work.
3. **RD-058B-1** — Internal financial review clarity. ✅ complete (`2ae07d5`): review card reframed
   as **Internal financial review** ("…does not change the customer request"), action → **Complete
   internal financial review**, per-visit state → **Financial review pending/completed**, both
   success surfaces announce the customer request status is unchanged.
4. **RD-058B-2** — Request Detail action hierarchy. ✅ complete: during active attention the
   server-authored attention action is sole-dominant; standalone Anchor **Contact customer** removed
   unconditionally; non-primary alternate → **Resolve another way…** opening the Why/Resolve-by
   guidance disclosure; **Mark work done** relocated to a quiet "Request lifecycle" block in the
   Work Canvas after Actual Work, before the composer (desktop + mobile), still gated on
   `markWorkDoneSecondary`; Anchor inner card bounded to `max-w-4xl mx-auto w-full`. Review fix:
   both Mark work done controls (and Close request) confirm through one focused
   `MutationConfirmDialog` (`KeepModal`-based, centered/bottom-sheet, Cancel-focused, Escape
   restores trigger focus) instead of an inline row that had expanded the Anchor and displaced the
   request identity. Full frontend suite 953 passed; tsc/`check:tokens`/`vite build`/`git diff
   --check` clean; visual evidence captured. No server/policy change.
5. **RD-059A** — readable, keyboard-safe Internal Planning controls. ✅ implementation complete
   (commit pending review): GAP-059 addressed. `TimingPanel` strip + full/`bare` modes now open the
   first editor field on open, close on Escape (`preventDefault` + `stopPropagation`) and on Cancel
   with focus restored to the disclosure trigger, and announce save/conflict errors through
   `role="alert"` in the relevant editor. Locked planning-row copy applied: labels **Internal
   priority** / **Planned work date** / **Internal follow-up (optional)**; enabled empty controls
   read **Set planned date** / **Set follow-up date** in normal-contrast ink with a calendar cue and
   no ellipsis; a restrained configuration checkmark shows for the current Internal priority
   (including default Routine) and a persisted Planned work date only — never for an empty planned
   date or the optional follow-up; read-only values drop the chevron/hover/button semantics and show
   a persistent muted **Read only** caption. Existing date/reason validation, mutation/version/
   conflict policy, and one-open-editor behavior unchanged; no server/policy change. 3 production
   (`TimingPanel.tsx`, `DetailPanels.tsx`) + test files: new `TimingPanel.strip.test.tsx`, extended
   `DetailPanels.priority.test.tsx` and `RequestDetailAnchor.test.tsx` (locked-copy rename). Full
   frontend suite 977 passed; tsc/`check:tokens`/`vite build`/`git diff --check` clean; visual
   evidence captured (desktop, narrow PWA, keyboard, zoom).
6. **Q-027A** — Owner/Admin queue row hierarchy, badges, and selection/severity treatment. ← next.

The full scope, exclusions, test proof, and source-of-truth decisions are in
[BL137](build-log/137-request-detail-and-queue-usability-handoff.md). Do not pull GAP-047,
GAP-048, GAP-049, filters, history, pagination, or generic queue redesign into these sessions.

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
