using OpHalo.SharedKernel.Time;
using Xunit;

namespace OpHalo.UnitTests.SharedKernel;

public class TimeZoneIdTests
{
    [Fact]
    public void TryResolve_returns_true_for_a_valid_iana_id()
    {
        var resolved = TimeZoneId.TryResolve("America/Chicago", out var timeZone);

        Assert.True(resolved);
        Assert.Equal("America/Chicago", timeZone.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not/AZone")]
    public void TryResolve_returns_false_and_utc_for_missing_or_unresolvable_values(string? value)
    {
        var resolved = TimeZoneId.TryResolve(value, out var timeZone);

        Assert.False(resolved);
        Assert.Equal(TimeZoneInfo.Utc, timeZone);
    }
}
