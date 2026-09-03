# Keep Settings & Getting Started UI Upgrade — V2 Restyle

**Status:** Locked — product-owner approved 2026-09-03
**Scope:** Authenticated `ophalo-app` Owner/Admin surfaces only — `Getting Started` (`pages/Home.tsx`
`OwnerHome`) and `Settings` (`pages/Settings.tsx` shell + `settings/CompanySection.tsx`,
`settings/PublicLinkSection.tsx`, `settings/PolicySection.tsx`, `settings/TeamSection.tsx`).
**Primary outcome:** A new business owner opening Keep for the first time sees a product that is
already working, understands in one viewport that nothing is required of them, and can reach any
optional adjustment without the screens reading as an unstyled prototype or generic SaaS setup
wizard.
**Not in scope:** the Operator `Home` view, mobile settings management (PWA remains the admin
surface, ADR-424), Price Book, Requests, and any public/customer surface.

## 1. Governing decisions this upgrade must preserve

This is a **pure V2 visual restyle**. It does not change information architecture, field sets,
defaults, routing, or flow.

- **ADR-428 (Locked) is unchanged.** Keep launches day-zero functional: intake link
  auto-provisioned, response policy pre-defaulted, Settings is *adjust-not-assemble*. Settings stays
  three sections — `Public Link & Profile`, `Response Policy`, `Team` — in that order. Getting
  Started stays a lightweight verification/on-ramp. **No completion meter, no progress score, no
  step counter, no seven-step checklist framing, and no gate on app access.** Team stays
  secondary/low-pressure for solo owners.
- **ADR-295 / ADR-429** link semantics unchanged: one active link, slug-based copy/open, replacement
  is exceptional recovery with a stale-link warning, raw tokens never persisted in visible state.
- **design-model-v2 §3** token contract is binding: canvas `#F8F6F1` (`--ophalo-canvas`), card
  `--ophalo-card`, Keep accent `#168A9A` (`--keep-accent`), attention `#C8741A`
  (`--ophalo-attention`, risk/information only — never a filled action), info `#244C95`
  (`--keep-info`), success `--ophalo-success`. Source Serif 4 for the one type anchor per surface;
  Inter for everything operational. No generic Tailwind `slate-*`, `emerald-*`, `teal-600`, or ad
  hoc hex.
- **design-model-v2 §4** richness floor: each surface has one clear type anchor, one elevated filled
  surface, one deliberate Keep-teal moment, unequal visual rhythm, and visible card/canvas
  separation — **without** decoration that does not carry meaning.
- **review-rubric-v2** dimensions apply: token/typography/contrast, keyboard/focus/zoom/SR/touch
  targets (44px general), wide + narrow containment, and all state/recovery treatments.
- **ADR-428 Deferred still deferred:** logo *upload* (URL field only), brand-color customization,
  audit-history UI.

If implementation exposes a genuine product conflict with ADR-428, stop and surface the specific
evidence — do not silently reinterpret it.

## 2. Problems this upgrade fixes

Observed on the current build (1920px, Owner):

1. **Canvas isolation.** A single ~440px column of content is stranded in the left third of the
   viewport; 60%+ of horizontal space is empty. The screens read as unfinished.
2. **Passive Getting Started.** Three identical cards that only deep-link into Settings tabs. No
   confirmation that the business is actually ready; nothing to *do* here.
3. **No readiness signal.** Nothing tells a first-run owner "your link is live, requests will
   arrive, you can stop configuring." The absence of a checklist is correct; the absence of
   reassurance is not.
4. **Flat visual rhythm.** Every section is the same white rounded card with the same 13px muted
   intro paragraph. No type anchor, no teal moment, no elevation hierarchy — below the V2 richness
   floor.
5. **Token drift.** `PublicLinkSection.tsx` uses `text-slate-400` / `text-slate-500` directly
   (lines ~441, ~467) instead of `--ophalo-muted`.

