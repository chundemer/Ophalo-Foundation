# UX Design Hardening Pass

> **RETIRED — historical only (2026-06-07).** This is a point-in-time work log of the
> 2026-06-04 UX review, not current doctrine. Do not derive styling rules from it.
> Live doctrine now lives in `ux-design-model-v1.md` (principles/tokens/Floor),
> `keep-component-spec.md` (recipes/structure), and `keep-review-rubric.md` (review +
> release gate). See `README.md` for the authority map and precedence order. Kept for
> provenance of the original findings.

**Status:** Retired — historical work log (findings dated 2026-06-04)
**Date:** 2026-06-04
**Superseded by:** `ux-design-model-v1.md`, `keep-component-spec.md`, `keep-review-rubric.md`

---

## Purpose

This document captures current styling, mobile, accessibility, and maintainability findings for OpHalo and Keep surfaces.

Use it as the implementation map for getting UX and styling deployment-ready.

---

## Review Scope

Frontend:

- `web/ophalo-web/src/app/globals.css`
- `web/ophalo-app/src/app/globals.css`
- `web/ophalo-web/src/app/q/[publicIntakeToken]/_components/IntakeForm.tsx`
- `web/ophalo-web/src/app/r/[pageToken]/page.tsx`
- `web/ophalo-web/src/app/r/[pageToken]/_components/CustomerQuickActions.tsx`
- `web/ophalo-web/src/app/r/[pageToken]/_components/ContactPreferences.tsx`
- `web/ophalo-app/src/app/keep/(protected)/requests/page.tsx`
- `web/ophalo-app/src/app/keep/(protected)/requests/_components/RequestFilters.tsx`
- `web/ophalo-app/src/app/keep/(protected)/requests/new/_components/NewRequestForm.tsx`
- `web/ophalo-app/src/app/keep/(protected)/requests/[requestId]/page.tsx`

Docs:

- `docs/v1/ophalo-brand-v1.2.md`
- `docs/reference/request-list_6_3/request-list-decisions.md`
- `docs/reference/customer-request-page_6_2/crp-design-decisions.md`

---

## Findings

### P0 - None Found

No UX issue was found that appears to directly block safe use of the product by itself.

The primary risk is design-system drift that will make public-availability polish harder to maintain.

---

### P1 - Parent Tokens Drift Between Web And App

**Severity:** High maintainability and brand consistency

`web/ophalo-app` and `web/ophalo-web` currently define different OpHalo parent values for navy, ink, background, border, muted text, and radius.

Required fix:

- Lock shared parent values to `ux-design-model-v1.md`.
- Update both app globals to use the same parent neutral palette.
- Keep product-specific values under `--keep-*`.

---

### P1 - Keep Accent Is Hard-Coded In Multiple Components

**Severity:** High maintainability

Keep teal appears as local constants and inline focus colors across public and operator Keep surfaces.

Required fix:

- Replace `KEEP_TEAL` and `KEEP_TEAL_SOFT` constants with `--keep-accent` and `--keep-accent-bg`.
- Replace focus ring arbitrary values with token-backed classes.
- Remove retired `#159DB8` usage from Keep surfaces.

---

### P1 - Product And Semantic Color Roles Are Mixed

**Severity:** High UX consistency

Keep teal, trust blue, navy, amber, and green are currently used page-by-page rather than through a clear role system.

Required fix:

- Use `--keep-accent` for Keep identity and communication cues.
- Use `--keep-info` for active/new/supporting state.
- Use shared `--ophalo-attention`, `--ophalo-success`, and `--ophalo-danger` for status meaning.
- Audit all badges, rails, buttons, and focus states.

---

### P1 - Shared UI Primitives Are Missing

**Severity:** High maintainability

Buttons, inputs, cards, badges, panels, empty states, and section labels are redefined directly inside page files.

Required fix:

- Create shared primitives before another broad restyle pass.
- Start with Button, Field, Textarea, Badge, Panel, PageShell, SectionLabel, and RequestCard primitives.
- Keep product-specific wrappers where behavior is unique.

---

### P2 - Request Detail Has A Separate Styling System

**Severity:** Medium UX consistency

