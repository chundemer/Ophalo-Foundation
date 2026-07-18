namespace OpHalo.Api.Keep;

public sealed record UpdateProfileBody(
    string BusinessName,
    string TimeZone,
    string? CustomerFacingPhone,
    string? CustomerFacingEmail,
    string? LogoUrl,
    string? WebsiteUrl);
