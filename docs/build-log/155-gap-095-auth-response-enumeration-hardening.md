# BL155 — GAP-095: Auth-response enumeration hardening

**Status:** GAP-095 complete. 095-2 reviewed and committed as `d1d2d0dc` 2026-09-11 —
`ExchangeAuthService` no longer returns `entryContext` for stale/used codes. 095-1 reviewed and
committed as `67648891` 2026-09-11 — magic-link email dispatch moved off the request path,
closing the start/signin timing oracle.

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

## 095-1 — async magic-link dispatch (landed 2026-09-11, `67648891`)

`StartAuthService`/`SignInAuthService` enqueue to `IMagicLinkDispatchQueue` instead of awaiting
`IEmailSender.SendAsync` inline. `MagicLinkDispatchQueue` (`OpHalo.Foundation.Application.Auth`)
wraps an unbounded `Channel<T>` with an explicit `Interlocked` depth counter enforcing
`MagicLinkDispatchSettings.QueueCapacity` (default 256, configurable via
`MagicLinkDispatch:QueueCapacity`) — not the channel's own bounded-capacity drop modes, since
every non-`Wait` `BoundedChannelFullMode` reports `TryWrite` as successful even when it silently
drops, which would make the drop unobservable. `MagicLinkDispatchBackgroundService` is the sole
consumer, one DI scope and one try/catch per item.

One placement deviation from the original sketch, decided during implementation preflight: the
`BackgroundService` lives in `OpHalo.Api/Auth` rather than `OpHalo.Foundation.Application`, matching
the existing `FeedbackMaintenanceBackgroundService` precedent (hosted-service scheduling stays in
the host project; the queue/business logic stays in `Application`). No locked behavioral decision
changed.

Graceful shutdown (added during code review, not in the original sketch): `StopAsync` calls
`MagicLinkDispatchQueue.CompleteAdding()` to stop admitting new work, then lets the read loop
(driven by `CancellationToken.None`, not the host's `stoppingToken` — `BackgroundService.StopAsync`
cancels that token immediately) drain whatever is already buffered, bounded by the host's own
shutdown deadline. If the deadline wins, the log reports both still-buffered depth and any item
actively mid-`SendAsync` (`MagicLinkDispatchQueue.Depth` excludes the latter, since it's already
been dequeued — tracked separately via `_inFlightItem` in the `BackgroundService`).

Drops (queue-full or post-shutdown-admission) are logged and counted via a
`System.Diagnostics.Metrics.Counter<long>` (`magic_link_dispatch.dropped`, tagged
`reason=queue_full|shutting_down`) — no metrics backend is wired up to scrape it yet, but the
counter exists for whenever one is.

Tests: existing auth-email-asserting tests (`AuthStartTests`, `AuthMagicLinkTests`,
`AuthEmailFailureLoggingTests`, `RateLimitIntegrationTests`, `AuthContinueTests`,
`AuthMobileHandoffTests`, `KeepIntakeApiTests`, etc.) stay deterministic via a synchronous
test-double `IMagicLinkDispatchQueue` wired into every `WebApplicationFactory` that overrides
`IEmailSender` and asserts on sent emails (`KeepApiWebFactory`, `PilotCapWebFactory`,
`ReleaseGateClosedWebFactory`, `FailingEmailWebFactory`, `RateLimitWebFactory`/
`RateLimitNoTrustWebFactory`) — no per-test-file changes needed beyond that. A dedicated suite
(`MagicLinkDispatchBackgroundServiceTests.cs`) exercises the real channel/worker via a scripted
blocking/throwing `IEmailSender`: non-blocking admission (response completes while the worker is
still blocked in `SendAsync`), queue-full drop without affecting the caller, one item's exception
not stopping the next, and both shutdown-drain outcomes (drains in time; logs abandonment when the
deadline wins, including the in-flight-only case `Depth` alone would miss).

7 prod files, 4 test files. `OpHalo.IntegrationTests` affected scope 115/115 (6/6 in the dedicated
dispatch suite). Full solution build clean, architecture tests 14/14.

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

**Acceptance criteria — all met:**

1. No request path awaits `IEmailSender.SendAsync`.
2. Queue admission never blocks the HTTP response.
3. Queue-full and send-failure outcomes are observable via logs/metrics but indistinguishable to
   callers.
4. Shutdown attempts a bounded drain; undrained work (buffered and in-flight) is logged.
5. Tests cover non-blocking admission, queue-full, and provider-failure scenarios — verifying that
   saturation does not introduce a new oracle — via a scripted blocking/throwing `IEmailSender`
   rather than wall-clock timing comparisons.
