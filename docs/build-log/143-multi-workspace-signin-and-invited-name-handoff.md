# BL143 — Multi-workspace sign-in and invited-user display name: Session 0 handoff

**Status:** Discovery complete. No production code, migration, or frontend written. Decision is [ADR-497](../decisions/ADR-497-post-auth-continuation-multi-workspace-signin-and-display-name.md). Tracked as pilot-blocking [GAP-068](../pilot-readiness-bug-tracker.md).

## Confirmed current-code evidence

- `src/OpHalo.Foundation.Infrastructure/Auth/EfAuthCodePersistence.cs`: `FindEligibleSignInMemberByEmailAsync` (`Take(2)` → `null` on 2+) and `ClassifyStartRequestAsync` (`Take(2)` → `StartAsNeutral` on 2+).
- `src/OpHalo.Foundation.Application/Auth/SignInAuthService.cs`: `member is null` → `Result.Success()`, no code, no email (line 40-41).
- `src/OpHalo.Foundation.Infrastructure/Auth/EfInvitePersistence.cs:123`: `User.CreateVerified(invite.Email, name: null, nowUtc)`.
- `src/OpHalo.Api/Auth/AuthEndpoints.cs:152` (`Me`): `userName` sourced from `AuthenticatedWorkspaceIdentity.UserName`, which is blank for an un-named invited user.
- `src/OpHalo.Foundation.Core/Entities/Users/User.cs`: `Name` is non-nullable `string`, empty string is the documented "no name yet" sentinel; no `SetName`/mutator exists yet.
- `src/OpHalo.Foundation.Core/Entities/Accounts/AccountAuthCode.cs`: `Create` (ExistingMember-shaped, requires `EntryContext != NewAccount`) and `CreateForNewAccount` (deferred-target shape) are the only two factories; `EntryContext` has `NewAccount=1`, `ExistingMember=2`, `InvitedUser=3` (unwired, ADR-074).
- `src/OpHalo.Foundation.Application/Auth/ExchangeAuthService.cs`: switches on `code.EntryContext`, currently only `ExistingMember`/`NewAccount`; `CreateSessionAsync`/`CreateMobileHandoffAsync` are the two session-issuance primitives everything must reuse.
- `src/OpHalo.Api/Auth/AuthCookieOptionsFactory.cs`: `ForCreate(expires)`/`ForDelete()` already produce the exact `HttpOnly`/`Secure`(non-Dev)/`SameSite=Lax`/`Path=/` shape the continuation cookie needs — reused as-is with a new cookie name and a 10-minute expiry, no new cookie-options code.
- `src/OpHalo.Foundation.Core/Constants/AuthConstants.cs`: has `CookieName = "ophalo.sid"` as the existing precedent for a new `ContinuationCookieName` constant.
- `web/ophalo-web/src/app/auth/exchange/ExchangeClient.tsx`: only handles `res.ok` (redirect into the app) or a fixed set of error statuses; has no branch for a non-session `200` continuation response.
- Frontend commit `226778af` (Sign out + business-name header fallback) is unrelated and untouched by this work — do not fold it in or duplicate the logout implementation.

## Files and layers touched (proposed, across all slices)

**Core:** `AccountAuthCode.cs` (new factory + enum value), `Enums/EntryContext.cs`, new `PostAuthContinuation.cs` (carries `ClientType`/`DeviceName` snapshot), `User.cs` (new `SetName`), `AuthConstants.cs` (new `ContinuationCookieName`).
**Application:** `IAuthCodePersistence.cs` (classification return shape), `SignInAuthService.cs`, `StartAuthService.cs`, `ExchangeAuthService.cs`, new `IPostAuthContinuationPersistence.cs`, new `CompleteAuthContinuationService.cs`, `AcceptInviteService.cs`.
**Infrastructure:** `EfAuthCodePersistence.cs`, `EfInvitePersistence.cs`, new `EfPostAuthContinuationPersistence.cs`, new EF configuration + one migration (new table only — no column changes to existing tables).
**Api:** `AuthEndpoints.cs` (new `POST /auth/continue` route reading the continuation cookie and writing/clearing it via the existing `AuthCookieOptionsFactory`; response shaping for `/auth/exchange` and `/accounts/invite/accept` limited to non-secret UI state).
**Frontend (`ophalo-web`):** `ExchangeClient.tsx` branch, invite-accept client branch, new "complete sign-in" screen (name entry / workspace selector).
**Tests:** unit (`AccountAuthCodeTests`, new `PostAuthContinuationTests`, `UserTests`), integration (`AuthMagicLinkTests`, `AuthStartTests`, new `AuthContinueTests`, invite accept tests).

