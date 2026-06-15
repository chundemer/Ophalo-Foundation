# Phase 5B — Magic Link Auth Discovery

**Status:** Re-scoped and ready for Phase 5B implementation. Decisions confirmed with Christian on 2026-06-15.
**Build-log preceding this:** 017-phase-5a-auth-session-foundation.md
**Reference files read:**
- `_reference/src/OpHalo.API/Endpoints/V1/AuthEndpoints.cs`
- `_reference/src/OpHalo.Application/Accounts/Commands/Auth/SignIn/SignInCommandHandler.cs`
- `_reference/src/OpHalo.Application/Accounts/Commands/KeepOnboarding/Start/StartOnboardingCommandHandler.cs`
- `_reference/src/OpHalo.Application/Accounts/Commands/KeepOnboarding/Exchange/ExchangeOnboardingCodeCommandHandler.cs`
- `_reference/src/OpHalo.Core/Entities/Accounts/AccountOnboardingCode.cs`
- `_reference/src/OpHalo.Application/Accounts/Commands/KeepOnboarding/OnboardingCodeGenerator.cs`
- `_reference/src/OpHalo.Application/Constants/MagicLinkEmailConstants.cs`
- `_reference/src/OpHalo.Core/Entities/Accounts/Enums/EntryContext.cs`
- `_reference/src/OpHalo.Core/Entities/Accounts/Errors/AccountOnboardingCodeErrors.cs`
- `_reference/src/OpHalo.API/Program.cs` (rate limiting policy)
- `_reference/src/OpHalo.Application/Settings/AppSettings.cs`
- `_reference/web/ophalo-web/src/app/auth/exchange/page.tsx`
- `_reference/web/ophalo-web/src/app/api/auth/exchange/route.ts`

---

## Phase 5A status check (pre-discovery)

All five Phase 5A quality items were verified before discovery:

| Item | Status |
|------|--------|
| `ExpiresAtUtc <= nowUtc` expiry check | Already `<=` in commit — no change needed |
| `AccountSessionConfiguration` FK/index shape | All 4 FKs and indexes already present |
| Migration matches configuration | Verified — migration already has FKs and all indexes |
| Redundant `Microsoft.Extensions.Configuration.*` packages | Already absent from all csproj files |
| Build + all tests | `dotnet build` → 0 warnings 0 errors; 308/308 tests passing |

---

## Reference behavior summary

The reference app implements magic-link auth with three endpoints:

```
POST /auth/signin     → existing-user fast path
POST /auth/start      → new + existing unified (captures businessName, name)
POST /auth/exchange   → code exchange → session creation + cookie
```

**Code issuance (both `/signin` and `/start`):**
1. Normalize email.
2. Look up `Account` by `Account.Email` (reference) to determine `EntryContext`.
3. Invalidate prior unconsumed codes for that account/email.
4. Generate a 32-byte cryptographically random URL-safe Base64 code (256 bits, ~43 chars).
5. Hash with SHA-256 (hex). Only the hash is persisted.
6. Commit `AccountOnboardingCode + EmailOutboxMessage + WorkItem` in one `SaveChangesAsync`.
7. Return `200 OK` (no body, no code in response — enumeration protection).

**Code exchange (`/exchange`):**
1. Hash the submitted raw code. Look up by hash.
2. Validate: not null, not expired, not consumed, not invalidated, `EntryContext` not null.
3. Route by `EntryContext`:
   - `NewAccount` → `HandleNewAccountAsync`: create `User + Account + AccountOnboarding + AccountNotifications + AccountEntitlements + AccountUser` atomically, then create session outside the TX.
   - `ExistingMember / Recovery` → `HandleReauthAsync`: load account + primary owner, consume code atomically, advance onboarding state if needed, create session outside TX.
4. Session creation failure (outside TX) is a distinct outcome — returns a specific error so the frontend can direct the user to `/signin` for recovery.
5. On success: set `ophalo.sid` cookie and return `200 OK`.

**Race guard:** Both exchange paths use `ExecuteUpdateAsync` on `ConsumedAtUtc == null && InvalidatedAtUtc == null` inside a transaction. `affected == 0` means another concurrent request won — return `AlreadyConsumed`.

**Auth rate limiting:** `"auth"` policy — fixed window, 10 req/min per IP, no queue. Applied to all four auth endpoints.

