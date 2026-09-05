# ADR-497 — Post-auth continuation: multi-workspace sign-in and invited-user display name

**Status:** Locked (Session 0 discovery — implementation not yet started)
**Supersedes in part:** ADR-067's "two or more active memberships → Neutral" outcome for **code issuance**. ADR-067's enumeration-safety response contract at `/auth/start` and `/auth/signin` is unchanged — callers still get the same generic response regardless of membership count. What changes is that 2+ active memberships now still get a magic-link code issued (of a new kind) instead of no code at all.

## Problem

1. `EfAuthCodePersistence.FindEligibleSignInMemberByEmailAsync` / `ClassifyStartRequestAsync` return no eligible member (`null` / `StartAsNeutral`) whenever an email has 2+ active `AccountUser` memberships. `SignInAuthService`/`StartAuthService` treat that as ordinary neutral success — no code is issued, no email is sent. A person with two active memberships (e.g. the founder, who owns more than one workspace) can never receive a magic link and is permanently locked out of sign-in. This is a real dead end, not a deferred UX nicety.
2. `EfInvitePersistence.CommitAcceptInviteAsync` creates a global `User` via `User.CreateVerified(email, name: null, ...)`, leaving `User.Name == string.Empty` (the documented "no name yet" sentinel). `/auth/me` surfaces that empty name as `userName: null`. Invited users can operate in the product and appear in customer-facing/attribution contexts with no identity.

## Locked product direction

Restated from the brief (binding on implementation):

1. `/auth/start` and `/auth/signin` responses stay enumeration-safe: identical generic response regardless of email existence, membership count, or eligibility. No business-identity or membership-count disclosure before the magic link is redeemed.
2. After redemption: missing display name is collected first; exactly one active membership signs straight in; two or more active memberships show a selector.
3. The selector shows factual business identity + role only after email proof, only for multiple active memberships.
4. Selection is server-authoritative: only a membership genuinely linked to the proven identity, and only `Active`, may be chosen. Forged IDs, expired/replayed challenges, and non-Active memberships fail safely.
5. Display name is `User`-level identity, collected once, never overwritten once present.
6. Invite acceptance stays account-specific; it flows through the same name-completion step, never through open account selection.
7. No client-trusted selector, no raw token exposure, no cookie/session weakening, no direct-ID authorization assumptions.
8. In-session "switch workspace" is out of scope. Sign out → sign in → choose workspace is sufficient for V1.
9. No production data change or migration until this ADR is implemented and approved; the new table below ships empty and additive.

## Decision: one continuation mechanism, three producers

A single new server-owned, single-use, short-lived record — `PostAuthContinuation` — represents "email/invite ownership has just been proven; a workspace session cannot yet be created because either the display name or the target membership is still undetermined." It replaces any temptation toward a client-side token workaround (a JWT carrying claims, a signed cookie, etc.) — the raw continuation token is opaque, hashed at rest exactly like `AccountAuthCode` and `AccountSession`, and every field needed to authorize the eventual session (name completeness, membership Active-ness) is re-read live from persistence when the continuation is redeemed, never trusted from the token or from the client.

### Why persistence is required (not client-side)

- The selector must reject a membership that was suspended/removed *during* the short continuation window — that requires a live re-check tied server-side to a specific proven identity, not a signed claim minted once and trusted for its lifetime.
- Rule 4 ("reject forged account IDs, expired/replayed challenges") is a fail-closed requirement; single-use is enforced by an atomic consume (`ExecuteUpdateAsync`, same pattern as `AccountAuthCode`/invite activation), which only a server-owned row can provide.

### Entity: `PostAuthContinuation` (Foundation.Core, standalone — not `BaseEntity`, mirrors `AccountAuthCode`/`AccountSession`)

