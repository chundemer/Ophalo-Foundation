# BL144 — Settings & Getting Started V2 UI upgrade

**Status:** Slices A, B, C delivered (frontend-only, `ophalo-app`). Locked contract:
[settings-and-getting-started-ui-upgrade.md](../ux-design/v2/settings-and-getting-started-ui-upgrade.md).
Preserves ADR-428 IA, defaults, and day-zero model. The §5 screenshot-acceptance pass for Slices B
and C (Public Link & Profile, Team) is still pending (product-owner review, not a coding task).
**Slice A's Getting Started/Home acceptance item is superseded (2026-09-06):** BL142 Session 3
(ADR-496) removes the Getting Started page/nav entirely and migrates its live-link readiness
content into the Requests empty-state panel — the request-first onboarding direction supersedes
review of the page it removes. See
[BL142](142-pilot-onboarding-upgrade-handoff.md#session-3--request-first-pwa-onboarding).

## Slice A — Getting Started + Settings shell + Response Policy — delivered

`keep-settings-frame` + `keep-field` shared recipes added to `app.css`. `Home.tsx` `OwnerHome`
rebuilt as the readiness panel + subordinate optional rows (reads the shared `["intake"]` query, no
new fetch/mutation). `Settings.tsx` shell on the 880px frame with section-shaped loading/error
placeholders. `PolicySection.tsx` inputs on `keep-field`. New `Home.readiness.test.tsx` (7 cases,
pins the no-checklist/no-meter contract); `Settings.v2Shell.test.tsx` updated. Full app suite 1071
passed; production build passes.

## Slice B — Public Link & Profile — delivered

`CompanySection.tsx` inputs/select on `keep-field`; `PublicLinkSection.tsx` — customer preview
reframed as the Keep-teal moment and all `slate-*` drift converted to tokens, replace-link warning
moved into an attention callout, confirmation + edit-name inputs on `keep-field`. Field sets,
"Branding & trust anchors" grouping, Save company, Edit link name, and the Replace-link destructive
flow (stale-link warning, one-time raw successor URL) are all logic-unchanged.
`PublicLinkSection.logo.test.tsx` gains a preview V2-treatment / no-`slate` assertion. Full app
suite 1072 passed; production build passes.

## Slice C — Team — delivered

`TeamSection.tsx` invite row (email input + role select + button) moved to the shared `keep-field`
recipe with 44px targets and clean narrow stacking (`flex-col sm:flex-row`, full-width controls
below `sm`). Member rows, the serif `keep-row-title` `<h2>`, the solo-owner reassurance copy,
`seatUsage` server display, and every roster/invite/role/resend/suspend/remove flow are
logic-unchanged. New `TeamSection.recipe.test.tsx` (4 cases: invite-row `keep-field`, tokenized
list-row container / no `slate`-`emerald`, solo-owner copy, server seat-usage value). Focused
settings + Home suites 38 passed; production build passes.

## Sequencing

Sequenced after GAP-039 → GAP-033 (per session log next-implementation order). Next up after this
feature completes its acceptance pass: Price Book direct-cost visibility (see session log deferred
work).
