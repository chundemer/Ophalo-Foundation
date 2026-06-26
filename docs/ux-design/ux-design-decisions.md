# UX Design Decisions

**Status:** Active reference
**Date:** 2026-06-04
**Primary source:** `docs/reference/ux-design/ux-design-model-v1.md`

---

## Purpose

This document records the decisions behind the OpHalo and Keep UX system.

Use `ux-design-model-v1.md` for the active contract. Use this file for why the contract exists.

---

## Locked Decisions

### UX Design Reference Supersedes The Old Brand Doc

**Decision:** `docs/reference/ux-design/` is the active source of truth for OpHalo and Keep UX guidance.

`docs/v1/ophalo-brand-v1.2.md` is retired and retained only as a historical pointer.

`docs/v1/product/ophalo-brand-v1.2.md` and `docs/v1/product/OpHalo Brand Guide.md` are also retired and retained only as historical pointers.

**Rationale:** The previous brand doc had useful doctrine, but its filename, version status, and token rules drifted from current product architecture. The reference folder pattern is already used for deployment-ready domains.

### Shared Token Source Is Required

**Decision:** Parent OpHalo tokens and shared semantic tokens must live in one shared source imported by both the marketing/account website and the Keep app.

Product-level globals may assign product tokens such as `--keep-*`, but they must not redefine the parent neutral palette independently.

**Rationale:** Duplicating token blocks across apps is how the current drift happened. A shared source makes the foundation durable as more Halo products are introduced.

### OpHalo Tokens Are Parent Foundation Only

**Decision:** `--ophalo-*` tokens represent parent brand, shared neutral values, and shared semantic states.

**Rationale:** Parent tokens should keep every Halo visibly related without forcing every product to share the same accent color.

### Product Tokens Use Product Names

**Decision:** Keep-specific tokens use `--keep-*`, not `--app-*`.

Future products must use their own namespaces, such as `--feedback-*`.

**Rationale:** Product-named tokens are searchable, explicit, and scale better as multiple Halo products enter the codebase.

### Keep Teal Is Locked

**Decision:** Keep teal is `#168A9A`.

Use `--keep-accent` for this value.

**Rationale:** Keep needs a dedicated identity color that is grounded, professional, and appropriate for service-business communication surfaces.

The previous teal candidate `#159DB8` is retired.

### Shared Neutrals Must Not Drift By Product

**Decision:** All products share the same neutral palette:

- `#10243E` parent navy
- `#172033` ink
- `#F8F6F1` canvas
- `#FFFFFF` card
- `#DDD6C8` border
- `#5D6878` muted text

**Rationale:** Products may have distinct accents, but the shared OpHalo family depends on common structure, typography, neutral color, spacing, and component behavior.

### Semantic Status Colors Are Shared

**Decision:** Attention, success, and danger colors are shared `--ophalo-*` semantic tokens.

Keep's active/info state uses `--keep-info` and `--keep-info-bg`.

The status color map is:

| Meaning | Token family | Use |
|---|---|---|
| Attention / waiting risk | `--ophalo-attention` | Needs attention rails, waiting cues, attention badges |
| Active / new / info | `--keep-info` | Active state, new request support, non-urgent supporting state |
| Success / complete / sent | `--ophalo-success` | Sent, delivered, complete, verified |
| Failure / destructive / severe urgency | `--ophalo-danger` | Send failures, destructive states, severe overdue only |
| Keep communication / identity | `--keep-accent` | Keep-specific communication actions, focus rings, customer page communication cues |

**Rationale:** A customer waiting, a completed action, or a failure means the same thing across products. These colors should not be product-branded.

### Background Model Is Locked

**Decision:** OpHalo and Keep surfaces use warm canvas, white cards, navy anchors, and tinted state surfaces only.

Use:

- warm canvas for the page environment
- white for forms, cards, request surfaces, and high-clarity panels
- navy for parent trust anchors and page-level primary actions
- tinted surfaces only for semantic or product communication states

**Rationale:** This keeps the UI warm and practical without becoming decorative, beige-heavy, or one-hue.

### Button Hierarchy Is Locked

