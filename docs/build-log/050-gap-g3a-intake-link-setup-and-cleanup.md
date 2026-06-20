# Build Log 050 — Gap G3a: Intake-Link Setup + Continuity Alias Removal

**Session:** Gap G3a
**Phase:** Pre-Session 6 gap closure
**Gaps closed:** GAP-001 (intake-link setup), GAP-014 (Continuity alias + ignored email field)

---

## What changed

### GAP-001: Keep intake-link setup endpoints

**Why:** Owner/Admin accounts had no API surface to acquire or rotate their public intake URL. The public intake endpoint (`/keep/public-intake/token/{token}`) existed, but there was no way to obtain the token in the first place.

**Files created:**
- `src/OpHalo.Keep.Application/IntakeSetup/IKeepIntakeSetupPersistence.cs` — persistence contract + `EnsureIntakeLinkCommitResult` enum
- `src/OpHalo.Keep.Application/IntakeSetup/KeepIntakeSetupResults.cs` — `KeepIntakeSetupStatusResult`, `KeepIntakeSetupEnsureResult`, `KeepIntakeSetupReplaceResult`
- `src/OpHalo.Keep.Application/IntakeSetup/KeepIntakeSetupService.cs` — `GetStatusAsync`, `EnsureAsync`, `ReplaceAsync`
- `src/OpHalo.Keep.Infrastructure/Persistence/KeepIntakeSetupPersistence.cs` — EF Core implementation
- `tests/OpHalo.IntegrationTests/Api/KeepIntakeSetupApiTests.cs` — 16 integration tests

**Files modified:**
- `src/OpHalo.Keep.Core/Entities/KeepPublicIntakeLink.cs` — added optional `createdByUserId` to `Create`, optional `modifiedByUserId` to `Revoke` (backward-compatible; existing callers unchanged)
- `src/OpHalo.Keep.Core/Errors/KeepPublicIntakeLinkErrors.cs` — added `NoActiveLink`
- `src/OpHalo.Api/Program.cs` — registered `KeepIntakeSetupService` + `IKeepIntakeSetupPersistence`; added three endpoints
- `src/OpHalo.Api/Helpers/ErrorHttpMapper.cs` — explicit entry for `KeepPublicIntakeLink.NoActiveLink` → 404
- `tests/OpHalo.UnitTests/Keep/KeepPublicIntakeLinkTests.cs` — added 4 audit-field tests

**Endpoints added:**
- `GET /keep/setup/intake` — returns `{ hasActiveLink, publicSlug, createdAtUtc }`; never includes a raw token
- `POST /keep/setup/intake/ensure` — idempotent; returns raw token only when this call created the link; concurrent loser returns `created=false, rawToken=null`
- `POST /keep/setup/intake/replace` — transactional (two SaveChanges inside one DB transaction); returns new raw token + `staleLinksWarning=true`

**Auth:** `keep.settings.manage` (Admin/Owner only); `RequestImplementsAllowedInOffSeason: true`; checks only `decision.IsBlocked` (not `IsReadOnly`); gated by `FeatureKeys.Keep.PublicIntake`.

**Slug generation:** kebab-case from business name (lowercase, non-alphanumeric → `-`, collapse, truncate at 60); numeric suffix loop for collisions.

**Audit fields:** initial link sets `CreatedByUserId`; replacement sets old link's `ModifiedByUserId` + `RevokedAtUtc`, and successor's `CreatedByUserId`. No migration needed (`BaseEntityConfiguration` already maps these columns).

**Concurrency:**
- Ensure: catches `ix_keep_public_intake_links_account_active` constraint (23505) → `AlreadyExists`; re-reads winning link, returns no token.
- Ensure: catches `ix_keep_public_intake_links_active_slug` constraint → `SlugCollision`; retries up to 5 times with fresh slug.
- Replace: two `SaveChanges` inside `BeginTransactionAsync`/`CommitAsync`; transaction auto-rolls back on failure, preserving the old active link.

### GAP-014: Remove Continuity alias and ignored email field

**Why:** `/continuity/public-intake/...` was an undeployed alias that added noise without value. `emailNotificationsEnabled` in `PublicIntakeRequest` was accepted but silently ignored, which is a deceptive API surface.

**Files modified:**
- `src/OpHalo.Api/Program.cs` — removed `app.MapPost("/continuity/public-intake/token/...")` route
- `src/OpHalo.Api/Keep/PublicIntakeRequest.cs` — removed `bool? EmailNotificationsEnabled` field
- `tests/OpHalo.IntegrationTests/Api/KeepIntakeApiTests.cs` — legacy alias test changed to assert 404; added `PublicIntake_WithoutEmailNotificationsEnabled_Returns201`

---

## Tests

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| Unit | 536 | 540 | +4 |
| Architecture | 14 | 14 | 0 |
| Integration | 370 | 387 | +17 |
| **Total** | **920** | **941** | **+21** |

---

## Exit gate

- `dotnet build` → 0 errors, 0 warnings
- `dotnet test` → 941 passed, 0 failed, 0 skipped
- Architecture tests green — no boundary violations introduced
- G3a decisions locked: audit fields (no migration), transactional replace, OffSeason access, concurrent ensure handling

---

## Risks / Next

- **G3b** (GAP-002): Authenticated business-created requests — `CreateBusinessRequestService` returns `KeepRequestDetailResult` directly (D3 resolved: `PageToken` already included).