Request detail defines local pills, panel heads, facts cards, customer preview cards, shadows, and arbitrary sizes. It feels related to the request list but not governed by the same component contract.

Required fix:

- Restyle after shared primitives exist.
- Align pills, panels, shadows, page shell, and timeline badges to the UX model.
- Keep customer-visible actions prominent and internal bookkeeping secondary.

---

### P2 - Request List Filters Can Become Cramped On Narrow Mobile

**Severity:** Medium mobile UX

The request list filter row wraps, but the long search placeholder plus explicit Search button can consume too much horizontal room on narrow screens.

Required fix:

- Shorten placeholder on mobile.
- Consider making Search button full-width below the input at narrow widths or relying on input submit with a compact icon button.
- Verify at 320px and 375px.

---

### P2 - Focus States Are Not Yet Standardized

**Severity:** Medium accessibility

Some controls use default outline behavior, some use arbitrary focus rings, and some use border changes only.

Required fix:

- Define shared focus treatment for buttons, inputs, links, and cards.
- Ensure focus is visible against white cards, warm background, and tinted surfaces.
- Test keyboard navigation across public intake, customer request page, request list, and request detail.

---

### P2 - Button Hierarchy Needs A Component-Level Lock

**Severity:** Medium UX consistency

Navy, Keep teal, outline, and quiet buttons are all present, but not yet governed by shared roles.

Required fix:

- Lock button variants: parent primary, Keep communication primary, operator secondary, quiet, destructive, success feedback.
- Apply consistently to New request, Send update, Send message, Contact, Save preferences, Mark handled, and Close request.

---

### P2 - Public And Operator Forms Duplicate Field Styling

**Severity:** Medium maintainability

Public intake, operator new request, customer quick actions, and contact preferences each define field styling locally.

Required fix:

- Extract shared field and textarea styles.
- Keep public forms at mobile-friendly text sizes.
- Align validation and helper text.

---

### P3 - The UI Is Clear But Still Plain

**Severity:** Low polish

The surfaces are understandable, but some screens feel plain because polish is coming from isolated local styling rather than a repeatable system.

Required fix:

- Add consistent page headers, section rhythm, state badges, panel headers, and compact helper surfaces.
- Avoid decorative effects. Polish should come from consistency, state clarity, and practical hierarchy.

---

### P2 - Visual Hierarchy And Keep Identity Need A Reusable Gate

**Severity:** Medium UX consistency

Some surfaces can pass token and layout checks while still feeling plain or prototype-like because product identity, communication cues, visual volume, and trust states are not yet governed by a reusable implementation gate.

Required fix:

- Use the Visual Hierarchy Gate in the deployment checklist before release.
- Use the Visual Volume Scale in `visual-polish-rubric.md` and the UX model before page-polish work.
- Require pre-coding audits to identify Level 1 page anchor, Level 2 primary content/actions, Level 3 supporting content, and Level 4 utilities.
- Ensure Keep teal appears on communication identity moments without washing entire screens in product color.
- Ensure neutral/warm styling is used for records, metadata, waiting states, and utilities.

---

## Implementation Sessions

### UX-S1 - Retire Old Brand Doc And Add UX Reference

**Status:** Complete

Scope:

- Create `docs/reference/ux-design/`.
- Add model, decisions, hardening pass, and deployment checklist.
- Add designer-facing production readiness rubric.
- Retire stale brand docs with pointers:
  - `docs/v1/ophalo-brand-v1.2.md`
  - `docs/v1/product/ophalo-brand-v1.2.md`
  - `docs/v1/product/OpHalo Brand Guide.md`
- Update decision index.

---

### UX-S2 - Shared Token Source And Globals Cleanup

**Status:** Complete

**Goal:** One shared token source, both apps on the correct OpHalo values, `--keep-*` tokens in place, no retired teal in any CSS file. Component files are not touched — backward-compat aliases in globals preserve current runtime behavior until S4 migrates each surface.

**Files touched (3 total):**

| File | Action |
|---|---|
| `web/shared/styles/ophalo-tokens.css` | Create |
| `web/ophalo-app/src/app/globals.css` | Edit |
| `web/ophalo-web/src/app/globals.css` | Edit |

**Step 1 — Create `web/shared/styles/ophalo-tokens.css`**

