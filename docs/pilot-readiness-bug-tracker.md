# Pilot Readiness Bug And Gap Tracker

**Purpose:** The live, forward-looking backlog for unresolved pilot-readiness work.

**Last triaged:** 2026-08-31

Historical findings, resolved work, and superseded implementation notes were removed from this document. They remain in [the session log](session-log.md) and the relevant `docs/build-log/` records. A tracker item belongs here only while it has remaining work or an unresolved decision.

## Status Legend

- **Open:** Decision is sufficiently clear; implementation has not started.
- **In progress:** A bounded implementation is underway; the stated remainder is still required.
- **Reopened:** A prior remedy was incomplete or superseded.
- **Needs decision:** Product direction must be locked before implementation.

## Tomorrow's Launch Gates

These are not an instruction to ship every remaining item before a supervised pilot. They define the conditions that must be satisfied before enabling the relevant workflow.

| Item | Gate condition |
| --- | --- |
| GAP-039 | Required before any customer-facing production pilot. |
| GAP-033 | Required before enabling public customer intake. |
| GAP-055 | Required before technicians capture Actual Work directly. |
| GAP-048 | Required before using email to share private customer request pages. |
| GAP-047 | Required if staff relies on Internal priority for operational triage. |
| GAP-016 / GAP-021 | Required before native use or accepting common `+1` phone entry in Quick Capture. |
| GAP-049 | Required before relying on follow-up creation from closed requests. |

## Implementation Order

Complete each numbered slice with focused automated coverage and a production-candidate/manual check where applicable. Do not start a dependent slice before its prerequisite is accepted.

1. **Release safety and truthful public entry:** GAP-039, GAP-033, then GAP-040. Establish safe production observability and configuration validation first; make the public request journey and published claims truthful second. (GAP-056 customer SMS/QR handoff sender/business context — resolved, commit `0fc7a2a`.)
2. **Field-work correctness:** GAP-055. This is a P0 authorization/data-model correction and needs its own migration, API, UI, and concurrency test plan.
3. **Phone and capture integrity:** GAP-016, GAP-021, GAP-051, then GAP-025. Consolidate the ADR-444 normalization path before extending fallback customer recognition.
4. **Request Detail foundation and correctness:** GAP-019, then GAP-047, GAP-048, and GAP-049. Decompose layout ownership before changing shared desktop/mobile behavior; then fix the bounded mutation, sharing, and follow-up defects.
5. **Request-list product decision and core behavior:** GAP-027 (decision), then GAP-045, GAP-057, GAP-042, GAP-041, GAP-046, GAP-043, GAP-044, GAP-026, and GAP-053. This locks row hierarchy before implementing queue context, sensible default selection, loading, filtering, scale/history, and small action-order polish.
6. **Pilot operating loop and final usability review:** GAP-037 (after GAP-039), GAP-038, and GAP-054. Deliver the founder's evidence/reporting loop, a fail-soft feedback route, and a final role/device navigation review.

## Active Work

### GAP-039 — Production failures and pilot health are not observable enough to earn trust

**Status:** Open
**Severity:** P0
**Area:** Production reliability and internal product operations

Configure redacted server/browser error capture with release identity, health checks, an actionable alert/runbook path, and production validation for required public configuration. Verify controlled failures, token/PII redaction, and that the founder can distinguish product faults from quiet accounts.

### GAP-033 — Public intake does not establish sufficient customer trust or return continuity

**Status:** Open
**Severity:** P1
**Area:** `ophalo-web` public intake

Before asking for customer address/contact data, show business identity and configured public contact information when available; place factual privacy/use disclosures before relevant fields; keep email visible and optional; take a successful submission directly to its private tracker; and provide a real privacy-policy link. Public copy must not promise automatic tracker-link email, verification, or unsupported security properties.

### GAP-040 — Marketing site does not accurately represent the current product or launch posture

**Status:** Open
**Severity:** P1
**Area:** `ophalo-web` marketing and legal/support routes

Audit public routes, copy, images, links, metadata, and deployment behavior against shipped V1. Remove unsupported automatic-email, SMS, verification, response-time, revenue, or security claims; use representative non-private visuals; and verify desktop/mobile, keyboard, and production-host behavior.

