# Session Log — OpHalo Foundation

**Last updated:** 2026-08-27
**Purpose:** active handoff only. Completed work belongs in Git history and the relevant build log.

## Authoritative sources

- Release priority and acceptance status: [pilot-readiness-bug-tracker.md](pilot-readiness-bug-tracker.md)
- Product decisions: [decision index](decisions/README.md) and individual ADRs
- Durable implementation evidence: [build logs](build-log/)
- Request Detail interaction contract: [Workbench signoff specification](ux-design/v2/request-detail-workbench-signoff-spec.md)
- Mobile workflow: [PWA mobile workflow specification](ux-design/v2/pwa-mobile-workflow-spec.md)

## Active handoff — Price Book visual polish

Items 0–2 are all COMPLETE (2026-08-27). Next approved work is in **Deferred / still required**
below — the auth-expiry redirect and the Actual Work composer real-device zoom check are the
highest-priority remaining items.

### 0. Migrate Settings and Getting Started to the V2 application layout — COMPLETE (2026-08-27)

**Shell migration.** `App.tsx` `isWorkbench` → `usesTopNavShell`, now covering `home` and
`settings` as well as requests/detail/pricebook. Getting Started and Settings render in the V2
top-nav application shell — one horizontal header, no desktop left `<aside>` (the sidebar block was
dead once every authenticated route used the header, and was removed). Header "Getting Started" and
"Settings" buttons gained the active-nav styling the other items already had. Mobile is unchanged:
`md:hidden` top bar + `MobileNavMenu`, with Price Book and Settings still omitted from the phone
overflow (`PHONE_OMITTED_NAV_IDS`). Global "New Request" CTA shows on Getting Started and Settings,
still suppressed on Price Book routes.

**Inner-page migration.** `Settings.tsx` and `Home.tsx` (Owner + Operator) use `max-w-[1440px]`
page rhythm, `keep-page-title`/`keep-page-subtitle` headings, token tab bar, and `--ophalo-*` /
`--keep-*` primitives. All four settings sections are card surfaces with tokenized form controls,
`KeepButton` submit/invite actions, and token loading/error/saved states; Settings keeps a readable
`max-w-2xl` inner form column, Getting Started a `max-w-xl` column. Replace-link confirmation, Team
role/status actions, public-link preview mock (incl. `object-contain`), routes, role gates, and
`scrollToSection` unchanged — token/component migration only.

Verified: full `web/ophalo-app` suite 708/708 (new `Settings.v2Shell.test.tsx` +2 App shell tests),
`tsc --noEmit` clean, `git diff --check` clean. See Git history for the change set.

### 1. Signed-in user name beside role — COMPLETE (2026-08-27)

`GET /auth/me` now returns nullable `userName` (`AuthenticatedWorkspaceIdentity.UserName`, projected
from linked `User.Name` in `EfMemberManagementPersistence`; empty/whitespace normalized to null at
the endpoint). The desktop workbench header right-side control renders `userName · role`, falling
back to role-only when absent. No email fallback. Sidebar and mobile identity labels unchanged.
Verified: `AuthApiTests` 34/34, `App.test.tsx` + `CompanySection.phone.test.tsx` 34/34,
`tsc --noEmit` clean. See Git history for the change set.

### 2. Coherent Price Book editing model — COMPLETE (2026-08-27)

Catalog item identity/settings edit (display name, SKU, category, Common Item) and offering/
assembly header edit (name, primary item, price treatment) now open dedicated responsive side
drawers — `CatalogItemEditDrawer.tsx` and `OfferingAssemblyHeaderEditDrawer.tsx` — matching the
create/Nudge drawer pattern (`KeepModal` shell, `w-full sm:w-[480px]/[520px]`, focus trap/restore,
Escape, `backdropClosable={false}`, nested discard-confirm on a dirty dismiss). The inline
header-edit `<form>` states are removed from `CatalogItemDetail.tsx` and `OfferingAssemblyDetail.tsx`.

