# Session Log — OpHalo Foundation

**Last updated:** 2026-06-14
**Next session tier:** Tier 2 — Implementation · **Pre-work complete** (Phase 4a target confirmed)
**Branch:** `main` (no remote yet)

> Next session **implements Phase 4a** (Account / User / AccountUser + lifecycle +
> access policy). The target below is fully confirmed — read this log, confirm the
> few signatures with targeted reads, then build. Do **not** re-run discovery.

---

## Where we are

Phases 0–3 complete and committed. Phase 4 **discovery done** this session; the
implementation target and its decisions (ADR-015…020) are locked. No Phase 4 code
written yet. Build clean, **33 tests passing**.

| Phase | Status | Notes |
|-------|--------|-------|
| 1 — Skeleton + architecture tests | ✅ done | `00227b0` |
| 2 — Legacy exclusion (doc) | ✅ done | `2fce382` |
| 3 — SharedKernel + abstraction cleanup | ✅ done | `2fce382` |
| 4 — Account/User/lifecycle/entitlements/permissions | 🔵 discovery done; 4a target locked | this session |

## Phase 4 decisions locked this session (ADR-015…020)

- **ADR-015 Roles:** `AccountUserRole` = Owner/Admin/Operator/Viewer. Legacy maps
  Owner→Owner, Admin→Admin, Technician→Operator, Member→Viewer.
- **ADR-016 Membership:** `MembershipStatus` = Invited/Active/Suspended/Removed
  (PendingInvite→Invited; Suspended is new, per-member, distinct from account-lifecycle Suspended).
- **ADR-017 AccountUser membership-only:** push subscription, `HasSeenMemberWelcome`,
  `HasDismissedInstallPrompt` excluded → future KeepUserSettings / AccountUserDevice / NotificationDestination.
- **ADR-018 Account trimmed:** identity + lifecycle + purpose + core profile only.
  Quotas/credits → entitlements/usage; commercial/plan/billing → AccountEntitlements;
  public slug + intake token → Keep public-intake; notification policy → AccountNotificationPolicy.
- **ADR-019 Primary owner:** `Account.PrimaryOwnerAccountUserId` (FK), not an
  `AccountUser.IsPrimaryOwner` flag. Ownership is separate from role.
- **ADR-020 Core profile:** keep `TimeZone` on Account; defer `ServiceCategory` to a Keep/business-profile satellite.

## Next — Phase 4a implementation target (CONFIRMED)

Build in the target repo (`/Users/christian/saas/ophalo-foundation`):

**`OpHalo.Foundation.Core`**
- `Account` — trimmed: `Id`, `BusinessName`, `Purpose`, `LifecycleState`, `TimeZone`,
  `PrimaryOwnerAccountUserId`, created/updated timestamps. Methods: `Suspend`/`Close`/
  `Reactivate` (preserve reference semantics), trimmed `CreateVerified` factory,
  `RecordLogin`, trimmed profile update. Drop quotas/credits/slug/intake-token.
- `User` — port near-verbatim (email, name, phone, verified flags, last login; `CreateVerified`).
- `AccountUser` — membership-only: `AccountId`, `UserId?`, `Role`, `MembershipStatus`,
  invite lifecycle (`InviteTokenHash`, `InviteExpiresAtUtc`), `ActivatedAtUtc`, timestamps.
  Methods `Activate`/`RefreshInvite`/`Remove` (+ a `Suspend`/membership-suspend for the new status).
  Factories `CreateOwner` (Role=Owner now, not legacy Admin), `CreatePendingInvite`.
  **Drop** push-subscription + welcome + install-prompt fields/methods.
- Enums: `AccountUserRole` (Owner/Admin/Operator/Viewer), `MembershipStatus`
  (Invited/Active/Suspended/Removed), `AccountLifecycleState` (Active/Suspended/Closed),
  `AccountPurpose` (Business/Internal). Also port `AccountCommercialState` +
  `AccountOperatingMode` enums (the access policy context needs them; the
  AccountEntitlements **entity** stays deferred).
- Errors: trimmed `AccountErrors` (lifecycle/access/commercial transitions in scope) +
  `AccountUserErrors` (membership transitions; drop push errors).
- Support: port `BaseEntity` and `EmailNormalizer`. **`EmailNormalizer` goes in Foundation,
  not SharedKernel** (§8 — no email concepts in SharedKernel).

**`OpHalo.Foundation.Application`**
- Port `AccountAccessPolicy` + `IAccountAccessPolicy`, `AccountAccessContext`,
  `AccountAccessDecision`, `AccountAccessPosture`, `AccountAccessReason` into
  `Accounts/Access/` — **verbatim logic**, new namespaces, `SharedKernel.Results`.
  No redesign: it already matches plan §4.10.

**Tests (`OpHalo.UnitTests`)**
- Account lifecycle transition tests (Suspend/Close/Reactivate guards).
- AccountUser membership transition tests (Activate/RefreshInvite/Remove/Suspend).
- `AccountAccessPolicy` posture matrix (the exit-gate "account posture" tests):
  lifecycle blocks, Internal bypass, Trial/PastDue-grace/Expired/Canceled, OffSeason read-only.

**Deferred to later Phase 4 sessions (NOT 4a):** permission keys + role→permission map,
feature-key entitlements (§4.11), AccountEntitlements entity, invitations flow,
sessions/devices (Phase 5).

**Exit gate (plan §Phase 4, partial — posture/lifecycle slice):** unit tests for account
posture green; Foundation has no Keep references; build + arch tests stay green.

## Reference source map (already read — re-verify signatures only)

- `_reference/src/OpHalo.Core/Entities/Accounts/Account.cs` (bloated — trim per ADR-018)
- `_reference/src/OpHalo.Core/Entities/Users/User.cs` (clean)
- `_reference/src/OpHalo.Core/Entities/Accounts/AccountUser.cs` (strip product fields per ADR-017)
- `_reference/src/OpHalo.Core/Entities/Accounts/Enums/*` and `Errors/*`
- `_reference/src/OpHalo.Application/Accounts/Access/*` (port verbatim)
- Still to locate at build time: `BaseEntity` + `EmailNormalizer` source paths under
  `_reference/src/OpHalo.Core/Entities/Shared/` and `_reference/src/OpHalo.Shared/Helpers/`.

## Blockers / watch-outs

- Never glob through `_reference/**/bin` (recursive nesting). Read specific source paths.
- Legacy `decision-index`/`decisions/**`/`coding-rules` remain **pending validation** — do not load.
- Real repo is `/Users/christian/saas/ophalo-foundation`; the launch cwd `Ophalo/` is empty.
- No GitHub remote yet. When added, repo must be named `ophalo-foundation`.
