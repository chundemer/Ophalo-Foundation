# ADR-480 — Proposed-Scope Capture Permission and Three-Gate Authority

**Status:** Locked  
**Date:** 2026-08-10  
**Related:** ADR-462; ADR-473; Build Logs 108, 117

## Decision

A new Foundation permission key, `keep.pricebook.scope.capture`
(`PermissionKeys.Keep.PriceBookScopeCapture`), is added to `RolePermissions.OperatorBase`. Through
this codebase's existing explicit role-composition hierarchy (`AdminBase = [.. OperatorBase, ...]`,
`OwnerBase = [.. AdminBase, ...]`), Admin and Owner hold it automatically — no separate grant is
needed for either. It is deliberately not added alongside `PriceBookCatalogManage` in `AdminBase`:
capturing a proposed scope (field or desk) is not catalog maintenance, and an Operator must be able
to do it without also gaining catalog-management authority.

Every `ProposedScope`-mutating action (create scope, add/edit/remove line, submit to office)
requires all three of the following, evaluated as independent gates:

1. **`PermissionKeys.Keep.RequestsOperate`** — the existing general "this role may operate on Keep
   requests" authority, evaluated the same way every other `RequestsOperate` call site does
   (role-level via `IUserAccessPolicy.IsPermitted`; not a per-request assignment/participation
   check — no existing `RequestsOperate` caller does that either).
2. **Price Book account-level entitlement** — `AccountFeatureAccessResolver.IsEnabledAsync` against
   `CapabilityPackageFeatureKeys.PriceBookQuotesMaterials` (ADR-462), the same entitlement check
   every other Price Book service performs.
3. **`PermissionKeys.Keep.PriceBookScopeCapture`** — the new key.

This composes with, rather than replaces, the standard ADR-462 gate order (account access gate →
entitlement resolver → user permission) already used by `OfferingAssemblyApiService` and
`CatalogItemApiService`: the request-domain permission check and the price-book-domain permission
check are both evaluated after the same account-access and entitlement gates, not folded into one
combined check.

## Rationale

`ProposedScope` sits at the intersection of two authority boundaries that both already exist
independently in this codebase and must both keep applying:

- It is work performed against a specific `KeepRequest` — the existing request-authority boundary
  (`RequestsOperate`) governs every other request mutation (internal notes, status changes,
  handoffs) and must govern this one too.
- It is an entitled Price Book capability — an account that has not enrolled in
  `PriceBookQuotesMaterials` must not expose scope capture at all, regardless of the acting user's
  role, exactly as no other Price Book surface is reachable without that entitlement.

Collapsing these into a single new permission key would weaken one boundary or the other: a
Price-Book-only key would not respect `RequestsOperate` (an Operator removed from request duties
but still holding a stale Price Book role grant could still capture scope), while requiring
`PriceBookCatalogManage` would incorrectly restrict scope capture to Admin/Owner, when ADR-473
explicitly assigns scope capture to technicians. Three explicit, independently testable gates
preserve both boundaries without inventing a new authorization mechanism.

## Consequences

- Session 3.3 owns registering `PermissionKeys.Keep.PriceBookScopeCapture`, adding it to
  `RolePermissions.OperatorBase`, and wiring the three-gate `AuthorizeAsync` for the
  `ProposedScope` create/edit/submit mutation family.
- A future request-scoped office-review action (Session 3.5) needs its own permission decision;
  this ADR does not pre-decide it.
- `Viewer` remains excluded — no key is added to `ViewerBase`, consistent with the existing
  read-mostly boundary.
