using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Errors;

public static class KeepPublicIntakeLinkErrors
{
    public static readonly Error NotFound =
        Error.Create("KeepPublicIntakeLink.NotFound", "Public intake link not found or invalid.");

    public static readonly Error AlreadyRevoked =
        Error.Create("KeepPublicIntakeLink.AlreadyRevoked", "This intake link has already been revoked.");

    public static readonly Error NoActiveLink =
        Error.Create("KeepPublicIntakeLink.NoActiveLink", "No active intake link exists for this account.");

    public static readonly Error SlugTaken =
        Error.Create("keep.public_intake.slug_taken", "That link name is already in use. Try a different name.");

    public static readonly Error ReplaceConfirmationInvalid =
        Error.Create("KeepPublicIntakeLink.ReplaceConfirmationInvalid",
            "Type REPLACE to confirm this action.");
}
