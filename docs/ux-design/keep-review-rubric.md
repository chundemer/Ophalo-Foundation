# Keep Review Rubric

**Status:** Active
**Date:** 2026-06-08
**Scope:** The single review gate for OpHalo and Keep surfaces — visual judgment,
production-readiness scoring, and the public-availability release gate.

Supersedes and merges the former `visual-polish-rubric.md`,
`ux-design-production-readiness.md`, and `ux-design-deployment-checklist.md`.

---

## Purpose

This is the **review layer**. It answers one question: *is this surface production-
ready, or does it still read like a prototype?* Use it on screenshots, local builds,
and pre-deployment changes.

It does not restate token values, type sizes, or recipe class strings. Those live in
`ux-design-model-v1.md` (tokens, voice, Richness Floor) and `keep-component-spec.md`
(recipes, scales, structure). This rubric checks a surface *against* those — when it
disagrees with them, they win (see README precedence).

**How to use this document:**

1. **Binary Gate Checklist** — all items must pass before any scoring begins.
2. **Gate 2** — score each universal category; a 3 is required in each.
3. **Per-surface criteria** — apply the surface-specific pass conditions.
4. **Gate 3** — release sign-off.

A surface that fails the checklist is not production-ready regardless of scores.

---

## Binary Gate Checklist

**All items must pass before any scoring begins.**

Each item is tagged:
- **(G)** — grep the source file; the check is a count or a specific class string
- **(S)** — verify from the component structure; no running app required
- **(V)** — run the app or inspect a screenshot; the criterion names the specific
  element or state to find

No item uses judgment words (clear, good, appropriate, visible enough).
Each either passes or it does not.

### Code checks

- [ ] **(G) No raw hex in inline styles.** `grep 'style={{.*#'` returns zero hits.
  The only allowed `style` prop is a data-driven token lookup (e.g. timeline icon dot
  map using a const); inline hex strings in `style={{}}` are not allowed.

- [ ] **(G) No retired teal.** `grep '#159DB8'` returns zero hits.

- [ ] **(G) Section-ramp uppercase is absent.** Search for the combined pattern
  `uppercase` with `tracking-wider` (not `tracking-widest`). Result must be zero hits.
  The section ramp (`tracking-wider`) must not appear on the hero, continuity card,
  standard cards, composer, or timeline. Note: `tracking-widest` (the hero
  reference-code eyebrow) is a separate deliberate pattern and does not fail this check.

- [ ] **(G) Exactly one display anchor.** `grep -c 'text-\[28px\]'` returns exactly
  one. That element is the status headline. No other element uses an explicit font size
  of 28px or larger.

- [ ] **(G) Hero background is tinted.** The status hero wrapper includes
  `bg-[var(--keep-accent-bg)]`. Not `bg-white`, not `bg-card`, not `bg-background`.

- [ ] **(G) Hero has elevation.** The status hero wrapper includes `shadow-sm`.

- [ ] **(G) Hero has a saturated teal bar.** The hero contains
  `h-1.5 bg-[var(--keep-accent)]`. This is the one fully-saturated teal element.
  `bg-[var(--keep-accent-bg)]` washes alone do not satisfy this item.

- [ ] **(G) Page background is not overridden.** No `<main>` or `.page-shell`
  element adds `bg-white`, `bg-card`, or `bg-[var(--ophalo-card)]`. The canvas
  (`bg-background` on `body`) must show through between cards.

- [ ] **(S) Badges use `KeepBadge`.** No hand-rolled `<span>` or `<div>` with
  `rounded-full` pill classes substitutes for a named status state. All event-type and
  status badges render `<KeepBadge`.

- [ ] **(S) Buttons use `KeepButton`.** No named action uses a hand-rolled button
  element. Exception: the §5 quick-action pill pattern
  (`inline-flex rounded-full border px-3`) which has its own spec.

