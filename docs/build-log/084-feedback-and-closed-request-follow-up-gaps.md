# Build Log 084 — Feedback Visibility And Closed Request Follow-up Gaps

**Started:** Prepared 2026-07-13  
**Status:** Complete — Session 30  
**Source tracker:** `docs/pilot-readiness-bug-tracker.md` GAP-012, GAP-013, GAP-014  
**Next free ADR before this log:** ADR-442

---

## Purpose

Pilot testing exposed three related polish gaps around the end of the customer request loop:

- customers can submit closed-request feedback without a clear submitted state;
- staff do not clearly see submitted customer feedback on authenticated request detail;
- closed requests need a path for new follow-up work without reopening the original request.

The product goal is to protect the closed-request lifecycle while making customer feedback and
post-close follow-up work visible, calm, and actionable.

---

## Locked Direction

### Feedback

- Feedback remains binary and resolution-oriented in V1:
  - `Yes, this was resolved`;
  - `No, I still need help`.
- No ratings, stars, CSAT/NPS, public reviews, or testimonials.
- Customer feedback submission must show a clear submitted state.
- Staff request detail must show submitted feedback according to existing role/visibility rules.
- Negative feedback should be hard for Owner/Admin to miss and should route to the existing review
  action.
- Positive feedback should be visible as a completed signal without creating false active work.
- Negative feedback does not reopen a request automatically.

### Closed Requests

- Do not add a general `Reopen` action in V1 unless a later ADR deliberately changes the lifecycle
  model.
- Closed request history, feedback, closeout timestamp, and customer-page state remain intact.
- Preferred direction is `Create follow-up request` from Closed request detail for Owner/Admin.
- A lighter `Copy request info` utility may be acceptable before a full prefilled-create flow if the
  full flow is too large for this pilot slice.

---

## Implementation Slices

Hard gate unless explicitly split: at most 3 mutation families, 8 production files, and 12 total
changed files including tests/docs per slice.

### S84a — Preflight and GAP-012 product decision

Goal: decide the smallest safe V1 path for Closed requests that need new work.

Decision to lock:

- full `Create follow-up request` flow from Closed detail;
- lighter `Copy request info` utility first;
- or defer GAP-012 after documenting the V1 no-reopen stance.

Preflight:

- inspect closed request detail actions and available server metadata;
- inspect Quick Capture / business-created request flow for prefill seams;
- confirm whether any backend relation/note support already exists for linking follow-up work;
- present a file-level gate before any implementation.

### S84b — Customer feedback submitted state

Goal: fix GAP-013 on the customer tracker page.

Expected changes:

- after successful feedback submit, render `Feedback submitted. Thank you.`;
- include safe supporting copy such as `Your feedback has been shared with {businessName}.`;
- render submitted state from server feedback fields on refresh/direct revisit when available;
- keep error, duplicate, and rate-limit copy customer-safe;
- preserve binary resolved / unresolved feedback semantics.

Verification:

- focused public tracker/feedback tests if backend or mapper changes;
- `ophalo-web` TypeScript check.

### S84c — Authenticated request detail feedback card/state

Goal: fix GAP-014 on the staff request detail page.

Expected changes:

- add or correct a feedback card/state on request detail;
- show positive/resolved feedback as a quiet completed signal;
- show negative/unresolved feedback prominently for Owner/Admin review;
- show comment, timestamp, and reviewed metadata only where existing visibility rules allow;
- render `Mark feedback reviewed` only when server metadata allows it;
- preserve that feedback does not automatically reopen the request.

Verification:

- focused backend/detail mapper tests if detail DTO lacks required fields;
- `ophalo-app` TypeScript check.

### S84d — Closed request follow-up path

Goal: implement the smallest path locked in S84a, if selected for this session.

Expected constraints:

- no general reopen;
- original Closed request remains Closed;
- copied/prefilled data respects public-token and visibility boundaries;
- new work links back through an internal note or future-safe relation field if implemented.

