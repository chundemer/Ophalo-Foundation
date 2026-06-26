# Build Log 064 — Session 9 Account Classification And Delivery Eligibility

**Started:** 2026-06-26  
**Status:** Complete  
**Next free ADR before this log:** ADR-363

Session 9 replaces the old pilot boolean with durable account classification and adds the safety
gates needed before real APNs/FCM delivery, repeatable demo tooling, and pilot reporting.

---

## Scope

Primary goal: make account classification a single source of truth.

In scope:

- replace `AccountEntitlements.IsPilot` with account classification;
- add classification values `Production`, `Pilot`, `Demo`, and `InternalTest`;
- update signup defaults and pilot-cap gating to use classification;
- migrate existing persisted `IsPilot` data into the new classification column;
- add the real-delivery eligibility gate so Demo/InternalTest cannot receive production pushes;
- update focused tests and documentation.

Out of scope:

- real APNs/FCM provider implementation;
- demo scenario packs and reset UI;
- admin/internal account-classification management UI;
- reporting UI;
- billing integration;
- run-as/impersonation.

---

## Alignment Discussion Outcomes

### Product Intent

Classification describes operational/reporting/safety posture. It is separate from commercial
lifecycle. A real account can be `Pilot` while commercially `Trial`, then later remain `Pilot` while
paid, or graduate to `Production` while still using the normal commercial state machine.

### Model Location

Classification replaces the existing `AccountEntitlements.IsPilot` flag on
`AccountEntitlements`. Keep the classification on `AccountEntitlements` for this migration because
that is where the current cohort flag lives and where commercial/operating posture is already read
for access and provisioning. Do not add a parallel classification on `Account`.

### Existing Data Migration

Migration intent:

- `IsPilot = true` -> `Classification = Pilot`;
- `IsPilot = false` and real/non-internal account -> `Classification = Production`;
- internal-purpose accounts, if present, -> `Classification = InternalTest`.

The internal-purpose mapping is an implementation clarification of the earlier pilot-readiness
decision: existing real accounts become Production, but internal OpHalo accounts should not be
classified as real customer Production accounts.

### Signup Defaults

`SignupDefaultsSettings.IsPilot` is replaced by a classification setting. The preferred config key
is:

```json
"SignupDefaults": {
  "Classification": "Pilot",
  "TrialDurationDays": 30,
  "MaxPilotAccounts": null
}
```

Self-signup/new-account auth may create only `Pilot` or `Production` accounts. `Demo` and
`InternalTest` accounts require explicit future admin/internal creation flows and are not allowed
through public signup.

`MaxPilotAccounts` remains valid and applies only when the signup classification is `Pilot`.
Pilot-cap counting uses classification `Pilot` and remains conservative by counting all Pilot
accounts rather than filtering by commercial state.

### Delivery Eligibility

Real APNs/FCM delivery remains out of Session 9, but Session 9 must install the production-delivery
eligibility gate required by ADR-353. The gate should suppress Demo/InternalTest before any real
provider can send. The cleanest first implementation point is device-delivery lookup: active devices
are returned for delivery only when the account classification allows production push delivery.

No-op delivery may also respect this gate; that is acceptable and safer because it tests the same
path that real delivery will later use.

---

## Locked Decisions

### ADR-363 — Account classification replaces `AccountEntitlements.IsPilot`

`AccountEntitlements.IsPilot` is removed as a source of truth and replaced by an
`AccountClassification` enum on `AccountEntitlements`.

Values:

- `Production = 1`
- `Pilot = 2`
- `Demo = 3`
- `InternalTest = 4`

Classification is operational/reporting/safety posture, not commercial lifecycle. Commercial state
continues to live in `AccountCommercialState`.

EF stores classification as a string column. Migration converts existing data as:

- `IsPilot = true` -> `Pilot`;
- `IsPilot = false` for real accounts -> `Production`;
- internal-purpose accounts -> `InternalTest`.

After migration, the old `is_pilot` column is removed.

### ADR-364 — Signup defaults set classification, not a pilot boolean

`SignupDefaultsSettings` uses classification instead of `IsPilot`.

The config value is `SignupDefaults:Classification`, with current pilot default `Pilot`.
`TrialDurationDays` remains separate. `MaxPilotAccounts` applies only when classification is
`Pilot`; null still means no cap.

Public self-signup/new-account exchange may provision only `Pilot` or `Production` accounts.
`Demo` and `InternalTest` are explicit future admin/internal creation modes, not signup defaults.

### ADR-365 — Pilot cap counts classification `Pilot`

Pilot capacity checks at `/auth/start` and `/auth/exchange` continue to run only when the signup
classification is `Pilot` and `MaxPilotAccounts` has a value.

