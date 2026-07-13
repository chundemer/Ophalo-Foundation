# Build Log 077 — Pre-Deployment Cleanup And File Decomposition

**Started:** 2026-07-10
**Status:** Active Session 26 brief — cleanup before Resend/customer tracker-link email
**Session name:** S26 pre-deployment cleanup / file decomposition / maintainability pass
**Depends on:** Completion of Session 25 Share Request Link Drawer

---

## Purpose

This log captures the focused cleanup session before wiring Resend/customer tracker-link email.
The goal is to reduce deployment risk by identifying oversized files, dense UI surfaces, and
domain/application classes that would benefit from extraction before pilot deployment.

This is not a feature session. Treat it as a pre-deployment maintainability pass over stable
behavior. If cleanup reveals a bug or product gap, record it explicitly before fixing it.

---

## Trigger

This session is now unblocked because:

1. Customer request page work is complete.
2. Intake urgency/contact preference display decisions are implemented or explicitly deferred.
3. Session 25 Share Request Link Drawer is complete.
4. Latest green baseline: S25d — 1042 unit tests passed, 14 architecture tests passed.
5. No active feature slice depends on keeping the current large files untouched.

---

## Initial Findings

Line-count scan refreshed on 2026-07-12 after S25 found the following high-value decomposition
candidates, excluding `node_modules`, EF migrations, build outputs, and lock files:

| File | Lines | Initial read |
|---|---:|---|
| `web/ophalo-app/src/pages/RequestDetail.tsx` | 3,089 | Highest-risk frontend split candidate after S24/S25. Contains page shell, timeline, attention guidance, work controls, share/link flows, modals, status/contact/feedback/business-update/team sections, and formatting helpers. |
| `mobile/ophalo-mobile/app/requests/[id].tsx` | 1,588 | Strong mobile split candidate. Contains orchestration, timing controls, timeline, date picker, helpers, and styles. |
| `src/OpHalo.Keep.Core/Entities/KeepRequest.cs` | 1,341 | Large domain entity. Review carefully before extracting behavior into policies/services; do not start here. |
| `src/OpHalo.Keep.Application/Requests/GetKeepRequestListService.cs` | 1,320 | Application service may contain separable query validation, filtering, mapping, and cursor behavior; semantic preflight required. |
| `web/ophalo-app/src/mocks/fixtures.ts` | 1,125 | Large mock fixture file; split only when it slows feature work. |
| `src/OpHalo.Api/Program.cs` | 1,092 | Composition root now owns too many Keep registrations/routes/handlers. Good first cleanup target before Resend touches API/public-intake setup. |
| `web/ophalo-app/src/components/QuickCapture.tsx` | 811 | Clear internal component boundaries already exist; good low-risk extraction target. |
| `web/ophalo-app/src/lib/apiClient.ts` | 800 | Consider splitting by feature area once request-detail churn settles. |
| `web/ophalo-web/src/app/keep/r/[pageToken]/CustomerTrackerView.tsx` | 678 | Candidate for status hero, composer, timeline, and utility extraction. |
| `web/ophalo-web/src/app/keep/intake/[token]/IntakeForm.tsx` | 622 | Fine for now; revisit after Resend/customer link retention work. |
| `web/ophalo-app/src/components/ShareLinkModal.tsx` | 509 | New S25 component. Leave intact unless follow-up share UX changes require it. |

Large test files also exist, but split tests by behavior area only when the current size slows review
or makes targeted changes risky.

---

## Recommended Investigation Order

1. **API composition root (`Program.cs`)**
   - Extract Keep service registrations into an extension method.
   - Extract Keep endpoint mappings into `OpHalo.Api/Keep/KeepEndpoints.cs` or equivalent.
   - Keep auth/account/device/badge endpoints in their existing files.
   - Preserve every route path, auth requirement, rate limit, and response shape.

2. **Request detail web page**
   - Extract stable presentational sections first.
   - Avoid behavioral rewrites during extraction.
   - Preserve existing component names where possible to keep diffs reviewable.

3. **Quick Capture**
   - Move already-defined internal components into a small folder.
   - Keep the public `QuickCapture` API stable.

4. **API client / mock fixture cleanup**
   - Split only by clear feature seams.
   - Avoid churn if request-detail extraction already creates enough review surface.

5. **Mobile request detail**
   - Extract timeline, date picker, timing controls, and formatters.
   - Keep route file focused on screen orchestration.

6. **Core/application backend hotspots**
   - Review `KeepRequest.cs` and `GetKeepRequestListService.cs` after UI cleanup.
   - Prefer domain-policy or mapper extraction only where responsibilities are already distinct.

---

## Completed Slices

### S26a — API composition cleanup (complete)

