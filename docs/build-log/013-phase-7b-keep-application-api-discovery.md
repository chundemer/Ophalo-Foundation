# Build Log 013 - Phase 7B discovery: Keep application + API vertical

**Date:** 2026-06-15
**Phase:** 7B - Keep intake/operator-list application + API discovery
**Target repo:** `/Users/christian/saas/ophalo-foundation`
**Reference repo:** `/Users/christian/application/ophalo` (read-only)

---

## Purpose

Phase 7A proved the Keep schema and domain contracts. Phase 7B now builds the
first runtime vertical:

- customer submits public intake;
- backend resolves the account from the Keep public intake link;
- Keep request + request-created event are persisted;
- operator can see the request in an account-scoped list.

This discovery pass resolves the open decisions from build-log/011 so
implementation can stay narrow and testable.

---

## Reference behavior checked

Legacy public intake uses:

- `POST /continuity/public-intake/token/{publicIntakeToken}`;
- request body: `CustomerName`, `CustomerPhone`, `Description`,
  `CustomerEmail?`, `EmailNotificationsEnabled`;
- public route has no authorization and uses rate-limit policy
  `public-intake`;
- account identity is resolved server-side from the intake token only.

The target repo keeps the proven behavior but uses Keep naming as the primary
surface.

---

## Decisions

### ADR-051 - Keep route primary, Continuity public-intake alias

The canonical new public route is:

```text
POST /keep/public-intake/token/{publicIntakeToken}
```

Also map the legacy alias during cutover:

```text
POST /continuity/public-intake/token/{publicIntakeToken}
```

Both routes must call the same handler/service and return the same response
shape. Do not introduce new `Continuity` namespaces, projects, tables, or
application types; the alias is an API compatibility shim only.

The operator route should be Keep-only in 7B:

```text
GET /keep/requests
```

Reason: public intake URLs may already be external contracts. Operator traffic
is authenticated app traffic and can move with the new app shell.

### ADR-052 - Token-only backend route first

Do not require `publicSlug` in the first backend route. The route token remains
the only authority, and the service resolves `AccountId` through
`KeepPublicIntakeLink.TokenHash`.

`PublicSlug` remains stored and unique because the future web/customer URL may
use a friendly path such as:

```text
/q/{publicSlug}/{publicIntakeToken}
```

If that route is added later, the slug is context/display only. A slug mismatch
must not authorize access; token hash resolution remains authoritative.

### ADR-053 - Defer intake email delivery to notifications

Phase 7B stores the customer's email and accepts `EmailNotificationsEnabled` in
the request body for contract continuity, but it does not send email or create
notification audit rows.

Reason: "customer submits intake -> operator sees request" does not require
outbound messaging. Email delivery, notification preferences, audit rows, quiet
hours, and retries belong to the later notifications phase. `IEmailSender`
already exists in Foundation.Application, but 7B should not wire a transport or
notification workflow yet.

### ADR-054 - 7B can use ICurrentUser fakes before real auth

Real Phase 5 session/auth implementation is not a blocker for 7B.

The operator list service should authorize through the existing Foundation
composition:

- `ICurrentUser` supplies `AccountId` and authenticated user context;
- `UserAccessPolicy` checks `PermissionKeys.Keep.RequestsView`;
- `FeatureAccessPolicy` checks `FeatureKeys.Keep.OperatorQueue`;
- account lifecycle/commercial access is evaluated through
  `AccountAccessPolicy`.

HTTP/integration tests may use a test-only `ICurrentUser` implementation and
test authentication setup until the real session stack exists. The production
API must still fail closed when `ICurrentUser.IsAuthenticated` is false or the
membership/account checks cannot be satisfied.

---

## Phase 7B implementation target

Keep.Application:

- Add a public intake application service, e.g. `CreateKeepPublicIntakeRequest`.
- Input mirrors the legacy body:
  `CustomerName`, `CustomerPhone`, `Description`, `CustomerEmail?`,
  `EmailNotificationsEnabled`.
- Resolve active `KeepPublicIntakeLink` by hashed token. Never accept client
  `AccountId`.
- Load account + entitlements, then compose:
  - `AccountAccessPolicy`;
  - `FeatureAccessPolicy.IsEnabled(plan, FeatureKeys.Keep.PublicIntake)`;
  - public-safe unavailability for missing/revoked token, unavailable account,
    missing entitlements, blocked access, or disabled feature.
- Upsert `KeepCustomer` by `(AccountId, PrimaryPhone)`; update name/email for
  repeat intake.
- Create `KeepRequest` and `KeepRequestEvent.CreateRequestCreated(...)`.
- Generate page token/reference code server-side through `KeepTokenService`.
  Handle rare unique collisions with a bounded retry around save.
- Return a minimal success response with request id, reference code, and page
  token/page URL material only if the existing domain contract supports it.

Keep.Application operator list:

- Add an account-scoped query/service for open/non-terminal Keep requests.
- Use `ICurrentUser.AccountId` as the account boundary.
- Require authenticated current user.
- Gate with `PermissionKeys.Keep.RequestsView` and
  `FeatureKeys.Keep.OperatorQueue`.
- Return the minimal list needed for the exit gate:
  request id, reference code, status, customer name/phone/email, description,
  current status text, last activity timestamps, created/updated timestamps.
- Defer rich list behavior: attention score, first-response SLA, search,
  closed-history pagination, actor names, SSE.

OpHalo.Api:

- Replace the template weather endpoint with real composition root wiring.
- Register `OpHaloDbContext` with Npgsql, snake_case naming, Foundation
  migrations history, and the Keep model assembly.
- Register `SystemClock`, Keep services, Foundation policies, and any 7B
  application services.
- Add configuration key `ConnectionStrings:DefaultConnection`.
- Add `public-intake` rate limiting and map it to both public intake routes.
- Map `GET /keep/requests` behind authentication/authorization plumbing. Until
  Phase 5 auth exists, tests can use a test host override; production should not
  invent a header-based auth shortcut.

Tests:

- Unit tests for public intake service:
  - invalid/missing token fails public-safe;
  - revoked link fails;
  - blocked account/access/feature fails;
  - repeat phone updates existing customer;
  - request and `request_created` event are produced.
- Unit tests for operator list gating and account scoping.
- HTTP/integration tests for:
  - `POST /keep/public-intake/token/{token}`;
  - `POST /continuity/public-intake/token/{token}` alias;
  - `GET /keep/requests` returns only the caller account's open requests;
  - unauthenticated or unauthorized operator list fails closed;
  - public intake route is rate-limit decorated.

---

## Out of scope for 7B

- Customer page `/r/{pageToken}` or `/q/{slug}/{token}` endpoints.
- Real magic-link/session auth.
- Customer email delivery, notification audit rows, quiet hours, retries.
- Operator mutations, request detail, SSE, attention policies, search, closed
  history, mobile push/browser push.
- MediatR/FluentValidation unless a later slice establishes them as local
  patterns.

---

## Exit criteria for implementation

- Application services compile and are covered by focused tests.
- API routes are mapped and covered by HTTP/integration tests.
- DbContext host registration includes the Keep model assembly.
- Public intake never trusts client `AccountId`.
- Operator list is account-scoped and fail-closed.
- Alias route exists only as a route shim; no active `Continuity` code family is
  reintroduced.
- Build, unit, architecture, and integration suites remain green.
