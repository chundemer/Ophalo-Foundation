# Build Log 066 — Session 12 Account Settings And Onboarding

**Date:** 2026-06-27
**Branch:** main
**Status:** S12a + S12b complete
**Next free ADR before this log:** ADR-373
**Current ADR after S12 pre-build decisions:** ADR-377

---

## Session Goal

Implement the minimum V1 account setup foundation for Keep:

- Keep-specific customer-facing business contact;
- simple response/status-check policy settings;
- onboarding checklist signals derived from product-ops events.

Session 12 is split into two independently committed slices:

- **S12a** — Keep business profile + response policy setup API.
- **S12b** — product-ops events + onboarding checklist signals.

---

## Locked Decisions

### ADR-373 — KeepBusinessProfile holds Keep customer-facing contact

`KeepBusinessProfile` is a Keep satellite entity in `OpHalo.Keep.Core`. It stores
`CustomerFacingPhone` and `CustomerFacingEmail`, both nullable, one row per account via a unique
account foreign key.

`Account.BusinessName` and `Account.TimeZone` remain in Foundation because they are account-wide and
product-neutral. Customer-facing contact belongs to Keep because it powers Keep intake and customer
pages. Future products may have different public contact rules.

### ADR-374 — Keep setup policy is an upsert

`PUT /keep/setup/policy` is an upsert: create `KeepResponsePolicy` if missing, update it if present.
Policy is required operational configuration that drives attention timing, not a user-created
collection resource.

`KeepResponsePolicy.Update(...)` mutates the existing row in place. `StatusCheckThresholdDays` is
added as a required fourth policy field with a V1 default of 5 calendar days.

### ADR-375 — Onboarding checklist derives from product-ops events

The V1 onboarding checklist is derived from durable product-ops event rows. There is no separate
checklist state entity in V1.

`GET /keep/setup/onboarding` will translate event rows into step-complete status at read time. The
event log is the onboarding source of truth per INT-002.

### ADR-376 — Session 12 batch split

S12a implements `KeepBusinessProfile`, the `KeepResponsePolicy` extension, the GET/PUT setup
settings API, and migration.

S12b implements `KeepProductOpsEvent`, onboarding signal recording, and the GET checklist endpoint.

---

## S12a — Complete

**Commit:** `ac635c8` — `S12a: Keep business profile + response policy settings API`

### Scope Delivered

- `KeepBusinessProfile` entity with `Create(accountId)` and `UpdateContact(phone, email)`.
- Blank/whitespace customer-facing contact values are stored as `null`.
- `KeepResponsePolicy.StatusCheckThresholdDays`.
- `KeepResponsePolicy.Update(...)`.
- Setup application service and persistence.
- Owner/Admin setup endpoints:
  - `GET /keep/setup`;
  - `PUT /keep/setup/profile`;
  - `PUT /keep/setup/policy`.

### Production Files

1. `src/OpHalo.Keep.Core/Entities/KeepBusinessProfile.cs`
2. `src/OpHalo.Keep.Infrastructure/Persistence/Configurations/KeepBusinessProfileConfiguration.cs`
3. `src/OpHalo.Keep.Application/Setup/IKeepSetupPersistence.cs`
4. `src/OpHalo.Keep.Application/Setup/KeepSetupResults.cs`
5. `src/OpHalo.Keep.Application/Setup/KeepSetupService.cs`
6. `src/OpHalo.Keep.Infrastructure/Persistence/EfKeepSetupPersistence.cs`
7. `src/OpHalo.Keep.Core/Entities/KeepResponsePolicy.cs`
8. `src/OpHalo.Keep.Infrastructure/Persistence/Configurations/KeepResponsePolicyConfiguration.cs`
9. `src/OpHalo.Api/Program.cs`

### Tests

- `tests/OpHalo.UnitTests/Keep/KeepBusinessProfileTests.cs`
- `tests/OpHalo.UnitTests/Keep/KeepResponsePolicyTests.cs`

Verification reported by Christian:

```text
Build successful
29 new unit tests green
14 architecture tests green
```

### Migration

Migration:
`src/OpHalo.Foundation.Infrastructure/Migrations/20260627102717_KeepSetupBusinessProfileAndPolicyThreshold.cs`

Shape:

- adds `keep_business_profiles`;
- adds unique index on `keep_business_profiles.account_id`;
- adds `keep_response_policies.status_check_threshold_days` as non-null integer with
  `defaultValue: 5`.

