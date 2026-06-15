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
        Assert.Equal("jane@example.com", customer.Email);
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
