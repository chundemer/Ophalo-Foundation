# OpHalo Keep PWA UI Quality System & Correction Contract

**Status:** Decision register — no implementation changes are authorized by this document until a decision is marked **Locked**.

**Date:** 2026-08-11

**Scope:** Authenticated Keep desktop/PWA first. Mobile design follows after the desktop information architecture and component behavior are locked.

---

## 1. Purpose

Keep is an operational product that businesses should trust with customer promises and pay for as essential software. The current product contains mature workflow logic, but its desktop presentation is not yet consistently communicating that level of quality. This document is the correction contract for closing that gap.

It is not a visual mood board, a replacement for the brand system, or a license to make isolated styling changes. It exists to turn the UI audit into explicit, reviewable product decisions before implementation begins.

The quality goal is a calm, high-throughput workspace where an operator can immediately understand:

1. where they are;
2. what needs action now;
3. the primary safe action for the current view; and
4. whether a remote action succeeded or needs recovery.

## 2. Relationship to Existing UX Documentation

Existing documents remain authoritative for their stated domains:

| Existing source | Continues to own |
|---|---|
| `ux-design-model-v1.md` | Brand foundation, tokens, voice, surface contracts, Production Richness Floor |
| `keep-component-spec.md` | Component recipes, scales, composition, state patterns |
| `keep-review-rubric.md` | Production-readiness review gate |
| `ux-design-decisions.md` | Locked decisions and their rationale |

This document owns the **PWA UI correction program**: its issue inventory, decisions to make, decision status, rollout order, and screen-level acceptance criteria. It must link to the other documents rather than duplicate their token values or component recipes.

When a decision below is locked, its durable rule belongs in the appropriate existing source of truth. This file then records the decision and links to that rule.

## 3. Guardrails Already Agreed

These are working guardrails for the correction program. They do not change existing locked UX doctrine.

- **Desktop/PWA first.** Do not solve desktop weaknesses by prematurely collapsing the interface into a mobile pattern.
- **Correct systems, not screenshots.** A page-level change must follow a reusable pattern or be documented as an exception.
- **Clarity before decoration.** Icons, cards, color, and motion must improve scanning, comprehension, or feedback.
- **No render-time rewriting of customer-entered data.** Do not title-case or otherwise normalize names, companies, cities, or free text just to make a screen look tidier.
- **State is visible.** A filter, loading transition, asynchronous write, success, failure, stale result, and lack of access must be understandable without inference.
- **One dominant task per view.** A view has one primary workflow action unless a documented workflow exception requires otherwise.
- **No implementation by implication.** “Recommended” is not “locked.” Code begins only after the relevant decisions are locked.

## 4. Audited Problems

The following problems have been identified from the Requests, Price Book, and Assemblies PWA surfaces.

1. Global and page-level calls to action can compete.
2. Navy and teal action treatments do not yet have an unambiguous, consistently applied product meaning.
3. Workspace surfaces, content widths, and use of desktop canvas are inconsistent.
4. Page hierarchy is too flat: title, tabs, filters, data, and metadata often have similar weight.
5. Requests contains the right information but does not yet provide an optimal scanning order or density.
6. Status, urgency, next action, and contextual metadata do not share a fully locked visual grammar.
7. Price Book and Assemblies do not yet feel like one mature, high-confidence data workspace.
8. Active filter state and recovery are inconsistent, particularly in Price Book.
9. Mutation success/failure feedback is fragmented; copy feedback is local but system-wide write feedback is not standardized.
10. Getting Started has no defined lifecycle after setup is complete.
11. Empty, loading, error, stale-data, and permission states lack one cross-product contract.
12. Typography, spacing, controls, icons, and density need a clear operational standard.
13. Mobile navigation is a future information-architecture concern and must be designed from the locked desktop model, not treated as an afterthought.

## 5. Decisions to Make

Each decision must end as **Locked**, **Deferred**, or **Rejected**. A locked decision requires an owner, rationale, and target documentation location.

### D1. Product-quality target and first-release scope

**Status:** Locked — 2026-08-12

**Decision:** Phase one is limited to the authenticated desktop/PWA **Requests queue**, **Price Book: Catalog Items**, and **Price Book: Offerings & Assemblies**. Request Detail, Settings, public/customer surfaces, and mobile are explicitly deferred.

