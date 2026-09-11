# BL155 — GAP-095: Auth-response enumeration hardening

**Status:** 095-2 landed 2026-09-11 — `ExchangeAuthService` no longer returns `entryContext` for
stale/used codes, awaiting Christian's diff review. 095-1 (async magic-link dispatch, closing the
timing oracle) not started.

**Scope:** [workboard](../workboard.md) Next item 7; audit Vector 8 F8.9, F8.10.
**Supervised-pilot gate.**

## Decisions

- F8.9 fix: move magic-link email dispatch off the request path with an in-memory `Channel<T>`
  background dispatcher (singleton `BackgroundService`, own DI scope per job) — not a durable
  persisted-outbox/retry pattern. Preserves the existing D8/D4 best-effort contract (delivery
  failure never changes the public response); a lost email on process crash carries the same
  practical risk as today's synchronous best-effort send failing. A durable outbox (reusing the
  `FeedbackDeliveryWorker` shape) was rejected: that precedent's 5/15/60/180-min backoff is far too
  slow for a magic link a user is actively waiting on, and durability isn't a requirement here.
- F8.10 fix: `/auth/exchange` omits `entryContext` for `Expired`/`AlreadyConsumed`/
  `CannotConsumeInvalidated` failures, matching the already-neutral unknown-code response. Accepted
  UX cost: the frontend's signin-hint shortcut (`web/ophalo-web/.../auth/exchange/error/page.tsx`,
  `isSigninContext`) no longer fires for stale/used links — they fall back to the generic
  "invalid or expired, start over" message. No alternate mitigation pursued; the generic message is
  the correct tradeoff per Christian.
- Split into two slices (095-1 async dispatch, 095-2 entryContext removal) to stay under the
  8-prod-file/12-total batch gate — same pattern as GAP-094's 094-1/094-2 split.

## 095-2 — entryContext removal (landed 2026-09-11)

`ExchangeAuthService.HandleAsync` now passes `null` instead of `code.EntryContext` on the
`Expired`, `AlreadyConsumed`, and `CannotConsumeInvalidated` branches (`ExchangeAuthService.cs`).
Updated three integration assertions that expected a stale/used code's classification in the 422
body (`AuthMagicLinkTests.Exchange_AlreadyConsumedCode_Returns422`,
`AuthStartTests.Exchange_ConsumedNewAccountCode_Returns422NoEntryContext` (renamed from
`...Returns422WithEntryContext`), and the old-code case in
`AuthStartTests.NewAccountRetry_...` around the `CannotConsumeInvalidated` assertion) to assert
`Assert.Null(body.EntryContext)`. No frontend change needed — the exchange error page already
falls back to its generic message when `entryContext` is absent from the response body.

1 prod file, 2 test files. `OpHalo.IntegrationTests` `AuthStartTests`/`AuthMagicLinkTests` 44/44.

## 095-1 — async magic-link dispatch (not started)

Planned: new dispatch-queue abstraction + `Channel<T>`-backed `BackgroundService` in
`OpHalo.Foundation.Application`; `StartAuthService` and `SignInAuthService` enqueue instead of
awaiting `IEmailSender.SendAsync` inline; `Program.cs` registers the channel + hosted service.
Existing integration tests/fakes (`AuthStartTests`, `AuthMagicLinkTests`,
`AuthEmailFailureLoggingTests`, `KeepApiWebFactory`) will need a way to await/flush the dispatch
queue before asserting on sent emails — exact fan-out to be confirmed at 095-1's implementation
preflight.
