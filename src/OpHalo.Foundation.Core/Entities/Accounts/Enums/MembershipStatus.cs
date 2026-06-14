namespace OpHalo.Foundation.Core.Entities.Accounts.Enums;

/// <summary>
/// Membership standing of an <c>AccountUser</c> within their Account (ADR-016).
/// Distinct from the account-wide <see cref="AccountLifecycleState"/>: this is per-member.
/// </summary>
public enum MembershipStatus
{
    /// <summary>Invited but has not yet accepted. Cannot authenticate. (Legacy PendingInvite.)</summary>
    Invited = 1,

    /// <summary>May authenticate and act on behalf of the Account.</summary>
    Active = 2,

    /// <summary>Membership has been suspended by an administrator. May not authenticate.</summary>
    Suspended = 3,

    /// <summary>Access has been revoked — terminal.</summary>
    Removed = 4
}