Define only the canonical parent tokens and Keep tokens here. No aliases, no backward-compat cruft.

```css
:root {
  /* OpHalo parent brand tokens */
  --ophalo-navy:          #10243E;
  --ophalo-ink:           #172033;
  --ophalo-canvas:        #F8F6F1;
  --ophalo-card:          #FFFFFF;
  --ophalo-border:        #DDD6C8;
  --ophalo-muted:         #5D6878;

  /* OpHalo shared semantic tokens */
  --ophalo-attention:     #B85B16;
  --ophalo-attention-bg:  #FFF2D7;
  --ophalo-success:       #166534;
  --ophalo-success-bg:    #E8F4ED;
  --ophalo-danger:        #B42318;
  --ophalo-danger-bg:     #FEE4E2;

  /* Keep product tokens */
  --keep-accent:          #168A9A;
  --keep-accent-bg:       #E5F4F3;
  --keep-accent-hover:    #107A90;
  --keep-info:            #244C95;
  --keep-info-bg:         #EAF1FF;
}
```

**Step 2 — Update `web/ophalo-app/src/app/globals.css`**

Import the shared file at the top (after `@import "tailwindcss"`).

The import path from `web/ophalo-app/src/app/globals.css` is:

```css
@import "../../../shared/styles/ophalo-tokens.css";
```

Remove the existing `:root` OpHalo block. Add backward-compat aliases for names still used in component files — these are removed surface-by-surface in S4:

```css
:root {
  /* Backward-compat aliases — remove as each surface is migrated in S4 */
  --ophalo-bg:        var(--ophalo-canvas);
  --ophalo-green:     var(--ophalo-success);
  --ophalo-green-bg:  var(--ophalo-success-bg);
  --ophalo-red:       var(--ophalo-danger);
  --ophalo-red-bg:    var(--ophalo-danger-bg);
  --ophalo-gold:      #e8c16c;   /* legacy alias — marketing site only */

  /* Tailwind semantic mappings */
  --background:           var(--ophalo-canvas);
  --foreground:           var(--ophalo-ink);
  --card:                 var(--ophalo-card);
  --card-foreground:      var(--ophalo-ink);
  --primary:              #c99a34;
  --primary-foreground:   var(--ophalo-navy);
  --secondary:            var(--ophalo-navy);
  --secondary-foreground: #ffffff;
  --muted:                #f2f0eb;
  --muted-foreground:     var(--ophalo-muted);
  --accent:               #efeae1;
  --accent-foreground:    var(--ophalo-navy);
  --destructive:          var(--ophalo-danger);
  --border:               var(--ophalo-border);
  --input:                #e3ddd1;
  --ring:                 var(--ophalo-navy);
  --radius:               0.75rem;
}
```

**Step 3 — Update `web/ophalo-web/src/app/globals.css`**

Keep `web/shared/styles/ophalo-tokens.css` as the canonical token reference, but do not import it
from `ophalo-web` CSS. Turbopack dev mode does not resolve CSS `@import` paths that escape the
Next.js project root, so `ophalo-web/src/app/globals.css` must contain a synced inline copy of the
token definitions with a comment pointing back to the shared source file.

Replace the existing `:root` OpHalo token block. Key corrections:

- `--ophalo-navy` was `#1b2a4a` (wrong) → now from shared file at `#10243E`
- `--ophalo-teal: #159DB8` (retired) → remove; the `@theme inline` block references `--color-ophalo-teal: var(--ophalo-teal)` — remove that line too
- `--ophalo-teal-dark` → remove
- `--ophalo-muted-navy` → remove (not in model)
- `.pilot-access` button in site CSS uses `background-color: #159DB8` — replace with `var(--keep-accent)` and the hover with `var(--keep-accent-hover)`
- `.start-error` uses `color: #c0392b` — replace with `var(--ophalo-danger)`

Keep the full site-level CSS (`site-header`, `site-footer`, `site-page`, etc.) untouched. Only fix token values and remove retired ones.

Add the same Tailwind semantic mappings block for ophalo-web's shadcn/Tailwind compatibility. Preserve `--sidebar-*` tokens since ophalo-web uses them.

**Verification steps before closing S2:**

