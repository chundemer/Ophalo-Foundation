using OpHalo.Keep.Core.Domain;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.UnitTests.Keep;

public class KeepCustomerTests
{
    static readonly Guid AccountId = Guid.NewGuid();

    // --- Create ---

    [Fact]
    public void Create_produces_customer_with_trimmed_fields()
    {
        var customer = KeepCustomer.Create(AccountId, "  Jane Smith  ", "  0412 345 678  ", " jane@example.com ");

        Assert.Equal(AccountId, customer.AccountId);
        Assert.Equal("Jane Smith", customer.Name);
        Assert.Equal("0412 345 678", customer.PrimaryPhone);
        Assert.Equal("0412345678", customer.CanonicalPhone);
        Assert.Equal("jane@example.com", customer.Email);
    }

    [Theory]
    [InlineData("0412 345 678", "0412345678")]   // spaces stripped
    [InlineData("+61 412-345-678", "61412345678")] // international prefix, hyphens
    [InlineData("(02) 9876-5432", "0298765432")] // parens and hyphen
    [InlineData("5551234", "5551234")]             // already canonical, 7-digit minimum
    public void Create_derives_canonical_phone_by_stripping_non_digits(string input, string expectedCanonical)
    {
        var customer = KeepCustomer.Create(AccountId, "Jane", input);
        Assert.Equal(expectedCanonical, customer.CanonicalPhone);
    }

    [Theory]
    [InlineData("123456")]   // 6 digits — below 7-digit minimum
    [InlineData("12")]       // very short
    public void Create_throws_when_phone_has_too_few_digits(string phone) =>
        Assert.Throws<ArgumentException>(() => KeepCustomer.Create(AccountId, "Jane", phone));

    [Fact]
    public void Create_throws_when_phone_has_too_many_digits()
    {
        // 16 digits — exceeds 15-digit maximum
        const string tooLong = "1234567890123456";
        Assert.Throws<ArgumentException>(() => KeepCustomer.Create(AccountId, "Jane", tooLong));
    }

    [Fact]
    public void Create_accepts_15_digit_phone_as_maximum()
    {
        const string maxLength = "123456789012345";
        var customer = KeepCustomer.Create(AccountId, "Jane", maxLength);
        Assert.Equal(maxLength, customer.CanonicalPhone);
    }

    [Fact]
    public void Create_allows_null_email()
    {
        var customer = KeepCustomer.Create(AccountId, "Jane Smith", "0412345678");
        Assert.Null(customer.Email);
    }

    [Fact]
    public void Create_requires_non_empty_account_id() =>
        Assert.Throws<ArgumentException>(() =>
            KeepCustomer.Create(Guid.Empty, "Jane", "0412345678"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_name(string name) =>
        Assert.Throws<ArgumentException>(() =>
            KeepCustomer.Create(AccountId, name, "0412345678"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_phone(string phone) =>
        Assert.Throws<ArgumentException>(() =>
            KeepCustomer.Create(AccountId, "Jane", phone));

    // --- UpdateContactInfo ---

    [Fact]
    public void UpdateContactInfo_updates_name_and_email()
    {
        var customer = KeepCustomer.Create(AccountId, "Jane", "0412345678");
        customer.UpdateContactInfo("Jane Smith", "jane@example.com");

        Assert.Equal("Jane Smith", customer.Name);
        Assert.Equal("jane@example.com", customer.Email);
    }

    [Fact]
    public void UpdateContactInfo_clears_email_when_null()
    {
        var customer = KeepCustomer.Create(AccountId, "Jane", "0412345678", "jane@example.com");
        customer.UpdateContactInfo("Jane", null);
        Assert.Null(customer.Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateContactInfo_requires_name(string name)
    {
        var customer = KeepCustomer.Create(AccountId, "Jane", "0412345678");
        Assert.Throws<ArgumentException>(() => customer.UpdateContactInfo(name, null));
    }
}