| Field | Notes |
|---|---|
| `Id` | `Guid.CreateVersion7()` |
| `TokenHash` | SHA-256 hex of the opaque raw continuation token; raw token never persisted |
| `UserId` | The proven global `User` — resolved once, before the continuation is created |
| `TargetAccountUserId` | Nullable. Set when the caller already knows the one membership to land on (invite acceptance; sign-in with exactly one active membership but a missing name). Null when membership is still open (2+ active memberships) — the selection call must supply an `accountUserId` and the server verifies it against a **live** query, not a snapshot |
| `ClientType` | `SessionClientType`, snapshotted from the original request that created the continuation (`browser` for every current producer — mobile sign-in is existing-member-only per ADR-389 and mobile `/auth/exchange` never reaches a name-blank or ambiguous-membership case today, but the field is typed for `SessionClientType` generally, not hardcoded, so it stays correct if that changes) |
| `DeviceName` | Nullable, snapshotted alongside `ClientType`. `/auth/continue` uses these two stored values to create the session — a value resupplied in the `/auth/continue` request is never accepted or trusted |
| `IssuedAtUtc`, `ExpiresAtUtc` (10 minutes), `ConsumedAtUtc` | Same lifecycle shape as `AccountAuthCode` |

No membership list, business name, or role is snapshotted onto the row — the selector response is computed fresh at issuance and re-verified fresh at redemption.

`ClientType` and `DeviceName` are also stored on the row, snapshotted from the original `/auth/exchange` request. `/auth/continue` finishes the session using these stored values, never a client-resupplied `clientType`/`deviceName` — completion must not let a caller swap a browser-issued continuation into a mobile bearer-token session (or vice versa) by simply asserting a different client type in the completion call.

### Expiration and opportunistic cleanup

Security expiry is independent of physical deletion: every continuation read rejects an expired
(`ExpiresAtUtc <= nowUtc`) or already-consumed row immediately and clears the continuation cookie.
Cleanup is not an authorization control.

There is no hosted hourly cleanup job in this pilot. Instead, each continuation creation performs
an idempotent, bounded cleanup of up to 100 rows that were consumed or expired more than 24 hours
ago. Successful consumption deletes that continuation immediately; an expired continuation that is
presented for redemption is also deleted immediately after its rejection. If no new continuation is
created for a while, a small number of expired rows may remain until the next creation; they are
already unusable. The table indexes `TokenHash` for redemption and `ExpiresAtUtc` for this bounded
cleanup query. A scheduled purge is deferred until volume or a retention requirement needs a
guaranteed deletion deadline.

### The continuation secret never appears in JSON

The raw continuation token is carried the same way the session token is: a dedicated **short-lived, HttpOnly, Secure, SameSite cookie** — never in a JSON response body and never in a request body. This keeps it out of JavaScript reach (no `document.cookie` access, no accidental logging of a response/request payload) exactly like the existing session cookie, and avoids introducing a second bearer-like credential that a client could store, forward, or leak differently from the session token.

- `AuthConstants.ContinuationCookieName = "ophalo.continuation"` (new constant, mirrors `CookieName = "ophalo.sid"`).
- Written with the existing `AuthCookieOptionsFactory.ForCreate(expires)` (`HttpOnly`, `Secure` outside Development, `SameSite=Lax`, `Path=/`) — same factory as the session cookie, just a different name and a 10-minute expiry instead of the session's long lifetime. No new cookie-options code is needed.
- `/auth/exchange` sets this cookie only when it responds `requiresContinuation: true`; the JSON body carries only non-secret UI state: `{ requiresContinuation, requiresName, workspaces }`. `workspaces` (business name + role, post-proof) is safe to return in the body — rule 3 only forbids disclosing it *before* email proof, and by this point the magic code has already been consumed.
- `POST /auth/continue` reads the raw token from that cookie server-side (`HttpContext.Request.Cookies`), not from the request body. The request body carries only `{ name?, accountUserId? }` — the two pieces of information the server cannot otherwise derive.
- The continuation cookie is cleared (`AuthCookieOptionsFactory.ForDelete()`) on every path that ends the continuation's life: successful consumption (immediately after the session cookie is set), and every terminal failure the endpoint can return (expired, already consumed/replayed, cross-user or non-Active selection, malformed/missing cookie). A client that retries after any such failure starts a fresh `/auth/exchange` rather than resending a dead token.

