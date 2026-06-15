# Build Log 016 — Phase 5 Auth Discovery

**Date:** 2026-06-15
**Scope:** Discovery only. No production code changes.
**Status:** Phase 5A ready for implementation. Decisions 1-8 confirmed with Christian; remaining unresolved decisions are deferred to Phase 5B+ before ADR-061+ are added.

---

## Why this phase is next

Phase 7B intentionally shipped with `AnonymousCurrentUser` and service-level fail-closed behavior so Keep public intake could be proven before real auth. The next load-bearing gap is Phase 5:

- `GET /keep/requests` should become framework-auth protected, not merely service-auth protected.
- Phase 8 operator detail/messaging needs a real operator identity.
- The build plan locks magic links, opaque server-side sessions, `/me`, logout/session revoke, and no JWT.

The current recommendation is to split Phase 5 so we do not drag the entire account-entry and email path into the same session as the auth substrate.

---

## Authoritative sources read

### New foundation build plan

`docs/build-log/ophalo-foundation-build-plan-greenfield-boundaries-brownfield-behavior.md`

Locked behavior from sections 4.7 and Phase 5:

- Magic links are the entry/recovery mechanism.
- Trusted server-side opaque sessions are the authentication mechanism.
- No JWT by default.
- Preserve hashed magic-link/token storage, one-time exchange, expiry enforcement, prior unused link invalidation, secure cookie issuance, configured cookie domain, `/me`, logout/session revoke.
- Add mobile-ready session/device fields: `SessionClientType`, `DeviceName`, `LastSeenAtUtc`, `RevokedAtUtc`, and optional future `AccountUserDevice`.

### Reference app auth/session files

Read and evaluated:

- `OpHalo.API/Helpers/ErrorHttpMapper.cs`
- `OpHalo.API/Endpoints/V1/AuthEndpoints.cs`
- `OpHalo.API/Startup/AuthCookieOptionsFactory.cs`
- `OpHalo.API/Startup/AuthCookieSettings.cs`
- `OpHalo.Continuity.API/Helpers/ErrorHttpMapper.cs`
- `OpHalo.Auth/Security/SessionAuthenticationHandler.cs`
- `OpHalo.Auth/Security/ISessionStore.cs`
- `OpHalo.Auth/Security/SessionData.cs`
- `OpHalo.Auth/Security/SessionHasher.cs`
- `OpHalo.Core/Constants/AuthConstants.cs`
- `OpHalo.Core/Entities/Accounts/AccountSession.cs`
- `OpHalo.Core/Entities/Accounts/AccountOnboardingCode.cs`
- `OpHalo.Core/Entities/Accounts/Enums/EntryContext.cs`
- `OpHalo.Infrastructure/Services/Security/CurrentUser.cs`
- `OpHalo.Infrastructure/Services/Security/KeepSessionService.cs`
- `OpHalo.Infrastructure/Services/Security/SessionStore.cs`
- `OpHalo.Application/Abstractions/Security/IKeepSessionService.cs`
- `OpHalo.Application/Accounts/Commands/Auth/SignIn/*`
- `OpHalo.Application/Accounts/Commands/KeepOnboarding/Start/*`
- `OpHalo.Application/Accounts/Commands/KeepOnboarding/Exchange/*`

### Current foundation files checked

- `src/OpHalo.Foundation.Application/Abstractions/Security/ICurrentUser.cs`
- `src/OpHalo.Foundation.Core/Entities/Accounts/AccountUser.cs`
- `src/OpHalo.Foundation.Application/Accounts/Provisioning/AccountProvisioningService.cs`
- `src/OpHalo.Api/Program.cs`

---

## Reference behavior to preserve directly

These are good ports with only namespace/project adjustments:

