# Build Log 065 — Session 11: Quick Capture Backend Contract

**Date:** 2026-06-26
**Branch:** main
**Baseline:** 864 unit · 14 arch · 676 integration = 1,554 total, 0 failures
**Next free ADR before this log:** ADR-369
**Current ADR after S11 decisions:** ADR-372

---

## Session Goal

Authenticated staff can create a normal Keep request immediately after a customer contact, with
required customer name, phone, summary, and source/channel. Staff-created requests start with
tracker sharing marked needed; the customer page token/link is always created. This is the Quick
Capture backend contract: creation contract + detail response in S11a; list indicators + share intent
clearing in S11b.

---

## Pre-Build Findings

### Existing creation path (stable)

`POST /keep/requests` → `CreateBusinessRequestBody` → `CreateBusinessRequestCommand` →
`CreateBusinessRequestService` → `KeepRequest.CreateByBusiness(...)` → `BusinessRequestCommitResult`.

The service is complete: auth stack, account access policy, feature gate (`OperatorQueue`), shared
input validator, customer find-or-create, page-token/reference-code retry loop, event commit. No
changes to auth stack, validation pipeline, or retry loop.

### What is already on `KeepRequest`

- `PageToken` (opaque string, set at creation, indexed unique)
- `Origin` enum (`Customer = 1, Business = 2`) — who submitted the request
- `CustomerPageLastViewedAtUtc` — adoption telemetry

### What is missing (S11 adds)

- `KeepRequestSource` enum — the V1 channel values
- `Source` property on `KeepRequest` — nullable; existing rows get `null`
- `NeedsShare` property on `KeepRequest` — bool; true when business-created and not yet shared
- `ClearNeedsShare()` domain method — called by share intent actions (S11b)

### Related files touched (confirmed via rg)

| File | Status |
|---|---|
| `KeepRequest.cs` | 1,230 lines; factory and property sections identified |
| `KeepRequestConfiguration.cs` | 205 lines; pattern: `HasConversion<string>()` for enums |
| `CreateBusinessRequestCommand.cs` | 5 lines; record with 4 fields |
| `CreateBusinessRequestService.cs` | 173 lines; creation path fully read |
| `KeepRequestDetailResult.cs` | ~60 fields; record |
| `KeepRequestDetailMapper.cs` | ToDetailResult builds full result; section identified |
| `CreateBusinessRequestBody.cs` | 5 lines; record with 4 fields |
| `KeepCreateBusinessRequestServiceTests.cs` | 459 lines |
| `KeepBusinessRequestApiTests.cs` | 470 lines |

---

## Locked Decisions (S11)

### ADR-369 — `KeepRequestSource` enum (V1 channel values)

Values: `Phone = 1`, `Voicemail = 2`, `Text = 3`, `Email = 4`, `WalkIn = 5`, `Referral = 6`,
`PublicIntake = 7`, `Other = 8`. Stored as string in DB (consistent with `Origin`, `Status`, etc.).

`Source` is nullable on `KeepRequest`. Existing rows (before this migration) get `null`. Null means
"captured before source tracking existed," not `Other`. `Other` is a valid explicit selection.

`Source` is required at the API for business-created requests. A missing or invalid source value
returns a validation error (not silently defaulted). The public API contract uses lowercase slugs
(`phone`, `voicemail`, `text`, `email`, `walk_in`, `referral`, `other`). Numeric enum values and
raw PascalCase enum names are not the preferred API contract. `PublicIntake`/`public_intake` is
blocked for staff and set only by customer-origin intake.

`Source` is not customer-facing. It is not included in the customer page response.

`PublicIntake` is set automatically by the domain factory on customer-origin requests; staff cannot
set it manually in the normal V1 flow.

### ADR-370 — `NeedsShare` flag

`NeedsShare` defaults to `true` for `Origin = Business`, `false` for `Origin = Customer`.
`CreateByBusiness` sets `NeedsShare = true`; `CreateByCustomer` sets `NeedsShare = false`.

