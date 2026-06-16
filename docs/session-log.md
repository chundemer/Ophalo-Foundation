# Session Log — OpHalo Foundation

**Last updated:** 2026-06-16
**Next session tier:** Tier 2 — Phase 5E-C API + Integration Tests (Pre-work complete)
**Branch:** `main` (no remote yet)

---

## Phase 5E-B — COMPLETE

Member management: Application + Infrastructure layer. 389/389 tests passing.

### What was built

**Foundation.Core:**
- `Entities/Accounts/Errors/MemberErrors.cs` — NEW: `Forbidden`, `NotFound`, `CannotModifySelf`, `CannotModifyOwner`, `PrimaryOwnerProtected`, `OwnerLimitReached`, `LastOwner`, `InvalidRole`, `InvalidStatusTransition`, `SeatLimitReached`, `PreviouslyRemoved`
- `Entities/Accounts/AccountUser.cs` — added `ChangeRole(newRole)`, `Reactivate()`, `RestoreInvite(tokenHash, expiresAtUtc, nowUtc)` domain methods; `Remove()` now unconditionally clears `InviteTokenHash`/`InviteExpiresAtUtc` (idempotent); `Reactivate()` also clears invite state

**Foundation.Application:**
- `Abstractions/Security/IAccountSessionService.cs` — added `RevokeAllSessionsByAccountUserId(accountId, accountUserId, ct)`
- `Members/InviteDeliveryMode.cs` — NEW: `Email = 1`, `ManualShare = 2`
- `Members/IMemberManagementPersistence.cs` — NEW: `GetMemberListContextAsync`, `GetMemberManagementContextAsync`, `CommitAsync`; `MemberListContext`, `MemberListItem`, `MemberManagementContext` records
- `Members/MemberManagementService.cs` — NEW: `ListMembersAsync`, `ChangeRoleAsync`, `SuspendAsync`, `ReactivateAsync`, `RemoveAsync`, `ResendInviteAsync(targetId, deliveryMode, ct)` → `Result<ResendInviteResult>`; `ResendInviteResult(DeliveryMode, InviteUrl?)`, `ListMembersResponse`, `MemberItem` response types
- `Auth/SendInviteService.cs` — Removed members now return `MemberErrors.PreviouslyRemoved` instead of `InviteErrors.AlreadyActive` (suggestedAction metadata deferred to 5E-C)

**Foundation.Infrastructure:**
- `Security/AccountSessionService.cs` — implemented `RevokeAllSessionsByAccountUserId` via `ExecuteUpdateAsync` bulk revocation with accountId cross-check
- `Members/EfMemberManagementPersistence.cs` — NEW: 5-query context load (caller, account, entitlements, owner counts, target); list query with optional Removed filter; `CommitAsync` in transaction
- `Auth/EfInvitePersistence.cs` — expiry edge fix: `< nowUtc` → `<= nowUtc` (valid means expires after now)

**Api:**
- `Helpers/ErrorHttpMapper.cs` — explicit 409 mappings for `Member.OwnerLimitReached`, `Member.LastOwner`, `Member.SeatLimitReached`, `Member.PreviouslyRemoved`; suffix 422 patterns for `.CannotModifySelf`, `.CannotModifyOwner`, `.PrimaryOwnerProtected`, `.InvalidStatusTransition`

**Tests:**
- `IntegrationTests/Api/InviteTests.cs` — updated `SendInvite_RemovedMember_Returns409` to expect `Member.PreviouslyRemoved` (behavior changed by SendInviteService fix)

### Key decisions applied

| # | Decision | Applied |
|---|----------|---------|
| ADR-078 | `members` API/domain language; AccountId from session | ✓ |
| ADR-079 | `account.members.manage` gate; Owner/Admin manage non-owner; Owner-only owner management | ✓ |
| ADR-080 | Max 2 Owners; at least 1 Active Owner; primary owner protected | ✓ |
| ADR-081 | Role changes for Active/Invited/Suspended; Removed via reactivate/resend-invite; Remove clears token; PreviouslyRemoved on send-invite to Removed email | ✓ |
| ADR-082 | Suspend/remove revoke sessions via `RevokeAllSessionsByAccountUserId`; role change does not | ✓ |
| ADR-083 | Off-season deferred | ✓ |

