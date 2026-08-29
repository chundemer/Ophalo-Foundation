# Session Log — OpHalo Foundation

**Last updated:** 2026-08-29
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

The Actual Work nudge UI (slice 3) is complete — the composer that hosts the nudge chips only
mounts in the recorder's editable `draft` state, so it never renders for `held-by-other` /
`owner-recovery`.

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

#### 3. P1 — Actual Work field-assist nudge UI — COMPLETE (shipped in `6543f81`, Batch 5d-ii-d)

Delivered by build-log 129 Batch 5d-ii-d (`6543f81`), which landed on top of GAP-055 Batches A–C.
`ActualWorkComposer` fetches nudges after a catalog-item add or assembly expansion (generation-
guarded so the newest trigger wins), renders the session-only price-blind "Often added together"
chip panel inline, adds an accepted catalog item via `addActualWorkLine` / assembly via
`expandActualWorkAssembly`, dismisses without persistence, retires a rule only on explicit
accept/dismiss, and clears the panel on a 409 (defers to the existing `onConflict` path, no retry).
Manual field acceptance recorded in `7b29e25`.

Non-recorder state: no additional work needed. `useActualWorkCapture` routes every non-recorder to
`held-by-other` / `owner-recovery` with `isModalOpen === false` (proven in `useActualWorkCapture.
test.ts`), and `RequestDetailContent` mounts `ActualWorkComposer` only when `isModalOpen &&
state.status === "draft"` — the composer and its nudge chips never mount outside the recorder's
editable Draft.

Verified 2026-08-27: `ActualWorkComposer.test.tsx` 26/26 (catalog + assembly triggers, tap-to-add,
dismiss, 409-reconcile), focused ActualWork frontend suite 60/60, full app suite 780/780, `tsc`
clean, `check:tokens` passed.

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

### Next after the release gate — Minimum Office Closeout

**Batch 0 (mechanical preflight) — COMPLETE (2026-08-27, `80f8065`).**
[`docs/build-log/135-minimum-office-closeout-mechanical-preflight.md`](build-log/135-minimum-office-closeout-mechanical-preflight.md)
(rev. 4, eleven review corrections over three rounds). It maps the ADR-493 / BL129 sequence into
gated slices — target files, DTOs, endpoints, error mapping, permission/entitlement gates,
transaction/concurrency boundaries, DB constraints, and focused tests, with per-batch file/family
counts against the batch gate. It carries the four required proofs (visit-level zero-line NoCharge
disposition; effective financial-resolution supersession; one unreleased Billing Revision membership
per visit; one Draft/ReadyForBilling revision per request) plus the immutable revision snapshot
model. Locked there: `AccountingManage` at Admin tier (Owner inherits; existing review/financial
reads retrofitted); `NoCharge` disposition zero-line-only; single-shot handoff. Sequence renumbered
in BL135 §4: Batch 3a → 3a-i / 3a-ii, Batch 3b → 3b-i / 3b-ii; Batches 2 and 6 defer DI.

**Batch 1 (financial-resolution + zero-line-disposition domain foundation) — COMPLETE
(2026-08-27, `56b8b7f`).** Core only, 0 mutation families, 5 prod / 2 test files. Immutable
append-only `ActualWorkLineFinancialResolution` and visit-level
`ActualWorkOfficeFinancialDisposition` records, `FinancialResolutionBasis` /
`OfficeFinancialDispositionKind` enums, `ActualWorkFinancialResolutionErrors`. Both `Create`
methods: `ArgumentException` on empty required GUIDs; trimmed reason with a 2,000-char cap;
required-value / non-negative / defined-enum validation; `CreatedByUserId` set to the
resolving/disposing actor for retained audit authorship. Snapshot/review-state rules deferred to
Batches 3a-ii / 3b-i. Verified: 26 focused unit tests, full unit suite 1652/1652, architecture
14/14, `git diff --check` clean.

