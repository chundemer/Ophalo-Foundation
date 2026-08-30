# Session Log — OpHalo Foundation

**Last updated:** 2026-08-30 (4e-ii-b-1 committed locally as `2be5203`; 4e-ii-a as `82d9b9e`)
**Purpose:** active handoff only. Completed implementation detail belongs in Git history and the
relevant build log.

## Authoritative sources

- Release priority and acceptance status: [pilot-readiness-bug-tracker.md](pilot-readiness-bug-tracker.md)
- Product decisions: [decision index](decisions/README.md) and individual ADRs
- Durable implementation evidence: [build logs](build-log/)
- Actual Work closeout/replacement contract: [ADR-494](decisions/ADR-494-actual-work-paper-compatible-pilot-upgrade.md), [ADR-493](decisions/ADR-493-actual-work-office-financial-resolution-and-billing-revisions.md), and [BL136 P](build-log/136-P-preflight.md)
- Request Detail interaction contract: [Workbench signoff specification](ux-design/v2/request-detail-workbench-signoff-spec.md)
- Mobile workflow: [PWA mobile workflow specification](ux-design/v2/pwa-mobile-workflow-spec.md)

## Active handoff — controlled field pilot and Actual Work

Keep is the factual field record for supported work. The contractor's existing system remains
authoritative for estimates, invoices, payments, and accounting during the pilot; keep the
existing-ticket workflow as the explicit outage fallback.

### Deployment / migration state

`main` at `8495ba3` (pushed 2026-08-30) carries the 4e-0 signal seam, 4e-i supersession foundation,
and `AddActualWorkSupersession`. Railway deployment completed and the migration is applied —
confirmed 2026-08-30; `ActualWorkSupersessionPersistenceTests` (4) pass. Local commits `2be5203`
(4e-ii-b-1), `82d9b9e` (4e-ii-a) and earlier are **not yet pushed**. No migration in 4e-ii-b-1
(read-path + mapper + response-shape only).

### Prior slice — 4e-ii-a (local commit `82d9b9e`)

`Supersede` guard widened to the ADR-494 pre-export rule; `ActualWorkReplacementApiService` added
(Application, DI-registered, **no public route**) building the Draft successor from the source and
handing it to `IActualWorkSupersessionPersistence`. Full detail in Git history / BL136.

### Completed this session — 4e-ii-b-1 (local commit `2be5203`)

BL136 D6c operational hardening, reads only (no mutation family). Slice 4e-ii was split into two
independently compiling slices to stay within the batch gate. Eligible-visit filtering and the
Resolved→Closed close gate are **deferred to 4g / Billing-Revision work** per BL136 D8 — not
introduced early.

- `ErrorHttpMapper` ← `ActualWork.Superseded` (409, reconcilable).
- `superseded_at_utc IS NULL` on the unreviewed review-queue list + count
  (`EfActualWorkFinancialReviewPersistence`).
- Single-visit financial-detail read returns the `ActualWork.Superseded` reconcilable outcome for a
  superseded source (`ActualWorkFinancialReadApiService`, after `NotFound`).
- `ActualWorkHistoryReadApiService` stays **unfiltered**; each `submittedVisits` entry now carries
  explicit-direction lineage: source → `superseded: true` + `supersededByActualWorkId`; successor →
  `supersedesActualWorkId`. `KeepEndpoints` emits the three fields.
- Verification: integration 41 pass (2 new), architecture 14 pass, focused unit 140 pass.

### Next code slice — 4e-ii-b-2 (start a fresh session)

Superseded-source mutation rejection. New `Superseded` result value on the review /
financial-resolution / zero-line-disposition seams (`IActualWorkReviewPersistence`,
`ActualWorkResolutionResult` + `ActualWorkDispositionResult`); Infra guard
(`if (visit.SupersededAtUtc is not null)`) **after** each path's existing version-mismatch check;
each ApiService maps the new outcome to `ActualWorkErrors.Superseded`. 3 mutation families, ~7 prod
files. Tests: per path, stale client → `VersionMismatch` first, current client on a superseded
source → `ActualWork.Superseded`.

Then **4e-ii-c**: map `POST .../{id}/replace` and the Draft `SetZeroLineDisposition` route
(recorder-only + concurrency-checked). Then **4e-iii** replacement UI. Preserve field price
blindness and all source history throughout.

### Remaining pilot/release work

