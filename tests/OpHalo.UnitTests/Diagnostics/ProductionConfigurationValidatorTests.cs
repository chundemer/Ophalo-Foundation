using Microsoft.Extensions.Configuration;
using OpHalo.Api.Diagnostics;

namespace OpHalo.UnitTests.Diagnostics;

public class ProductionConfigurationValidatorTests
{
    private static IConfiguration BuildConfiguration(IDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=db;Database=ophalo;Username=u;Password=p",
            ["App:PublicBaseUrl"] = "https://app.ophalo.com",
            ["Resend:ApiKey"] = "re_test_key",
            ["Resend:FromAddress"] = "OpHalo <no-reply@mail.ophalo.com>",
        };

        if (overrides is not null)
            foreach (var (key, value) in overrides)
                values[key] = value;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void GetMissingKeys_WithAllRequiredValues_ReturnsEmpty()
    {
        var missing = ProductionConfigurationValidator.GetMissingKeys(BuildConfiguration());

        Assert.Empty(missing);
    }

    [Fact]
    public void GetMissingKeys_MissingConnectionString_ReportsRailwayDoubleUnderscoreKey()
    {
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = null
        });

        var missing = ProductionConfigurationValidator.GetMissingKeys(config);

        Assert.Contains(missing, m => m.Contains("ConnectionStrings__DefaultConnection"));
    }

    [Fact]
    public void GetMissingKeys_MissingPublicBaseUrl_ReportsIt()
    {
        var config = BuildConfiguration(new Dictionary<string, string?> { ["App:PublicBaseUrl"] = "" });

        var missing = ProductionConfigurationValidator.GetMissingKeys(config);

        Assert.Contains("App:PublicBaseUrl", missing);
    }

    [Fact]
    public void GetMissingKeys_MissingResendApiKey_ReportsIt()
    {
        var config = BuildConfiguration(new Dictionary<string, string?> { ["Resend:ApiKey"] = "   " });

        var missing = ProductionConfigurationValidator.GetMissingKeys(config);

        Assert.Contains("Resend:ApiKey", missing);
    }

    [Fact]
    public void GetMissingKeys_ResendFromAddressWithoutAtSign_ReportsInvalid()
    {
        var config = BuildConfiguration(new Dictionary<string, string?> { ["Resend:FromAddress"] = "not-an-email" });

        var missing = ProductionConfigurationValidator.GetMissingKeys(config);

        Assert.Contains(missing, m => m.StartsWith("Resend:FromAddress"));
    }

    [Fact]
    public void ValidateOrThrow_WithMissingValues_Throws()
    {
        var config = BuildConfiguration(new Dictionary<string, string?> { ["Resend:ApiKey"] = "" });

        Assert.Throws<InvalidOperationException>(() => ProductionConfigurationValidator.ValidateOrThrow(config));
    }

    [Fact]
    public void ValidateOrThrow_WithAllValuesPresent_DoesNotThrow()
    {
        var config = BuildConfiguration();

        var exception = Record.Exception(() => ProductionConfigurationValidator.ValidateOrThrow(config));

        Assert.Null(exception);
    }
}