`NeedsShare` clears via explicit in-app share intent actions only (S11b): native SMS/share handoff,
copy tracker link from share controls, or manual "Mark Shared." Reading the detail or viewing the
page does not clear it.

`ClearNeedsShare()` is a domain method added in S11a. It takes no parameters; it simply sets
`NeedsShare = false`. The service layer (S11b) records the share event type and actor. No timestamp
is stored on the flag itself in V1 (timestamp deferred).

### ADR-371 — S11 batch split

S11a and S11b are independently compiling vertical slices.

**S11a** adds Source/NeedsShare to the domain, creation contract, and detail response:
- 10 production files (see gate below; approved as the minimal compiling vertical slice because
  endpoint wiring and HTTP error mapping are required for the API contract)
- EF migration: add `source` (varchar 50, nullable) and `needs_share` (bool, not null, default false)
  to `keep_requests`. Current working assumption is no meaningful persisted Keep request data; if a
  non-empty database matters later, business-origin rows need explicit historical backfill to
  `needs_share = true`.

**S11b** adds list summary indicators and share intent clearing:
- `KeepRequestSummary` gets `NeedsShare: bool` and `Source: string?`
- `GetKeepRequestListService` updated to pass new fields
- New `POST /keep/requests/{id}/share-intent` endpoint + command + service
- Decisions deferred: share intent body shape, idempotency contract, whether copy-link needs a
  backend signal vs. client-only, NeedsShare effect on ranking/sort

---

## S11a — File-Level Gate

**Mutation families (3):**
1. Domain model enrichment — enum + entity properties + factory
2. Application contract — command + detail result + mapper
3. API contract — request body

**Production files (10; gate exception):**

The original gate listed 8 files but missed endpoint wiring and HTTP error mapping. S11a now counts
10 production files. This remains one narrow vertical slice: domain contract, application contract,
API request/response wiring, persistence config, and migration.

| # | File | Change |
|---|---|---|
| 1 | `src/OpHalo.Keep.Core/Entities/Enums/KeepRequestSource.cs` | NEW — enum Phone/Voicemail/Text/Email/WalkIn/Referral/PublicIntake/Other |
| 2 | `src/OpHalo.Keep.Core/Entities/KeepRequest.cs` | Add `Source`, `NeedsShare` properties; update `CreateByBusiness` (+ source param), `CreateCore` (source + NeedsShare init); add `ClearNeedsShare()`; `CreateByCustomer` auto-sets PublicIntake + NeedsShare=false via CreateCore |
| 3 | `src/OpHalo.Keep.Application/Requests/CreateBusinessRequestCommand.cs` | Add `KeepRequestSource Source` |
| 4 | `src/OpHalo.Keep.Application/Requests/CreateBusinessRequestService.cs` | Parse API source slugs; reject missing/invalid/public-intake values; pass to `CreateByBusiness` |
| 5 | `src/OpHalo.Keep.Application/Requests/KeepRequestDetailResult.cs` | Add `string? Source`, `bool NeedsShare` |
| 6 | `src/OpHalo.Keep.Application/Requests/KeepRequestDetailMapper.cs` | Map `Source` (string slug), `NeedsShare` |
| 7 | `src/OpHalo.Api/Keep/CreateBusinessRequestBody.cs` | Add `string? Source` |
| 8 | `src/OpHalo.Keep.Infrastructure/Persistence/Configurations/KeepRequestConfiguration.cs` | Add Source + NeedsShare EF config |
| 9 | `src/OpHalo.Api/Program.cs` | Pass `body.Source` to command |
| 10 | `src/OpHalo.Api/Helpers/ErrorHttpMapper.cs` | Map source validation errors to 400 |

**Test files:**

| File | Change |
|---|---|
| `tests/OpHalo.UnitTests/Keep/KeepCreateBusinessRequestServiceTests.cs` | Add: Source required; invalid/public-intake source rejected; NeedsShare=true on created request; Source slug in detail response |
| `tests/OpHalo.IntegrationTests/Api/KeepBusinessRequestApiTests.cs` | Add: Source field in create body; Source + NeedsShare in response |

