# Build Log 070 — Session 16: Native Mobile App Foundation

**Started:** 2026-07-02
**Status:** S16d complete (backend mobile auth/device contracts committed); S16e planned
**Next free ADR before this log:** ADR-385
**Next free ADR after this log:** ADR-396

---

## Context

Session 16 establishes the native mobile app foundation. S15 closed all active pilot-readiness
tracker items; mobile and Apple/Google review approval are now the critical path before pilot
go-live. Decisions in this log use a store-review-safe posture, not a "good enough for pilot"
posture.

The backend is largely ready: Session 8 delivered device registration (`PUT /me/devices/{appInstallationId}`),
badge endpoint (`GET /me/badge`), push adapter interface, and no-op delivery. Session 5 delivered
one opaque session model for browser and mobile, bearer-first resolution, and `SessionClientType.MobileApp`.
The exchange endpoint already supports `clientType: "mobile_app"`, but S16 deliberately does not
send raw bearer tokens through browser URLs or custom URL scheme callbacks. Mobile exchange uses a
one-time handoff code that the app redeems directly with the API.

ADR-236 (mobile stack, Proposed since S8 planning) is promoted to Locked in this session.

---

## Scope

Session 16 is split into five smaller Claude Code sessions. No operator product workflow is built
here — that begins in S17.

### S16a — Decisions and Contracts (this session)
Lock all mobile foundation decisions. Identify the exact backend/web contract changes needed for
mobile sign-in. Document S16b–S16e scope and acceptance gates.

### S16b — Mobile Project Scaffold
Create `mobile/ophalo-mobile/`, Expo Router, TypeScript, `app.json` with bundle ID/scheme, basic
tab/stack shell, env vars (`.env.local` / `.env.example`). **Done when:** `npx expo start` launches
with no errors and the shell renders on iOS simulator.

### S16c — Auth and Secure Client Foundation
SecureStore token wrapper, persistent `appInstallationId`, typed API client with Bearer injection,
auth state bootstrap via `GET /auth/me`, sign-in screen (email input → calls `POST /auth/signin`
with mobile hint), deep-link callback handler (`ophalo://auth/callback?code=...`), logout. **Done
when:** a manually supplied bearer token can be stored, `GET /auth/me` succeeds, and a simulated
deep-link callback is handled correctly.

### S16d — Backend Mobile Auth and Device Contracts
Implement backend contract changes only: mobile `clientHint` magic links, mobile exchange returning
a one-time handoff code, handoff-code redemption endpoint, nullable device `pushToken`, required
EF migration(s), and focused integration test updates/additions. **Done when:** backend tests prove
mobile exchange no longer returns raw bearer tokens to `ophalo-web`, handoff redemption creates a
`MobileApp` bearer session, null-token devices register as push-ineligible, and `git diff --check`
is clean.

### S16e — Web Handoff and Mobile Device/Badge Hooks
Update `ophalo-web` `/auth/exchange` to render `MobileExchangeClient` when `?from=mobile`.
`MobileExchangeClient` calls backend with `clientType: "mobile_app"`, receives a short-lived
one-time handoff code, renders sterile "Device Authorized — Open Keep Mobile App" page with an
`ophalo://auth/callback?code=...` button. In mobile: redeem the handoff code for a bearer session,
device upsert call after auth, `GET /me/badge` TanStack polling on foreground/resume. **Done when:**
full browser-to-app auth handoff works locally on simulator, device upsert succeeds, badge polling
returns a count.

---

## Completed Slices

### S16b — Mobile Project Scaffold

Created `mobile/ophalo-mobile/` as an Expo Router + TypeScript project. Locked app identity in
`app.json`: display name `OpHalo Keep`, scheme `ophalo`, iOS bundle ID and Android application ID
`com.ophalo.keep`. Added `.env.example` with `EXPO_PUBLIC_API_URL` and `EXPO_PUBLIC_WEB_URL`.
Verified the scaffold launches on iOS simulator and TypeScript is clean.

### S16c — Auth and Secure Client Foundation