Expansion beyond these three surfaces is gated on internal review only: product-owner review plus D16's rubric, breakpoint, keyboard, and accessibility checks. A pilot/customer review gate will be added when an active client or pilot exists; it is not a phase-one prerequisite.

**Rationale:** These surfaces expose the most visible hierarchy, density, table, filtering, and CTA issues. A narrow first release establishes reusable patterns without turning the effort into a whole-app restyle. Internal review is the credible validation path before a client/pilot exists, while the future customer-review gate is intentionally recorded rather than silently assumed.

**Phase-one acceptance criteria:**

1. Each view has one unambiguous dominant CTA, or a documented workflow exception.
2. Page hierarchy is clear: title/context → navigation or tabs → filters → content.
3. Desktop composition uses intentional, bounded workspace surfaces; content does not appear to float accidentally on a wide canvas.
4. Action, status, urgency, and metadata roles are consistent across the three surfaces.
5. Applied filters are visible and have a clear/reset path.
6. Loading, empty, filtered-empty, remote-error, permission, and mutation-feedback states are explicit and recoverable.
7. All interactive controls are keyboard-accessible and expose visible focus.
8. The correction is reviewed with realistic, populated data at agreed desktop widths—not only sparse or empty data.
9. Existing request, catalog, and assembly workflow behavior has no regression.
10. A first-time viewer can reasonably perceive every corrected surface as a finished, paid operational product: no placeholder-feeling density, accidental spacing, unstyled state, or prototype-like presentation.

### D2. Global navigation and CTA hierarchy

**Status:** Locked — 2026-08-12

**Decision:** Each desktop workspace has one dominant page-level CTA. Global **New Request** is a cross-product utility, not a competing page CTA.

| Phase-one workspace | Dominant CTA | Global New Request |
|---|---|---|
| Requests | New Request | Filled navy; it is the natural workspace action. |
| Price Book: Catalog Items | Add catalog item | Absent from the desktop header. |
| Price Book: Assemblies | Add assembly | Absent from the desktop header. |

Removing the global utility from Price Book does not remove access to Requests: Requests remains a primary navigation destination.

**Boundaries:**

- D4 owns color and token semantics. D2 defines hierarchy only; any navy/teal fill-or-outline recipe remains governed by D4.
- D11 owns unsaved-change confirmation and close behavior. Global utilities and navigation must respect the D11 rule once it is locked.
- D15 owns mobile CTA placement and mobile navigation. D2 establishes the desktop hierarchy semantics that D15 must reconcile with thumb-reach patterns.
- D3 owns whether Getting Started remains a primary navigation destination. While it is present, its page-level CTA follows this same global-utility subordination rule.
- Request Detail and Settings are outside phase one under D1. They inherit this hierarchy when their correction work enters scope; D2 does not authorize implementation changes to them now.

**Rationale:** Two equally dominant buttons imply two equally preferred next steps. On a catalog workspace, creating a request is valid but not the task the page is asking the user to perform. The rule preserves quick access in Requests, prevents Price Book CTA conflict, and avoids re-deciding color, unsaved-change, mobile, or onboarding lifecycle policy in the wrong decision area.

### D3. Getting Started lifecycle

**Status:** Needs decision

**Question:** When is setup “complete,” and where do onboarding, help, and recurring setup tasks live afterward?

**Recommendation:** Show Getting Started only while setup is materially incomplete. After completion, remove it from daily primary navigation and preserve relevant guidance contextually in Requests and/or a help/account destination.

**Reasoning:** Onboarding is useful context during activation but is dead weight in an established operator workflow. Hiding it must not make support or later configuration undiscoverable.

**Decision must specify:** completion criteria, display rules by role, dismissal/reappearance behavior, and destination for help/setup tasks.

### D4. Semantic action and color roles

**Status:** Needs decision

**Question:** Do we retain the current role-based button hierarchy or revise it, and what product meaning does each role carry?

**Recommendation:** Preserve a role-based system, then make it more explicit: teal for Keep communication/affirmative workflow commitments; navy for navigation, structural emphasis, and defined global utilities; neutral outline for secondary actions; red only for destructive/error; amber for attention; green for successful/healthy states.