- [ ] **(S) Customer tracker Send uses `variant="primary"`.** The Send and Submit
  feedback buttons on the customer tracker are `<KeepButton variant="primary"` (OpHalo
  brand navy). They do not use `variant="teal"` or any hard-coded color class.

- [ ] **(S) No nested primary card surfaces.** No `rounded-xl` or `rounded-2xl` card
  element wraps another `rounded-xl` or `rounded-2xl` element as a primary operational
  surface. Small inset `rounded-lg` blocks for receipts or metadata are the only
  allowed exception.

### Visual checks

Run the app or inspect a screenshot at 390px viewport unless stated.

- [ ] **(V) Canvas is visible between cards.** The warm cream background appears as a
  visible gap between the hero and the card below it, and between subsequent cards.
  Cards do not share a border or touch edge-to-edge.

- [ ] **(V) Hero background is distinct from all other cards.** The hero's teal-tinted
  background is a visibly different color than every `bg-white` surface below it. No
  other primary card on the page is tinted.

- [ ] **(V) Disabled Send is visibly inactive.** With an empty composer, the Send
  button shows a muted, bordered appearance — not teal, not navy. The button does not
  look like a ready-to-click primary action.

- [ ] **(V) Enabled Send is navy-filled.** After typing any text in the composer, the
  Send button becomes `bg-[var(--ophalo-navy)]` filled with white text (OpHalo brand
  navy). Not teal, not gray.

- [ ] **(V) No horizontal scroll at 320px.** At exactly 320px viewport, no horizontal
  scrollbar appears. All cards, badges, and buttons are within the viewport.

- [ ] **(V) Hero is in the first viewport.** At 390px on initial page load, the hero
  is visible without scrolling. No spinner or blank space larger than a skeleton block
  pushes it below the fold.

---

## Gate 1 — Production Richness Floor (hard fail)

The binary checklist covers the floor. This table maps each floor requirement to the
checklist items that verify it — use it to diagnose what specifically failed.

| Floor requirement | Checklist items that cover it |
|---|---|
| One type anchor ≥28px | G: Exactly one display anchor |
| One elevated, filled surface | G: Hero background is tinted · G: Hero has elevation |
| One saturated teal moment | G: Hero has a saturated teal bar |
| Uneven rhythm | V: Hero background is distinct · V: Canvas is visible between cards |
| Two-tone neutrals | G: Page background is not overridden · V: Canvas is visible between cards |
| No repeating uppercase labels | G: Section-ramp uppercase is absent |
| No strokes-on-canvas flatness | G: Hero background is tinted · G: Hero has elevation |

If the checklist passes, Gate 1 passes. If a floor concept feels violated but all
checklist items pass, the checklist is authoritative — update the checklist rather
than overriding it with prose judgment.

---

## Diagnostic Context

The sections below describe *why* surfaces fail and what to look for. They are not
gate criteria — the checklist is.

### The "too plain" vs "over-designed" signals

OpHalo polish comes from correct hierarchy, visible product identity, clear affordance,
calm trust states, restrained-but-floored volume, consistent rhythm, and token-backed
state meaning.

**Too plain** (the usual failure):

- every section has the same visual weight
- most sections are white/gray/muted with little Keep identity
- the first viewport lacks a clear anchor
- all panels share one border, padding, and type treatment
- trust/status information is styled like secondary metadata
- primary actions don't stand apart from secondary
- business updates aren't distinct from logs/records
- empty/waiting states appear as blank gaps
- section labels repeat without a stronger content anchor
- the page relies on repeated borders instead of deliberate rhythm
- it feels technically functional but not confidently finished

Fix by identifying the intended hierarchy and raising volume on the anchor — not by
adding decoration.

**Over-designed** (the overcorrection):

- shadows, gradients, illustrations, or icons carry hierarchy instead of content
- it starts to feel like a generic SaaS dashboard
- operational screens use large decorative hero sections
- timeline rows become chat bubbles or standalone decorative cards
- accent colors wash whole sections without semantic purpose
- utility actions become louder than primary communication actions
- spacing is so large the user scrolls past low-value content