**Decision:** Buttons must follow role, not page-local preference.

| Role | Treatment | Examples |
|---|---|---|
| Parent/site primary | Navy filled | Parent signup/start action |
| Keep page-level primary | Navy filled | New request |
| Keep communication primary | Keep teal filled | Send update, Send message |
| Secondary operator action | Navy outline | Contact, customer page secondary actions |
| Quiet bookkeeping | Neutral/text | Mark handled, cancel, change email |
| Destructive | Red treatment | Close request, remove email when destructive |
| Success feedback | Green subtle | Update sent, preferences saved |

**Rationale:** Button inconsistency affects every surface. Locking role-based button color prevents local drift while still letting Keep communication actions use Keep teal.

### Shared Primitives With Product Wrappers

**Decision:** Shared foundation primitives should be created for Button, Field, Textarea, Badge, Panel, PageShell, SectionLabel, EmptyState, and related base UI.

Product-specific wrappers may adapt those primitives for Keep behavior and copy.

**Rationale:** Foundation primitives keep the OpHalo family consistent. Product wrappers keep each Halo free to express its product job without forking the base design system.

### Card Anatomy Is Locked

**Decision:** Operational cards use white surfaces, warm borders, and state rails or badges when useful. Depth is applied **differentially**, not as a uniform flat minimum: most cards are border-first with minimal shadow, but each primary surface must satisfy the Production Richness Floor — one elevated, filled surface, uneven rhythm, two-tone neutrals (see `ux-design-model-v1.md`).

Primary operational content must not use nested cards. Small receipts, previews, or inset metadata blocks are allowed only when they do not visually become a card inside a card.

**Rationale:** Keep cards are continuity units. They need to be sturdy, scannable, and field-ready, not decorative or dashboard-like.

> **Amended 2026-06-07 (see ADR-148).** The original wording — "modest radius, minimal shadow, compact spacing" — produced correctly-tokened wireframes: every constraint pushed toward *less* with no floor for *more*. Superseded by differential depth + the Production Richness Floor.

### Focus Treatment Is Token-Backed

**Decision:** Buttons, inputs, links, cards, segmented controls, drawers, and dialogs need a visible token-backed focus state.

Keep surfaces should use the Keep accent for communication/product focus treatment unless a destructive or semantic state requires another token.

**Rationale:** Focus visibility is a WCAG 2.2 readiness requirement and a practical field-use requirement.

### Keep Is Continuity, Not Chat

**Decision:** Keep UI should not default to chat language or chat-bubble-heavy presentation.

**Rationale:** Keep's job is customer continuity. Chat framing changes operator expectations and makes the product feel like a message inbox rather than a focused continuity layer.

### Send Update Is The Default Customer-Visible Action

**Decision:** Use **Send update** as the default customer-visible action.

Use **Answer customer** only when the customer explicitly asked a question and the context benefits from that specificity.

Avoid **Reply** as the default action.

**Rationale:** "Send update" reinforces proactive customer continuity. "Reply" makes the product feel like chat.

### Request List Is An Action Queue

**Decision:** The Keep request list is an action queue, not a dashboard or historical table.

**Rationale:** The operator needs to know who might feel forgotten and what action to take now.

### Public Customer Page Is Mobile-First

**Decision:** The customer request page stays single-column and mobile-first.

**Rationale:** Customers are likely to open the page from a phone. A dashboard-style layout would add friction and reduce trust.

### Mobile QA Widths Are A Release Gate

**Decision:** Keep and public OpHalo surfaces must be checked at 320px, 375px, 390px, 430px, 768px, and desktop widths before public availability.

**Rationale:** Keep is used between calls, jobs, and interruptions. The product cannot depend on a comfortable desktop viewport.

### Marketing Must Show Product Proof

**Decision:** Marketing pages must show real or realistic product visuals when explaining Keep.

**Rationale:** Keep is practical software for practical businesses. Product proof builds more trust than abstract SaaS claims.

### Request Detail Sequencing Is Locked

**Decision:** Request detail should be restyled after token unification and shared primitive extraction.

