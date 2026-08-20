# Build Log 131 — Next-Week Parallel Field Pilot Plan

**Status:** Delivery-control plan — each code slice still requires its normal bounded mechanical
preflight and acceptance evidence
**Date:** 2026-08-19
**Related:** Build Logs 104, 128, 129; GAP-037, GAP-038, GAP-039; ADR-487, ADR-488

## Purpose and release posture

The next-week release is a **controlled parallel field pilot**, not the full mixed-contractor
cutover described in Build 104. For the named pilot technicians, Keep is the normal primary field
record for supported jobs. The contractor's existing paper/software workflow remains the authority
for estimates, invoices, payments, and accounting.

This posture removes routine technician dual entry while preserving billing continuity. It does not
lower the standards for authorization, tenant isolation, data integrity, error handling, or
production verification.

```text
Pilot technician completes visit
        -> records factual Actual Work in Keep (the normal field record)
        -> Owner/Admin reviews work and financials in Keep
        -> office creates billing in the existing system from the reviewed Keep record
Keep unavailable
        -> existing ticket is the explicit exception fallback
        -> technician enters/retries Keep once connection is restored
```

## Pilot operating model

Start with one or two named technicians and supported service/repair work. Multi-technician work
is allowed: the active Responsible user records the single Actual Work visit and remains
accountable for job details; other technicians do not create parallel field records. Keep is not a
routine second entry for field staff. The office continues its normal accounting/billing entry from
the Owner/Admin-reviewed Keep record until the later CSV handoff is released.

## Completed foundation

**Work Context storage foundation.** `KeepRequest` now durably stores
`Unclassified`/`Residential`/`Commercial`; existing requests migrate truthfully to
`Unclassified`, and both creation factories remain backward compatible. This is a safe data
foundation only. It adds no pilot-visible selection, label, correction, list filter, assignment
gate, commercial fields, or workflow behavior.

## Required next-week scope, in delivery order

1. **Direct Actual Work MVP.** An authorized field user can start **Record completed work** from a
   request and submit one price-blind, per-visit factual record. It supports catalog or explicit
   custom lines, actual quantities, a field note where needed, recorder identity, and visit
   timestamp. Submission raises an Owner/Admin Actual Work Review queue item. The office review
   shows the factual visit alongside its Price Book-backed sales price, Standard/Expected Direct
   Cost, margin, totals, and any financially incomplete line; it records the office reviewer and
   review time. Field capture remains price-blind.

2. **Diagnostic/no-work safeguard.** A zero-line visit is allowed only with a required completion
   note and one truthful outcome: `DiagnosticOnly`, `NoWorkAuthorized`, or `NoAccess`. It must
   never silently represent a $0 billable job.

3. **Production error and usage insight.** Complete the errors-only Sentry slice with release and
   correlation metadata, strict removal of PII/secrets/tokens, and founder alert routing. Retain
   the existing health/readiness and correlated server-log path. Add only privacy-safe pilot usage
   counters/events needed for daily operations: sign-in, request created, Actual Work draft
   started, Actual Work submitted, Actual Work submission failed, and Report Friction submitted.
   No session replay, tracing, user profiling, general product analytics platform, or customer
   content capture is authorized.

4. **Feedback and operating loop.** Provide an authenticated Report Friction path, or an equally
   visible in-app support route that records enough account/screen context for follow-up without
   capturing customer free text by default. Name an owner for daily review of error alerts,
   failed-submission counts, usage, and reported friction.

5. **Final pilot UI-quality pass.** Review the real pilot paths on phone, tablet, and desktop:
   sign-in, request discovery, field capture, diagnostic submission, office history, error/empty/
   loading states, and feedback. Resolve wireframe signals such as placeholder/developer copy,
   raw identifiers, dead controls, weak hierarchy, inconsistent visual tokens, inaccessible focus
   behavior, or insufficient touch targets. This is a targeted acceptance pass, not a general
   redesign.

6. **Production rehearsal and parallel-run guide.** Rehearse the deployed end-to-end flow,
   including a normal repair and a diagnostic-only visit. Verify error reporting/alert routing and
   the feedback route. Give technicians a concise instruction that Keep is the primary field
   record, with the existing ticket process used only when Keep is unavailable. Name the support
   and escalation owner.

## Explicitly deferred from next week

- Owner/Admin Proposed Work Review queue and **Mark reviewed** transition.
- Commercial estimates, customer quote/approval flows, and accounting closeout/reconciliation
  decisions beyond the required Owner/Admin Actual Work financial review.
- Owner/Admin Actual Work closeout, CSV export, invoice/reference capture, and reconciliation.
- QuickBooks integration, invoicing, payments, tax, inventory, photos, routing, and offline
  mutation queues.
- User-facing Work Context: staff selection/correction, request detail/list display or filter,
  Responsible-assignment gate, and commercial on-site-contact/PO fields.

Proposed Work is deferred only from this narrow field-capture release. It remains the next office
workflow for recommendations that need a decision; it is not required for a technician to record
a repair already completed.

## Post-pilot expansion candidates

Pilot evidence—not the existence of the storage field—determines whether Work Context grows.
If residential/commercial distinctions prove useful, begin with a simple staff-visible label and
authorized correction on request detail. Consider list filtering, commercial facts, or a
Responsible-assignment gate only after the pilot establishes a concrete operational need. None is
an implied Day-1 requirement.

## Evidence and adjustment checkpoint

During the first week, compare Keep Actual Work records against the contractor's existing tickets
and billing records. Review missed parts/consumables, diagnostic clarity, time to log a visit,
submission failures, user-reported friction, and discrepancies between the two records.

At the end of the current week, review implementation and release-evidence progress. Christian may
adjust the next-week scope only by recording the reason, retained fallback, and acceptance gate.
Unfinished work remains deferred rather than being compressed into an unsafe cutover. Build 104
continues to govern the later full mixed-contractor pilot and accounting-closeout path.
