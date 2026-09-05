using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Core.Entities.Users.Errors;
using Xunit;

namespace OpHalo.UnitTests.Auth;

/// <summary>
/// Locks User.SetName guards (ADR-497 name-blank sign-in / invite-accept continuation).
/// </summary>
public class UserTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static User CreateWithBlankName() => User.CreateVerified("owner@example.com", name: null, nowUtc: Now);

    [Fact]
    public void SetName_on_blank_name_sets_trimmed_name()
    {
        var user = CreateWithBlankName();

        var result = user.SetName("  Riley  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Riley", user.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetName_rejects_blank_name(string? name)
    {
        var user = CreateWithBlankName();

        Assert.Throws<ArgumentException>(() => user.SetName(name!));
    }

    [Fact]
    public void SetName_rejects_overwrite_of_a_non_blank_name()
    {
        var user = CreateWithBlankName();
        user.SetName("Riley");

        var result = user.SetName("Someone Else");

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.NameAlreadySet, result.Error);
        Assert.Equal("Riley", user.Name);
    }
}
