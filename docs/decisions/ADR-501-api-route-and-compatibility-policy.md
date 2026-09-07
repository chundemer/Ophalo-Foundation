# ADR-501 — API route shape and compatibility policy

**Status:** Locked
**Date:** 2026-09-07
**Context:** Raised during the GAP-038 / ADR-500 preflight — ADR-500 drafted `GET /api/v1/updates`
and `POST /api/v1/feedback`, but no live route carries an `/api` or version prefix. This ADR fixes
the convention so ADR-500 and every later build-log has one thing to cite.
**Related:** ADR-500 (first consumer — routes corrected to the flat form), ADR-236 (native mobile
app), ADR-497 (`/auth/*` routes), ADR-495 (Sentry release/version tagging)

## Decision

### 1. No global API versioning; flat resource-scoped routes

- OpHalo exposes **one API surface** consumed only by first-party clients (`ophalo-app`, the native
  mobile app, internal callers). There is no third-party API contract.
- Routes are **flat and resource-scoped** — `/auth/*`, `/accounts/*`, `/keep/*`, and now
  `/updates`, `/feedback` — with **no `/api` prefix and no `/v1` path segment**. This matches every
  existing route; introducing a prefix now would create a mixed convention or force a
  no-value migration of ~40 endpoint files, the frontend `apiClient`, and their tests.
- URL path versioning (`/v1` → `/v2` route trees) is explicitly **not adopted**. It solves a
  problem OpHalo does not have (independent consumers on divergent release cycles) at a standing
  maintenance cost.

### 2. Ownership follows the domain, not the nearest workbench

- An endpoint's project/namespace is chosen by **which layer owns the concept**, not by which UI
  first surfaces it. Platform and support concerns (in-product updates, feedback, health,
  entitlements) are **Foundation**-owned even when only the Keep workbench renders them today.
  Keep-workbench domain operations stay under `/keep/*` in the Keep projects.
- Consequence for ADR-500: `GET /updates` and `POST /feedback` are **Foundation** endpoints and
  services (not `/keep/*`), respecting the "Foundation must not reference Keep" boundary.

### 3. Compatibility policy — additive by default

- Changes to a shared endpoint are **additive**: new optional request fields, new response fields,
  new enum members handled as unknown-safe by clients. Additive changes ship without ceremony.
- A **breaking** change to a request/response contract is avoided; when genuinely unavoidable it is
  scoped to the **single affected endpoint** via an explicit request header
  (`X-OpHalo-<Resource>-Contract: <n>` or equivalent), never a new global route tree. The existing
  per-resource concurrency headers (`X-Keep-*-Version`) are the precedent for endpoint-local
  versioning.
- Web and API deploy close to lockstep, so web/API skew is handled by additive discipline plus
  coordinated release.

### 4. Native-client / backend skew — deferred handshake

- An installed mobile app can lag the backend. The solution is a **minimum-supported-client
  handshake**, not versioned routes: the client sends its build identifier on every request; the
  server may respond `426 Upgrade Required` when the build is below the supported floor.
- Design and implementation of that handshake is **deferred to the native mobile track (ADR-236)**;
  it is not built now. Until then, additive discipline (§3) is the only compatibility mechanism.

## Consequences

- ADR-500's routes are `GET /updates` and `POST /feedback`, Foundation-owned.
- No `/api` or `/v1` prefix is added to any route. A future decision to add a prefix would be its
  own ADR with a full migration plan.
- Breaking a shared contract now has a defined, narrow escape hatch (endpoint-local header) and a
  clear default (don't — make it additive).
- The mobile skew risk is named and assigned rather than silently carried.

## Out of scope

Global/date-based API versioning, an `/api` path prefix, a public/partner API contract, GraphQL or
RPC transports, the native min-version handshake implementation (ADR-236), and any change to the
existing `X-Keep-*-Version` concurrency-token semantics.
