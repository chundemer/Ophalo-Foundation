# ADR-379 — Web Foundation And Self-Hosted Assets

**Date:** 2026-06-28  
**Status:** Locked  
**Source:** Session 13 foundation discussion; ADR-367; ADR-368; ADR-377; UX design docs

## Context

Keep is the first OpHalo application, but it is not the only intended application. Session 13 starts
the authenticated web workbench and therefore sets patterns future OpHalo apps are likely to inherit.

The UX model already locks the parent/product visual system:

- OpHalo is the parent brand.
- Keep is the first product Halo and uses the shared OpHalo foundation with a Keep accent.
- Typography is Source Serif 4 for headings and Inter for body/UI text.

The initial implementation choice for font loading must be treated as a foundation decision, not as a
temporary pilot convenience.

## Decision

Authenticated OpHalo web applications must self-host core brand assets by default.

For Session 13:

- `web/ophalo-app` loads Source Serif 4 and Inter from app-origin font files under
  `public/fonts/`.
- CSS uses explicit `@font-face` declarations rather than Google Fonts `@import` or remote font CDN
  links.
- The app HTML preloads the primary self-hosted variable font assets with `rel="preload"` so the
  shell can request typography in parallel with the main JavaScript bundle.
- The font and token organization should make a later shared frontend brand package possible without
  changing product screen code.
- Keep-specific UI may use Keep tokens and components, but the app shell, typography, primitive
  component posture, API-client boundary, and authenticated routing posture are OpHalo web foundation
  concerns.

Local development support may add developer-only infrastructure such as a console email sender when
provider secrets are absent. Such support must stay environment-scoped and must not weaken the
production auth model: production email remains provider-backed, auth tokens remain opaque and
server-side, browser sessions remain HttpOnly-cookie based, and frontend code never reads or stores
session tokens.

## Rationale

Self-hosting brand assets is the stronger default for authenticated work surfaces:

- avoids third-party font requests from authenticated product usage;
- removes an external runtime dependency from the app shell;
- gives more predictable caching and availability behavior;
- keeps typography consistent across future OpHalo products;
- aligns with the product-family model where future Halos share the OpHalo foundation.

This is not a rejection of Google Fonts as a service. It is a product-foundation choice: the
authenticated workbench should own the assets that define its brand and interface contract.

## Consequences

- Do not use Google Fonts imports in `ophalo-app`.
- Preload core self-hosted fonts from `index.html`; use CSS `@font-face` for the actual family,
  weight, and display contract.
- Font files become committed static assets or later move into a shared OpHalo frontend package.
- S13a should keep the primitive/component set small, but durable: add only what the shell/home
  actually uses.
- Pilot-specific shortcuts must be avoided unless they are explicitly local-development-only and
  impossible to confuse with production behavior.

## Deferred

- A shared frontend package for OpHalo tokens, fonts, and primitive components.
- Self-hosted asset strategy for `ophalo-web` when that surface is scaffolded.
- Broader asset pipeline decisions such as subsetting, preload policy, and cache-busting strategy
  beyond the first Vite app.