1. `npm run build` in both `web/ophalo-app` and `web/ophalo-web` must pass.
2. Visually confirm request list, request detail, and new request form still render correctly in ophalo-app (no color breakage from the alias chain).
3. Visually confirm marketing site header and CTA button in ophalo-web render correctly with the corrected navy value.

**Alias removal schedule (do not remove in S2):**

| Alias | Remove in |
|---|---|
| `--ophalo-bg` | S4A (new request form migration) |
| `--ophalo-green`, `--ophalo-green-bg` | S4B (request list migration) |
| `--ophalo-red`, `--ophalo-red-bg` | S4A or S4B depending on surface order |
| `--ophalo-gold` | After marketing site uses token directly |

---

### UX-S3 - Shared UI Primitives

**Status:** Pending

**Goal:** Build token-backed primitive components before restyling any page. No surface migration in this session — only component creation and a smoke-test import on one surface to confirm the primitives work.

**Scope decision — two component locations:**

Keep operator primitives live in ophalo-app. Public/customer primitives live in ophalo-web. There is no shared component package at this stage.

| App | Component directory |
|---|---|
| `web/ophalo-app` | `src/components/keep/` |
| `web/ophalo-web` | `src/components/keep/` |

**Primitives to create in `web/ophalo-app/src/components/keep/`:**

| Component | Purpose |
|---|---|
| `KeepButton.tsx` | Variants: `navy` (default), `teal`, `outline`, `quiet`, `danger`, `success`. Uses `--ophalo-navy`, `--keep-accent`, `--keep-accent-hover`, `--ophalo-danger`, `--ophalo-success`. Min height 42px. |
| `KeepField.tsx` | Label + input + optional error. White bg, `--ophalo-border`, focus ring using `--keep-accent`. `text-base` on public surfaces. |
| `KeepTextarea.tsx` | Same as KeepField but textarea. Consistent resize behavior. |
| `KeepBadge.tsx` | Variants: `attention`, `active`, `success`, `danger`, `teal`. Maps to semantic token pairs. Falls back to neutral for unknown states. |
| `KeepPanel.tsx` | White card, `--ophalo-border`, modest radius, compact padding variants. |
| `KeepSectionLabel.tsx` | Small uppercase muted label for section heads. Uses `--ophalo-muted`. |
| `KeepEmptyState.tsx` | Centered message + optional action for empty list states. |
| `KeepPageShell.tsx` | `mx-auto w-full max-w-3xl px-4 sm:px-6`. Replaces the current `.page-shell` utility. |

**Primitives to create in `web/ophalo-web/src/components/keep/`:**

| Component | Purpose |
|---|---|
| `KeepButton.tsx` | Same variant contract as ophalo-app version. Used in IntakeForm and customer request page. |
| `KeepField.tsx` | Public-safe: `text-base` minimum (prevents iOS zoom). |
| `KeepTextarea.tsx` | Public-safe textarea. |
| `KeepBadge.tsx` | Same variant contract. |

**Smoke test:**

At the end of S3, import `KeepButton` into `NewRequestForm.tsx` in place of one existing inline-styled button. Confirm it renders correctly. Do not restyle the whole form — that is S4A. This is a compile and render check only.

**Do not do in S3:**

- Do not migrate any full page.
- Do not remove backward-compat aliases from globals.
- Do not create a `KeepRequestCard` or `KeepStatusRail` yet — those emerge from the actual request list migration in S4B.

---

### UX-S4A - New Request Form And Public Intake Migration

**Status:** Pending

**Prerequisite:** UX-S3 complete.

**Goal:** Replace all hard-coded hex values, `KEEP_TEAL` constants, and inline style objects in NewRequestForm.tsx and IntakeForm.tsx with KeepButton, KeepField, KeepTextarea, and semantic CSS tokens. Remove the S2 aliases that become unused after this migration.

**Files touched:**

| File | Action |
|---|---|
| `web/ophalo-app/src/app/keep/(protected)/requests/new/_components/NewRequestForm.tsx` | Migrate |
| `web/ophalo-web/src/app/q/[publicIntakeToken]/_components/IntakeForm.tsx` | Migrate |
| `web/ophalo-app/src/app/globals.css` | Remove `--ophalo-bg` and `--ophalo-red`, `--ophalo-red-bg` aliases if unused after migration |

