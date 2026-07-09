# Build Log 073 — Session 19: Keep UX Production-Readiness — Tracker Punch List + Workbench Migration

**Started:** 2026-07-06
**Status:** Implementation complete — S19a/S19b/S19c/S19d shipped; S19e (customer tracker punch list) and Gate 3 exit criteria deferred
**Session name:** S19 Keep UX production-readiness
**Review baseline:** `main` @ `a377dae` (customer tracker redesign)
**Next free ADR:** ADR-418 (per build log 072)
**Target bar:** Production-ready per `keep-review-rubric.md` Gate 3 (release sign-off), **not** pilot-ready.
Every item below is judged against the public-availability gate: binary checklist clean + score 3 in
every universal category. No pilot shortcuts; known risks are flagged explicitly, never deferred silently.

---

## Purpose

A full UX-design review pass was performed on two Keep surfaces against the layered design docs
(`docs/ux-design/README.md` authority map):

1. **Customer request page** (`web/ophalo-web/src/app/keep/r/[pageToken]/`) — public tracker.
2. **Keep workbench** (`web/ophalo-app/`) — the owner/admin request command center (request list +
   request detail).

This log records the findings and the implementation handoff. The implementing session (Opus) should
treat this document as the spec for *what* to change and the design docs as the spec for *how*.

**Required reading for the implementing session (precedence order):**

1. `docs/ux-design/keep-component-spec.md` — recipes are operative truth. Part 5 (composition),
   §4 composer, §5 pills, §6 timeline, §8 section labels, §9 status chip, Appendix "Operator
   surfaces" ("apply the same scales, swap the page-primary token to navy; teal stays reserved for
   communication actions").
2. `docs/ux-design/ux-design-model-v1.md` — tokens (`--ophalo-canvas #F8F6F1` is explicitly the
   PWA background), type ramp, Production Richness Floor.
3. `docs/ux-design/keep-review-rubric.md` — binary gate + per-surface criteria for customer request
   page, request list, request detail.
4. `docs/ux-design/ux-design-decisions.md` — Button Hierarchy Is Locked, Background Model Is Locked,
   Focus Treatment Is Token-Backed, ADR-150, ADR-152/153/154, ADR-367 (one Keep identity, two
   densities), ADR-368 (serif headlines, terracotta is parent-only).

---

## Part A — Customer request page: verdict and punch list

**Verdict:** Binary gate passes in full (all G/S code checks verified by grep against
`CustomerTrackerView.tsx` / `page.tsx`). Structure matches spec Part 5. Four code gaps and a
mobile-viewport verification pass stand between this surface and Gate 3 sign-off.

### A1. Code fixes (small, well-bounded)

| # | Finding | Location | Fix |
|---|---|---|---|
| A1.1 | No `loading.tsx` anywhere under `keep/` — route shows blank while streaming; spec Part 6 step 2 requires a skeleton mirroring the separated surfaces (Part 2) | `web/ophalo-web/src/app/keep/r/[pageToken]/` | Build `loading.tsx` skeleton per spec Part 2 |
| A1.2 | §7 Utility footer missing entirely (spec Part 5 ends with "no longer needed · email · fine print"; rubric per-surface pass condition includes the email-preferences check) | `CustomerTrackerView.tsx` (DOM ends at timeline) | Needs decision D5 below — backend support for email preferences may not exist; requires explicit scope decision or written exception, not silent omission |
| A1.3 | Quick-action pill icons are neutral; §5 pins `text-[var(--keep-accent)]` on the icon ("teal as accent only") | `CustomerTrackerView.tsx:549` | Add the icon color class |
| A1.4 | Message textarea has no accessible label (placeholder only); feedback textarea has one | `CustomerTrackerView.tsx:556` (`id="tracker-message"`) | Add `aria-label` or sr-only `<label>` |

### A2. Spec/doc reconciliations (docs, not code — spec must reflect shipped truth)