Added `expo-secure-store`, token storage, durable `appInstallationId` generation, typed API client
with Bearer injection, auth bootstrap via `GET /auth/me`, sign-in screen with
`clientHint: "mobile"`, deep-link callback route for `ophalo://auth/callback?code=...`, and
best-effort logout. Token-safe logging posture: API debug logging may include method/path/status
only, never authorization header material.

Verification: `npx tsc --noEmit` clean in `mobile/ophalo-mobile`; manual dev-token bootstrap
verified `GET /auth/me` and authenticated shell rendering.

### S16d — Backend Mobile Auth and Device Contracts

Committed backend contract changes for native mobile auth and device registration:
- `POST /auth/signin` accepts optional `clientHint: "mobile"` and appends `&from=mobile` to the
  emailed magic-link exchange URL.
- `POST /auth/exchange` with `clientType: "mobile_app"` returns a 10-minute one-time
  `{ handoffCode, expiresAtUtc }` instead of a raw bearer session token.
- `POST /auth/mobile-handoff/redeem` validates and atomically consumes the handoff code, creates a
  `SessionClientType.MobileApp` session, and returns `{ sessionToken, expiresAtUtc }` directly to
  the native app.
- `PUT /me/devices/{appInstallationId}` accepts omitted/null `pushToken`; null-token devices remain
  Active but push-ineligible with null token fingerprint/last-four, and token rebinding is skipped
  when no token exists.

Schema: Christian generated migration `S16dMobileHandoffAndNullableDeviceToken`, adding
`mobile_handoff_codes` and making `account_user_devices.push_token`,
`push_token_fingerprint`, and `token_last_four` nullable.

Verification recorded in commit `80a33a0`: 53 focused integration tests passed across auth magic
link, mobile handoff, and device registration.

---

## S16d Implementation Notes

S16d is backend-only. It must not touch `ophalo-web` or mobile UI files; those are S16e.

Implemented mutation families:
- Mobile sign-in hint and exchange behavior: `clientHint: "mobile"` on `/auth/signin`,
  `&from=mobile` magic links, and mobile `/auth/exchange` returning `{ handoffCode, expiresAtUtc }`
  instead of `{ sessionToken, expiresAtUtc }`.
- Mobile handoff-code creation/redeem: 10-minute one-time code, hash-only storage, atomic consume,
  same generic invalid-or-expired response for expired/consumed/unknown codes, and
  `SessionClientType.MobileApp` session creation on redeem.
- Nullable device token registration: omitted/null `pushToken` accepted, null derived fields, no
  token-rebinding revocation when no token exists, and push-ineligible handling for null-token rows.

Completed schema work:
- nullable `AccountUserDevices.PushToken`;
- nullable token-derived columns `PushTokenFingerprint` and `TokenLastFour`;
- new `mobile_handoff_codes` table/entity with hash, expiry, consumed timestamp, and safe
  account/user context for redemption.

Migration: Christian generated `S16dMobileHandoffAndNullableDeviceToken`.

Completed test posture:
- update existing `clientType: "mobile_app"` exchange tests to expect `handoffCode`;
- add redemption success, expired, consumed, unknown, and concurrent redemption tests;
- add nullable-push-token registration tests and response/raw-token redaction assertions;
- run focused auth/device integration tests.

Post-review tightening before S16e: mobile V1 sign-in remains existing-member only. New account
creation and invite acceptance stay web/PWA-owned; `clientType: "mobile_app"` must not provision an
`EntryContext.NewAccount` code. Add/keep focused tests for that boundary and for
`clientHint: "mobile"` producing `from=mobile` in the emailed link.

---

## Locked Decisions

### ADR-385 — ADR-236 promoted to Locked; push token library deferred to S18

ADR-236 (React Native + Expo + TypeScript) is now **Locked**. The only open question from
ADR-236 that remained — `expo-notifications` vs React Native Firebase Messaging for push token
capture — is **deferred to S18** (push delivery session). S16 stubs the push token so device
registration can be wired before real permissions are requested.

