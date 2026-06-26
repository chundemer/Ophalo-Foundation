# UX Design Model v1

**Status:** Active
**Date:** 2026-06-04
**Scope:** OpHalo parent brand, product Halo styling model, and Keep UX implementation contract

---

## Purpose

This model defines the active visual, copy, and interaction contract for OpHalo and Keep.

OpHalo is the parent brand. Keep is the first product Halo. Future products must share the OpHalo foundation while receiving their own product namespace and accent tokens.

---

## Brand Foundation

### Written Name

The written name is always **OpHalo**.

Never use:

- OPHALO
- Ophalo
- Op Halo
- op halo
- ophalo

The logo may use stylized letterforms. Product copy and UI text always use **OpHalo**.

### Tagline

The parent tagline is:

```text
See the gaps. Close them.
```

Use this exact wording and punctuation.

### Audience

OpHalo is built for small service businesses.

Primary users include owners, office staff, operators, and field employees at businesses that often run from the phone, truck, counter, and job site.

Preferred public phrases:

- Built for small service businesses.
- Made for businesses that run from the phone, truck, and job site.

Avoid language that makes OpHalo sound enterprise-first, Silicon Valley-first, or built for teams with dedicated software administrators.

---

## Product Architecture

### Parent Brand

OpHalo is the company and product system.

OpHalo helps small businesses see operational gaps that cost trust, revenue, customers, or time, then close those gaps with focused products.

### Product Family

Each focused product is a **Halo**.

Each Halo should:

- solve one specific business gap
- have a clear product job
- work alongside existing business habits
- avoid becoming a broad management platform
- use a dedicated product accent color inside the OpHalo visual system

### Keep

First reference: **OpHalo Keep**

Subsequent references: **Keep**

Keep is a customer continuity communication layer for small service businesses.

Keep helps a business answer:

```text
What customer needs attention right now, and how can I keep them informed quickly?
```

Keep is not a CRM, workflow engine, job tracker, chat inbox, reminder app, or dashboard.

---

## Voice And Copy

OpHalo copy should be:

- short
- direct
- human
- calm
- specific
- practical

The voice should feel like a calm dispatcher or trusted operator, not a marketing team, chatbot, or enterprise software manual.

### Preferred Keep Terms

Use:

- Requests
- Needs attention
- Active
- Send update
- Mark handled
- Customer page
- Open request
- Closed request

Avoid:

- Inbox
- Ticket
- Thread
- Pipeline
- Workflow
- Optimize
- SLA performance
- Customer interactions

### Action Copy

Use **Send update** for the default customer-visible action.

Use **Answer customer** only when the customer has explicitly asked a question and the product context benefits from that specificity.

Avoid **Reply** as the default action because it makes Keep feel like chat.

Avoid **Acknowledge** as a primary concept because Keep is not read-receipt or acknowledgement tracking.

---

## Visual Doctrine

OpHalo interfaces should look professional enough to build trust and practical enough to use in the field.

The product should feel like:

```text
These apps are essential to my business. They do not tell me how to work. They work with how we work.
```

It should not feel like:

```text
Another system I have to manage.
```

### Interface Principles

1. Confidence over complexity.
2. Field-ready over decorative.
3. Warm restraint over generic SaaS.
4. Continuity over management.
5. Professional polish through consistency.

### Visual Tone

OpHalo should feel clear, sturdy, trustworthy, modern but not trendy, warm but not soft, and simple but not empty.

Avoid:

- heavy gradients
- decorative blobs or orbs
- overly large empty spacing on operational screens
- cartoon illustrations
- cute mascots
- chat-bubble-heavy UI
- dashboard charts unless the product job explicitly requires them
- dense enterprise tables as the default experience

### Typography (ADR-368)

The type system is a **serif headline over a sans body**. This pairing is what
separates a finished OpHalo surface from one that reads as a wireframe with real
data; all-sans headlines are the generic default the system rejects.

| Role | Face | Notes |
|---|---|---|
| Display / headings (H1–H3, card titles) | **Source Serif 4** | Weight 600. The brand voice — steady, trustworthy, established. Chosen over Fraunces because it holds up across the whole ramp (hero down to 20px card title) and stays robust on field/mobile screens. |
| Body, UI, forms, dense data | **Inter** | The readable workhorse. Carries long text, the Keep workbench, and the mobile app. |
| Wordmark / logo | **Poppins SemiBold**, outlined | Logo lockup only (`brand-kit/BRAND.md`). Never used as a live UI face. |

Rules:

- Headlines are Source Serif, never Inter. Body is Inter, never Source Serif.
- The serif carries voice; the sans carries the work. Do not blur the split.
- Headline scale steps down clearly from the H1 anchor (see the Richness Floor);
  hierarchy comes from the type ramp and surface weight, not decoration.