**Rationale:** Request detail has the most local page-level styling and timeline complexity. Restyling it before tokens and primitives would create duplicate work.

### Production Polish Comes From Hierarchy, Not Decoration (ADR-145)

**Decision:** OpHalo and Keep screens must not rely on decorative SaaS effects for polish.

Production readiness requires clear visual hierarchy, visible product identity, section rhythm, action affordance, trust-building empty states, and appropriate visual volume.

Use `keep-review-rubric.md` as the standard pre-coding and review reference for page-polish work.

**Rationale:** Correct tokens alone do not make a screen feel production-ready. Screens that treat every section equally feel like prototypes even when colors and copy are correct.

### Keep Is Restrained But Not Plain (ADR-146)

**Decision:** Keep uses restrained structure with visible Keep identity and continuity cues.

Neutral OpHalo styling is used for structure, records, metadata, waiting states, and utilities. Keep teal is used for identity, enabled communication actions, customer-page communication cues, and real business-update highlights.

**Rationale:** Keep should feel calm and field-ready, but it must still feel like a branded product. If Keep teal is absent from customer communication surfaces, the product feels generic.

### Customer Request Page Communication States (ADR-147)

**Decision:** On the customer request page, real business updates and communication actions may use Keep teal treatment. Waiting or no-update reassurance states should use neutral/warm trust-card treatment.

**Rationale:** Customers should be able to distinguish between "the business has shared an update" and "there is no update yet, but the request is received." Teal should signal communication identity, not empty waiting.

### Keep Primitive Inputs Use forwardRef (ADR-142)

**Decision:** `KeepField` and `KeepTextarea` must use React `forwardRef` so parent components can attach refs to the underlying `<input>` and `<textarea>` elements.

**Rationale:** Some inputs require parent-managed refs for cursor-position tracking (phone number masking) or post-action focus control (status starter chips). Without `forwardRef`, those inputs would have to be kept as raw elements, defeating the purpose of the primitive.

### KeepPanel Is Single-Content Only (ADR-143)

**Decision:** `KeepPanel` is for single-content cards — it wraps all children in one padded div. Multi-section cards that use `divide-y` between independently padded sections (such as form cards with a header, field sections, and a submit row) must stay as raw divs with semantic Tailwind token classes.

**Rationale:** Forcing a `divide-y` multi-section card through `KeepPanel` would require gutting the primitive or adding slot props that do not generalize. The distinction clarifies ADR-138: `KeepPanel` is for operational information cards; multi-section form layout is page-level structure.

### Pill and Chip Buttons Are Not KeepButton (ADR-144)

**Decision:** Small pill-shaped suggestion chips, toggles, and inline action buttons (e.g., status starter chips, hide/show tips toggle, inline copy buttons) must not use `KeepButton`. They stay as raw `<button>` elements with semantic Tailwind token classes.

**Rationale:** `KeepButton` enforces `min-h-[42px]` and `rounded-lg` — dimensions appropriate for primary and secondary action buttons. Pill chips and small inline buttons are distinct UI elements with their own proportions. Mixing them through `KeepButton` would require a size prop that weakens the primitive's contract.

### Production Richness Floor Is Mandatory (ADR-148)

**Decision:** Restraint is the baseline, not the ceiling. Every primary Keep surface
must satisfy all five Richness Floor items (one ≥28px type anchor; one elevated,
filled surface; one saturated teal moment; uneven rhythm; two-tone neutrals) and must
avoid the two named failures (repeating uppercase section labels; strokes-on-canvas
flatness). The Floor is defined in `ux-design-model-v1.md` and enforced as a hard fail
in `keep-review-rubric.md`. This supersedes the restraint-only wording of the Card
Anatomy decision above.

**Rationale:** Every prior visual constraint pushed toward *less* — minimal shadow,
modest radius, compact spacing, soft headers, pale accents — with no constraint
requiring *more* of anything. A session following the docs faithfully produced a
correctly-tokened wireframe. The fix is to rebalance (keep the guardrails, add a
required floor), not to remove constraints, because removing constraints increases
drift. The customer request page rendered as a prototype precisely because nothing in
the docs demanded a peak.