### New `EntryContext.MultipleMembers = 4`

Added to the existing enum (next free explicit value; `InvitedUser = 3` stays unwired to `/exchange` per ADR-074). `AccountAuthCode.CreateForMultipleMembers(...)` mirrors `CreateForNewAccount`'s shape: `AccountId = null`, `TargetAccountUserId = null`, only `DeliveryEmailSnapshot` set. Issued by `SignInAuthService`/`StartAuthService` when classification finds 2+ active members instead of today's silent no-op. The public response is unchanged either way (Result.Success, generic copy) — only the persistence/email side differs.

### `/auth/exchange` branch behavior (all three EntryContext values it now handles)

1. **`ExistingMember`, name present, single membership implied by the code's `TargetAccountUserId`** — unchanged: consume code, create session, set cookie/handoff, same as today.
2. **`ExistingMember`, name blank** — consume the magic code (proves email) exactly as today, but instead of creating a session, create a `PostAuthContinuation` with `TargetAccountUserId = code.TargetAccountUserId` and `ClientType`/`DeviceName` snapshotted from this exchange request, set the `ophalo.continuation` cookie, and respond `200 { requiresContinuation: true, requiresName: true, workspaces: null }`.
3. **`MultipleMembers`** — consume the magic code, resolve `UserId` from `DeliveryEmailSnapshot`, live-query all `Active` memberships for that user, create a `PostAuthContinuation` with `TargetAccountUserId = null` and the same `ClientType`/`DeviceName` snapshot, set the `ophalo.continuation` cookie, and respond `200 { requiresContinuation: true, requiresName: <User.Name blank?>, workspaces: [{ accountUserId, businessName, role }, ...] }`.
4. **`NewAccount`** — unchanged (a brand-new User always already has whatever name was captured at `/auth/start`; `NameSnapshot` flows through provisioning as today, so this path never needs a continuation).

### New endpoint: `POST /auth/continue`

Request body: `{ name?: string, accountUserId?: guid }` only. The continuation secret itself travels in the `ophalo.continuation` cookie set by `/auth/exchange`, read server-side — never accepted from the body, a header, or a query string.

Server logic (in a new `CompleteAuthContinuationService`):
1. Read the raw continuation token from the cookie. Missing cookie is treated the same as an invalid one (generic failure) — clear the cookie (it may be stale) and return.
2. Look up the continuation by token hash; unexpired and unconsumed, else the same generic failure, and clear the cookie (no distinction between expired/replayed/unknown in the response — same enumeration-safety posture as the rest of auth).
3. Re-load the `User`. If `Name` is still blank, `name` is required in this call — trim, reject blank/whitespace, call new `User.SetName(name)` (a no-op-guarded domain method that throws/returns failure if `Name` is already non-blank — "preserve an existing name" is enforced in the domain, not just the caller). A validation failure here does **not** consume or clear the continuation — the client may resubmit with a corrected name before the cookie expires.
4. Resolve the target membership:
   - If `continuation.TargetAccountUserId` is set: re-verify live that this `AccountUser` is still `Active` (cheap re-check; membership could have changed in the last 10 minutes). Failure clears the cookie (terminal — this membership is not coming back within the window).
   - Else: `accountUserId` is required; server loads that `AccountUser` and verifies, live, `UserId == continuation.UserId && MembershipStatus == Active`. Any mismatch (wrong user, not Active, unknown ID) is the same generic failure as an expired continuation — never a distinguishable error — and clears the cookie only when the continuation itself is being invalidated (see below), not on a simple "pick again" validation error if the continuation is still otherwise valid.
5. Atomically consume the continuation (`ExecuteUpdateAsync` guard, same race pattern as `AccountAuthCode.ConsumeCodeAsync`).
6. Create the session using the continuation's **stored** `ClientType`/`DeviceName` — never a value resupplied in this request — exactly as `ExchangeAuthService.CreateSessionAsync`/`CreateMobileHandoffAsync` do today.
7. On success: clear the continuation cookie, then set the session cookie (browser) or return the handoff code body (mobile), matching today's `/auth/exchange` behavior for the equivalent `clientType`.

