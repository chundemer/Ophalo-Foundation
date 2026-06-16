# Phase 8-B1-α — Keep Domain Model + EF Schema

**Status:** Complete.
**Build-log preceding this:** 024-phase-8-reference-audit-and-discovery.md
**Date:** 2026-06-16

---

## Purpose

Extend the Keep domain model for the full Phase 8 data foundation: actor fields on events,
message intent, communication channel, request origin, first-response fields, attention fields,
terminal feedback fields, plus two new entities (`KeepResponsePolicy`, `KeepRequestParticipant`).
Update EF configurations and generate the migration. No application services, no API endpoints,
no new integration tests — schema and domain only.

---

## What Was Built

**Keep.Core — new enums (9 files):**
- `Entities/Enums/ActorType.cs` — `Customer=1`, `AccountUser=2`, `System=3`
- `Entities/Enums/MessageIntent.cs` — `GeneralMessage=1`, `Question=2`, `UpdateRequest=3`, `ScheduleChangeRequest=4`, `ChangeOrCancelRequest=5`, `Complaint=6`, `BusinessUpdate=7`
- `Entities/Enums/CommunicationChannel.cs` — `InApp=1`, `Phone=2`, `Sms=3`, `Email=4`, `InPerson=5`, `Other=6`
- `Entities/Enums/KeepRequestOrigin.cs` — `Customer=1`, `Business=2`
- `Entities/Enums/AttentionLevel.cs` — `None=0`, `Waiting=1`, `NeedsAttention=2`, `Overdue=3`
- `Entities/Enums/WaitingDirection.cs` — `None=0`, `Business=1`, `Customer=2`
- `Entities/Enums/AttentionReason.cs` — `CustomerMessage=1` through `UnresolvedFeedback=7`
- `Entities/Enums/PriorityBand.cs` — `Standard=1`, `Priority=2`
- `Entities/Enums/ParticipationType.cs` — `Responsible=1`, `Watching=2`

**Keep.Core — modified enum (1 file):**
- `Entities/Enums/KeepRequestEventType.cs` — action-oriented vocabulary with explicit int values.
  Removed `OperatorReplied`(3) and `CustomerReplied`(4); added `MessageAdded=3`,
  `InternalNoteAdded=7`, `AttentionAcknowledged=8`. Gap at 4 is intentional (ADR-094).

**Keep.Core — modified entities (2 files):**
- `Entities/KeepRequestEvent.cs` — added `ActorType` (required), `ActorAccountUserId?`,
  `ActorDisplayName?`, `MessageIntent?`, `CommunicationChannel?`. `CreateRequestCreated`
  factory sets `ActorType=System`.
- `Entities/KeepRequest.cs` — added `Origin` (default `Customer`, ADR-095);
  renamed `ClosedAtUtc` → `TerminatedAtUtc` (ADR-096); added first-response fields
  (`FirstResponseDueAtUtc?`, `FirstRespondedAtUtc?`, `FirstResponderAccountUserId?`,
  `FirstResponseEventId?`); added attention fields (`AttentionLevel`, `WaitingDirection`,
  `AttentionReason?`, `PriorityBand`, `AttentionSinceUtc?`, `NextAttentionAtUtc?`,
  `AttentionClearedAtUtc?`, `AttentionClearedByAccountUserId?`, `AttentionClearReason?`);
  added terminal feedback fields (`FeedbackWasResolved?`, `FeedbackComment?`,
  `FeedbackSubmittedAtUtc?`). `Create()` initializes attention to `None/None/Standard`
  (ADR-098); `origin` parameter is optional defaulting to `Customer` (ADR-095).

**Keep.Core — new entities (2 files):**
- `Entities/KeepResponsePolicy.cs` — account-level response SLA policy. Inherits `BaseEntity`
  (ADR-097). Fields: `AccountId`, `FirstResponseTargetMinutes`, `StandardResponseTargetMinutes`,
  `PriorityResponseTargetMinutes`, `BusinessHoursOnly?`. Factory validates positive targets.
- `Entities/KeepRequestParticipant.cs` — Responsible/Watching participation record. Fields:
  `RequestId`, `AccountId`, `AccountUserId`, `ParticipationType`, `NotificationsEnabled`,
  `AttachedAtUtc`, `DetachedAtUtc?`. `IsActive` is computed (ignored by EF).

**Keep.Infrastructure — updated EF configurations (2 files):**
- `KeepRequestConfiguration.cs` — added all new columns with appropriate lengths/conversions;
  attention fields as string enums; added `ix_keep_requests_account_attention` composite index.
- `KeepRequestEventConfiguration.cs` — added `ActorType` (required), `ActorDisplayName?`,
  `MessageIntent?`, `CommunicationChannel?`.

**Keep.Infrastructure — new EF configurations (2 files):**
- `KeepResponsePolicyConfiguration.cs` — unique index on `AccountId` (ADR-097).
- `KeepRequestParticipantConfiguration.cs` — unique index on `(request_id, account_user_id)`;
  composite index on `(account_id, account_user_id)`; index on `request_id`.

**Foundation.Infrastructure/Migrations:**
- `20260616150747_Phase8KeepDataModel` — renames `closed_at_utc` → `terminated_at_utc`;
  adds all new columns to `keep_requests` and `keep_request_events`; creates
  `keep_request_participants` and `keep_response_policies` tables with correct indexes.

**Tests (mechanical fix, 1 file):**
- `UnitTests/Keep/KeepRequestTests.cs` — `ClosedAtUtc` → `TerminatedAtUtc` reference updated.

---

## Key Decisions Applied

| ADR | Decision | Applied |
|-----|----------|---------|
| ADR-094 | `KeepRequestEventType` action-oriented vocabulary; explicit int values; gap at 4 | ✓ |
| ADR-095 | `KeepRequest.Create()` `origin` optional, defaults to `Customer` | ✓ |
| ADR-096 | `ClosedAtUtc` → `TerminatedAtUtc` covers both terminal states | ✓ |
| ADR-097 | `KeepResponsePolicy` inherits `BaseEntity`; unique index on `AccountId` | ✓ |
| ADR-098 | `Create()` initializes attention to `None/None/Standard`; B2 wires attention behavior | ✓ |

---

## Build State

- `dotnet build` → 0 errors, 0 warnings
- Architecture tests → 14/14 passing
- Unit tests → 280/280 passing
- Integration tests → 126/126 passing (schema-drop + MigrateAsync picks up new migration)
- Total → 417/417 passing

---

## Exit Gate

Schema is locked. All existing tests pass against the new schema. Ready for B1-β.

---

## Risks / Watch-outs for B1-β

- `ActorType` is required on `KeepRequestEvent`. Any new event factory methods must set it
  explicitly — no uninitialized default will compile cleanly.
- The attention index `ix_keep_requests_account_attention` sorts on `attention_since_utc`,
  which will be null for B1-α rows. B2 must set this field when attention is created.
- `KeepRequestParticipant` unique index on `(request_id, account_user_id)` means a detach +
  re-attach for the same user must handle the index (soft detach via `DetachedAtUtc`, or
  delete the row and insert a new one). B4 must decide.
- `KeepResponsePolicy` has no default seeding — if no policy exists for an account, B2 must
  handle the fallback policy case (default SLA values) without crashing.