**Batch 2 (financial-resolution / disposition persistence, no DI) — COMPLETE (2026-08-28,
`946481a`).** Infrastructure + Foundation migration (`20260828094154_AddActualWorkFinancialResolution`)
+ Application persistence seam; 0 mutation families; 5 prod / 1 test + generated migration/snapshot.
`ActualWorkLineFinancialResolutionConfiguration` (value-present / non-negative / reason-present
checks; composite FK to `keep_actual_works` + three-column FK to
`keep_actual_work_lines(account_id, actual_work_id, id)` per drift D2; no unique index; effective-read
index), `ActualWorkOfficeFinancialDispositionConfiguration` (reason-present check; visit FK;
constraint name shortened under the 63-char PG limit), `HasAlternateKey(AccountId, ActualWorkId, Id)`
added to `ActualWorkLineConfiguration`. `IActualWorkFinancialResolutionPersistence` +
`EfActualWorkFinancialResolutionPersistence`: account-scoped newest-first untracked reads +
`AddResolutionAsync` / `AddDispositionAsync` staging seam (no `SaveChangesAsync`, no transaction).
DI deferred to Batch 3a-ii. Integration tests (11/11) prove every check constraint via raw INSERT,
the three-column FK cross-visit rejection, component-by-component effective resolution, and the
append boundary with fresh contexts before/after `SaveChangesAsync`. Verified: `~ActualWork`
integration 168/168, `~ActualWork` unit 81/81, architecture 14/14, `git diff --check` clean.
Non-blocking follow-ups noted in review: no explicit cross-account read-isolation assertion; ordering
tests use distinct timestamps so the `Id DESC` tie-breaker is unexercised (code implements both).

**Batch 3a-i (`AccountingManage` permission seam, authorization only) — COMPLETE (2026-08-28).**
Foundation authorization + two existing Application auth copies; 0 families; 4 prod / 3 test
(the rev.-4 "2 test" header was stale — role-permission matrix plus both API auth matrices).
`PermissionKeys.Keep.AccountingManage` (`"keep.accounting.manage"`) added to `AdminBase` in
`RolePermissions` (Owner inherits; Operator/Viewer do not — locked §6.1). Both
`ActualWorkReviewApiService.AuthorizeAsync` and `ActualWorkFinancialReadApiService.AuthorizeAsync`
gained an `AccountingManage` `IsPermitted` check after the `RequestsOperate` check, alongside the
retained explicit Owner/Admin role check (defense-in-depth). Behaviour unchanged for Owner/Admin.
Tests: `UserAccessPolicyTests` matrix rows (Admin/Owner hold it, Operator/Viewer do not) +
`Review_Viewer_Returns403` / `FinancialDetail_Viewer_Returns403` with a new `SeedViewerAsync`
helper in each API test class. Verified: full unit suite 1656/1656 (+4), `~ActualWorkReview` /
`~ActualWorkFinancialRead` integration 28/28, architecture 14/14, `git diff --check` clean.

**Batch 3a-ii (financial-resolution mutation API — mutation only) — COMPLETE (2026-08-28).**
Split approved by Christian: the read-projection fold moved to a new Batch 3a-iii so the mutation
plus its required domain seam stays within the file gate. Application + API + one Core seam;
1 family (create financial resolution); 8 prod / 2 test (the "3 test" label was stale — the
stale-version review proof lives inside the new API test class, not a separate file).

New `ActualWorkFinancialResolutionApiService` (auth copied from the `AccountingManage`-gated
`ActualWorkFinancialReadApiService.AuthorizeAsync`; parses `Basis`, runs domain value validation via
`ActualWorkLineFinancialResolution.Create`, maps the outcome). `IActualWorkFinancialResolutionPersistence`
gains `CreateResolutionAsync` + `ActualWorkResolutionResult` / `ActualWorkResolutionOutcome` /
`ActualWorkFinancialResolutionCommand`; `EfActualWorkFinancialResolutionPersistence` implements the
transactional orchestrator — `BeginTransaction` → tracked visit load with `Lines` → guards in fixed
order (not found → `ConcurrencyVersion` mismatch → `Status != Submitted` → `ReviewedAtUtc != null`
[D5] → line not on visit → targeted component snapshot already non-null) → stage via `AddResolutionAsync`
→ `visit.RefreshConcurrencyVersionForFinancialResolution()` → `SaveChanges` (catch
`DbUpdateConcurrencyException` → `VersionMismatch`) → commit; returns the rotated visit version.