**Public magic-link base URL:** the reference binds `App:PublicBaseUrl` to the public web/auth site, not the API host. Magic-link emails point to `{App:PublicBaseUrl}/auth/exchange?code=...`; the frontend route reads the code and then POSTs to the backend `/auth/exchange`. Do not configure this value as the API base URL.

**Code generator:** `RandomNumberGenerator.GetBytes(32)` → URL-safe Base64 (no padding). SHA-256 hex stored. Foundation uses `MagicLinkCodeGenerator` in Application — internal static class.

**`AccountAuthCode` fields:** `Id`, `AccountId?` (null for NewAccount), `CodeHash`, `IssuedAtUtc`, `ExpiresAtUtc` (24-hour window), `ConsumedAtUtc?`, `InvalidatedAtUtc?`, `DeliveryEmailSnapshot`, `TargetAccountUserId?`, `EntryContext?`. New-account snapshots such as `BusinessNameSnapshot` / `NameSnapshot` are deferred to Phase 5C.

---

## Confirmed phase split

Phase 5B is now intentionally narrow:

| Phase | Scope | Status |
|-------|-------|--------|
| 5B | Existing-member magic-link sign-in only: `POST /auth/signin` + `POST /auth/exchange` | Ready to implement |
| 5C | New-account registration/start: `POST /auth/start`, account provisioning, time zone, trial defaults, Keep setup decisions | Discovery required before code |
| 5D | Member invites: owner/admin creates pending member invite; invitee accepts temporary link; membership activates | Discovery required before code |

Rationale:

- Existing-member sign-in can be built safely without deciding new-account onboarding, Keep public-intake setup, invite activation, seat limits, or pilot gates.
- Business-critical member invites are not dropped; they are explicitly reserved as Phase 5D.
- The endpoint surface stays clean: sign-in, registration/start, and invite acceptance each get their own contract and validation rules.

---

## Foundation model divergences

### 1. Email lookup: `User.Email`, not `Account.Email`

Foundation dropped `Account.Email` (ADR-021). The identity anchor is `User.Email`.

**Impact for Phase 5B:** `POST /auth/signin` looks up `User.Email`, then requires at least one active `AccountUser` membership for that user. If none exists, it returns neutral success and issues no code.

**Confirmed behavior:** never create a second account for an email that already belongs to a `User`. Unknown email, invited-only membership, suspended/removed-only membership, or inconsistent membership state all return neutral `200 OK` with no code for enumeration protection.

### 2. Account.PublicSlug / InitPublicIntakeToken not in Foundation

The reference `/start` generates a slug candidate from `BusinessName` and stores it as `SlugSnapshot` on the code. The reference `/exchange` NewAccount path calls `account.InitPublicIntakeToken(...)` and sets `PublicSlug`.

**Foundation:** `Account` carries neither (ADR-018, ADR-046). The public intake link (`KeepPublicIntakeLink`) is a Keep concern. Phase 5B existing-member sign-in must NOT touch slug or intake token. New-account/Keep setup is deferred to Phase 5C discovery.

### 3. AccountNotifications / AccountOnboarding not in Foundation

The reference `HandleNewAccountAsync` creates `AccountNotifications` and `AccountOnboarding` satellites in the same TX.

**Foundation:** Neither exists in Phase 5B scope. Phase 5B does not create new accounts. NewAccount exchange is deferred to Phase 5C discovery.

### 4. AccountProvisioningService requires inputs not in the reference code flow

`AccountProvisioningService.CreateVerified(email, name, businessName, purpose, timeZone, plan, isPilot, nowUtc, trialEndsAtUtc)` takes `timeZone`, `purpose`, `plan`, `isPilot`, and `trialEndsAtUtc` — none of which are captured in the reference `/start` request body.

New-account inputs are deferred to Phase 5C. When 5C is designed, remember that the current enum is `AccountPurpose.Business` / `Internal` — there is no `Regular` value in this repo.

### 5. Email delivery: IEmailSender, not outbox + worker

Foundation has `IEmailSender` in `Foundation.Application.Abstractions.Messaging` but no `EmailOutboxMessage` table or work item scheduler — those are not built yet.

**Impact:** Phase 5B cannot atomically commit the magic-link code and guarantee email delivery in one TX the way the reference does.

**Confirmed behavior for 5B:** use direct `IEmailSender.SendAsync` for now, plus a test/dev capture implementation for integration tests. Production email delivery uses **Resend** behind the existing `IEmailSender` abstraction. Preserve enumeration-safe `/signin` responses: unknown or ineligible emails return the same neutral `200 OK` and send nothing. Known eligible emails return neutral `200 OK` after attempting delivery; provider failure must not reveal account existence through a different public response.

