# Build Log 057 — Gap G6: Cancelled Customer-Page Expiry Correction

**Gap:** GAP-011 · **Decision:** ADR-297 (implemented in part)
**Baseline entering:** 1182 tests (619 unit · 14 architecture · 549 integration)
**Baseline leaving:** 1188 tests (621 unit · 14 architecture · 553 integration)

---

## Domain change

`KeepRequest.ChangeStatus` (`src/OpHalo.Keep.Core/Entities/KeepRequest.cs`):

- Added `private const int CancelledPageRetentionDays = 30`.
- After the terminal block sets `TerminatedAtUtc`, a `Cancelled`-specific branch sets
  `ExpiresAtUtc = nowUtc.AddDays(CancelledPageRetentionDays)`.
- Closed expiry is intentionally excluded — Session 6 owns the dedicated close command.

## Ordering and atomicity

The existing G5 commit path in `ChangeKeepRequestStatusService` already calls
`RotateConcurrencyVersion()` and persists the request plus the status event in one
`EfKeepRequestOperatePersistence.CommitAsync` call. No persistence, service, or API signature changes
were required; `ExpiresAtUtc` is already mapped by the detail mapper and EF configuration.

## Customer page lifecycle

After cancellation:
- GET `/keep/r/{token}` returns 200 with `status = "cancelled"`, `isTerminal = true`,
  `allowedActions = []`, and a future `expiresAtUtc`.
- Once `ExpiresAtUtc` is in the past (and `Status` is terminal), the guard returns 410 with
  the established safe-context tombstone shape (`isExpired = true`, null status/description/
  events/version).

## Defensive ADR-120 boundary

`KeepPublicCustomerAccessGuard` enforces terminal-only expiry. A stale past `ExpiresAtUtc`
on a Received or Resolved request does not tombstone the customer page.

## Tests added

- `KeepRequestTests`: 2 unit tests — Received→Cancelled success (asserts
  `ExpiresAtUtc == Now + 30d`); missing-message rejection (status/termination/expiry all null).
- `ChangeKeepRequestStatusTests`: 2 integration tests — successful cancellation (response +
  fresh DB `expiresAtUtc == terminatedAtUtc + 30d`, version rotation, event exists; then
  customer page 200/cancelled/no-write-actions/future-expiry, then past-expiry 410); stale-version
  cancellation (409 `RequestChanged`, original Received status/version, null termination/expiry,
  no new event).
- `KeepCustomerPageTests`: 2-case theory (Received, Resolved) — past `expires_at_utc` written
  via raw SQL does not tombstone a non-terminal page; GET returns 200 with live status and version.

## Exclusions

Closed expiry, close-and-next, Spam/Test immediate disablement (DEF-061), expiry jobs,
token rotation, notifications, and frontend behavior are all excluded from this slice.