Locked stack for `mobile/ophalo-mobile/`:
- React Native + Expo (managed workflow)
- TypeScript
- Expo Router (file-based navigation)
- TanStack Query for API and server state
- Zustand only where local state cannot reasonably be server-derived
- `expo-secure-store` for session token and `appInstallationId`
- `expo-linking` for deep-link handling
- EAS Build deferred to S19; S16–S18 use local `npx expo run:ios` / `npx expo run:android`

### ADR-386 — App identity: OpHalo Keep, com.ophalo.keep, scheme ophalo://

- **App display name:** OpHalo Keep
- **Bundle ID (iOS) / Application ID (Android):** `com.ophalo.keep`
- **Custom URL scheme:** `ophalo` (all lowercase)
- **Auth callback deep link:** `ophalo://auth/callback?code={oneTimeHandoffCode}`
- **Push/request deep link:** `ophalo://keep/requests/{requestId}` (already established in ADR-358)

No branded or universal links in S16. Universal links require `apple-app-site-association` on
`ophalo.com` and Apple-side entitlement setup — that work belongs in S19 (store submission readiness).

### ADR-387 — Project location: mobile/ophalo-mobile/ in ophalo-foundation repo

The mobile app lives at `mobile/ophalo-mobile/` alongside `web/`. No `mobile/shared/` in S16;
add only when a concrete sharing need exists across two mobile projects.

### ADR-388 — Owner/Admin/Operator may use the mobile app; UX is phone-first for field operators

Owner, Admin, and Operator roles may sign in via the mobile app. The API already gates behavior by
role; the app should not add an Operator-only login wall. "Phone-first for operators" describes UX
priority and screen layout decisions, not a role restriction at sign-in. Viewer mobile access is
deferred until a deliberate read-only mobile UX exists.

### ADR-389 — Mobile sign-in uses existing /auth/signin with clientHint field

Mobile sign-in targets **existing members only** in V1. New account creation remains a browser-only
flow; mobile users of a new account sign in via the web first.

Sign-in flow:
1. Mobile app shows email input, taps "Send Magic Link".
2. App calls `POST /auth/signin` with body `{ email, clientHint: "mobile" }`.
3. `SignInBody` gains optional `ClientHint?: string`. `AuthEndpoints.SignIn` extracts it and passes
   to `SignInAuthService`.
4. `SignInAuthService.HandleAsync` gains a `clientHint` param. When `"mobile"`, the exchange URL
   becomes `{PublicBaseUrl}/auth/exchange?code={rawCode}&from=mobile`.
5. User taps the email link → browser opens `ophalo-web /auth/exchange?code=X&from=mobile`.
6. `page.tsx` extracts both `code` and `from` params; when `from=mobile`, renders
   `MobileExchangeClient` instead of `ExchangeClient`.
7. `MobileExchangeClient` calls `POST {NEXT_PUBLIC_API_BASE_URL}/auth/exchange` with
   `{ code, clientType: "mobile_app" }` (no `credentials: "include"` — no cookie needed). Same
   API-origin URL pattern as the existing `ExchangeClient`.
8. Backend returns `{ handoffCode, expiresAtUtc }`, not a raw bearer session token. The handoff code
   expires 10 minutes after creation.
9. `MobileExchangeClient` renders the mobile handoff page (ADR-390).
10. The native app receives `ophalo://auth/callback?code={handoffCode}` and calls the API to redeem
    the handoff code for `{ sessionToken, expiresAtUtc }`, then stores the session token in
    SecureStore.

`POST /auth/start` (new account) is not modified — new account creation is browser-only in V1.
`POST /auth/exchange` with `clientType: "mobile_app"` must not provision an `EntryContext.NewAccount`
code; the code must remain usable by the browser signup exchange path.

### ADR-390 — Mobile auth handoff page is sterile; one-time code delivered via click-bound scheme

`MobileExchangeClient` renders a minimal page with one action: "Open Keep Mobile App."

