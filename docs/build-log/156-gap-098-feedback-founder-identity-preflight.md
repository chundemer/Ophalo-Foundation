# BL156 — GAP-098: Feedback follow-up identity — preflight

**Status:** Slice A landed 2026-09-13 (test corrections applied — real two-attempt retry proving
fresh identity resolution, plus a worker-path reader-exception case). Slice B (frontend confirmation
copy) not started.

**Scope:** [workboard](../workboard.md#audit-and-operational-gap-registry) GAP-098; amends
[ADR-500](../decisions/ADR-500-in-product-feedback-and-help-updates-loop.md) (2026-09-13 amendment).
**Must complete before 038-R3.**

## Decisions (locked 2026-09-13)

- **No internal deep link.** There is no cross-workspace support/admin route or
  workspace-switching authorization model in OpHalo today, so inventing a PWA URL would be
  misleading and unsafe. The founder alert carries `AccountId` and `AccountUserId` (stable, always
  present — sourced straight from `FeedbackSubmission`) instead of a clickable link.
- **Resolved at delivery time, not persisted.** Business name, submitter display name, membership
  role, and email are resolved fresh from the current **active** `AccountUser`/`Account`/`User`
  rows on every delivery attempt (the 038-2b synchronous attempt and each 038-2c retry) — never
  cached on `FeedbackSubmission`, never trusted from the client. A missing, removed, suspended, or
  otherwise non-active membership is unavailable: delivery still proceeds using only the stable IDs.
  Resolution failure is fail-soft and never blocks or retries delivery on its own.
- **Two-commit split** (stays under the 8-prod-file / 12-total batch gate):
  - **Slice A** (this preflight): backend identity resolver, founder-alert enrichment, DI, and
    backend tests.
  - **Slice B**: frontend confirmation-copy change (`FeedbackDialog.tsx`) to the ADR-500-locked
    text, plus its test.

## Slice A — mechanical preflight

New:
- `src/OpHalo.Foundation.Application/Feedback/IFeedbackIdentityReader.cs` — `FeedbackFounderIdentity`
  record (`BusinessName`, `SubmitterName`, `Role`, `Email`) + reader interface, keyed on
  `(AccountId, AccountUserId)`. Domain scalars only (Application/Infrastructure boundary).
- `src/OpHalo.Foundation.Infrastructure/Feedback/EfFeedbackIdentityReader.cs` — EF query over
  active `AccountUser` (`Account.BusinessName`, `AccountUser.Role`, `AccountUser.Email`,
  `AccountUser.User.Name`), returns `null` when no active row matches.
- `tests/OpHalo.IntegrationTests/Persistence/EfFeedbackIdentityReaderTests.cs` — found/not-found
  plus non-active-membership fallback cases against `PostgresFixture`.

Edited:
- `src/OpHalo.Foundation.Application/Notifications/IFounderNotifier.cs` — `FounderEvent` gains
  optional `AccountId`, `AccountUserId`, `BusinessName`, `SubmitterName`, `Role`, `Email`. Existing
  operational-alert callers (`content_source_failure`, `delivery_backlog`/`delivery_abandoned`)
  leave the new fields unset — no behavior change for them.
- `src/OpHalo.Foundation.Infrastructure/Notifications/FounderNotifier.cs` — `RenderText` appends
  `account:`/`submitter:` lines only when the identity fields are present.
- `src/OpHalo.Foundation.Application/Feedback/FeedbackSubmissionService.cs` — resolves identity via
  `IFeedbackIdentityReader` immediately before the synchronous delivery attempt; a reader exception
  is caught and logged, never faults the request (matches the existing fail-soft/persist-first
  posture).
- `src/OpHalo.Foundation.Application/Feedback/FeedbackDeliveryWorker.cs` — same resolve-then-notify
  call in `ProcessDueRetriesAsync`, so a stale name/role is never re-sent on retry.
- `src/OpHalo.Api/Program.cs` — DI registration for `IFeedbackIdentityReader` →
  `EfFeedbackIdentityReader`.

Test edits: `FeedbackSubmissionServiceTests.cs`, `FeedbackDeliveryWorkerTests.cs`,
`FounderNotifierTests.cs` — cover identity-present, identity-unavailable (fallback), and
reader-throws (fail-soft) cases.

**Count:** 7 production files (2 new), 4 test files (1 new) — 11 total. Within gate.

## Verification (Slice A, 2026-09-13)

- `OpHalo.UnitTests` Feedback filter: 126/126 — includes a real two-attempt retry proving the
  worker re-resolves identity on each attempt (first attempt fails with one identity, second
  attempt succeeds with a changed role) and a worker-path reader-exception case (fail-soft, IDs
  only, delivery still confirmed).
- `OpHalo.IntegrationTests` `EfFeedbackIdentityReaderTests` (active/missing/suspended-membership
  fallback): 3/3. Full Feedback filter: 75/75.
- `OpHalo.ArchitectureTests`: 14/14.
- `git diff --check`: clean.
