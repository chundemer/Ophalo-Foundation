# Build Log 092 — ADR-450 Request List Row-Context Handoff

**Status:** Pre-work complete — implementation ready  
**Date:** 2026-07-27  
**Scope:** Session 3.0c / ADR-450 / GAP-007  
**Controlling decisions:** ADR-435, ADR-447, ADR-450

## Purpose

Implement the locked Request List row-context contract without turning list rows into timelines or
exposing private content. An authorized staff member must be able to understand the customer's
original need and see that team context exists without opening Request Detail.

## Discovery Findings

The existing list pipeline is the correct composition point:

- `GetKeepRequestListService` produces the already-authorized, already-sliced page of
  `KeepRequestSummary` rows.
- Session 3.0b's `ApplyPagePreviewsAsync` then makes one bounded batch read for the latest safe
  activity per row. Its persistence implementation selects only customer messages,
  customer-visible business updates, or a derived external-contact label; it never selects
  internal-note or feedback text.
- Today, the preview fallback is the full original description. That is no longer acceptable as
  the row's primary original-request context: ADR-450 requires a bounded original summary with
  local expansion, and latest safe activity must remain a distinct secondary cue.
- Existing list visibility and role authorization must stay unchanged. Note presence is additive
  metadata after that authorization, never a reason to widen visibility.

## Locked Read Model

Replace the use of `Preview` as an original-description fallback with two separate row fields:

```text
originalSummary: {
  fullText: string         // complete original description, bounded by the existing 4,000-char write limit
}

latestActivity: {
  previewText: string | null,
  previewSource: "customer_message" | "business_update" | "external_contact" | null,
  previewTruncated: boolean,
  previewAtUtc: string | null
}

hasInternalNote: boolean   // server-authorized, human-authored presence only
```

`originalSummary.fullText` is bounded by the established request-description maximum rather than a
new unbounded payload. The PWA derives the collapsed 240-character display locally from that text;
this is what makes `Read full request` a local, no-fetch interaction. Do not duplicate prefix and
full-body fields in the DTO. A non-empty original description is an existing request-creation
invariant; do not add empty-summary placeholder or activity-as-summary fallback behavior for this
batch. Any legacy/import path that violates the invariant is a data-integrity concern to remediate
at that boundary.

`latestActivity` must be `null` when no safe activity exists; do not synthesize original description
as activity. Preserve the existing safe selection precedence and source labels from 3.0b.

## Persistence And Authorization

Add two page-batched persistence reads, each receiving only the request IDs of the final sliced
page:

1. An original-description projection keyed by request ID, retaining the established 4,000-character
   request-write bound. Do not load event history or add a second full-body fetch for this.
2. An internal-note-presence projection keyed by request ID. It may inspect only whether at least
   one qualifying human-authored internal-note event exists: `InternalNoteAdded`, or a non-empty
   `ParticipationInternalNote`, with `ActorType == AccountUser`. It must not select event content,
   participation-note content, feedback-review content, actor details, or timestamps. System events
   and feedback-review notes never count. This is a durable, full-history `EXISTS` signal: terminal
   requests remain eligible and multiple qualifying notes still produce one boolean cue.

`KeepRequestEvent` is currently immutable and has neither `DeletedAt` nor a note-delete/redaction
workflow. Do not invent a soft-delete predicate for this batch. If deletion/redaction is introduced
later, its retention semantics must explicitly decide whether it removes the presence cue, and the
query must then follow that decision.

The service decides whether to surface the resulting presence bit from the current viewer's
server-authorized permission. Reuse the existing `Keep.InternalNotesAdd` permission: Owners,
Admins, and permitted Operators may receive the presence bit; Viewers and every permission-denied
state receive `false` regardless of database presence. This does not grant note-reading access or
alter any list visibility rule.

The implementation must retain account scoping, existing request visibility scope, and the final-page
batching rule. It must not issue per-row queries, mutate data, add event-history search, or expose
internal/feedback content through a boolean's label or surrounding copy.

## PWA Contract

In `RequestRow`:

- Render `originalSummary` as stable context before the latest-activity cue. In its collapsed form,
  normalize whitespace/newlines to single spaces, show at most 240 characters, and back up to a word
  boundary rather than split a word.
- Show `Read full request` whenever that collapsed presentation differs from the supplied full text
  (including whitespace normalization) or exceeds 240 characters. It toggles local presentation
  only, has `aria-expanded`, stops propagation so it does not open the row, and changes to
  `Show less`. Expanded text preserves the supplied original text exactly.
