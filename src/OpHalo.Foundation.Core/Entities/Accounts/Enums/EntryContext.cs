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
}
