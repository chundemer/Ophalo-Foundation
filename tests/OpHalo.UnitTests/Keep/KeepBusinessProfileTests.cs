using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Errors;

namespace OpHalo.UnitTests.Keep;

public class KeepBusinessProfileTests
{
    static readonly Guid AccountId = Guid.NewGuid();

    // --- Create ---

    [Fact]
    public void Create_sets_account_id_with_null_contact_fields()
    {
        var profile = KeepBusinessProfile.Create(AccountId);

        Assert.Equal(AccountId, profile.AccountId);
        Assert.Null(profile.CustomerFacingPhone);
        Assert.Null(profile.CustomerFacingEmail);
    }

    [Fact]
    public void Create_requires_non_empty_account_id() =>
        Assert.Throws<ArgumentException>(() => KeepBusinessProfile.Create(Guid.Empty));

    // --- UpdateContact ---

    [Fact]
    public void UpdateContact_sets_trimmed_values()
    {
        var profile = KeepBusinessProfile.Create(AccountId);

        profile.UpdateContact("  +61 400 000 000  ", "  hello@acme.com  ");

        Assert.Equal("+61 400 000 000", profile.CustomerFacingPhone);
        Assert.Equal("hello@acme.com", profile.CustomerFacingEmail);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void UpdateContact_treats_blank_phone_as_null(string? phone)
    {
        var profile = KeepBusinessProfile.Create(AccountId);
        profile.UpdateContact("+61 400 000 000", "x@x.com");

        profile.UpdateContact(phone, "x@x.com");

        Assert.Null(profile.CustomerFacingPhone);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void UpdateContact_treats_blank_email_as_null(string? email)
    {
        var profile = KeepBusinessProfile.Create(AccountId);
        profile.UpdateContact("+61 400 000 000", "x@x.com");

        profile.UpdateContact("+61 400 000 000", email);

        Assert.Null(profile.CustomerFacingEmail);
    }

    [Fact]
    public void UpdateContact_can_clear_both_fields()
    {
        var profile = KeepBusinessProfile.Create(AccountId);
        profile.UpdateContact("+61 400 000 000", "x@x.com");

        profile.UpdateContact(null, null);

        Assert.Null(profile.CustomerFacingPhone);
        Assert.Null(profile.CustomerFacingEmail);
    }

    // --- UpdatePublicIdentity ---

    [Fact]
    public void UpdatePublicIdentity_sets_trimmed_valid_https_urls()
    {
        var profile = KeepBusinessProfile.Create(AccountId);

        var result = profile.UpdatePublicIdentity(
            "  https://cdn.example.com/logo.png  ", "  https://acme.example.com  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("https://cdn.example.com/logo.png", profile.LogoUrl);
        Assert.Equal("https://acme.example.com", profile.WebsiteUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void UpdatePublicIdentity_treats_blank_values_as_null(string? value)
    {
        var profile = KeepBusinessProfile.Create(AccountId);
        profile.UpdatePublicIdentity("https://cdn.example.com/logo.png", "https://acme.example.com");

        var result = profile.UpdatePublicIdentity(value, value);

        Assert.True(result.IsSuccess);
        Assert.Null(profile.LogoUrl);
        Assert.Null(profile.WebsiteUrl);
    }

    [Theory]
    [InlineData("http://cdn.example.com/logo.png")]
    [InlineData("ftp://cdn.example.com/logo.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not-a-url")]
    [InlineData("/relative/logo.png")]
    public void UpdatePublicIdentity_rejects_non_absolute_https_logo_url(string logoUrl)
    {
        var profile = KeepBusinessProfile.Create(AccountId);

        var result = profile.UpdatePublicIdentity(logoUrl, null);

        Assert.True(result.IsFailure);
        Assert.Equal(KeepBusinessProfileErrors.LogoUrlInvalid, result.Error);
        Assert.Null(profile.LogoUrl);
    }

    [Fact]
    public void UpdatePublicIdentity_rejects_non_absolute_https_website_url()
    {
        var profile = KeepBusinessProfile.Create(AccountId);

        var result = profile.UpdatePublicIdentity(null, "http://acme.example.com");

        Assert.True(result.IsFailure);
        Assert.Equal(KeepBusinessProfileErrors.WebsiteUrlInvalid, result.Error);
        Assert.Null(profile.WebsiteUrl);
    }

    [Fact]
    public void UpdatePublicIdentity_rejects_logo_url_over_max_length()
    {
        var profile = KeepBusinessProfile.Create(AccountId);
        var longUrl = "https://cdn.example.com/" + new string('a', 2048);

        var result = profile.UpdatePublicIdentity(longUrl, null);

        Assert.True(result.IsFailure);
        Assert.Equal(KeepBusinessProfileErrors.LogoUrlTooLong, result.Error);
    }

    [Fact]
    public void UpdatePublicIdentity_rejects_website_url_over_max_length()
    {
        var profile = KeepBusinessProfile.Create(AccountId);
        var longUrl = "https://acme.example.com/" + new string('a', 2048);

        var result = profile.UpdatePublicIdentity(null, longUrl);

        Assert.True(result.IsFailure);
        Assert.Equal(KeepBusinessProfileErrors.WebsiteUrlTooLong, result.Error);
    }

    [Fact]
    public void UpdatePublicIdentity_does_not_partially_apply_when_website_invalid()
    {
        var profile = KeepBusinessProfile.Create(AccountId);

        var result = profile.UpdatePublicIdentity("https://cdn.example.com/logo.png", "not-a-url");

        Assert.True(result.IsFailure);
        Assert.Null(profile.LogoUrl);
        Assert.Null(profile.WebsiteUrl);
    }
}
