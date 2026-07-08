# Keep Component Spec

**Status:** Active
**Date:** 2026-06-07
**Scope:** Closed component recipes and foundations for Keep customer-facing and operator surfaces

---

## Purpose

This is the **specification layer** of the Keep design system. It sits below the
principles and judgment docs and above the code:

| Layer | Doc | Answers |
|---|---|---|
| Principles | `ux-design-model-v1.md` | Tokens, voice, balance rules |
| Review | `keep-review-rubric.md` | Is this screen production-ready or still a prototype? |
| **Specification** | **this file** | **What exactly is each component — to the class?** |
| Code | `r/[pageToken]/page.tsx`, `components/keep/*` | The implementation |

The problem this doc solves: iterations kept re-deriving styling from principles
every session, and re-derivation drifts back toward "wireframe with correct
tokens." A page can use every right color and still read flat because each
component is rebuilt by hand and no two surfaces share a scale.

A recipe here is a **closed recipe** — every visual property is pinned as a
Tailwind class. There is nothing left to re-decide, so there is nothing to drift.

---

## The Three Rules

Recipes alone do not produce a production screen. These three rules do.

### Rule 1 — Composition by separated surfaces

Volume contrast (the Level 1–4 scale in `keep-review-rubric.md`) comes from
**surface separation and spacing**, not from hairline dividers inside one box.

- The page is **not** one outer card sliced by `border-t`.
- Each surface is its own block on the warm canvas, separated by vertical gap.
- Higher-volume surfaces get stronger treatment (tint, rail, shadow); lower-volume
  content sheds the card entirely.

```
main  →  bg-background px-4 py-6 sm:py-10
  └ container  →  mx-auto w-full max-w-2xl space-y-4 sm:space-y-5
       ├ Status hero        (Level 1)
       ├ Continuity card    (Level 2)
       ├ Composer           (Level 2)
       ├ Standard card(s)   (Level 2/3)
       ├ Timeline           (Level 3)
       └ Utility footer     (Level 4 — no card)
```

The `space-y-*` on the container replaces the old `border-t` dividers.

### Rule 2 — Single source for primitives

Every chip is `KeepBadge`. Every button is `KeepButton`. Every labeled field is
`KeepField` / `KeepTextarea`. No hand-rolled equivalents. If a recipe needs a
variant that does not exist, add it to the primitive — do not inline it on the page.

### Rule 3 — No inline `style={{}}` for visual properties

Every value lives in a Tailwind class backed by a token. Inline `style` is the
surface drift lives on — not enforceable, not greppable. The only allowed `style`
is a genuinely data-driven token lookup (e.g. the timeline icon map), and even
then prefer a class map.

### Rule 4 — Hierarchy from weight, not labels

A muted uppercase label above every section reads as wireframe annotation, not
design. Page hierarchy comes from surface weight (size, fill, elevation, space) —
not from a `KeepSectionLabel` on every block.

- Do **not** place an uppercase section label above each surface by default.
- The uppercase `section` label (Part 1 ramp) is allowed on **at most one** block —
  the quietest footer-weight utility (e.g. §8 email preferences) — and never on the
  hero, continuity card, composer, or standard cards.
- Where a surface needs naming, prefer the surface's own treatment (the continuity
  card's teal badge *is* its label) or a sentence-case `title` from the type ramp.

This is the page-level expression of the Production Richness Floor's "repeating
uppercase section labels" failure (`ux-design-model-v1.md`).

---

# Part 1 — Foundations

Every component below pulls from these scales. Naming the scales once is what stops
ad-hoc sizes (the biggest wireframe tell after flat hierarchy).

## Typography ramp