- `AccountSession` core concept: server-side session row, raw token never stored, SHA-256 token hash only, absolute expiry, last activity, revocation.
- `SessionAuthenticationHandler`: cookie read, token hash lookup, revoked/expired/inactivity checks, claim creation, renewal throttle, lenient last-activity update failure.
- `SessionStore`: persistence-only adapter. It should not own auth policy.
- `CurrentUser`: resolves `AccountUser.Id` from `ClaimTypes.NameIdentifier`, `AccountId` from an OpHalo claim, and fails closed if either is missing or invalid.
- `SessionHasher`: SHA-256 hash of raw opaque token.
- `AuthCookieOptionsFactory`: HttpOnly cookie, `SameSite=Lax`, Secure outside Development, optional configured domain, path `/`.
- `KeepSessionService`: creates raw opaque session token, stores only hash, returns raw token for cookie issuance, revokes by hash on logout.
- Program wiring pattern: `AddHttpContextAccessor`, authentication scheme, authorization, `UseAuthentication`, `UseAuthorization`, protected operator routes.
- `ErrorHttpMapper`: RFC 7807 helper that maps `Error.Code` patterns to `Results.Problem(...)` with `extensions["code"]`.

---

## Reference behavior that needs adapting

The reference magic-link/onboarding flow cannot be ported verbatim.

Reasons:

- The new foundation model intentionally removed `Account.Email` and `Account.PublicSlug`.
- New-account provisioning is centralized in `AccountProvisioningService` and requires `businessName`, `purpose`, `timeZone`, `plan`, trial/pilot inputs, and the two-phase persistence save from ADR-044.
- Public intake identity moved to Keep-owned `KeepPublicIntakeLink`, not Account columns.
- Reference onboarding also creates legacy onboarding/notification/work-item concepts that are not present in this greenfield build.
- Current `AccountUser` already has invite state (`InviteTokenHash`, `InviteExpiresAtUtc`, `Activate`) that should be evaluated before adding a separate invite acceptance flow.

Conclusion: port session/auth infrastructure first, then design magic-link issuance/exchange against the new Foundation model.

---

## Future-facing notes from discovery

### Seat limits and login sharing

The current Foundation model can support subscription seat limits through `AccountEntitlements.MaxUserSeats` plus `AccountUser` membership rows. Seat enforcement belongs on invite/activation/member-management flows, not inside the session entity itself.

Login sharing is related but different: the system cannot reliably know that two humans are sharing one login unless session/device patterns are observed. Phase 5A should therefore add enough session/device structure to support later controls without enforcing heavy policy now:

- Display active sessions/devices.
- Revoke one session.
- Revoke all sessions for one account user.
- Revoke all sessions for an account.
- Flag unusual session counts or client/device patterns.
- Add concurrent-session limits later if abuse appears.

No aggressive anti-sharing enforcement is planned for Phase 5A.

### Connected-device messaging and announcements

Christian raised future messages to connected devices, such as service maintenance notices.

Discovery decision: do not turn `AccountSession` into notification infrastructure.

The future model should keep these concepts separate:

- `AccountSession` = authenticated session and auth lifecycle.
- `AccountUserDevice` or similar = known user device/install, if needed later.
- `NotificationDestination` = reachable email/push/in-app destination.
- `Announcement` or `SystemNotice` = operational/product message such as maintenance windows.

Phase 5A session fields (`SessionClientType`, `DeviceName`, `LastSeenAtUtc`) support later device visibility, but actual push/email/in-app delivery remains deferred to a notification/device phase.

---

## Proposed session split

### Phase 5A — Session substrate and protected operator route

Goal: replace `AnonymousCurrentUser` in production with real cookie-backed server-side session auth, without implementing magic-link issuance/exchange yet.

Likely implementation scope:

- Add `AccountSession` to Foundation Core.
- Add `SessionClientType` and mobile-ready session fields if confirmed.
- Add EF mapping and migration for `account_sessions`.
- Add `AuthConstants`, `SessionHasher`, `SessionData`, `ISessionStore`, `SessionStore`.
- Add `SessionAuthenticationHandler`.
- Add real `CurrentUser` in Foundation Infrastructure.
- Add `IKeepSessionService` and `KeepSessionService`.
- Add `AuthCookieSettings` and `AuthCookieOptionsFactory`.
- Wire `Program.cs`: `AddHttpContextAccessor`, auth scheme, authorization, `UseAuthentication`, `UseAuthorization`.
- Change `GET /keep/requests` to `.RequireAuthorization()`.
- Add `POST /auth/logout`.
- Add `/me` route if route/shape is confirmed.
- Update HTTP integration tests to seed a real `AccountSession` row and send the auth cookie instead of overriding `ICurrentUser`.

