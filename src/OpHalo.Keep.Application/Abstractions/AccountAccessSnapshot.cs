using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Keep.Application.Abstractions;

/// <summary>
/// Foundation state needed by Keep policy composition. Collapses Account +
/// AccountEntitlements into one read model so Keep.Application can evaluate
/// AccountAccessPolicy and FeatureAccessPolicy without exposing Foundation
/// DbSets through a Keep-owned interface.
/// </summary>
public sealed record AccountAccessSnapshot(
    Guid AccountId,
    AccountLifecycleState LifecycleState,
    AccountPurpose Purpose,
    AccountPlan Plan,
    AccountCommercialState CommercialState,
    AccountOperatingMode OperatingMode,
    DateTime? TrialEndsAtUtc,
    DateTime? PastDueGraceEndsAtUtc);
