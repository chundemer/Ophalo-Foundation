# Build Log 056 — Gap G5: Entity-Wide KeepRequest Optimistic Concurrency

**Gap:** GAP-004  
**Decisions:** ADR-330–335  
**Entering baseline:** 1109 tests (post-G4/G4e-3, build-log/055)  
**Final baseline:** 1182 tests (619 unit · 14 architecture · 549 integration)

---

## Locked model and contract

- `KeepRequest.ConcurrencyVersion` is a required application-managed `Guid`, stored as PostgreSQL
  `uuid`, configured with `IsConcurrencyToken()` and `ValueGeneratedNever()`.
- New requests receive `Guid.NewGuid()`. Existing rows were backfilled with `gen_random_uuid()` via
  the two-phase migration before the column was made non-null.
- The request row, its events, and its participants form one concurrency aggregate. Every successful
  non-no-op write rotates the version in the same atomic commit. Reads, failures, and valid
  idempotent no-ops do not rotate. No append-only exclusions.
- Existing-request mutations require exactly one strict `X-Keep-Request-Version` GUID-D header.
  Missing → `400 KeepRequest.ExpectedVersionRequired`; blank, malformed, empty, duplicate,
  wildcard, quoted, or braced → `400 KeepRequest.ExpectedVersionInvalid`.
- Stale pre-mutation token and save-time `DbUpdateConcurrencyException` both map to
  `409 KeepRequest.RequestChanged` (`The request changed. Refresh and try again.`). No retry,
  merge, or lock. Conflict and error responses never disclose the current version.
- Authenticated row access precedes expected-version comparison; invisible/cross-account requests
  remain 404 before any version check.
- `RotateConcurrencyVersion()` on `KeepRequest`; `KeepRequestCommitResult` enum (`Committed` |
  `Conflict`); persistence catches only `DbUpdateConcurrencyException`.

## Migration

`20260622093644_KeepG5ConcurrencyVersion` — three-step: (1) adds the column nullable, (2) backfills
every existing row with `gen_random_uuid()` (unique, nonempty), (3) makes the column non-null. EF
maps the column with `IsConcurrencyToken()` and `ValueGeneratedNever()`.

Migration upgrade behavior and unique nonempty backfill verified in
`KeepG5ConcurrencyVersionMigrationTests` (1 test, 3 assertions).

## Read surfaces returning `version`

All read/success responses expose the opaque `version` field (no extra query):

- Authenticated detail (`KeepRequestDetailResult`)
- Standard list rows (`KeepRequestSummary`)
- Available rows (`KeepRequestAvailableRow` / `AvailableItemBody`)
- Business-created detail
- Customer page (`KeepCustomerPageResult` / `KeepCustomerPageContext`)
- Every successful mutation response (changing write returns rotated version; no-op returns
  unchanged version)

## G5a — Foundation (commit c7435b2 and 2c8390a)

- `KeepRequest.ConcurrencyVersion`, factory init, EF concurrency-token mapping, migration.
- `ExpectedVersionRequired`, `ExpectedVersionInvalid`, `RequestChanged` errors + HTTP mapping.
- Strict `X-Keep-Request-Version` parser (14 cases: accepts only GUID-D non-empty; rejects all
  malformed/duplicate/wildcard/quoted/braced forms).
- `version` added to all five read projections; mapper and guard wired; expired tombstone returns
  null.
- 6 version-contract integration facts across 5 fixtures; 3 migration tests; 14 parser unit tests.

## G5b — Operational mutations (commit 7b4796f)

Status change, business update, internal note, external contact, attention acknowledgement, and
feedback review. All routes enforce expected version after row visibility, rotate on actual writes,
preserve version on no-op, and map `Committed`/`Conflict` exhaustively. `RotateConcurrencyVersion()`
and `KeepRequestCommitResult` introduced here. One typo fix (`KeepRequestEvent.KeepRequestId` →
`RequestId` in race test).

## G5c — Participation mutations (commits ab6399b / 4998aab / 19099e3 / 9c2df85)

Four independently compiling slices:

- **G5c-1:** Responsible set/clear. Introduced the versioned `CommitParticipationAsync` overload
  accepting the tracked request; retained the legacy overload for unmigrated callers.