- **Production error/usage insight and friction loop:** errors-only Sentry with release/correlation
  metadata, PII/secret/token removal, founder alert routing, privacy-safe daily counters, and an
  authenticated Report Friction/support route without customer free text by default.
- **Pilot acceptance and rehearsal:** real-device/browser-zoom validation of the fixed Actual Work
  composer; normal-repair and diagnostic/no-work flows; non-recorder and Owner/Admin review paths;
  alert/feedback routing; field fallback/escalation guide; targeted phone/tablet/desktop quality pass.
- **Price Book nudge ordinal defect:** display the API's one-based `Order` directly. Do not change
  persisted/API numbering or composer behavior; cover one-, two-, and three-suggestion rules.

## Deferred / still required

### Minimum Office Closeout, accounting export, and reconciliation

The financial-resolution, no-charge disposition, review gate, and office review-card foundation
are complete. After 4c–4g and the rehearsal gate, resume the Minimum Office Closeout plan in
[BL135](build-log/135-minimum-office-closeout-mechanical-preflight.md) / ADR-493:

1. Billing Revision domain and persistence.
2. Draft assembly/detail read, then Ready/Void and Handed Off.
3. Billing Revision summary UI.
4. Preflight the correction/adjustment flow, including the pre-/post-export behavior above.

Do not expose “Ready for billing” without the durable Billing Revision record that prevents
duplicate handoff. The design must prove effective-resolution/supersession, one unreleased revision
membership per visit, and one Draft/Ready revision per request. Financial controls remain on
`ActualWorkReviewCard`, never the price-blind `ActualWorkComposer`.

CSV generation, QuickBooks/API integration, invoice creation, payments, tax, inventory,
reconciliation, and Accountant UI remain deferred. Future export serializes the immutable Billing
Revision; it must not rebuild financial facts from live visits.

### Deferred — office-financial role model

`PermissionKeys.Keep.AccountingManage` is the shared office-financial seam, but current closeout
surfaces retain their explicit Owner/Admin gate. Before a narrower accounting/Accountant role is
introduced, run a dedicated authorization/product discovery covering membership/invitation,
read/mutation authority, price-blindness, UI/navigation, audit, and migration compatibility.
Until then, AccountingManage remains Admin-tier (Owner inherits).

### Deferred — post-V2 onboarding and business-page polish

Build a server-backed setup checklist using existing onboarding facts—no client-only checkboxes.
Required setup steps must be distinct from optional team invitation; completed work remains
reviewable but quiet, with the next incomplete action prominent.

For Settings/Team, retain readable form tabs, use structured identity/role/status badges, validate
the active/invited/suspended/removed authorization matrix, protect the primary owner from unsafe
self-management, and add an empty state for removed members. Do not add billing/seat-purchase
links without a real authorized destination.

The future header architecture decision is separate: consider permanent Requests + Price Book,
a temporary `Setup n/m` pill while incomplete, and an accessible user menu for Settings, Team, and
Log out. The current V2 top navigation remains approved until an explicit decision.

### Deferred — phone-safe Price Book lookup

Phone navigation intentionally omits Price Book, Settings, and Account Administration. The first
post-pilot candidate is a phone-safe read-only Price Book lookup; editing needs a separately scoped
mobile-native design.

### Deferred — Quote Production Readiness Gate

`ProposedScopeComposer.tsx` remains unmounted for the pilot. Do not expose it until its own
preflight, tests, and connection-recovery batches complete:

1. Search, drafts, and undo (`ComposerSearchAndAdd`, `ComposerDraftList`,
   `ComposerUndoToast`) with one `ConnectionFailureBanner` owner.
2. Quick actions, Nudges, and submit.

Preserve server validation/conflict behavior. Transport failures need explicit retry of the
captured payload. Batch gate: at most three mutation-handler families, eight production files, and
twelve files total per batch.

## Guardrails

- The responsive staff PWA (`web/ophalo-app`) is the active field surface; native parity is not implied.
- Do not infer authority for quotes, pricing, invoicing, payments, QuickBooks, inventory, or fleet from Request Detail work.
- Price Book requires its capability package. Use disposable local data for mutable acceptance; never seed founder production data.
- Before a production candidate, complete repository checks and the controlled production smoke test: health/readiness, release identity, error capture, alert routing, and telemetry redaction.
- Preflight current code and the controlling ADR/build log/tracker. Make one reviewable change set at a time; stop for product direction when server data or authorization cannot truthfully support a UI.
