using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using Xunit;

namespace OpHalo.UnitTests.Auth;

/// <summary>
/// Locks PostAuthContinuation factory guards (ADR-497).
/// </summary>
public class PostAuthContinuationTests
{
    private static readonly DateTime ValidIssuedAt =
        new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime ValidExpiresAt =
        new(2026, 1, 1, 0, 10, 0, DateTimeKind.Utc);

    private static PostAuthContinuation CreateValid(
        Guid? targetAccountUserId = null,
        string? deviceName = null) =>
        PostAuthContinuation.Create(
            tokenHash: "abc123",
            userId: Guid.NewGuid(),
            targetAccountUserId: targetAccountUserId,
            clientType: SessionClientType.Browser,
            deviceName: deviceName,
            issuedAtUtc: ValidIssuedAt,
            expiresAtUtc: ValidExpiresAt);

    [Fact]
    public void Create_happy_path_sets_all_fields()
    {
        var userId = Guid.NewGuid();
        var targetAccountUserId = Guid.NewGuid();

        var continuation = PostAuthContinuation.Create(
            tokenHash: "abc123",
            userId: userId,
            targetAccountUserId: targetAccountUserId,
            clientType: SessionClientType.MobileApp,
            deviceName: "  Riley's iPhone  ",
            issuedAtUtc: ValidIssuedAt,
            expiresAtUtc: ValidExpiresAt);

        Assert.Equal("abc123", continuation.TokenHash);
        Assert.Equal(userId, continuation.UserId);
        Assert.Equal(targetAccountUserId, continuation.TargetAccountUserId);
        Assert.Equal(SessionClientType.MobileApp, continuation.ClientType);
        Assert.Equal("Riley's iPhone", continuation.DeviceName);
        Assert.Equal(ValidIssuedAt, continuation.IssuedAtUtc);
        Assert.Equal(ValidExpiresAt, continuation.ExpiresAtUtc);
        Assert.Null(continuation.ConsumedAtUtc);
        Assert.False(continuation.IsConsumed);
    }

    [Fact]
    public void Create_allows_null_target_account_user_id_for_ambiguous_membership()
    {
        var continuation = CreateValid(targetAccountUserId: null);

        Assert.Null(continuation.TargetAccountUserId);
    }

    [Fact]
    public void Create_trims_blank_device_name_to_null()
    {
        var continuation = CreateValid(deviceName: "   ");

        Assert.Null(continuation.DeviceName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_token_hash(string? tokenHash)
    {
        Assert.Throws<ArgumentException>(() => PostAuthContinuation.Create(
            tokenHash: tokenHash!,
            userId: Guid.NewGuid(),
            targetAccountUserId: null,
            clientType: SessionClientType.Browser,
            deviceName: null,
            issuedAtUtc: ValidIssuedAt,
            expiresAtUtc: ValidExpiresAt));
    }

    [Fact]
    public void Create_rejects_empty_user_id()
    {
        Assert.Throws<ArgumentException>(() => PostAuthContinuation.Create(
            tokenHash: "abc123",
            userId: Guid.Empty,
            targetAccountUserId: null,
            clientType: SessionClientType.Browser,
            deviceName: null,
            issuedAtUtc: ValidIssuedAt,
            expiresAtUtc: ValidExpiresAt));
    }

    [Fact]
    public void Create_rejects_empty_target_account_user_id_when_provided()
    {
        Assert.Throws<ArgumentException>(() => PostAuthContinuation.Create(
            tokenHash: "abc123",
            userId: Guid.NewGuid(),
            targetAccountUserId: Guid.Empty,
            clientType: SessionClientType.Browser,
            deviceName: null,
            issuedAtUtc: ValidIssuedAt,
            expiresAtUtc: ValidExpiresAt));
    }

    [Fact]
    public void Create_rejects_undefined_client_type()
    {
        Assert.Throws<ArgumentException>(() => PostAuthContinuation.Create(
            tokenHash: "abc123",
            userId: Guid.NewGuid(),
            targetAccountUserId: null,
            clientType: (SessionClientType)999,
            deviceName: null,
            issuedAtUtc: ValidIssuedAt,
            expiresAtUtc: ValidExpiresAt));
    }

    [Fact]
    public void Create_rejects_non_utc_issued_at()
    {
        Assert.Throws<ArgumentException>(() => PostAuthContinuation.Create(
            tokenHash: "abc123",
            userId: Guid.NewGuid(),
            targetAccountUserId: null,
            clientType: SessionClientType.Browser,
            deviceName: null,
            issuedAtUtc: DateTime.SpecifyKind(ValidIssuedAt, DateTimeKind.Local),
            expiresAtUtc: ValidExpiresAt));
    }

    [Fact]
    public void Create_rejects_non_utc_expires_at()
    {
        Assert.Throws<ArgumentException>(() => PostAuthContinuation.Create(
            tokenHash: "abc123",
            userId: Guid.NewGuid(),
            targetAccountUserId: null,
            clientType: SessionClientType.Browser,
            deviceName: null,
            issuedAtUtc: ValidIssuedAt,
            expiresAtUtc: DateTime.SpecifyKind(ValidExpiresAt, DateTimeKind.Local)));
    }

    [Fact]
    public void Create_rejects_expires_at_not_after_issued_at()
    {
        Assert.Throws<ArgumentException>(() => PostAuthContinuation.Create(
            tokenHash: "abc123",
            userId: Guid.NewGuid(),
            targetAccountUserId: null,
            clientType: SessionClientType.Browser,
            deviceName: null,
            issuedAtUtc: ValidIssuedAt,
            expiresAtUtc: ValidIssuedAt));
    }

    [Fact]
    public void IsExpired_is_fail_closed_at_the_exact_expiry_instant()
    {
        var continuation = CreateValid();

        Assert.True(continuation.IsExpired(ValidExpiresAt));
        Assert.False(continuation.IsExpired(ValidExpiresAt.AddTicks(-1)));
    }
}