- Keep expansion state local to the rendered list/page; a queue, search, filter, or cursor-page
  change remounts/resets it.
- Render a quiet, neutral `Internal note` cue only when `hasInternalNote` is true. It is not a
  button, urgency marker, count, author attribution, or claim that the current viewer can read the
  note. Place it at the end of the existing bottom metadata line, not between the original-context
  and activity blocks; use a muted note/notepad icon rather than a lock, and no alert styling.
- Render latest activity only from `latestActivity`; it remains visually secondary and must never
  replace original context.

## Required Verification

- Application/service tests: original context and safe latest activity are separately populated;
  no-safe-activity yields `latestActivity: null`; a viewer/permission-denied user gets false note
  presence; a permitted internal-note user gets true only for a human-authored qualifying note.
- Persistence proof: original summary is bounded/truthful; note-presence batch read is account
  scoped, contains no content projection, and excludes feedback-review-only events.
- API contract tests: no internal-note, participation-note, or feedback text appears in a list row;
  list visibility remains unchanged for Operator and Viewer.
- PWA tests: word-boundary/whitespace-aware collapsed summary; truncation toggle/ARIA/propagation;
  reset on list query/page change; activity is secondary; `latestActivity: null` leaves no empty
  wrapper margin/gap; neutral note cue appears only for `hasInternalNote`.
- Manual Owner/Admin completion check: a long original request is understandable from the row, its
  bounded expansion works without navigation, safe customer-visible activity is distinct, and team
  context is visibly present without note text.

## Out Of Scope

- Any list mutation/action changes, queue hierarchy work (3.0d), search/filter/paging redesign,
  full original-description payloads, internal-note text/search/counts/authors/timestamps,
  participation-note content, feedback-review text, and changes to list visibility or permissions.

## Implementation Result — Approved Batch-Size Exception

**Status:** Implemented and verified. Christian approved a 13-file batch against the 12-file hard
cap (CLAUDE.md batch gate), justified because the API-contract test verifying no internal-note/
participation-note/feedback text leaks into the list JSON is inseparable from removing `Description`
from the DTO — splitting the API test out, or splitting mocks from their tests, would leave an
invalid intermediate contract (mock mode broken, or the leak-prevention behavior unverified).
`docs/session-log.md` shows as modified in the same working tree but is a pre-existing, unrelated
pending change from before this batch started; it is not counted in the 13.

Production files (7, within the 8-file limit):
- `src/OpHalo.Keep.Application/Requests/KeepRequestSummary.cs`
- `src/OpHalo.Keep.Application/Requests/IKeepRequestListPersistence.cs`
- `src/OpHalo.Keep.Application/Requests/GetKeepRequestListService.cs`
- `src/OpHalo.Keep.Infrastructure/Persistence/KeepRequestListPersistence.cs`
- `web/ophalo-app/src/lib/apiClient.types.ts`
- `web/ophalo-app/src/pages/Requests.tsx`
- `web/ophalo-app/src/components/RequestRow.tsx`

Fakes (2): `web/ophalo-app/src/mocks/fixtures.ts`, `web/ophalo-app/src/mocks/mockApiClient.ts`

Tests (4): `tests/OpHalo.UnitTests/Keep/KeepRequestListServiceTests.cs`,
`tests/OpHalo.IntegrationTests/Persistence/KeepPersistenceProofTests.cs`,
`tests/OpHalo.IntegrationTests/Api/KeepRequestListQueryApiTests.cs`,
`web/ophalo-app/src/components/__tests__/RequestRow.test.tsx`

One mutation-handler family (read-model shape of the existing list query), well under the 3-family
limit.

**Corrections applied during review** (see PR discussion for full detail): non-nested row markup
(real sibling `<button>`s, not a `div[role=button]` wrapping a real button, since ARIA button
semantics make descendants presentational to assistive tech); composite reset key in `Requests.tsx`;
account-scoped `GetInternalNotePresenceAsync`; removal of `Description`/`Preview`
original-description fallback from both the C# and TS DTOs (mapped from `KeepRequest.Description`
at read time instead); `whitespace-pre-wrap` on expanded text; permission short-circuit skipping the
presence query entirely for denied viewers; collapsed-summary cap corrected to 239 chars + ellipsis
(240 total, not 241); mock/fixture `latestActivity` set to `null` (not an object with null fields)
wherever no safe activity exists, and the retired `"original_description"` preview source removed
from fixtures entirely.

**Verification:** 968 Keep unit tests, 636 Keep integration tests, 155 PWA tests, `git diff --check`
— all pass.
