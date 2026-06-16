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
    /// Public API code for the PreviouslyRemoved 409 response. Always use this code in the
    /// HTTP response body — never expose the internal routing codes below.
    /// </summary>
    public static readonly Error PreviouslyRemoved =
        Error.Create("Member.PreviouslyRemoved",
            "This person was previously removed from the account.");

    // -------------------------------------------------------------------------
    // SERVICE-TO-ENDPOINT ROUTING ERRORS — not public API codes.
    //
    // SendInviteService uses these to carry suggestedAction context to the
    // SendInvite endpoint. The endpoint MUST intercept both and translate them
    // to Member.PreviouslyRemoved + suggestedAction before calling ErrorHttpMapper.
    // These codes must NEVER appear in an API response body.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Internal routing code: Removed member has a UserId → caller should use reactivate.
    /// The SendInvite endpoint translates this to Member.PreviouslyRemoved + suggestedAction: "reactivate".
    /// </summary>
    public static readonly Error PreviouslyRemovedNeedsReactivate =
        Error.Create("Member.PreviouslyRemovedNeedsReactivate",
            "This person was previously removed and has a user account — use reactivate.");

    /// <summary>
    /// Internal routing code: Removed invite has no UserId → caller should use resend-invite.
    /// The SendInvite endpoint translates this to Member.PreviouslyRemoved + suggestedAction: "resend_invite".
    /// </summary>
    public static readonly Error PreviouslyRemovedNeedsResend =
        Error.Create("Member.PreviouslyRemovedNeedsResend",
            "This person was previously removed without accepting their invite — use resend-invite.");
}