Exit gate:

- Build, unit, architecture, and integration tests pass.
- Anonymous `/keep/requests` returns 401 from framework auth.
- Valid session cookie populates `ICurrentUser` and allows `/keep/requests`.
- Revoked, expired, inactive, suspended, and removed-member sessions cannot continue access.
- Logout revokes the session and deletes the cookie.

### Phase 5B — Magic-link issuance and exchange

Goal: implement entry/recovery magic links against the new Foundation model.

Likely implementation scope:

- Add auth-code entity equivalent to reference `AccountOnboardingCode`, with new naming if confirmed.
- Generate high-entropy raw codes; store only hashes.
- Enforce one-time exchange, expiry, and prior-code invalidation.
- Add `POST /auth/signin`.
- Add `POST /auth/exchange`.
- Decide how codes are delivered/captured before full notification infrastructure exists.
- On successful exchange, create a server-side session and issue the cookie.

Open design area:

- Whether Phase 5B supports existing-member sign-in only, or also creates brand-new accounts.
- How to adapt the reference new-account onboarding path to `AccountProvisioningService` and the Keep public-intake link model.

### Phase 5C — Account start/invites/devices, if needed

Possible follow-up if 5B grows too large:

- `POST /auth/start` for new-account bootstrap.
- Account invite acceptance using current `AccountUser` invite state.
- Rich device/session management beyond the fields on `AccountSession`.

---

## Confirmed decisions

### Decision 1 — One opaque session model for browser and mobile

Confirmed with Christian on 2026-06-15.

OpHalo will use one server-side opaque session model across browser and mobile:

- Browser clients send the raw opaque session token in the secure `ophalo.sid` cookie.
- Native mobile clients send the same kind of raw opaque session token as `Authorization: Bearer <token>`.
- The database stores only SHA-256 token hashes.
- The token carries no claims; all validity and authorization remain server-enforced.
- JWT remains excluded.

Rationale:

- Preserves the build-plan lock: trusted server-side sessions, no JWT.
- Gives mobile first-class support without a second auth system.
- Keeps revocation, inactivity expiry, membership status, account posture, entitlements, and permissions authoritative on the server.
- Lets `CurrentUser` and application services behave identically regardless of whether the raw token arrived via cookie or bearer header.

### Decision 2 — Mobile-ready fields live on AccountSession

Confirmed with Christian on 2026-06-15.

Phase 5A will add mobile-ready fields directly to `AccountSession`:

- `SessionClientType` enum with at least `Browser`, `MobileApp`, and `Admin`.
- Optional `DeviceName`.
- `LastSeenAtUtc`.
- Existing session lifecycle fields: `CreatedAtUtc`, `ExpiresAtUtc`, `LastActivityAtUtc`, `RevokedAtUtc`.

`LastActivityAtUtc` and `LastSeenAtUtc` stay separate:

- `LastActivityAtUtc` drives the inactivity security window.
- `LastSeenAtUtc` supports display/audit/device-list scenarios and can evolve independently.

A separate `AccountUserDevice` table is deferred until richer device management is needed.

Rationale:

- Supports the planned mobile app before launch without forcing a later heavy schema correction.
- Avoids overbuilding device-management tables before there is a concrete feature surface.
- Keeps live-client change risk low: future device features can attach to or reference existing session rows without replacing the auth model.

### Decision 3 — Phase 5A is session substrate only

Confirmed with Christian on 2026-06-15.

Phase 5A will implement the auth/session foundation only:

- `AccountSession` storage and EF mapping.
- Browser cookie auth.
- Mobile opaque bearer-token auth.
- Real `CurrentUser`.
- Auth middleware wiring in `Program.cs`.
- Framework authorization on `GET /keep/requests`.
- Logout/session revoke.
- `/me` current-user resolution, subject to the route/shape decision below.

Phase 5A will explicitly defer:

- Magic-link auth-code entity.
- `/auth/signin`.
- `/auth/start`.
- `/auth/exchange`.
- Email delivery.
- New-account onboarding.
- Invite acceptance.

Rationale:

- Removes the production `AnonymousCurrentUser` placeholder and protects operator access first.
- Gives the browser and mobile apps the same server-authoritative session foundation.
- Keeps the high-variance account-entry/email/onboarding flow out of the session-substrate implementation session.

### Decision 4 — Current-session endpoint is GET /auth/me

Confirmed with Christian on 2026-06-15.

Phase 5A will add `GET /auth/me`.

- It is an API endpoint, not an account/profile page.
- It requires authentication.
- Anonymous callers receive 401.
- Minimal Phase 5A response: `accountUserId`, `accountId`, `isAuthenticated`, `isVerified`.
- No `/me` alias for now.

Rationale:

- Gives browser and mobile apps a shared session-introspection endpoint.
- Keeps the route grouped with auth.
- Avoids a profile/account-management contract before that surface exists.

### Decision 5 — Logout ships in Phase 5A

Confirmed with Christian on 2026-06-15.

Phase 5A will add `POST /auth/logout`.

- Browser logout revokes the current server-side session and deletes the `ophalo.sid` cookie.
- Mobile logout revokes the current server-side session; the mobile client discards its bearer token.
- Logout is idempotent: missing/unknown/already-revoked sessions still return success after clearing browser cookie state where applicable.

Rationale:

- Logout only depends on the session substrate.
- Session creation without revocation would leave the auth foundation incomplete.

### Decision 6 — Session validity depends on membership status

Confirmed with Christian on 2026-06-15.

A valid raw token is not enough to authenticate a request. The session-auth path must stop authenticating sessions whose backing `AccountUser` is no longer active.

Phase 5A should fail closed when the session's `AccountUser` is missing, `Suspended`, or `Removed`.

Rationale:

- Supports operator offboarding.
- Supports future seat enforcement and login-sharing controls.
- Prevents stale sessions from surviving membership removal.

### Decision 7 — Auth resolves identity; authorization remains action-specific

Confirmed with Christian on 2026-06-15.

Phase 5A auth answers "who is this session?" It should not absorb all account posture, entitlement, feature, and permission checks.

- The auth handler/session store may validate that the session and membership are still active enough to authenticate.
- Application services continue checking account lifecycle, commercial posture, operating mode, permissions, and feature entitlements for each action.

Rationale:

- Keeps authentication and authorization separate.
- Preserves the existing Foundation access-policy shape.
- Avoids packing action-specific business rules into the ASP.NET authentication handler.

### Decision 8 — Port/adapt the reference ErrorHttpMapper

Confirmed with Christian on 2026-06-15.

The reference app already has HTTP error mappers:

- `OpHalo.API/Helpers/ErrorHttpMapper.cs`
- `OpHalo.Continuity.API/Helpers/ErrorHttpMapper.cs`
- `OpHalo.Signal.API/Helpers/ErrorHttpMapper.cs`

Phase 5A should port/adapt the main API helper into `OpHalo.Api` rather than inventing a new mapper from scratch.

Adaptation notes:

- Use the current repo's `OpHalo.SharedKernel.Results.Error`.
- Keep the RFC 7807 shape: `Results.Problem(...)`, `type: "about:blank"`, and `extensions["code"] = error.Code`.
- Prefer pattern-based mappings (`Contains("Validation")`, `EndsWith(".Unauthorized")`, etc.) over enumerating every validation/domain error.
- Preserve the special public-intake mapping from Phase 7B: `keep.public_intake.unavailable` => 422.
- Keep current auth mappings: unauthenticated => 401, forbidden => 403.
- Do not import obsolete legacy-only mappings blindly; evaluate against current Foundation/Keep errors.

