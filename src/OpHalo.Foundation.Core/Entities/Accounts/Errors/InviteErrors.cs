using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Core.Entities.Accounts.Errors;

/// <summary>
/// Invite domain errors — covers send-invite authorization and accept-invite validation.
/// HTTP mappings: Forbidden→403, InvalidToken→422, Expired→422, AlreadyActive→409,
/// SeatLimitReached→409 (ADR-077).
/// </summary>
public static class InviteErrors
{
    public static readonly Error Forbidden =
        Error.Create("Invite.Forbidden", "You do not have permission to invite members.");

    public static readonly Error InvalidToken =
        Error.Create("Invite.InvalidToken", "The invite link is invalid or has already been used.");

    public static readonly Error Expired =
        Error.Create("Invite.Expired", "The invite link has expired. Please request a new invite.");

    public static readonly Error AlreadyActive =
        Error.Create("Invite.AlreadyActive", "This person is already a member of the account.");

    public static readonly Error SeatLimitReached =
        Error.Create("Invite.SeatLimitReached", "Your account has reached its member seat limit.");
}
