# BL148 — GAP-054: Authenticated app-shell navigation discovery

**Status:** Discovery complete; decisions locked in [ADR-499](../decisions/ADR-499-authenticated-app-shell-account-menu.md)
(2026-09-07). Slice 054-1 implemented, awaiting diff review + browser verification.

## Slice 054-1 — delivered (pending review)

Frontend only (`web/ophalo-app`), no API/domain/migration.

- **New** `src/components/layout/AccountMenu.tsx` — workspace-name-labelled menu button; identity
  header; Owner/Admin Business Settings group (`getBusinessSettingsSections`) deep-linking the
  existing `#/settings?section=…` routes; terracotta Sign out. Follows the `KeepSplitButton`
  dropdown pattern; adds Escape-restores-focus and ArrowUp/Down/Home/End roving.
- **New** `src/components/layout/__tests__/AccountMenu.test.tsx` — 9 tests.
- **Modified** `src/App.tsx` — `getNavItems` drops `settings` (Requests + entitled Price Book
  only); removed `PHONE_OMITTED_NAV_IDS`/`phoneNavItems`; desktop identity text + loose Sign out
  → `<AccountMenu>`; Settings nav pill removed; added `navigateToSettingsSection`; `MobileNavMenu`
  now gets full `navItems` + `sections` + `onNavigateSection`.
- **Modified** `src/components/layout/MobileNavMenu.tsx` — optional `sections`/`onNavigateSection`;
  renders the Business Settings group; Sign out recoloured to `--ophalo-accent`.
- **Modified** tests: `src/App.test.tsx` (getNavItems expectations; phone-nav describe inverted to
  "carries Price Book and Business Settings"; account-menu open step added to sign-out and
  V2-shell-Settings tests), `src/components/layout/__tests__/MobileNavMenu.test.tsx` (+2 tests).

Verification: `tsc --noEmit` clean; `check:tokens` pass; focused suites
(`src/components/layout`, `src/App.test.tsx`, `Settings.v2Shell`, `App.actualWorkRoute`,
`RequestsWorkspaceHeader`) — 68 tests pass. Browser verification completed 2026-09-07 (Christian):
desktop + narrow; Owner/Admin/Operator/Viewer; keyboard-only menu operation; active-route
treatment — per the GAP-054 tracker requirement.

## Resolved decisions (2026-09-07)

| # | Resolution |
|---|------------|
| D1 | **A1** — account menu; standalone Settings pill removed; Business Settings group moves into the menu. |
| D2 | **Yes** — `MobileNavMenu` gains the Business Settings group + Price Book; the 2026-08-26 `PHONE_OMITTED_NAV_IDS` omission is deliberately reversed (recorded in ADR-499 §5). |
| D3 | **B1** — Price Book stays a first-class top-level pill (ADR-496 §6). |
| D4 | Workspace **name displayed, never switched** (ADR-497 rule 8). In-session switch = post-native-mobile candidate. Workboard wording softened from "switcher" to "label". |
| D5 | Help & Updates menu entry is built by **GAP-038** (now sequenced immediately after GAP-054), not here. |
| D6 | F6 (New Request CTA suppression asymmetry) is a **separate later slice**, not folded in. |

Primitive question resolved in preflight: no new shared primitive. `AccountMenu` follows the
existing `src/components/keep/KeepSplitButton.tsx` dropdown pattern (self-contained open state,
outside-pointer + Escape close, `role="menu"`/`"menuitem"`, `aria-haspopup`/`aria-expanded`), plus
added menu keyboard semantics (Escape restores focus to trigger; Up/Down/Home/End move).

**Scope reference:** [workboard](../workboard.md) item 1; [pilot-readiness-bug-tracker](../pilot-readiness-bug-tracker.md)
GAP-054 (P2). Session-log guardrail: *do not expand into entitlement or commercial-workflow work.*

**Reference mock:** Christian's 2026-09-06 screenshot — an expanded business/account menu (State 1
enrolled, State 2 unenrolled). Treated as design input, not an approved contract; several elements
sit outside GAP-054 scope (see §4).

---

## 1. Current shell — evidence

All shell markup is in `web/ophalo-app/src/App.tsx` (`AppShell`, 684 lines) plus
`src/components/layout/MobileNavMenu.tsx`. There is no dedicated shell component or ADR.