### GAP-055 — Actual Work capture is incorrectly blocked by dispatch assignment

**Status:** Open — decision locked; remediation planned
**Severity:** P0
**Area:** Actual Work authorization and Draft ownership

Implement ADR-487 first-recorder ownership: qualified active members may create the one Draft; `RecorderAccountUserId` exclusively controls mutable Draft work; creation authorship remains immutable; and Owner/Admin transfer is explicit, reason-required, and audited. Preserve one-Draft and concurrency guarantees while auditing every current Responsible-based authorization path.

### GAP-016 — New Request phone validation and correction path remains incomplete

**Status:** In progress
**Severity:** P0
**Area:** Quick Capture, authenticated request API, and native parity

Finish the ADR-444 normalized ten-digit North American policy across native and all client paths, including leading `1`/`+1`, correction from capture, and consistent actionable validation.

### GAP-021 — Quick Capture rejects valid country-code input

**Status:** Open
**Severity:** P1
**Area:** `ophalo-app` Quick Capture lookup

Normalize an 11-digit value beginning with `1` to its final ten digits before the UI gate, lookup, and return-to-draft path.

### GAP-051 — Phone formatting remains incomplete outside the authenticated PWA

**Status:** In progress
**Severity:** P1
**Area:** Native and public phone input/display

Authenticated PWA staff-facing formatting is complete. Finish native parity and the public-web audit, including tolerant `1`/`+1` input and formatted configured business-phone display, while preserving canonical stored values and `tel:`/API behavior.

### GAP-025 — Quick Capture hides request-phone-only customer continuity

**Status:** Reopened
**Severity:** P1
**Area:** Quick Capture identity lookup

Implement ADR-492: retain canonical-customer matches, but render a request-phone-only hit as an explicit possible existing customer with up to three active-request cards and a clear choice to open, reuse, or create new. Never auto-select, link, navigate, or silently backfill; preserve exact, account-scoped normalized lookup and the no-candidate-cap protections.

### GAP-019 — Request Detail needs layout decomposition before further behavior changes

**Status:** Open
**Severity:** P1
**Area:** `ophalo-app` Request Detail architecture

Keep one controller for data and mutations, while extracting desktop/mobile composition and shared panels. Do not fork business behavior by device; shared callbacks, policy, accessibility, and concurrency behavior must remain common.

### GAP-047 — Internal-priority updates can fail silently on Request Detail

**Status:** Open
**Severity:** P1
**Area:** Request Detail triage mutation

Surface associated failure feedback for transport/API failures and stale-version conflicts. A failed or conflicted priority change must not appear saved; require refresh before further stale mutations.

### GAP-048 — Emailing a private request page bypasses deliberate share intent

**Status:** Open
**Severity:** P1
**Area:** Request Detail customer email/share path

Route email containing a private tracker through the explicit share workflow. Opening `mailto:` is not proof of delivery; only an informed owner confirmation records sharing. Preserve token secrecy, plain-email capability, and truthful `Needs Share` state.

### GAP-049 — Closed-request follow-up prefill can exceed the description limit

**Status:** Open
**Severity:** P1
**Area:** Request Detail follow-up creation

Reserve space for the provenance prefix and safely truncate copied source text so maximum-length closed requests can start a valid follow-up without changing the original record.

### GAP-027 — Request-list alerts compete and lifecycle state is hard to scan

**Status:** Needs decision
**Severity:** P1
**Area:** Request-list row hierarchy and lifecycle presentation

Lock a compact, truthful lifecycle cue and a deterministic single-exception priority. Suppress ordinary SLA/follow-up alarms for terminal work, retain the approved unresolved-feedback exception, and reconcile prominent row signals with queue counts.

### GAP-045 — Default Queue language does not explain work scope or prioritization

**Status:** Open
**Severity:** P1
**Area:** Request-list orientation

Replace implementation language with the locked Owner/Admin label **All work** and clear supporting copy explaining that open requests and review work are ranked with customer promises needing attention first. Keep server queue/ranking authority unchanged.

### GAP-057 — Empty Attention queue falsely implies the system has no active requests

**Status:** Open — decision locked
**Severity:** P1
**Area:** Request-list initial queue selection and empty state

