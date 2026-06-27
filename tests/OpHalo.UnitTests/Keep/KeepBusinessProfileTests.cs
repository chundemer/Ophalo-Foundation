using OpHalo.Keep.Core.Entities;

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
}