Rationale:

- This is established reference-app behavior.
- It keeps endpoints thin and avoids growing local `Program.cs` mapper functions as auth routes are added.
- It prevents Claude from creating a parallel error-mapping style.

---

## Open decisions for Christian

These need answers before ADR-061+ are added and before Claude writes Phase 5 code.

1. **Immediate implementation split**

   Confirmed: Phase 5A is session substrate only. Magic-link issuance/exchange, email delivery, new-account onboarding, and invite acceptance are deferred.

2. **Session fields**

   Confirmed: 5A adds `SessionClientType`, `DeviceName`, and `LastSeenAtUtc` to `AccountSession`. Separate `AccountUserDevice` is deferred.

3. **`/me` route and response**

   Confirmed: `GET /auth/me`, authenticated only, minimal identity response. Anonymous callers receive 401. No `/me` alias for now.

4. **Logout in 5A**

   Confirmed: `POST /auth/logout` ships in 5A.

5. **Auth-code entity naming**

   Keep the reference name `AccountOnboardingCode`, or rename for the new model?

   Recommendation: rename to `AccountAuthCode` or `AuthMagicLinkCode` because the code supports sign-in/recovery/invite/new-account flows, not only onboarding.

6. **Code hash casing**

   Reference onboarding code hashes are uppercase hex; current Keep public token hashes use lowercase hex.

   Recommendation: use lowercase SHA-256 hex for all new auth/session token hashes unless we need legacy database compatibility, which we do not.

7. **Email delivery before notifications**

   For 5B, how should `/auth/signin` deliver or expose magic-link codes before the notification/outbox phase exists?

   Options:

   - Create code rows only and expose raw codes in tests/development only.
   - Add a small `IAuthEmailSender` adapter now.
   - Wait for notification/outbox infrastructure before exposing sign-in.

   Recommendation: decide explicitly before 5B. Do not silently add a fake production delivery path.

8. **New-account creation timing**

   Should 5B support existing-member sign-in only, or include new-account creation through `/auth/start` and `/auth/exchange`?

   Recommendation: existing-member sign-in first if we want a tight 5B. New-account creation requires decisions about `purpose`, `timeZone`, `plan`, trial defaults, and Keep public-intake link creation.

9. **Invite acceptance**

   Should invite acceptance be part of Phase 5B, or deferred to account/team management?

   Recommendation: defer unless launch requires inviting operators immediately. Current `AccountUser` has invite token fields, but the full acceptance flow deserves its own narrow pass.

10. **API error mapper**

   Confirmed: port/adapt the reference `ErrorHttpMapper` into `OpHalo.Api`. Do not build a new style from scratch.

11. **Cookie domain and frontend origin**

   Should 5A include `Auth:CookieDomain` config and any credentialed CORS policy now?

   Recommendation: include cookie-domain config with empty host-only default. Add CORS only if we know the exact frontend origins for local/dev/prod.

---

## Suggested Claude handoff once decisions are answered

Use this instruction shape after Christian answers the open decisions:

> Implement Phase 5A only. Read `docs/build-log/016-phase-5-auth-discovery.md`, build-plan section 4.7, and the listed reference auth/session files before the pre-implementation gate. Port the session substrate from the reference app into the new Foundation architecture, adapting namespaces and current model boundaries. Do not implement magic-link issuance/exchange yet. Replace production `AnonymousCurrentUser` with real cookie-backed `CurrentUser`, protect `GET /keep/requests` with framework authorization, add logout and the confirmed `/me` route, seed real session rows in HTTP tests instead of overriding `ICurrentUser`, and update docs/ADR only for decisions confirmed by Christian.

---

## Decision-index status

No ADRs were added during this discovery pass.

Next free ID remains `ADR-061`. Phase 5A implementation may proceed from the confirmed decisions above. Add ADR-061+ after implementation or at the Phase 5A pre-implementation gate using only the decisions confirmed with Christian; defer unresolved Phase 5B+ magic-link/email/onboarding decisions.