### UX Docs Are A Layered System With Precedence (ADR-149)

**Decision:** The UX-design docs consolidate to four active layers — `ux-design-model-v1.md`
(principles/tokens/voice/Floor), `keep-component-spec.md` (recipes/scales/structure),
`keep-review-rubric.md` (the single review + release gate), and this ADR log — mapped
in the folder `README.md`. Two governing rules: **one fact has one home** (no doc
restates a token value, type size, or recipe owned by another — it links instead), and
a **precedence order** for conflicts (spec → model → rubric → decisions; code follows
the spec). `keep-review-rubric.md` replaces and merges the former
`visual-polish-rubric.md`, `ux-design-production-readiness.md`, and
`ux-design-deployment-checklist.md`. `ux-design-harden-pass.md` is retired to history.

**Rationale:** The docs were fighting each other across AI sessions: the same styling
facts were restated in three-plus files (drift surface), three overlapping rubrics
answered the same question differently, and a work log sat alongside doctrine as a
peer. Sessions re-derived from whichever doc they opened first and drifted differently
each time. A single home per fact plus an explicit precedence order makes conflicts
resolve deterministically and makes contradictions visible bugs instead of silent
divergence.

### Request Detail Activity Timeline — Suppress Per-Open Page-View Noise (ADR-150)

**Decision:** All `customer_page_opened` events are filtered out of the operator
activity timeline on the request detail page. A single "Customer last viewed X ago"
status pill replaces them in the request header, alongside the open/closed and
attention badges. If the customer has never opened the page, the pill reads
"Not yet viewed" (attention amber). The backend continues to append every page-open
event and those events remain available for analytics, but none appear in the timeline UI.

**Rationale:** Individual page-open events are analytics data, not actionable operator
information. In practice the timeline was flooded with 10-plus consecutive
"Customer opened their page" rows, pushing meaningful events — customer questions,
business updates, status changes, internal notes — below the fold. The only fact the
operator needs is "did the customer see my last update?", which is answered by the
most recent view timestamp alone. That fact is actionable context — it tells the
operator instantly whether their last update landed — so it belongs in the header with
the other status badges, not buried at the bottom of the activity section.

**Implementation:** Frontend-only change in `page.tsx`. `displayTimeline` filters out
all `customer_page_opened` events before rendering. `lastPageOpened` captures the
first item (most recent, since timeline is newest-first) with that event type. The
status pill renders in the header badge row regardless of delivery state.

### Customer Page Preview Removed From Operator Request Detail (ADR-152)

**Decision:** The "Customer page preview" sidebar card is removed from the operator request detail page. It will not appear in open or closed request layouts.

**Rationale:** The operator already has two direct customer-page actions in the hero — "View customer page" and "Copy customer link." The preview card duplicated that function while consuming vertical scroll space and pushing operational context (request description, latest activity) lower on mobile. The preview showed only the customer-facing status message, which is set by the operator and already visible in the status card. On mobile — the primary use context — every card in the scroll order must earn its place; this one does not. A customer-page preview may be useful in a future admin or debug context but is not appropriate as default content in the operator workflow.

### Original Request Card: Dedicated Context Section (ADR-153)

**Decision:** A dedicated "Original request" card is placed immediately below the hero card and above the attention/overdue alert. It always shows: original description, customer phone number, and created timestamp. No secondary metadata (last team update, last customer activity) is shown in this card; that information is available in the activity timeline below.

**Rationale:** The operator's first question on landing is "what is this request actually about?" The current layout answered "who and what to do next" before answering "what did they ask." On mobile, the description was below the update composer, meaning the operator had to scroll past the action area before seeing the context that informs the action. Moving description into a compact card immediately after the hero resolves this without collapsing anything (collapse adds a tap on every visit) or overloading the hero (which would turn it from an identity anchor into a dashboard card). Secondary facts — last team update, last customer activity — are not shown here because they are duplicated in the activity timeline. The card stays compact: three fields, no heading chrome.

### Request Detail Page Structure Lock (ADR-154) — amended

**Decision:** The operator request detail page section order is locked as follows:

1. **Back link**
2. **Hero** — reference code, customer name, status badges, customer page actions
3. **Original request card** — description, phone, created (ADR-153)
4. **Attention card** — first response overdue / pending / waiting on business (open requests only, when active)
5. **Operator actions card** — Change status · Add internal note · Close request (open requests only)
6. **Update composer** — BusinessUpdatePanel (open requests only)
7. **Latest activity** — full width, newest first

For closed requests: hero → original request card → latest activity (no composer, no operator actions, no attention card).

**Amendment (session RD-C):** The "utility row" (items 5–6 above) was originally ordered as composer-first, utility-row-second. That order was reversed and the section reclassified. Change status, Add internal note, and Close request are Level 2 lifecycle controls, not bookkeeping utilities. Internal notes in particular are core operator workflow ("called customer, left voicemail," "waiting on part") — not secondary decoration. On mobile, the previous placement buried these actions below a long composer, making them effectively unreachable. The operator actions card now appears above the composer in a white card with full-width stacked buttons on mobile (`grid gap-2 sm:flex sm:flex-wrap`, `w-full sm:w-auto`).

**Rationale (original):** Orient → understand urgency → choose lifecycle action → update customer → review history. Bookkeeping comes after context, not before it — but operator lifecycle controls must be reachable before the operator scrolls through the full composer.

**Deferred product decision:** Closed requests currently hide the operator actions card entirely (`!request.closedAtUtc`). Add internal note may be useful after close (customer called back, request closed by mistake, in-person resolution to document). Not blocking this session.

### Marketing Surface Contract — Marketing Is Not Exempt (ADR-155)

**Decision:** Marketing pages are bound by the same visual system as product
surfaces: locked token consumption only (derived neutrals via `color-mix` over
locked tokens allowed; no marketing-local brand hex), the locked button hierarchy
(ADR-136 — site CTAs navy filled / navy outline), and the Production Richness
Floor (ADR-148 — including the one-uppercase-label rule; the hero eyebrow is the
single allowed label per marketing page). "Product proof" is defined in
`ux-design-model-v1.md`: a faithful miniature of a real product surface using
real section names, badge labels, status copy, and component anatomy — never
invented states or fabricated metrics — and it must be visible on mobile.

**Palette drift incident (logged for the record):** `marketing.css` was built as
a parallel `mkt-*` palette that redefined brand colors locally, including the
retired teal `#159DB8` (ADR-131), and the pages carried fabricated stats with
named citations plus a fake Quiet/Waiting/Moving product model that never
existed in Keep. The drift survived because nothing stated that marketing was
inside the token and review contracts. Fixed across two sessions (June 2026):
session one retokened the file and removed the false claims; session two applied
the hierarchy rules and replaced decorative mockups with faithful product proof.

**Rationale:** The marketing site is the first product surface a buyer sees. A
marketing page that drifts from the locked system erodes the same trust the
product is selling, and unverified claims are a legal and credibility liability.
Declaring marketing explicitly in-contract removes the gap the drift grew in.

---

### Brand Architecture — Differentiation By Brand, Not By Surface (ADR-367)

**Decision:** OpHalo is a branded house with product accents. Differentiation
runs on two distinct axes, and they must not be conflated.

- **Brand axis** — OpHalo (parent) vs Keep (first product) vs any future
  product. This is the only axis on which the *look* legitimately varies, and it
  varies through a constrained set of levers: the **accent color** and a measured
  amount of **product personality**. The mark, wordmark typography, neutral
  palette, type system, spacing scale, component grammar, interaction patterns,
  and voice are shared foundation and do not fork per product.
- **Surface axis** — within a single product, the workbench vs the field app vs
  the customer-facing pages. Surfaces differ by **density and form factor, not
  brand.** A surface never gets its own look; it gets a different *density* of the
  same look.

Concrete bindings:

- **OpHalo Marketing** is the OpHalo *parent* surface (terracotta accent,
  trust/sell register). Bound by ADR-155.
