using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Application.Accounts.Access;

public sealed record AccountAccessContext(
    AccountLifecycleState LifecycleState,
    AccountPurpose Purpose,
    AccountCommercialState? CommercialState,
    DateTime? TrialEndsAtUtc,
    DateTime? GracePeriodEndsUtc,
    AccountOperatingMode? OperatingMode,
    bool RequestImplementsAllowedInOffSeason,
    DateTime CurrentTimeUtc);
