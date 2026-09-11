# BL154 — GAP-094: Auth-code and invite issuance rate limiting

**Status:** All of GAP-094 is code-complete. 094-1 is implemented, verified, and committed as
`197457b8` on `main`, still awaiting Christian's diff review. **094-2 landed 2026-09-11** on top of
that same commit, reusing its seam unchanged — also awaiting Christian's diff review.

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

**094-1 implemented and verified, committed as `197457b8` 2026-09-11, awaiting Christian's diff
review.** `IAuthIssuanceThrottle`
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

**Landed 2026-09-11**, reusing 094-1's `IAuthIssuanceThrottle` seam unchanged — no migration in this
slice. `SendInviteService.HandleAsync` and `MemberManagementService.ResendInviteAsync` each acquire
the shared `Recipient` allowance (3/15min, same global bucket as magic-link issuance — keyed on
`target.NormalizedEmail` on the resend path) plus the new `Account` allowance (20/60min, keyed on
`currentUser.AccountId`) atomically in one `TryAcquireAsync` call, right before token
generation/mutation — after authorization and the seat-limit check, so a seat-limit or
already-active/removed rejection never consumes the allowance. `ResendInviteAsync` acquires before
`RefreshInvite`/`RestoreInvite` + commit for both `Email` and `ManualShare` delivery, so
`ManualShare` can't bypass the cap; denial returns the same `Auth.IssuanceRateLimited` → body-free
429 already wired in `ErrorHttpMapper`. No endpoint signature, client contract, email template, token
lifetime, frontend, or GAP-095 change — as scoped.

Two production files (`SendInviteService.cs`, `MemberManagementService.cs`); two test files
(`InviteTests.cs`, `MemberManagementTests.cs`) — well inside the hard batch limit.

Verification: 4 new regression tests (`SendInvite_FourthIssuanceToOneRecipient_...`,
`SendInvite_TwentyFirstIssuanceFromOneAccount_...`, `ResendInvite_FourthIssuanceToOneRecipient_...
ManualShareCannotBypassCap`, `ResendInvite_TwentyFirstIssuanceFromOneAccount_...`) — each mutation-
tested by temporarily moving its service's `TryAcquireAsync` call to after the commit and confirming
the test then fails (proving the assertions catch a real ordering regression, not just a stale
`MembershipStatus`). Full `InviteTests` + `MemberManagementTests` 64/64 passed against a real
Postgres container; full `OpHalo.IntegrationTests` 1677/1680 (3 pre-existing failures unrelated to
this slice — an Npgsql connection timeout in a PriceBook migration test, a Sentry envelope-capture
timeout, and a known-flaky catalog-item concurrent-create race; none touch Auth/Invite/
MemberManagement code); `git diff --check` clean; Foundation.Application/Infrastructure/Api/
IntegrationTests/ArchitectureTests all build clean.

Repo-wide `rg -n "accounts/me/invite|resend-invite" tests/` confirmed no other test file issues
invites, so no BL154-094-1-style cross-test cap collision to fix.

Decision-queue follow-up (not done here — would touch the already-committed 094-1 files): the
`RecipientPermitLimit`/`RecipientWindow` constants are now private and duplicated across four
services (`StartAuthService`, `SignInAuthService`, `SendInviteService`, `MemberManagementService`).
Hoist them next to `AuthIssuanceThrottleScopes` in `IAuthIssuanceThrottle.cs` in a future cleanup
slice.

**All of GAP-094 is code-complete.** Hot blocker: none. Awaiting Christian's diff review.

## Claude handoff

GAP-094 is done. Next: GAP-095 discovery/preflight.
