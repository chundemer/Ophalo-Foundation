# BL153 — GAP-093: Session-layer account-lifecycle gate

**Status:** Reviewed, committed as `b7c815e4`, and pushed to `main` 2026-09-11.

**Scope reference:** [workboard](../workboard.md) Next item 5; [production-readiness audit](../audits/production-readiness-audit.md) Vector 8, F8.3.
**Supervised-pilot gate** — before issuing pilot access.

## Problem

`SessionAuthenticationHandler` already fails closed on the backing membership's status, but it does
not observe `Account.LifecycleState`. `AccountAccessPolicy` rejects suspended and closed accounts,
but its use is opt-in at individual application services. Therefore a live session can reach any
authenticated endpoint that lacks that application-level policy call after the whole account is
suspended or closed.

## Discovery result and locked implementation

No new product or ADR decision is required. Account lifecycle is already an account-wide access
invariant; this slice supplies its missing session-auth backstop.

1. Extend the narrow `SessionData` projection with nullable `AccountLifecycleState`.
2. In `SessionStore.FindByTokenHash`, obtain the state from the backing `AccountUser.Account`
   relation in the existing no-tracking membership query. Preserve the existing null result for a
   missing membership or session/account mismatch. A missing account/state must remain
   unauthenticated, not throw or grant access.
3. In `SessionAuthenticationHandler`, after the existing membership gate and before renewal or
   claim construction, require `session.AccountLifecycleState == AccountLifecycleState.Active`.
   `Suspended`, `Closed`, null, and any future/unknown enum value all return `NoResult`.
4. Keep the existing `NoResult`/challenge behavior: protected endpoints return the ordinary clean
   `401`; do not disclose lifecycle state, add a special error, clear a browser cookie, or alter the
   stored session row. This is a per-request fail-closed check; no best-effort revocation side effect
   is needed for correctness.

This is intentionally **not** a call from Infrastructure to `IAccountAccessPolicy`: authentication
must enforce only the account-lifecycle invariant, not commercial/trial/off-season posture or each
service's action-specific access rules. Existing application-policy checks remain unchanged and
defense in depth.

## Preflight — exact implementation gate

| File | Change |
| --- | --- |
| `src/OpHalo.Foundation.Infrastructure/Security/SessionData.cs` | Add the nullable lifecycle field and update its auth-projection contract comment. |
| `src/OpHalo.Foundation.Infrastructure/Security/ISessionStore.cs` | Update the lookup contract/docs for the lifecycle projection. |
| `src/OpHalo.Foundation.Infrastructure/Security/SessionStore.cs` | Project `au.Account.LifecycleState` along with `AccountId` and membership status; pass it into `SessionData`. |
| `src/OpHalo.Foundation.Infrastructure/Security/SessionAuthenticationHandler.cs` | Add the Active-account gate before activity renewal / claims; update handler documentation. |
| `tests/OpHalo.IntegrationTests/Api/AuthApiTests.cs` | Add fresh-session regressions showing `GET /auth/me` returns 401 after `Account.Suspend()` and after `Account.Close()`. |
| `docs/build-log/153-gap-093-session-account-lifecycle-gate.md` | Record implementation outcome and verification results. |
| `docs/workboard.md` | Link the completed slice / update its status only when implementation is verified. |
| `docs/session-log.md` | Advance the next-session pointer only when implementation is verified. |

No migration, endpoint, DI registration, public contract, frontend, or Application/Core policy change
is required. `SessionData` has no external constructor callers beyond `SessionStore`; the lookup
interface signature and handler constructor are unchanged.

## Required regression coverage

- A valid pre-existing cookie session gets 401 from `/auth/me` immediately after its account is
  suspended, while the membership remains Active.
- The same is true after the account is closed.
- Existing valid, expired, revoked, inactive, suspended-member, and removed-member session tests
  remain green, proving guard ordering and the ordinary 401 challenge contract are preserved.

The two account cases exercise an endpoint that does not depend on a feature-service access-policy
call, so they prove the session-auth backstop rather than merely another opt-in service check.

## Claude handoff

Before editing, report the eight-file gate above, Infrastructure + integration-test layers, and that
there are no unresolved decisions. Keep this as one bounded vertical slice: four production files,
one test file, and documentation. Do not fold in GAP-094/095, commercial access checks, token
revocation/rotation, cookie deletion, or account-lifecycle administration UI/API work.

Verify with:

```sh
dotnet test tests/OpHalo.IntegrationTests/OpHalo.IntegrationTests.csproj --filter FullyQualifiedName~AuthApiTests --no-restore
git diff --check
```

Run the full unit + architecture suites only if the focused integration gate reveals a boundary or
compile-impact concern.

## Completion record (2026-09-11)

Implemented exactly the eight-file gate above; no drift from the preflight. `SessionData` gained a
nullable `AccountLifecycleState` (its only constructor caller is `SessionStore`, confirmed via
`rg "new SessionData("`); `SessionStore.FindByTokenHash` projects `au.Account.LifecycleState`
alongside the existing `MembershipStatus`; `SessionAuthenticationHandler` gates on
`AccountLifecycleState.Active` immediately after the existing membership gate and before the
renewal-throttle block, returning `NoResult` for `Suspended`/`Closed`/null exactly as specified.

This is treated as an auth-middleware change (CLAUDE.md full-suite trigger), so verification ran
beyond the focused gate:

- `AuthApiTests` (integration, focused): **37/37** — the 35 pre-existing session-auth cases plus the
  two new `Me_SuspendedAccount_Returns401` / `Me_ClosedAccount_Returns401` regressions, each seeding
  a fresh account with the *member* left Active and only the *account* suspended/closed, proving the
  session-layer backstop rather than the pre-existing opt-in membership gate.
- Unit: **1909/1909**.
- Architecture: **14/14**.
- `git diff --check`: clean.

No migration, endpoint, DI registration, public API contract, frontend, or Application/Core policy
change was needed, matching the preflight. GAP-094 and GAP-095 were not touched.