**Reasoning:** The existing UX decisions already distinguish Keep communication teal from navy page-level actions. The correction should clarify and enforce the rule—not replace it with “every primary action is teal” without considering context.

**Decision must specify:** exact role names, component mapping, exceptions, and the source document that owns the token/recipe details.

### D5. Desktop workspace shell and surface depth

**Status:** Needs decision

**Question:** What are the standard desktop content widths, page regions, and rules for canvas versus elevated surfaces?

**Recommendation:** Define a reusable shell with bounded content, page header, optional tabs, grouped toolbar, data/content region, and pagination or footer. Use white surfaces for meaningful groups (forms, filter groups, tables, operational panels), not as a blanket card treatment for every element.

**Reasoning:** The goal is intentional composition and readable work zones, not a card-heavy dashboard. This resolves unconstrained empty canvas and flat, strokes-on-canvas layouts while respecting the Production Richness Floor.

**Decision must specify:** shell variants, max-width guidance, surface boundaries, elevation rules, and page-level exceptions.

### D6. Data-workspace pattern: Price Book and Assemblies

**Status:** Partially locked — Price Book workspace controls, 2026-08-12

**Question:** What common data-grid/list pattern should catalog, assemblies, and future administrative collections use?

**Recommendation:** Standardize page context, tabs where needed, grouped filters, explicit applied-filter summary/reset, a bounded table surface, structured table headers, row hover/focus/open affordance, semantic status treatment, clear pricing/value emphasis, pagination, and differentiated empty states.

**Reasoning:** Price Book should communicate a managed operational catalog rather than a static export. Reusing the pattern prevents Catalog Items and Assemblies from drifting apart.

**Decision must specify:** mandatory columns/row actions, responsive behavior to defer or retain, table versus card-list threshold, and whether assemblies need an always-visible explanatory cue.

**Approved Catalog Items correction baseline (desktop/PWA):**

- Price Book has one contextual dominant CTA: **Add catalog item** on Catalog Items and **Add assembly** on Offerings & Assemblies. The global desktop New Request utility is absent on Price Book routes; Requests remains primary navigation.
- The populated Catalog Items view provides a truthful current-page result count, a bounded table surface, readable table headers, row-open affordance, price emphasis, and semantic status badges.
- A populated search field has one custom accessible clear control; native browser search-cancel UI must not create a duplicate affordance.
- Applied filters remain visible with a Reset all recovery path; filtered-empty content explicitly explains that filters produced no results.
- For long desktop lists, Price Book uses one CSS-native **sticky workspace bar**, not a conditional JavaScript pop-in CTA. It keeps the user’s active workspace controls available while the page title/subtitle scroll away.
- The sticky workspace bar includes Price Book section tabs, search, category, status, applied-filter context/reset, and the contextual create CTA. It must sit below persistent global navigation when that navigation is sticky; its actual offset must use the application’s established header height/token, not a magic number.
- Do not add a table-footer “Add another catalog item” action in this pass. Reconsider it only after reviewing the sticky workspace bar with a paginated catalog.

**Deferred within D6:** Assemblies-specific explanatory guidance, exact table/footer pagination treatment, and mobile adaptation remain open.

**Assemblies creation/editing relationship (direction locked; implementation deferred until Step 2
pricing-summary work is resolved):**

- **Create** remains a slide-over drawer launched from the Assemblies workspace, preserving the
  list context for fast office entry.
- **Existing assembly management** remains an Assembly Detail page because it owns lifecycle,
  eligibility, associated-item management, and the future pricing/profitability summary.
- This is one product workflow expressed at two containment levels, not two different editor
  concepts. Both must share the same learnable editor anatomy: **Name → Primary catalog item →
  Price treatment → Associated items → Save/cancel and validation**.
- On the detail page, **Edit** must enter an explicit Edit offering/assembly mode using the same
  field labels, section order, catalog-picker behavior, price-treatment explanation, and validation
  language as creation. Lifecycle controls such as Inactivate remain outside that editor mode.
- Do not convert existing management into a drawer merely for visual symmetry. The future header
  pricing/margin summary establishes the final detail-page hierarchy before this convergence is
  implemented.

**Assembly Detail pricing-summary preflight (Step 2, authorized 2026-08-13; no implementation yet):**