Fix by reducing decoration and letting structure, copy, and state meaning carry the
page.

---

## Visual Volume Scale

Volume prevents every section from competing equally.

| Level | Use | Treatment |
|---|---|---|
| **1 — Page anchor** | Main status, primary title, key trust state | Strongest type; first viewport; the one elevated/filled surface; not repeated |
| **2 — Primary content** | What the user came to verify or act on (latest update, request summary, composer, attention cards) | Clear card/panel; readable body; strong primary action; distinct from logs/utilities |
| **3 — Supporting** | Logs, metadata, secondary summaries (timeline, submitted/reference metadata, active previews) | Compact type, quiet metadata; scannable but not competing |
| **4 — Utility** | Preferences, secondary options, filters, bookkeeping (email prefs, mark-no-longer-needed, copy link, filters, internal notes) | Quiet by default; usable but not prominent |

**Balance rule:** one Level 1, one to three Level 2, quieter Level 3/4. A screen is
too plain when Levels 1–4 all look similar.

---

## Gate 2 — Review score

| Score | Meaning | Deployment posture |
|---|---|---|
| 0 | Broken or unclear | Block |
| 1 | Understandable but inconsistent or plain (reads like a wireframe with production copy) | Do not deploy as final |
| 2 | Right anchor and content order; needs visual-volume tuning | Deploy only with explicit exception |
| 3 | Production-ready: clear Level 1 anchor, distinct Level 2 areas, quieter Level 3/4, passes the Floor, no decorative overcorrection | Pass |

**Production-ready** requires a 3 in *every* universal category below **and** a clean
binary checklist.

### Universal categories (3 required in each)

**1. Purpose & hierarchy** — first viewport makes purpose obvious; most important
info before secondary controls; section order matches the user job; empty states
explain what's next; metadata present but quiet.

*Specific check (S):* DOM order from top is: hero → (badge row) → continuity or
request card → attention card (if present) → composer → utility controls → timeline
→ footer. No utility section appears before the timeline.

*Fail:* trust moments render as tiny divider labels; equal weight everywhere; settings
compete with the main action.

---

**2. Action hierarchy** — button roles match the model-v1 component contract (navy
page-level and customer-send primary, teal operator-communication primary, navy outline
secondary, quiet bookkeeping, red destructive); disabled states are visibly disabled,
not weak enabled buttons.

*Specific check (S):* On **customer-facing surfaces** (customer tracker, public intake),
`<KeepButton variant="primary"` is used for Send and Submit — OpHalo brand navy. On
**operator surfaces** (request detail, request list), `<KeepButton variant="teal"` is
used for Send update — Keep brand teal. No surface mixes the two send styles.

*Fail:* a primary communication button looks pale/weak; save/settings compete with
the main action; page-local button styles introduce a new hierarchy.

---

**3. Token & component use** — colors from `--ophalo-*` / `--keep-*` tokens; shared
primitives/recipes for buttons, fields, badges, cards; no retired `#159DB8`; no local
teal constants; no inline `style` for visual props; focus states token-backed.

*Specific check (G):* `grep 'style={{[^}]*#'` returns zero hits outside data-driven
const lookups. `grep '#159DB8'` returns zero hits. `grep 'text-\[#\|bg-\[#\|border-\[#'`
returns zero hits — all color values reference `var(--...)` tokens.

*Fail:* inline hex decides styling; similar badges/buttons differ across surfaces;
retired teal appears anywhere.

---

**4. Mobile & field readiness** — works at 320 / 375 / 390 / 430 / 768px and
desktop; no horizontal scroll; text wraps before clipping; practical tap targets;
first useful content appears quickly; filters/settings don't dominate mobile.