Constraints:
- No third-party scripts, no analytics, no external links.
- No `credentials: "include"` on the exchange fetch.
- Raw bearer token never appears in browser state, browser history, DOM text, custom URL scheme URLs,
  or web logs. The browser receives only a 10-minute, one-time handoff code.
- The `ophalo://auth/callback?code=` URL activates only via explicit tap/click, not
  `window.location.assign` on page load — prevents auto-open across browsers.
- Fallback copy: "If nothing happens, open the app and sign in again." No App Store or TestFlight
  routing until S19.
- The handoff code expires 10 minutes after creation, is single-use, randomly generated, stored only
  as a hash, and redeemed by the app directly with the API for a bearer session. Expired, consumed,
  and unknown codes fail closed with the same generic invalid-or-expired response. A failed or
  expired redemption requires requesting a new magic link.
- Custom scheme handoff is accepted only as an S16/S17 bridge. Store-submitted builds should prefer
  universal/app links once S19 configures Apple/Google domain association.

Accepted review-safe limitations (documented, not fixed in S16):
- The user may open the mobile-flavored email link on a desktop browser. The exchange still succeeds
  with `clientType: "mobile_app"`, but the "Open Keep Mobile App" button does nothing there — the
  fallback copy covers this; the user requests a new link from the device they want to use.
- The exchange consumes the magic-link code before the app handoff. If the tap fails or the app is
  not installed, the handoff code eventually expires and the user must request a new link. This is
  fail-safe: no raw bearer token has been exposed, and a consumed magic-link code cannot be replayed.
- A custom URL scheme can theoretically be claimed by another installed app. The value exposed to
  that scheme is only a one-time, 10-minute handoff code, not a bearer token. Universal/app links
  in S19 close this risk for store-submitted builds.

### ADR-391 — appInstallationId is durable client identity stored in expo-secure-store

`appInstallationId` is a UUID v4 generated once on first launch and stored in `expo-secure-store`.

- NOT `AsyncStorage` (unencrypted, easily cleared) and NOT runtime/memory state.
- Survives app restarts, updates, and re-authentication.
- Serves as the upsert key for `PUT /me/devices/{appInstallationId}` (ADR-356).
- Never reset by re-auth; only cleared by explicit uninstall or OS-level secure store wipe.
- Client generates the ID so it is stable across sessions without a server round-trip.

### ADR-392 — Mobile logout revokes session and device, then clears SecureStore session token

Logout sequence (best-effort; SecureStore clear always happens last):
1. `DELETE /me/devices/{appInstallationId}` — revokes device registration so no stale notification
   destination remains.
2. `POST /auth/logout` with `Authorization: Bearer <token>` — the existing logout endpoint already
   extracts bearer and revokes the session hash.
3. Clear session token from `expo-secure-store`.
4. Do NOT clear `appInstallationId` — ID is durable client identity (ADR-391). If API is
   unreachable, still clear the session token so the user is locally signed out.

### ADR-393 — Mobile environments for S16–S18 use two env vars only; EAS profiles in S19

S16–S18 use only:
- `EXPO_PUBLIC_API_URL` — points at `http://localhost:5092` locally or `https://api.ophalo.com`
- `EXPO_PUBLIC_WEB_URL` — points at `http://localhost:3000` locally or `https://ophalo.com`

No EAS build profiles, flavor machinery, or per-environment bundle ID suffixes until S19.
`.env.local` is gitignored; `.env.example` is committed with placeholder values.

### ADR-394 — S16d device registration omits pushToken; real token arrives in S18

`PUT /me/devices/{appInstallationId}` in S16d sends:
- `appInstallationId` (from SecureStore)
- `platform` (`"ios"` or `"android"` — from `expo-device` or `Platform.OS`)
- `appVersion` (from `expo-constants`)
- `pushToken`: omitted/null — no empty-string or sentinel values; the column stays null until S18
  supplies a real token

This requires a minor backend change: make `pushToken` optional/nullable on the device upsert endpoint.
Devices with no push token are registered as Active but push-ineligible; S18 updates the same
`appInstallationId` row with the real token via a second upsert.

