using Microsoft.Extensions.Configuration;
using OpHalo.Keep.Infrastructure.PriceBook;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Focused coverage for <see cref="ConfigurationReleaseGatePolicy"/> (BL142 Session 1, ADR-496):
/// the gate must fail closed for every input except an explicit, valid "true" — including a
/// malformed value, which <c>IConfiguration.GetValue&lt;bool?&gt;</c> would throw on instead of
/// returning false.
/// </summary>
public class ConfigurationReleaseGatePolicyTests
{
    private const string Key = "Keep:ReleaseGates:ProposedWorkQuotes";

    private static IConfiguration BuildConfiguration(string? value)
    {
        var values = new Dictionary<string, string?>();
        if (value is not null)
            values[Key] = value;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void IsProposedWorkReleased_KeyMissing_ReturnsFalse()
    {
        var policy = new ConfigurationReleaseGatePolicy(BuildConfiguration(null));

        Assert.False(policy.IsProposedWorkReleased());
    }

    [Fact]
    public void IsProposedWorkReleased_MalformedValue_ReturnsFalseWithoutThrowing()
    {
        var policy = new ConfigurationReleaseGatePolicy(BuildConfiguration("not-a-bool"));

        var exception = Record.Exception(() => policy.IsProposedWorkReleased());

        Assert.Null(exception);
        Assert.False(policy.IsProposedWorkReleased());
    }

    [Fact]
    public void IsProposedWorkReleased_ExplicitTrue_ReturnsTrue()
    {
        var policy = new ConfigurationReleaseGatePolicy(BuildConfiguration("true"));

        Assert.True(policy.IsProposedWorkReleased());
    }

    [Fact]
    public void IsProposedWorkReleased_ExplicitFalse_ReturnsFalse()
    {
        var policy = new ConfigurationReleaseGatePolicy(BuildConfiguration("false"));

        Assert.False(policy.IsProposedWorkReleased());
    }
}
