# BL154 — GAP-094: Auth-code and invite issuance rate limiting

**Status:** 094-1 implemented and verified 2026-09-11, awaiting Christian's diff review.
094-2 begins only after that review/commit.

**Scope:** [workboard](../workboard.md) Next item 6; audit Vector 8 F8.4, F8.5, F8.11.
**Supervised-pilot gate.**

## Decisions

- Keep the existing fixed 10/minute IP `"auth"` limiter. Add a PostgreSQL-authoritative throttle;
  Vector 11 requires security-significant rate limits to be shared across API instances.
- `/auth/start` and `/auth/signin` share a normalized-recipient-email allowance: 3 attempts per
  15-minute fixed window. Acquire after endpoint validation but before classification, code
  issuance/invalidation, or email dispatch. Valid unknown/ineligible requests retain neutral 200
  below the cap; exhaustion returns body-free 429.
- Every invite/resend issuance acquires that recipient allowance and an authenticated-account
  allowance of 20 issuances per 60-minute fixed window. `manual_share` does not mail the recipient,
  but it rotates an invite bearer credential and therefore consumes the same allowance.
- Two-key invite acquisition is all-or-nothing in one transaction. A denied key rolls back any
  provisional increment; concurrent callers cannot exceed permits.
- Persist only SHA-256 hashes of namespaced normalized-email keys, never raw emails. Account keys
  are namespaced UUIDs. Store key hash, scope, window start, and count only; prune expired rows in
  bounded acquisition-time batches.
- `IAuthIssuanceThrottle` lives in Application and its PostgreSQL implementation in Infrastructure.
  It returns allow/deny; callers return `Auth.IssuanceRateLimited`, mapped to bare 429.
- Resend resolves/authorizes its target first, then acquires against stored normalized email before
  replacement-token creation. GAP-095 remains sole owner of timing equalization and metadata leaks.

Use PostgreSQL `INSERT … ON CONFLICT … DO UPDATE … WHERE … RETURNING` inside a short transaction,
not EF read-then-write. An expired window resets to 1; an active one increments only below its cap.

## 094-1 — public magic-link issuance

Implement first. Exact files: new `IAuthIssuanceThrottle.cs`; modify `StartAuthService.cs`,
`SignInAuthService.cs`, `Program.cs`, and `ErrorHttpMapper.cs`; new
`EfAuthIssuanceThrottle.cs`; founder-generated `AuthIssuanceThrottles` migration (and snapshot only
if needed); `RateLimitIntegrationTests.cs` for the production-like cross-IP proof and
`AuthEmailFailureLoggingTests.cs` for the shared-fixture email isolation regression; this build log;
workboard/session-log only after verification.

Regression: three mixed `/auth/start` + `/auth/signin` valid attempts for one recipient, from
distinct trusted IPs, succeed; fourth is 429 with no fourth email/code/invalidation. Test the neutral
unknown/ineligible contract remains 200 below cap.

Christian generates the migration; Claude must not run `dotnet ef`. This is six production files,
one migration family, two integration test files, and docs — still within the hard batch limit.

**094-1 implemented and verified 2026-09-11, awaiting Christian's diff review.** `IAuthIssuanceThrottle`
(Application) takes one-or-more `AuthIssuanceThrottleRequest`s and acquires them atomically —
all-or-nothing across the whole set in one PostgreSQL transaction, row-lock-ordered by (scope,
key hash) to avoid deadlocks — so 094-2's two-key (recipient + account) invite acquisition reuses
this same seam unchanged. `EfAuthIssuanceThrottle` (Infrastructure) is deliberately entity-free: a
single `INSERT … ON CONFLICT … DO UPDATE … WHERE … RETURNING count AS "Value"` per partition (EF's
scalar `Database.SqlQuery<int>`, no DbSet/model registration), with a best-effort, failure-isolated,
`FOR UPDATE SKIP LOCKED` bounded prune after commit. `StartAuthService`/`SignInAuthService` acquire
the shared `Recipient` allowance (3/15min) right after email normalization, before classification;
denial returns `Auth.IssuanceRateLimited`, mapped in `ErrorHttpMapper` to a body-free 429 outside the
normal ProblemDetails pipeline. Migration `20260911120000_AddAuthIssuanceThrottles` hand-written (no
`.Designer.cs`, no `OpHaloDbContextModelSnapshot.cs` change — the table has no EF model to diff
against): `auth_issuance_throttles(scope, key_hash, window_start_utc timestamptz, count)`,
`PRIMARY KEY (scope, key_hash)`, index on `window_start_utc`.

Test files ended up different from the original file gate: the cross-IP proof lives in
`RateLimitIntegrationTests.cs` (new `AuthIssuanceThrottleProofTests`, using `RateLimitWebFactory` so
the pre-existing per-IP `"auth"` policy is live alongside the new throttle) rather than
`AuthMagicLinkTests.cs`, and `AuthEmailFailureLoggingTests.cs` needed a fix: its `IClassFixture`
shares one never-reset database across 5 `[Fact]`s that all issued to one constant email, which
would trip the new 3/15min cap on the 4th/5th test — each test now uses its own recipient email.

Verification: `AuthMagicLinkTests` + `AuthStartTests` + `AuthEmailFailureLoggingTests` +
`AuthIssuanceThrottleProofTests` 50/50 passed against a real Postgres container (the proof test
confirms the 4th cross-IP attempt is a body-free 429 that creates no new `AccountAuthCode` row and
does not invalidate the 3rd request's still-exchangeable code); full `RateLimit*` file 6/6; `git diff
--check` clean; Foundation.Infrastructure/Api/IntegrationTests/UnitTests/ArchitectureTests all build
clean.

## 094-2 — authenticated invite issuance

Begin only after 094-1 is reviewed/committed. Modify `SendInviteService.cs` and
`MemberManagementService.cs`; add coverage in `InviteTests.cs` and `MemberManagementTests.cs`; then
update this build log/workboard/session log. Reuse 094-1's seam unchanged.

Regression: a fourth invite/resend issuance to one recipient is 429 with no email or token mutation;
the twenty-first issuance from one account in an hour is 429; manual-share resend consumes the
allowance and remains unable to bypass the token-rotation guard.

No endpoint signature, client contract, email template, token lifetime, frontend, or GAP-095 change
belongs in either slice.

## Claude handoff

Start 094-1 only. Report the file gate, Application/Infrastructure/API/migration/test layers, and
the 3/15 recipient plus 20/60 account limits before editing. Do not substitute in-memory state,
alter email timing, or combine 094-2/GAP-095. Stop for Christian to generate/review the migration.