---

## Phase 5D — COMPLETE

Member invites: send (`POST /accounts/me/invite`) + accept (`POST /accounts/invite/accept`). All exit-gate items verified. 389/389 tests passing.

### What was built

**Foundation.Core:**
- `Entities/Accounts/Enums/EntryContext.cs` — added `InvitedUser = 3`
- `Entities/Accounts/Errors/InviteErrors.cs` — NEW: `Forbidden`, `InvalidToken`, `Expired`, `AlreadyActive`, `SeatLimitReached`

**Foundation.Application:**
- `Auth/InviteTokenGenerator.cs` — NEW: 32-byte URL-safe Base64 token; uppercase SHA-256 hex hash (ADR-076)
- `Auth/IInvitePersistence.cs` — NEW: `GetSendInviteContextAsync`, `CommitSendInviteAsync`, `CommitAcceptInviteAsync`; `SendInviteContext`, `AcceptedInvite` records
- `Auth/SendInviteService.cs` — NEW: permission gate, seat-limit check (bypassed for resend), best-effort email with try/catch
- `Auth/AcceptInviteService.cs` — NEW: blank-token guard, `AcceptInviteResult` dedicated type, session outside transaction
- `Auth/InviteEmailTemplate.cs` — NEW: `BuildSubject`, `BuildHtmlBody`
- `Auth/MagicLinkSettings.cs` — added `OperatorBaseUrl`, corrected stale `"Auth"` → `"App"` comment

**Foundation.Infrastructure:**
- `Auth/EfInvitePersistence.cs` — NEW: 5-query context load; `EntityState.Detached` check for new vs tracked invite; `CommitAcceptInviteAsync` uses PostgreSQL savepoint for User creation race + `ExecuteUpdateAsync` conditioned on `Invited` state for activation race; `UpdatedAtUtc` set explicitly in `ExecuteUpdateAsync`

**Api:**
- `Accounts/AccountEndpoints.cs` — NEW: `POST /accounts/me/invite` (RequireAuthorization) + `POST /accounts/invite/accept` (RequireRateLimiting "auth")
- `Program.cs` — registered `SendInviteService`, `AcceptInviteService`, `IInvitePersistence`; mapped `AccountEndpoints`
- `Helpers/ErrorHttpMapper.cs` — explicit `Invite.SeatLimitReached` → 409
- `appsettings.json` — added `OperatorBaseUrl` to `App` section

**Tests:**
- `UnitTests/Auth/InviteTokenGeneratorTests.cs` — NEW: 9 tests
- `IntegrationTests/Api/InviteTests.cs` — NEW: 26 tests
- `IntegrationTests/Api/KeepApiWebFactory.cs` — added `App:OperatorBaseUrl` to test config; added `ExtractInviteToken()` helper

---

## Build state

- `dotnet build` → 0 errors, 0 warnings
- Architecture tests → 14/14 passing
- Unit tests → 280/280 passing
- Integration tests → 95/95 passing
- Total → 389/389 passing

---

## Watch-outs carried forward

- **ADR-058** — updated to Superseded by ADR-061..064 in decision index.
- **AnonymousCurrentUser** — kept for potential worker/test use; not registered in production.
- **SystemClock** FQDN in Program.cs — `OpHalo.Foundation.Infrastructure.Services.SystemClock` (avoids collision).
- **Schema-drop reset pattern** — integration test factory uses `DROP SCHEMA public CASCADE` + recreate + `MigrateAsync`.
- **Migration generation** — always `--startup-project src/OpHalo.Keep.Infrastructure`; needs `ConnectionStrings__DefaultConnection` env var for design-time factory.
- **No GitHub remote yet.**
- **Resend `ApiKey` and `FromAddress`** — must be set via user secrets in production; appsettings.json values are empty placeholders.
- **`App:PublicBaseUrl`** — must point at the public frontend/auth site that owns `/auth/exchange?code=...`.
- **`App:OperatorBaseUrl`** — must point at the operator app that owns `/invite/accept?token=...`.
- **`UseRateLimiter` skipped in Testing** — intentional.
- **`CountActivePilotAccountsAsync`** — counts all `IsPilot = true` without filtering `CommercialState != Canceled`. Conservative count is safe for now.
- **`SignupDefaultsSettings` startup validation** — `TrialDurationDays <= 0` and `MaxPilotAccounts <= 0` are not validated at startup. Add `IValidateOptions<SignupDefaultsSettings>` in a follow-up.
- **Session creation failure test (invite accept)** — not covered in 5D integration tests. Would require overriding `IAccountSessionService`. Deferred; the 503 path is exercised by existing 5B tests.
- **Mobile invite accept** — deferred (D9). Needs `clientType` parsing and bearer response when added.
- **`Member.PreviouslyRemoved` suggestedAction** — `SendInviteService` returns one `Member.PreviouslyRemoved` error for both Removed-with-UserId and Removed-without-UserId cases. The `suggestedAction` field (`"reactivate"` vs `"resend_invite"`) in the HTTP response body is a 5E-C enhancement — endpoint needs to determine it from context.
- **`SendInviteService` manual-share** — `POST /accounts/me/invite` does not yet support `manual_share` delivery mode. Deferred to a small follow-up after resend-invite manual-share is proven in 5E-C.