Note: the page background is *already* the correct warm `--ophalo-canvas`; the older "cool gray
background" deferred item does not apply to these two surfaces.

## 3. Layout contract

### 3.1 Shared page frame (both surfaces)

- Outer canvas `--ophalo-canvas`, unchanged.
- Content max width **`max-w-[880px]`**, centered (`mx-auto`), with the existing responsive page
  padding. Rationale: these are single-task reading-and-editing surfaces, not parallel-work
  dashboards. A ~640px form column inside an 880px frame gives the content honest breathing room and
  a visible left/right margin **without** manufacturing a second column of filler. We do **not**
  adopt the Requests three-column workbench pattern here — there is one task stream, not three.
- One Source Serif 4 page title (`keep-page-title`) + one Inter subtitle (`keep-page-subtitle`) per
  surface, unchanged in copy intent.

### 3.2 Getting Started (`OwnerHome`)

Replace the three passive cards with a **first-run readiness panel** — a single elevated card that
confirms the business is live, then offers optional next steps. It is verification, not a checklist:
no numbered steps, no completion state, no "X of Y done".

```text
┌─────────────────────────────────────────────────────────────┐
│  Getting started                          (keep-page-title)  │
│  Keep is ready — here's your setup at a glance.  (subtitle)  │
│                                                             │
│  ┌───────────────────────────────────────────────────────┐  │  ← elevated card,
│  │  ● Your business is live on Keep      (teal moment)    │  │    --ophalo-card,
│  │                                                       │  │    stronger shadow than
│  │  Public request link                                  │  │    the option rows
│  │  keep.ophalo.com/s/ophalo-demo-business                │  │
│  │  [ Copy link ]  [ Open ↗ ]      ← reuses PublicLink    │  │
│  │                                   copy/open affordance │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                             │
│  Optional — adjust when you want to                         │
│  ┌───────────────┐ ┌───────────────┐ ┌───────────────┐       │
│  │ Business      │ │ Response      │ │ Invite        │       │  ← quiet option rows,
│  │ profile       │ │ targets       │ │ teammates     │       │    subdued, → Settings
│  │ Name, phone,  │ │ Defaults work │ │ Solo is fine; │       │    tabs (unchanged
│  │ email, logo   │ │ for most      │ │ add later     │       │    deep-link behavior)
│  └───────────────┘ └───────────────┘ └───────────────┘       │
│                                                             │
│  [ Add your first customer request ]  ← navy primary,        │
│                                          opens Quick Capture │
└─────────────────────────────────────────────────────────────┘
```

Rules:

- The readiness card is the **one elevated filled surface** and carries the **one Keep-teal
  moment** (the live-status dot/label and the primary link affordance treatment).
- "Your business is live on Keep" is a **fact** — the link exists by default per ADR-428. It is not
  a claim about customer receipt or verification (rubric: action truth).
- The public link value is displayed from the same slug-based source `PublicLinkSection` uses.
  Getting Started does **not** introduce its own link-fetch or link-mutation path; if the link data
  is not yet loaded, show the card's structured loading placeholder, never a guessed/constructed
  URL (ADR-428).
- The three "Optional" rows are visually **subordinate** to the readiness card — subdued surface,
  no shadow or minimal, smaller type. They keep their current `onNavigateSettings(section)`
  behavior exactly.
- "Add your first customer request" keeps its current `onStartCapture` behavior; it is the single
  navy primary at rest on this surface.
- **Never:** a progress bar, a checkmark/completion column, a percent, a step number, a "finish
  setup" CTA, or anything that blocks navigating away.

### 3.3 Settings shell (`pages/Settings.tsx`)

- Keep the three tabs, order, labels, `role="tablist"` semantics, and deep-link `scrollToSection`
  behavior exactly.