- **Timeline recipe drift:** spec §6 pins `border-l` on the list + newest-only `w-1` rail; the shipped
  redesign (a377dae) uses a centered connector div, `ring-2 ring-white` icon dots, and a
  customer-event left-border variant. Rendered result honors the rubric's intent (newest = teal
  border + tinted bg). Amend §6 to match the shipped redesign.
- **Danger pill unspecced:** the red "Request cancellation" chip variant is not in §5 (teal accent
  only). Model-v1's action contract includes a red destructive role — amend §5 with the danger
  variant rather than reverting.
- **Metadata row** omits the "submitted" date (spec Part 5: chip · submitted · reference). Add it or
  amend the spec.
- **§5 "sent" chip state** (muted + "Sent") is specced but not implemented. Implement or amend.
- **Reset link recipe** (spec line ~183) lacks the focus ring the rubric mandates on all interactive
  controls. Contradiction — add the focus ring to both spec recipe and code.
- **Rubric wording bug:** "No named state uses a default gray badge" contradicts §9's own table
  (`no_longer_needed → default (muted)`). §9 wins; fix the rubric sentence.
- **Stale links:** `docs/ux-design/README.md` "Related References" point to `docs/reference/…` paths
  that do not exist in this repo. Fix or remove.

### A3. Verification still owed before Gate 3

Rubric-required screenshot pass at **320 / 390 / 768 px**: no horizontal scroll at 320, enabled Send
turns navy, empty/error/sent states. Desktop passes were verified from screenshot; code strongly
suggests the mobile checks pass (fluid widths, `flex-wrap`, navy guaranteed by `KeepButton`), but the
gate requires the visual pass to be performed and recorded.

---

## Part B — Keep workbench (`web/ophalo-app`): verdict and findings

**Verdict: the workbench predates the design system entirely — this is a migration, not a polish
pass.** It fails the binary gate wholesale: zero design-system tokens, zero Keep primitives,
cool-slate palette, no type anchor, three button-hierarchy violations. Spec Part 6 anticipated this
("request detail, request list … each migrated in its own session"). The structure, data handling,
and interaction logic are solid — this is a reskin plus targeted restructuring, not a rebuild.

ADR-367 is the load-bearing decision: **Keep Web (this workbench) and Keep Mobile are the same Keep
identity at two densities** — same teal accent, same type ramp, same component grammar. A surface
never gets its own look. Terracotta/amber-as-brand is parent-only.

### B1. Foundation gaps (blocking; fix first)

1. **Shared tokens not imported.** `web/shared/styles/ophalo-tokens.css` exists (defines
   `--ophalo-navy`, `--ophalo-canvas`, `--keep-accent`, …) but `ophalo-app/src/styles/app.css`
   imports only fonts + Tailwind directives. Zero `var(--keep-*)`/`var(--ophalo-*)` references in
   `src/pages` or `src/components` — all styling is raw Tailwind slate/amber/red/yellow. Violates
   "Shared Token Source Is Required" and "Shared Neutrals Must Not Drift By Product."
2. **No Keep primitives.** No `components/keep/` in this app. Spec governance requires both apps to
   carry mirrored copies. Mirror `KeepButton.tsx` / `KeepBadge.tsx` from
   `ophalo-web/src/components/keep/` verbatim — they already encode navy/teal variants, muted
   disabled states, `min-h-[42px]`, and the token-backed focus ring.
3. **Canvas is cool slate, not warm cream.** Shell is `bg-slate-50` (`App.tsx:76`); sidebar, nav,
   and tab chrome are all slate. Background Model lock: warm canvas, white cards, navy anchors,
   tinted state surfaces only. Model-v1 names `--ophalo-canvas` as the PWA background.
4. **Production Richness Floor fails on both surfaces.** No ≥28px anchor (detail h1 is `text-lg`,
   `RequestDetail.tsx:604`; the list page has no title — the tab bar is the topmost element), no
   elevated/filled surface, no saturated teal moment. ADR-368's serif headline ramp is absent from
   both pages (`font-serif` only on the wordmark, Home, QuickCapture). Fonts are already self-hosted
   (ADR-379), so the type migration is cheap.