*Specific check (G):* Public-facing `<textarea>` and `<input>` elements use
`text-base` (≥16px) to prevent iOS auto-zoom. Primary action `<KeepButton` components
enforce `min-h-[42px]`.

*Specific check (V):* At 320px: (1) hero is visible without scrolling, (2) composer
textarea is focusable without triggering zoom, (3) no horizontal scrollbar at any
scroll position.

*Fail:* search/filters push the first work item down; badges crowd the name; inline
actions wrap awkwardly.

---

**5. Accessibility** — keyboard reaches all controls in logical order; focus visible
on white/warm/tinted/dark surfaces (the one focus-ring spec); labels tied to fields;
error/success visible and announced in context; state never by color alone; contrast
checked.

*Specific checks (G):*
- Icon-only `<button>` elements have `aria-label` or a child `<span className="sr-only">`.
- Success/error feedback containers include `aria-live="polite"`.
- Interactive controls use `focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2`.

*Fail:* focus is only a subtle border change; disabled vs enabled hard to tell;
icon-only controls lack labels.

---

**6. Visual polish** — consistent spacing rhythm; intentional volume (Levels don't
all look the same); visible Keep identity where communication/continuity matters; real
update states distinct from records/metadata/waiting; cards share radius/border
treatment; distinct weights for label/body/meta/action; tinted surfaces used
intentionally; warm and practical, not generic SaaS.

*Specific checks (V):*
- The newest timeline item is visually distinct from older items — teal border and
  tinted background per the spec.
- Status badges use the correct semantic color: amber for attention, blue for
  active/new, green for complete/sent. No named state uses a default gray badge.
- Utility controls (bookkeeping, close request, email preferences) have no card
  wrapper — they sit directly on the canvas or in a quiet container, visually quieter
  than the cards above.

*Specific check (S):* All card outer wrappers use `rounded-xl` (standard) or
`rounded-2xl` (hero). No primary card uses `rounded-lg` as its outermost class.

*Fail:* divider-heavy wireframe feel; too many equal-weight elements; Keep surfaces so
neutral they feel generic; real updates look like ordinary logs; empty sections look
accidental.

---

## Per-surface criteria

Each surface lists its trust-question order, recommended volume, and specific pass
conditions.

### Customer request page

Trust-question order: (1) current status → (2) did the business get the right
request → (3) has the business shared an update → (4) what can I do → (5) what's
happened → (6) preferences / close the loop.

| Area | Volume |
|---|---|
| Current status header (the elevated anchor) | 1 |
| Latest business update / no-update trust state | 2 |
| Message composer / quick actions | 2 |
| Original request summary | 2–3 |
| Timeline | 3 |
| No-longer-needed / email preferences | 4 |

**Pass when (specific):**
- (S) Hero wrapper: `overflow-hidden rounded-2xl border border-[var(--ophalo-border)] bg-[var(--keep-accent-bg)] shadow-sm`. Top bar: `h-1.5 bg-[var(--keep-accent)]`.
- (S) "No business update yet" uses a white `bg-card` or `bg-white` card with a
  `bg-[var(--keep-accent-bg)]` icon dot — not a teal-tinted card wrapper.
- (S) "Real business update" uses a left-rail card:
  `border-l-4 border-l-[var(--keep-accent)] bg-[var(--keep-accent-bg)]`.
- (S) Send button: `<KeepButton variant="primary"` (navy). Disabled state is visibly muted.
- (V) Email preferences section has no card wrapper and is visually quieter than the
  composer above it.

*Too flat if status, details, actions, timeline, and utilities all share one weight.*

---

### Request list

Trust-question order: who needs attention → why → what next → which open requests
are calm.

| Area | Volume |
|---|---|
| Page title + New request | 1 |
| Needs attention section + cards | 2 |
| Active section + cards | 2–3 |
| Filters / search | 4 unless actively filtering |
| Closed / history | 3–4 |

