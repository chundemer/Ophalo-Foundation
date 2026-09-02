# Request Workspace Visual Token Specification

**Status:** Locked for GAP-067 implementation — 2026-09-02  
**Purpose:** Make the retained Request List/Detail desktop reference a mechanical implementation target. This document governs presentation only; the Request Detail interaction specification, GAP-027, GAP-065, and server-authoritative responses continue to govern behavior.

## Scope and implementation rule

GAP-067 must add Request-specific aliases in both token sources:

- `web/shared/styles/ophalo-tokens.css`
- `web/ophalo-app/src/styles/app.css`

Do not repurpose `--ophalo-canvas`, which remains a shared warm legacy canvas for other surfaces, or change Price Book's financial-workspace tokens. These aliases are scoped to Request List and Request Detail classes/components. Existing behavior, data, permissions, ranking, and responsive breakpoints are unchanged.

## Color tokens

| Semantic role | Token / value | Required use |
|---|---|---|
| Operational canvas | `--keep-request-canvas: #f8fafc` | Request workspace background only. |
| Card / app shell surface | `--ophalo-card: #ffffff` | White Request Anchor, modules, queue rows, and shell surfaces. |
| Muted request surface | `--keep-request-surface-muted: #f8fafc` | Customer Need and quiet inset rows; always retain a visible border where needed. |
| Standard border | `--ophalo-border: #e2e8f0` | Cards, inputs, dividers, and queue rows. |
| Subtle divider | `--ophalo-border-subtle: #edf1f5` | Internal section dividers only. |
| Primary ink | `--ophalo-ink: #172033` | Headings, customer/request identity, and control labels. |
| Muted metadata | `--ophalo-muted: #5d6878` | Secondary text and factual row metadata. Do not use a lighter gray for readable text. |
| Eyebrow label | `--keep-request-eyebrow: #64748b` | 10 px bold uppercase labels only. |
| Selected queue state | `--keep-accent: #168a9a`; `--keep-accent-bg: #e5f4f3` | Selection outline/accent and quiet active treatment—not a second primary action. |
| Customer-facing primary | `--keep-request-primary: #0f766e`; hover `#115e59` | White-text customer response/composer submit only. |
| Internal financial emphasis | `--keep-request-financial: #0f172a`; hover `#020617` | The contextual financial-review action only. |
| Active customer attention | background `#fffbeb`; border `#fbbf24`; text `#92400e` | The single customer-message attention rail/card. Never use for Customer Need. |
| Financial-review metadata dot | `#f59e0b` | The GAP-065 tiny, non-alert row dot only; it is never a badge, rail, or ranked exception. |

All text/background pairings must meet WCAG AA at the rendered size. In particular, eyebrow labels and muted metadata must not be replaced with low-contrast gray from a reference image.

## Consistency with Actual Work and Price Book

Request work is not a visual clone of the financial-review workspace. The distinct canvases are
intentional: Request work uses operational Slate-50 (`#f8fafc`); Actual Work financial review uses
the cooler `--keep-workspace-canvas` (`#eef2f7`). This distinction helps users recognize whether
they are triaging customer work or evaluating internal financial records.

The shared product grammar is non-negotiable: white surfaces, Slate-200 borders, rounded-xl cards,
rounded-lg controls, restrained one-step card shadows, the same ink/muted type scale, uppercase
micro-label treatment, visible focus rings, and the same 4/8/12/16/20/24 spacing scale. Queue rows
remain denser than financial-workspace modules (12 px between rows and 16 px row padding); this is
an intentional scan-density difference, not a new component system. GAP-067 does not restyle the
existing Actual Work financial workspace.

## Geometry and typography

| Element | Locked value |
|---|---|
| Wide work-canvas width | `min(100%, 1000px)`, anchored 24 px from the queue/workbench divider; do not center a narrow column and create an empty left gutter. The remaining right gutter is flexible whitespace, not a reason to invent a third rail. |
| Major-module spacing | 20 px; 24 px only at wide-canvas section breaks. |
| Card padding | 16 px narrow; 20 px wide. |
| Internal control gap | 12 px. |
| Card radius | 12 px (`rounded-xl`). |
| Input/button radius | 8 px (`rounded-lg`). |
| Card elevation | `0 1px 2px rgba(16, 36, 62, 0.06)` at most; do not stack pronounced shadows. |
| Eyebrows | 10 px, bold, uppercase, 0.08em tracking, `--keep-request-eyebrow`. |
| Standard controls | 36 px height, 14 px semibold text, 16 px horizontal padding unless an icon-only control requires less. |

## Component rules

1. The Request Anchor scan order is identity/status → contact, service location, owner → planning row → neutral Customer Need. Customer Need uses the muted request surface and standard border; it never borrows attention styling.
2. The customer-message attention card is the only strong amber panel. Its teal action is the visually dominant immediate customer action; “Resolve another way…” is text/quiet secondary.
3. `Review Visit Financials` may use internal-financial dark-slate emphasis inside its own module. Draft continuation, edit, navigation, and lifecycle actions are white outlined controls unless their existing authorized workflow requires otherwise.
4. Queue rows preserve GAP-027: one lifecycle cue, at most one server-ranked exception cue, and one next-action line. Preserve tab counts and factual next-action context. The GAP-065 financial cue is a separate quiet metadata line in both default and compact-pane rows, beneath the existing compact action-signal line in pane mode.
5. The shared Customer update/Internal note composer remains inline. Any banner action that says “Respond to customer” focuses or scrolls to this composer; it does not create a competing flow.

## Acceptance evidence

Before handoff, verify desktop and narrow screenshots against the retained reference, keyboard focus visibility, WCAG-AA text contrast for all new token pairings, and that GAP-027/GAP-065 cues, tab counts, next-action lines, the financial-review clarifier, and customer/contact versus request identity remain present and truthful.