The count source changes from `IsPilot = true` to `Classification = Pilot`. The conservative V1
behavior remains: cancelled/expired pilot accounts are not excluded from the cap unless a later
decision changes that rule.

### ADR-366 — Demo/InternalTest suppress production push delivery

Before any real APNs/FCM adapter is enabled, production push delivery must be gated by account
classification.

`Production` and `Pilot` are delivery-eligible. `Demo` and `InternalTest` are not delivery-eligible
by default. Session 9 installs the gate in the device-delivery path so later real APNs/FCM
delivery cannot accidentally send to Demo/InternalTest accounts.

This does not implement real APNs/FCM, a notification ledger, delivery analytics, or notification
preferences.

---

## Gate Exception — S9a

**Approved by Christian, 2026-06-26.** S9a exceeds the normal 8-production-file / 12-total-file
batch gate. Rationale: `IsPilot → AccountClassification` is a single coherent model replacement,
not scope creep. Splitting it leaves a temporary boolean compatibility layer that S9 exists to
remove. The 35+ integration-test file edits are one mechanical rename class (named arg `isPilot:`
→ `classification:`), not independent behavioral surface. Constraint: no long-lived overloads;
any temporary adapter must be gone before final diff.

---

## Implementation Slices

### S9a — Classification model, provisioning, and migration ✓

Goal: replace the boolean cohort flag with classification in the core model and auth provisioning.

Read:

- this build log through S9a;
- `AccountEntitlements`, `AccountEntitlementsConfiguration`, and current migration snapshot;
- `SignupDefaultsSettings`, `StartAuthService`, `ExchangeAuthService`, `EfAuthCodePersistence`;
- `AccountProvisioningService` and related account unit tests.

Implement:

- `AccountClassification` enum with explicit numeric values.
- `AccountEntitlements.Classification`.
- Remove `IsPilot` as model/API/test source of truth.
- Update factories/provisioning to accept classification.
- Reject Demo/InternalTest from public signup/default provisioning.
- Update `SignupDefaultsSettings` and `appsettings.json`.
- Rename/update pilot-cap persistence method to count classification `Pilot`.
- Generate and inspect EF migration:
  - add classification;
  - migrate data from `is_pilot`;
  - map internal-purpose accounts to `InternalTest`;
  - drop `is_pilot`;
  - update model snapshot.
- Focused unit/integration tests for factories, provisioning, signup defaults, and pilot cap.

Do not implement:

- admin classification management;
- demo reset;
- real APNs/FCM.

Completion gate:

```text
focused account/auth tests green
migration inspected
dotnet build
```

Result:

- `AccountClassification` added with `Production`, `Pilot`, `Demo`, and `InternalTest`.
- `AccountEntitlements.IsPilot` removed as model/test source of truth.
- Signup defaults, provisioning, and pilot-cap checks now use classification.
- EF migration `20260626111822_AccountClassification` adds `classification`, maps existing data,
  maps internal-plan rows to `InternalTest`, and drops `is_pilot`.

### S9b — Delivery eligibility gate and docs ledger ✓

Goal: add the Demo/InternalTest production-delivery suppression gate and reconcile docs.

Read:

- this build log through S9b;
- `IAccountUserDevicePersistence.FindActiveDevicesForDeliveryAsync`;
- `EfAccountUserDevicePersistence`;
- S8 notification docs and tests.

Implement:

- device-delivery lookup returns active devices only for `Production`/`Pilot` accounts;
- focused tests proving Demo/InternalTest accounts return no delivery devices;
- update `DEF-079` and Session 8/S9 docs as needed;
- update decision index statuses when implementation is complete.

Do not implement:

- real provider adapter;
- notification preferences;
- demo reset.

Completion gate:

```text
focused device delivery eligibility tests green
docs reconciled
dotnet build
```

Result:

- `FindActiveDevicesForDeliveryAsync` now returns active devices only when the account
  classification is `Production` or `Pilot`.
- `Demo` and `InternalTest` accounts return no delivery devices, so the existing notifier path and
  any later real APNs/FCM adapter inherit the suppression gate before send.
- Focused integration coverage proves all four classification cases.
- `DEF-079`, notification deferred ledgers, Session 9 build log, session log, and ADR index are
  reconciled.

Verification:

```text
dotnet test tests/OpHalo.IntegrationTests/OpHalo.IntegrationTests.csproj --filter FullyQualifiedName~AccountUserDeviceApiTests
Passed: 30, Failed: 0

dotnet build
Succeeded with 2 NU1900 package-vulnerability feed warnings from nuget.org lookup; no compile errors.
```

---

## Open Questions

None for Session 9 implementation.

Future questions remain deferred:

- how founders/admins explicitly create Demo/InternalTest accounts;
- demo scenario pack/reset UX;
- role switching/run-as for demos;
- whether the pilot cap should later exclude cancelled/expired pilot accounts.
