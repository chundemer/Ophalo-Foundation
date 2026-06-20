# Build Log 048 — Gap G1: Keep Account-Safe Schema

**Session:** Pre-Session 6 Gap G1
**Date:** 2026-06-19
**Status:** Complete — full suite green

---

## Purpose

Correct GAP-005, GAP-006, GAP-010, and GAP-015 from build-log/047 before coding Phase 8-B5 Session 6.

Goal: make the greenfield Keep schema account-safe and establish trustworthy identity/activity
semantics — enforced at the database level before any new Keep features are built on top.

---

## Relationships Audited

Every Keep entity-to-entity relationship was inspected against the EF configuration and the database
constraint set. The full audit covered:

| Relationship | Prior constraint | G1 constraint |
|---|---|---|
| `KeepCustomer.AccountId` → `accounts` | none | Simple FK, Restrict |
| `KeepCustomer (AccountId, Id)` | none | Alternate key (enables composite FK targets) |
| `KeepCustomer (AccountId, CanonicalPhone)` | `(AccountId, PrimaryPhone)` unique | Renamed + moved to `CanonicalPhone` |
| `KeepRequest.AccountId` → `accounts` | none | Simple FK, Restrict |
| `KeepRequest (AccountId, CustomerId)` → `KeepCustomer (AccountId, Id)` | simple FK on CustomerId | Composite composite FK, Restrict |
| `KeepRequest (AccountId, Id)` | none | Alternate key |
| `KeepRequest.FirstResponderAccountUserId` → `AccountUser (AccountId, Id)` | none | Composite FK (nullable), Restrict |
| `KeepRequest.AttentionClearedByAccountUserId` → `AccountUser (AccountId, Id)` | none | Composite FK (nullable), Restrict |
| `KeepRequest.FeedbackReviewedByAccountUserId` → `AccountUser (AccountId, Id)` | none | Composite FK (nullable), Restrict |
| `KeepRequest.FirstResponseEventId` | none | Deferred (circular dependency — see below) |
| `KeepRequestEvent (AccountId, RequestId)` → `KeepRequest (AccountId, Id)` | simple FK on RequestId | Composite FK, Restrict |
| `KeepRequestEvent.ActorAccountUserId` → `AccountUser (AccountId, Id)` | none | Composite FK (nullable), Restrict |
| `KeepRequestEvent.ParticipationTargetAccountUserId` | none | Composite FK (nullable), Restrict |
| `KeepRequestEvent.ParticipationPreviousResponsibleAccountUserId` | none | Composite FK (nullable), Restrict |
| `KeepRequestEvent.ParticipationNotificationIntendedRecipientAccountUserId` | none | Composite FK (nullable), Restrict |
| `KeepRequestParticipant (AccountId, RequestId)` → `KeepRequest (AccountId, Id)` | simple FK | Composite FK, Restrict |
| `KeepRequestParticipant (AccountId, AccountUserId)` → `AccountUser (AccountId, Id)` | none | Composite FK, Restrict |
| `KeepPublicIntakeLink.AccountId` → `accounts` | none | Simple FK, Restrict |
| `KeepPublicIntakeLink (AccountId) WHERE active` | none | Partial unique index (one active per account) |
| `KeepResponsePolicy.AccountId` → `accounts` | none | Simple FK, Restrict |
| `AccountUser (AccountId, Id)` | none | Alternate key (enables composite FK targets from Keep) |

**Deferred:** `KeepRequest.FirstResponseEventId → KeepRequestEvent.Id` was not added in G1 because
`KeepRequest` and `KeepRequestEvent` have a circular FK relationship (Request references its first
response event; events reference their request). The EF navigation and FK setup would require
careful nullable FK ordering. Deferred to a focused follow-up session when the first-response-event
assignment flow is implemented.

---

## Canonical Phone Rule (D4 / ADR-304)

`PhoneNormalizer.Normalize(string phone)` strips all non-ASCII-digit characters using
`char.IsAsciiDigit`. `PhoneNormalizer.IsValidLength(string canonical)` checks 7–15 digits
(E.164 minimum through E.164 maximum). `KeepCustomer.Create` enforces both as a domain invariant
(`ArgumentException` on violation). Stores both:

- `PrimaryPhone` — the caller-supplied/trimmed display form
- `CanonicalPhone` — the normalized digit-only identity form (max 15, varchar 15 in DB)

The unique constraint moves from `(AccountId, PrimaryPhone)` to `(AccountId, CanonicalPhone)`,
so "0412 345 678" and "0412-345-678" map to the same customer and the constraint fires correctly.

---

## Activity Semantics (D3 / ADR-303)

`LastBusinessActivityAt` is `DateTime?` (nullable). Storage and API expose the factual null.
Creation semantics:
- `CreateFromCustomerIntake` sets `LastCustomerActivityAt = nowUtc`, `LastBusinessActivityAt = null`
- `CreateByBusiness` sets `LastBusinessActivityAt = nowUtc`, `LastCustomerActivityAt = null`