### 6. Application layer must not inject DbContext

The reference handlers inject `IPlatformDbContext` (EF context) directly into Application. Foundation's architecture boundary (§8, architecture tests) forbids this. Phase 5B needs persistence interfaces in `Foundation.Application` implemented by `Foundation.Infrastructure`.

Minimum persistence interface surface needed:
- `FindEligibleSignInMemberByEmailAsync(string normalizedEmail, CancellationToken ct)` — returns an active account-user/account snapshot or null
- `CommitSignInCodeAsync(AccountAuthCode code, CancellationToken ct)` — atomic: invalidates prior codes for this member + persists the new code in one transaction
- `FindCodeByHashAsync(string hash, CancellationToken ct)` — lookup by hash
- `ConsumeCodeAsync(Guid codeId, DateTime consumedAtUtc, CancellationToken ct)` → `bool` — atomic conditional update; returns false if code was already consumed/invalidated (race guard)

New-account provisioning persistence is deferred to Phase 5C.

### 7. IAccountSessionService.CreateSession requires ClientType

The Foundation `IAccountSessionService.CreateSession` signature requires `SessionClientType clientType` and `string? deviceName`. The reference session creation takes only `(account, accountUserId)`.

**Confirmed behavior:** `/auth/exchange` accepts optional `clientType` and `deviceName`. Omitted `clientType` defaults to `Browser`; mobile clients send `MobileApp`. `deviceName` is optional descriptive metadata only, not an authorization input.

### 8. Auth rate limiting policy

The reference uses a named `"auth"` policy (fixed window, 10 req/min per IP) on all auth endpoints. Phase 5A already added this policy. Phase 5B endpoints must use it.

### 9. PublicBaseUrl is frontend-owned

The reference has `AppSettings.PublicBaseUrl` and `AppSettings.OperatorBaseUrl` bound from the `"App"` configuration section.

- `App:PublicBaseUrl` is the public website/auth frontend, e.g. `https://ophalo.com`.
- `App:OperatorBaseUrl` is the operator app, e.g. `https://app.ophalo.com`, and is used by invite links in the reference.

For Phase 5B, magic-link email URLs must use:

```text
{App:PublicBaseUrl}/auth/exchange?code={rawCode}
```

That URL is browser-clickable and belongs to the frontend route. The frontend then posts the code to the backend API:

```text
POST /auth/exchange
```

**Confirmed:** use `App:PublicBaseUrl` for magic-link URL generation. Do not use `Auth:PublicBaseUrl`, and do not point the value at the API host. Phase 5D should introduce `App:OperatorBaseUrl` for invite acceptance links, matching the reference.

---

## Files Phase 5B will touch (estimated)

**New — Foundation.Core:**
- `Entities/Accounts/AccountAuthCode.cs`
- `Entities/Accounts/Enums/EntryContext.cs`
- `Entities/Accounts/Errors/AccountAuthCodeErrors.cs`
- `AccountErrors.cs` additions only if needed, e.g. `SessionCreationFailed`

**New — Foundation.Application:**
- `Auth/IAuthCodePersistence.cs` (or per-operation interface)
- `Auth/MagicLinkCodeGenerator.cs`
- `Auth/MagicLinkEmailTemplate.cs`
- `Auth/SignInAuthService.cs`
- `Auth/ExchangeAuthService.cs`

**New — Foundation.Infrastructure:**
- `Auth/EfAuthCodePersistence.cs`
- `Persistence/Configurations/AccountAuthCodeConfiguration.cs`
- Resend-backed `IEmailSender` implementation/configuration for production email delivery, if not already present
- test/dev email capture implementation if not handled inside tests
- New migration: `AccountAuthCodes`

**Modified — Api:**
- `Auth/AuthEndpoints.cs` — add `POST /auth/signin` + `POST /auth/exchange`
- `Program.cs` — wire new services and email sender/test capture
- `appsettings.json` — `App:PublicBaseUrl` section for frontend magic-link URLs; `Resend` section for email provider settings

---

## Confirmed decisions

### Decision 1: Code entity name

The reference calls it `AccountOnboardingCode` — a name tied to the onboarding flow, not the general sign-in case. Foundation uses this entity for both new-account registration AND re-auth for existing members.

Options:
- `AccountLoginCode` — describes the auth use case precisely
- `AccountMagicCode` — product-language match ("magic link")
- `AccountAuthCode` — generic, neutral
- `AccountOnboardingCode` — keep reference name, accept the mismatch

