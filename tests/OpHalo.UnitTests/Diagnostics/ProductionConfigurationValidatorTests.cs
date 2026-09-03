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
            ["Sentry:Dsn"] = "https://examplePublicKey@o0.ingest.sentry.io/0",
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetMissingKeys_MissingSentryDsn_ReportsDeploymentVariableName(string? dsn)
    {
        var config = BuildConfiguration(new Dictionary<string, string?> { ["Sentry:Dsn"] = dsn });

        var missing = ProductionConfigurationValidator.GetMissingKeys(config);

        Assert.Contains("Sentry__Dsn", missing);
    }

    [Theory]
    [InlineData("not-a-dsn")]
    [InlineData("ftp://key@sentry.io/1")]
    [InlineData("o0.ingest.sentry.io/0")]
    public void GetMissingKeys_MalformedSentryDsn_ReportsInvalid(string dsn)
    {
        var config = BuildConfiguration(new Dictionary<string, string?> { ["Sentry:Dsn"] = dsn });

        var missing = ProductionConfigurationValidator.GetMissingKeys(config);

        Assert.Contains(missing, m => m.StartsWith("Sentry__Dsn"));
    }

    [Fact]
    public void GetMissingKeys_ValidSentryDsn_NotReported()
    {
        var missing = ProductionConfigurationValidator.GetMissingKeys(BuildConfiguration());

        Assert.DoesNotContain(missing, m => m.StartsWith("Sentry__Dsn"));
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