Files created:
- `src/OpHalo.Api/Keep/KeepServiceCollectionExtensions.cs` (80 lines) — `AddKeepServices()` extension extracting all Keep DI registrations including badge and push notifier.
- `src/OpHalo.Api/Keep/KeepEndpoints.cs` (782 lines) — `MapKeepEndpoints()` extension extracting all `/keep/...` routes; four handlers converted to private statics.
- `src/OpHalo.Api/Keep/RenameLinkNameBody.cs` — orphan record moved from Program.cs bottom.
- `src/OpHalo.Api/Keep/UpdateServiceLocationBody.cs` — orphan record moved from Program.cs bottom.

File modified:
- `src/OpHalo.Api/Program.cs` — 1,092 → 254 lines. All Keep DI and route code removed; replaced with `builder.Services.AddKeepServices()` and `app.MapKeepEndpoints()`.

Gap check results:
- SMS handoff resolve: SHA256 hash still inline in route lambda; 404 returned for invalid/expired with no payload. Behavior preserved.
- No raw token in any log or response path.
- Rate limiting policy names (`"public-intake"`, `"customer-write"`) are string literals in both policy definitions (Program.cs) and route registrations (KeepEndpoints.cs) — unchanged.
- No new direct-ID public paths introduced.
- All route paths, auth requirements, and response shapes preserved exactly.

Verification: build clean (0 warnings, 0 errors); 14/14 architecture tests passed; 826/828 integration tests passed (2 pre-existing ShareIntent failures confirmed on baseline, not caused by this slice).

Findings to address before S26 closeout:
- `IClock clock` remains unused in the SMS handoff creation endpoint. This was pre-existing and not
  introduced by S26a; remove it or document why it should stay.
- Two pre-existing ShareIntent integration failures were confirmed on baseline before S26a:
  `ShareIntent_NeedsShare_false_after_successful_clear` and
  `ShareIntent_idempotent_second_call_returns_204_without_error`. Both return `BadRequest` instead
  of `NoContent`; handle as a separate behavior gap, not an API-composition regression.

### S26 Closeout (complete)

**IClock removal:** Removed unused `IClock clock` parameter from the SMS handoff creation
endpoint (`MapPost /keep/requests/{requestId:guid}/sms-handoff`) in `KeepEndpoints.cs`. The
resolve endpoint (`MapGet /keep/share-sms/{handoffToken}`) correctly retains `IClock` for
`clock.UtcNow` expiry checks.

**ShareIntent method gap fixed:** Added `native_share` and `manual_mark_shared` to
`ClearShareIntentService.ValidMethods`. These are the two mobile-app share methods that existed
in `useClearShareIntent.ts` but were absent from server-side validation, causing two integration
tests to return `BadRequest` instead of `NoContent`. Updated error message and added
`[InlineData]` cases to `ClearShareIntentServiceTests`.

Verification: 1,044/1,044 unit tests passed (was 1,042 — two new `[InlineData]` cases);
14/14 architecture tests passed; 10/10 ShareIntent integration tests passed (was 8/10).

---

## S26 Slice Plan

### S26a — API composition cleanup

Files likely touched:

- `src/OpHalo.Api/Program.cs`
- `src/OpHalo.Api/Keep/KeepEndpoints.cs`
- `src/OpHalo.Api/Keep/KeepServiceCollectionExtensions.cs`

Checks:

- route list behavior unchanged;
- auth/account/device/badge endpoint mappings still compile;
- public-token routes still avoid raw-token logging;
- SMS handoff resolve remains invalid/expired no-payload;
- no new direct-ID public paths.

### S26b — Request detail mechanical split

Candidate folder:

```text
web/ophalo-app/src/pages/request-detail/
```

Potential extraction targets:

- `RequestDetailHeader.tsx`
- `OriginalRequestCard.tsx`
- `ActivityTimeline.tsx`
- `AttentionGuidanceCard.tsx`
- `CustomerPanel.tsx`
- `ServiceLocationPanel.tsx`
- `TimingPanel.tsx`
- `InternalNoteCard.tsx`
- `requestDetailUtils.ts`

Checks:

- no behavior change;
- no prop/API contract churn beyond local imports;
- no customer-visible/internal-only copy regression;
- no accidental delivery language such as **Sent** for external share/contact handoffs.

### S26c — Low-risk PWA component/client cleanup

Potential targets:

- `web/ophalo-app/src/components/QuickCapture.tsx`
- `web/ophalo-app/src/lib/apiClient.ts`
- `web/ophalo-app/src/mocks/fixtures.ts`

Checks:

- QuickCapture public component API stays stable;
- customer-facing links use configured public/app base URLs;
- mock fixture split does not mask real API contract drift.

### S26d — CustomerTrackerView split (complete)

Re-scan after S26a–S26c identified `CustomerTrackerView.tsx` (678 lines, ophalo-web public app)
as the highest-priority split. Build 078 (customer tracker link email / Resend) will add
email-link behavior to this surface, so reducing local complexity before that work was the
lowest-risk sequencing choice.

