# Phase 5C — New-Account Registration Discovery

**Status:** Locked for Phase 5C implementation. Decisions confirmed with Christian on 2026-06-15.
**Build-log preceding this:** 018-phase-5b-magic-link-auth-discovery.md
**Date:** 2026-06-15

**Reference files read:**
- `_reference/src/OpHalo.API/Endpoints/V1/AuthEndpoints.cs`
- `_reference/src/OpHalo.Application/Accounts/Commands/KeepOnboarding/Start/StartOnboardingCommandHandler.cs`
- `_reference/src/OpHalo.Application/Accounts/Commands/KeepOnboarding/Exchange/ExchangeOnboardingCodeCommandHandler.cs`
- `_reference/src/OpHalo.Core/Entities/Accounts/AccountOnboardingCode.cs`
- `_reference/src/OpHalo.Core/Entities/Accounts/Enums/EntryContext.cs`
- `_reference/src/OpHalo.Core/Entities/Accounts/Errors/AccountOnboardingCodeErrors.cs`
- `_reference/src/OpHalo.Application/Constants/MagicLinkEmailConstants.cs`
- `_reference/src/OpHalo.Application/Settings/AppSettings.cs`
- `_reference/web/ophalo-web/src/app/api/onboarding/start/route.ts`
- `_reference/web/ophalo-web/src/app/(marketing)/start/page.tsx`
- `_reference/web/ophalo-web/src/app/api/auth/exchange/route.ts`
- Current Foundation auth/provisioning/persistence code under `src/OpHalo.Foundation.*`, `src/OpHalo.Api/Auth`, and Keep public-intake link code

---

## Phase 5B state verified

Phase 5B already built the reusable magic-link/session foundation:

- `POST /auth/signin` issues `EntryContext.ExistingMember` codes for exactly one active member.
- `POST /auth/exchange` validates code state, handles `ExistingMember`, atomically consumes the code, creates browser/mobile sessions, and fails closed for unknown entry contexts.
- `AccountAuthCode.AccountId` and `TargetAccountUserId` are already nullable for the future `NewAccount` path.
- `EntryContext` currently contains only `ExistingMember = 2`.
- `ErrorHttpMapper.ToHttpResult(error, extraExtensions)` can attach `entryContext` without changing the status mapping.
- Direct `IEmailSender` is the temporary email delivery seam. There is no outbox/worker yet.
- Logging policy from 5B: do not log raw codes, tokens, magic-link URLs, token hashes, or email addresses. Log only unexpected server failures with safe IDs.

---

## Reference behavior summary

The reference app has three auth endpoints:

```text
POST /auth/signin   existing-member fast path
POST /auth/start    new-account registration plus existing-member fallback
POST /auth/exchange consumes code and creates session
```

Reference `/auth/start`:

1. Requires `App:PublicBaseUrl`.
2. Normalizes email.
3. Looks up existing account by `Account.Email`.
4. If no account exists:
   - checks pilot capacity when enabled;
   - invalidates prior active `NewAccount` codes for the email;
   - generates a slug snapshot from `businessName`;
   - creates only the code plus email outbox/work item.
5. If an account exists:
   - issues an `ExistingMember` code and invalidates prior active account codes.
6. Stores snapshots on the code: `businessName`, `name`, and `slug`.
7. Sends a magic link to `{App:PublicBaseUrl}/auth/exchange?code={rawCode}`.
8. Returns neutral `200 OK` with no auth code in the body.

Reference `/auth/exchange` `NewAccount` path:

1. Hashes and loads the code.
2. Rejects missing, expired, consumed, invalidated, or null `EntryContext`.
3. Re-checks that no account exists for the email.
4. Re-checks pilot capacity before consuming the code.
5. Opens a transaction.
6. Atomically consumes the code using a conditional update.
7. Creates `Account`, public intake token/slug, onboarding, notifications, entitlements, `User`, and owner `AccountUser`.
8. Saves and commits.
9. Creates a session outside the transaction.
10. Logs session creation failure and returns `Account.SessionCreationFailed`.