**Christian runs (not Claude):**
```
dotnet ef migrations add QuickCaptureSourceAndNeedsShare \
  --project src/OpHalo.Foundation.Infrastructure \
  --startup-project src/OpHalo.Keep.Infrastructure \
  --context OpHaloDbContext

dotnet ef database update \
  --project src/OpHalo.Foundation.Infrastructure \
  --startup-project src/OpHalo.Keep.Infrastructure \
  --context OpHaloDbContext
```

Migrations live in `OpHalo.Foundation.Infrastructure`, but Keep model configuration is available
only through the `KeepDesignTimeDbContextFactory` in `OpHalo.Keep.Infrastructure`. Using
Foundation.Infrastructure as both project and startup creates a Foundation-only model and triggers
pending-model-change errors against snapshots that include Keep tables.

---

## S11a Implementation Notes

### `KeepRequestSource.cs` (new)
```
Phone = 1, Voicemail = 2, Text = 3, Email = 4,
WalkIn = 5, Referral = 6, PublicIntake = 7, Other = 8
```

### `KeepRequest.cs` changes
- Add: `public KeepRequestSource? Source { get; private set; }`
- Add: `public bool NeedsShare { get; private set; }`
- Add `source` param to `CreateByBusiness`; validate it is not `PublicIntake` (business cannot
  manually set public intake channel)
- In `CreateCore`: accept `KeepRequestSource? source` and `bool needsShare`; set on new instance
- `CreateByBusiness` passes: `source = source, needsShare = true`
- `CreateByCustomer` passes: `source = KeepRequestSource.PublicIntake, needsShare = false`
- Add `ClearNeedsShare()`: `NeedsShare = false;`

### `CreateBusinessRequestService.cs` guard
After input validation, before customer lookup, parse the source slug explicitly and reject
`public_intake`/`PublicIntake` with a validation error. Do not rely on `Enum.TryParse` for the
public contract because it accepts numeric strings and PascalCase enum names.

### `CreateBusinessRequestBody.cs`
`string? Source` — nullable at the API level; service validates it is present and valid.

### `KeepRequestDetailMapper.cs` source slug map
String slug mirrors enum name in lowercase snake_case: `phone`, `voicemail`, `text`, `email`,
`walk_in`, `referral`, `public_intake`, `other`. Null when source is null (legacy rows).

### `KeepRequestConfiguration.cs` additions
```csharp
builder.Property(x => x.Source)
    .HasConversion<string>()
    .HasMaxLength(50);  // nullable — no IsRequired()

builder.Property(x => x.NeedsShare)
    .IsRequired()
    .HasDefaultValue(false);  // existing rows default false; domain sets business-created true
```

Generated migration:
`src/OpHalo.Foundation.Infrastructure/Migrations/20260627003337_QuickCaptureSourceAndNeedsShare.cs`

---

## Deferred past S11b

- NeedsShare in ranking/sort
- Source filter on list/search
- `CreateKeepPublicIntakeService` — no change needed (domain factory auto-sets PublicIntake/NeedsShare=false)

---

## S11b — Mini-Brief (Pre-build locked 2026-06-26)

**Scope:** List summary indicators + share intent clearing.

### Locked Decisions

| # | Decision | Answer |
|---|---|---|
| 1 | Endpoint | `POST /keep/requests/{id}/share-intent` |
| 2 | Request body | `{ "method": "copy_link" \| "native_share" \| "manual_mark_shared" }` |
| 3 | Who can call | Owner / Admin / Operator with row visibility and write access; Viewer → 403 |
| 4 | OffSeason | Blocked → 403 (same gate as other mutation endpoints) |
| 5 | Idempotent | Yes — if `NeedsShare` already false, return 204 without error or re-write |
| 6 | What it writes | Clears `NeedsShare = false` on the entity; records a domain event (name: `ShareIntentRecorded`); persists actor + method + timestamp via existing event/audit infrastructure |
| 7 | Copy link clears | Yes — `method: copy_link` is a valid share intent signal |
| 8 | List summary fields | Add `NeedsShare: bool` and `Source: string?` to `KeepRequestSummary` |
| 9 | Ranking / sort | Deferred — `NeedsShare` does not affect sort order in S11b |

