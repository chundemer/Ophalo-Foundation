# Session Log — OpHalo Foundation

**Last updated:** 2026-08-30 (4e-iii-b implemented locally, commit pending; 4e-iii-a commit pending; 4e-ii-c `e19e5f9`)
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
confirmed 2026-08-30; `ActualWorkSupersessionPersistenceTests` (4) pass. Local commits `e19e5f9`
(4e-ii-c), `29db179` (4e-ii-b-2), `2be5203` (4e-ii-b-1), `82d9b9e` (4e-ii-a) and earlier are **not yet
pushed**. No migration since 4e-i (4e-ii-a/b-1/b-2/c are code-only: services, read/mutation guards,
routes, mapper, response shape).

### Prior slice — 4e-ii-a (local commit `82d9b9e`)

`Supersede` guard widened to the ADR-494 pre-export rule; `ActualWorkReplacementApiService` added
(Application, DI-registered, **no public route**) building the Draft successor from the source and
handing it to `IActualWorkSupersessionPersistence`. Full detail in Git history / BL136.

### Prior slice — 4e-ii-b-1 (local commit `2be5203`)

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

### Prior slice — 4e-ii-b-2 (local commit `29db179`)

Superseded-source mutation rejection. New `Superseded` result value on the review /
financial-resolution / zero-line-disposition seams (`ActualWorkReviewResult`,
`ActualWorkResolutionResult`, `ActualWorkDispositionResult`); Infra guard
(`if (visit.SupersededAtUtc is not null)`) immediately **after** each path's existing
version-mismatch check in `EfActualWorkReviewPersistence` and both `EfActualWorkFinancialResolutionPersistence`
orchestrators; the three ApiServices map the new outcome to `ActualWorkErrors.Superseded` (already
409-reconcilable from 4e-ii-b-1). No migration. Verification: integration 30 pass in the two touched
persistence classes (+6 new), architecture 14, related ActualWork API/supersession 55, focused unit 140.

### Prior slice — 4e-ii-c (local commit `e19e5f9`)

Final API-exposure slice for replacement-copy. No migration.

- **Replacement route:** `POST /keep/pricebook/actual-work/{actualWorkId}/replace` (Actual Work
  route family, no request id, `X-Keep-ActualWork-Version` header) → `ActualWorkReplacementApiService`
  `.CreateReplacementAsync`; returns `{ successorActualWorkId }`.
- **Zero-line route:** `PUT /keep/pricebook/actual-work/{actualWorkId}/zero-line-disposition` →
  new recorder-only, Draft-guarded, concurrency-checked `ActualWorkDraftApiService.SetZeroLineDispositionAsync`
  wrapping `ActualWork.SetZeroLineDisposition`; endpoint parses the outcome string like `/submit`
  (null/invalid → `ActualWork.InvalidOutcome` 400); returns the rotated concurrency version.
- **Conflict ordering** in `CreateReplacementAsync`: source-version check → `SupersededAtUtc` →
  open-Draft precondition → build successor → atomic `SupersedeAsync` (re-checks version + supersede
  inside the transaction). `ErrorHttpMapper` ← `ActualWork.AlreadySuperseded` (409, reconcilable).
- **Not guarded:** the zero-line route accepts a lined Draft (mirrors `SetVisitNote`); harmless —
  `Submit` overwrites `Outcome`/`CompletionNote` unconditionally, so no stale state survives submit.
- Route/regression tests live in `ActualWorkVisitNoteApiTests.cs` (not a dedicated file).
- Verification: integration 19 in that class (+7), architecture 14, 290 ActualWork integration,
  replacement-service unit 12, `git diff --check` clean.

### Completed this session — 4e-iii-a replacement-copy correction UI (local commit pending)

UI-only, `web/ophalo-app`. 9 production + 5 test-file extensions. No backend, no migration.

- **Correction action:** new `ReplaceVisitForm.tsx` (reason-required disclosure, ≤2000) on every
  visit in `ActualWorkReviewCard` — reviewed or not. `useActualWorkFinancialReview.replace()` joins
  the outcome family: 409 `DraftAlreadyOpenForRequest` → `replace-blocked-open-draft`; 409
  version / `AlreadySuperseded` → `reconciled`+reload; 403 → hidden; 400 → validation-failure.
- **Auto-open:** `RequestDetailContent` filters `superseded` visits out of the review-hook input
  (history read stays unfiltered for lineage), then on `replaced` refreshes history and calls
  `useActualWorkCapture.openReplacementDraft(successorId)` — opens the composer **only** when the
  authoritative read confirms `canCaptureActualWork` **and** the open Draft id === the returned
  successor id (guards the no-capture 403 path and the concurrent-Draft race). Otherwise an
  explicit "Open replacement draft" recovery affordance is shown.
- **Composer banner:** session-scoped `replacementCorrection` flag (set only after a confirmed
  auto-open, cleared on close/submit) drives a "this draft replaces a superseded visit" notice.
- **Lineage badges:** `ActualWorkHistoryCard` — "Superseded · replaced by a correction" /
  "Correction of an earlier visit" from the DTO lineage fields.
- Verification: `tsc` clean, full web suite 843 pass (90 files), CSS token check, `git diff --check`.

### Completed this session — 4e-iii-b composer zero-line disposition prefill/persistence (local commit pending)

UI-only, `web/ophalo-app` (BL136 §4e-iii). 4 production + 2 test files. No backend, no migration.

- **Client:** `api.setActualWorkZeroLineDisposition(id, outcome, completionNote, version)` →
  `PUT /keep/pricebook/actual-work/{id}/zero-line-disposition` (version header, body
  `{ outcome, completionNote }`, returns the rotated version).
- **Hook:** `useActualWorkCapture.setZeroLineDisposition` mirrors `setVisitNote` — `refetchDraft`
  on success; `ActualWork.InvalidOutcome` (400) → `"invalid"`; `VersionMismatch` / `NotDraft` →
  shared reconcile + `"stale"`; else `"failed"`.
- **`ActualWorkSubmitFooter`:** zero-line `outcome` / `completionNote` seed from `draft.outcome` /
  `draft.completionNote`; call site keys the footer on `` `${draft.outcome}|${draft.completionNote}` ``
  so a persisted write re-seeds from the server trim and survives reload. Blur persists via the hook
  **only once a valid outcome exists** (the route rejects a blank outcome; the note stays local
  until then), sending outcome + note together. `"invalid"` shown inline. Fields disabled while a
  blur write is pending (serialises the two field writes). **Blur/submit race guard:** a blur into
  the Submit control skips the disposition write (`submitIntentRef`, set on the button's
  pointer-down), and Submit is disabled while an ordinary blur write is in flight — so the autosave
  and the final submit can never issue against the same pre-write version. The final `Submit` still
  sends the local fields.
- Verification: `tsc` clean, full web suite 850 pass (90 files), `git diff --check` clean.

### Next code slice — 4e-iii-a review follow-up (fresh session)

Focused follow-up on the -a replacement-copy correction UI, kept separate from the -b persistence
slice: capture-permission gating, successor-ID verification, and the session replacement notice.
4g / Billing-Revision deferrals (eligible-visit filter, Resolved→Closed close gate,
revoke-on-replace) remain out of scope.

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