When an Owner/Admin opens Requests and **Needs Attention** is empty, the current selection leaves the work area saying **No active requests** even when the visible **All** tab has active work. That is false and makes an owner click a tab just to see the system's actual workload.

**Locked resolution:** Do not add another tab. On initial Requests landing only, select **Needs Attention** when it has one or more items; otherwise select **All work**. Preserve a user's explicit tab selection for the rest of that visit—do not automatically switch tabs after a mutation or background refresh. If a user is already viewing an Attention queue that becomes empty, retain the selected queue and show the truthful **Nothing needs attention** state with an accessible **View all {count} active requests** action when active work exists. The main empty state must never say **No active requests** unless the All-work result is actually empty.

**Acceptance criteria:**

- With zero attention items and active work present, initial landing shows All work and its requests without a click.
- With attention items present, initial landing retains attention-first triage.
- An explicit selection does not jump because counts change; every empty-state message and action remains truthful, keyboard-accessible, and correct after refresh.
- Focused PWA tests cover both initial-count cases, post-mutation empty Attention, and truly empty All work.

### GAP-042 — Authenticated request work lacks visible business identity

**Status:** Open
**Severity:** P1
**Area:** Request List and Request Detail context

Add restrained, fresh business-name context to authenticated list/detail views without competing with the request/customer, duplicating stale labels, or exposing account identity publicly.

### GAP-041 — First queue selection blanks the work area

**Status:** Open
**Severity:** P1
**Area:** Request-list loading and queue tabs

Keep queue context and list geometry stable during first fetch, use an appropriate loading treatment, and complete tab keyboard behavior without showing prior-queue rows under a new label.

### GAP-046 — Request search and filters lack visible applied-state and recovery

**Status:** Open
**Severity:** P2
**Area:** Request-list search/filter accessibility

Show applied criteria, an accessible result/status announcement, and a clear/reset path. Preserve deliberate-submit search, cursor/query binding, and accurate cursor-page count language.

### GAP-043 — Request-list scale behavior is not a verified operating experience

**Status:** Open
**Severity:** P1
**Area:** Cursor pagination and scale UX

Make and document a V1 scale decision from representative pilot data. If retaining the cursor model, make page transitions, older/newer work, focus, end state, and result context clear without adding misleading offset/numbered pagination or infinite scroll.

### GAP-044 — Completed and cancelled work is not discoverable in the PWA

**Status:** Open
**Severity:** P1
**Area:** Request history access

Expose the existing authorized closed/cancelled/all-history API views through a clear PWA path. Keep active and terminal contexts distinct and preserve roles, protected cursors, filters, and detail-back navigation.

### GAP-026 — Request-list search has no clear affordance

**Status:** Open
**Severity:** P2
**Area:** Request-list search

Add an accessible clear control that restores the selected queue's unfiltered list by keyboard or pointer. Deliver with GAP-046 rather than as a separate interaction pattern.

### GAP-053 — Needs Attention reverses canonical row communication action order

**Status:** Open
**Severity:** P2
**Area:** Request-list row actions

Render **Update customer** before **Log contact** whenever both actions are allowed, including Needs Attention, Open Work, and narrow layouts. Share the ordering rule and cover visual plus focus order.

### GAP-037 — Pilot has no weekly, evidence-based value report

**Status:** Open
**Severity:** P1
**Area:** Founder/pilot operations

Provide a founder-only, account-timezone, copy-pasteable weekly summary of safe request-level signals. Exclude Spam/Test and demo/internal accounts; do not turn it into owner analytics, automated email, staff scoring, or unsupported business-outcome claims.

### GAP-038 — Pilot businesses lack an in-product feedback and help loop

**Status:** Open
**Severity:** P1
**Area:** Authenticated PWA pilot support

Add an authenticated, rate-limited, fail-soft feedback route to a private founder channel and a maintained Help & Updates page. Do not automatically attach customer PII, broad logs, or create a ticketing/CMS system.

### GAP-054 — Authenticated app-shell navigation and action hierarchy needs review

**Status:** Open
**Severity:** P2
**Area:** Authenticated desktop/mobile shell

Perform a role- and entitlement-aware desktop/mobile review of global versus page-local actions, profile grouping, active-route treatment, discoverability, keyboard behavior, and narrow layouts. Make only evidence-backed shared-shell changes and record browser verification.
