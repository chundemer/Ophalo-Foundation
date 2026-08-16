# Build Log 123 — Paired Nudges: Implementation-Contract Preflight

**Status:** Complete — decisions locked; no production code written
**Date:** 2026-08-16
**Scope:** Lock the Owner/Admin rule-configuration API/UI contract, the post-reload field
nudge-read contract, and the bounded implementation-session sequence required by Build Log 122
before any Paired Nudges production code is authorized.
**Related:** Build Log 122 (product model), Build Log 119 (Quick scope-action precedent — contrast,
not copy), ADR-479, ADR-483, ADR-485, ADR-486

## Locked decisions

### 1. Configuration mutation shape: per-rule CRUD

`ScopeNudgeRule` is catalog-attached configuration, potentially one rule for each catalog item or
assembly across an account. It uses per-rule Create, Update, and Delete; it does not use a
QuickScopeAction-style whole-set replacement.

Each Create or Update atomically owns one rule's ordered 1–3 suggestion list. There is no
per-suggestion mutation endpoint. A trigger is immutable after Create: Update replaces only the
suggestion list; changing a trigger requires Delete then Create.

### 2. Rule count and uniqueness

There is no account-wide rule cap. Enforce one rule per trigger type per account with two partial
unique indexes:

- `(AccountId, TriggerCatalogItemId)` where `TriggerCatalogItemId` is not null;
- `(AccountId, TriggerOfferingAssemblyId)` where `TriggerOfferingAssemblyId` is not null.

A conflicting Create returns `ScopeNudgeRuleErrors.DuplicateTrigger`; it never silently overwrites
the existing rule. No optimistic-concurrency version is added: this is low-contention Owner/Admin
configuration, not a field Draft artifact.

## Owner/Admin configuration contract

Route prefix: `/keep/pricebook/scope-nudge-rules`. Every route requires authentication, normal
account access, Price Book entitlement, and `PriceBookCatalogManage` authority.

- `GET /keep/pricebook/scope-nudge-rules` lists all account rules, including rules with a now
  inactive/ineligible trigger or suggestion. Such targets are marked for Owner/Admin repair rather
  than omitted.
- `POST /keep/pricebook/scope-nudge-rules` creates a rule with exactly one trigger target and 1–3
  ordered suggestions. Every target must exist at write time, but need not currently be active or
  eligible. Reject duplicate triggers and invalid suggestion sets.
- `PUT /keep/pricebook/scope-nudge-rules/{ruleId}` atomically replaces the existing rule's full
  suggestion list only; it accepts no trigger fields.
- `DELETE /keep/pricebook/scope-nudge-rules/{ruleId}` removes the rule and its suggestion rows.

Configuration responses are price-free:

```text
ScopeNudgeRuleConfigRow(
  Id, TriggerCatalogItemId?, TriggerOfferingAssemblyId?,
  TriggerDisplayName, TriggerIsEligible,
  Suggestions: IReadOnlyList<ScopeNudgeSuggestionConfigRow>)

ScopeNudgeSuggestionConfigRow(
  Id, Order, SuggestedCatalogItemId?, SuggestedOfferingAssemblyId?,
  TargetDisplayName, IsEligible)
```

Eligibility means an Active `CatalogItem` or operationally eligible `OfferingAssembly`; neither is
limited to Common Items.

## Field nudge-read contract

`GET /keep/pricebook/proposed-scopes/{proposedScopeId}/nudge-suggestions` requires exactly one
query parameter: `triggerCatalogItemId` or `triggerOfferingAssemblyId`. Missing, duplicate, or
combined trigger parameters return `400`.

It is a scope-bound read. Its server ordering is: authenticate; account access; Price Book
entitlement; `RequestsOperate` and `ScopeCapture`; load the proposed scope; verify request
visibility; then evaluate the supplied direct trigger. It takes no request body or concurrency
token. It does not change Draft state and therefore does not apply a terminal-state mutation gate.

The service:

1. finds the account rule for the one direct trigger;
2. verifies the current trigger target is active/eligible, returning an empty result if not;
3. filters ordered suggestions that are inactive/ineligible or already represented by an active
   Draft line with the same `CatalogItemId` or `OfferingAssemblyId`; and
4. returns an empty result when no rule or no surviving suggestion exists.

The response is price-free:

```text
ScopeNudgeFieldResult(
  RuleId, TriggerCatalogItemId?, TriggerOfferingAssemblyId?,
  Suggestions: IReadOnlyList<ScopeNudgeSuggestionFieldRow>)

ScopeNudgeSuggestionFieldRow(
  Id, Order, CatalogItemId?, OfferingAssemblyId?, DisplayName, TargetKind)
```

After a direct catalog add or assembly expansion succeeds and its authoritative Draft reload
completes, `useProposedScopeCapture` calls this endpoint once unless that rule was already retired
for the open composer session. Accepting a chip invokes the existing field-select or expand-assembly
mutation and retires the rule; dismissing retires it without a write. An empty result does not retire
anything.

## Persistence lifecycle

Use `DeleteBehavior.Restrict` for nudge references to `CatalogItem` and `OfferingAssembly`, matching
the existing references to those lifecycle-managed entities. Neither target currently exposes a
hard-delete path. Reconfirm this relationship posture when reviewing the migration.

## Bounded implementation sequence

1. **Rule domain, persistence, and migration.** Add rule/suggestion entities, validators, errors,
   persistence, configurations, partial unique indexes, migration, and domain/persistence tests.
   No API surface.
2. **Owner/Admin configuration API.** Add the per-rule CRUD service/endpoints and integration tests.
3. **Field nudge-read API.** Add the scope-bound read service/endpoint and integration tests for
   eligibility, Draft dedupe, and request visibility.
4. **Owner/Admin settings UI.** Add the Price Book rule-management screen using Session 2's API.
5. **Composer hook and chips.** Add session-only retired-rule state, post-reload field reads,
   price-blind accept/dismiss chips, and all Build Log 122 manual acceptance scenarios.

Each is an independent, file-gated session. Sessions 1–3 are backend-only; Session 4 depends on
Session 2, and Session 5 depends on Session 3. Do not combine sessions to save a boundary.

## Explicitly out of scope

No production code, migration, endpoint, or UI is authorized by this preflight. Session 1 above
still requires a mechanical file-level preflight and approval before its first edit.