Internal ranking fallback for any computation needing "most recent activity":
`LastBusinessActivityAt ?? LastCustomerActivityAt ?? CreatedAtUtc`

Applied in `GetKeepRequestListService` at the sort and ticks-for-cursor computations.

---

## Named Factories (D5 / ADR-305)

`KeepRequest.Create` removed. Replacements:
- `CreateFromCustomerIntake(accountId, customerId, customerName, customerPhone, customerEmail, description, referenceCode, pageToken, nowUtc, firstResponseTargetMinutes)` — 10 params
- `CreateByBusiness(accountId, customerId, customerName, customerPhone, customerEmail, description, referenceCode, pageToken, nowUtc)` — 9 params (no firstResponseTargetMinutes; no first-response timer on business-origin)

Both call a private `CreateCore`. Unit tests updated for both; test helpers updated to dispatch on origin.

---

## Migration

Name: `20260619235301_KeepG1AccountSafeSchema`
Project: `src/OpHalo.Foundation.Infrastructure`
Context: `OpHaloDbContext`

Up operations:
- Drop old `ix_keep_customers_account_phone` (PrimaryPhone-based, replaced)
- Alter `keep_requests.last_business_activity_at` nullable: true
- Add `keep_customers.canonical_phone` varchar(15) not null default ""
- Add 3 alternate key constraints: `ak_keep_requests_account_id`, `ak_keep_customers_account_id`, `ak_account_users_account_id`
- Create 10 FK support indexes (composite account + FK column pairs)
- Create partial unique index `ix_keep_public_intake_links_account_active` (filter: revoked_at_utc IS NULL AND deleted_at_utc IS NULL)
- Create unique index `ix_keep_customers_account_canonical_phone`
- Add 14 FK constraints (all Restrict)

---

## Test Results

| Suite | Before G1 | After G1 |
|---|---|---|
| Unit | 494 | 503 (+9 canonical phone, activity semantics, named factory tests) |
| Architecture | 14 | 14 |
| Integration | 339 | 343 (+4 new G1 proof tests) |
| **Total** | **847** | **860** |

**G1-specific integration tests (KeepPersistenceProofTests):**
- `Keep_migration_applies_and_creates_all_tables`
- `KeepRequest_and_event_round_trip` (verifies LastCustomerActivityAt=Now, LastBusinessActivityAt=null)
- `Duplicate_canonical_phone_within_account_is_rejected` (23505 on ix_keep_customers_account_canonical_phone)
- `Same_canonical_phone_under_different_accounts_is_accepted`
- `Request_cannot_reference_customer_from_different_account` (23503 composite FK)
- `Event_cannot_reference_request_from_different_account` (23503 composite FK)
- `Second_active_intake_link_for_same_account_is_rejected` (23505 partial unique index)
- `Revoked_link_allows_new_active_link_for_same_account`
- `Active_slug_uniqueness_enforced`
- `Soft_deleted_keep_rows_are_hidden_by_query_filter`

---

## Implementation Notes

**EF Core 10 / `HasPrincipalKey` type argument issue:** EF Core 10's
`ReferenceCollectionBuilder<TEntity, TDependent>` exposes `HasPrincipalKey(Expression<Func<TEntity, object?>>)` — the generic type is inferred from the builder chain. Writing `.HasPrincipalKey<T>(...)` with an explicit type argument fails to compile. All occurrences in KeepRequestConfiguration, KeepRequestEventConfiguration, and KeepRequestParticipantConfiguration were fixed to remove the explicit type arg.

**Two-phase account provisioning required in KeepPersistenceProofTests:** The `Account ↔ AccountUser` circular FK (ADR-019) requires two-phase `SaveChangesAsync` (null the PrimaryOwnerAccountUserId, save, then update it). `KeepPersistenceProofTests.SeedAccountAsync` now follows the same pattern as `PersistenceProofTests.PersistGraph`.

**Stale participant representation:** Under G1's composite FK `(AccountId, AccountUserId) → AccountUser`, inserting a `KeepRequestParticipant` with a non-existent AccountUserId is prohibited. The integration test `KeepRequestParticipationApiTests` previously seeded a stale responsible using `Guid.NewGuid()`. Fixed: use the Viewer-role AccountUser (an ineligible role — not Owner/Admin/Operator). The application's staleness detection (`responsibleUserInfo` lookup + `IsEligible` check) correctly reports `responsibleIsStale = true` for Viewer-assigned participants. The FK semantics are correct: a "stale" participant is one whose AccountUser exists but is not eligible, not one whose AccountUser is missing.

---

## Exit Gate

- All 5 decision records (D1–D5) added to decision-index (ADR-301 to ADR-305)
- Migration generated, Up/Down inspected, model snapshot matches intent
- 860/860 tests passing (503 unit + 14 arch + 343 integration)
- No production code shortcuts or rationalized gaps
- G2 (public-intake validation and concurrent customer recovery) unblocked
