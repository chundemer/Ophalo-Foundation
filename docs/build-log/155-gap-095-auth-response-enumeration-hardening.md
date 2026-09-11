# BL155 — GAP-095: Auth-response enumeration hardening

**Status:** 095-2 reviewed and committed as `d1d2d0dc` 2026-09-11 — `ExchangeAuthService` no longer
returns `entryContext` for stale/used codes. 095-1 (async magic-link dispatch, closing the timing
oracle) not started.

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

**Locked decisions (2026-09-11, Christian):**

- Bounded `Channel<T>` with a deliberately chosen capacity, **not** `BoundedChannelFullMode.Wait`.
  `Wait` would make enqueue block only for requests that produce a real email, recreating a timing
  distinction under saturation — the exact oracle 095-1 exists to close. Use non-blocking
  `TryWrite` with a drop-on-full policy instead (structured log + metric/alert on drop; no
  behavioral difference to the caller).
- The public `/auth/start` and `/signin` response is identical regardless of outcome: enqueued,
  dropped-on-full, or later send failure. None of these may surface in status code, body, or
  timing.
- Single consumer wraps each dequeued send in try/catch and logs failures per-item; one provider
  exception must not terminate the `BackgroundService`.
- Shutdown: attempt a bounded drain within the host's shutdown timeout (stop accepting new work,
  drain what's queued), and log any work still queued if cancellation wins. This is a best-effort
  drain, not a delivery guarantee — a process crash can still lose in-memory jobs, which is
  acceptable only because delivery is already best-effort (D4/D8). If reliable delivery across
  restarts becomes a requirement, that is the threshold for a durable outbox, not a reason to build
  one now.
- Rate limiting / abuse controls on `/auth/start` remain a separate, still-important concern since
  the endpoint is also an email-send amplification surface.

**Acceptance criteria:**

1. No request path awaits `IEmailSender.SendAsync`.
2. Queue admission never blocks the HTTP response.
3. Queue-full and send-failure outcomes are observable via logs/metrics but indistinguishable to
   callers.
4. Shutdown attempts a bounded drain; undrained work is logged.
5. Timing tests cover real-email, unknown-email, queue-full, and provider-failure scenarios —
   specifically verifying that saturation does not introduce a new oracle.