---

## Next session — Phase 5E-C API + Integration Tests

**Pre-work complete.** Confirmed facts:

### Routes to map in `AccountEndpoints.cs`

```text
GET    /accounts/me/members                              → ListMembersAsync(includeRemoved)
PATCH  /accounts/me/members/{accountUserId}/role         → ChangeRoleAsync(id, role)
POST   /accounts/me/members/{accountUserId}/resend-invite → ResendInviteAsync(id, deliveryMode)
POST   /accounts/me/members/{accountUserId}/suspend      → SuspendAsync(id)
POST   /accounts/me/members/{accountUserId}/reactivate   → ReactivateAsync(id)
DELETE /accounts/me/members/{accountUserId}              → RemoveAsync(id)
```

All mutation routes: `RequireAuthorization()`.
List route: `RequireAuthorization()` (Active-only enforced by session handler).

### Request / response shapes

**GET /accounts/me/members**
- Query: `?includeRemoved=true` (default false)
- Response 200: `{ "members": [ { accountUserId, email, role, status, isCurrentUser, isPrimaryOwner, activatedAtUtc, inviteExpiresAtUtc } ] }`

**PATCH role**
- Body: `{ "role": "admin" }` — validate against Owner/Admin/Operator/Viewer strings
- Success 200: empty body

**POST resend-invite**
- Body: `{ "delivery": "email" | "manual_share" }` — default `"email"` if absent
- Validation: `Validation.InviteDeliveryInvalid` → 400 for unknown delivery string
- Success 200 (email): empty body
- Success 200 (manual_share): `{ "inviteUrl": "..." }` — raw token in URL; do not log
- `InviteDeliveryMode` enum string mapping: `"email"` → `Email`, `"manual_share"` → `ManualShare`

**POST suspend / reactivate**
- No body
- Success 200: empty body

**DELETE**
- No body
- Success 200: empty body

### Error HTTP mappings already added in 5E-B

Already in `ErrorHttpMapper`: `Member.OwnerLimitReached` → 409, `Member.LastOwner` → 409, `Member.SeatLimitReached` → 409, `Member.PreviouslyRemoved` → 409; `.CannotModifySelf` → 422, `.CannotModifyOwner` → 422, `.PrimaryOwnerProtected` → 422, `.InvalidStatusTransition` → 422.

### PreviouslyRemoved suggestedAction (open design for 5E-C)

`SendInviteService` returns one `Member.PreviouslyRemoved` error without context. The endpoint for `POST /accounts/me/invite` needs to add `suggestedAction` to the response. Options:
1. Endpoint queries existing membership after the error to determine `UserId` presence (extra DB call)
2. Extend `Error` with a `Metadata` dictionary in SharedKernel (small, unlocks the pattern)
3. Change `SendInviteService` return to a richer type

Resolve in 5E-C before mapping the endpoint.

### Program.cs registrations to add

```csharp
builder.Services.AddScoped<MemberManagementService>();
builder.Services.AddScoped<IMemberManagementPersistence, EfMemberManagementPersistence>();
```

### Integration tests to write (26 minimum from build-log/022)

See build-log/022 "Tests To Require" list (tests 1–26).