| Name | Use | Tailwind |
|---|---|---|
| `display` | Page anchor headline (status hero) | `text-[28px] font-bold leading-tight tracking-tight sm:text-[32px]` (≥28px on all widths — Richness Floor) |
| `title` | Surface heading | `text-base font-semibold` |
| `feature` | Emphasized continuity body | `text-lg font-semibold leading-7` |
| `body` | Standard copy | `text-sm leading-6` |
| `body-lg` | Public mobile body / inputs (≥16px, no zoom) | `text-base leading-6` |
| `label` | Field label, hero eyebrow | `text-sm font-semibold` |
| `section` | Uppercase section label (§8) | `text-[13px] font-semibold uppercase tracking-wider` |
| `meta` | Timestamps, secondary metadata | `text-xs text-muted-foreground` |
| `micro` | Pills, badges | `text-[11px] font-semibold leading-none` |

Color pairs with the ramp via tokens: primary text `text-foreground`, secondary
`text-muted-foreground`. Do not introduce sizes outside this table.

## Elevation scale

Cards are border-first, not shadow-first (per visual doctrine: minimal shadow).

| Name | Use | Tailwind |
|---|---|---|
| `flat` | Standard cards, utilities | _(no shadow; border only)_ |
| `raised` | Status hero, real continuity card | `shadow-sm` or `shadow-[0_1px_2px_rgba(16,36,62,0.04)]` |
| `overlay` | Future modals/toasts only | `shadow-lg` |

## Radius scale

| Element | Tailwind |
|---|---|
| Pills, chips, round buttons | `rounded-full` |
| Buttons, fields, small blocks | `rounded-lg` |
| Cards | `rounded-xl` |
| Outer hero / page-level container | `rounded-2xl` |

## Spacing & rhythm

| Property | Value |
|---|---|
| Page padding | `px-4 py-6 sm:py-10` |
| Surface gap (between blocks) | `space-y-4 sm:space-y-5` |
| Card internal padding | `px-5 py-5 sm:px-6` |
| In-card stack gap | `space-y-3` |
| Max content width | `max-w-2xl` |
| Tap target min height | `min-h-[42px]` (≥44px preferred) |

## Color

Defer to `ux-design-model-v1.md` token tables — not duplicated here. The only spec
rule added: a component references a token by class (`bg-[var(--keep-accent-bg)]`),
never a raw hex, except the data-driven timeline icon map.

---

# Part 2 — States & Feedback

What separates a product from a prototype: every interactive surface acknowledges
loading, pending, success, error, and empty. The composer
(`CustomerQuickActions.tsx`) already implements these well — this section codifies
those patterns as the standard.

## Loading / skeleton

Loading is a **skeleton of the real surfaces**, not a spinner. Skeletons must
mirror the separated-surface structure (Rule 1), not the old single-container
layout. `loading.tsx` is currently stale and must be rebuilt to match.

```
block     animate-pulse rounded-xl border border-[var(--ophalo-border)] bg-card px-5 py-5
bar       h-4 w-full rounded bg-muted   (vary width: w-3/4, w-4/5 for natural rhythm)
pill      h-5 w-24 rounded-full bg-muted
gap       container uses the same space-y-4 sm:space-y-5 as the live page
```

## Pending

In-flight action: the button label changes (`Sending…`) and the control is
disabled for the duration. No layout shift.

## Success confirmation

Replace the composer with a teal confirmation surface; announce it; offer a reset.

```
wrapper   rounded-lg border border-[var(--ophalo-border)]
          bg-[var(--keep-accent-bg)] px-4 py-4   + aria-live="polite"
title     text-base font-semibold text-foreground   ("Delivered to {business}.")
body      mt-1 text-base leading-6 text-muted-foreground
reset     mt-3 text-xs text-muted-foreground underline underline-offset-2
          hover:text-foreground   ("Send another message")
auto-dismiss  clears after 5s
```

## Error

Inline, announced, with copy mapped from the failure — never a raw status code.

```
region    aria-live="polite" empty:hidden
text      text-base text-destructive
```

Status→copy map (the `resolvePostError` pattern): 410/401/403/404 → "This request
link is no longer active." · 400 → "Message can't be empty." · 429 → "Please wait a
moment and try again." · network → "…check your connection and try again." ·
default → "Message not sent. Please try again."

