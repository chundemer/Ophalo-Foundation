# Build Log 011 - Phase 7 discovery: Keep intake to operator view

**Date:** 2026-06-15
**Phase:** 7 - Keep Core Vertical Slice 1: intake to operator view
**Target repo:** `/Users/christian/saas/ophalo-foundation`
**Reference repo:** `/Users/christian/application/ophalo` (read-only)
**Current V1 lock:** Later V1 scope decisions in
`043-keep-v1-product-scope-and-freshness-lock.md` supersede this discovery log's early notification
timing questions. Basic push/badges are V1 pre-go-live; the full notification platform remains later.

---

## Purpose

Phase 7 starts the first Keep product slice: a customer submits a public request
and an operator can see it. The build plan names four behaviors:

- public intake token resolution;
- public request submission;
- Keep request creation;
- operator request list read.

Discovery goal: decide the implementation split and lock the first implementation
session so the Keep port does not pull the whole legacy Continuity surface forward
at once.

---

## Reference behavior inspected

Legacy source of truth is `OpHalo.Continuity.*`; target naming is `OpHalo.Keep.*`.

Relevant legacy files inspected:

- `OpHalo.Continuity.API/Endpoints/Public/PublicIntakeEndpoints.cs`
- `OpHalo.Continuity.API/Requests/PublicIntakeBody.cs`
- `OpHalo.Continuity.Application/ContinuityRequests/Commands/CreatePublicContinuityRequestByToken/*`
- `OpHalo.Continuity.Application/ContinuityRequests/PublicCustomerAccessGuard.cs`
- `OpHalo.Continuity.Application/ContinuityRequests/Queries/GetContinuityRequestList/*`
- `OpHalo.Continuity.Core/Entities/ContinuityCustomer.cs`
- `OpHalo.Continuity.Core/Entities/ContinuityRequest.cs`
- `OpHalo.Continuity.Core/Entities/ContinuityRequestEvent.cs`
- `OpHalo.Continuity.Infrastructure/Persistence/Configurations/ContinuityCustomerConfiguration.cs`
- `OpHalo.Continuity.Infrastructure/Persistence/Configurations/ContinuityRequestConfiguration.cs`
- `OpHalo.Continuity.Infrastructure/Persistence/Configurations/ContinuityRequestEventConfiguration.cs`
- `OpHalo.Continuity.Infrastructure/Services/PageTokenGenerator.cs`

Behavior to preserve:

- The public endpoint accepts only a public intake token and request body; it never
  accepts client-supplied `AccountId`.
- Token resolution is server-side and fail-closed: missing token, unavailable
  account, missing entitlements, or disabled capability all return public-safe
  unavailability.
- Customer identity is scoped by `(AccountId, normalized phone)`; repeat intake
  updates name/email instead of creating duplicate customers.
- Request creation writes a request plus a `request_created` event.
- Page token and reference code are generated server-side and uniqueness is backed
  by DB constraints.
- Operator list is account-scoped and gated by authentication/entitlement.

Behavior deliberately not in the first implementation slice:

- Customer page endpoints (`/r/{pageToken}`) and customer page public guard
  mutations. Phase 7 names intake and operator list only.
- Email delivery and notification audit rows from the legacy create handler.
  They are not required for "customer can submit / operator can see request" and
  belong with the later notifications phase unless Christian pulls them forward.
- Rich operator list behavior: attention scoring, first-response SLA, search,
  closed-history pagination, actor-name resolution, SSE broadcast. The first list
  should prove the request is visible; later Keep sessions can port the richer
  operator view.
- Signal/attention fields and scheduled evaluation infrastructure.

---

## Decisions

### ADR-045 - Split Phase 7

Phase 7 is split under the Session Size Rule:

- **Phase 7A:** Keep domain + persistence foundation for intake/list:
  `KeepCustomer`, `KeepRequest`, `KeepRequestEvent`, public intake token link,
  page-token/reference-code services, EF mappings, migration, and integration
  proof tests.
- **Phase 7B:** Application/API vertical:
  public intake endpoint, create request service, operator request list endpoint,
  entitlement/permission/public guard/rate-limit checks, and HTTP-level tests.

Reason: the full legacy handler/list path spans Core, Application, Infrastructure,
API, auth, rate limiting, notifications, and rich list read-model logic. Splitting
keeps the first implementation session inside a small set of persistence/domain
contracts and lets the API layer depend on a proven schema.

### ADR-046 - Keep-owned hashed public intake token

Do **not** put `PublicIntakeToken` or `PublicSlug` back on `Account`.

Phase 4 intentionally trimmed Account identity (ADR-018/021) and moved public
intake concerns to Keep. Phase 7 should introduce a Keep-owned public intake link
satellite keyed by `AccountId`.

Recommended shape:

- `KeepPublicIntakeLink`
  - `AccountId`
  - `PublicSlug`
  - `TokenHash`
  - `RevokedAtUtc`
  - audit/soft-delete fields from `BaseEntity`
- Unique active indexes:
  - public slug unique among non-deleted/non-revoked links;
  - token hash unique among non-deleted/non-revoked links.

Raw public intake tokens should be high-entropy opaque values, hashed at rest
with SHA-256 lowercase hex. Resolution hashes the route token and looks up the
active link. Public slug is display/routing context only, not authority.

This adapts the legacy raw-token `Account.PublicIntakeToken` lookup to the build
plan's standardized public-token posture (§4.19: high entropy, hashing where
appropriate, rotation/revocation, public guard, no client-trusted AccountId).

---

## Phase 7A implementation target