- Price summaries are Owner/Admin header context and must be delivered by an authoritative backend
  read model, not frontend price arithmetic.
- For **Summed**, base calculated sell price is the current standalone sell price of the primary
  item plus every required associated item’s current standalone sell price × quantity. A missing
  standalone sell price on any required line yields **Price needs review**, never $0.00.
- For **All-inclusive**, package price is the current standalone sell price of the primary item.
  No separate assembly override/package-price field exists or is implied. A missing standalone
  price on the primary yields **Price needs review**.
- Business cost remains optional: missing cost never blocks catalog/assembly creation, activation,
  or customer-price completeness. It produces a distinct Owner/Admin profitability state such as
  **Margin needs cost review**, never a price error.
- The preflight must propose DTO/query/test boundaries and identify whether the existing Assembly
  Detail endpoint is extended or a dedicated summary endpoint is warranted. It must also surface,
  without deciding, optional-line totals and whether the first release displays only margin
  readiness/missing-cost count or full gross-profit/margin/markup values.

**Step 2 preflight review — implementation contract (locked 2026-08-13):**

- Extend the existing Owner/Admin Assembly Detail endpoint/read model; do not create a dedicated
  pricing-summary endpoint. The summary is detail-header context and uses the same referenced
  catalog-item set already loaded for assembly eligibility.
- No migration is required. Project the current price line for every referenced item—its pricing
  mode, sell-price snapshot, and cost snapshot. Cost must be projected independently of whether a
  line has `StandalonePrice`; a `NoStandalonePrice` item may still carry business cost.
- Add one nested, server-computed pricing-summary object to Assembly Detail. It owns price status,
  nullable calculated sell price, margin status, missing-cost count, and structured price/margin
  review reasons. The frontend must not derive arithmetic or review reasons from individual lines.
- **Summed:** calculated customer sell price includes primary + every required associated item ×
  quantity. Missing standalone sell price on any of those lines is **Price needs review**.
- **All-inclusive:** package price is the primary item’s standalone sell price; associated sell
  prices are not part of the customer package price. Missing primary standalone sell price is
  **Price needs review**. No separate package-price override field is introduced.
- For **both** price treatments, margin readiness includes the primary and every required
  associated item. All-inclusive component costs are included in the cost basis even though their
  sell prices are not separately charged. Missing cost yields **Margin needs cost review** and
  never changes price completeness or activation eligibility.
- Optional associated items are excluded from phase-one calculated price, missing-cost count, and
  totals. Do not add optional-add-on totals in this release.
- Review reasons must identify the affected catalog item by ID and display name. Keep lifecycle
  eligibility, price completeness, and margin completeness as separate issue groups.
- The repair loop is URL-addressable in the app’s existing hash router:
  `#/pricebook/{catalogItemId}?returnToAssembly={assemblyId}` →
  `#/pricebook/assembly/{assemblyId}`. Item Detail uses **Back to assembly** as the safe default
  label; an assembly name may be used only when authoritatively available. On return, Assembly
  Detail refetches the server detail summary. Do not introduce inline Catalog Item editing on
  Assembly Detail.
- Deliver in two reviewable batches: **(1) backend/read contract and API/frontend types with
  tests; no visible UI**, then **(2) Assembly Detail repair UI** (header summary, grouped linked
  reasons, per-row indicators, and repair-loop navigation).

### D7. Search, filters, and result context

**Status:** Needs decision

**Question:** What is the shared filter interaction contract across PWA workspaces?

**Recommendation:** Every non-default search/filter state must be visible near the controls and recoverable in one action. Search fields use an inline clear affordance when populated; workspaces show applied criteria or a filter count plus Reset all; zero results distinguish filtered-empty from truly empty.

**Reasoning:** Without visible filter context, an empty table looks like a broken product. Requests already implements part of this model; Price Book should converge on it.

**Decision must specify:** submitted versus draft search behavior, when filtering applies, applied-state wording, reset behavior, and pagination reset rules.

### D8. Requests queue scanning and row density

**Status:** Needs decision

**Question:** What exact information order and density should a Request row support on desktop?

**Recommendation:** Lock the scan order as: highest-priority exception/urgency → request/customer identity → the next appropriate action → concise request context → operational metadata. Limit a row to one status indicator and one highest-priority exception indicator; use icons only for assignment, time, or location when they improve scanning.