## Empty

Empty is a designed state, not a blank gap.

- No business update yet → the warm neutral trust card (Continuity card §2).
- No timeline yet → `body` copy ("No updates yet."), not an empty container.

## Form validation

Owned by `KeepField` / `KeepTextarea` via the `error` prop — do not re-implement.

```
error border   border-[var(--ophalo-danger)]   (replaces the neutral border)
error message  mt-1 text-xs font-medium text-[var(--ophalo-danger)]
required mark  ml-1 text-[var(--ophalo-danger)]   ("*")
```

Validation copy is field-specific and appears beneath the field.

---

# Part 3 — Motion & Accessibility

## Motion

One transition contract. Motion is for state feedback, never decoration.

| Property | Value |
|---|---|
| Default | `transition-colors` (hover/focus on buttons, chips, links) |
| Broader state change | `transition` (border + ring on fields) |
| Duration / easing | Tailwind default (~150ms, ease) — do not hand-tune per component |
| Reduced motion | Honor `prefers-reduced-motion`; skeleton `animate-pulse` is acceptable but no large movement |

## Accessibility

| Concern | Rule |
|---|---|
| Focus ring (one spec) | `focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2` |
| Live regions | Success/error use `aria-live="polite"` |
| Icon-only controls | Forbidden on mobile without an accessible label |
| Hidden labels | Use `sr-only` for `<dt>`/visual-only labels |
| Contrast | Body/meta verified against canvas and card; disabled states still legible |
| Target size | `min-h-[42px]` per WCAG 2.2 target size |
| Keyboard | All controls reachable in logical order; no focus traps |

---

# Part 4 — Component Recipes

Each: anatomy → Tailwind → states → volume level → never-do. All values reference
Part 1 scales and `globals.css` tokens.

## 1. Status hero — Level 1

The single page anchor and the page's one elevated, filled surface (Richness Floor).
A Keep identity surface, not a marketing hero (no imagery, no oversized empty space) —
but never a flat strip. Largest type on page; the surface that most clearly lifts off
the canvas.

**Anatomy:** top accent bar · business eyebrow · status headline · subline ·
privacy trust line · copy-link · metadata footer (status chip · reference, below a
faint `border-t` divider). The subline is lifecycle reassurance derived only from
the status code — never business-written text (that belongs to the continuity card
and timeline) and never promissory follow-up copy.

```
wrapper   overflow-hidden rounded-2xl border border-[var(--ophalo-border)]
          bg-[var(--keep-accent-bg)] shadow-sm
top bar   h-1.5 bg-[var(--keep-accent)]
body      px-5 pb-6 pt-5 sm:px-6
eyebrow   label  → text-foreground
headline  display  → text-foreground   (StatusHeadline copy)
subline   body  → text-muted-foreground
trust     mt-3 inline-flex items-center gap-1.5 text-xs font-medium text-[var(--keep-accent)]
```

**States:** static; meaning carried by headline copy + status chip below.
**Volume:** Level 1.
**Never:** more than one per page · plain white surface · headline below `display`.

## 2. Continuity card — Level 2

The most important differentiator on the customer page. Two distinct states.

**Real business update:**

```
wrapper   rounded-xl border border-[var(--ophalo-border)]
          border-l-4 border-l-[var(--keep-accent)]
          bg-[var(--keep-accent-bg)] px-5 py-5 shadow-[0_1px_2px_rgba(16,36,62,0.04)]
badge row flex flex-wrap items-center gap-2
          → KeepBadge variant="teal"  "Latest from {business}"
          → updated-ago pill: rounded-full bg-white/70 px-2 py-0.5 micro text-[var(--keep-accent)]
body      feature  → text-foreground
date      mt-3 meta
```

**No update yet** (reassurance, not a communication):

```
wrapper   rounded-xl border border-[var(--ophalo-border)] bg-card px-5 py-5
icon dot  flex h-8 w-8 shrink-0 items-center justify-center rounded-full
          bg-[var(--keep-accent-bg)]   (ShieldCheck h-4 w-4 text-[var(--keep-accent)])
title     body font-semibold text-foreground
body      mt-1.5 body text-muted-foreground
```