**Specific targets in NewRequestForm.tsx:**

- `KEEP_TEAL = "#168A9A"` and `KEEP_TEAL_SOFT = "#E5F4F3"` constants → delete, use `--keep-accent` / `--keep-accent-bg` via tokens
- `focus:ring-[#168A9A]` arbitrary values → use shared KeepField component
- `style={{ background: KEEP_TEAL_SOFT, color: KEEP_TEAL }}` inline styles → KeepBadge with teal variant
- `style={{ background: KEEP_TEAL }}` on submit button → KeepButton with teal variant
- `style={{ background: "var(--ophalo-red-bg)" }}` → KeepBadge with danger variant

**Specific targets in IntakeForm.tsx (ophalo-web):**

- `KEEP_TEAL = "#168A9A"` constant → delete
- `focus:ring-[#168A9A]` arbitrary value → use KeepField component
- `accent-[#168A9A]` arbitrary value → use `accent-[color:var(--keep-accent)]` or inline style via token
- `style={{ backgroundColor: KEEP_TEAL }}` → KeepButton with teal variant

**Button hierarchy to lock in S4A:**

| Button | Variant |
|---|---|
| Submit new request | `navy` filled |
| Send update (if present) | `teal` filled |
| Cancel / reset | `quiet` |

**Session size check:** 2 component files + 1 globals edit. Well within limit.

---

### UX-S4B - Customer Request Page And Request List Migration

**Status:** Pending

**Prerequisite:** UX-S4A complete.

**Goal:** Migrate the customer request page (ophalo-web) and request list (ophalo-app) to shared primitives and semantic tokens. Create `KeepRequestCard` and `KeepStatusRail` primitives from the patterns that emerge during request list migration.

**Files touched (ophalo-web — customer request page):**

| File | Action |
|---|---|
| `web/ophalo-web/src/app/r/[pageToken]/page.tsx` | Migrate |
| `web/ophalo-web/src/app/r/[pageToken]/_components/CustomerQuickActions.tsx` | Migrate |
| `web/ophalo-web/src/app/r/[pageToken]/_components/ContactPreferences.tsx` | Migrate |

**Files touched (ophalo-app — request list):**

| File | Action |
|---|---|
| `web/ophalo-app/src/app/keep/(protected)/requests/page.tsx` | Migrate |
| `web/ophalo-app/src/app/keep/(protected)/requests/_components/RequestFilters.tsx` | Migrate |

**New primitives to extract during this session:**

| Component | Location | From |
|---|---|---|
| `KeepRequestCard.tsx` | `web/ophalo-app/src/components/keep/` | Request list card pattern |
| `KeepStatusRail.tsx` | `web/ophalo-app/src/components/keep/` | Attention/active section header |

**Specific targets:**

Customer request page:
- Any hardcoded `#168A9A`, `#159DB8` → `--keep-accent` via token or KeepButton
- Status badges → KeepBadge variants
- Send message / quick action buttons → KeepButton with correct variants

Request list:
- `var(--ophalo-green-bg)`, `var(--ophalo-green)` → `var(--ophalo-success-bg)`, `var(--ophalo-success)` via token or KeepBadge
- `var(--ophalo-red-bg)`, `var(--ophalo-red)` → `var(--ophalo-danger-bg)`, `var(--ophalo-danger)` via token or KeepBadge
- Filter row: address narrow mobile layout (P2 finding) — shorten placeholder on mobile, verify at 320px

Remove backward-compat aliases from ophalo-app globals after this session:
- `--ophalo-green`, `--ophalo-green-bg`
- `--ophalo-red`, `--ophalo-red-bg` (if not already removed in S4A)

**Button hierarchy to lock in S4B:**

| Button | Variant |
|---|---|
| Send update / Send message | `teal` filled |
| Contact | `outline` |
| Save preferences | `teal` (communication action) |
| Mark handled | `quiet` |
| Cancel | `quiet` |
| Change email | `quiet` |

**Session size check:** 5 component files + 2 new primitives + globals edit. At the upper boundary. If request list migration becomes large, split S4B into two sessions: customer page first, request list second.