Correct EF command pattern for Keep model migrations:

```bash
dotnet ef migrations add KeepSetupBusinessProfileAndPolicyThreshold \
  --project src/OpHalo.Foundation.Infrastructure \
  --startup-project src/OpHalo.Keep.Infrastructure \
  --context OpHaloDbContext

dotnet ef database update \
  --project src/OpHalo.Foundation.Infrastructure \
  --startup-project src/OpHalo.Keep.Infrastructure \
  --context OpHaloDbContext
```

---

## S12b — Complete

**Commit:** `1f3fb26` — `S12b: KeepProductOpsEvent entity, onboarding signal recording, and GET checklist endpoint`

### Scope Delivered

- `KeepProductOpsEventType` enum — 17 V1 signal types per INT-003; singleton and recurring types distinguished in comments.
- `KeepProductOpsEvent` entity with `Record(accountId, eventType, occurredAtUtc)` factory.
- EF configuration (`keep_product_ops_events` table) with unique index on `(account_id, event_type)`.
- Migration `20260627111847_KeepProductOpsEvents`.
- `IKeepSetupPersistence.SaveProfileAsync` / `SavePolicyAsync` extended with optional `KeepProductOpsEvent?` parameter — profile/policy and event staged together in one `SaveChangesAsync` (atomicity fix per reviewer).
- `EfKeepSetupPersistence.StageEventIfFirstAsync` — checks existence then adds entity to context; no separate save.
- `IKeepProductOpsPersistence` with `RecordEventIfFirstAsync` (standalone save + concurrent-insert catch) and `GetOnboardingDataAsync`.
- `EfKeepProductOpsPersistence` — catches `PostgresException 23505` in `RecordEventIfFirstAsync`; queries event rows + live Foundation state (intake link, member count, device count, request count) for checklist.
- `KeepOnboardingService` with `GetChecklistAsync` and `MarkStepCompleteAsync`.
- Endpoints:
  - `GET /keep/setup/onboarding`;
  - `POST /keep/setup/onboarding/marks/quick-capture-exercise`;
  - `POST /keep/setup/onboarding/marks/tracker-review`;
  - `POST /keep/setup/onboarding/marks/spam-classification`.

### Checklist Step Derivation

| Step | Source |
|------|--------|
| 1–2. Profile & timezone | `ProfileAndContactSaved` event row |
| 3. Policy | `PolicySaved` event row |
| 4. Intake active | `KeepPublicIntakeLink` live state |
| 5. Operator invited | `AccountUser` count (non-Owner, active) |
| 6. Mobile device | `AccountUserDevice` count for account |
| 7. First request | `KeepRequest` count for account |
| 8–10. Manual marks | `QuickCaptureExerciseDone` / `TrackerReviewDone` / `SpamClassificationExplained` event rows |

### Production Files

1. `src/OpHalo.Keep.Core/Entities/Enums/KeepProductOpsEventType.cs`
2. `src/OpHalo.Keep.Core/Entities/KeepProductOpsEvent.cs`
3. `src/OpHalo.Keep.Infrastructure/Persistence/Configurations/KeepProductOpsEventConfiguration.cs`
4. `src/OpHalo.Keep.Application/Setup/IKeepProductOpsPersistence.cs`
5. `src/OpHalo.Keep.Application/Setup/KeepOnboardingService.cs`
6. `src/OpHalo.Keep.Infrastructure/Persistence/EfKeepProductOpsPersistence.cs`
7. `src/OpHalo.Keep.Application/Setup/IKeepSetupPersistence.cs` (modified)
8. `src/OpHalo.Keep.Application/Setup/KeepSetupService.cs` (modified)
9. `src/OpHalo.Keep.Infrastructure/Persistence/EfKeepSetupPersistence.cs` (modified)
10. `src/OpHalo.Api/Program.cs` (modified)

### Tests

- `tests/OpHalo.UnitTests/Keep/KeepProductOpsEventTests.cs`
- `tests/OpHalo.UnitTests/Keep/KeepOnboardingServiceTests.cs`
- `tests/OpHalo.IntegrationTests/Api/KeepOnboardingApiTests.cs`

### Verification

```
939 unit · 14 arch · 705 integration = 1,658 total, 0 failures
(1 pre-existing KeepG5ConcurrencyVersionMigrationTests fluke excluded — unrelated to S12b)
```