Important reference bug fix carried forward: code consumption and new-account creation must commit or roll back together. A failed account save must not leave the code consumed.

---

## Foundation divergences

### Identity lookup

Foundation intentionally removed `Account.Email` (ADR-021). New-account availability must be checked against:

- `Users.NormalizedEmail` / `Users.Email`, and
- `AccountUsers.NormalizedEmail` for invited or membership-only rows.

Recommended behavior:

- If any `User` already exists for the normalized email, `/auth/start` returns neutral `200 OK`, sends no new-account code, and does not create a second account.
- If any `AccountUser` exists for the normalized email, including invited/suspended/removed membership, `/auth/start` returns neutral `200 OK`, sends no new-account code unless it is exactly one active existing member handled by the existing-member branch.
- Existing active member fallback should use the same eligibility rule as `/auth/signin`.

### Account creation inputs

Foundation `AccountProvisioningService.CreateVerified(...)` requires:

```text
email
name?
businessName
purpose
timeZone
plan
isPilot
nowUtc
trialEndsAtUtc
```

The reference `/start` body only had `email`, `businessName`, and `name`. Phase 5C must add `timeZone` to the API contract or choose a default. ADR-022 says `Account.CreateVerified` requires a non-null time zone and IANA validation belongs upstream.

### Account satellites

Reference creates `AccountOnboarding` and `AccountNotifications`. Foundation has neither in this scope. Do not add them in 5C unless a separate discovery adds those bounded concepts.

### Keep public intake link

Reference stores `PublicSlug` and public intake token on `Account`. Foundation moved these to `KeepPublicIntakeLink` (ADR-046).

Current Keep pieces:

- `KeepPublicIntakeLink.Create(accountId, publicSlug, tokenHash)`
- `KeepTokenService.GeneratePublicIntakeToken()`
- `KeepTokenService.HashPublicIntakeToken(rawToken)`
- partial unique indexes on active slug and token hash

Confirmed 5C decision: do not create the Keep public-intake link during new-account exchange. Keep setup will create the link later, when the product flow is ready to show/configure it. This keeps 5C focused on auth, account provisioning, entitlements, and session creation.

### Email delivery

Reference uses outbox plus worker. Foundation currently uses direct `IEmailSender`.

Recommended 5C behavior:

- Persist the code first.
- Send email best-effort after commit.
- Public response remains `200 OK` whether delivery succeeds or fails.
- Log delivery failure as a warning with `AccountAuthCodeId` only, matching 5B.

This is not as durable as outbox delivery, but it is consistent with 5B until the messaging phase exists.

---

## Confirmed Phase 5C signup policy

Pilot and trial are different concepts:

- **Trial** is the commercial access state/window. It controls the unpaid trial period through `CommercialState = Trial` and `TrialEndsAtUtc`.
- **Pilot** is a cohort/rollout flag. It marks early customer accounts with `IsPilot = true` but must not directly grant access.

Phase 5C should add config-backed signup defaults, not hard-code pilot behavior into the domain:

```json
"SignupDefaults": {
  "IsPilot": true,
  "TrialDurationDays": 30,
  "MaxPilotAccounts": 1000
}
```

Initial pilot launch:

```text
purpose = AccountPurpose.Business
plan = AccountPlan.Trial
isPilot = SignupDefaults:IsPilot        // true initially
trialEndsAtUtc = nowUtc + 30 days
```

Later public signup can become config-only:

```json
"SignupDefaults": {
  "IsPilot": false,
  "TrialDurationDays": 14,
  "MaxPilotAccounts": null
}
```

Guardrails:

- `TrialDurationDays` must be positive.
- `MaxPilotAccounts` is optional. Null means no capacity gate.
- If `MaxPilotAccounts` is set and `IsPilot = true`, enforce the cap at both `/auth/start` and `/auth/exchange`.
- If `IsPilot = false`, ignore `MaxPilotAccounts`.
- Do not add `Pilot` to `AccountPlan`; pilot remains `AccountEntitlements.IsPilot`.
- Do not let `IsPilot` directly drive access. Access continues through lifecycle, commercial state, operating mode, and entitlements.

---

## Recommended Phase 5C contract

Add:

```text
POST /auth/start
```

Request:

```json
{
  "email": "owner@example.com",
  "businessName": "Acme Plumbing",
  "name": "Riley Owner",
  "timeZone": "America/Chicago"
}
```

Response:

```text
200 OK
```

Validation failures:

- missing/blank `email` -> `400 Validation.EmailRequired`
- missing/blank `businessName` -> `400 Validation.BusinessNameRequired`
- missing/blank `timeZone` -> `400 Validation.TimeZoneRequired`
- invalid IANA time zone -> `400 Validation.TimeZoneInvalid`
- `name` remains optional

Existing-member fallback:

- If exactly one active member exists for the email, `/auth/start` may issue an `ExistingMember` code exactly like `/auth/signin`.
- If unknown/new email, issue `NewAccount`.
- If ambiguous or ineligible, return neutral `200 OK` and send no code.

This preserves the reference route behavior while keeping `/auth/signin` as the cleaner existing-member endpoint.

---

## Required model changes

### `EntryContext`

Add at minimum:

```csharp
NewAccount = 1,
ExistingMember = 2,
```

Keep the reference numeric values. Do not add `InvitedUser`, `Recovery`, or `Ambiguous` in 5C unless implemented in exchange. `ExchangeAuthService` intentionally fails closed for unknown contexts.

### `AccountAuthCode`

Add snapshots needed for deferred account creation:

- `BusinessNameSnapshot`
- `NameSnapshot`
- `TimeZoneSnapshot`

Update `Create(...)` signature or add a named factory so existing-member code creation does not become noisy. New-account code creation must require `businessName` and `timeZone` snapshots before it can be exchanged.

Recommended validation:

- trim optional name to null if blank;
- trim business name and time zone;
- require business name/time zone when `entryContext == NewAccount`;
- require null `AccountId` and null `TargetAccountUserId` when `entryContext == NewAccount`;
- require non-null `AccountId` and `TargetAccountUserId` when `entryContext == ExistingMember`.

### EF mapping

Update `AccountAuthCodeConfiguration`:

- `BusinessNameSnapshot` max length should match `Account.BusinessName` mapping.
- `NameSnapshot` max length should match `User.Name` mapping.
- `TimeZoneSnapshot` max length should match `Account.TimeZone` mapping.
- Add an index to support invalidating prior new-account codes:

```text
(delivery_email_snapshot, entry_context)
```

Optional filtered index for active new-account codes can wait unless needed.

New migration:

```text
AccountAuthCodeNewAccountSnapshots
```

---

## Required service changes

### Application

Add `StartAuthService` in `Foundation.Application/Auth`.

Recommended dependencies:

- `IAuthCodePersistence`
- `IEmailSender`
- `IClock`
- `IOptions<MagicLinkSettings>`
- `IOptions<SignupDefaultsSettings>`
- `ILogger<StartAuthService>`

Recommended `StartAuthService.HandleAsync(email, businessName, name, timeZone, ct)`:

1. Require configured `App:PublicBaseUrl`.
2. Normalize email.
3. Validate business name and time zone upstream or in endpoint before service call.
4. Ask persistence to classify the start request:
   - exactly one active member -> existing member;
   - unknown email/no membership -> new account;
   - any ambiguous/ineligible/invited case -> neutral success/no code.
5. Create an `AccountAuthCode` with `EntryContext.ExistingMember` or `EntryContext.NewAccount`.
6. Commit issuance atomically with invalidation of prior active codes in the same class of target.
7. Send direct email best-effort after commit.
8. Return `Result.Success()` for all neutral public outcomes.