- Restyle the tab row to the V2 treatment already used elsewhere (active = `--keep-accent`
  underline + `--ophalo-navy` text; inactive = `--ophalo-muted`), which it broadly already matches
  — tighten spacing/contrast only.
- Content frame becomes the shared `max-w-[880px]` frame (from the current `max-w-2xl` ≈ 672px).
- Loading / error / empty states must preserve layout (rubric §state): a structured placeholder
  shaped like the section, not a centered "Loading…" string.

### 3.4 Section cards (all four sections)

Each section is one `--ophalo-card` rounded panel (unchanged structure). Apply consistently:

- **Type anchor:** section `<h2>` stays `keep-row-title` (Source Serif 4). Exactly one per card.
- **Intro line:** one Inter sentence, `--ophalo-muted`, ≤ 2 lines. Keep existing copy intent;
  tighten any that runs long.
- **Field rhythm:** label (Inter 14 medium, `--ophalo-ink`) → optional helper (13, `--ophalo-muted`)
  → control. Consistent vertical spacing token across all four sections (they currently vary).
- **Inputs:** one shared input recipe — `--ophalo-card` bg, `--ophalo-border`, `rounded-lg`,
  `--keep-accent` focus ring, min 44px target. `PolicySection`'s `w-36` numeric inputs may stay
  intrinsic-width but must meet the target and ring recipe.
- **Primary button:** `KeepButton variant="primary"` (navy). One per card. Inline success text uses
  `--ophalo-success` (already correct in `PolicySection`).
- **Elevation:** section cards lift from canvas with the standard card shadow; within
  `Public Link & Profile` the live customer preview block is the deliberate elevated/teal moment
  (see 3.5).

### 3.5 Public Link & Profile specifics

- `CompanySection` + `PublicLinkSection` keep their current split, field sets, "Branding & trust
  anchors" grouping, `Save company`, `Edit link name`, and `Replace link (breaks old shared links)`
  recovery flow **exactly**.
- The **live customer preview** (phone-sized, "Unsaved changes shown live — save to publish") is the
  surface's teal/elevated moment: give it a defined framed treatment and fix its token drift
  (`text-slate-400/500` → `--ophalo-muted`; `#FAF8F5`-ish → `--ophalo-canvas` if present).
- Preview must continue to reflect the shared unsaved `profileDraft` and never present unsaved edits
  as published (existing behavior in `Settings.tsx`).
- `Replace link` stays a confirmed destructive action with the stale-link warning; raw successor URL
  shown once (ADR-428/429). Restyle the confirm dialog to the V2 dialog treatment; do not change its
  logic.

### 3.6 Response Policy specifics

- Four fields, order, copy, and `updatePolicy` contract unchanged.
- Apply the shared field rhythm and input recipe. Keep the plain-language helper under each label
  (ADR-428 requires it).

### 3.7 Team specifics

- Roster / invite / role / resend / suspend / reactivate / remove flows, `seatUsage` display,
  disabled-at-limit behavior, and "Show removed members" all unchanged.
- Keep the reassuring solo-owner copy ("Keep works great for solo businesses…").
- Restyle: invite row (email + role select + button) to the shared input recipe with 44px targets;
  member rows to a consistent list-row treatment; the dismiss "×" on notices gets an accessible
  name (already `aria-label="Dismiss"`).
- Seat availability stays server-authoritative — no inference from row counts (ADR-428).

## 4. Responsive / containment contract

- **≥ 880px viewport:** centered 880px frame, visible canvas margins both sides.
- **Below 880px:** frame is fluid to the existing page padding; single column; nothing clips or
  scrolls horizontally at the card level.
- **Narrow / mobile (PWA):** stacks cleanly; the live customer preview drops below the form; all
  targets ≥ 44px; editable controls ≥ 16px text (iOS). PWA remains the admin surface — no separate
  mobile settings UX.
- 100% / 125% / 150% browser zoom: no clipped labels, no card-level horizontal scroll, tab row
  wraps rather than truncates.