`ActualWork.RefreshConcurrencyVersionForFinancialResolution()` is a **public** domain method (not
internal — the EF orchestrator is in a different assembly with no `InternalsVisibleTo`), documented
as existing solely to invalidate a stale financial-review command after an immutable resolution
append. `ActualWorkFinancialResolutionErrors` gains `FinancialResolutionLineNotFound` (404),
`…SnapshotComponentAlreadyValid` (409), `…VisitAlreadyReviewed` (409); `ErrorHttpMapper` maps those
plus the existing value/basis/reason codes (400). New endpoint
`POST /keep/pricebook/actual-work/{actualWorkId:guid}/lines/{lineId:guid}/financial-resolution`
(`X-Keep-ActualWork-Version` header, `ActualWorkConcurrencyVersionResponse`). DI registration for
`IActualWorkFinancialResolutionPersistence` (deferred from Batch 2) + the new service lands here.

Tests: `ActualWorkFinancialResolutionApiTests` (new, 19 — auth matrix, every guard, missing-header,
domain validation, chained-append ordering, and the token-rotation proof: resolve returns a new
version → review with the stale version is 409 `ActualWork.VersionMismatch` → review with the
returned version succeeds); +2 `ActualWorkTests` unit cases for the domain method. Verified: full
unit suite 1658/1658 (+2), `~ActualWork` integration 189/189, architecture 14/14,
`git diff --check` clean.

**Batch 3a-iii (financial-resolution read-projection fold — read only) — COMPLETE (2026-08-28).**
Application projection + API mappers; 0 families; 2 prod / 2 test (4 changed files, no new files, no
DI change — resolution persistence + read service were already registered in 3a-ii).
`ActualWorkFinancialReadApiService` gains an `IActualWorkFinancialResolutionPersistence` ctor
dependency; `GetFinancialDetailAsync` loads `GetResolutionsForVisitAsync` and folds it in. New
`ActualWorkFinancialProjection.ProjectVisit(lines, resolutions)` is the one read entry point:
it orders the resolution rows once (`ResolvedAtUtc DESC, Id DESC`), folds each line once into an
effective per-component struct (each component's value is its snapshot, or — only if the snapshot
is missing — the most-recent supplying row; sell price and direct cost resolved independently, each
with its own provenance), and returns totals + line DTOs + blockers all derived from that same
per-line struct. `ToDetailResult` and `ToQueueEntry` each call it exactly once. New
`RoundMoney` = `decimal.Round(v, 2, MidpointRounding.AwayFromZero)` (ADR-467 round-half-up;
inputs/quantities are non-negative in this domain); each line total rounded independently, visit
totals = sum of already-rounded line totals, margin = rounded-sales − rounded-cost (all three
reconcile). DTO additions: `ActualWorkFinancialLineEntry` per-component `…Resolved` bool + resolved
value + basis string; `ActualWorkFinancialDetailResult.Blockers` (new
`ActualWorkFinancialBlocker` record — line components only, not disposition). `KeepEndpoints`
`ToActualWorkFinancialDetailResponse` / `ToFinancialLineResponse` extended.

*Known follow-up (not a bug):* the review-queue source seam carries no resolution rows, so queue-row
`hasIncompleteFinancialData` / `incompleteLineCount` / totals stay **snapshot-only** — a visit whose
blockers have since been resolved still reads pessimistically incomplete in the queue until Batch
3b-ii's transactional review gate. Safe direction (never reports "ready" when it is not); the
authoritative readiness check is 3b-ii, not the queue. Making the queue resolution-aware needs an
`IActualWorkFinancialReviewPersistence` seam change — out of this batch's gate.

Verified: full unit suite 1663/1663 (+5), `~ActualWork` integration 191/191 (+2),
`~ActualWorkFinancialRead`/`~ActualWorkFinancialResolution` 51/51, architecture 14/14,
`git diff --check` clean.