### Desktop (`md:` / 768px and up) — `App.tsx:392-500`
One horizontal `<header>` (`h-14`), left to right:
- Keep lockup button → explicit Requests entry (`navigateToRequests`).
- Nav pills: **Requests** (with unread count badge), **Price Book** (only `owner`/`admin` **and**
  `priceBookEntitled`), **Settings** (only `owner`/`admin`). Active pill styled from `activeNavId`
  (`App.tsx:327-330`).
- Right cluster (`ml-auto`): static text `"{userName | businessName} · {Role}"` (`App.tsx:467-476`),
  a bare **Sign out** button, and the **New Request** primary button (suppressed on the three Price
  Book routes, which carry their own dominant CTA — `App.tsx:486-497`).

### Mobile (below 768px) — `App.tsx:363-389`, `MobileNavMenu.tsx`
- Minimal top bar: lockup + hamburger (`aria-expanded`, `aria-label="Open navigation menu"`).
- `MobileNavMenu` (a `KeepModal`): role-filtered nav items, role label, Sign out.
  **Price Book and Settings are unconditionally omitted** from the phone menu
  (`PHONE_OMITTED_NAV_IDS`, `App.tsx:31`; locked/corrected 2026-08-26). Rationale in code: there is
  no viewport where the phone menu opens and the desktop nav is also unavailable.
- Fixed **New Request FAB**, bottom-right, suppressed on `detail` + Price Book routes
  (`App.tsx:628-643`).

### What already works well
- `KeepModal` (`src/components/keep/KeepModal.tsx`) gives the mobile menu a real dialog: initial
  focus, Tab/Shift-Tab focus trap, Escape-to-close, focus restoration to the trigger.
- Active-route treatment is centralised in one `activeNavId` derivation and reused by both surfaces.
- Role/entitlement gating is server-authoritative; nav visibility is discovery-only (ADR-496 §7).
- Brand lockup routes to an explicit Requests entry rather than `history.back()`.
- Test coverage exists for the CTA-suppression matrix, `name · role` rendering + invited-name
  fallback, sign-out, brand nav, and the V2 top-nav-shell-covers-Settings behavior
  (`src/App.test.tsx`).

---

## 2. Evidence-backed findings

| # | Finding | Evidence | Severity |
|---|---------|----------|----------|
| F1 | **No account/profile menu.** The desktop identity area is static text plus a loose Sign out button. Sign out has no grouping, no confirmation, and is a peer of navigation. There is no home for account-level items (workspace identity, business settings entry, Help/Updates from GAP-053). | `App.tsx:467-485` | P2 |
| F2 | **Settings is a top-level nav pill competing with Requests.** ADR-428 defines Settings as three business-config sections (Public Link & Profile, Response Policy, Team — `Settings.tsx:17-20`). These are configuration, not a primary workspace, yet Settings sits at the same altitude as Requests in the top nav. The reference mock moves them into the account menu. | `App.tsx:451-462`; `Settings.tsx` | P2 |
| F3 | **Mobile cannot reach Settings or Price Book at all.** `PHONE_OMITTED_NAV_IDS` removes both from the phone menu unconditionally. The in-code rationale ("no width where the phone menu opens and desktop nav is absent") is now questionable: a 767px-wide tablet in portrait gets the phone menu and no other path to Settings/Price Book. An Owner on a phone genuinely cannot open Team or Response Policy. | `App.tsx:26-31`, `App.tsx:190` | P2 (product decision — was a deliberate lock) |
| F4 | **`activeNavId` has no "Settings" state when the account menu owns Settings.** If Settings moves under an account menu, the current `route.page === "settings" → activeNavId = "settings"` branch and the Settings pill's active styling need a replacement affordance (e.g. an "active" indicator on the account-menu trigger). | `App.tsx:327-330`, `App.tsx:451-462` | P3 (follows from F2) |
| F5 | **No keyboard model for the identity cluster.** The account area is not a menu, so there is nothing to arrow through; but once it becomes a menu (F1) it needs the standard menu-button pattern (Enter/Space/Down opens, Escape closes and restores focus, Up/Down move, Tab exits). `KeepModal` gives a trap but a dropdown menu is not a modal — a menu should not trap Tab. Need a distinct lightweight popover pattern or a deliberate decision to reuse the modal on mobile only. | `KeepModal.tsx:37-39`; no popover primitive exists (`rg popover/dropdown src/components` → none) | P2 |
| F6 | **`New Request` global CTA vs page-local CTAs is inconsistent across breakpoints.** Desktop suppresses the header CTA on 3 Price Book routes; mobile suppresses the FAB on `detail` + the same 3 routes. `detail` keeps the desktop header CTA but drops the FAB. The rule ("drop the global CTA where a route has its own dominant CTA") is sound but the two lists differ and `detail` is treated asymmetrically. | `App.tsx:486-497` vs `App.tsx:628-643` | P3 |
| F7 | **`businessName` is the identity fallback but there is no persistent workspace label otherwise.** For a user *with* a name, the workspace/business they are currently in is not shown at all on desktop (`userName · Role` wins). A user in two workspaces (ADR-497 founder case) has no on-screen confirmation of which one they are in. | `App.tsx:467-476` | P2 |
| F8 | **`#/getting-started` is dead (correct) but nothing documents the removal at the shell layer.** ADR-496 §4 removed the permanent Getting Started nav destination; the route now falls through to Requests (`App.test.tsx:649`). The reference mock still shows a "Getting Started" pill — **the mock contradicts ADR-496 here** and that pill must not be built. | ADR-496 §4; `App.test.tsx:649` | note only |

