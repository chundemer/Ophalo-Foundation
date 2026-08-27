# Session Log — OpHalo Foundation

**Last updated:** 2026-08-27
**Purpose:** active handoff only. Completed work belongs in Git history and the relevant build log.

## Authoritative sources

- Release priority and acceptance status: [pilot-readiness-bug-tracker.md](pilot-readiness-bug-tracker.md)
- Product decisions: [decision index](decisions/README.md) and individual ADRs
- Durable implementation evidence: [build logs](build-log/)
- Request Detail interaction contract: [Workbench signoff specification](ux-design/v2/request-detail-workbench-signoff-spec.md)
- Mobile workflow: [PWA mobile workflow specification](ux-design/v2/pwa-mobile-workflow-spec.md)

## Active handoff — next-week controlled field pilot

Price Book visual-polish items 0–2 below are complete. The next release is a controlled field
pilot: Keep is the primary factual field record for supported work while the contractor's existing
system remains authoritative for estimates, invoices, payments, and accounting. Do not compress
unfinished slices into cutover; retain the existing-ticket workflow as the explicit outage fallback.

### Ordered next-week code slices

#### 1. P0 — Actual Work recorder ownership (GAP-055) — COMPLETE (2026-08-27)

**Backend (Batches A–D):** `b3b3d41`, `d26b955`, `72ce6a5`, `c7ce822` (documented by `7fc575a`).
Dispatch `Responsible` is routing context, not authority to record factual work. Any qualified
member may create the one open Draft; immutable `CreatedByUserId` retains authorship, exclusive
`RecorderAccountUserId` owns mutation/submission, Owner/Admin may perform a reason-required,
immutable-audited Draft-only transfer. Active-Draft constraint, concurrency token, write
authorization, assembly expansion, history, and nudge-read seams use the recorder model.

**Frontend state/copy + presence signal:** `ActualWorkHistoryResult` gains `openDraftHeldByOther`
(presence-only; never exposes recorder identity, mutually exclusive with a populated `openDraft`).
`useActualWorkCapture` routes both non-recorder cases — `openDraft.isRecorder === false`
(Owner/Admin) and `openDraftHeldByOther` (qualified non-recorder) — into one non-actionable
`held-by-other` state rendering “Another team member is recording this visit.”; no composer, no
start affordance. A create-time 409 re-probes into that state with no modal and no conflict notice.
Stale active-Responsible comments corrected in card/hook/types. Verified: app suite 754/754 (+4),
`ActualWorkHistoryApiTests` + all `~ActualWork` integration 148/148, `~ActualWork` unit 55/55,
architecture 14/14, `tsc`, `check:tokens`, `git diff --check` clean. Chrome check confirmed all
four card states (no-draft, recorder, held, Owner/Admin non-recorder).

The Actual Work nudge UI (slice 3) stays paused until it consumes this `held-by-other` Draft state.

#### 1a. P0 — Owner/Admin Draft recorder-transfer recovery UI — COMPLETE (2026-08-27)

Split into two batches (option C — recorder eligibility is a server-side invariant, not just a
picker concern; a stale/malicious client must not be able to strand a Draft with a member who
cannot record it). 1a-i + 1a-ii-a committed; 1a-ii-b below is the final commit for this slice.

