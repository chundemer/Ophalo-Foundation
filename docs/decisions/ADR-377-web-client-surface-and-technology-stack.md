# ADR-377 — Web Client Surface And Technology Stack

**Date:** 2026-06-28  
**Status:** Locked  
**Source:** Session 13 preflight discussion; build plan section 9.1; ADR-236; ADR-290; UX design docs

## Context

Session 13 starts the first real authenticated web application surface. Keep is the first product
to consume the web foundation, but `ophalo-app` must be built as reusable OpHalo application
infrastructure rather than a one-off Keep pilot UI. The repo already reserves two web directories:

- `web/ophalo-app`
- `web/ophalo-web`

The legacy reference app used Next.js for both app and public web surfaces, but this greenfield
Foundation build deliberately does not copy the reference stack wholesale. `OpHalo.Api` is already
the server authority and owns authentication, authorization, persistence, and API contracts.

Keep V1 also has a locked freshness posture: fresh, not realtime. It does not use SSE/WebSockets for
go-live. Clients refresh through refetch-after-write, focus/resume sync, pull-to-refresh, limited
active-list polling, server-derived counts/badges, and push/deep links where applicable.

## Decision

Use separate client surfaces:

- `web/ophalo-app` is the authenticated Keep/PWA workbench.
- `web/ophalo-web` is the public marketing, signup/start, invite-accept, public-intake,
  customer-tracker, and legal surface.
- The native mobile app is a later React Native + Expo + TypeScript project under `mobile/` or
  `apps/`, not under `web/` (ADR-236).

Use **Vite + React + TypeScript** for `web/ophalo-app`.

Use **Next.js + React + TypeScript** for `web/ophalo-web`.

Shared frontend stack:

- Tailwind CSS for styling.
- Lucide React for icons.
- Accessible primitives in the Radix/shadcn style where useful.
- TanStack Query for server state in operational clients.
- Self-hosted OpHalo type assets loaded from the app origin, not Google Fonts or another third-party
  font CDN.
- Brand kit and UX design docs are authoritative for visual language.

## Rationale

`ophalo-app` is an authenticated operational workbench. It does not need SEO, public crawling,
server components, incremental static generation, or SSR to fulfill its first job. A Vite React SPA
keeps the app boundary clean: static client assets call `OpHalo.Api`, and the API remains the only
server authority. `ophalo-app` must not become a backend-for-frontend or a second server authority
for tenant security, authorization, persistence, orchestration, or background work.

The first app surface should establish decisions future OpHalo applications can inherit: stable
design tokens, local brand assets, a small component primitive layer, typed API boundaries, and
credentialed API access through the server-owned auth model. Session 13 must not optimize for a
temporary "good enough for pilot" UI that later products have to unwind.

TanStack Query maps directly to Keep's V1 freshness model:

- fetch functions throw typed errors for non-2xx API responses so query retries, error states, and
  auth invalidation behave normally;
- mutation success invalidates authoritative query keys;
- app/tab focus refetches active state;
- active operational views may poll every 30-60 seconds;
- hidden/backgrounded surfaces pause or slow polling;
- server state stays authoritative.

`ophalo-web` is different. It owns public trust and entry surfaces where Next.js is a better fit:
marketing pages, signup/start, invite accept, public intake, customer tracker pages, metadata,
legal pages, and later SEO-sensitive content.

## Consequences

- S13 scaffolds `web/ophalo-app` as a Vite React TypeScript PWA/workbench, not as Next.js.
- `web/ophalo-app` is the first consumer of a reusable OpHalo web foundation: Keep-specific screens
  sit on top of shared app-shell, typography, token, and API-client decisions.
- App API calls are credentialed browser requests to `OpHalo.Api`; the client fetch wrapper must set
  `credentials: "include"` and throw typed errors for non-2xx responses.
- Reference-app code may be used for product/design learning, but its Next.js architecture and SSE
  list-refresh behavior are not copied by default.
- `web/ophalo-web` remains the natural home for public auth entry and customer-facing routes.
- Shared DTO/client types may be factored later if duplication becomes costly.
- CORS, cookie scope, and environment variables must support cross-subdomain app/API calls.

## Deferred

- Native app project scaffolding remains Session 14 scope.
- Public/customer `ophalo-web` implementation remains a later web slice unless required for S13 auth
  entry.
- SSE/WebSocket realtime list streaming remains deferred per ADR-290 / DEF-017.
