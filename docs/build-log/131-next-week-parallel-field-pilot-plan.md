# Build Log 131 — Next-Week Parallel Field Pilot Plan

**Status:** Delivery-control plan — each code slice still requires its normal bounded mechanical
preflight and acceptance evidence
**Date:** 2026-08-19
**Related:** Build Logs 104, 128, 129; GAP-037, GAP-038, GAP-039; ADR-487, ADR-488

## Purpose and release posture

The next-week release is a **controlled parallel field pilot**, not the full mixed-contractor
cutover described in Build 104. Keep will capture factual field visits while the contractor's
existing paper/software workflow remains the authority for estimates, invoices, payments, and
accounting.

This posture creates a safe way to test whether Keep improves the field record without risking the
business's ability to bill. It does not lower the standards for authorization, tenant isolation,
data integrity, error handling, or production verification.

```text
Technician completes visit
        -> records factual Actual Work in Keep
        -> continues normal ticket/billing process outside Keep
Office compares the two records during the pilot
        -> records gaps, friction, and reliability findings
```

## Required next-week scope

1. **Request Work Context.** Staff can classify a request as `Residential` or `Commercial` before
   assigning field responsibility. Commercial context stays launch-minimum: on-site contact and
   PO/work-order reference where needed. No property hierarchy or authorization engine.

2. **Direct Actual Work MVP.** An authorized field user can start **Record completed work** from a
   request and submit one price-blind, per-visit factual record. It supports catalog or explicit
   custom lines, actual quantities, a field note where needed, recorder identity, and visit
   timestamp. Submitted visits are immutable history; office users can read them.

3. **Diagnostic/no-work safeguard.** A zero-line visit is allowed only with a required completion
   note and one truthful outcome: `DiagnosticOnly`, `NoWorkAuthorized`, or `NoAccess`. It must
   never silently represent a $0 billable job.

4. **Production error and usage insight.** Complete the errors-only Sentry slice with release and
   correlation metadata, strict removal of PII/secrets/tokens, and founder alert routing. Retain
   the existing health/readiness and correlated server-log path. Add only privacy-safe pilot usage
   counters/events needed for daily operations: sign-in, request created, Actual Work draft
   started, Actual Work submitted, Actual Work submission failed, and Report Friction submitted.
   No session replay, tracing, user profiling, general product analytics platform, or customer
   content capture is authorized.

5. **Feedback and operating loop.** Provide an authenticated Report Friction path, or an equally
   visible in-app support route that records enough account/screen context for follow-up without
   capturing customer free text by default. Name an owner for daily review of error alerts,
   failed-submission counts, usage, and reported friction.

6. **Final pilot UI-quality pass.** Review the real pilot paths on phone, tablet, and desktop:
   sign-in, request discovery, field capture, diagnostic submission, office history, error/empty/
   loading states, and feedback. Resolve wireframe signals such as placeholder/developer copy,
   raw identifiers, dead controls, weak hierarchy, inconsistent visual tokens, inaccessible focus
   behavior, or insufficient touch targets. This is a targeted acceptance pass, not a general
   redesign.

7. **Production rehearsal and parallel-run guide.** Rehearse the deployed end-to-end flow,
   including a normal repair and a diagnostic-only visit. Verify error reporting/alert routing and
   the feedback route. Give technicians a concise instruction to record the visit in Keep and to
   continue the existing ticket/billing process. Name the support and escalation owner.

## Explicitly deferred from next week

- Owner/Admin Proposed Work Review queue and **Mark reviewed** transition.
- Commercial estimates, quotes, pricing, costs, margin, and customer approval flows.
- Owner/Admin Actual Work closeout, CSV export, invoice/reference capture, and reconciliation.
- QuickBooks integration, invoicing, payments, tax, inventory, photos, routing, and offline
  mutation queues.

Proposed Work is deferred only from this narrow field-capture release. It remains the next office
workflow for recommendations that need a decision; it is not required for a technician to record
a repair already completed.

## Evidence and adjustment checkpoint

During the first week, compare Keep Actual Work records against the contractor's existing tickets
and billing records. Review missed parts/consumables, diagnostic clarity, time to log a visit,
submission failures, user-reported friction, and discrepancies between the two records.

At the end of the current week, review implementation and release-evidence progress. Christian may
adjust the next-week scope only by recording the reason, retained fallback, and acceptance gate.
Unfinished work remains deferred rather than being compressed into an unsafe cutover. Build 104
continues to govern the later full mixed-contractor pilot and accounting-closeout path.