### Mutation Families (2)

1. **List summary enrichment** — `KeepRequestSummary` + `GetKeepRequestListService`
2. **Share intent clearing** — command + service + endpoint + auth + idempotency

### File-Level Gate

**Production files (7):**

| # | File | Change |
|---|---|---|
| 1 | `src/OpHalo.Keep.Application/Requests/KeepRequestSummary.cs` | Add `NeedsShare: bool`, `Source: string?` |
| 2 | `src/OpHalo.Keep.Application/Requests/GetKeepRequestListService.cs` | Map new summary fields |
| 3 | `src/OpHalo.Keep.Application/Requests/ClearShareIntentCommand.cs` | NEW — `record` with `RequestId`, `ActorId`, `Method` |
| 4 | `src/OpHalo.Keep.Application/Requests/ClearShareIntentService.cs` | NEW — load request, auth gate (OffSeason/Viewer → error), idempotency check, call `ClearNeedsShare()`, emit `ShareIntentRecorded` |
| 5 | `src/OpHalo.Keep.Core/Events/ShareIntentRecorded.cs` | NEW — domain event with `RequestId`, `ActorId`, `Method`, `OccurredAt` |
| 6 | `src/OpHalo.Api/Keep/ShareIntentBody.cs` | NEW — `record` with `string Method` |
| 7 | `src/OpHalo.Api/Program.cs` | Wire `POST /keep/requests/{id}/share-intent` → `ClearShareIntentService` |

**Test files (3):**

| File | Change |
|---|---|
| `tests/OpHalo.UnitTests/Keep/ClearShareIntentServiceTests.cs` | NEW — Viewer blocked; OffSeason blocked; already-cleared idempotent 204; valid call clears + emits event; invalid method → 400 |
| `tests/OpHalo.UnitTests/Keep/GetKeepRequestListServiceTests.cs` | Add: NeedsShare + Source propagate to summary |
| `tests/OpHalo.IntegrationTests/Api/KeepBusinessRequestApiTests.cs` | Add: share intent endpoint auth matrix; idempotent call; list response includes NeedsShare/Source |

**Total: 7 production + 3 test = 10 changed files. Within gate.**

### Error Codes

| Code | HTTP | Condition |
|---|---|---|
| `keep_request.share_intent.offseason_blocked` | 403 | Account is OffSeason |
| `keep_request.share_intent.viewer_blocked` | 403 | Actor is Viewer |
| `keep_request.share_intent.invalid_method` | 400 | Method string not in allowed set |

### Verification (S11b)

```
dotnet build --no-restore -v q
dotnet test tests/OpHalo.UnitTests --no-restore -v q \
  --filter "FullyQualifiedName~ClearShareIntent|FullyQualifiedName~GetKeepRequestList"
dotnet test tests/OpHalo.IntegrationTests --no-restore -v q \
  --filter "FullyQualifiedName~KeepBusinessRequestApi"
dotnet test tests/OpHalo.UnitTests --no-restore -v q
dotnet test tests/OpHalo.ArchitectureTests --no-restore -v q
```

---

## Verification (S11a)

```
dotnet build --no-restore -v q
dotnet test tests/OpHalo.UnitTests --no-restore -v q \
  --filter "FullyQualifiedName~KeepCreateBusinessRequest"
dotnet test tests/OpHalo.IntegrationTests --no-restore -v q \
  --filter "FullyQualifiedName~KeepBusinessRequestApi"
dotnet test tests/OpHalo.UnitTests --no-restore -v q  # full unit suite
dotnet test tests/OpHalo.ArchitectureTests --no-restore -v q
```