**Volume:** Level 2.
**Never:** teal background on the no-update state · equal weight with the timeline.

## 3. Standard card — Level 2/3

The neutral baseline; its quietness makes the teal surfaces read as special.

```
wrapper   rounded-xl border border-[var(--ophalo-border)] bg-card px-5 py-5
title     sentence-case title  → text-base font-semibold text-foreground  (no uppercase label — Rule 4)
body      body text-foreground
meta      mt-2 meta
```

**Never:** any accent color · card nested inside card · chat-bubble look · an uppercase
section label (Rule 4).

## 4. Composer — Level 2

Message field + primary send. Send is an OpHalo brand primary action — **navy-filled
when enabled** (`variant="primary"`).

```
field     KeepTextarea  (or raw textarea where a ref/no-label is required —
          must match: bg-card border-[var(--ophalo-border)] rounded-lg
          focus:border/ring-[var(--keep-accent)] text-base resize-none min-h-[108px])
hint      meta   (shown while disabled: "Add a message to send it to {business}.")
send      KeepButton variant="primary" className="w-full"
```

**States:** enabled = navy filled (`bg-[var(--ophalo-navy)] text-white`) · disabled
(empty/pending) = bordered muted (`border-[var(--ophalo-border)] bg-muted
text-muted-foreground`) — this is the correct resting look, not a bug · pending =
label "Sending…" · success/error per Part 2.

> Note: the gray Send seen with an empty composer is the **correct disabled
> state**. Enabled Send is navy (OpHalo brand), not teal.

**Volume:** Level 2.
**Never:** teal Send on this surface · an enabled Send that looks disabled.

## 5. Secondary action button — quick actions

Teal as **accent only**: teal icon, neutral border, teal hover/focus. Never filled.

```
button    inline-flex min-h-9 items-center gap-1.5 rounded-full border px-3
          text-xs font-semibold transition-colors + focus ring (Part 3)
          resting:  border-[var(--ophalo-border)] bg-card text-foreground
                    hover:border-[var(--keep-accent)] hover:bg-[var(--keep-accent-bg)]
                    hover:text-[var(--keep-accent)]
          selected: border-[var(--keep-accent)] bg-[var(--keep-accent-bg)] text-[var(--keep-accent)]
          sent:     border-[var(--ophalo-border)] bg-muted text-muted-foreground  + "Sent"
icon      h-3.5 w-3.5 shrink-0 text-[var(--keep-accent)]
```

**Never:** filled teal/navy · icon-only on mobile without a label.

## 6. Timeline item — Level 3

Log style, not chat. Rhythm from the connector rail, icon dots, type badges, newest
marker.

```
list      relative space-y-3 border-l border-[var(--ophalo-border)] pl-3
item      relative flex gap-4 rounded-lg px-3 py-3
          newest: border border-[var(--keep-accent)] bg-[var(--keep-accent-bg)]
          else:   border border-transparent
rail      newest only: absolute -left-px top-3 h-[calc(100%-1.5rem)] w-1
                       rounded-r-full bg-[var(--keep-accent)]
icon dot  mt-1 flex h-8 w-8 shrink-0 items-center justify-center rounded-full
          (bg + icon color from the kind→token map below)
badges    "Newest" pill + KeepBadge for event type   (Rule 2)
label     body font-medium text-foreground
message   body text-foreground   (status note: body italic text-muted-foreground)
time      mt-2 meta
```

Event-type → token map (drives icon dot and type badge):

| kind | meaning | token family |
|---|---|---|
| customer_* | customer communication | `--keep-accent` |
| business_update | business sent an update | `--ophalo-success` |
| customer_no_longer_needed | closed | `--ophalo-attention` |
| status_set | status note | muted |
| _unknown_ | safe fallback | muted (never throw) |

