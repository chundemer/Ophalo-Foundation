# 134 — UI-001 Post-Step-4 Queue and Preview Refinement Backlog

**Status:** Product-review record — requires its own bounded implementation preflight before code
**Date:** 2026-08-21
**Related:** UI-001, UI-003, UI-004, UI-006; Build Logs 132–133

## Purpose

Record the observations from the first wide/narrow Queue + Priority Preview visual review without
silently expanding the completed UI-001 Step 3/4 implementation. This is a prioritized refinement
backlog, not a new locked decision or implementation authorization.

## Immediate refinement candidates — preflight next

### 1. Queue-pane density

The 360px Queue pane currently retains too much full-page vertical chrome before the request rows.
Preflight a pane-mode-only compact presentation that:

- replaces the full-page business H1/subtitle with a compact `REQUEST QUEUE` label;
- keeps primary queue navigation and secondary controls within two compact rows;
- combines search and status filtering into one inline control row; and
- preserves the full-page H1/subtitle and existing controls in the narrow/one-pane fallback.

Do not move the actual request list above Pane 2 in this refinement. That is a materially different
desktop composition and needs a separate design decision after reviewing the compact pane result.

### 2. Scan-only Queue rows in pane mode

Queue cards in the bounded pane should be scan-and-select surfaces. Preflight hiding their filled
`Update customer` and outlined `Log contact` controls only in pane mode; a row selection opens the
durable request route and moves action-taking into the request work area. Preserve the richer
one-pane fallback row treatment unless separately redesigned.

### 3. Priority Preview richness

The current UI-003 branches are functionally correct but visually sparse. Preflight an elevated
card treatment using only already-authoritative list-summary data: a real type anchor, customer
identity, attention reason, original customer need when available, service location when available,
and one Keep-teal `Open request` action. Do not add a second fetch, auto-select a request, or turn
this into a business-wide dashboard.

## Deferred product candidates — not authorized by this record

| Candidate | Why deferred / required decision |
|---|---|
| Daily Continuity Briefing | `At Risk` and review counts can reuse existing signals, but `On Track` has no authoritative definition or count. Must decide whether metrics follow the active filtered queue or represent the whole business. |
| J/K queue navigation and action hotkeys | Needs a durable selection/history, focus, permission, modal, and text-input contract. Revisit after the embedded Step 5 Workbench exists. Do not add row-level hotkey badges by default. |
| Queue micro-context chips | Worth a later bounded preflight, but only for fields already returned and authoritatively defined. Photos, attached assemblies, and unread-note counts must not be fabricated or inferred. |
| Inbound call match | A phone lookup may offer an explicit possible-match action; never auto-select/navigate from ordinary search input. Requires a separately designed call-handling mode. |
| Multi-color promise-aging rails | Retain the existing canonical attention rail for now. Freshness color alone is not a truthful health signal; any expansion must use server-authoritative semantics and labels. |
| Automatic name title-casing | Rejected. Presentation-layer casing can corrupt customer-preferred and legitimate name/business capitalization. |

## Verification carried forward

The Step 4 visual acceptance still requires real-browser evidence at the actual 360px Queue width,
for Owner/Admin and Operator, at 100%, 125%, and 150% browser zoom: no clipped labels/horizontal
scroll, usable touch targets, correct Office Review/Views/History treatment, and clean one-pane
fallback on resize. The compact refinement must repeat these checks.
