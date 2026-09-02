# Session Log — OpHalo Foundation

**Last updated:** 2026-09-02 — **Next implementation session: GAP-067 Slice 3 — Request Detail
anchor.** GAP-067 Slices 1–2 are complete (`80b853b`, plus this commit). GAP-065 Owner/Admin
financial-review discovery is complete through Slice 3b (`f231126`, `606203d`); its detailed record
is in [BL138](build-log/138-gap-065-owner-admin-financial-review-discovery-and-delivery-plan.md).

**Purpose:** active handoff only. Completed implementation detail belongs in Git history and the
relevant build log.

## Authoritative sources

- Release priority and acceptance status: [pilot-readiness-bug-tracker.md](pilot-readiness-bug-tracker.md)
- Product decisions: [decision index](decisions/README.md) and individual ADRs
- Request Detail / queue execution sequence: [BL137](build-log/137-request-detail-and-queue-usability-handoff.md)
- Request Detail interaction contract: [Workbench signoff specification](ux-design/v2/request-detail-workbench-signoff-spec.md)
- Actual Work closeout/replacement contract: [ADR-494](decisions/ADR-494-actual-work-paper-compatible-pilot-upgrade.md), [ADR-493](decisions/ADR-493-actual-work-office-financial-resolution-and-billing-revisions.md), and [BL136 P](build-log/136-P-preflight.md)

## Current repository state

- GAP-065 is complete: Request Detail pending-review discovery, the wide financial-review
  continuation flow, server-authoritative request-row counts, the Owner/Admin row cue, and the
  persistent Actual Work Review destination. Detailed implementation and commit history are in
  [BL138](build-log/138-gap-065-owner-admin-financial-review-discovery-and-delivery-plan.md).
- GAP-067's revised Request page mockup is the retained desktop visual reference. Its exact
  presentation contract is in [GAP-067](pilot-readiness-bug-tracker.md#gap-067--request-workspace-presentation-lacks-a-coherent-operational-visual-system).
- The controlled pilot keeps the contractor's existing system authoritative for estimates,
  invoices, payments, and accounting. Keep is the factual field record; the existing-ticket
  workflow remains the outage fallback.

## Next implementation sequence

**GAP-067: Request workspace presentation coherence — in progress, sliced.** Implement the retained
desktop reference across Request List and Request Detail: Slate-50 operational canvas; restrained
white cards; compact identity/contact/location/owner anchor; retained planning row; neutral
Customer Need; semantic queue cues; and the locked action hierarchy. Preserve all current behavior,
authority, ranking, lifecycle, financial-review semantics, responsive behavior, and keyboard access.
Use the [Request Workspace Visual Token Specification](ux-design/v2/request-workspace-visual-spec.md)
as the mechanical implementation contract; add only the locked `--keep-request-*` aliases to both
token sources (`web/shared/styles/ophalo-tokens.css`, `web/ophalo-app/src/styles/app.css`) without
changing existing `--ophalo-*`/`--keep-*`/Price Book values or comments. That exact check is part
of every GAP-067 slice preflight.

Slices (start a fresh session per slice after each approved commit):

- **Slice 1 — token foundation + operational canvas — DONE (`80b853b`).** Added the 11
  `--keep-request-*` aliases to both token sources; swapped the Request List and Request Detail
  page-root canvas (`Requests.tsx`, `RequestDetail.tsx`) from `--ophalo-canvas` to
  `--keep-request-canvas`. 4 files. `check:tokens` + typecheck clean; requests/request-detail
  suites 655/655; wide-pane browser check confirmed the Slate-50 canvas. Inner queue/inset/input
  cream surfaces deliberately left for later slices.
- **Slice 2 — Request List rows + header — DONE (`022bc89`).** Group/pane/popover eyebrows →
  `text-[10px] font-bold tracking-[0.08em] --keep-request-eyebrow`; queue-row rhythm `space-y-3`
  (12 px) with a 24 px break between Needs attention / Open work (row padding kept at 16 px per
  the scan-density decision); active queue tab `bg-slate-100`, inactive hover `bg-slate-50`;
  financial dot tokenised to `--keep-request-financial-dot`; "Unassigned" chip and quick-action
  button fills off `--ophalo-canvas` onto white / `--keep-request-surface-muted`; Views-popover
  cream hovers → `bg-slate-50`. No `--ophalo-canvas` reference remains in the 5 Request List
  components. GAP-027 grammar, server ranking, and the Slice 3b financial cue (text + both-row
  rendering + non-interactivity) unchanged. `check:tokens` + typecheck clean; 573/573
  request-touching suites; wide-pane browser check confirmed. Also lands the spec's "Consistency
  with Actual Work and Price Book" section.
- **Slice 3 — Request Detail anchor** (`RequestDetailAnchor.tsx`, `MobileRequestAnchor.tsx`,
  `RequestDetailHeader.tsx`, `DetailHero.tsx`): compact identity/contact/location/owner grid,
  distinct planning row, neutral Slate Customer Need.
- **Slice 4 — Detail work canvas: attention + action hierarchy** (`RequestDetailWorkCanvas.tsx`,
  `BusinessSection.tsx`, `PrimaryActionControl.tsx`, financial-review module): single amber
  attention card, teal customer primary, dark-slate financial emphasis, outlined secondaries.
  Full narrow + keyboard + WCAG-AA acceptance evidence lands at this slice.

GAP-042 placement preflight (done, read-only): business name is `meQuery.data?.businessName` from
the auth-gated `/me` endpoint (`apiClient.ts:730`), already rendered authenticated-only as muted
`· {businessName}` in `RequestDetailHeader`; never on the public customer page. In the finished
workspace it belongs in the shell chrome (workbench/list header), outside the Request Anchor
identity grid. No GAP-042 code in any GAP-067 slice.

**Following session: GAP-042** — add the restrained, fresh authenticated business identity to the
finished Request workspace without competing with request/customer context or exposing it publicly.

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
