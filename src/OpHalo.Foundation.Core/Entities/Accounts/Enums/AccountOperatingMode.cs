namespace OpHalo.Foundation.Core.Entities.Accounts.Enums;

/// <summary>
/// Seasonal operating mode of an account. Distinct from lifecycle and commercial state.
/// OffSeason restricts write operations while keeping the account commercially active.
/// Owned by the (deferred) AccountEntitlements entity and supplied to the access context.
/// </summary>
public enum AccountOperatingMode
{
    /// <summary>Account is operating normally.</summary>
    Standard = 1,

    /// <summary>Read access is permitted; write operations are restricted.</summary>
    OffSeason = 2
}
