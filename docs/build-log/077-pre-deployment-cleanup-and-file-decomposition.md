# Build Log 077 — Pre-Deployment Cleanup And File Decomposition

**Started:** 2026-07-10
**Status:** Deferred investigation — run after customer request page work and testing
**Session name:** S23 pre-deployment cleanup / file decomposition / maintainability pass
**Depends on:** Completion of Session 22 customer request page, intake metadata display, and regression testing

---

## Purpose

This log parks a focused cleanup investigation for after the customer request page and related
testing are complete. The goal is to reduce deployment risk by identifying oversized files, dense UI
surfaces, and domain/application classes that would benefit from extraction before pilot deployment.

This is not a feature session. It should not interrupt the active customer request page work. Treat it
as a pre-deployment maintainability pass once the current behavior is stable.

---

## Trigger

Start this session only after:

1. Customer request page work is complete.
2. Intake urgency/contact preference display decisions are implemented or explicitly deferred.
3. Relevant web/API tests have passed.
4. No active feature slice depends on keeping the current large files untouched.

---

## Initial Findings

Line-count scan on 2026-07-10 found the following high-value decomposition candidates, excluding
`node_modules` and EF migrations:

| File | Lines | Initial read |
|---|---:|---|
| `web/ophalo-app/src/pages/RequestDetail.tsx` | 2,292 | Highest-priority frontend split candidate. Contains page shell, timeline, attention guidance, work controls, modals, status/contact/feedback/business-update/team sections, and formatting helpers. |
| `mobile/ophalo-mobile/app/requests/[id].tsx` | 1,435 | Strong mobile split candidate. Contains orchestration, timing controls, timeline, date picker, helpers, and styles. |
| `src/OpHalo.Keep.Core/Entities/KeepRequest.cs` | 1,263 | Large domain entity. Review carefully before extracting behavior into policies/services. |
| `src/OpHalo.Keep.Application/Requests/GetKeepRequestListService.cs` | 1,249 | Application service may contain separable query validation, filtering, mapping, and cursor behavior. |
| `web/ophalo-app/src/components/QuickCapture.tsx` | 811 | Clear internal component boundaries already exist; good low-risk extraction target. |
| `web/ophalo-app/src/lib/apiClient.ts` | 710 | Consider splitting by feature area once request-detail churn settles. |
| `web/ophalo-web/src/app/keep/r/[pageToken]/CustomerTrackerView.tsx` | 650 | Candidate for status hero, composer, timeline, and utility extraction. |
| `web/ophalo-web/src/app/keep/intake/[token]/IntakeForm.tsx` | 607 | Fine for now; revisit after urgency/contact-preference work lands. |

Large test files also exist, but split tests by behavior area only when the current size slows review
or makes targeted changes risky.

---

## Recommended Investigation Order

1. **Request detail web page**
   - Extract stable presentational sections first.
   - Avoid behavioral rewrites during extraction.
   - Preserve existing component names where possible to keep diffs reviewable.

2. **Quick Capture**
   - Move already-defined internal components into a small folder.
   - Keep the public `QuickCapture` API stable.

3. **Mobile request detail**
   - Extract timeline, date picker, timing controls, and formatters.
   - Keep route file focused on screen orchestration.

4. **Core/application backend hotspots**
   - Review `KeepRequest.cs` and `GetKeepRequestListService.cs` after UI cleanup.
   - Prefer domain-policy or mapper extraction only where responsibilities are already distinct.

---

## Candidate Target Shapes

### Web Request Detail

Potential folder:

```text
web/ophalo-app/src/pages/request-detail/
```

Potential files:

- `RequestDetailPage.tsx`
- `DetailHero.tsx`
- `OriginalRequestCard.tsx`
- `Timeline.tsx`
- `AttentionGuidanceCard.tsx`
- `WorkControlsGroup.tsx`
- `LogContactModal.tsx`
- `BusinessUpdateSection.tsx`
- `requestDetailUtils.ts`

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

---

## Exit Criteria

This cleanup session is complete when:

1. The highest-risk oversized frontend file has been split or intentionally deferred with rationale.
2. Follow-up cleanup items are listed in this log or `docs/deferred-topics.md`.
3. Regression tests and relevant frontend checks pass.
4. The deployment candidate no longer depends on editing a 1,500+ line frontend file for routine
   request-detail polish.
