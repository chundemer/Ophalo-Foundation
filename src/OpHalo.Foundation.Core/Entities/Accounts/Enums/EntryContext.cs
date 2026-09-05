namespace OpHalo.Foundation.Core.Entities.Accounts.Enums;

/// <summary>
/// Classifies the auth intent resolved at code issuance time and used by /exchange to route.
/// Values match the reference app to allow safe future additions without gap confusion.
/// </summary>
public enum EntryContext
{
    /// <summary>New email — Account, User, and AccountUser will be created at /exchange.</summary>
    NewAccount = 1,

    /// <summary>Existing verified User with at least one active AccountUser — standard re-auth.</summary>
    ExistingMember = 2,

    /// <summary>Invited member accepting via raw token — not routed through /auth/exchange (ADR-074).</summary>
    InvitedUser = 3,

    /// <summary>2+ active AccountUsers across accounts for the same email — workspace selection is
    /// resolved at /exchange via a live query (later slice); AccountId/TargetAccountUserId are
    /// always null at issuance, matching NewAccount's deferred-target shape.</summary>
    MultipleMembers = 4,
}