Terminal vs. retryable failures matter for cookie clearing: a bad `name` or a `accountUserId` that doesn't match any workspace is retryable (continuation stays alive, cookie stays set, client can resubmit) as long as the continuation row itself is still valid; an expired/consumed/forged continuation, or a selection that fails the live Active/ownership check, is terminal and always clears the cookie.

### Invite acceptance (`AcceptInviteService` / `EfInvitePersistence.CommitAcceptInviteAsync`)

After activation, if the resolved/created `User.Name` is blank: do not create a session. Create a `PostAuthContinuation` with `TargetAccountUserId` = the just-activated `AccountUserId` and `ClientType = Browser` (invite acceptance is browser-only per ADR-076), set the `ophalo.continuation` cookie, and return the same `{ requiresContinuation: true, requiresName: true, workspaces: null }` body shape. If the user already has a name (returning invitee, or an existing `User` row invited into a second business), behavior is unchanged — session created directly. This satisfies rule 6: invite acceptance always lands in the invited workspace specifically; it never opens the general selector.

### Founder recovery path (rule: no second email, no destructive membership change)

Once `SignInAuthService` issues `MultipleMembers` codes instead of silently dropping them, the founder's existing two active memberships are handled by the ordinary flow above — sign in, redeem, see the two-workspace selector, pick one. No special-cased data migration or membership edit is needed; this is the same path every future 2+-membership user takes.

## Rejected alternatives

- A signed, client-held continuation (JWT/opaque signed payload naming the eligible `AccountUserId`s) was rejected: it either has to embed a membership snapshot (stale by the time it's redeemed, violating rule 4's suspended/removed re-check) or embed nothing and become a bare "trust me, this AccountUserId is mine" claim, which is a client-trusted selector — explicitly excluded by rule 7.
- Returning the raw continuation token in the `/auth/exchange` JSON body (for the client to hold in memory or resend in the `/auth/continue` body) was rejected: it is a second bearer-like credential handed to JavaScript, with different exposure characteristics than the HttpOnly session cookie (readable by any script on the page, capturable by a logging/analytics library, more easily mishandled by a future client). A dedicated short-lived HttpOnly/Secure/SameSite cookie keeps the same non-JS-readable guarantee the session token already has, at no extra implementation cost — `AuthCookieOptionsFactory` already produces exactly this shape.
- Letting `/auth/continue` accept a `clientType`/`deviceName` and use it to create the session was rejected: it would let a caller redeem a continuation issued for one client type (e.g. a browser `/auth/exchange` call) as a different one (e.g. a mobile bearer-token handoff) purely by asserting it in the completion call. The continuation stores the original intent; completion only ever honors that stored value.

## Acceptance coverage required at implementation

- One active membership, name present: direct session, unchanged.
- Two active memberships: selector shown after redemption; each permitted choice sessions only that account; the other membership's data never appears in any earlier response.
- No disclosure of membership count/business identity before redemption (existing enumeration tests plus new ones for the `MultipleMembers` code path).
- Missing name (sign-in and invite-accept): required once, persisted via `User.SetName`, then returned by `/auth/me` and available for attribution.
- Existing name: `/auth/continue` never prompts or overwrites.
- Invalid / expired / replayed / cross-user / suspended / removed selections at `/auth/continue` all fail with the same generic error, and every such terminal outcome clears the `ophalo.continuation` cookie.
- Invite acceptance with a missing name routes through continuation into the correct workspace only.
- The raw continuation token never appears in any `/auth/exchange` or `/auth/continue` JSON body (request or response) in tests or in a manual network-tab check — it is asserted to travel only via the `ophalo.continuation` cookie, with `HttpOnly`/`Secure`/`SameSite` set.
- `/auth/continue` ignores a `clientType`/`deviceName` supplied in its request body (if a test sends one, the resulting session must match the continuation's stored value, not the supplied one).
- Existing session, sign-out, role checks, account isolation, and raw-token non-exposure regression tests stay green.