**Batch 3b-i (zero-line no-charge disposition API + persistence) — COMPLETE (2026-08-28).**
Application + API + one domain method; 1 family; 8 prod / 1 test (9 changed). New
`ActualWorkOfficeFinancialDispositionApiService` (dedicated class, `AccountingManage`-gated
composition identical to the resolution service; parses `Kind` trimmed/case-insensitive →
`DispositionInvalidKind`; domain factory owns reason validation).
`IActualWorkFinancialResolutionPersistence` gains `RecordDispositionAsync` + `ActualWorkDispositionResult`
(`Committed, VisitNotFound, VersionMismatch, VisitNotSubmitted, VisitAlreadyReviewed, VisitHasLines`).
EF orchestrator: one transaction, guard order **not-found → version → not-submitted → already-reviewed
(D5) → `Lines.Count > 0` → `VisitHasLines`** (version ahead of every business guard; already-reviewed
ahead of has-lines), stage row → `RefreshConcurrencyVersionForOfficeFinancialDisposition()` (new,
parallel to the resolution token method) → save (catch `DbUpdateConcurrencyException` →
`VersionMismatch`, pre-commit return, no persisted row) → commit. Dispositions are append-only; the
effective one is the most-recent — repeats on a still-eligible visit are permitted by design.
Errors `DispositionVisitHasLines` / `DispositionVisitAlreadyReviewed` → 409; `Disposition*` reason/kind
→ 400. `POST /keep/pricebook/actual-work/{actualWorkId:guid}/financial-disposition`, body
`(Kind, Reason)`, `X-Keep-ActualWork-Version`. Verified: `~ActualWorkDisposition` API 21/21,
`~ActualWorkFinancialResolution`/`~ActualWorkReview` API + `~ActualWorkFinancialResolutionPersistence`
green, `~ActualWork` unit 88/88, architecture 14/14, `git diff --check` clean.

**Batch 3b-ii (hard `MarkReviewed` gate + review transaction/read integration) — COMPLETE
(2026-08-28, `19a3918`).** Domain + Application + Infrastructure; 1 family (`MarkReviewed`); 6 prod /
6 test (12 changed, no new files, no DI-registration change). `ActualWork.MarkReviewed` gains
`bool financialDataComplete, bool zeroLineDispositionSatisfied` (stays pure — the orchestration
supplies them). New guards run **after** the existing `NotSubmitted` / `AlreadyReviewed` /
note-length guards and before the state write, so every previously-valid API failure mode is
unchanged: `!financialDataComplete` → `ReviewBlockedIncompleteFinancials`;
`_lines.Count == 0 && !zeroLineDispositionSatisfied` → `ReviewBlockedZeroLineDispositionRequired`.
`ActualWorkReviewResult` gains `BlockedIncompleteFinancials` / `BlockedZeroLineDisposition`; both new
errors → 409. `EfActualWorkReviewPersistence` injects `IActualWorkFinancialResolutionPersistence`
(already registered) and, inside the existing Read-Committed transaction after the version check,
loads `.Include(x => x.Lines)` + account-scoped resolutions + dispositions and computes the two
booleans via a private `AllLinesFinanciallyComplete` — a deliberate one-way restatement of the
read-side `ActualWorkFinancialProjection` completeness rule (Infrastructure must not consume
Application internals; the projection is the other site to keep in step). `zeroLineDispositionSatisfied`
= any `NoCharge` disposition, not a row count. The visit concurrency token stays the race guard — a
resolution/disposition appended after the gate reads loses the token race on save. Blocked outcomes
return before the signal-resolve SQL; only `Committed` advances review state or resolves the work
signal. **No revision-membership check (D4).** Verified: full unit 1666/1666 (+3), `~ActualWork`
integration 219/219 (blocked path asserts reviewer/timestamp null + signal unresolved;
mixed snapshot+resolution provenance), architecture 14/14, `git diff --check` clean.

**Batch 4a (`hasNoChargeDisposition` on the financial-detail read) — COMPLETE (2026-08-28).**
Backend read prerequisite split out of Batch 4 so the UI stays within the file gate. Application +
API; 0 families; 2 prod / 1 test. `ActualWorkFinancialDetailResult` gains `bool HasNoChargeDisposition`;
`GetFinancialDetailAsync` calls the already-injected `GetDispositionsForVisitAsync` and sets it to
`dispositions.Any(d => d.Kind == NoCharge)` (always false for a visit with lines — disposition is
zero-line-only). `KeepEndpoints` `ToActualWorkFinancialDetailResponse` serializes
`hasNoChargeDisposition`. Gives the Batch 4b zero-line review card a truthful post-reload
"disposition recorded" state instead of inferring it from a successful mutation; the hard review
gate stays the race backstop. Tests: 3 cases in `ActualWorkFinancialReadApiTests` (zero-line no
disposition → false; after a recorded `NoCharge` → true; visit with lines → false). Verified: read
API integration 24/24, `~ActualWorkReviewApi`/`~ActualWorkDispositionApi`/`~ActualWorkFinancialResolutionApi`
51/51, `~ActualWork` unit 91/91, full unit 1666/1666, architecture 14/14, `git diff --check` clean.