## 5. Screenshot-acceptance contract

Product-owner visual acceptance requires, with a real Owner account and realistic data, screenshots
at **1366×768, 1440×900, and 1920×1080** plus **100 / 125 / 150%** zoom, covering:

### Getting Started
1. Content is centered with visible canvas margin on both sides at every width — no stranded
   left-third column.
2. The readiness card is clearly the dominant elevated element; it states the business is live and
   shows a working Copy link / Open control.
3. The three optional rows are visibly subordinate (quieter surface, smaller type) and still
   navigate to the correct Settings tab.
4. "Add your first customer request" is the only navy primary and opens Quick Capture.
5. **No** progress meter, percent, step number, completion checkmark, or access gate anywhere on the
   surface.
6. One Source Serif 4 anchor; one Keep-teal moment; card lifts from canvas.

### Settings (each tab)
7. Tab row: active tab has the `--keep-accent` underline + navy text; inactive is muted; row wraps
   (never truncates) at 150%.
8. Content sits in the centered 880px frame with canvas margins; section card lifts from canvas.
9. Each section: one serif `<h2>`, one ≤2-line intro, consistent label→helper→control rhythm,
   consistent input styling, one navy primary.
10. Response Policy: every field keeps its plain-language helper line.
11. Public Link & Profile: the live customer preview is the framed teal/elevated moment and updates
    live from unsaved edits; "save to publish" caption present; no `slate-*` greys.
12. Team: solo-owner reassurance copy present; invite controls and member rows on the shared
    recipe; seat usage shown from server value.
13. `Replace link` confirm dialog uses the V2 dialog treatment and still shows the stale-link
    warning.

### Cross-cutting
14. Token audit: no `slate-*`, `emerald-*`, `teal-600`, or non-token hex in any of the six files.
15. Keyboard-only traversal reaches every control with a visible focus ring; SR announces tab
    selection and save results.
16. Loading and error states preserve layout (structured placeholder, not a bare centered string).

## 6. Implementation slices

Recorded as new work **after** the currently approved GAP-039 → GAP-033 sequence. Each slice is
independently compilable and independently acceptance-reviewed. All slices are frontend-only
(`ophalo-app`); no API, domain, migration, or backend change.

- **Slice A — Getting Started + Settings shell + Response Policy.**
  `pages/Home.tsx`, `pages/Settings.tsx`, `pages/settings/PolicySection.tsx` + shared frame/input
  recipe. Lowest risk; establishes the shared primitives B and C reuse. Tests:
  `pages/__tests__/*` for Home readiness card (no meter/gate assertions), Settings shell frame,
  Policy field rhythm.
- **Slice B — Public Link & Profile.**
  `pages/settings/CompanySection.tsx`, `pages/settings/PublicLinkSection.tsx`. Largest surface;
  fixes token drift; restyles the preview and the replace-link dialog. Tests: existing
  `PublicLinkSection.*` suites must stay green; add preview token/treatment assertions.
- **Slice C — Team.**
  `pages/settings/TeamSection.tsx`. Tests: existing Team suites green; add list-row / invite-row
  recipe assertions.

Batch-size: each slice is 1–3 production files + tests, within the CLAUDE.md gate. Do not combine
slices.

## 7. Explicit "never" list

- Never add a completion score, progress bar, step counter, or "finish setup" pressure to Getting
  Started.
- Never gate app access, navigation, or any feature on a Getting Started / Settings action.
- Never change ADR-428 field sets, section order, defaults, or the day-zero auto-provisioning model
  in this effort.
- Never introduce a Getting Started-owned link fetch or link mutation; read the slug-based value
  from the existing source.
- Never display a constructed/guessed public URL as guaranteed-live.
- Never persist or log a raw intake token in visible state.
- Never use generic Tailwind palette colors or non-token hex.
- Never infer seat availability from visible row counts.
- Never make Team feel mandatory for a solo owner.
