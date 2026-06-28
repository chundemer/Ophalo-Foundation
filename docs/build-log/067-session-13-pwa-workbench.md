# Build Log 067 — Session 13 PWA Workbench

**Date:** 2026-06-28  
**Status:** Pre-work locked; implementation pending  
**Current ADR after S13 pre-build decisions:** ADR-379

## Session Intent

Session 13 starts `web/ophalo-app`, the authenticated OpHalo application workbench. Keep is the first
product surface to use it, but the implementation must establish reusable OpHalo web foundation
patterns rather than a temporary Keep-only pilot UI.

## S13a Scope

S13a is the first independently useful web slice:

- scaffold `web/ophalo-app` as a standalone Vite + React + TypeScript app;
- add the authenticated app shell and guarded home route;
- use a hand-written typed fetch client with credentialed API calls;
- call live `GET /auth/me` and `GET /keep/setup/onboarding`;
- render an Owner/Admin home screen with the onboarding checklist as the primary surface;
- render a Viewer access-limited state;
- redirect unauthenticated users to `PublicBaseUrl` auth entry;
- self-host Source Serif 4 and Inter under `public/fonts/`;
- preload the core self-hosted font assets from `index.html`;
- add a minimal PWA manifest, deferring service worker/offline caching.

Out of S13a:

- request list/detail;
- Quick Capture;
- member management;
- tracker sharing;
- customer update composer;
- Operator workbench shell;
- placeholder navigation for unbuilt workflows;
- MSW/test mocking infrastructure unless a tested utility or hook requires it.

## Locked Decisions

- `ophalo-app`: Vite + React + TypeScript.
- `ophalo-web`: future Next.js public/auth/customer surface.
- Package manager: `pnpm`.
- Project shape: standalone app for now; workspace only when a second package makes sharing real.
- Styling: Tailwind CSS with OpHalo/Keep tokens from the active UX design model.
- Typography: Source Serif 4 headings and Inter body/UI, self-hosted from the app origin.
- Components: small Radix/shadcn-style primitive set only as needed by the shell/home.
- Icons: Lucide React.
- Server state: TanStack Query with the ADR-377 freshness model.
- API client: hand-written typed fetch client first; OpenAPI generation can replace it later.
- Auth: browser sessions are HttpOnly cookies; frontend never reads or stores session tokens.
- Local origins: API `http://localhost:5092`, app `http://localhost:5173`, public web
  `http://localhost:3000`.
- Local cookie posture: Development must omit the cookie `Domain` attribute so `ophalo.sid` is a
  host-only localhost cookie; do not configure `.localhost` or `localhost:port` as a cookie domain.
- Local/API credential posture: every app API request uses `credentials: "include"` and API CORS
  allows the explicit app origin, not wildcard origins.
- Base URL vocabulary: `PublicBaseUrl` for public/auth entry, `AppBaseUrl` for authenticated app.
- Home screen: onboarding checklist primary; no fake request summary and no `/keep/requests` call.

## S13a Integration Requirements

### Local Cookies And CORS

Production uses `Auth:CookieDomain=.ophalo.com` so `ophalo.sid` can be shared across
`app.ophalo.com` and `api.ophalo.com`.

Local development must not attempt a shared localhost cookie domain. With `Auth:CookieDomain` empty,
`AuthCookieOptionsFactory` omits the `Domain` attribute and produces a host-only cookie, which modern
browsers accept for the API host. The Vite app must make credentialed requests with
`credentials: "include"`, and API CORS must allow `http://localhost:5173` explicitly with
credentials enabled.

### Return-To Auth Redirects

The unauthenticated app guard must not blind-redirect to the public auth entry. It must append the
current app path/query as a `return_to` value when redirecting to `PublicBaseUrl`.

`ophalo-web` later owns validation and final routing for that value:

- accept only same-app destinations rooted under configured `AppBaseUrl`;
- reject external origins and malformed values;
- after successful exchange/accept, route back to the validated destination or the default app home.

S13a only needs to emit the parameter correctly and document the contract because `ophalo-web` is
out of this slice.

### Fetch Client Error Contract

The typed fetch client must treat non-2xx responses as thrown errors, not `{ data, error }` success
payloads. The thrown error should carry the HTTP status and server-derived problem `code` when
available.

This preserves TanStack Query behavior for retries, error states, error boundaries, and auth/session
invalidations. In particular, a `401` from `/auth/me` must throw so the route guard can clear local
view state and redirect through the auth entry path.

### Self-Hosted Font Loading

`index.html` must preload the primary self-hosted variable font files from `/fonts/...` using
`rel="preload"`, `as="font"`, the correct `type`, and `crossorigin`. CSS remains responsible for the
`@font-face` declarations and `font-display` policy.

## Local Auth Path

S13a must make local authenticated browser testing reliable without weakening production behavior.

Locked implementation direction:

- production email remains provider-backed;
- when email provider secrets are absent in local development, use an explicit developer-only console
  email sender fallback so magic-link URLs can be copied from API logs;
- document `/auth/start` or `/auth/signin` -> magic link -> `/auth/exchange` -> `ophalo.sid`
  cookie -> `localhost:5173` in the local web setup runbook.

## Done Gate

- `pnpm` install succeeds for `web/ophalo-app`;
- `pnpm tsc --noEmit` or equivalent typecheck is clean;
- `pnpm build` succeeds;
- local dev server runs;
- browser screenshot shows the authenticated Owner/Admin home screen;
- docs/runbook local web setup exists with required environment values and auth steps.