**Batch 4b (Office financial-resolution UI) — COMPLETE (2026-08-28).** `web/ophalo-app` only;
0 families; 7 prod / 4 test = 11 files. `useActualWorkFinancialReview.ts` (the existing hook — no
second hook) now exposes `review`, `resolveLine`, `recordNoChargeDisposition`, all returning one
`FinancialReviewOutcome` family: `success | validation-failure{code} | reconciled{code} |
review-blocked-incomplete | review-blocked-zero-line | hidden`. The two 409 hard-gate codes
(`ReviewBlockedIncompleteFinancials` / `ReviewBlockedZeroLineDispositionRequired`) map to their own
variants and still reload; other 409/404 → `reconciled` + authoritative reload; 400 → `validation-failure`
with the stable code; 403 → `hidden`. Mutations are serialized per visit via a promise chain **and**
the hook exposes `mutatingVisitIds` / `isVisitMutating(id)`, threaded through `RequestDetailContent.tsx`
to `ActualWorkReviewCard` so the review button and both inline forms disable for a visit for the full
duration of any mutation and its reload. `apiClient` gains `createActualWorkFinancialResolution` +
`recordActualWorkFinancialDisposition` (both send `X-Keep-ActualWork-Version`); `apiClient.types.ts`
gains `hasNoChargeDisposition`, `blockers[]`, the six resolved-`*` line fields, and the two request
body types. New `FinancialResolutionForm.tsx` — inline `<details>`, offers only the component(s) the
blocker names as missing, **allows resolving one or both** (untouched component sent as `null`),
client-side non-negative validation (`type=number min=0 step=0.01` + explicit guard), draft
preserved and first errored field focused on a 400. New `NoChargeDispositionForm.tsx` renders only
for an unreviewed zero-line visit with `hasNoChargeDisposition === false`; a reviewed or
already-dispositioned visit shows read-only state only. No drawer/modal; existing Work Canvas
language (`rounded-xl` card, native `<details>`, `KeepButton`, tokens, inline alerts). Review
correction applied pre-commit: partial-component resolution + client negative check. Verified:
full frontend suite **799/799** (90 files, +4), `tsc --noEmit` clean, `check:tokens` passed,
`git diff --check` clean.

### Claude handoff — 4c-i Session 2 (`4c-i-a-1`) COMPLETE; next is Christian's `4c-i-mig`, then Session 3 (`4c-i-a-2`); see session plan below

**BL136 P (workflow/mechanical preflight) is complete.** ADR-494 (D1–D12) committed at `2118293`;
the ADR-487 wording fix + 4c-i seam preflight rev. 3 committed at `644aa4a`; the rev. 4 docs
(ADR-494, BL136, BL136 P preflight, session-log) committed at `52e490d`.

**Session 1 (seam prep) — COMPLETE (2026-08-29).** `4c-i-r` `ea4f9b8` (developer-only reset tool +
runbook note, checked in inert — **not executed**), `4c-i-0a` `79008bd` (unit-test
`ActualWorkTestData` seam; `ActualWork`-filtered unit 91/91), `4c-i-0b` `03a081f` (integration
`ActualWorkTestData` seam under `Support/`; 18 `AddLine` sites across 9 files; `ActualWork`-filtered
integration 222/222, 0 warnings). No production code. The reset tool is run **only later, by
Christian, immediately before `4c-i-mig`**, to validate the strict migration on a local DB — never
during seam prep, never from app migration/startup/deploy (ADR-494 D12).