**Pass when (specific):**
- (S) Needs Attention section is absent (not empty-headed) when count is zero.
- (V) At 390px, at least one Needs Attention card is visible without scrolling past the
  page title and section header.
- (V) Active cards are visually quieter than Needs Attention cards.
- (V) Filters and search are not the first or dominant element at 390px.

*Too flat if filters/active compete with Needs attention.*

---

### Request detail

Trust-question order: who is this for → what state → what customer-visible action
next → what context → what happened over time.

| Area | Volume |
|---|---|
| Customer / status header | 1 |
| Send update / customer-visible action | 2 |
| Request context (what is this about) | 2–3 |
| Timeline | 3 |
| Utility row / bookkeeping | 4 |

**Pass when (specific):**
- (S) DOM order: hero → request card → attention card (if present) → composer →
  utility row → timeline. Utility row is below the composer.
- (S) `<KeepButton variant="teal"` is used for the Send update action.
- (S) Request card `<dt>` elements contain no `uppercase tracking-wider` classes.
  Field labels are sentence-case or visually lightweight.
- (V) At 390px, customer name, request context, and composer are accessible without
  excessive scrolling.

*Too dashboard-like if facts, timeline, notes, and actions all become equal cards.*

---

### Public intake

Trust-question order: which business → what's required → how do I send → can I get
updates.

| Area | Volume |
|---|---|
| Business name / intro | 1 |
| Required contact + request fields | 2 |
| Submit action | 2 |
| Email updates | 3–4 |
| Recent request resume | 4 unless it's the main intent |

**Pass when (specific):**
- (S) Submit button is `<KeepButton variant="teal"`.
- (G) All `<input>` and `<textarea>` elements use `text-base` (≥16px).
- (V) At 320px, all form fields are fully visible with no horizontal clipping.

---

### Operator new request

**Pass when (specific):**
- (V) The success/share state uses a teal-accented or teal-tinted surface — not a
  plain white card.
- (V) The operator can complete new request creation and reach the share link without
  more than 3 action steps.

---

### Marketing website

**Pass when (specific):**
- (V) The customer-silence problem is described in the first viewport without
  scrolling.
- (S) Parent OpHalo sections use `--ophalo-*` tokens for identity; no `--keep-accent`
  is used on parent-brand sections.

---

## Screenshot review requirements

For each reviewed surface, capture/inspect: 320px, 390px, 768px, desktop; keyboard
focus on the primary action; the empty / no-update state; the error state where
applicable; the success / sent state where applicable.

---

## Gate 3 — Release sign-off (public availability)

Before deployment lock, each surface touched by the release must pass the binary
checklist and score 3 in every universal category.

Required surfaces: public intake, customer request page, operator new request, request
list, and any marketing/auth path in the release. Request detail must score 3 or carry
a documented exception.

Any score below 3 requires one of:

- fix before deployment,
- a written exception with owner and follow-up session, or
- an explicit decision to keep the surface out of public availability.

Cross-surface checks (run with all surfaces side by side):

- They share one OpHalo foundation and feel like the same product family.
- Keep surfaces clearly use Keep teal for identity / continuity cues.
- Attention, active, success, and failure states are visually consistent (locked color
  map).
- The product feels professional and field-ready — not generic SaaS, not decorative,
  not overly plain.
- Brand/voice: name is always OpHalo; tagline exact; Keep described as continuity, not
  CRM/chat/workflow/ticketing; default action copy is "Send update," never "Reply."

---

## Better prompt for "too plain"

Instead of "this is too plain," say:

> This surface fails the visual hierarchy gate. It uses correct tokens, but the
> sections have equal weight, so it reads like a wireframe with production copy.
> Identify the Level 1 anchor, the Level 2 primary content, the Level 3 supporting
> content, and the Level 4 utilities, then apply the Production Richness Floor —
> one ≥28px anchor, one elevated/filled surface, one saturated teal moment, uneven
> rhythm, two-tone neutrals — without adding decorative SaaS styling.
