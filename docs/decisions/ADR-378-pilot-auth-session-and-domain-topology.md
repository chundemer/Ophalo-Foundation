# ADR-378 — Pilot Auth, Session, And Domain Topology

**Date:** 2026-06-28  
**Status:** Locked  
**Source:** Session 13 preflight discussion; ADR-006; ADR-062; ADR-063; ADR-075; ADR-082

## Context

OpHalo already uses magic links for entry/recovery and trusted opaque server-side sessions. Browser
clients use the `ophalo.sid` HttpOnly cookie. Mobile clients can use the same opaque token through
`Authorization: Bearer <token>`, with bearer resolution first (ADR-062).

Small business users must not be forced through email login as a daily ritual. At the same time,
access must be revocable immediately for suspended/removed employees, role changes, and account
commercial/lifecycle changes.

Session 13 also introduces distinct public and authenticated web surfaces:

- public/auth/customer entry on `ophalo-web`;
- authenticated workbench on `ophalo-app`;
- API on `OpHalo.Api`.

## Decision

Magic link remains the V1 entry and recovery mechanism. It is used for first sign-in, invite accept,
new device/browser access, expired sessions, and recovery. It is not the daily-login model.

Browser/PWA daily use relies on durable opaque server-side sessions:

- `SessionInactivityWindowDays = 30`
- `SessionAbsoluteExpiryDays = 60`

This supersedes ADR-063's original `SessionInactivityWindowDays = 7` pilot value and extends the
absolute lifetime from 30 to 60 days.

Continuous authorization remains mandatory. Every authenticated API request revalidates current
session, user, account-user membership, role/action permission, account lifecycle, commercial state,
operating mode, and feature gates as applicable. Long-lived login convenience does not create
long-lived access trust.

Production domain topology:

- `ophalo.com` / `www.ophalo.com` -> `web/ophalo-web`
- `app.ophalo.com` -> `web/ophalo-app`
- `api.ophalo.com` -> `OpHalo.Api`

Production auth cookie settings:

- `Domain=.ophalo.com`
- `Secure`
- `HttpOnly`
- `SameSite=Lax`
- `Path=/`

Because `ophalo.com`, `app.ophalo.com`, and `api.ophalo.com` share the same eTLD+1, `SameSite=Lax`
supports credentialed cross-subdomain app/API requests while avoiding broader cross-site cookie
behavior.

Invite and auth entry flow:

- Sign-in, signup/start, auth exchange, and invite accept pages live on `ophalo-web`.
- After successful exchange/accept, `OpHalo.Api` sets the parent-domain session cookie.
- The user is redirected to `ophalo-app` for authenticated work.

Configuration vocabulary should be clarified:

- `PublicBaseUrl` points to `ophalo-web` entry flows.
- `AppBaseUrl` points to the authenticated `ophalo-app` workbench.
- Existing `OperatorBaseUrl` usage from ADR-075 must be reviewed and either renamed/mapped to the
  public invite-accept surface or replaced by the clearer `PublicBaseUrl`/`AppBaseUrl` split.

Fallback numeric email codes are deferred for V1 web/PWA pilot. Revisit before native auth or if
pilot evidence shows magic-link UX is brittle.

Native auth is not S13 scope. S14/native uses the existing opaque bearer-session contract and stores
the raw bearer token only in platform secure storage such as iOS Keychain or Android Keystore.

## Rationale

This balances convenience and safety:

- users open Keep and work without daily email friction;
- sessions remain opaque and server-side, so they can be revoked immediately;
- each request reloads current authorization posture;
- removed/suspended employees and blocked/canceled accounts fail on the next request;
- parent-domain cookies avoid one-time redirect-token complexity for pilot;
- public entry and authenticated workbench surfaces stay cleanly separated.

## Implementation Notes

- `AuthConstants.SessionInactivityWindowDays` and `SessionAbsoluteExpiryDays` carry the pilot values.
- `AuthCookieOptionsFactory` already centralizes `HttpOnly`, `SameSite=Lax`, secure-outside-dev, and
  configured domain behavior. Production configuration must set `Auth:CookieDomain=.ophalo.com`.
- CORS must allow credentialed requests from `https://app.ophalo.com` and the public/auth web origin
  where needed. Wildcard CORS is not valid for credentialed browser requests.
- Local development must keep `Auth:CookieDomain` empty so browsers receive a host-only localhost
  cookie. Do not configure `.localhost` or `localhost:port` as a cookie domain.
- Local app API calls must use credentialed fetch (`credentials: "include"`) and CORS must allow the
  explicit app origin such as `http://localhost:5173`.
- Auth redirects from `ophalo-app` to `ophalo-web` should carry a validated return-to intent so deep
  links can resume after successful exchange without introducing open-redirect behavior.

## Deferred

- Numeric fallback code in magic-link email.
- Native deep-link handling, app-link/universal-link routing, and native invite accept.
- One-time redirect-token handoff between web surfaces if future domain constraints prevent
  parent-domain cookie sharing.