---

### UX-S4C - Request Detail Migration

**Status:** Pending

**Prerequisite:** UX-S4B complete. Shared primitives stable.

**Goal:** Align request detail to the shared design system. This is the most locally styled surface — defer until primitives are proven to avoid re-work.

**Files touched:**

| File | Action |
|---|---|
| `web/ophalo-app/src/app/keep/(protected)/requests/[requestId]/page.tsx` | Migrate |
| `web/ophalo-app/src/app/keep/(protected)/requests/[requestId]/_components/BusinessUpdatePanel.tsx` | Migrate |
| `web/ophalo-app/src/app/keep/(protected)/requests/[requestId]/_components/CustomerPageActions.tsx` | Migrate |
| `web/ophalo-app/src/app/keep/(protected)/requests/[requestId]/_components/CustomerWaitingExplanation.tsx` | Migrate |
| `web/ophalo-app/src/app/keep/(protected)/requests/[requestId]/_components/ChangeStatusButton.tsx` | Migrate |
| `web/ophalo-app/src/app/keep/(protected)/requests/[requestId]/_components/AddInternalNoteButton.tsx` | Migrate |
| `web/ophalo-app/src/lib/toast.tsx` | Replace `var(--ophalo-red)` with `var(--ophalo-danger)` |

**Specific targets:**

- Local pill, panel-head, and facts-card styles → KeepPanel, KeepBadge
- Any remaining `#168A9A` inline → `--keep-accent` via token
- Timeline badges → KeepBadge variants by status
- Customer preview card → KeepPanel (inset metadata, not card-in-card)
- `toast.tsx`: `var(--ophalo-red)` → `var(--ophalo-danger)`

**Button hierarchy to lock in S4C:**

| Button | Variant |
|---|---|
| Send customer update | `teal` filled |
| Add internal note | `outline` |
| Close request | `danger` |
| Change status | `quiet` |
| Mark handled | `quiet` |

**Final alias cleanup:** After this session, all backward-compat aliases in both globals should be removed except `--ophalo-gold` (marketing site legacy alias — leave until ophalo-web marketing CSS is audited).

**Session size check:** 7 component files. At the upper boundary. If detail page has significant local styling that requires more than token replacement (structure changes), split into page.tsx first, then components.

---

### UX-S5 - Mobile And Accessibility QA

**Status:** Pending

**Goal:** Verify the release checklist from `ux-design-deployment-checklist.md` across all migrated surfaces before public availability.

**Test surfaces:**

- Public intake (ophalo-web)
- Customer request page (ophalo-web)
- Operator new request form (ophalo-app)
- Request list (ophalo-app)
- Request detail (ophalo-app)

**Test widths:** 320px, 375px, 390px, 430px, 768px, desktop.

**Checks:**

- No horizontal scroll at any width
- Tap targets practical (min 24×24px, prefer 42px for primary actions)
- Focus states visible on white cards, warm background, and tinted surfaces
- Keyboard navigation reaches all controls in logical order across all surfaces
- Text wraps — no clipping in cards, filters, buttons, or badges
- First request card visible on mobile without excessive scroll past controls
- Public forms readable without layout shift
- Filter row compact on narrow mobile (P2 finding — search placeholder shortens)

**Fix protocol:** Fixes identified in S5 go into the same session. S5 must close with all P0–P2 checklist items green. P3 items are polish and do not block public availability.

---

### UX-S6 - Designer Production Readiness Review

**Status:** Pending

**Goal:** Score each public-availability surface with `ux-design-production-readiness.md` and decide pass, exception, or block.

**Scope:**

- Public intake
- Customer request page
- Operator new request form
- Request list
- Request detail
- Marketing/account path touched by the release

**Required review artifacts:**

- 320px screenshot or inspection notes
- 390px screenshot or inspection notes
- 768px screenshot or inspection notes
- desktop screenshot or inspection notes
- focus state on primary action
- empty or no-update state
- error state where applicable
- success or sent state where applicable

**Exit criteria:**

- Every surface scores 3 in every universal category, or
- any score below 3 has a written deployment exception with owner and follow-up, or
- the surface is explicitly held out of public availability.