**Session 2 (`4c-i-a-1`) — COMPLETE (2026-08-29).** Domain + persistence + EF config, 11 files
(7 prod + 4 test): `ActualWorkLine.PerformedByAccountUserId` (non-null, `PerformerRequired` on empty
guid); `ActualWork.DefaultPerformedByAccountUserId` (nullable) + optional default arg on `Create` +
`AddLine` optional explicit performer that seeds from the ticket default and returns
`PerformerRequired` when both absent (**no creator/recorder fallback**) + Draft-only
`SetDefaultPerformer(Guid?)` (recorder authorization stays an API-layer gate for `4c-i-b`, matching
`TransferRecorder`); `ActualWorkErrors.PerformerRequired`; EF config (line column `IsRequired`, no
FK; nullable default column + `(AccountId, DefaultPerformedByAccountUserId)` index); both existing
`AddLine` prod call sites thread `actualWork.DefaultPerformedByAccountUserId` (compile-level — the
assembly no-default outcome is `4c-i-a-2`, failures still collapse to `NotDraft`); both
`ActualWorkTestData` helpers gain the default/performer args (helper `AddLine` resolves an omitted
performer to `createdByUserId` — fixture setup only; `PerformerRequired` tests call the domain
directly with explicit `null`). Verified: full unit **1679/1679** (`ActualWork`-filtered 104),
architecture **14/14**, full solution build 0 warnings, `git diff --check` clean. The persistence
round-trip test in `ActualWorkPersistenceTests` is added and compiles but **executes only after
`4c-i-mig`** — a-1's new EF columns are not in migration history yet, so the integration suite is
red until Christian applies the migration.

**Next — Christian's `4c-i-mig`.** Run the local Actual Work reset (`4c-i-r`, still un-executed)
first, then `dotnet ef migrations add AddActualWorkPerformer --startup-project
src/OpHalo.Keep.Infrastructure` (ADR-049), apply locally, and run the focused integration suite
including `ActualWorkPersistenceTests`.

**rev. 4 changes (committed `52e490d`), from the advisor review:**
- **Assembly expansion is a third line-creation route.** `EfActualWorkAssemblyExpansionPersistence`
  collapses every `AddLine` failure to `NotDraft`; `PerformerRequired` becomes newly reachable.
  Locked: expansion uses the persisted ticket default; no default → explicit `PerformerRequired`
  outcome (never `NotDraft`), **no partial writes**; genuine non-`Draft` → still `NotDraft`. New
  commit **`4c-i-a-2`** (3 prod + 1 test); `4c-i-a` renamed `4c-i-a-1`. `4c-i-c` also gates
  assembly + nudge, not just the line editor.
- **Inventory re-derived across all three routes: 13 test files.** Two HTTP-only files
  (`ActualWorkDraftApiTests`, `ActualWorkNudgeFieldReadApiTests`) + `ActualWorkFinancialResolutionApiTests`
  break at `4c-i-b` (their per-file `CreateDraftAsync` must send a default); `ActualWorkAssemblyExpansionPersistenceTests`
  moves to `4c-i-a-2`. `4c-i-b` is now 6 prod + 6 test = 12.
- **`CompletionNote` ≤2000/trim guard removed from 4c-i entirely** — separate note-validation
  behaviour, own bounded slice/preflight (ADR-494 D3 keeps the intent only).
- version-header/migration wording from the prior round is retained (`X-Keep-ActualWork-Version` +
  `ParseActualWorkVersion` + `ActualWorkConcurrencyVersionResponse`; migration deploys through the
  normal production path, prod has zero rows).

**4c-i is one deployable slice = eight commits**, none deployed until all merge; every commit ≤ 12
total / ≤ 8 production / ≤ 1 mutation family — proven per-commit counts in
[BL136 P preflight → Slice 4c-i](build-log/136-P-preflight.md).

**Session plan (one Claude session per commit unless noted; fresh session after each approved commit):**

| # | Session | Produces | Depends on | Notes |
|---|---|---|---|---|
| — | *done (`52e490d`)* | rev. 4 docs committed | approval | no code |
| 1 | *done* | `4c-i-r` `ea4f9b8` + `4c-i-0a` `79008bd` + `4c-i-0b` `03a081f` | — | seam prep, no behaviour change |
| 2 | *done* | `4c-i-a-1` — domain + persistence + EF config (7 prod + 4 test) | 1 | committed; integration round-trip runs after `4c-i-mig` |
| — | *Christian — next* | `4c-i-mig` — author + apply `AddActualWorkPerformer` (`--startup-project src/OpHalo.Keep.Infrastructure`) | 2 | not a Claude session; reset local DB via `4c-i-r` first |
| 3 | `4c-i-a-2` | assembly-expansion outcome contract (3 prod + 1 test) | 2 (not the migration) | |
| 4 | `4c-i-b` | performer-candidate read + `SetDefaultPerformer` + create/add-line performer + HTTP test fixes (6 prod + 6 test) | 2, `4c-i-mig` | at the file gate; split to `b-1`/`b-2` if needed |
| 5 | `4c-i-c` | minimum functional frontend (6 prod + 3 test) | 4 | last commit of the slice; deploy the whole slice after this |

