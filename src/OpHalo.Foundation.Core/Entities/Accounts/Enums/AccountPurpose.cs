namespace OpHalo.Foundation.Core.Entities.Accounts.Enums;

/// <summary>
/// Classifies the intended use of an Account. Set at creation — immutable after.
/// Internal accounts bypass commercial/trial access checks (access policy D-7).
/// </summary>
public enum AccountPurpose
{
    /// <summary>A customer-facing trade business account.</summary>
    Business = 1,

    /// <summary>An internal platform administration account.</summary>
    Internal = 2
}