- **G5c-2:** Managed watcher add/remove.
- **G5c-3:** Self-watch/unwatch; preserves ParticipationEntry/MyWork scope rules.
- **G5c-4:** Mute/unmute; removed the zero-caller legacy participation-commit overload from
  interface, EF implementation, and unit-test fake.

All participation routes rotate atomically; idempotent no-ops preserve the version; missing/stale
tokens return the correct 400/409 errors with no side effects.

## G5d — Customer writes, cross-path race, and completion (commits bb8010e / 3fb154f / 0a9d570)

- **G5d-1:** Customer-message route family (six aliases). `IKeepCustomerWritePersistence.CommitAsync`
  changed to return `Task<KeepRequestCommitResult>`; EF implementation rotates via
  `RotateConcurrencyVersion()` before save.
- **G5d-2:** Customer-feedback route. `CommitFeedbackAsync` changed from `Task` to
  `Task<KeepRequestCommitResult>`; same rotation and conflict handling.
- **G5d-3:** Operator/customer cross-path race test and documentation. No production source changes.

## Race proofs

Three real two-DbContext first-writer-wins tests, each using independent contexts loaded from the
same seed version:

1. **Operate/operate** (`OperatePersistence_ConcurrentCommits_FirstWins_SecondReturnsConflict`,
   G5b): two `EfKeepRequestOperatePersistence.CommitAsync` calls; winner event persists; loser
   event rolled back; request version equals winner version.

2. **Participation/request** (`ParticipationCommit_FirstWriterWins_ParticipantAndEventRolledBack`,
   G5c-4): `EfKeepRequestOperatePersistence.CommitParticipationAsync` wins a mute; the loser
   `CommitParticipationAsync` attempts an unwatch and returns `Conflict`. Participant row is still
   Watching with `NotificationsEnabled=false`; losing SelfUnwatched event absent.

3. **Operator/customer** (`OperatorAndCustomerRace_OperatorWins_CustomerEventAndStateRolledBack`,
   G5d-3): `EfKeepRequestOperatePersistence.CommitAsync` wins a business update on a
   customer-origin request; `EfKeepCustomerWritePersistence.CommitAsync` loses a customer message.
   Fresh-context assertions:
   - `ConcurrencyVersion` equals operator winner version.
   - `LastBusinessActivityAt`, `FirstRespondedAtUtc`, `FirstResponderAccountUserId`, and
     `FirstResponseEventId` reflect the winning operator write.
   - `LastCustomerActivityAt` remains the intake seed value (`Now`), not the losing message
     timestamp.
   - `AttentionLevel.None`, `WaitingDirection.None`, null reason/timestamps — attention reflects
     the winning business response, not the losing customer message.
   - Winner business-update event exists; losing customer-message event does not (both checked by
     ID, not content).

## Stale/save-time conflict behavior

Both pre-mutation stale tokens and save-time `DbUpdateConcurrencyException` map to the same
`409 KeepRequest.RequestChanged` response with no side effects. Conflict responses never reveal
the current version, actor, or request state. No-op paths do not call commit.

## Verification

- `KeepPersistenceProofTests`: 26 passed (3 race tests + 23 existing regressions).
- Full suite: 1182 passed (619 unit · 14 architecture · 549 integration).
- `git diff --check`: clean.

## Explicit exclusions

No auto-retry, merge, pessimistic/distributed lock, event sourcing, frontend draft recovery,
API route-versioning implementation, or unrelated refactor included in G5.

## Commit ledger

| Commit | Slice |
|---|---|
| `c7435b2` | G5a-1: entity, EF mapping, migration, errors, header parser |
| `2c8390a` | G5a-2: version on read projections and response contracts |
| `7b4796f` | G5b: operational mutations |
| `ab6399b` | G5c-1: responsible set/clear |
| `4998aab` | G5c-2: managed watcher add/remove |
| `19099e3` | G5c-3: self-watch/unwatch |
| `9c2df85` | G5c-4: mute/unmute, legacy overload removal, participation race |
| `bb8010e` | G5d-1: customer-message concurrency |
| `3fb154f` | G5d-2: customer-feedback concurrency |
| `0a9d570` | G5d-3: cross-path race, build-log/056, ADR-330–335 implemented, session-log G6 |