After `4c-i` deploys: `4c-ii` (VisitNote API), `4c-iii` (rich UI), `4d`, `4e-0/i/ii/iii`, `4f-i/ii`,
`4g` — each its own session(s), per the BL136 per-slice split. Then BL135 Batch 5 (Billing Revision)
resumes.

**Still locked / preserved:** submitted facts and financial resolution/disposition evidence remain
immutable and append-only; this sequence introduces no reopen or delete authority. Superseding a
visit sets marker columns only — `Status` never changes.

**Sequencing — Billing Revision remains paused.** BL135 Batch 5 resumes only after BL136 4c–4g
land. Batch 4 (office financial-resolution) is fully landed end-to-end (domain 1 → persistence 2 →
API 3a/3b → UI 4a/4b).

**Deferred UI follow-up — Request Workbench primary-tab selection (2026-08-28).** In the wide
two-pane Request Workbench, selecting a primary queue tab such as **Mine** currently preserves an
already-open detail even when that request is outside the newly selected queue. This follows the
existing no-reselection implementation, but is not the intended business behavior: explicitly
choosing a primary queue is a work-context switch. Address after the Actual Work release slices in
a separately preflighted UI change: once the selected tab's settled ranked queue loads, select its
first visible/API-ranked request; if empty, show that queue's empty/preview state rather than an
unrelated detail. Search and secondary-filter changes may preserve the open detail while it remains
in the result set, clearing it only once it no longer matches. Test Mine selection, empty queues,
and the distinction between primary-tab switches and secondary filters. Do not silently promote an
attention request ahead of the first ranked row.

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

**Locked implementation order:** mechanical preflight (no code) → financial resolution plus
zero-line disposition domain → persistence/migration → financial-resolution API/read → hard review
gate/disposition API → existing review-card UI → Billing Revision domain → persistence → Draft
assembly/detail read → Ready/Void → Handed Off → Billing Revision summary UI → correction/
adjustment preflight. Financial controls belong on `ActualWorkReviewCard`, never the price-blind
`ActualWorkComposer`; expandable queue review is separate scope. The preflight must prove an
effective-resolution/supersession rule, one unreleased revision membership per visit, and one
Draft/Ready revision per request.

CSV generation, QuickBooks/API integration, invoice creation, payments, tax, inventory,
reconciliation, and an Accountant role/UI remain deferred. Future export serializes the immutable
Billing Revision; it must not rebuild financial facts from live visits.

**Deferred follow-up — office-financial role model (do not silently implement).**
`PermissionKeys.Keep.AccountingManage` is now the shared office-financial permission seam, but the
current closeout surfaces intentionally retain their explicit Owner/Admin role gate. Before a
narrower accounting or Accountant role can use that seam, run a dedicated authorization/product
discovery: define the role's membership and invitation model, exact read/mutation authority across
review, resolution, disposition, Billing Revisions and export, field-price-blindness boundaries,
UI/navigation exposure, audit requirements, and migration/compatibility plan. Until that decision
is approved, AccountingManage remains Admin-tier (with Owner inheritance) and does not by itself
admit a new role. Source: BL135 §6.1 / ADR-493.

## Guardrails

- The responsive staff PWA (`web/ophalo-app`) is the active field surface; native parity is not implied.
- Do not infer authority for quotes, pricing, invoicing, payments, QuickBooks, inventory, or fleet from Request Detail work.
- Price Book requires its capability package. Use disposable local data for mutable acceptance; never seed founder production data.
- Before a production candidate, complete repository checks and the controlled production smoke test: health/readiness, release identity, error capture, alert routing, and telemetry redaction.
- Preflight current code and the controlling ADR/build log/tracker. Make one reviewable change set at a time; stop for product direction when server data or authorization cannot truthfully support a UI.