Email body can reuse `MagicLinkEmailTemplate`, but 5C should either:

- keep a generic subject/body for both sign-in and registration, or
- add an overload that accepts `businessName` and uses safe HTML encoding.

Do not include raw code or magic link in logs.

### Persistence

Extend `IAuthCodePersistence` or split into narrower interfaces. Minimum operations:

```text
ClassifyStartRequestAsync(normalizedEmail, ct)
CommitStartCodeAsync(code, ct)
CountActivePilotAccountsAsync(ct)
CommitNewAccountExchangeAsync(code, provisioningGraph, consumedAtUtc, ct)
```

`ClassifyStartRequestAsync` must not leak enumeration through the API. It can return an internal classification:

- `ExistingMember(accountId, accountUserId)`
- `NewAccount`
- `NeutralNoCode`

`CommitStartCodeAsync`:

- ExistingMember: same invalidation rule as `CommitSignInCodeAsync` by `TargetAccountUserId`.
- NewAccount: invalidate prior active `NewAccount` codes by normalized `DeliveryEmailSnapshot`.
- Add the new code and save in one transaction.

`CommitNewAccountExchangeAsync`:

- conditional consume: `Id == code.Id && ConsumedAtUtc == null && InvalidatedAtUtc == null`;
- add `User`, `Account`, `AccountUser`, and `AccountEntitlements`;
- save and commit in one transaction;
- return a duplicate-email/conflict result if the email was claimed between `/start` and `/exchange`.

The persistence implementation must catch unique constraint collisions for email/account-user conflicts and return a domain/application conflict rather than throwing through as a 500.

### Exchange

Update `ExchangeAuthService`:

- route `EntryContext.NewAccount` to a new handler;
- keep existing-member behavior unchanged;
- continue returning `entryContext` on invalid/expired/consumed/invalidated code failures;
- update endpoint `ToEntryContextString` with `"new_account"`;
- create the session outside the transaction, same as 5B.

New-account exchange flow:

1. Guard code has `EntryContext.NewAccount`.
2. Guard snapshots are present (`DeliveryEmailSnapshot`, `BusinessNameSnapshot`, `TimeZoneSnapshot`).
3. Re-check no user/account-user exists for the email.
4. If `SignupDefaults.IsPilot = true` and `MaxPilotAccounts` is set, re-check pilot capacity before consuming the code.
5. Build graph with `AccountProvisioningService.CreateVerified(...)`.
6. Commit code consumption plus graph creation in one transaction.
7. Create session outside the transaction.
8. On session creation failure, log error with `AccountId`, `AccountUserId`, and `AccountAuthCodeId`; return `Account.SessionCreationFailed`.

Provisioning defaults for 5C:

- `purpose = AccountPurpose.Business`
- `plan = AccountPlan.Trial`
- `isPilot = SignupDefaults.IsPilot`
- `trialEndsAtUtc = nowUtc.AddDays(SignupDefaults.TrialDurationDays)`

---

## Keep public-intake decision

Keep public-intake link creation is deferred from 5C.

Rationale:

- Keeps 5C focused on auth/account provisioning.
- Avoids cross-boundary orchestration in the auth service.
- Avoids deciding how to surface a raw public intake token during exchange.
- Lets a later Keep setup flow own `KeepPublicIntakeLink` creation when the app is ready to show/configure it.

Phase 5C must not add `PublicSlugSnapshot`, create `KeepPublicIntakeLink`, generate Keep public-intake tokens, or return public intake link data from `/auth/exchange`.

---

## Pilot capacity behavior

Reference checks pilot capacity at `/auth/start` and again at `/auth/exchange`.

Phase 5C should implement the foundation now through `SignupDefaults.MaxPilotAccounts`, while allowing the cap to be effectively non-blocking during early pilot by setting a large number.

Behavior:

- If `SignupDefaults.IsPilot = false`, no pilot capacity gate runs.
- If `SignupDefaults.IsPilot = true` and `MaxPilotAccounts = null`, no pilot capacity gate runs.
- If `SignupDefaults.IsPilot = true` and `MaxPilotAccounts` has a value, count active pilot accounts and return `Account.PilotFull` when the cap is reached.
- Check capacity at `/auth/start` before issuing a new-account code.
- Re-check capacity at `/auth/exchange` before consuming the code.
- A pilot-full rejection before consume leaves the code unconsumed.

Add `AccountErrors.PilotFull` and ensure `ErrorHttpMapper` maps it to `409 Conflict`.

---

## Logging policy for 5C

Log:

- email delivery failure after code commit: warning with `AccountAuthCodeId`;
- session creation failure after successful new-account commit: error with `AccountId`, `AccountUserId`, `AccountAuthCodeId`;
- unexpected unique constraint collision after passing pre-check: warning with `AccountAuthCodeId` and collision type, no email.

Do not log:

- raw auth code;
- raw session token;
- magic-link URL;
- token hash;
- email address;
- business name or operator name until a PII logging policy exists.

Do not log expected public auth outcomes as errors:

- unknown/ineligible/ambiguous email;
- duplicate `/start` superseding prior code;
- expired/consumed/invalidated exchange;
- concurrent exchange loser.

---

## API/frontend compatibility notes

Reference web route `/api/onboarding/start` forwards:

```json
{ "email": "...", "businessName": "...", "name": "..." }
```

Foundation 5C should require:

```json
{ "email": "...", "businessName": "...", "name": "...", "timeZone": "America/Chicago" }
```

The frontend can use `Intl.DateTimeFormat().resolvedOptions().timeZone`.

Reference exchange route already understands:

- `404` / `422` as invalid link;
- `409 Account.PilotFull` as pilot full;
- `409 Account.NewAccountEmailAlreadyRegistered` as account already exists;
- `503 Account.SessionCreationFailed` as directed recovery via sign-in.

Foundation should add a duplicate-email conflict error if it needs frontend distinction. Suggested error:

```text
Account.EmailAlreadyInUse
```

`ErrorHttpMapper` already maps `.EmailAlreadyInUse` to `409 Conflict`, so this name avoids a mapper change.

---

## Files expected to change

**Foundation.Core**

- `Entities/Accounts/Enums/EntryContext.cs`
- `Entities/Accounts/AccountAuthCode.cs`
- `Entities/Accounts/Errors/AccountErrors.cs`

**Foundation.Application**

- `Auth/StartAuthService.cs` new
- `Auth/SignupDefaultsSettings.cs` new
- `Auth/IAuthCodePersistence.cs`
- `Auth/ExchangeAuthService.cs`
- `Auth/MagicLinkEmailTemplate.cs` maybe

**Foundation.Infrastructure**

- `Auth/EfAuthCodePersistence.cs`
- `Persistence/Configurations/AccountAuthCodeConfiguration.cs`
- new migration for auth code snapshots

**Api**

- `Auth/AuthEndpoints.cs` add `POST /auth/start`, body record, validation, `new_account` entry context
- `Program.cs` bind `SignupDefaults`, register `StartAuthService`
- `Helpers/ErrorHttpMapper.cs` map `Account.PilotFull` to `409 Conflict`
- `appsettings.json` add `SignupDefaults`

**Tests**

- `Api/AuthMagicLinkTests.cs` can stay focused on 5B
- Add `Api/AuthStartTests.cs` for 5C
- Add unit tests for `AccountAuthCode` snapshot/factory validation
- Add persistence/exchange tests for new-account atomicity and races

---

## Test plan

Minimum integration tests:

1. `/auth/start` missing email -> 400.
2. `/auth/start` missing business name -> 400.
3. `/auth/start` missing time zone -> 400.
4. `/auth/start` invalid time zone -> 400.
5. `/auth/start` unknown email -> 200 and sends one email.
6. New-account code exchange -> 200, sets cookie for browser, creates `User`, `Account`, owner `AccountUser`, and `AccountEntitlements`.
7. New-account exchange allows `/auth/me`.
8. New-account exchange creates Trial entitlements with confirmed defaults.
9. New-account code second `/auth/start` invalidates prior code.
10. Old new-account code exchange -> 422 with `entryContext = "new_account"`.
11. Existing-member `/auth/start` -> 200 and sends existing-member code.
12. Existing-member `/auth/start` exchange reuses 5B session behavior.
13. Existing `User` but no active membership -> 200, no email, no duplicate account.
14. Invited-only membership -> 200, no email, no duplicate account.
15. Concurrent exchange of same new-account code -> exactly one success; loser gets already-consumed/invalid response; one account graph exists.
16. Session creation failure after new-account commit -> 503 `Account.SessionCreationFailed`; account graph remains committed; safe error log exists.
17. Duplicate email claimed between `/start` and `/exchange` -> `409 Account.EmailAlreadyInUse`, and the code is not consumed unless the graph commit succeeds.
18. `SignupDefaults.IsPilot = true` creates `IsPilot = true` trial accounts.
19. `SignupDefaults.TrialDurationDays = 30` sets a 30-day trial; a 14-day config value sets a 14-day trial.
20. Pilot cap reached at `/auth/start` -> `409 Account.PilotFull`, no code issued.
21. Pilot cap reached at `/auth/exchange` -> `409 Account.PilotFull`, code remains unconsumed.

Keep public-intake link tests are not part of Phase 5C. Link creation is deferred to a later Keep setup phase.

Verification gate:

```text
dotnet build
dotnet test
```

Architecture tests must remain green: no Foundation.Application dependency on Infrastructure or Keep.

---

## Confirmed decisions

| # | Decision | Confirmed |
|---|----------|----------------|
| D1 | `/auth/start` requires `timeZone`? | Yes, require IANA time zone. Store instants in UTC; use IANA for business-local interpretation. |
| D2 | New-account defaults | Config-backed `SignupDefaults`: initially Business account, Trial plan, `IsPilot = true`, 30-day trial. Later switch to `IsPilot = false`, 14-day trial by config. |
| D3 | Pilot gate in 5C? | Yes, via optional `SignupDefaults.MaxPilotAccounts`; use a large value during early pilot if the cap should be effectively non-blocking. |
| D4 | Keep public-intake link creation in 5C? | Defer. Later Keep setup owns `KeepPublicIntakeLink` creation. |
| D5 | Existing `User`/invited membership behavior | Neutral 200, no code, no duplicate account. |
| D6 | Existing-member fallback through `/auth/start` | Yes, issue existing-member code for exactly one active member. |
| D7 | New-account duplicate discovered at exchange | Return `409 Account.EmailAlreadyInUse`; do not create a duplicate account. |
| D8 | Email delivery | Same as 5B: direct `IEmailSender`, best-effort, neutral public response. |
| D9 | Logging | Adopt 5C logging policy above before coding starts. |

---

## Claude Code implementation order

1. Add `SignupDefaultsSettings`, bind it in `Program.cs`, and add `appsettings.json` defaults.
2. Add `EntryContext.NewAccount`, auth-code snapshots, factory validation, EF mapping, and migration.
3. Extend auth persistence for start classification, start-code commit, pilot count, and new-account exchange commit.
4. Add `StartAuthService` and `POST /auth/start` with validation including IANA time zone.
5. Add `NewAccount` branch in `ExchangeAuthService`, using `AccountProvisioningService` plus `SignupDefaults`.
6. Add `Account.EmailAlreadyInUse` and `Account.PilotFull`; update `ErrorHttpMapper` only for `PilotFull`.
7. Add focused unit tests for `AccountAuthCode` and settings validation.
8. Add integration tests for `/auth/start`, new-account exchange, pilot defaults/cap, duplicate email race, and concurrent exchange.
9. Run `dotnet build` and `dotnet test`; architecture tests must remain green.