**Confirmed:** `AccountAuthCode`.

Rationale: the code is an authentication artifact that can support existing-member sign-in now and future auth contexts later. It is not only onboarding, and "login" is too narrow for registration/recovery-style auth links.

### Decision 2: Endpoint shape — two or three endpoints

**Option A (two endpoints):** `/start` (new + existing unified) + `/exchange`
- `/start` takes `email`, `businessName?`, `name?`
- Matches the reference `/start` behavior; eliminates the separate `/signin` endpoint
- Simpler surface; existing member sign-in is just `/start` with no business name

**Option B (three endpoints):** `/signin` + `/start` + `/exchange`
- `/signin` takes only `email` — clean existing-member path, no business name fields
- `/start` takes `email`, `businessName?`, `name?` — new account registration
- Matches the reference exactly; allows different UX for sign-in vs registration

**Confirmed:** split endpoint surface, but Phase 5B implements only:

- `POST /auth/signin`
- `POST /auth/exchange`

`POST /auth/start` is deferred to Phase 5C.

Rationale: sign-in and registration have different validation and UX contracts. Keeping them separate avoids conditional `/start` semantics and lets existing-member auth ship before new-account provisioning.

### Decision 3: timeZone at /start

Foundation's `Account.CreateVerified` requires a `timeZone` argument (non-nullable). The reference did not require time zone at sign-up (it was set later).

Options:
- **A: Required field at `/start`** — caller must supply an IANA time zone. Simplest at the Application layer; moves UX burden to the frontend.
- **B: Optional at `/start`, default `"UTC"`** — captured at onboarding step post-session. Practical default; account operates in UTC until the owner explicitly sets it.
- **C: Optional at `/start`, required during post-session onboarding before Keep is usable** — the app blocks Keep access until time zone is confirmed.

**Deferred to Phase 5C.**

Current recommendation for 5C: require IANA `timeZone` at `/auth/start`. Browser and mobile clients can auto-detect it; time zone affects business-hours, notifications, reporting, and Keep behavior later. Do not default silently to `"UTC"` without a deliberate 5C decision.

### Decision 4: Email delivery seam in Phase 5B

The reference commits the magic-link code and sends an email atomically via outbox + worker. Foundation doesn't have the outbox/worker yet (Phase 8/9 concern).

Options:
- **A: Direct `IEmailSender.SendAsync`** — call synchronously from the service. Email send failure is a best-effort result: the code is already persisted, user can retry `/start`. Simple for Phase 5B; outbox can be layered in later.
- **B: Defer email entirely** — Phase 5B only stores the code; no email is sent. Requires a dev-mode response hack (returning the magic link in the response body in non-production). Not useful for any real testing without additional scaffolding.

**Confirmed:** direct `IEmailSender` in Phase 5B, with Resend as the production provider behind the abstraction, enumeration-safe public responses, and test/dev capture.

Rationale: outbox-grade delivery is deferred, but 5B needs functional magic links. The public `/signin` response must not reveal whether an email exists or whether delivery failed for a known account.

### Decision 5: clientType at /exchange

`IAccountSessionService.CreateSession` requires `SessionClientType clientType`. The reference had no such field.

Options:
- **A: Always `Browser` at `/exchange`** — mobile apps that call `/exchange` also get `Browser` session type. Simple, but the `clientType` distinction is lost at exchange time.
- **B: Accept `clientType` as optional in the exchange request body** — defaults to `Browser` if absent. Mobile SDK can pass `MobileApp`. Forward-compatible.

**Confirmed:** optional `clientType` and optional `deviceName` at `/auth/exchange`.

Defaults and constraints:

- omitted `clientType` => `Browser`
- mobile clients may send `MobileApp`
- `deviceName` is optional and descriptive
- public clients must not be allowed to create `Admin` sessions unless a later admin-specific auth surface permits it

### Decision 6: InvitedUser path — in scope for 5B?

The reference `EntryContext.InvitedUser` handles the case where a user has an `AccountUser` with `MembershipStatus.Invited` but no `User` row yet. The invite acceptance flow creates the `User` and activates the `AccountUser`.

Foundation has `AccountUser.Invited` with `InviteTokenHash`/`InviteExpiresAtUtc`. The invite acceptance could use a dedicated `/auth/accept-invite?token=...` endpoint rather than going through the magic-link flow at all.

