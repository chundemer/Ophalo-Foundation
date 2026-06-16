using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Core.Entities.Accounts.Errors;

/// <summary>
/// Member-management domain errors — covers list, role change, suspend, reactivate, remove, and
/// resend-invite. HTTP mappings: Forbidden→403, NotFound→404, CannotModifySelf/CannotModifyOwner/
/// PrimaryOwnerProtected/InvalidStatusTransition→422, OwnerLimitReached/LastOwner/
/// SeatLimitReached/PreviouslyRemoved→409, InvalidRole→400 (ADR-079..082).
/// </summary>
public static class MemberErrors
{
    public static readonly Error Forbidden =
        Error.Create("Member.Forbidden", "You do not have permission to manage members.");

    public static readonly Error NotFound =
        Error.Create("Member.NotFound", "The specified member was not found.");

    public static readonly Error CannotModifySelf =
        Error.Create("Member.CannotModifySelf",
            "You cannot modify your own membership through member management.");

    public static readonly Error CannotModifyOwner =
        Error.Create("Member.CannotModifyOwner",
            "Admins cannot manage Owner-role members.");

    public static readonly Error PrimaryOwnerProtected =
        Error.Create("Member.PrimaryOwnerProtected",
            "The primary owner cannot be modified in this operation.");

    public static readonly Error OwnerLimitReached =
        Error.Create("Member.OwnerLimitReached",
            "An account may have at most 2 Owner-role members.");

    public static readonly Error LastOwner =
        Error.Create("Member.LastOwner",
            "At least one Active Owner must remain.");

    public static readonly Error InvalidRole =
        Error.Create("Member.InvalidRole",
            "The specified role is not valid.");

    public static readonly Error InvalidStatusTransition =
        Error.Create("Member.InvalidStatusTransition",
            "The requested status transition is not allowed for this member.");

    public static readonly Error SeatLimitReached =
        Error.Create("Member.SeatLimitReached",
            "The account has reached its member seat limit.");

    /// <summary>
    /// Returned when sending a new invite to an email that already has a Removed membership.
    /// The API response should include a suggestedAction ("reactivate" when UserId is set,
    /// "resend_invite" when UserId is null) — that metadata is added in 5E-C at the endpoint layer.
    /// </summary>
    public static readonly Error PreviouslyRemoved =
        Error.Create("Member.PreviouslyRemoved",
            "This person was previously removed from the account.");
}
