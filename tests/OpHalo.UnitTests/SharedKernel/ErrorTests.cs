using OpHalo.SharedKernel.Results;
using Xunit;

namespace OpHalo.UnitTests.SharedKernel;

/// <summary>
/// Behavior tests for the Error primitive ported into SharedKernel in Phase 3.
/// </summary>
public class ErrorTests
{
    [Fact]
    public void Create_sets_code_and_message()
    {
        var error = Error.Create("auth.expired", "The link has expired.");

        Assert.Equal("auth.expired", error.Code);
        Assert.Equal("The link has expired.", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_code(string code)
        => Assert.Throws<ArgumentException>(() => Error.Create(code, "message"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_message(string message)
        => Assert.Throws<ArgumentException>(() => Error.Create("code", message));

    [Fact]
    public void None_is_empty()
    {
        Assert.Equal(string.Empty, Error.None.Code);
        Assert.Equal(string.Empty, Error.None.Message);
    }

    [Fact]
    public void Is_matches_on_code_only()
    {
        var error = Error.Create("auth.expired", "The link has expired.");

        Assert.True(error.Is("auth.expired"));
        Assert.False(error.Is("auth.invalid"));
    }

    [Fact]
    public void Equality_is_value_based()
    {
        var a = Error.Create("x", "y");
        var b = Error.Create("x", "y");

        Assert.Equal(a, b);
    }
}