Device name and device model are not sent — no extra collection beyond what ADR-355/356 established
as minimum fields.

### ADR-395 — Account creation and invite acceptance remain web-owned in V1

New account creation and invite acceptance remain owned by the web/PWA experience for V1. The native
app sign-in surface is for existing members only. This keeps mobile onboarding scope small, avoids
premature native signup/invite UX commitments before pilot customer usage, and reduces app-store
review surface while the product workflow is still being learned.

Recommended V1 flow:
- New owners create the account on web/PWA, then install/open the native app and sign in as an
  existing member.
- Invited members accept the invite on web/PWA. A later slice may add a deliberate post-success
  web-to-app handoff so they do not need a second email sign-in, but native invite acceptance is not
  part of S16.

Backend boundary: generic mobile exchange must not silently create accounts from new-account auth
codes. If a future mobile onboarding flow is desired, it should be designed as an explicit product
surface and contract rather than falling out of the sign-in handoff path.

---

## Required Backend/Web Contract Changes

All changes are bounded. Nothing touches the permission layer or Keep domain, but S16d intentionally
adds a small mobile handoff-code contract so the app foundation is suitable for Apple/Google review.

### Backend — src/OpHalo.Api/Auth/AuthEndpoints.cs and SignInAuthService.cs

**Change 1:** `SignInBody` record gains `ClientHint?: string`.

```csharp
internal sealed record SignInBody(string? Email, string? ClientHint);
```

`AuthEndpoints.SignIn` extracts `body.ClientHint` and passes it to `service.HandleAsync`.

**Change 2:** `SignInAuthService.HandleAsync` gains `clientHint` param.

When `clientHint == "mobile"`, the magic link URL appends `&from=mobile`:
```csharp
var mobileSuffix = clientHint == "mobile" ? "&from=mobile" : string.Empty;
var magicLink = $"{settings.Value.PublicBaseUrl}/auth/exchange?code={rawCode}{mobileSuffix}";
```

**Change 3:** mobile handoff code creation and redemption.

For `POST /auth/exchange` with `clientType: "mobile_app"`:
- consume the magic-link code as today;
- create a 10-minute one-time mobile handoff code tied to the account/user/session intent;
- return `{ handoffCode, expiresAtUtc }`;
- do not create or return the raw bearer session token to `ophalo-web`.

Add a mobile redemption endpoint, for example `POST /auth/mobile-handoff/redeem`, accepting
`{ handoffCode, deviceName? }`. It validates expiry/unused state, consumes the handoff code
atomically, creates a `SessionClientType.MobileApp` session, and returns
`{ sessionToken, expiresAtUtc }` directly to the native app. Store only handoff-code hashes; never
log raw handoff codes or session tokens. Add focused integration tests for success, expired,
already-consumed, unknown code, and concurrent redemption.
Expired, consumed, and unknown handoff codes must return the same generic invalid-or-expired error
shape so clients cannot distinguish which condition occurred.

The redeem endpoint creates the actual `AccountSession`, not `/auth/exchange`. Until a later mobile
session-lifetime ADR changes this, redemption uses the same session lifetime policy as the existing
mobile/browser session service. This default is explicit so S16d can ship; the mobile-specific
lifetime review remains deferred below.

Existing integration tests for the current `clientType: "mobile_app"` exchange path must be updated,
not treated as regressions. Tests that currently expect `/auth/exchange` to return
`{ sessionToken, expiresAtUtc }` for mobile should instead expect `{ handoffCode, expiresAtUtc }`
and cover redemption as the step that returns the bearer token.

`ExchangeBody.DeviceName` becomes vestigial for the mobile exchange step because the exchange no
longer creates the mobile session. Keep it optional and harmless there; wire device name, if used at
all, into handoff redemption where the `MobileApp` session is created.

**Change 4:** EF schema changes.

S16d requires EF schema work in addition to API/service edits:
- make `AccountUserDevices.PushToken` nullable;
- make token-derived columns such as `PushTokenFingerprint` and `TokenLastFour` nullable if the
  current mapping requires it;
