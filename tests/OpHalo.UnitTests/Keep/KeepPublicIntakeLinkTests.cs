using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Errors;

namespace OpHalo.UnitTests.Keep;

public class KeepPublicIntakeLinkTests
{
    static readonly Guid AccountId = Guid.NewGuid();
    static readonly DateTime Now = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private const string DefaultHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    static KeepPublicIntakeLink NewLink(
        string slug = "acme-plumbing",
        string tokenHash = DefaultHash) =>
        KeepPublicIntakeLink.Create(AccountId, slug, tokenHash);

    // --- Create ---

    [Fact]
    public void Create_produces_active_link_with_trimmed_slug()
    {
        var link = KeepPublicIntakeLink.Create(AccountId, "  acme-plumbing  ", new string('a', 64));

        Assert.Equal(AccountId, link.AccountId);
        Assert.Equal("acme-plumbing", link.PublicSlug);
        Assert.True(link.IsActive);
        Assert.Null(link.RevokedAtUtc);
    }

    [Fact]
    public void Create_requires_non_empty_account_id() =>
        Assert.Throws<ArgumentException>(() =>
            KeepPublicIntakeLink.Create(Guid.Empty, "slug", new string('a', 64)));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_public_slug(string slug) =>
        Assert.Throws<ArgumentException>(() =>
            KeepPublicIntakeLink.Create(AccountId, slug, new string('a', 64)));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_token_hash(string hash) =>
        Assert.Throws<ArgumentException>(() =>
            KeepPublicIntakeLink.Create(AccountId, "slug", hash));

    // --- Revoke ---

    [Fact]
    public void Revoke_sets_revoked_timestamp_and_link_becomes_inactive()
    {
        var link = NewLink();
        var result = link.Revoke(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, link.RevokedAtUtc);
        Assert.False(link.IsActive);
    }

    [Fact]
    public void Revoke_twice_returns_already_revoked_error()
    {
        var link = NewLink();
        link.Revoke(Now);

        var result = link.Revoke(Now.AddMinutes(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepPublicIntakeLinkErrors.AlreadyRevoked.Code, result.Error.Code);
    }

    [Fact]
    public void Create_sets_created_by_user_id_when_provided()
    {
        var actorId = Guid.NewGuid();
        var link = KeepPublicIntakeLink.Create(AccountId, "acme-plumbing", DefaultHash, actorId);

        Assert.Equal(actorId, link.CreatedByUserId);
    }

    [Fact]
    public void Create_leaves_created_by_null_when_not_provided()
    {
        var link = KeepPublicIntakeLink.Create(AccountId, "acme-plumbing", DefaultHash);

        Assert.Null(link.CreatedByUserId);
    }

    [Fact]
    public void Revoke_sets_modified_by_user_id()
    {
        var actorId = Guid.NewGuid();
        var link = NewLink();
        link.Revoke(Now, actorId);

        Assert.Equal(actorId, link.ModifiedByUserId);
    }

    [Fact]
    public void Revoke_leaves_modified_by_null_when_not_provided()
    {
        var link = NewLink();
        link.Revoke(Now);

        Assert.Null(link.ModifiedByUserId);
    }

    // --- IsActive ---

    [Fact]
    public void IsActive_is_false_when_soft_deleted()
    {
        var link = NewLink();
        link.DeletedAtUtc = Now;
        Assert.False(link.IsActive);
    }
}
