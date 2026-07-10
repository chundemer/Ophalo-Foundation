# Build Log 079 — GAP-006: Service Location Add/Edit for Authenticated Staff

**Session:** S23
**Date:** 2026-07-10
**Status:** Resolved — GAP-006 closed

## What Was Built

Authenticated staff can now add or edit a service location on any Keep request after creation,
including business/staff-created requests that were intentionally saved without one.

## Files Changed

### Production (8 files)

| File | Change |
|------|--------|
| `src/OpHalo.Keep.Core/Entities/Enums/KeepRequestEventType.cs` | Added `ServiceLocationChanged = 16` |
| `src/OpHalo.Keep.Core/Entities/KeepRequest.cs` | Added `SetServiceLocation()` domain method |
| `src/OpHalo.Keep.Core/Entities/KeepRequestEvent.cs` | Added `CreateServiceLocationChanged()` factory |
| `src/OpHalo.Keep.Application/Requests/UpdateServiceLocationService.cs` | New application service (PUT handler) |
| `src/OpHalo.Keep.Application/Requests/KeepRequestDetailMapper.cs` | Added `service_location_changed` event type mapping |
| `src/OpHalo.Api/Program.cs` | DI registration + `PUT /keep/requests/{id}/service-location` route + `UpdateServiceLocationBody` record |
| `web/ophalo-app/src/lib/apiClient.ts` | `UpdateServiceLocationBody` interface + `updateServiceLocation` method |
| `web/ophalo-app/src/pages/RequestDetail.tsx` | `ServiceLocationModal` component + service location display section (all sources, not just public_intake) |

### Tests (2 files)

| File | Tests |
|------|-------|
| `tests/OpHalo.UnitTests/Keep/KeepRequestServiceLocationTests.cs` | 10 unit tests |
| `tests/OpHalo.IntegrationTests/Api/KeepUpdateServiceLocationApiTests.cs` | 12 integration tests |

## Behavior

**Backend route:** `PUT /keep/requests/{id}/service-location`
- Authenticated only (`RequireAuthorization`)
- Versioned: requires `X-Keep-Request-Version` header (Guid)
- Body: `{ addressLine1, addressLine2?, city, state, zip? }`
- Authorization: Owner/Admin → AccountWide scope; Operator → MyWork scope (Responsible participant required)
- Validation: addressLine1, city, state required; state normalized to uppercase and checked against ValidUsStateCodes (51 US codes including DC); addressLine2 and zip optional (null if blank)
- Domain method `SetServiceLocation()` validates required fields, normalizes whitespace and state casing, updates `LastBusinessActivityAt`
- Audited with `ServiceLocationChanged` event (Internal visibility, AccountUser actor, no content field)
- Allowed on all lifecycle states including terminal (staff may correct location post-close)
- Returns full request detail result on success (same shape as detail GET)

**Frontend (RequestDetail.tsx):**
- Service location section now appears for ALL request sources (was previously `public_intake` only)
- When location present: shows formatted address + "Edit" button (if `canAddInternalNote` — proxy for write access)
- When location absent: shows "Service location needed" italic text + "Add location" button (if `canAddInternalNote`)
- Both open `ServiceLocationModal`: state dropdown (51 US states), address fields, city/zip grid
- 409 conflict triggers disabled/locked state with STATUS_CONFLICT_MESSAGE
- On success: calls `onDetailUpdated` to refresh parent request detail state
- Event type label: `service_location_changed → "Service location updated"`

**Privacy:** Service location fields are not projected by the customer page mapper and do not appear in `GET /keep/r/{pageToken}` responses.

## Decisions

- **`canAddInternalNote` as proxy for write access** — avoids adding `CanUpdateServiceLocation` to `AvailableActionsMetadata`, `ActionDecision`, and `KeepRequestActionPolicy` (would have added 3 extra files beyond the batch gate). The permission conditions are identical for both operations.
- **US state validation in application layer** — domain validates required field presence; app service validates state code validity (same pattern as public intake).
- **No lifecycle restriction** — `SetServiceLocation()` is allowed on all states including Resolved and Closed. Staff may need to correct an address after a job closes.

## Tests Run

```
Unit:        10/10 passed — KeepRequestServiceLocationTests
Integration: 12/12 passed — KeepUpdateServiceLocationApiTests
```

Integration tests cover: owner success (fields + event round-trip), missing addressLine1 → 422, missing city → 422, invalid US state → 422, stale version → 409, missing version header → 400, malformed version header → 400, operator with row access → 200, operator without row access → 404, viewer → 403, anonymous → 401, customer page does not expose service location → 200 with no address fields.

## Residual Notes

- The "Service location needed" cue does not yet feed into the Needs Attention attention policy. The spec notes this as optional ("if attention policy supports it"); deferring to a future session if needed.
- Quick Capture (native mobile) path was not changed; it already accepts null service location fields via `CreateByBusiness`.