**Reasoning:** Requests is an action queue, not a dashboard. Operators need to triage quickly without losing the original customer context or being overwhelmed by badge noise.

**Decision must specify:** which metadata is always visible, what moves to detail, grouping rules, action-bar rules, and row-height/density targets.

### D9. Status, urgency, and metadata grammar

**Status:** Needs decision

**Question:** What is the shared visual language for lifecycle status, attention, exception, success, inactive, and contextual metadata?

**Recommendation:** Define a finite semantic vocabulary with one component recipe per meaning. Status describes lifecycle; exception describes the highest-priority risk; metadata remains visually quiet. Do not use status colors as general decoration or button fills.

**Reasoning:** A user should learn meaning once and recognize it in Requests, Price Book, Assemblies, details, and settings.

**Decision must specify:** badge content, dot/icon usage, color role, priority rule, and when plain text is preferable to a badge.

### D10. Mutation feedback and write safety

**Status:** Needs decision

**Question:** What feedback does a user receive before, during, and after a remote write?

**Recommendation:** Establish a global notification pattern for completed/failed asynchronous writes, paired with local pending state and disabled/retry-safe controls. Use inline validation adjacent to fields; do not use a toast as validation. Avoid notifications for navigation and instant local UI changes.

**Reasoning:** Silent writes encourage duplicate submissions and reduce trust. A toast alone is insufficient if the originating control still looks actionable or field errors lack context.

**Decision must specify:** notification placement, duration, accessible announcement, success/error copy format, concurrency/conflict treatment, and which mutation classes are exempt.

### D11. Form, modal, drawer, and full-page boundaries

**Status:** Needs decision

**Question:** When should a mutation occur inline, in a modal, drawer, or dedicated page?

**Recommendation:** Use inline for a small, low-risk field change; modal for focused self-contained confirmation/action; drawer for multi-field create/edit work that benefits from keeping the workspace visible; full page for complex or consequential multi-section configuration.

**Reasoning:** Consistent containment makes the product predictable and limits accidental workflow complexity.

**Decision must specify:** confirmation requirements, close/unsaved-change behavior, validation behavior, and current exceptions.

### D12. Operational typography, iconography, and control density

**Status:** Needs decision

**Question:** How should the established font families and components create hierarchy without making dense workspaces feel ornamental?

**Recommendation:** Use Source Serif 4 selectively for page-level hierarchy and high-value human/identity anchors; use Inter for controls, tables, metadata, and dense operations; restrict Poppins to prebuilt logo assets. Establish fixed roles for type, spacing, control height, icon size, badge size, and table density.

**Reasoning:** Typography and spacing create most perceived quality. Requiring serif for every heading/card title would reduce density and make hierarchy mechanical; its use should be purposeful.

**Decision must specify:** typography role map and the existing document where exact scale/recipes will be locked.

### D13. Shared primitives and implementation boundaries

**Status:** Needs decision

**Question:** Which UI primitives must exist before page corrections start, and which can be introduced during the first surface implementation?

**Recommendation:** Inventory existing primitives, then establish only the missing reusable pieces required by the decisions above—likely toast/notification, applied-filter summary, workspace shell, data-table structure, status badges, and state panels.

**Reasoning:** A broad component-library rewrite would delay visible progress. Duplicating page-local patterns would recreate the drift this correction is intended to stop.

**Decision must specify:** minimal primitive set, ownership, adoption plan, and test/accessibility requirements.

### D14. State matrix and recovery behavior

**Status:** Needs decision

**Question:** What does every workspace show for loading, empty, filtered-empty, remote error, stale data, permission denial, and completed action?

**Recommendation:** Define a state matrix with one purpose, copy standard, visual treatment, and recovery action for each state. “No data” and “no results under the current filters” must never share the same presentation.

**Reasoning:** State handling is where paid operational software earns trust; it is not edge-case polish.

**Decision must specify:** states, recovery affordances, retry rules, skeleton/progress rules, and accessibility announcements.

### D15. Mobile adaptation and navigation

**Status:** Deferred until desktop decisions D2, D5–D12 are locked

**Question:** Which destinations and actions earn persistent thumb access on mobile, and what is the mobile equivalent of the desktop workspace?

