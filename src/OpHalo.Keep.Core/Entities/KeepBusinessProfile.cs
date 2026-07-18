using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

public sealed class KeepBusinessProfile : BaseEntity
{
    private const int MaxPublicUrlLength = 2048;

    public Guid AccountId { get; private set; }
    public string? CustomerFacingPhone { get; private set; }
    public string? CustomerFacingEmail { get; private set; }
    public string? LogoUrl { get; private set; }
    public string? WebsiteUrl { get; private set; }

    public static KeepBusinessProfile Create(Guid accountId)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));

        return new KeepBusinessProfile { AccountId = accountId };
    }

    public void UpdateContact(string? customerFacingPhone, string? customerFacingEmail)
    {
        CustomerFacingPhone = string.IsNullOrWhiteSpace(customerFacingPhone) ? null : customerFacingPhone.Trim();
        CustomerFacingEmail = string.IsNullOrWhiteSpace(customerFacingEmail) ? null : customerFacingEmail.Trim();
    }

    /// <summary>
    /// Input-validated absolute https:// URLs only (GAP-033/R90b). This is format validation, not
    /// DNS/domain-ownership verification — never label the result "verified".
    /// </summary>
    public Result UpdatePublicIdentity(string? logoUrl, string? websiteUrl)
    {
        var trimmedLogo = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
        var trimmedWebsite = string.IsNullOrWhiteSpace(websiteUrl) ? null : websiteUrl.Trim();

        if (trimmedLogo is not null)
        {
            if (trimmedLogo.Length > MaxPublicUrlLength)
                return Result.Failure(KeepBusinessProfileErrors.LogoUrlTooLong);
            if (!IsAbsoluteHttpsUrl(trimmedLogo))
                return Result.Failure(KeepBusinessProfileErrors.LogoUrlInvalid);
        }

        if (trimmedWebsite is not null)
        {
            if (trimmedWebsite.Length > MaxPublicUrlLength)
                return Result.Failure(KeepBusinessProfileErrors.WebsiteUrlTooLong);
            if (!IsAbsoluteHttpsUrl(trimmedWebsite))
                return Result.Failure(KeepBusinessProfileErrors.WebsiteUrlInvalid);
        }

        LogoUrl = trimmedLogo;
        WebsiteUrl = trimmedWebsite;
        return Result.Success();
    }

    private static bool IsAbsoluteHttpsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
