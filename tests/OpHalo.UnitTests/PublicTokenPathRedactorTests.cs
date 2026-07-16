using OpHalo.Api.Helpers;

namespace OpHalo.UnitTests;

public sealed class PublicTokenPathRedactorTests
{
    // --- /keep/public-intake/token/{token} ---

    [Fact]
    public void PublicIntakePath_TokenIsRedacted()
    {
        var result = PublicTokenPathRedactor.Redact("/keep/public-intake/token/abc123secret");
        Assert.Equal("/keep/public-intake/token/[redacted]", result);
        Assert.DoesNotContain("abc123secret", result);
    }

    [Fact]
    public void PublicIntakePath_CaseInsensitive_IsRedacted()
    {
        var result = PublicTokenPathRedactor.Redact("/KEEP/PUBLIC-INTAKE/TOKEN/abc123secret");
        Assert.Equal("/keep/public-intake/token/[redacted]", result);
    }

    // --- /continuity/public-intake/token/{token} (legacy alias) ---

    [Fact]
    public void LegacyContinuityIntakePath_TokenIsRedacted()
    {
        var result = PublicTokenPathRedactor.Redact("/continuity/public-intake/token/xyz987secret");
        Assert.Equal("/continuity/public-intake/token/[redacted]", result);
        Assert.DoesNotContain("xyz987secret", result);
    }

    // --- /keep/r/{token} ---

    [Fact]
    public void CustomerPagePath_NoAction_TokenIsRedacted()
    {
        var result = PublicTokenPathRedactor.Redact("/keep/r/pagetoken99secret");
        Assert.Equal("/keep/r/[redacted]", result);
        Assert.DoesNotContain("pagetoken99secret", result);
    }

    // --- /keep/r/{token}/{action} ---

    [Theory]
    [InlineData("message")]
    [InlineData("question")]
    [InlineData("update_request")]
    [InlineData("schedule_change_request")]
    [InlineData("change_or_cancel_request")]
    [InlineData("issue")]
    [InlineData("feedback")]
    public void CustomerWritePath_TokenIsRedacted_ActionPreserved(string action)
    {
        var result = PublicTokenPathRedactor.Redact($"/keep/r/pagetoken99secret/{action}");
        Assert.Equal($"/keep/r/[redacted]/{action}", result);
        Assert.DoesNotContain("pagetoken99secret", result);
        Assert.Contains(action, result);
    }

    // --- /keep/intake-sms/{token} ---

    [Fact]
    public void IntakeSmsHandoffPath_TokenIsRedacted()
    {
        var result = PublicTokenPathRedactor.Redact("/keep/intake-sms/abc123secret");
        Assert.Equal("/keep/intake-sms/[redacted]", result);
        Assert.DoesNotContain("abc123secret", result);
    }

    [Fact]
    public void IntakeSmsHandoffPath_CaseInsensitive_IsRedacted()
    {
        var result = PublicTokenPathRedactor.Redact("/KEEP/INTAKE-SMS/abc123secret");
        Assert.Equal("/keep/intake-sms/[redacted]", result);
    }

    // --- Unrelated paths are returned unchanged ---

    [Theory]
    [InlineData("/keep/requests")]
    [InlineData("/keep/requests/available")]
    [InlineData("/keep/setup/intake")]
    [InlineData("/auth/signin")]
    [InlineData("/keep/r")]
    public void UnrelatedPath_ReturnedUnchanged(string path)
    {
        Assert.Equal(path, PublicTokenPathRedactor.Redact(path));
    }

    // --- Edge cases ---

    [Fact]
    public void NullPath_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, PublicTokenPathRedactor.Redact(null));
    }

    [Fact]
    public void EmptyPath_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, PublicTokenPathRedactor.Redact(string.Empty));
    }
}