Ownership split: each drawer owns form state, validation presentation, dirty-dismiss protection,
and field-level API errors; the detail page keeps refresh/invalidation and version-conflict
recovery. On `VersionMismatch` the drawer hands the draft back via `onVersionConflict`; the page
stores it, closes the drawer, refetches, disables Edit during the refresh, and restores that draft
once into the next deliberate Edit (unchanged `conflictDraft` / `conflictRefreshPending` behavior,
now consumed via a separate `editSessionDraft`). Catalog `categoryPending` gate preserved. No
shared helper module (small local drawer-shell/discard duplication accepted). Aliases, catalog
pricing/cost, and assembly component editing untouched. No backend changes.

**Production-hardening corrections made during this slice's validation pass:**

- **Save version frozen at drawer open** (`versionRef`) — a background refetch can no longer let a
  save land against a `concurrencyVersion` the user never saw.
- **Restored conflict drafts read as dirty** — baseline is always the item as loaded, so
  abandoning a re-apply routes through the discard confirmation instead of dropping silently.
- **No modal close path bypasses dirty-dismiss** — `backdropClosable={false}` plus
  `attemptClose` no-op while a save is in flight; Escape and header-Close both route through the
  confirm.
- **Focus return after the drawer closes** (WCAG 2.4.3) — detail-page effect focuses the Edit
  trigger on a normal cancel/save, or the conflict banner after a version conflict.
- **Intentional correction, not preserved behavior:** `OfferingAssemblyDetail` now renders a
  version-conflict banner (catalog already had one; assembly previously set `conflictDraft` but
  showed nothing). It gives the user feedback and a valid post-conflict focus destination.

Verified: full `web/ophalo-app` suite 741/741 (+33 across the two new drawer specs and
`CatalogItemDetail`/`OfferingAssemblyDetail` additions covering version-freeze-across-rerender,
backdrop/Escape while dirty, restored-draft guard, and focus return), existing detail-page tests
pass unmodified, `tsc --noEmit` clean, `check:tokens` pass, `git diff --check` clean. See Git
history for the change set.

Known adjacent issue (not touched): the create `OfferingAssemblyDrawer` renders its discard
confirmation inside its own `inert` form — the buttons would be non-interactive in a real
browser. The new edit drawers place the confirmation outside the form.

## Deferred / still required

### Deferred — post-V2 business-page polish and onboarding information architecture

The V2 shell migration for Getting Started and Settings is complete. The following are deliberate
follow-ups, not acceptance defects in that migration, and must stay behind the higher-priority
Price Book edit-drawer and authentication-expiry work.

**Getting Started: server-backed setup checklist.** Replace the current passive three-card
orientation with a truthful progress view and direct actions. Use existing server-owned onboarding
facts; never add client-only/manual completion checkboxes. Required steps must be distinct from
optional team invitation so a solo business can complete setup. Completed steps remain reviewable
but visually quiet; the next incomplete step is prominent. Candidate actions: copy/open public
link, create a request, and jump to Team. Preflight the current onboarding response before deciding
the exact step-to-data mapping and completion wording.

**Settings: Team management polish.** Retain readable `max-w-2xl` form tabs, but allow the Team
tab its own wider desktop content region when members justify it. Replace parenthetical identity
text such as `(you) (primary owner)` with structured identity/role/status badges. Verify the
authorized action matrix for active, invited, suspended, and removed members before adding or
repositioning controls; the primary owner must never receive unsafe self-management actions. Add a
clear empty state for "Show removed members" when there are none. A desktop table is appropriate
only if the actual member count/columns make it more readable than the responsive list.

Do not add a seat-purchase/billing link unless a real, authorized billing destination exists.

**Future header architecture decision.** Consider reducing permanent primary navigation to
Requests and Price Book, with a temporary `Setup n/m` pill while setup is incomplete and an
accessible user menu containing Settings, Team, and Log out. The Setup control should disappear
when all required setup work is complete; do not replace it with a permanent "Getting Started
(Completed)" item. This is a separate navigation/authentication accessibility slice—not an
incidental visual change—and needs an explicit product decision before implementation. The current
V2 top navigation remains the approved interim design.

### Authentication UX follow-up — redirect immediately when an open SPA session expires