Scope: Keep Core + Keep Infrastructure + integration tests. Avoid API endpoints
and auth/session wiring in 7A.

Add Core types under `src/OpHalo.Keep.Core`:

- `Entities/KeepCustomer.cs`
- `Entities/KeepRequest.cs`
- `Entities/KeepRequestEvent.cs`
- `Entities/KeepPublicIntakeLink.cs`
- small enums needed for v1 request lifecycle/event visibility:
  - `KeepRequestStatus` at minimum `Received`;
  - `KeepRequestEventType` at minimum `RequestCreated`;
  - `KeepRequestEventVisibility` at minimum `System`.
- `Errors/KeepRequestErrors.cs`

Naming:

- Use `Keep*` type names and `OpHalo.Keep.*` namespaces only.
- Table names should be `keep_*`, not `continuity_*`, unless a later cutover
  explicitly decides to preserve legacy table names.

Core behavior to port/adapt:

- `KeepCustomer` identity is `(AccountId, PrimaryPhone)`; constructor validates
  non-empty account id/name/phone; `UpdateContactInfo` updates name/email only.
- `KeepRequest` stores account/customer ids, denormalized customer name/phone/email,
  description, optional `CurrentStatusText`, status, reference code, page token,
  optional page expiry/closed timestamp, and last business/customer activity.
- Constructor validates non-empty required values and initializes `Received`
  status and `LastBusinessActivityAt`.
- `KeepRequestEvent` stores request id, account id, event type, optional content,
  optional actor account-user id, canonical event type/visibility/occurred-at if
  needed for the request-created event.
- Keep public intake link stores only token hash, never raw token.

Add Infrastructure:

- EF configurations for the four Keep entities.
- Add DbSets to `OpHaloDbContext`.
- Add services:
  - `IKeepPageTokenGenerator` / `KeepPageTokenGenerator` or a small shared Keep
    token service in Keep Application/Infrastructure;
  - `IPublicTokenHasher` or similar, using SHA-256 lowercase hex;
  - `IKeepReferenceCodeGenerator` if reference code generation is included in 7A.
- Generate a migration after mappings are in place.

Indexes to include:

- `keep_customers`: unique `(account_id, primary_phone)`.
- `keep_requests`: unique `page_token`; unique `(account_id, reference_code)`;
  index `account_id`.
- `keep_request_events`: index `request_id`; index `account_id` if stored.
- `keep_public_intake_links`: unique active `public_slug`; unique active `token_hash`.

Integration proof tests:

- Migration applies to PostgreSQL.
- Public intake link resolves by token hash and never requires client AccountId.
- Customer upsert identity is enforced by `(AccountId, PrimaryPhone)`.
- Keep request + request-created event round-trips.
- Duplicate page token is rejected.
- Duplicate account/reference code is rejected.
- Soft-delete filters hide Keep base rows consistently.

Unit tests:

- Domain constructor validation for customer/request/link.
- Token hasher produces deterministic 64-char lowercase SHA-256 hex and does not
  persist raw token.

Verification:

```bash
dotnet test tests/OpHalo.UnitTests/OpHalo.UnitTests.csproj
dotnet test tests/OpHalo.ArchitectureTests/OpHalo.ArchitectureTests.csproj
dotnet test tests/OpHalo.IntegrationTests/OpHalo.IntegrationTests.csproj
```

Docker is required for the integration tests.

---

## Phase 7B preview

After 7A proves the schema, build the application/API vertical:

- Public route: `POST /keep/public-intake/token/{publicIntakeToken}`.
  Consider preserving legacy `/continuity/public-intake/token/{publicIntakeToken}`
  as a compatibility alias only if a cutover decision requires it.
- Request body mirrors legacy:
  `CustomerName`, `CustomerPhone`, `Description`, `CustomerEmail?`,
  `EmailNotificationsEnabled`.
- Handler/service resolves account from `KeepPublicIntakeLink`, then checks:
  account lifecycle/commercial posture through existing Foundation access policy;
  `FeatureKeys.Keep.PublicIntake`;
  no client `AccountId`.
- Operator list route should return the minimal account-scoped open list needed
  for "operator can see request".
- Gate operator list through existing `UserAccessPolicy` with
  `PermissionKeys.Keep.RequestsView` and `FeatureAccessPolicy` with an appropriate
  Keep feature key.
- Add public intake rate limiting in `OpHalo.Api` composition root.

Do not introduce MediatR/FluentValidation just because legacy used them. The
target repo has so far used direct services and explicit tests; keep that style
unless a real caller makes the abstraction pay for itself.

---

## Open questions for 7B

- Which public route names are cutover contracts in v1: only `/keep/...`, or both
  `/keep/...` and legacy `/continuity/...` aliases?
- Is the `publicSlug` segment required in the first backend route, or can the
  token-only route ship first while web cutover decides `/q/{publicSlug}/{token}`?
- Should customer email delivery on intake move forward into Phase 7B, or stay
  deferred to Phase 9 notifications?
- Does operator "sign in" in the Phase 7 exit gate mean real Phase 5 auth must be
  implemented before 7B, or can 7B use the existing `ICurrentUser` abstraction with
  integration-level fakes until auth lands?

---

## Exit criteria for Phase 7A

- Keep domain entities and EF mappings compile.
- Migration generated and applies in the PostgreSQL Testcontainers harness.
- Unit/domain tests and integration persistence proofs are green.
- Architecture tests remain green: Foundation does not reference Keep; Keep may
  reference Foundation.
- Session log points to Phase 7B only after the Keep schema is proven.