---

## Landed — Session 30

### S84a — Preflight and GAP-012 product decision
- Inspected Closed request detail actions, Quick Capture prefill seams, and existing creation endpoints.
- Decision locked: **Option A — full Create follow-up request** with prefilled Quick Capture.
- Guardrails locked: intentional lookup-gate bypass; safe description prefix only; original request never mutates.

### S84b — Customer feedback submitted state (GAP-013)
- `tracker-types.ts`: added `feedbackWasResolved` and `feedbackSubmittedAtUtc` to `CustomerPageData`.
- `CustomerTrackerView.tsx`: initialize phase as `{ kind: "feedback_sent" }` when `initialPage.feedbackSubmittedAtUtc != null` — revisit/refresh now shows the submitted state.
- `TrackerActionCard` already had correct copy ("Feedback submitted. Thank you." + "{businessName} appreciates you letting them know.") — no change needed.

### S84c — Authenticated request detail feedback card (GAP-014)
- `RequestDetail.tsx`: added `FeedbackSummaryCard` component — quiet "Customer confirmed resolved" card for positive feedback; quiet "Negative feedback reviewed" card for already-reviewed negative feedback.
- Card placed in `renderPrimaryActions` (mobile) and desktop sidebar Utilities section alongside `WorkControlsGroup`.
- All fields were already in `KeepRequestDetailResult` DTO.

### S84d — Closed request follow-up path (GAP-012)
Backend:
- `KeepRequestActionDecision.cs`: added `CanCreateFollowUpRequest` field.
- `KeepRequestActionPolicy.cs`: `DenyAll` → false; `Evaluate` → `isOwnerAdmin && status == Closed`.
- `KeepRequestDetailResult.cs`: added `CanCreateFollowUpRequest` to `AvailableActionsMetadata`.
- `KeepRequestDetailMapper.cs`: mapped `CanCreateFollowUpRequest: decision.CanCreateFollowUpRequest`.

Frontend:
- `apiClient.types.ts`: added `canCreateFollowUpRequest: boolean`.
- `CaptureForm.tsx`: extended `prefill` type to include `description?: string`; initializes description state from prefill.
- `QuickCapture.tsx`: added `followUpPrefill` prop — when present, bypasses lookup gate and initializes directly to capture stage with locked phone and prefilled name/email/description. Bypass is explicitly commented as intentional and scoped to Create follow-up request.
- `RequestDetail.tsx`: added `renderCreateFollowUpCard()` — renders "Create follow-up request" card with `KeepButton` (secondary) when `canCreateFollowUpRequest` is true; opens `QuickCapture` with prefill containing phone, name, email, and `Follow-up to closed request {referenceCode}: {description}`. Original request never mutated.
- `fixtures.ts` and `mockApiClient.ts`: added `canCreateFollowUpRequest: false` to keep mock types in sync.

Tests:
- 5 new `KeepRequestActionPolicy` tests: Owner/Admin true on Closed, Operator false on Closed, Owner false on non-Closed active, Owner false on Cancelled.
- 1,069 unit tests pass.
- ophalo-app and ophalo-web TypeScript clean.

### S84e — Closeout docs
- Build log, session log, and bug tracker updated.

---

## Deferred

- Native mobile follow-up completion (post-pilot).
- Planned For completion workflow (post-pilot).

---

### S84e — Closeout docs (original placeholder)

Goal: update this build log, session log, and bug tracker statuses with landed scope and deferred
items.

---

## Guardrails

- Do not expose internal review/attention state on the customer tracker page.
- Do not make feedback a public review system.
- Do not reopen Closed requests by side effect.
- Do not imply customer feedback creates a new request automatically.
- Preserve fail-closed account, membership, row/action-policy, and public-token behavior.
- Preserve role/comment visibility from ADR-151, ADR-263, ADR-271, and ADR-384.