**Verified current policy:** Browser sessions have a **60-day absolute lifetime** and a **30-day
inactivity window**. Every authenticated request rechecks the opaque-token hash, absolute expiry,
inactivity, revocation, account/user integrity, and Active membership. Activity is persisted no
more often than every five minutes, but never extends the 60-day absolute deadline. Therefore, an
actively used session remaining signed in for more than 30 days is expected; the user should be
challenged at the earlier of 30 days with no authenticated API activity or 60 days after sign-in.

**Gap:** `AuthGuard.tsx` redirects to sign-in when its initial `GET /auth/me` receives 401, but
ordinary API calls made later by an already-open SPA have no centralized 401 handling. The server
correctly rejects them, but the user may see an action-level error until a refresh instead of being
sent directly to sign-in.

**Claude implementation handoff:**

1. Preflight every `apiFetch` family in `web/ophalo-app/src/lib/apiClient.ts` and the app's
   sign-in/public-base URL handling. Add one centralized, loop-safe 401 path that clears any
   relevant client query state and navigates to `${VITE_PUBLIC_BASE_URL}/signin`.
2. Do not redirect for 403: it is an authenticated authorization/entitlement result and must keep
   its existing local treatment. Do not turn transport, validation, conflict, or server errors into
   sign-in redirects.
3. Avoid redirect loops for an auth endpoint/sign-in route and avoid performing browser navigation
   from test or non-browser environments without a safe boundary.
4. Add focused tests proving: the initial AuthGuard path remains correct; a later protected-call
   401 redirects once; 403 and non-401 failures do not redirect; and the user cannot continue to
   mutate after session expiry. Also add explicit API integration coverage for the 30-day
   inactivity boundary and the 60-day absolute-expiry boundary if it is not already present.

### Pilot release gate — Actual Work composer real-device zoom

The sole remaining Mobile V2 signoff check: on a real phone or real browser zoom, verify pinch/zoom
behavior for Actual Work's full-bleed fixed (`fixed inset-0`) composer. The CSS zoom proxy covered
normal canvas content but cannot faithfully exercise this fixed surface. Record the result in the
pilot tracker/build log; do not mark complete from the proxy alone.

### Post-pilot — phone-safe Price Book lookup

The phone overflow intentionally omits Price Book, Settings, and Account Administration; desktop
and tablet retain them. The first post-pilot administration candidate is a phone-safe read-only Price
Book lookup. Editing requires separately scoped mobile-native design. The drawer decision above does
not authorize exposing the current desktop workspace in phone navigation.

### Post-pilot — Quote Production Readiness Gate

`ProposedScopeComposer.tsx` is intentionally unmounted for the pilot. Do not expose it until its
own preflight, tests, and connection-recovery batches complete:

1. Search, drafts, and undo: `ComposerSearchAndAdd.tsx`, `ComposerDraftList.tsx`, and
   `ComposerUndoToast.tsx`, with `ProposedScopeComposer.tsx` owning one
   `ConnectionFailureBanner`.
2. Quick actions, Nudges, and submit: `ComposerQuickActions.tsx`, `ComposerNudgePanel.tsx`,
   and the composer submit handler.

Preserve server validation/conflict behavior. Transport failures need explicit retry of the original
captured payload. Batch gate: at most three mutation-handler families, eight production files, and
twelve files total per batch.

### Deferred — Owner/Admin Actual Work financial review UI (8B)

Revalidate before implementation. Backend financial-detail and review endpoints exist and return a
concurrency version. The UI must be Owner/Admin only; quietly hide 403/entitlement denial; load per
submitted visit; submit the exact returned version; and refresh on `ActualWork.AlreadyReviewed` to
show the actual reviewer/note. Keep it separate from price-blind Operator/field workflows.

## Guardrails

- The responsive staff PWA (`web/ophalo-app`) is the active field surface; native parity is not implied.
- Do not infer authority for quotes, pricing, invoicing, payments, QuickBooks, inventory, or fleet from Request Detail work.
- Price Book requires its capability package. Use disposable local data for mutable acceptance; never seed founder production data.
- Before a production candidate, complete repository checks and the controlled production smoke test: health/readiness, release identity, error capture, alert routing, and telemetry redaction.
- Preflight current code and the controlling ADR/build log/tracker. Make one reviewable change set at a time; stop for product direction when server data or authorization cannot truthfully support a UI.