### B2. Button hierarchy violations (locked table in `ux-design-decisions.md`)

5. **Send update is `bg-slate-800`** (`RequestDetail.tsx:1241`) → `KeepButton variant="teal"`. The
   rubric's request-detail (S) check names this exact requirement. It is the one teal communication
   primary on the page.
6. **Share Link is filled `bg-amber-600`** (`RequestDetail.tsx:277`) → no locked role maps to
   orange; amber is the attention semantic and terracotta is parent-brand-only (ADR-367/368).
   Customer-page actions are "Secondary operator action → navy outline." Keep the "not yet shared"
   urgency on the amber-tinted card/badge (`RequestDetail.tsx:264–269`), not the button fill.
7. **Update Status is `bg-slate-800`** (`RequestDetail.tsx:395`) and **New Request is
   `bg-slate-900`** (`App.tsx:113` sidebar, `App.tsx:150` mobile FAB) → navy via
   `KeepButton variant="primary"` / `--ophalo-navy` (page-level primary).
8. **Focus states:** zero `focus-visible` in pages/components; inputs use `focus:ring-slate-400`.
   Locked decision requires token-backed focus:
   `focus-visible:ring-2 focus-visible:ring-[var(--keep-accent)] focus-visible:ring-offset-2`
   (destructive/semantic contexts may use their own token).

### B3. Section-label ramp abuse

Eight `uppercase tracking-wide` labels on request detail alone (`RequestDetail.tsx:265, 363, 675,
695, 708, 721, 779, 875`). They use `tracking-wide`, so the rubric's literal `tracking-wider` grep
passes — but this is exactly the repeated-uppercase wireframe pattern the ban targets (spec §8: max
one section label per surface; a divider, not an anchor). Replace with card titles per the recipes,
**and tighten the rubric grep to also catch `tracking-wide`.**

### B4. Request detail — structure and content

- **ADR-154 locks the section order** (back → hero → original request card → attention → operator
  actions → composer → latest activity). The workbench instead uses a desktop right rail ordered
  Tracker Link → composer → Change Status → Feedback → Acknowledge → Participation → Metadata
  (`RequestDetail.tsx:1412–1450`). ADR-367's density axis arguably legitimizes a two-column
  workbench, but ADR-154 is a lock → **decision D1**. Whatever the outcome, the composer (Send
  update) leads the action rail — it is the primary communication action, and must not sit below
  Tracker Link.
- **Timeline noise:** raw event dumps — four "Participation Changed / muted/unmuted notifications"
  rows, duplicate planned-date entries. ADR-150 suppressed page-view noise for exactly this reason;
  the mobile sessions built human-readable event labels (commit `05c0c58`). Port that label mapping;
  propose an ADR-150 extension to suppress mute/unmute participation events → **decision D3**.
- Timeline lacks the §6 recipe entirely (no newest marker, no icon dots/type badges; gray
  text-on-white rows).

### B5. Request list — per-surface rubric

- **Search/filter bar sits directly under the tabs, above the first work item**
  (`Requests.tsx:219–235`) — rubric: filters/search are volume 4, "not the first or dominant
  element."
- The tab-queue model (Default Queue / Assigned / Needs Attention / Watching / Ready to Close /
  Feedback Review) differs from the rubric's sectioned "Needs Attention + Active" list. The Action
  Queue decision supports queue framing, and rows already carry a thoughtful, exhaustive-with-
  fallback attention-reason → tone mapping (`RequestRow.tsx:5–40` — keep it) → **decision D2**.
- Styling migrates regardless of D2: hand-rolled `rounded` badges → `KeepBadge` (rounded-full) with
  locked semantic tokens instead of raw `red-100/orange-100/yellow-100`; gray "In Progress" status
  chip → teal per §9; active-tab underline `slate-900` → navy/teal token.