Options:
- **A: Include in Phase 5B** — `/start` detects an invited `AccountUser` by email, issues `EntryContext.InvitedUser`, and `/exchange` activates the membership.
- **B: Defer to invitation phase** — Phase 5B covers only `NewAccount` + `ExistingMember`. Invited users use a separate accept-invite flow.

**Confirmed:** defer invite acceptance from Phase 5B, but lock it as required Phase 5D scope.

Phase 5D direction:

- Owner/Admin creates a pending `AccountUser` invite.
- Backend stores a hashed temporary invite token and expiry.
- Backend returns a shareable invite link and suggested message.
- Mobile clients may use native share/SMS compose so the owner/admin can send the link manually from their phone.
- Invitee opens the link, validates the token, links or creates `User`, activates the pending `AccountUser`, and creates a session.
- Email delivery can be first-class; SMS infrastructure can be added later as a notification transport.

Phase 5B must not treat invited users as active existing members and must not create a second account for them.

### Decision 7: Pilot slot gate — in scope for 5B?

The reference checks pilot slot capacity at both `/start` and `/exchange` (NewAccount path only). Foundation has `IsPilot` on `AccountEntitlements` and the `CreatePilot` factory.

This requires a `PilotSlotsSettings` configuration class and a live DB count query in the service layer.

**Confirmed:** defer. Pilot gate is not in Phase 5B.

### Decision 8: User with no active AccountUser — behavior at signin

If a `User` row exists for the given email but has no active `AccountUser` memberships (invited only, all removed/suspended, or zero rows), what does `/signin` do?

Options:
- **A: `InconsistentState` error** — a verified User should always have at least one AccountUser.
- **B: Treat as NewAccount** — silently creates a new account. Risk: the existing User gets a second Account.
- **C: `ExistingMember` with a session that fails the Active gate** — issue code, create session, session handler fails closed (already correct behavior for Suspended/Removed).

**Confirmed:** return neutral `200 OK`, issue no code, and do not create a second account.

Rationale: preserves enumeration protection, avoids useless magic links that exchange into a session immediately rejected by the Active gate, and prevents accidental duplicate account creation for an existing identity.

---

## Files NOT changing in Phase 5B

- `SessionAuthenticationHandler.cs` — complete, no changes
- `AccountSession.cs`, `AccountSessionService.cs`, `SessionStore.cs` — complete
- `AuthEndpoints.cs` `/auth/me` + `/auth/logout` — complete
- `POST /auth/start` — deferred to Phase 5C
- invite creation/acceptance — deferred to Phase 5D
- Architecture tests — additive only (new rules for the code entity if needed)

---

## Exit gate (from build plan Phase 5)

- Existing-member sign-in issues a magic-link code for active members and sends/captures email
- Unknown/ineligible email returns neutral `200 OK` and issues no code
- Exchange succeeds for `ExistingMember` and creates a browser/mobile session
- Exchange rejects expired, consumed, invalidated, or unknown codes
- Concurrent exchange race: one request consumes the code; the loser receives already-consumed/invalid response
- `/me` and logout continue to work from Phase 5A
- Suspended/Removed user cannot continue access (covered by Session handler Active gate — already passing)

---

## Phase 5B logging policy

**What to log (server-side failures only):**

Session creation failure after a successful code exchange is the only unexpected server-side
failure in Phase 5B. Log it as an error with safe diagnostic context:

- `AccountId` (from the auth code)
- `AccountUserId` (from the auth code's `TargetAccountUserId`)
- `AccountAuthCodeId` (the code's `Id`)
- Exception details

**What not to log:**

- Raw auth code (never — would allow replay if logs are compromised)
- Raw session token
- Magic link URL (contains the raw code)
- Token hashes (no diagnostic value; omit unless a specific incident requires it)
- Email address (PII — no logging policy confirmed yet; omit until decided)

**Normal auth outcomes — do not log as errors:**

The following are expected auth outcomes, not server failures. No error logging:

- Unknown email on `/auth/signin`
- Ineligible / suspended / removed member on `/auth/signin`
- Invalid, expired, or already-consumed code at `/auth/exchange`
- Anonymous 401 from the session handler

**Public-facing error messages:**

Keep user-facing messages generic and recovery-oriented. Do not expose internal failure
reasons. `Account.SessionCreationFailed` message:

> "We could not finish signing you in. Please try signing in again."

---

_Discovery updated. Phase 5B implementation may proceed with the confirmed existing-member sign-in scope above. Phase 5C and 5D require their own discovery passes before code._