---

## 3. Target options for the two genuine design choices

### Choice A — Account / business menu (addresses F1, F2, F5, F7)

**A1 — Grouped account menu, Settings sections move in (matches reference mock).**
Replace the static `name · role` text + loose Sign out with a menu-button. Trigger shows workspace
identity (`businessName` + a caret) and, secondarily, `userName · Role`. Menu contents, scoped to
GAP-054:
- Header: business name + role (no plan/seat/trial data — see §4).
- **Business Settings** group → Company Profile & Public Link, Response Policy (SLAs),
  Team Seats & Permissions — each deep-links to the existing `#/settings?section=…` route.
- **Sign out** (visually separated, terracotta per mock).
- Owner/Admin only (same gate as today's Settings pill). Operator/Viewer see only identity + Sign out.
- Remove the top-level **Settings** pill; keep **Price Book** and **Requests** pills.

*Pros:* de-clutters the nav bar to true workspaces; gives Sign out a home; one obvious place for
account-level items; directly matches the mock's business-settings grouping.
*Cons:* one more click to Settings sections; needs a new popover primitive (F5); needs the mobile
equivalent (fold the same group into `MobileNavMenu`, which resolves F3).

**A2 — Minimal account menu, Settings stays a top-level pill.**
Add the menu-button only for identity + Sign out (+ future Help/Updates). Leave Settings where it is.
*Pros:* smallest change; no route-altitude debate.
*Cons:* does not resolve F2; nav bar still carries a config surface at workspace altitude; mock
grouping not realised.

**A3 — Account menu with Settings, plus keep a single "Settings" pill as a shortcut.**
Hybrid. *Cons:* two paths to the same place; more surface to test; not obviously better than A1.

**Recommendation: A1**, with the mobile menu absorbing the same Business Settings group (which also
resolves F3). This is still one bounded slice if the popover primitive is small.

### Choice B — Price Book placement (F2 context; ADR-496 §6)

ADR-496 §6: Price Book "is visible immediately to entitled Owner/Admin users… remains secondary to
Requests… The package is **discovered and selected commercially through the account/subscription
area, not through Business Settings**; Business Settings configures an already-entitled package."

**B1 — Price Book stays a top-level pill (status quo).**
Preserves ADR-496 immediate discoverability literally. Account menu is business-settings only.
*Recommendation unless findings below change it.*

**B2 — Price Book moves under the account menu.**
Would violate the spirit of ADR-496 §6 ("visible immediately", "secondary to Requests" — a menu is
less discoverable than a pill) and blurs into the deferred commercial area. **Not recommended.**

**B3 — Price Book pill stays; *Price Book settings/config* attaches under the account menu's
Business Settings group later.**
The session-log phrase "entitled Price Book settings attachment" most plausibly means this: the
*configuration* entry point for an already-entitled package belongs with Business Settings (ADR-496
§6), while the Price Book *workspace* stays a top-level pill. There is no Price Book settings surface
today, so this is a forward-looking hook, not this slice's work.

**Recommendation: B1 for this slice.** Keep Price Book top-level. Note B3 as the intended home for a
future Price Book config surface. Do not build B2.

---

## 4. Explicitly OUT of GAP-054 scope (session-log guardrail)

The reference mock's **"Subscription & Entitlements"** group is deferred commercial-workflow work
and must **not** be built here:
- "Manage Subscription & Billing" / "Subscribe" — self-service billing is deferred (ADR-496 §6,
  GAP-070/071).
- "Trial (12 days left)" / "Growth Plan" / "1/3 seats used" — plan, trial, and seat-plan data have
  no V1 source; entitlement stays on the authorized internal path (ADR-496 §6, ADR-496 Locked).
- "Optional Modules & Add-ons" — GAP-070/071, pending a Decision Queue entry.
- "Getting Started" pill — removed by ADR-496 §4; the mock is stale on this point.

`seatUsage` *is* already server-provided inside the Team section (ADR-383) — it stays there, not in
the menu header.

Also out: in-session workspace switching (**ADR-497 rule 8** — "sign out → sign in → choose
workspace is sufficient for V1"). The account menu may *display* the current workspace name (F7) but
must not offer a switch action. If a persistent switcher is wanted, it needs an amendment to
ADR-497 first — flagged to Christian, not assumed.

---

## 5. Proposed bounded slice (for approval — not yet started)

**Slice 054-1 — Account menu + Settings regrouping (Option A1 + B1):**

Production files (est. 4–6):
1. `src/components/layout/AccountMenu.tsx` — new; menu-button + popover, Business Settings group
   (Owner/Admin), identity header, Sign out. Reuses existing `#/settings?section=…` navigation.
2. `src/components/keep/KeepPopover.tsx` *or* `KeepMenu.tsx` — new; lightweight non-trapping
   dropdown primitive (menu-button ARIA pattern, Escape + outside-click close, focus restore). Only
   if no existing primitive is adaptable — confirm in preflight.
3. `src/App.tsx` — replace the desktop identity cluster + Sign out with `<AccountMenu>`; remove the
   Settings nav pill and its `activeNavId === "settings"` pill styling; add an active affordance on
   the menu trigger (F4).
4. `src/components/layout/MobileNavMenu.tsx` — add the Business Settings group for Owner/Admin;
   revisit `PHONE_OMITTED_NAV_IDS` for Settings (F3) — **needs Christian's sign-off since the phone
   omission was a deliberate 2026-08-26 lock.**
5. Tests: `AccountMenu.test.tsx` (role gating, section deep-links, keyboard, Sign out),
   `KeepPopover`/`KeepMenu` primitive test, `App.test.tsx` updates (Settings pill gone, menu
   present), `MobileNavMenu.test.tsx` updates.

Architecture layers: frontend only (`web/ophalo-app`). No API, no domain, no migration.

Unresolved decisions blocking the slice:
- **D1:** Option A1 vs A2 vs A3 (recommend A1).
- **D2:** Does the phone menu gain Settings access (F3)? Overturns a prior lock — Christian's call.
- **D3:** Confirm B1 (Price Book stays top-level) for this slice; B3 noted as future.
- **D4:** Workspace name shown in the menu header only, no switch action — confirm this reading of
  ADR-497 rule 8 holds and the workboard's "workspace switcher" phrase is downgraded to
  "workspace label" for GAP-054.
- **D5:** Whether Help & Updates (GAP-038, now sequenced immediately after GAP-054) gets its menu
  slot built here or added by GAP-038 itself. Recommendation: add it in GAP-038 — the gap between
  slices is now one session, so a reserved dead/disabled slot buys little.
- **D6:** F6 (global-CTA suppression asymmetry) — fold a small consistency fix into this slice, or
  leave it as a separate finding.

Browser verification (per GAP-054 tracker requirement) to be recorded when the slice lands: desktop
+ narrow, Owner/Admin/Operator/Viewer, keyboard-only menu operation, active-route treatment.
