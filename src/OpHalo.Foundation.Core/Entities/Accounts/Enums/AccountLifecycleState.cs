namespace OpHalo.Foundation.Core.Entities.Accounts.Enums;

/// <summary>
/// Operational standing of an Account. Separate from commercial state
/// (entitlements) and from per-member <see cref="MembershipStatus"/>.
/// </summary>
public enum AccountLifecycleState
{
    /// <summary>Account is in good standing and fully operational.</summary>
    Active = 1,

    /// <summary>Account has been restricted by an administrator.</summary>
    Suspended = 2,

    /// <summary>Account has been permanently shut down.</summary>
    Closed = 3
}