- Supersedes any prior "Headings & UI: Poppins" guidance in `brand-kit/BRAND.md`,
  which predates this contract and must be corrected to wordmark-only.

---

## Visual Hierarchy And Product Identity Rubric

OpHalo and Keep interfaces should not look decorative, but they also must not look like wireframes with real data.

Production polish comes from hierarchy, visible product identity, affordance, trust-building states, and restrained visual volume.

Use `keep-review-rubric.md` before page-polish work. It defines:

- how to tell whether a screen is too plain
- how to tell whether a screen is over-designed
- the Level 1-4 visual volume scale
- surface-specific hierarchy rules
- the pre-coding AI gate for visual polish sessions

The core test:

```text
Can the user tell what matters first, what they can do next, and what is secondary without the page relying on decoration?
```

Correct tokens alone do not make a screen production-ready. A page that treats status, content, actions, timeline, and utilities with equal visual weight is not production-polished.

### Keep Balance Rule

Keep uses restrained structure with visible continuity cues.

- Use neutral OpHalo structure for page canvas, records, metadata, dividers, and utilities.
- Use Keep teal for product identity, communication actions, customer-page communication cues, and real business-update highlights.
- Use `--keep-info` for active/new/supporting status.
- Use shared semantic colors for operational meaning: attention, success, danger.
- Do not wash the entire page in teal.
- Do not make Keep so neutral that it feels like a generic form or prototype.

### Production Richness Floor

Restraint is the baseline, not the ceiling. Every other rule in this model pushes
toward *less*; this one is the required floor of *more*. A screen that satisfies the
tokens, the voice, and the restraint rules but hits none of the items below is not
production-ready — it is a correctly-tokened wireframe. This floor is mandatory and
absolute: failing it fails review the same way wrong tokens do.

Every primary Keep surface must satisfy **all five**:

1. **One type anchor at 28px or larger.** Exactly one headline carries the page;
   everything else steps down from it. (Exact class: see `keep-component-spec.md`.)
2. **One elevated, filled surface.** At least one surface lifts off the canvas with
   real elevation *and* fill (tint or white-on-cream), not a border alone.
3. **One saturated teal moment.** One genuine `--keep-accent` presence per screen —
   not only pale `--keep-accent-bg` washes.
4. **Uneven rhythm.** The primary surface gets more space and size; utilities get
   less. Equal spacing on every block reads as a wireframe.
5. **Two-tone neutrals.** Cards must visibly lift off the canvas (`--ophalo-card` on
   `--ophalo-canvas`) — never card-on-card or one flat plane.

Two patterns are now explicit failures, not allowed defaults:

- **Repeating uppercase section labels.** A muted uppercase label above every block
  reads as wireframe annotation. Allowed only on the single quietest footer-weight
  block — never as a per-section default. Page hierarchy must come from surface
  weight, not from labels.
- **Strokes-on-canvas flatness.** A screen built entirely from thin borders, with no
  filled or elevated surface, fails the floor even if every token is correct.

This model defines *that* the floor is required. The structure that delivers it —
separated surfaces, the elevated-anchor recipe — is specified to the class in
`keep-component-spec.md`.

---

## Token System

### Naming Rule

Use `--ophalo-*` for parent brand and shared semantic tokens.

Use `--keep-*` for Keep-specific product tokens.

Future products must use their own product namespace, for example `--feedback-*`.

Do not use generic `--app-*` tokens for product identity. The product name should be searchable in code.

### Parent OpHalo Tokens

These values are locked for the parent foundation and shared neutral system.

| Token | Hex | Role |
|---|---:|---|
| `--ophalo-navy` | `#10243E` | Parent trust anchor, wordmark, parent navigation, company-level primary actions |
| `--ophalo-accent` | `#BF6B43` | Parent identity accent (terracotta, the halo). The OpHalo brand-axis lever vs Keep's teal (ADR-367) — marketing/parent surfaces only. Navy remains the structural anchor; terracotta is the accent, not a background wash. |
| `--ophalo-accent-bg` | _derived_ | `color-mix(--ophalo-accent 12%, --ophalo-canvas)`. Subtle terracotta tint surfaces. |
| `--ophalo-accent-hover` | _derived_ | `color-mix(--ophalo-accent 85%, black)`. Hover/pressed for terracotta controls. |
| `--ophalo-ink` | `#172033` | Primary text |
| `--ophalo-canvas` | `#F8F6F1` | Main website and PWA background |
| `--ophalo-card` | `#FFFFFF` | Cards, forms, request surfaces |
| `--ophalo-border` | `#DDD6C8` | Borders, dividers, rails, quiet structure |
| `--ophalo-muted` | `#5D6878` | Supporting text, metadata, timestamps |