- add the mobile handoff-code table/entity for hashed one-time codes, expiry, consumed timestamp,
  and enough account/user/session-intent data to redeem safely.

These can be one migration or multiple migrations, but the migration scope must be explicit in S16d.
Christian runs migration commands; the coding session should create/inspect migration files and
document the exact command/output needed for Christian to apply them.

**Change 5:** `PUT /me/devices/{appInstallationId}` — make `pushToken` optional.

Accept `null` or absent `pushToken` in S16. Device upserts with no token register as active but
push-ineligible. S18 updates the token. No status enum change required; routing/eligibility checks
must treat missing tokens as non-delivery. This requires updates below the endpoint too:
`AccountUserDevice` entity nullability/invariants, `AccountUserDeviceService.RegisterAsync`,
`DeviceRegistrationResult`, persistence mappings/tests, and API response typing. Derived fields
follow the token: `PushTokenFingerprint` and `TokenLastFour` are null when the token is null, and
the ADR-355 token-to-user rebinding revocation check is skipped for null tokens (there is no binding
to steal).

### Web — web/ophalo-web/src/app/auth/exchange/

**Change 6:** `page.tsx` — extract `from` search param alongside `code`.

```tsx
const { code, from } = await searchParams;
```

When `from === "mobile"`, render `<MobileExchangeClient code={trimmedCode} />` instead of
`<ExchangeClient code={trimmedCode} />`.

**Change 7:** New `MobileExchangeClient.tsx`.

- Calls `POST ${NEXT_PUBLIC_API_BASE_URL}/auth/exchange` with `{ code, clientType: "mobile_app" }`
  (no `credentials: "include"`).
- On success: receives `{ handoffCode }`, renders sterile "Device Authorized" page with one button.
- Button `onClick`: `window.location.href = \`ophalo://auth/callback?code=${handoffCode}\`` —
  click-bound, not auto-triggered.
- On failure: same error redirect patterns as `ExchangeClient`.
- No analytics, no external scripts, no raw bearer token in DOM text or URLs.

---

## S16 Acceptance Gates

**S16b done when:** `npx expo start` (or `npx expo run:ios`) launches, shell renders, no TypeScript
errors.

**S16c done when:** a manually supplied bearer token is stored in SecureStore, `GET /auth/me`
returns authenticated user, and the app handles a simulated `ophalo://auth/callback?code=X`
deep link by attempting handoff-code redemption.

**S16d done when:** backend contract changes compile and focused integration tests pass for mobile
`clientHint`, handoff-code exchange/redeem success and failure modes, updated mobile exchange
expectations, nullable push-token device registration, null derived-token fields, and migration
generation/inspection. No web/mobile UI work is included.

**S16e done when:** the full sign-in → email → browser handoff → tap "Open Keep Mobile App" →
app receives handoff code → app redeems code → `GET /auth/me` succeeds → device upsert registers →
badge polling returns a count — all verified locally on simulator.

---

## Explicitly Deferred

- Real push permissions request, APNs/FCM token capture — S18
- `expo-notifications` vs React Native Firebase Messaging choice — S18
- Universal links / `apple-app-site-association` / Android App Links — S19, required before store
  submission unless explicitly waived with a review-safe rationale
- EAS Build profiles, per-environment bundle ID suffixes, TestFlight/internal testing — S19
- Numeric in-app code entry for reviewer/demo login — S19 candidate; not S16 because it requires
  brute-force/rate-limit/expiry design beyond the magic-link handoff path
- Mobile session lifetime policy — decide before external TestFlight/store review; default inherited
  browser windows may be too short for a field app
- Suspend/remove membership should revoke device rows as well as sessions — S18 before real push
  delivery
- App Store icons, screenshots, privacy labels, signing profiles — S19
- Operator list, request detail, Quick Capture, contact actions — S17
- Native share, minimum-client enforcement, app update prompts — later product hardening
- Viewer-role mobile access/read-only UX — deferred until deliberately designed