- **Keep Web** (Owner/Admin command center) and **Keep Mobile** (operator field
  app) are the **same Keep identity at two densities** — Web wide and powerful,
  Mobile narrow and action-oriented. They are explicitly *not* two different app
  looks: same teal accent, same type ramp, same component grammar, differing only
  in how much they place on screen and which actions they foreground.
- **Customer-facing Keep surfaces** (public intake form, customer request/status
  page) carry the Keep identity in a warm, minimal, trust-first register. They are
  lightweight customer surfaces, not workspaces.

Future products attach to OpHalo by taking a new accent and personality over the
**same shared foundation.** A new product is a thin expression layer, never a new
design system. "Each app looks a little different" means a different accent and
density inside one system — never a different system.

**Token gap (logged):** the Keep accent is tokenized (`--keep-accent`), but the
OpHalo parent accent — terracotta `#BF6B43` in `brand-kit/BRAND.md` — is not yet
present in `globals.css`. The parent-accent lever this decision relies on must be
tokenized in the shared source (per the Shared Token Source decision) before the
marketing surface can express the OpHalo brand axis. Tracked as an open gap below.

**Rationale:** Conflating the two axes is the expensive mistake. If Keep Web and
Keep Mobile are treated as "different app looks," they become two systems to
maintain and the operator feels the seam moving between them; keeping them one
identity at two densities makes the field app a thin layer over the workbench.
The same logic protects future products: differentiation by *accent over shared
foundation* makes product #2 and #3 cheap expression layers instead of bespoke
rebuilds, and it makes a buyer who moves marketing → app → customer page
experience one coherent company rather than three vendors. This decision governs
how the system is structured, so it is locked before applied-look work proceeds.

---

### Type-And-Color System Lock — Serif Headlines, Terracotta Parent Accent (ADR-368)

**Decision:** The OpHalo/Keep type system is **Source Serif 4 headlines over Inter
body**, with Poppins reserved to the wordmark. Source Serif was chosen over
Fraunces because it holds up across the full ramp (hero to 20px card title) and
stays robust on field/mobile screens, while still delivering the serif-headline
upgrade that separates a finished surface from a wireframe. The full contract
lives in `ux-design-model-v1.md` → Typography.

Color is locked alongside it:

- **Terracotta `#BF6B43`** is added as the parent accent token (`--ophalo-accent`),
  the OpHalo brand-axis lever vs Keep's teal (ADR-367). Navy remains the
  structural anchor; terracotta is an accent, never a background wash.
- **Attention is nudged** from burnt-orange `#B85B16` to true amber `#C8741A` so an
  alert is visually distinct from the terracotta brand accent. AA contrast on the
  attention background must be verified during implementation.
- **Gold is removed.** The non-brand `#c99a34` wired as Tailwind `--primary` is
  drift from the retired brand doc; `--primary` maps to navy (ADR-136).

**Rationale:** The live marketing surface read as a prototype despite correct
content and tokens because every applied decision sat at its safest, lowest-
contrast setting — all-sans headlines, an unused parent accent, and a rogue gold
primary. Committing the headline face, the parent accent, and a clean primary is
the difference between a first pass and a locked look. Source Serif over Fraunces
trades a small amount of personality for a face that works across every surface
density, which the multi-surface architecture (ADR-367) requires.

**Supersedes:** `brand-kit/BRAND.md` §5 "Headings & UI: Poppins" — corrected to
wordmark-only. BRAND.md must be updated to match this contract.

---

## Open Gaps

- **Type-and-color contract not yet applied in the app (ADR-368):** the contract
  is locked in `model-v1`, and `--ophalo-accent` is tokenized in `globals.css`, but
  the app still ships Inter headlines, the gold `--primary`, the old burnt-orange
  attention value, and near-invisible marketing section contrast. Pending bounded
  implementation: wire Source Serif headlines, map `--primary` to navy, update
  `--ophalo-attention` to `#C8741A`, strengthen marketing section contrast, and
  correct `brand-kit/BRAND.md` §5 to wordmark-only.

Implementation tracking lives in `docs/session-log.md`; the closed component recipes
and migration checklist live in `keep-component-spec.md`. The retired
`ux-design-harden-pass.md` is historical only.
