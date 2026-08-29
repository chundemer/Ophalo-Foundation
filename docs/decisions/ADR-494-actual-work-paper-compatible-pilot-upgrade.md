# ADR-494 — Actual Work Paper-Compatible Pilot Upgrade

**Status:** Locked
**Date:** 2026-08-29
**Related:** ADR-463, ADR-487, ADR-488, ADR-493; Build Logs 129, 135, 136

## Context

The first pilot business records most field work on paper; an office administrator transcribes it
into Keep later, while a smaller group of technicians adopts field capture gradually. The completed
Actual Work financial foundation (immutable submitted facts, append-only financial
resolution/disposition, hard review gate) stays valid. What is missing is the capture and
office-review experience around those safeguards: the person who *performs* work is now routinely
different from the person who *records* it, a submitted visit with a pre-review factual error has no
correction path short of the deferred Billing Revision work, and request close ignores outstanding
Actual Work entirely.

There is **no live Actual Work data in production**. All existing Actual Work rows are disposable
local demo data. The schema is therefore made strict from the outset rather than carrying a
permanent "unknown historic performer" exception.

BL135 Batch 5 (Billing Revision) is paused; it resumes only after the slices below (BL136 4c–4g)
land. This is a sequencing change, not a relaxation of financial controls.

## Decision

### D1 — Per-line performer is an authoritative, non-null fact

`ActualWorkLine` gains `PerformedByAccountUserId` (**non-null**) — who performed that line's work.
This **supersedes ADR-487's future-facing `RecordedByAccountUserId` constraint**: `CreatedByUserId`
already records authorship (who entered the line); performer is the distinct fact worth persisting
now that office transcription makes recorder and performer materially different. `ActualWork` gains
a nullable `DefaultPerformedByAccountUserId` that seeds new lines only; the per-line value is
authoritative and always present once a line exists.

Because no production Actual Work data exists, the strict non-null migration is safe with **no
backfill of any kind** — it must never manufacture performer attribution. Local demo data must be
reset before the migration is applied (D12). If a developer applies the migration against a local
database that still holds Actual Work rows, **the migration fails loudly**; that is the intended
behaviour, not a case to paper over with a deterministic fill.

### D2 — Performer selection rules

- A newly selected performer must be an **active, account-scoped, eligible staff user** (holds the
  capture-eligible permission set), validated server-side against the account membership snapshot —
  never a free-form id, never a cross-account user.