**Volume:** Level 3.
**Never:** chat bubbles · decorative card per row · equal weight with continuity.

## 7. Utility footer — Level 4

Quiet by construction. No card. Sits on the canvas.

```
container space-y-4 pt-2
label     §8 section label (muted)
body      body text-muted-foreground
save      KeepButton variant="teal" — emphasised only after a value changes; quiet at rest
fine      meta   (phone on file, submit-new link)
```

**Never:** card treatment · loud buttons · competing with composer or status.

## 8. Section label

A divider, not an anchor. Pair with a count chip where a count is meaningful.

```
label     section ramp → text-muted-foreground
count     rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground
```

**Never:** more than one per surface · the only hierarchy cue.

## 9. Status chip

Always `KeepBadge` with a mapped variant. Reconciles the hand-rolled
`CustomerStatusChip`.

| customerStatus | KeepBadge variant | label |
|---|---|---|
| in_progress | `teal` | Active |
| needs_your_reply | `attention` | Needs your reply |
| complete | `success` | Complete |
| no_longer_needed | _default (muted)_ | No longer needed |
| _unknown_ | _default (muted)_ | safe fallback, never throw |

**Never:** an inline-styled chip · a color outside this table.

## 10. Mobile spacing rules

Defined in Part 1 (Spacing & rhythm). Release checks per `ux-design-model-v1.md`:
verify at 320 / 375 / 390 / 430 / 768 / desktop — no horizontal scroll, visible
focus, no clipped text, first useful content not pushed far down.

---

# Part 5 — Composition Example: Customer Request Page

```
main  bg-background px-4 py-6 sm:py-10
└ mx-auto w-full max-w-2xl space-y-4 sm:space-y-5
   ├ Status hero            §1   Level 1   (metadata footer: status chip §9 · reference)
   ├ Continuity card        §2   Level 2
   ├ Composer               §4   Level 2   (+ Secondary actions §5)
   ├ Standard card          §3   Level 2/3 ("What you sent")
   ├ Timeline               §6   Level 3
   └ Utility footer         §7   Level 4   (no longer needed · email · fine print)
```

Migration replaces the single `rounded-2xl` wrapper and its `border-t` dividers in
`r/[pageToken]/page.tsx` with this separated-surface structure.

---

# Part 6 — Migration

Applied surface by surface. **Customer request page is surface #1.**

1. Replace the single outer card + `border-t` dividers with separated surfaces on
   canvas (Rule 1).
2. Rebuild `loading.tsx` to mirror the separated surfaces (Part 2).
3. Replace hand-rolled `CustomerStatusChip` with `KeepBadge` §9.
4. Replace hand-rolled timeline type badges with `KeepBadge` §6.
5. Reconcile the composer send button into `KeepButton` §4.
6. Remove inline `style={{}}` visual properties (Rule 3); keep only data-driven
   token lookups (timeline icon map).
7. Bump status headline to the `display` ramp.

Subsequent surfaces: request detail, request list, public intake — each migrated in
its own session.

---

# Appendix

**Icons.** Lucide, `h-4 w-4` standard (`h-3.5 w-3.5` in chips). Practical only —
plus, search, clock, alert, send, check, link/eye. No decorative icons. Icon-only
controls require an accessible label.

**Content formatting.** Dates via the page helpers (`formatDateShort`,
`formatDateFull`, `formatDaysAgo`) — all return `null` on bad input and callers
must guard. Reference codes render `K-{code}` in `font-mono`. Empty values fall back
to a designed state, never a blank.

**Operator surfaces.** Request list and request detail use **navy** page-level
primary (New request), not teal; teal stays reserved for communication actions.
These recipes are customer-surface tuned — apply the same scales, swap the
page-primary token, when migrating operator surfaces.

**Governance.** Stable: the four `components/keep/*` primitives + these 10 recipes.
Deferred: consolidating `components/keep/` into one shared package across both apps
(both currently carry local copies). Until then, changes to a primitive must be
mirrored.