**1a-i — server-side eligibility invariant — COMPLETE (2026-08-27, `48de17f`).** The
`transfer-recorder` endpoint now rejects a target who is not an active account member holding
`RequestsOperate` + `ActualWorkCapture`. New `ActualWork.RecorderTransferTargetIneligible` (422,
mirroring `KeepRequest.ParticipationTargetIneligible`; message "That team member can't be assigned
as the recorder." — no permission detail leaked). Non-member and unqualified collapse to one error
(no membership enumeration). Check is command-shape, ahead of the load/version/Draft-state guards.
Files: `ActualWorkErrors.cs`, `ActualWorkDraftApiService.cs` (`ActualWorkAuthorization` gains
`Purpose`), `ErrorHttpMapper.cs`, `ActualWorkRecorderTransferApiTests.cs` (+3 tests: Viewer,
non-member, non-active Operator — each asserts recorder + version unchanged and no audit record).
Verified: `~ActualWorkRecorderTransfer` 10/10, `~ActualWork` integration 151/151 (+3), `~ActualWork`
unit 55/55, architecture 14/14, `git diff --check` clean.

**1a-ii — recovery UI. Split approved 2026-08-27 into two commits (over the 8-production-file
gate; backend reads land and test independently of the UI that consumes them).**

**1a-ii-a — backend reads / types / tests — COMPLETE (2026-08-27, `7bdd857`).**
`ActualWorkOpenDraftEntry` gains `RecorderAccountUserId` + `RecorderDisplayName`, populated only
for the Owner/Admin non-recorder view (resolved via `IKeepRequestOperatePersistence.
GetActorDisplayNameAsync`, now injected into the history read); null for the recorder's own view;
field users still receive no `openDraft`. New account-wide `GET
/keep/pricebook/actual-work/recorder-candidates` — Owner/Admin-only, guard order per ADR-462
(account-access gate → entitlement resolver → `RequestsOperate` → Owner/Admin), filters
`GetParticipantCandidatesAsync` to the exact GAP-055 recorder predicate (`RequestsOperate` +
`ActualWorkCapture`) so Viewers and pending invites are excluded; non-qualified callers get an
opaque 403 (no enumeration). `apiClient` gains `getActualWorkRecorderCandidates` +
`transferActualWorkDraftRecorder` (sends `X-Keep-ActualWork-Version`); the two new open-draft
fields are optional on `ActualWorkOpenDraftEntry`. Files: `ActualWorkHistoryReadApiService.cs`,
new `GetActualWorkRecorderCandidatesService.cs`, `KeepEndpoints.cs`,
`KeepServiceCollectionExtensions.cs`, `apiClient.types.ts`, `apiClient.ts`; tests
`ActualWorkHistoryApiTests` (+identity assertions) + new `ActualWorkRecorderCandidatesApiTests`
(6 tests). Verified: `~ActualWork` integration 157/157 (+6), `~ActualWork` unit 55/55,
architecture 14/14, app suite 755/755, `tsc`, `git diff --check` clean.

**1a-ii-b — recovery UI — COMPLETE (2026-08-27, `de40491`).** `useActualWorkCapture` gains an `owner-recovery`
state: `routeHistory` now retains the populated read-only `openDraft` (`isRecorder: false`) for the
Owner/Admin non-recorder instead of collapsing it into `held-by-other` — version, lines, and current
recorder identity are kept for the transfer control. New `transferRecorder(id, displayName, reason)`
submits against the exact `concurrencyVersion`, then re-probes: an Owner/Admin who hands the draft to
someone else lands on `held-by-other`, one who self-assigns lands back on the editable `draft` state,
and either way a transient `recoveryNotice` ({tone,text}) — "Recording handed to {name}." — is stored
in hook state (survives the drawer unmounting) and rendered over the resolved card state. 422
`RecorderTransferTargetIneligible` → `ineligible` (drawer stays open, refetches candidates, no state
change); 409 `VersionMismatch`/`AlreadyReviewed`/`NotDraft` → `stale` (re-probe + warning notice,
drawer closes); other → `failed` (generic inline error, drawer open). New
`ActualWorkRecoveryDrawer.tsx` (`KeepModal`, right sheet): loads `getActualWorkRecorderCandidates`
via `useQuery`, excludes `draft.recorderAccountUserId`, required reason (500 max), disabled submit +
"No other team member is eligible" when the filtered list is empty, retry on candidate-load error.
`ActualWorkCard` renders the `owner-recovery` strip ("{recorder} is recording this visit." +
secondary "Reassign recorder") and the dismissible recovery banner over every non-hidden state;
qualified non-Owner/Admin `held-by-other` is unchanged (no affordance). `RequestDetailContent` adds
`owner-recovery` to card visibility, threads the notice props, and mounts the drawer.
Files: `useActualWorkCapture.ts`, `ActualWorkCard.tsx`, `RequestDetailContent.tsx`, new
`ActualWorkRecoveryDrawer.tsx`; tests `useActualWorkCapture.test.ts` (+6, existing Owner/Admin
held-by-other case re-pointed to `owner-recovery`; the stale path is parameterized across
`VersionMismatch`/`AlreadyReviewed`/`NotDraft`, each asserting `stale` + re-probe + warning notice),
`ActualWorkCard.test.tsx` (+2), new `ActualWorkRecoveryDrawer.test.tsx` (6, incl. `stale` closes the
drawer). Verified: full app suite 770/770 (+15), `tsc` clean, `check:tokens` passed, `git diff
--check` clean.

#### 2. P0 — Owner/Admin Actual Work audit, approval, and financial review UI (slice 8)

The Actual Work backend foundation is complete through review mutation and financial reads. Build
the Owner/Admin-only **Actual Work Review** tab in the existing Requests workspace plus a
request-detail review card. Show the FIFO queue of submitted/unreviewed visits; show immutable
sales-price, Standard/Expected Direct Cost, margin, totals, and explicit incomplete-financial-data
cues per visit; retain price blindness for field/Operator workflows. The review action must submit
the exact returned concurrency version and, on `ActualWork.AlreadyReviewed`, refresh to show the
actual reviewer/note. Successful review must update both queue and visit history.

Preflight the bounded frontend/API-type/test batch after recorder ownership is corrected. Quietly
hide 403/entitlement-denied surfaces; do not add a new top-level navigation item. Manual acceptance
must cover submitted, reviewed, stale-version, already-reviewed, incomplete-financial-data,
zero-line diagnostic, and role/entitlement-denial paths.

Slice 2 implemented (2026-08-27): the request-detail canvas now adds an Owner/Admin-only Actual
Work financial review module immediately below Work execution. It reads every submitted visit's
financial detail, presents immutable totals and line breakdowns, marks missing cost data explicitly,
and reviews with the returned concurrency version. A 403 returns no financial UI; a 409 refreshes
the authoritative record to reconcile stale/already-reviewed states. Successful review refreshes
visit history and invalidates both the review queue and authoritative queue-count. Queue rows now
navigate with `focus=actual-work-review` and smoothly scroll to the module after it loads.

Slice 2 review corrections (2026-08-27, second commit): (1) `ActualWorkFinancialDetailResult` gains
`ReviewedByDisplayName`, resolved server-side via
`IKeepRequestOperatePersistence.GetActorDisplayNameAsync` (mirrors the 1a-ii-a recorder-identity
pattern) — the card shows the reviewer name, never the raw id. (2) Incomplete-financial-data copy
corrected: totals/margin are "unavailable", not "estimated" (ADR-487: never a fabricated total or
margin). (3) Zero-line diagnostic visits render the structured outcome + completion note and an
explicit "no work lines" state. (4) The 409 conflict notice is persistent (no longer swallowed when
the visit flips to reviewed). (5) Successful review shows a transient confirmation. Files:
`ActualWorkFinancialReadApiService.cs`, `KeepEndpoints.cs`, `ActualWorkFinancialReadApiTests.cs`,
`apiClient.types.ts`, `ActualWorkReviewCard.tsx`, `RequestDetail.tsx`, +tests. Verified: full app
suite 780/780, `~ActualWork` integration 157/157, `~ActualWork` unit 55/55, architecture 14/14,
`tsc`, `check:tokens`, `git diff --check` clean.

#### 3. P1 — Actual Work field-assist nudge UI

The price-blind Actual Work nudge backend is complete. After slice 1 establishes safe ownership
states, wire the existing nudge suggestions into the field composer: fetch after the qualifying
catalog-item/assembly commit, render the session-only “Often added together” choices, add a chosen
catalog item or assembly through the existing mutation path, dismiss without persistence, and
reconcile a 409 without retrying the mutation. Do not expose pricing, auto-add work, or blur these
factual-completion nudges with Proposed Scope's commercial recommendations. Test catalog and
assembly triggers, accept/dismiss, conflict reconciliation, and the non-recorder state.

#### 4. P1 — Production error/usage insight and friction loop

Complete the errors-only Sentry slice with release/correlation metadata, PII/secret/token removal,
and founder alert routing. Add only privacy-safe daily pilot counters: sign-in, request created,
Actual Work Draft started, Actual Work submitted/failed, and Report Friction submitted. Provide an
authenticated Report Friction or equally visible support route with useful account/screen context
but no customer free-text capture by default. Assign a daily owner for alerts, failure counts,
usage, and friction reports.

#### 5. P1 — Pilot acceptance, real-device validation, and production rehearsal

Perform the remaining real-phone/browser-zoom check for Actual Work's full-bleed fixed composer;
the CSS zoom proxy is insufficient. Then rehearse deployed normal-repair and diagnostic/no-work
flows, including a non-recorder attempt and the Owner/Admin office-review loop; verify alert and
feedback routing; and publish the concise field fallback/escalation guide. Record real device
evidence in the tracker/build log. Include a targeted phone/tablet/desktop UI-quality pass for
loading, error, empty, focus, and touch-target states.

#### 6. P2 — Correct Price Book nudge suggestion ordinals

The nudge domain/API contract is one-based: suggestion `Order` is valid from 1 through 3. The
Price Book Nudge card currently sorts by that value but displays `order + 1`, so the first
suggestion is incorrectly labeled `2.` (as in the Blower Motor example). Change the card to render
the returned ordinal directly; do not change the persisted/API numbering or the composer behavior.
Update the Price Book nudge fixture and add/assert regression coverage for one-, two-, and
three-suggestion rules displaying `1.`, `2.`, and `3.` in their saved order.

### Recently completed

#### Centralized SPA session-expiry redirect — COMPLETE (2026-08-27)

`redirectToSignInOnce()` is a browser-safe, module-guarded shared redirect helper that navigates to
`${VITE_PUBLIC_BASE_URL}/signin` at most once. All three API fetch wrappers invoke it before
throwing an `ApiError` for a 401, and `AuthGuard` uses the same helper for its initial `/auth/me`
401 path. A full-page navigation clears in-memory query state, so no QueryClient-specific wiring
was added. 403, validation, conflict, transport, and server failures retain their local treatment;
there is no backend authorization change.

Verified: app suite 750/750 (+9), `AuthApiTests` 35/35 (+1, including a 31-day inactive-session
401 while the absolute deadline remains future), `tsc --noEmit`, `check:tokens`, and
`git diff --check` all clean. Focused browser coverage proves a redirect for a 401 through each
of the three wrappers (`apiFetch`, `apiFetchVoid`, `apiFetchMaybeJson`), a single redirect for
sequential 401s, and no redirect for 403, 500, or transport failures; AuthGuard withholds
children and redirects once for `/auth/me` 401.

### Next after the release gate — triage, do not silently bundle

Prioritize the remaining active pilot bugs by production evidence after the active release slices
above:
public-intake trust/return continuity (GAP-033); request workspace identity, scale/history,
search/filter, priority-update, private-link-email, and closed-follow-up gaps (GAP-041–049);
phone-entry parity and Quick Capture customer recognition (GAP-016, GAP-021, GAP-025, GAP-051);
and queue/action hierarchy review (GAP-053–054). Use the pilot tracker for their individual
acceptance criteria; none is automatically authorized as part of the Actual Work release.

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
follow-ups, not acceptance defects in that migration, and stay behind the ordered field-pilot
slices above.

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

### Deferred — Actual Work closeout, accounting export, and reconciliation

After the active release slices and rehearsal gate, the next Actual Work product preflight is the
**Minimum Office Closeout foundation** (ADR-493; Build Log 129). It is not a queue-polish task:
it introduces immutable per-line office financial resolution, server-derived visit billing
eligibility, request-bound Billing Revisions for manual legacy-system handoff/future export, and
explicit Addendum/Replacement correction semantics. It must prove revision-membership uniqueness,
pre/post-handoff correction behavior, authorization, concurrency, and the manual billing-summary
read before implementation. Do not expose a "Ready for billing" list without the durable Billing
Revision record that prevents duplicate handoff.

CSV generation, QuickBooks/API integration, invoice creation, payments, tax, inventory,
reconciliation, and an Accountant role/UI remain deferred. Future export serializes the immutable
Billing Revision; it must not rebuild financial facts from live visits.

## Guardrails

- The responsive staff PWA (`web/ophalo-app`) is the active field surface; native parity is not implied.
- Do not infer authority for quotes, pricing, invoicing, payments, QuickBooks, inventory, or fleet from Request Detail work.
- Price Book requires its capability package. Use disposable local data for mutable acceptance; never seed founder production data.
- Before a production candidate, complete repository checks and the controlled production smoke test: health/readiness, release identity, error capture, alert routing, and telemetry redaction.
- Preflight current code and the controlling ADR/build log/tracker. Make one reviewable change set at a time; stop for product direction when server data or authorization cannot truthfully support a UI.