- An **inactive former user remains valid on an already-recorded line** for historical truth. The
  performer picker offers active eligible members ∪ {the line's current value}, so a now-inactive
  technician stays selectable and displayed on lines already attributed to them.
- The ticket-level "Performed by" default is visible and editable **before lines are added**:
  - Technician-created Draft → default to that technician.
  - Office-transcribed Draft → the office user selects the technician first; new lines inherit it.
- **Draft handoff never rewrites existing line performers.** A recorder-ownership transfer changes
  only who may edit the Draft; every already-recorded line keeps its captured performer.
- The line performer is **never silently defaulted to the current Draft recorder.**

### D3 — The three note types and one validation convention

Keep now has three distinct Actual Work notes. All three are **trimmed-to-null, maximum 2,000
characters**, and are never interchangeable:

| Note | Owner / when | Required? | Lifecycle |
|---|---|---|---|
| `VisitNote` | current recorder, while `Status = Draft` | optional | new (this ADR); frozen at submit; readable on history / financial detail / workspace |
| `CompletionNote` | recorder, at submit | **required only for a zero-line submit** | existing; also settable on a Draft via D5's `SetZeroLineDisposition` for the replacement flow; `Submit` remains the authoritative validator |
| `ReviewNote` | Owner/Admin, at review | optional | existing; office acknowledgement only |

`VisitNote` is a new nullable `ActualWork` column, editable only while `Status = Draft` and only by
the current recorder, through a dedicated Draft-guarded mutation path with the same authorization and
optimistic-concurrency contract as line edits. It carries no financial content and is never a
substitute for a submitted factual line. `CompletionNote`'s 2,000-character / trimmed-to-null rule is
**locked here** (it was previously unbounded); it stays required only for zero-line submission. A
zero-line visit may carry `VisitNote` and `CompletionNote` independently.

### D4 — Office Draft authority and handoff

No new capability is minted for the pilot. Office transcription uses the existing `RequestsOperate`
+ `ActualWorkCapture` permissions (the pilot's office administrator is given a seat that holds
them). Draft handoff:

- An Owner/Admin may transfer an unsubmitted Draft's recorder, reason-required, as today.
- The **current recorder may additionally transfer their own unsubmitted Draft** to another
  eligible user ("hand off to the office"), reusing the existing immutable
  `ActualWorkDraftRecorderTransfer` audit event with a system-supplied reason.
- The transfer target must still hold `RequestsOperate` + `ActualWorkCapture`.
- Shared concurrent Draft editing is not introduced. One open Draft per request, one recorder.

### D5 — Pre-review replacement / supersession lifecycle

A `Submitted`, not-`Reviewed` visit may be corrected by an **atomic replacement-copy**. `ActualWork`
gains nullable marker columns — `SupersededAtUtc`, `SupersededByActualWorkId` (self-reference,
account-composite FK, unique), `SupersededByAccountUserId`, `SupersessionReason` (required when
superseded, ≤2,000). **`Status` stays `Submitted`; no `Reviewed` or `Superseded` status value is
added** — the open-Draft partial unique index predicate and every owned-enum switch are unchanged.

**Successor contents.** The successor `ActualWork` is `Status = Draft` with the acting user as
recorder and author, a deep copy of every line's factual fields, snapshots, and performer, plus the
`VisitNote`, and **no** financial-resolution, disposition, or review rows.

**Zero-line source — persisted Draft contract for `Outcome` / `CompletionNote`.** Today the aggregate
sets `Outcome` and `CompletionNote` only inside `Submit`; there is no Draft setter or read path. This
upgrade adds one: `ActualWork` gains `SetZeroLineDisposition(outcome, completionNote)` — a
**Draft-only, current-recorder** mutation (same authorization + optimistic-concurrency contract as
line edits) that persists both fields on the Draft; the history and workspace reads project them for
a Draft. The replacement flow copies the source values into the successor Draft via this path, so a
replacement Draft can be reopened and edited across reloads. `Submit` continues to be the
authoritative validator — a zero-line submit still requires non-whitespace `CompletionNote` and a
defined `Outcome`, and a submit that adds lines simply ignores stored zero-line values. (Non-zero-line
replacements do not use this path.)

**Transaction ownership.** The **application service** (`ActualWorkReplacementApiService`,
Owner/Admin-gated) composes authorization, checks the no-open-Draft precondition, and constructs the
successor aggregate *from* the loaded source. The **persistence seam** owns one transaction that:
concurrency-checks the source, marks it superseded, adds the provided successor, re-evaluates the
request review signal (D7), saves, and commits. Core defines `ActualWork.Supersede(...)` (guards
only) and the successor factory; Application builds the successor; Infrastructure owns atomicity.

**Precondition.** The request has no open Draft (the partial unique index enforces it — a
pre-existing Draft returns the existing `DraftAlreadyOpenForRequest` conflict). Authority:
**Owner/Admin only for the pilot** (mirrors the office review gate); widening to the source recorder
is deferred.

### D6 — Replacement-chain rules

- **One direct successor per source.** The unique index on `SupersededByActualWorkId` plus a guard
  rejecting a source that is already superseded (`AlreadySuperseded`) together forbid **sibling
  replacements** — a source is replaced at most once.
- **A successor may itself be superseded before review.** Once submitted it is an ordinary
  `Submitted`-unreviewed visit; correcting it again forms a chain (`v1 → v2 → v3`), each link
  one-to-one. A successor still in `Draft` is discarded through the normal discard path, never
  superseded.
- **No row is ever deleted and no `Status` ever changes.** Supersession sets marker columns only.

### D7 — Review-signal reconciliation (corrects a stranding defect)

The **"open outstanding review" predicate** — used by `ResolveIfClearAsync` and by the operational
reads in D8 — becomes:

```
status = 'Submitted' AND reviewed_at_utc IS NULL AND superseded_at_utc IS NULL
```

A superseded visit is never reviewed; without this exclusion the request's
`ActualWorkNeedsOfficeReview` signal would remain active forever. (Signal **raising** does not use
this predicate — see below.)

**One reconciliation implementation, three callers.** `ResolveWorkSignalIfClearAsync` is today a
private method of `EfActualWorkReviewPersistence`, and the raise is a private `UpsertWorkSignalAsync`
of `EfActualWorkSubmissionPersistence`; the new supersession persistence cannot reach either. This
upgrade **extracts a dedicated signal-reconciliation seam**:

- **Application** declares `IActualWorkReviewSignalReconciliation` using only domain scalars —
  `RaiseAsync(accountId, requestId, nowUtc, ct)` and `ResolveIfClearAsync(accountId, requestId,
  nowUtc, ct)`. The interface **never mentions `DbContext`, `DatabaseFacade`, or transactions.**
- **Infrastructure** provides one implementation that receives the request-scoped `OpHaloDbContext`
  through DI. Submission, review, and supersession persistence resolve the **same scoped context**;
  when a caller has already opened a transaction on that context, EF automatically enlists the
  implementation's SQL in it, so atomicity is preserved without the interface exposing it.
- `RaiseAsync` performs the existing idempotent upsert/reopen of the signal row (it does **not**
  evaluate the "open outstanding review" predicate). `ResolveIfClearAsync` is the sole owner of that
  predicate, sharing one constant with the D8 operational reads.
- The raw SQL is **not** duplicated in a second persistence class. This predicate change does **not**
  touch `GetSubmittedVisitsForRequestAsync`.

Submission persistence calls `RaiseAsync` on submit; review persistence and the supersession
transaction call `ResolveIfClearAsync` after their write. Superseding the last outstanding visit on a
request clears the signal in the same commit.

### D8 — Superseded work: inert to mutation, excluded from operational reads, retained in history

**New error:** `ActualWorkErrors.Superseded` — code `"ActualWork.Superseded"`, message
"This actual work visit was replaced by a corrected copy." Maps to 409 (reconcilable — the client
reloads and follows the successor).

**Mutations** on a superseded source fail closed. Guard order: the existing **version-mismatch check
wins for a stale request** (client is out of date on more than just supersession); a *current*
request against a superseded source then returns `ActualWork.Superseded`. This covers mark-reviewed,
line financial resolution, zero-line no-charge disposition, and a second direct replacement attempt.

**Direct reads** of a superseded visit — the single-visit financial-detail read and any live
"open this ticket" read — return the same reconcilable `ActualWork.Superseded` outcome rather than
rendering it as a normal live ticket, so a stale deep link resolves to "reload / go to the
replacement" instead of a misleading live surface.

**Billing eligibility is not a mutation** — it is exclusion: a superseded visit never appears in the
review queue, the queue count, eligible-visit lists, or any future Billing Revision selection,
because those queries filter `superseded_at_utc IS NULL`.

**History is retained and unfiltered.** `GetSubmittedVisitsForRequestAsync` (the
`ActualWorkHistoryReadApiService` source) returns **all** submitted visits including superseded ones,
each flagged with `supersedes` / `supersededBy` lineage walkable in both directions. The operational
filter is added only to the review/financial/close/signal reads — never to the history source — so
the audit trail is never hidden.

### D9 — Actual Work Ticket Workspace

The office works a ticket in a **dedicated, capability-gated route** within the existing Requests
surface, using the Request List / Request Detail visual language — not a sheet or drawer, which
recreates the usability problem this upgrade exists to solve. Within the route:

- The **field / Draft region** is price-blind (`ActualWorkCapture`) and hosts the unchanged
  `ActualWorkComposer` interaction.
- The **office region** (`AccountingManage`) hosts the existing financial-resolution, no-charge
  disposition, review, totals, and blocker controls, placed beside the line each concerns.
- The two regions never share a line renderer; financial controls are never rendered on the
  price-blind field path (ADR-493 §5).
- Narrow screens fall back to the existing stacked Request Detail cards; no separate mobile
  workspace is built.

### D10 — Request-close eligibility gate

Resolved → Closed is blocked in the **authoritative status-change transition/transaction** when the
request has an open Actual Work Draft, or a `Submitted` visit that is not `Reviewed` and not
`Superseded`. Superseded and reviewed visits never block. `KeepRequestActionPolicy` carries only the
derived UI hint and a stable reason code; it is not the enforcement point. Progressive billing and
"work completed" remain separate lifecycle concerns (ADR-493 §2–3).

### D11 — Sequencing with Billing Revision

BL135 Batch 5 resumes only after BL136 slices 4c–4g land and pass their individual gates. ADR-493
§4's post-handoff `Replacement` correction consumes the `SupersededByActualWorkId` links defined
here; it does not introduce a separate mechanism.

### D12 — Local reset/seed is a developer tool, never an automated path

The local demo-data reset that precedes the strict `PerformedByAccountUserId` migration is an
explicit, local-only developer reset/seed tool run deliberately by a developer. It **must never run
from an application migration, startup, or deployment path** and is never a fallback the migration
invokes. Production has no Actual Work data and is untouched, so the strict non-null migration is
safe there. On a local database, the migration is expected to **fail loudly** if Actual Work rows
still exist — the developer resets first, then applies the migration, then reseeds demo tickets
through the normal capture flow so every seeded line has a deliberately chosen performer. The
migration never manufactures performer attribution.

## Consequences

- Field price blindness and submitted factual immutability are preserved.
- Recorder and performer are distinct, queryable facts; multi-technician and office-transcribed work
  are represented truthfully.
- A pre-review factual error has an auditable correction path that retains the erroneous record and
  its financial evidence.
- The aggregate review signal can no longer be stranded by a corrected visit.
- Request close reflects outstanding Actual Work.

## Non-goals

- Reopening, editing, or deleting submitted factual lines or captured financial snapshots.
- Field price/cost/margin visibility; accounting/QuickBooks scope; invoice, payment, or
  reconciliation claims.
- Shared concurrent Draft editing or multiple open Drafts per request.
- Migrating demo data as if it were meaningful historical attribution.