Neutral palette values must not be tinted by product accent colors.

### Shared Semantic Tokens

These values are shared across products because the meaning is operational, not product-specific.

| Token | Hex | Role |
|---|---:|---|
| `--ophalo-attention` | `#C8741A` | Customer attention, continuity risk, waiting cues. Nudged to true amber from the prior `#B85B16` so it reads as an alert, distinct from the parent terracotta accent `#BF6B43` (ADR-368). Verify AA contrast on `--ophalo-attention-bg` during implementation. |
| `--ophalo-attention-bg` | `#FFF2D7` | Attention badges and subtle backgrounds |
| `--ophalo-success` | `#166534` | Sent, verified, resolved, complete |
| `--ophalo-success-bg` | `#E8F4ED` | Success chips and confirmation backgrounds |
| `--ophalo-danger` | `#B42318` | Failures, destructive states, severe overdue only |
| `--ophalo-danger-bg` | `#FEE4E2` | Error badges and recoverable failure messages |

### Keep Tokens

Keep teal is locked at `#168A9A`.

| Token | Hex | Role |
|---|---:|---|
| `--keep-accent` | `#168A9A` | Keep identity, communication cues, customer page, Keep-specific primary actions |
| `--keep-accent-bg` | `#E5F4F3` | Teal badge surfaces, communication highlights, latest-update surfaces when appropriate |
| `--keep-accent-hover` | `#107A90` | Hover and pressed state for Keep accent controls |
| `--keep-info` | `#244C95` | Active/new/supporting state |
| `--keep-info-bg` | `#EAF1FF` | Active/new badges and subtle backgrounds |

The retired teal candidate `#159DB8` must not be used for Keep.

### Color Use Rules

- Deep navy is the parent roof.
- Keep teal is Keep's product identity color.
- Amber (`--ophalo-attention`) is for customer attention and continuity risk, and must stay visually distinct from the parent terracotta accent so an alert never reads as brand decoration.
- Terracotta (`--ophalo-accent`) is the parent identity accent only; it is not a Keep accent and not a status color.
- Gold is not a brand color. Do not wire a gold/mustard value (e.g. the retired `#c99a34`) as `--primary` or any token.
- Blue is for active, new, and supporting state.
- Green is for successful delivery, sent states, verified states, or completion.
- Red is used sparingly and only when severity or failure warrants it.
- Gold or amber should not become a broad decorative brand wash.
- Do not make the whole UI one hue family.
- Do not let cream or off-white become visually sleepy. Pair it with strong ink, navy, and clear borders.

---

## Component Contract

### Buttons

| Role | Treatment | Examples |
|---|---|---|
| Parent/site primary | Navy filled | Parent signup/start action |
| Keep page-level primary | Navy filled | New request |
| Keep communication primary | Keep teal filled | Send update, Send message, Save preferences when saving communication preferences |
| Secondary operator action | Navy outline | Contact, customer page secondary actions |
| Quiet bookkeeping | Quiet button or text button | Mark handled, cancel, change email |
| Destructive | Red treatment | Close request, remove email when destructive |
| Success feedback | Green subtle | Update sent |
| Failure feedback | Red text or red subtle surface | Could not send update |

Buttons should have practical tap size. Prefer `min-h-[42px]` or larger for important touch actions.

### Forms

Form fields should use:

- white surface
- warm neutral border
- clear label
- visible focus state using Keep teal on Keep surfaces
- input text at least `text-base` on public mobile surfaces to avoid mobile zoom friction
- field-specific validation copy

Do not duplicate form class strings page-by-page once shared primitives exist.

### Cards And Panels

Operational cards use a quiet baseline with depth applied **differentially** — not a
flat minimum applied uniformly:

- white surface on warm canvas — two-tone neutrals, so the card lifts off the page
- warm neutral border
- restrained radius, sized up for primary surfaces (radius scale in `keep-component-spec.md`)
- depth is differential: most cards are border-first with minimal shadow, but the
  page's primary/anchor surface earns real elevation and fill (Production Richness Floor)
- spacing is uneven: primary surfaces get more room, utilities less — not uniform
  compact spacing on every block
- state rails or badges when useful
- scannable structure

Do not use nested cards for primary operational surfaces. Small receipts, previews, or inset metadata blocks are allowed only when they do not visually become a card inside a card.

Do not make request cards look like chat messages.

### Badges And State Rails

Badges must map to semantic meaning:

- attention: amber
- active/new/info: Keep blue
- success/complete/sent: green
- failure/severe urgency: red
- product identity/communication: Keep teal

Unknown backend state codes must fall back safely without throwing.

### Icons

Use practical icons only where they improve scanning:

- plus for new request
- search for search
- clock for waiting or time
- alert for attention
- send for update
- check for handled or sent
- link or eye for customer page state

Avoid decorative icons. Avoid icon-only actions on mobile unless the meaning is obvious and supported by accessible labels.

---

## Surface Contracts

### Marketing Website

The marketing website should explain Keep quickly and build trust.

It should:

- make the customer-silence problem recognizable
- show how Keep helps the business stay ahead of customers
- demonstrate the customer-facing page and operator request list
- explain why Keep is different from CRMs, job trackers, reminders, and workflow systems
- make trying Keep feel low-risk and practical

The site should show product proof. A text-only page is not enough.

Marketing is not exempt from the product visual system (ADR-155):

- **Locked tokens only.** Marketing styles consume the locked `--ophalo-*` and
  `--keep-*` tokens. No marketing-local brand hex values. Derived neutrals are
  allowed only via `color-mix` over locked tokens (no new palette values).
- **Richness Floor applies.** Marketing pages satisfy the Production Richness
  Floor like any primary surface: one type anchor (the `h1`), one elevated filled
  anchor surface, one saturated teal moment, uneven section rhythm, two-tone
  neutrals.
- **One uppercase label per page.** The hero eyebrow is the single allowed
  uppercase label. Section hierarchy comes from headings and surface weight,
  never from repeating uppercase section labels.
- **Button hierarchy applies.** Site CTAs are parent/site primary — navy filled,
  navy outline secondary. Keep teal buttons appear on marketing pages only inside
  product proof, where they faithfully render real Keep communication actions.

**Product proof, defined:** a faithful miniature of a real product surface — real
section names, real badge labels, real status copy, real component anatomy, and
locked tokens. Never invented states, fabricated metrics, or features the product
does not have. The current proof surfaces are the operator request list (summary
strip, Needs attention / Active sections, state-rail cards with action zones) and
the customer request page (teal status hero, latest-update continuity card).

Product proof must be visible on mobile. Hiding the proof below a breakpoint
fails this contract — mobile is the primary buyer context.

### Public Intake

Public intake should be mobile-first, calm, and low-friction.

It should:

- show the business name clearly
- collect only the required contact/request details
- make optional email updates understandable
- use Keep teal for Keep communication actions
- preserve trust through strong field labels and simple error states

### Customer Request Page

The customer request page is a mobile-first public status page.

It should answer customer trust questions in this order:

1. What is the current status?
2. Did the business receive the correct request?
3. Has the business shared an update?
4. What can I do if I need to add something?
5. What has happened so far?
6. How do I manage preferences or close the loop?

Visual rules:

- The status header is the page's elevated anchor: a Keep identity surface carrying
  the primary type anchor (≥28px) and visible volume. It is not a marketing hero
  (no imagery, no oversized empty space), but it must not be a flat strip either —
  it is the one surface that most clearly lifts off the canvas.
- The submitted request is a white stored-record card, not a muted placeholder.
- Enabled Send message uses Keep teal; disabled Send message is muted, bordered, and clearly inactive.
- Quick actions use Keep teal as an accent through icons, borders, hover, or focus; they should not all become filled primary buttons.
- A real latest business update may use `--keep-accent-bg`, a Keep accent rail, or a Keep accent badge.
- "No business update yet" uses a neutral/warm trust card, not teal, because it is reassurance rather than a business communication.
- Timeline stays log-style, not chat-style. Use spacing, icons, event labels, and latest markers for rhythm.
- No longer needed, email preferences, and link utilities are Level 4 content and must stay visually quieter.

### Keep Request List

The request list is Keep's primary operator work surface.

It should help the operator answer:

```text
Who might feel forgotten, and what can I do about it fast?
```

Required sections:

- Needs attention
- Active

Suppress Needs attention when count is zero.

Mobile should prioritize:

1. Requests title and primary action.
2. Compact attention/active summary.
3. Filters/search only as needed.
4. First request card quickly.

### Request Detail

Request detail should support focused follow-through, not become a management dashboard.

It should:

- show customer name and status clearly
- expose customer page actions
- make sending a customer-visible update prominent
- keep internal notes and bookkeeping secondary
- make the timeline scannable and visibility-safe

---

## Responsive And Accessibility Contract

Public and operator Keep surfaces must be usable at:

- 320px
- 375px
- 390px
- 430px
- 768px
- desktop widths

Release checks must verify:

- no horizontal scrolling
- tap targets are practical and at least 24 by 24 CSS pixels where WCAG 2.2 target-size rules apply
- focus states are visible
- keyboard navigation reaches all controls in a logical order
- text does not clip in cards, filters, buttons, or badges
- mobile controls do not push the first useful request content too far down the page
- public forms remain readable without layout shift