**Recommendation:** Preserve the option for a persistent bottom navigation, but do not lock its tabs yet. Design it from real mobile frequency and task criticality after desktop hierarchy is validated.

**Reasoning:** Mirroring the desktop navbar into three fixed mobile tabs risks hiding Capture, detail actions, or account functions that field users need more often.

**Decision must specify:** tab destinations, capture placement, overflow behavior, safe-area behavior, and which desktop data patterns collapse or change.

### D16. Review gate, measurement, and rollout

**Status:** Needs decision

**Question:** How will we review, validate, and release the correction work without reintroducing local drift?

**Recommendation:** Require the existing review rubric plus this contract’s screen-specific acceptance criteria, desktop visual review at agreed breakpoints, keyboard/accessibility verification, and targeted operator feedback before applying the pattern to more surfaces.

**Reasoning:** “Looks better” is not a durable acceptance criterion. A small, reviewed rollout catches workflow regressions before a full redesign spreads them.

**Decision must specify:** reviewers, required evidence, rollout sequence, rollback posture, and user/pilot feedback method.

## 6. Issue-to-Decision Map

| Audited problem | Governing decision(s) |
|---|---|
| Competing CTAs | D2 |
| Action color ambiguity | D4, D9 |
| Inconsistent surfaces and empty canvas | D5, D12 |
| Flat hierarchy | D5, D8, D12 |
| Request scan/density | D8, D9, D12 |
| Price Book/Assemblies maturity | D6, D7, D9 |
| Filter visibility | D7, D14 |
| Fragmented write feedback | D10, D11, D14 |
| Getting Started lifecycle | D3, D2 |
| Inconsistent empty/loading/error states | D14 |
| Missing shared system/primitive boundaries | D13, D16 |
| Future mobile navigation | D15 |

## 7. Proposed Decision Order

Do not decide page styling before its governing system decisions.

1. D1 — scope and quality target
2. D2–D5 — navigation, onboarding, semantic action roles, workspace shell
3. D7, D9–D14 — filters, status grammar, feedback, containment, typography, primitives, state matrix
4. D6 and D8 — Price Book data workspace and Requests queue patterns
5. D16 — review/release gate
6. Implement Requests and Price Book in the order locked under D1
7. D15 — mobile adaptation

## 8. Screen-Level Backlog (Not Yet Authorized)

No item below should be implemented until the relevant decisions above are locked.

| Surface | Candidate correction work | Depends on |
|---|---|---|
| Requests | Queue scan order, metadata density, priority/exception grammar, row action hierarchy, grouped states | D2, D4, D5, D8–D10, D12, D14 |
| Price Book: Catalog Items | Workspace shell, filters, filter summary/reset, table/row treatment, pricing/status scan anchors | D2, D4–D7, D9, D12–D14 |
| Price Book: Assemblies | Shared data pattern, eligibility communication, explanatory context, row/empty states | D4–D7, D9, D12–D14 |

## 9. Decision Log

Add a row when a decision is made. Link the durable rule after it is added to its authoritative UX document.

| ID | Status | Decision | Date | Owner | Durable rule location |
|---|---|---|---|---|---|
| D1 | Locked | Phase one: Requests, Catalog Items, and Assemblies; internal-review gate until a client/pilot exists | 2026-08-12 | Product owner | This document §5 D1 |
| D2 | Locked | One dominant desktop CTA per workspace; New Request is primary in Requests and absent from Price Book header | 2026-08-12 | Product owner | This document §5 D2 |
| D3 | Needs decision | — | — | — | — |
| D4 | Needs decision | — | — | — | — |
| D5 | Needs decision | — | — | — | — |
| D6 | Needs decision | — | — | — | — |
| D7 | Needs decision | — | — | — | — |
| D8 | Needs decision | — | — | — | — |
| D9 | Needs decision | — | — | — | — |
| D10 | Needs decision | — | — | — | — |
| D11 | Needs decision | — | — | — | — |
| D12 | Needs decision | — | — | — | — |
| D13 | Needs decision | — | — | — | — |
| D14 | Needs decision | — | — | — | — |
| D15 | Deferred | Mobile adaptation after desktop lock | 2026-08-11 | — | — |
| D16 | Needs decision | — | — | — | — |
