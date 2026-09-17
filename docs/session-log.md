# Session Log — OpHalo Foundation

**Next-session pointer only.** Read this first, then the canonical [workboard](workboard.md) for
scope, sequencing, gates, and deferrals. Locked decisions are in
[decision-index](decisions/decision-index.md); completed-work evidence is in `docs/build-log/`.

**Updated 2026-09-17.** The foundation-first sequence is active. GAP-039 Batch 4, GAP-038,
GAP-093, GAP-095, GAP-098, GAP-073, GAP-094, GAP-099, GAP-063, GAP-048, and GAP-049 are complete.
The canonical scope, order, gates, and deferrals are in the [workboard](workboard.md).

## Start here — GAP-092 (business-timezone display)

GAP-049 is fully closed (confirmed 2026-09-16): closed-request follow-up prefill already reserved
provenance-prefix space and safely truncated the copied source text at a whitespace boundary
(`buildFollowUpDescription` in `request-detail/helpers.ts`) — it had landed in `21baaab5` bundled
with GAP-047/048 but was never marked done on the board. 4/4 unit tests passing, no code change
needed. See the workboard's Done/evidence index for the full evidence trail.

GAP-092 slices **1a — Request Row** and **1b — Detail Hero** are both complete: `useBusinessTimeZone()`
reads the account IANA zone from the role-agnostic `['me']` query (not `['setup']` — see the
ADR-073/workboard correction), `businessTime.ts` provides the pure, reference-date-injectable
day-boundary comparisons, and both `RequestRow` and `DetailHero`'s `TodayPromiseBanner` stay
presentational via an optional `timeZone` prop threaded from the page (`Requests.tsx` /
`RequestDetail.tsx`).

GAP-092 slice **2a — shared formatters** is complete: `businessTime.ts`'s `formatInstant`/
`formatRelativeOrInstant` render absolute instants business-zone-aware, with an explicit
UTC-labeled fallback while unresolved; `helpers.ts`'s `formatDate`/`formatEventTime` gained an
optional `timeZone` (`undefined` = not yet migrated, temporary GAP-092 debt; `null` = migrated,
unresolved); `RequestCommunicationsWorkspace.tsx` is migrated end-to-end as the observable
corrected surface.

GAP-092 slice **2b — identity row** is complete: `DetailHero.tsx`'s `DetailHeroName`
(customer-page-viewed timestamp) takes `timeZone`, threaded through `RequestDetailAnchor.tsx` and
`MobileRequestAnchor.tsx` from `RequestDetailContent.tsx`'s existing `props.timeZone`.

GAP-092 slice **2c — remaining timeline/communications consumers** is complete: `TimelineEvent.tsx`
takes `timeZone` (threaded from `RequestDetailContent.tsx` through `RequestDetailActivity.tsx`/
`RequestMemoryRail.tsx`, and from `RequestDetailWorkCanvas.tsx` through `UnifiedComposer.tsx` →
`BusinessSection.tsx` → `NotifyCustomerPanel.tsx`). Exactly 8 production files, landed as one slice.

GAP-092 slice **2d — detail panels** is complete: `DetailPanels.tsx`'s `FeedbackSummaryCard`/
`ProminentFeedbackCard`/`RelatedWorkPanel`/`SourceMetaPanel` take `timeZone`, threaded through
`RecordDetailsSection.tsx` from `RequestDetailContent.tsx`, and directly from
`RequestDetailWorkCanvas.tsx` for `ProminentFeedbackCard`. **Slice (2) is now fully complete.**

Take up GAP-092 slice **(3) — Actual Work/history timestamp consumers** next: `ActualWorkReviewQueueList.tsx`,
`ActualWorkReviewCard.tsx`, `ActualWorkPendingReviewsCard.tsx`, `ActualWorkFinancialReviewWorkspace.tsx`
are the remaining `formatDate`/`formatEventTime` call sites still on the legacy (`undefined`)
compatibility path; migrating them all removes that compatibility branch from `helpers.ts` entirely.
Re-check file count against the batch gate before committing to one slice.

## Next several sessions

1. Continue the workboard's foundation-first order: GAP-092, GAP-100, then DEF-037
   needs-status-check resurfacing and DEF-063's Request Detail closeout warning. GAP-100 is
   promoted from DEF-025 because the current pilot requires office-hours-aware signals.
2. Run the **Proposed Work & Commercial Quotes** decision session. Its first deliverable is a
   decision record, not code; use its workboard Decision Queue entry and the cited ADRs/build logs.
3. Begin **GAP-069** only at the release-readiness trigger: about two weeks before Keep becomes the
   authoritative pilot record, after Railway Pro daily backups/PITR are enabled and the first PITR
   recovery window exists. See [authoritative-pilot-release-readiness.md](runbook/authoritative-pilot-release-readiness.md).

## Pilot posture

The controlled parallel field pilot keeps the existing system authoritative for estimates,
invoices, payments, and accounting; Keep is the factual field record. See
[BL131](build-log/131-next-week-parallel-field-pilot-plan.md).

## Current hot blockers

- No current platform blocker. GAP-099, GAP-063, and GAP-048 are closed and pushed. The
  release-readiness trigger for GAP-069 is documented in the workboard and the authoritative-pilot
  release runbook.
- Native GAP-091 review, S18, and S19 are deferred; they must not displace the foundation-first
  closed-loop sequence.