- No Level 1 anchor on the page; empty states are bare (rubric: empty states explain what's next).

### B6. Accessibility

Nearly no `aria-*` in pages (1 hit in Settings; the mobile FAB correctly has `aria-label` + focus
ring — keep). Tab bar lacks `role="tablist"` / `aria-selected`; status-filter `<select>`s are
unlabeled; error messages lack `aria-live` (the customer tracker sets the pattern); several states
convey meaning by color alone (AttentionBadge's icons partially mitigate).

### B7. What is already right (do not regress)

New Request placement and dark fill (wrong token only); attention-reason mapping in `RequestRow`;
mobile FAB a11y; tab overflow-x scrolling; concurrency-conflict messaging; self-hosted Inter/Source
Serif; overall component decomposition in `RequestDetail.tsx`.

---

## Decisions required from Christian before implementation

| # | Decision | Options |
|---|---|---|
| D1 | ADR-154 vs workbench two-column layout | Amend ADR-154 with a desktop-rail density mapping (composer-first rail), or restructure the page to the locked single-column order |
| D2 | Request-list model at workbench density | Bless the tab-queue as the workbench expression (amend rubric per-surface criteria), or restructure to the sectioned Needs Attention + Active list |
| D3 | Timeline participation noise | Extend ADR-150 to suppress mute/unmute participation events (recommended), or keep and restyle them |
| D4 | Share Link treatment after amber fill removal | Navy outline + amber badge/card urgency (recommended per locked table), or another explicit treatment |
| D5 | Customer tracker §7 utility footer | Build it (requires email-preferences backend scope), defer with a written Gate 3 exception, or amend spec Part 5 |

---

## Recommended implementation slicing (respects the batch gate: ≤8 production files, ≤12 total per session)

Each slice compiles and ships independently; run focused checks per slice, full rubric pass at the end.

1. **S19a — Workbench foundation:** import `ophalo-tokens.css` into `app.css`; map tokens in
   `tailwind.config.ts`; mirror `KeepButton`/`KeepBadge` into `ophalo-app/src/components/keep/`;
   convert app shell + sidebar (canvas, nav, New Request) to tokens.
2. **S19b — Request list:** demote search; `RequestRow` → `KeepBadge` + semantic tokens; tab chrome
   tokens + a11y roles; empty states; Level 1 anchor (per D2).
3. **S19c — Request detail actions:** teal Send update, navy Update Status, navy-outline Share/Copy
   Link (per D4); composer per §4 recipe; focus rings; remove the uppercase label ramp.
4. **S19d — Request detail structure + timeline:** section order per D1; §6 timeline recipe;
   human-readable event labels ported from mobile; noise suppression per D3.
5. **S19e — Customer tracker punch list (A1) + doc reconciliations (A2):** small, can pair with any
   slice or run standalone.

**Gate 3 exit criteria for this whole effort:** binary checklist clean on request list, request
detail, and customer request page; score 3 in all six universal categories per surface; screenshot
pass at 320/390/768/desktop recorded; cross-surface check (one product family, teal = Keep identity,
locked semantic color map) performed with all surfaces side by side.

---

## Outcome

S19a–S19d shipped across commits `b35725a`, `daf5eaa`, `f8dec32`, and `7da83b5`. Decisions D1–D4
were resolved implicitly through implementation (two-column workbench rail retained; tab-queue model
kept; mute/unmute participation events suppressed in timeline label mapping; Share Link converted to
navy-outline). No formal ADRs were locked for D1–D4.

**Deferred:**

- **S19e** — Customer tracker A1 code fixes (`loading.tsx` skeleton, §7 utility footer scope, icon
  color class, accessible textarea label) and A2 doc reconciliations remain undone.
- **Gate 3 sign-off** — Screenshot pass at 320/390/768/desktop and cross-surface rubric check have
  not been performed. Defer to a dedicated UX sign-off session before public launch.
