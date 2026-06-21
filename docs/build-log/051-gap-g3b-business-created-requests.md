# Build Log 051 — Gap G3b: Authenticated Business-Created Requests

**Date:** 2026-06-20
**Gap:** GAP-002
**ADRs:** ADR-315 to ADR-318
**Baseline:** 941 tests (540 unit · 14 arch · 387 integration) — G3a exit
**Exit:** 986 tests (568 unit · 14 arch · 404 integration) — all green

---

## What was built

`POST /keep/requests` — Owner/Admin/Operator creates a Keep request on behalf of a customer
from a phone call, walk-in, referral, or voicemail.

### Moved / adapted from reference app

No direct port. The public intake path (G1/G2) provided the customer normalization,
canonical matching, contact-update, and race-recovery logic. G3b layers authenticated
actor context and business-origin semantics on top without forking a second identity path.

### New files

| File | Purpose |
|---|---|
| `Keep.Application/Validation/KeepRequestInputValidator.cs` | Shared validation pipeline extracted from public intake (required → lengths → phone chars → phone format → email). Prevents drift between the two creation paths |
| `Keep.Application/Requests/CreateBusinessRequestCommand.cs` | Command record: CustomerName, CustomerPhone, CustomerEmail, Description |
| `Keep.Application/Requests/BusinessRequestCommitResult.cs` | Enum: Committed=1, UniqueTokenCollision=2, CustomerCanonicalPhoneCollision=3 |
| `Keep.Application/Requests/IKeepBusinessRequestPersistence.cs` | Interface: FindCustomerByCanonicalPhoneAsync, PageTokenExistsAsync, ReferenceCodeExistsAsync, CommitBusinessRequestAsync |
| `Keep.Application/Requests/CreateBusinessRequestService.cs` | Auth → validation → actor display name → customer find/create → retry loop → commit → BuildDetailResultAsync |
| `Keep.Infrastructure/Persistence/KeepIntakeCommitHelper.cs` | Internal static helper shared by public intake and business request paths: adds entities to tracker, SaveChanges, catches 23505 token/phone collisions and 23503 reference-code collision, detaches on failure |
| `Keep.Infrastructure/Persistence/KeepBusinessRequestPersistence.cs` | Delegates FindCustomer, PageTokenExists, ReferenceCodeExists, CommitBusinessRequestAsync to the helper and shared queries |
| `Api/Keep/CreateBusinessRequestBody.cs` | Request body record: CustomerName, CustomerPhone, CustomerEmail, Description |
| `tests/OpHalo.IntegrationTests/Api/KeepBusinessRequestApiTests.cs` | 13 HTTP integration tests |

### Modified files

| File | Change |
|---|---|
| `Keep.Core/Entities/KeepRequestEvent.cs` | +5-param `CreateRequestCreated` overload (actorAccountUserId, actorDisplayName, occurredAtUtc); Visibility=System, ActorType=AccountUser; `occurredAtUtc == default` guard |
| `Keep.Application/PublicIntake/CreateKeepPublicIntakeService.cs` | Refactored to use shared `KeepRequestInputValidator`; removed private email validator and phone char helper |
| `Keep.Infrastructure/Persistence/KeepIntakePersistence.cs` | Delegates commit to `KeepIntakeCommitHelper` |
| `Api/Helpers/ErrorHttpMapper.cs` | +3 entries: CustomerNameRequired, CustomerPhoneRequired, DescriptionRequired → 400 |
| `Api/Program.cs` | DI: IKeepBusinessRequestPersistence, CreateBusinessRequestService; POST /keep/requests → 201 |
| `tests/OpHalo.IntegrationTests/Persistence/KeepPersistenceProofTests.cs` | 5 G3b persistence proof tests appended; all `Guid.NewGuid()` actor IDs replaced with `AccountOwnerAccountUserId`; `r.CustomerId` → `r.KeepCustomerId` fix |
| `tests/OpHalo.UnitTests/Keep/KeepCreateBusinessRequestServiceTests.cs` | 21 unit tests (existed from prior session; confirmed passing) |

---

## Design decisions (ADR-315..318)

**ADR-315 — Two-interface pattern:** `IKeepRequestOperatePersistence` for auth snapshots and actor
lookup; `IKeepBusinessRequestPersistence` for customer ops and commit. Kept separate so the commit
path does not accumulate operate-level concerns and `KeepIntakeCommitHelper` can be shared.

**ADR-316 — Endpoint shape:** `POST /keep/requests` → 201 Created with `KeepRequestDetailResult`
directly. No wrapper needed (`KeepRequestDetailResult` already carries `PageToken`).

**ADR-317 — Viewer rejection order:** Viewer is rejected immediately after userSnapshot load,
before the account/feature/OffSeason stack. Consistent with how Viewer is treated across
all write services.

**ADR-318 — OffSeason and actor event:** `RequestImplementsAllowedInOffSeason: false`;
`decision.IsBlocked || decision.IsReadOnly`. Feature gate: `FeatureKeys.Keep.OperatorQueue`.
Actor event uses the 5-param `CreateRequestCreated` overload carrying the authenticated
AccountUserId and display name; Visibility=System; ActorType=AccountUser.

---

## Bugs found and fixed during implementation

1. **`r.CustomerId` → `r.KeepCustomerId`** — Persistence proof test used the wrong property
   name; domain entity uses `KeepCustomerId`.

2. **`Guid.NewGuid()` as actor ID in proof tests** — G1 added composite FK constraints on
   `KeepRequestEvent.ActorAccountUserId`. Proof tests that pass a random GUID as actor hit FK
   23503. Fixed by using the seeded `AccountOwnerAccountUserId` from the class `InitializeAsync`.

---

## Test coverage

**Unit tests (21, existing):**
- Unauthenticated → 401 path
- Viewer → 403 path
- Owner/Admin/Operator → allowed
- Permission denied → 403
- Account blocked → 403, OffSeason → 403, feature disabled → 403
- Validation errors: name/phone/desc required; bad phone chars/format; bad email
- Actor lookup not called on validation failure
- New customer happy path, existing customer reuse, token collision retry, phone collision race recovery

**Persistence proof tests (5):**
- Page-token collision → UniqueTokenCollision + tracker clean
- Reference-code collision → UniqueTokenCollision
- Canonical-phone collision → CustomerCanonicalPhoneCollision + tracker clean
- Unrelated FK violation → propagates as DbUpdateException
- Phone collision then retry with winner → Committed; one customer, one request in DB

**HTTP integration tests (13):**
- Owner → 201 with full shape: origin="business", eventType="request_created", actorType="account_user", actorAccountUserId set, actorDisplayName set, action flags correct
- Admin → 201
- Operator → 201
- Viewer → 403
- Unauthenticated → 401
- Missing name → 400 (KeepRequest.CustomerNameRequired)
- Missing phone → 400 (KeepRequest.CustomerPhoneRequired)
- Missing description → 400 (KeepRequest.DescriptionRequired)
- Bad phone chars → 400 (KeepRequest.CustomerPhoneInvalidCharacters)
- OffSeason (inline account seed) → 403
- Same phone, two requests → single KeepCustomer row, two KeepRequest rows
- Two requests, same customer → both events carry actorAccountUserId

---

## Exit gate

- `dotnet build --verbosity minimal` → 0 warnings, 0 errors
- Unit: 568/568 ✓
- Architecture: 14/14 ✓
- Integration: 404/404 ✓
- **Total: 986/986 ✓**
- GAP-002 closed; G3b marked complete in session log
