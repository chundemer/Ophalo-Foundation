# Session Log — OpHalo Foundation

**Last updated:** 2026-09-01 — RD-019A (behavior-preserving Request Detail composition seams)
complete; next session is RD-058A. Handoff detail in [BL137](build-log/137-request-detail-and-queue-usability-handoff.md).

**Purpose:** active handoff only. Completed implementation detail belongs in Git history and the
relevant build log.

## Authoritative sources

- Release priority and acceptance status: [pilot-readiness-bug-tracker.md](pilot-readiness-bug-tracker.md)
- Product decisions: [decision index](decisions/README.md) and individual ADRs
- Request Detail / queue execution sequence: [BL137](build-log/137-request-detail-and-queue-usability-handoff.md)
- Request Detail interaction contract: [Workbench signoff specification](ux-design/v2/request-detail-workbench-signoff-spec.md)
- Actual Work closeout/replacement contract: [ADR-494](decisions/ADR-494-actual-work-paper-compatible-pilot-upgrade.md), [ADR-493](decisions/ADR-493-actual-work-office-financial-resolution-and-billing-revisions.md), and [BL136 P](build-log/136-P-preflight.md)

## Current repository state

- `main` includes RD-019A and is ahead of `origin/main`. Every Claude session must confirm current
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
2. **RD-058A** — Actual Work Review queue exposes factual request lifecycle status. ← next.
3. **RD-058B** — Request Detail action hierarchy and internal-review clarity.
4. **RD-059A** — readable, keyboard-safe Internal Planning controls.
5. **Q-027A** — Owner/Admin queue row hierarchy, badges, and selection/severity treatment.

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
