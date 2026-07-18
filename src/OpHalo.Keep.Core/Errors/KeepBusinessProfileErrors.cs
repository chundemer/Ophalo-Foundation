using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Errors;

public static class KeepBusinessProfileErrors
{
    public static readonly Error LogoUrlTooLong =
        Error.Create("KeepBusinessProfile.LogoUrlTooLong", "Logo URL must not exceed 2048 characters.");

    public static readonly Error LogoUrlInvalid =
        Error.Create("KeepBusinessProfile.LogoUrlInvalid", "Logo URL must be a valid absolute https:// URL.");

    public static readonly Error WebsiteUrlTooLong =
        Error.Create("KeepBusinessProfile.WebsiteUrlTooLong", "Website URL must not exceed 2048 characters.");

    public static readonly Error WebsiteUrlInvalid =
        Error.Create("KeepBusinessProfile.WebsiteUrlInvalid", "Website URL must be a valid absolute https:// URL.");
}
