# ADR-462 — Account Feature Access Resolver And Capability Package Enrollment

**Status:** Locked  
**Date:** 2026-07-30  
**Related:** ADR-009, build-log/107, build-log/108

## Decision

`FeatureAccessPolicy` stays pure and plan-based. It performs no database I/O and answers only "what
does this plan include," per the existing `AccountPlan` (ADR-009) mapping.

Account-aware entitlement — plan **or** an active capability-package enrollment — is a separate,
explicit fan-out that callers use instead of the raw policy:

- `AccountFeatureAccessResolver` / `AccountFeatureAccessContext` combine the pure plan answer with
  any active enrollment for the account, and are the only account-aware entry point. Callers that
  need account-scoped access must go through the resolver, never the plan policy alone.
- Per-account entitlement for `keep.price_book_quotes_materials` is granted via a new
  `AccountCapabilityPackageEnrollment` row:

  ```text
  AccountCapabilityPackageEnrollment
  - AccountId
  - FeatureKey            (Core-owned allow-list, not an arbitrary string)
  - Status
  - EnabledAt / DisabledAt
  - changed-by internal user (actor attribution)
  - concurrency token
  - guarded Enroll / Disable / Reenable methods
  - unique on (AccountId, FeatureKey)
  ```

  This is a mutable state-machine row, not an event log, and there is no package-to-feature-set
  expansion table yet — one row grants one feature key to one account. The `(AccountId, FeatureKey)`
  uniqueness rule and concurrency token mean `Enroll`/`Disable`/`Reenable` always transition the same
  logical row rather than risking a second, conflicting row for the same account/feature pair, which
  would make access resolution ambiguous.

- `internal.entitlements.manage` (existing) is the correct internal-only authority for
  enroll/disable/reenable. No new permission key is introduced.

## Test Obligations

`AccountFeatureAccessResolver` must be covered for: plan-only access, enrollment-only access,
disabled-enrollment access, unknown feature key, and blocked-account access.

## Rationale

Keeping `FeatureAccessPolicy` pure preserves its current fast, deterministic behavior for every
existing plan-based feature check and avoids adding I/O to a policy other call sites already treat
as synchronous and side-effect-free. Pushing account-awareness into an explicit resolver makes the
caller fan-out visible in the code rather than hidden inside the policy, and gives the
capability-package model (pilot-style entitlement independent of plan) a narrow, auditable home
instead of overloading `AccountPlan` or bolting a boolean override onto the plan policy itself.
