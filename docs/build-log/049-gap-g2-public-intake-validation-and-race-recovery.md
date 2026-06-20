# Build Log 049 — Gap G2: Public Intake Validation and Concurrent Customer Recovery

**Session:** Pre-Session 6 Gap G2
**Date:** 2026-06-20
**Status:** Complete — full suite green

---

## Purpose

Correct GAP-007, GAP-008, and GAP-009 from build-log/047, and complete the application-layer usage
of the canonical-phone identity from G1. The goal is that malformed or concurrent public submissions
never become avoidable 500s and never damage known customer data.

---

## Decisions Made (ADR-306 – ADR-310)

### ADR-306 — Ordered validation pipeline

All public intake field validation runs fail-fast before any token lookup or DB access:

1. Required fields: name, phone, description (existing)
2. Maximum lengths: name 200, phone 50 submitted chars, email 320 (when supplied), description 4000
3. Phone character allowlist check
4. Phone digit-count check (on canonical form)
5. Email syntax check (when supplied)
6. Token/account/feature gate (returns collapsed `Unavailable`)

Each step returns a named 400 error. No DB mutation occurs on any validation failure. Validation
errors do not expose account or token state.

### ADR-307 — Phone character allowlist and split errors

Allowed characters: ASCII digits (0–9), space, `-`, `(`, `)`, `.` anywhere; `+` only as the first
character (international prefix — e.g., `+61 412 345 678` is valid; `555+1234` and `++5551234`
are not). Letters and all other symbols are rejected.

Two distinct errors: `CustomerPhoneInvalidCharacters` (bad chars, including `+` in mid/repeat) and
`CustomerPhoneInvalidFormat` (correct chars but < 7 or > 15 digits after normalization).

### ADR-308 — UpdateContactInfo email preservation

`KeepCustomer.UpdateContactInfo(name, email)`:
- Always updates the trimmed name (existing behavior, unchanged)
- Replaces `Email` only when the incoming value is nonblank after trimming
- Null or blank email is **not** a clear-email command — it preserves the existing known email

This applies to both the repeat-intake path (existing customer found by canonical phone) and the
post-collision re-read path (winning customer loaded after race).

### ADR-309 — Concurrent customer race recovery

When `CommitPublicIntakeAsync` catches `ix_keep_customers_account_canonical_phone` (23505), it:
- Detaches all failed tracked entities (customer if new, request, event)
- Returns `PublicIntakeCommitResult.CustomerCanonicalPhoneCollision`

The service then:
- Re-reads the winning customer via `FindCustomerByCanonicalPhoneAsync`
- Applies `UpdateContactInfo` (name update + safe email update per ADR-308)
- Continues the loop with the winning tracked customer

Customer and token collisions share the existing `MaxAttempts = 5` ceiling. Unrelated DB failures
propagate as exceptions. Null after collision is an `InvalidOperationException` (should not happen
given committed customer must be visible).

### ADR-310 — EmailAddressAttribute for email format check

`System.ComponentModel.DataAnnotations.EmailAddressAttribute` is used (stateless, held as a
`static readonly` field). `MailAddress` was rejected — it is a parser and accepts display-name
forms (e.g., `"John Doe" <john@example.com>`) that are not appropriate for a plain email input.

---

## Files Changed

| File | Change |
|---|---|
| `Keep.Core/Errors/KeepRequestErrors.cs` | +7 errors: `CustomerNameTooLong`, `CustomerPhoneTooLong`, `CustomerPhoneInvalidCharacters`, `CustomerPhoneInvalidFormat`, `CustomerEmailTooLong`, `CustomerEmailInvalid`, `DescriptionTooLong` |
| `Keep.Core/Entities/KeepCustomer.cs` | `UpdateContactInfo`: email update conditioned on nonblank incoming value |
| `Keep.Application/PublicIntake/PublicIntakeCommitResult.cs` | `+CustomerCanonicalPhoneCollision = 3` |
| `Keep.Application/PublicIntake/CreateKeepPublicIntakeService.cs` | Full validation pipeline before token lookup; `HasValidPhoneCharacters` with `+`-first-only rule; `CustomerCanonicalPhoneCollision` case in retry loop with re-read and safe contact-update |
| `Keep.Infrastructure/Persistence/KeepIntakePersistence.cs` | `CommitPublicIntakeAsync`: new catch arm for `ix_keep_customers_account_canonical_phone` (23505) returning `CustomerCanonicalPhoneCollision` |
| `Api/Helpers/ErrorHttpMapper.cs` | +7 explicit 400 entries for all new validation errors |
| `UnitTests/Keep/KeepCustomerTests.cs` | Removed stale `clears_email_when_null` test; +5 email preservation tests |
| `UnitTests/Keep/KeepPublicIntakeServiceTests.cs` | Fixed stale "555" phone to "5551234"; added `CustomerResults` queue to fake; +18 new unit tests covering all 7 validation errors, phone character/format edge cases, email preservation (omit/replace), race recovery (collision → re-read → success, email preserved, max-attempts exhaustion) |
| `IntegrationTests/Api/KeepIntakeApiTests.cs` | +21 integration tests: max/max+1 boundaries for all four fields; phone character rejections and leading-`+` acceptance; phone digit-count boundaries; email syntax; no-mutation proof for one validation failure; repeat-intake email preservation (omit and replace); equivalent formatted phones → one customer; concurrent first submissions → one customer, two requests, two events |

---

## Test Results

| Suite | Before G2 | After G2 |
|---|---|---|
| Unit | 503 | 536 (+33) |
| Architecture | 14 | 14 |
| Integration | 349 | 370 (+21) |
| **Total** | **866** | **920** |

---

## Self-Review

- `HasValidPhoneCharacters` iterates once; `+` allowed only at `i == 0`; correctly rejects `555+1234` and `++5551234` before reaching the digit-count check.
- Validation order is strict: required → lengths → phone chars → phone format → email syntax → token. `trimmedEmail` is set to null for blank/whitespace inputs so all email checks only run for genuinely-supplied emails.
- The `EmailAddressAttribute` instance is `static readonly` — allocated once, safe to call concurrently.
- After a `CustomerCanonicalPhoneCollision`, the service throws `InvalidOperationException` if `FindCustomerByCanonicalPhoneAsync` returns null — that state should not occur (a committed customer must be visible to a subsequent read) but is explicitly guarded.
- The concurrent integration test uses `TaskCompletionSource` barrier to release two `CreateClient()` instances simultaneously, maximizing the chance of a true race. Even when the server processes them sequentially, the DB-level result (1 customer, 2 requests) is correct and the test passes.
- `KeepCustomerTests.UpdateContactInfo_clears_email_when_null` was the only test asserting the incorrect (GAP-009) behavior. It is replaced by four tests asserting the correct preservation semantics.
- No shortcuts, no rationalized gaps, no scope expansion.

---

## Exit Gate

- ADR-306 through ADR-310 recorded in decision index
- 920/920 tests passing (536 unit · 14 arch · 370 integration)
- No new migrations (pure application/infrastructure behavior change)
- No production code shortcuts
- G3 (Keep onboarding setup, durable intake controls, and manual intake) unblocked