No change to `SharedKernel`, no change to Keep, no change to `AccountUser`/`AccountSession` schemas.

## Unresolved decisions

None remaining — ADR-497 locks the continuation shape, endpoint contract, and enumeration-safety boundary. Two mechanical items for the implementation session's preflight, not decisions:

- Exact JSON casing/field names for the `/auth/continue` request (`{ name?, accountUserId? }`) and the `/auth/exchange` continuation response (`{ requiresContinuation, requiresName, workspaces }`) should match the codebase's existing camelCase convention (see `ExchangeBody`/`SignInBody` in `AuthEndpoints.cs`). Neither body ever carries the continuation token — that travels only via the `ophalo.continuation` cookie.
- Whether `CompleteAuthContinuationService` needs its own persistence seam or can extend `IAuthCodePersistence` — proposed as its own seam (`IPostAuthContinuationPersistence`) per ADR-077's precedent (separate seams for unrelated storage), confirm at Slice 2 preflight only if it changes file count.

## Proposed implementation slices

Each slice is independently compiling and separately gated. Respect the hard batch limit (3 handler families / 8 production files / 12 total files) per session — Slice 2 is the largest and is written to stay inside that limit; if the actual preflight file count runs over, split Slice 2 into 2a (classification + code issuance) and 2b (exchange branching + `/auth/continue` + service).

