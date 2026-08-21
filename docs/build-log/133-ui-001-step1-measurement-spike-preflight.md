# 133 — UI-001 Step 1: Measurement/Sizing Spike Preflight

**Status:** Complete — reviewed and approved by Christian, 2026-08-21. Result: protected Workbench
minimum locked at **640 CSS-px**; protected application-workspace minimum locked at **1001 CSS-px**
(360 Queue + 640 Workbench + 1px border). Recorded in `docs/ux-design/v2/keep-ui-design-model-v2.md`
§13 and `docs/build-log/132-ui-001-queue-workbench-shell-preflight.md` §7. All four temporary spike
files deleted per §7 go/no-go gate. Does not authorize `RequestWorkbenchShell`, Priority Preview,
pane-mode navigation, or embedded Request Detail — those are gated on the separate Step 3 preflight.

**Governing decisions:** UI-001 (bounded 320–360 CSS-px Queue pane; "container/minimum-width rule,
not a fixed viewport breakpoint"; reviewed at 100/125/150% browser zoom with populated data).
`keep-ui-design-model-v2.md` §13: "The exact protected workbench minimum remains an implementation
measurement validated at 100%, 125%, and 150% zoom with populated data" — this spike produces that
measurement.

## 1. What is being measured

The **protected Workbench minimum**: the smallest CSS-px width at which the Workbench pane (Request
Anchor + Work Canvas, reusing `RequestDetailDesktopLayout`/`DetailPanels` per build-log 132 §3.3)
remains usable — no clipped/truncated action labels, no horizontal scroll on cards, no control
falling below its locked touch-target size (44px general / 48px persistent field targets), no wrap
that breaks the anchor's phone/service-location/owner-without-scroll requirement (UI-005).

The **protected application-workspace minimum** = Queue pane width (320–360 CSS-px, already locked)
+ protected Workbench minimum (this spike's output) + any gutter/border between panes. This total is
the threshold `RequestWorkbenchShell` will use in Step 2+ to choose two-pane vs. one-pane fallback.
This spike proposes only the Workbench half of that sum; the Queue half is already fixed by UI-001.

## 2. Existing width data points (from current code, read this pass)

- Today's full-page `RequestDetailContent.tsx:42` uses
  `md:grid md:[grid-template-columns:minmax(0,7fr)_minmax(320px,3fr)]` — the existing action-rail
  column (`RequestDetailDesktopLayout`, the `<aside>` at `RequestDetailDesktopLayout.tsx:32`)
  already carries a real, locked **320px minimum** at 7:3 ratio against an unbounded main column.
  This is the closest existing precedent for a "how narrow can this content get" constraint, but it
  is not directly transferable: today's main column and rail coexist inside a full page with no
  competing Queue pane, so the *main* content column (customer need, activity stream, composition —
  the Work Canvas) has never been measured at a bounded width.
- No `ResizeObserver` usage exists anywhere in `web/ophalo-app/src` today (verified this pass) — the
  container/minimum-width measurement approach is new infrastructure, not a reuse of an existing
  pattern.
- No custom Tailwind breakpoints are configured (`web/ophalo-app/tailwind.config.ts` uses only
  `extend.fontFamily`); default breakpoints (`md`=768, `lg`=1024, `xl`=1280 px) are available as
  reference points but are not assumed to be the answer — UI-001 explicitly rejects a fixed
  viewport breakpoint in favor of a container/minimum-width rule.

## 3. Proposed Workbench-minimum hypothesis (to confirm empirically, not to assume)

Starting hypothesis for the spike to test, not a locked number: **640 CSS-px** for the Workbench
pane (Anchor + Canvas + existing 320px action rail at its current ratio, i.e. roughly a 320px
main-content sub-column plus the existing 320px rail). This is a starting point derived from the
existing rail minimum in §2, not a measurement — the spike's job is to raise or lower it based on
real rendering evidence in §5, not to confirm it.

## 4. Files touched by the spike (temporary vs. retained)

All spike code lives under a dedicated, clearly-labeled temporary path so it cannot be mistaken for
shipped UI, and is mounted through a **separate Vite entry outside the production app route tree** —
not a hash route inside `App.tsx`. This is the one safe approach for this spike; `App.tsx` is not
touched, and no hash route or guard inside the production router is introduced:

- **Temporary, deleted at spike close:**
  - `web/ophalo-app/dev-ui001-spike.html` — a second, standalone Vite HTML entry point (sibling to
    the existing `web/ophalo-app/index.html`), served only by `vite dev` at
    `/dev-ui001-spike.html`. It is never referenced by `index.html`, `App.tsx`, or any production
    route, and is not added to any build/rollup input list, so it cannot ship.
  - `web/ophalo-app/src/dev/ui001-workbench-width-harness.tsx` — the harness component, mounted by
    the HTML entry's own small `ReactDOM.createRoot` bootstrap script (also temporary, e.g.
    `web/ophalo-app/src/dev/ui001-spike-main.tsx`). It renders the real `RequestDetailDesktopLayout`
    (imported unchanged, not forked) inside a resizable container with a live width readout and a
    draggable/steppable width control, using the exact fixture named in §5.
  - All four files above (`dev-ui001-spike.html`, `ui001-workbench-width-harness.tsx`,
    `ui001-spike-main.tsx`, and the fixture file in §5) are deleted together at spike close, per the
    §7 go/no-go gate — none are retained past this spike.
- **Retained, spike output only:** an update to `docs/build-log/132-ui-001-queue-workbench-shell-preflight.md`
  §7 ("Remaining open item") recording the measured minimum and evidence, plus a new locked entry in
  `docs/ux-design/v2/keep-ui-design-model-v2.md` §13 filling in "the exact protected workbench
  minimum" once confirmed with Christian — that document edit happens only after this spike is
  reviewed and approved, not during the spike itself.
- **Not touched:** `RequestDetailDesktopLayout.tsx`, `DetailPanels.tsx`, `RequestDetailContent.tsx`,
  `App.tsx`, `RequestQueueNavigation.tsx` — the spike imports and observes existing components, it
  does not modify them.

## 5. Realistic populated-data fixture

Checked this pass: none of the five existing `KeepRequestDetailResult` records in
`web/ophalo-app/src/mocks/fixtures.ts` (`mockRequestDetails["mock-req-001"]` through
`"mock-req-005"`) has a populated service address — all five have
`serviceAddressLine1: null`/`serviceCity: null`/etc. — and all five share the same short
`businessName: "Apex Home Services"`. No single existing detail fixture is worst-case, so the
deferred choice is resolved by composing one, from values that already exist elsewhere in the
codebase rather than hand-written content:

- **New temporary file:** `web/ophalo-app/src/dev/ui001-spike-fixture.ts`, exporting
  `export const ui001SpikeFixtureDetail: KeepRequestDetailResult`.
- **Base record:** a full copy of `mockRequestDetails["mock-req-002"]`
  (`web/ophalo-app/src/mocks/fixtures.ts:850-925`) — the richest attention state among the five
  (`attentionLevel: "elevated"`, `attentionReason: "no_first_response"`, `priorityBand: "high"`,
  `needsShare: true`, two `contactActions`).
- **Overrides applied on top of that base**, each sourced from an existing value elsewhere in this
  codebase (not invented for this spike):
  - `businessName: "A Very Long Business Name That Would Otherwise Push Navigation Off Screen LLC"`
    — the existing long-name stress value from
    `web/ophalo-app/src/pages/request-detail/__tests__/RequestDetailHeader.businessName.test.tsx:31`.
  - `serviceAddressLine1: "1234 Oak Street"`, `serviceCity: "Memphis"`, `serviceState: "TN"`,
    `serviceZip: "38117"` — the existing populated address already present in this same file's
    request-summary fixture for `mock-req-002` (`fixtures.ts:410-413`), which the detail record
    itself omits.
- No other fields are changed; `description`, `events`, and `contactActions` stay as
  `mock-req-002` defines them, since that record's description and elevated-attention event history
  are already the fullest among the five.

## 6. Zoom evidence to capture

For each candidate Workbench-minimum width tested (starting from the §3 hypothesis, adjusted per
findings), capture screenshot + pass/fail notes at:

- 100% browser zoom
- 125% browser zoom
- 150% browser zoom

At each zoom level, record: whether any action label truncates/clips, whether any card requires
horizontal scroll, whether any touch target falls below 44px (general) / 48px (persistent field
actions), and whether the Anchor's phone/service-location/owner remain reachable without scrolling
past the original customer description (UI-005). A candidate width fails if any of these are
violated at any tested zoom level.

## 7. Go/no-go criterion for starting Step 3 (first functional Queue + Priority Preview slice)

Step 3 (build-log 132 §5) may start once:

1. A specific Workbench-minimum CSS-px value has passed the §6 evidence check at 100/125/150% zoom
   with the §5 populated-data fixture, with no fail condition above.
2. The resulting protected-workspace-minimum (Queue 320–360px + confirmed Workbench minimum +
   gutter) has been recorded in `keep-ui-design-model-v2.md` §13 as the locked value.
3. Christian has reviewed the spike evidence (screenshots + notes) and approved the number.
4. The temporary spike files (§4: `dev-ui001-spike.html`, `ui001-workbench-width-harness.tsx`,
   `ui001-spike-main.tsx`, and the §5 fixture file) have been deleted from the branch before Step 3
   work begins — the spike does not leave dev-only entries, routes, or fixtures in the tree.

If evidence shows no single width satisfies all roles/states cleanly (e.g. Owner/Admin's Office
Review content needs more room than Operator's), that is a **stop-and-surface** finding for
Christian, not a judgment call to average or guess past.

## 8. Explicit out-of-scope for this spike

- `RequestWorkbenchShell`, any pane-mode routing/rendering logic in `App.tsx`.
- Priority Preview implementation.
- Any change to `RequestQueueNavigation`'s layout (Step 4, not this spike).
- Any change to `RequestDetailDesktopLayout` itself — the spike measures the existing component
  as-is; adapting it for embedding is Step 5.