Files created:
- `web/ophalo-web/src/app/keep/r/[pageToken]/tracker-types.ts` (158 lines) — types (`CustomerEventItem`, `CustomerPageData`, `ComposerPhase`), all constants, and all pure helpers.
- `web/ophalo-web/src/app/keep/r/[pageToken]/TrackerExpiredView.tsx` (29 lines) — expired-link full-page state.
- `web/ophalo-web/src/app/keep/r/[pageToken]/TrackerStatusCard.tsx` (61 lines) — §2 status card with share/copy button.
- `web/ophalo-web/src/app/keep/r/[pageToken]/TrackerActionCard.tsx` (220 lines) — §3 action card: idle picker, composer, feedback form, sent/feedback_sent confirmations.
- `web/ophalo-web/src/app/keep/r/[pageToken]/TrackerInitialRequestCard.tsx` (40 lines) — §4 initial request and urgency display.
- `web/ophalo-web/src/app/keep/r/[pageToken]/TrackerHistoryCard.tsx` (72 lines) — §5 request history event list.

File modified:
- `web/ophalo-web/src/app/keep/r/[pageToken]/CustomerTrackerView.tsx` — 678 → 268 lines. State, effects, and all submit handlers remain in orchestrator. Sub-components receive props and callbacks only; no fetch/mutation logic moved into children.

`page.tsx` required no changes; `CustomerPageData` is re-exported from the orchestrator.

Verification: `tsc --noEmit` clean (0 errors).

Deferred items:
- `mobile/ophalo-mobile/app/requests/[id].tsx` (1,588 lines) — not in the Resend path; needs a separate semantic preflight before splitting.
- `web/ophalo-app/src/pages/RequestDetail.tsx` (1,510 lines) — orchestrator after S26b extract; defer until the current refactor batch stabilizes.
- `web/ophalo-app/src/components/ShareLinkModal.tsx` (509 lines), `Requests.tsx` (503 lines), `settings/TeamSection.tsx` (592 lines), `web/ophalo-web/…/IntakeForm.tsx` (622 lines) — defer unless Build 078 directly touches them.
- `KeepRequest.cs` and `GetKeepRequestListService.cs` — deferred; require separate semantic preflight.

---

## Candidate Target Shapes

### Web Request Detail

Potential folder:

```text
web/ophalo-app/src/pages/request-detail/
```

Potential files:

- `RequestDetailHeader.tsx`
- `DetailHero.tsx`
- `OriginalRequestCard.tsx`
- `Timeline.tsx`
- `AttentionGuidanceCard.tsx`
- `WorkControlsGroup.tsx`
- `LogContactModal.tsx`
- `BusinessUpdateSection.tsx`
- `requestDetailUtils.ts`

### API Program / Keep Endpoints

Potential files:

- `src/OpHalo.Api/Keep/KeepEndpoints.cs`
- `src/OpHalo.Api/Keep/KeepServiceCollectionExtensions.cs`
- optional later split: `KeepPublicEndpoints.cs`, `KeepRequestEndpoints.cs`, `KeepSetupEndpoints.cs`

### Mobile Request Detail

Potential folder:

```text
mobile/ophalo-mobile/app/requests/_detail/
```

Potential files:

- `RequestDetailContent.tsx`
- `RequestTimingControls.tsx`
- `EventTimeline.tsx`
- `DateSheetPicker.tsx`
- `requestDetailFormatters.ts`
- `requestDetailStyles.ts`

### Quick Capture

Potential folder:

```text
web/ophalo-app/src/components/quick-capture/
```

Potential files:

- `QuickCapture.tsx`
- `LookupGate.tsx`
- `LookupResultView.tsx`
- `ActiveRequestCard.tsx`
- `CaptureForm.tsx`
- `SuccessPanel.tsx`
- `quickCaptureUtils.ts`

---

## Guardrails

- Do not mix feature changes with decomposition.
- Do not rename public API types or route paths unless required.
- Prefer mechanical extraction with minimal logic movement.
- Keep each extraction batch small enough to review.
- Run the same tests before and after each split.
- If a file is actively changing for a feature slice, defer decomposition until that slice lands.
- While moving code, explicitly check for token leakage, public direct-ID paths, auth drift,
  customer/internal visibility drift, hard-coded URLs, and share/contact language that implies
  delivery instead of manual preparation.

---

## Exit Criteria

This cleanup session is complete when:

1. The highest-risk oversized frontend file has been split or intentionally deferred with rationale.
2. Follow-up cleanup items are listed in this log or `docs/deferred-topics.md`.
3. Regression tests and relevant frontend checks pass.
4. The deployment candidate no longer depends on editing a 1,500+ line frontend file for routine
   request-detail polish.