### Slice 1 — `PostAuthContinuation` foundation (additive only, no behavior change)
- `PostAuthContinuation` entity (Core) — `Id`, `TokenHash`, `UserId`, `TargetAccountUserId?`, `ClientType`, `DeviceName?`, `IssuedAtUtc`, `ExpiresAtUtc`, `ConsumedAtUtc?` — EF configuration, migration (new table only).
- `IPostAuthContinuationPersistence` / `EfPostAuthContinuationPersistence` (create with bounded
  opportunistic cleanup, find-by-hash, atomic consume, and terminal deletion — mirrors
  `AccountAuthCode`'s persistence shape). No hosted/background cleanup job: each creation deletes
  up to 100 rows consumed or expired more than 24 hours ago; successful consumption and presented
  expired continuations are deleted immediately. Runtime expiry remains fail-closed regardless of
  whether a stale row has been physically deleted.
- `User.SetName(string)` domain method + unit tests (rejects blank/whitespace, rejects overwrite of a non-blank name).
- `AuthConstants.ContinuationCookieName` constant. No route/endpoint wiring yet — this slice does not read or write the cookie; it only lands the storage and domain primitives Slice 2 will use.
- ~6 production files, no route/endpoint changes, no existing-service changes. Tests cover
  fail-closed expiry, immediate terminal deletion, and bounded creation-time cleanup. Full existing
  auth suite must still pass unchanged (nothing wired yet).

### Slice 2 — Sign-in continuation: multi-membership selector + name gate
- `EntryContext.MultipleMembers`, `AccountAuthCode.CreateForMultipleMembers`.
- `EfAuthCodePersistence`: classification distinguishes 0 / 1 / 2+ active explicitly (replace the `Take(2)`-collapses-to-null shape with a 3-state result the two call sites branch on); `SignInAuthService`/`StartAuthService` issue a `MultipleMembers` code on 2+ instead of no-op — public response string is unchanged either way.
- `ExchangeAuthService`: branch for `MultipleMembers` and the name-blank sub-case of `ExistingMember`, both producing a continuation instead of a session — sets the `ophalo.continuation` cookie via `AuthCookieOptionsFactory.ForCreate` (10-minute expiry) and returns only `{ requiresContinuation, requiresName, workspaces }` in JSON, never the raw token.
- New `CompleteAuthContinuationService` + `POST /auth/continue` in `AuthEndpoints.cs`: reads the raw token from the `ophalo.continuation` cookie (never from the body), request body is `{ name?, accountUserId? }` only, uses the continuation's stored `ClientType`/`DeviceName` (ignores any client-type value if one is sent), clears the cookie via `AuthCookieOptionsFactory.ForDelete()` on success and on every terminal failure (expired/consumed/replayed/cross-user/non-Active), leaves the cookie intact on a retryable validation failure (bad name, unmatched `accountUserId`) against an otherwise-still-valid continuation.
- Handler families: (a) code-issuance branch, (b) exchange branching + cookie set, (c) `/auth/continue` + cookie read/clear. Three — at the hard limit; do not add a fourth in this slice.
- Tests: enumeration-safety (no count/identity disclosure pre-redemption), single-membership unchanged, two-membership selector + correct-account-only session, cross-user/suspended/removed/expired/replayed `/auth/continue` failures with cookie cleared, name-once-then-locked, continuation cookie never appears in any JSON body, `/auth/continue` ignores a supplied `clientType`/`deviceName`.

### Slice 3 — Invite acceptance name gate
- `EfInvitePersistence.CommitAcceptInviteAsync` / `AcceptInviteService`: route through the same continuation when the resolved `User.Name` is blank (sets `ophalo.continuation`, returns `{ requiresContinuation: true, requiresName: true, workspaces: null }`); unchanged when already named.
- Reuses Slice 1/2's continuation, cookie, and `/auth/continue` — no new endpoint, no new cookie.
- ~3 files. Tests: invited-user-with-no-name lands via continuation into the correct (and only the correct) workspace; existing named-user invite-accept path unchanged.

### Slice 4 — Frontend (`ophalo-web`), separate session
- `ExchangeClient.tsx` and the invite-accept client: branch on `requiresContinuation` instead of assuming every `200` is a completed session. No token to carry client-side — the browser already holds the `ophalo.continuation` cookie from the same-origin `/auth/exchange` response; the new screens only need `credentials: "include"` on the `/auth/continue` fetch, same as today's exchange call.
- New screen(s) for name entry and workspace selection, posting `{ name?, accountUserId? }` to `/auth/continue`.
- Confirm whether any PWA (`ophalo-app`)-side auth screen duplicates this logic or only ever redirects through `ophalo-web`; if it duplicates, it needs the same branch — flag this explicitly at Slice 4 preflight rather than assuming.
- No backend changes in this slice.

## Stop point

Session 0 stops here for approval. Recommended order: Slice 1 → Slice 2 (or 2a/2b if it overflows the limit) → Slice 3 → Slice 4, one Claude session each.

## Slice 1 — done, accepted

Delivered as scoped above, additive only, no route/endpoint wiring. `PostAuthContinuation` (Core),
`User.SetName` + `UserErrors.NameAlreadySet` (Core), `AuthConstants.ContinuationCookieName` (Core),
`IPostAuthContinuationPersistence` (Application), `EfPostAuthContinuationPersistence` +
`PostAuthContinuationConfiguration` + `OpHaloDbContext.PostAuthContinuations` (Infrastructure), and
migration `20260905204136_AddPostAuthContinuation` (new table only).

Schema follows the `AccountAuthCode`/`AccountSession` precedent rather than introducing new
patterns: unique index on `TokenHash`, index on `ExpiresAtUtc` for cleanup, index on `UserId`,
cascade FK to `User` (ephemeral user-scoped artifact, matching `AccountSession`'s cascade rather
than `AccountUser`'s restrictive FK to `User`). No DB check constraint for the
`ExpiresAtUtc > IssuedAtUtc` invariant — Foundation auth entities enforce this in the Core factory
only (`AccountAuthCode` does the same); check constraints in this codebase are a Keep
pricing/financial convention, not a Foundation auth one. Target-membership ownership verification
(`TargetAccountUserId` belongs to `UserId`) is deferred to Slice 2, where continuations are
actually created.

`EfPostAuthContinuationPersistence.CreateAsync`'s opportunistic cleanup sweeps a row only when
`ConsumedAtUtc` itself is more than 24h old, or `ExpiresAtUtc` is more than 24h old — not merely
because `ConsumedAtUtc` is set. A row left behind by an interrupted Slice-2 completion must survive
until it is actually stale; Slice 2's normal path still deletes a spent continuation immediately via
`DeleteAsync`.

DI registration (`Program.cs`) is deferred to Slice 2 — nothing consumes
`IPostAuthContinuationPersistence` yet.

Tests: 18 new unit (`PostAuthContinuationTests` factory guards, `UserTests` `SetName` blank/overwrite
guards), 3 new integration against real Postgres (`PostAuthContinuationPersistenceTests` — atomic
`ConsumeAsync` race, terminal `DeleteAsync`, and the corrected bounded-cleanup predicate covering
recently-consumed-survives / stale-consumed-deleted / stale-expired-deleted / live-survives). 14/14
architecture tests pass. 8 production files changed, within the batch gate.
