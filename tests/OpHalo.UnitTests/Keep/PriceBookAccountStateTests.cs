using OpHalo.Keep.Core.Entities;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks <see cref="PriceBookAccountState"/> (ADR-470, build-log/111): construction and the
/// publish-lock rotation every publish/override transaction must perform.
/// </summary>
public class PriceBookAccountStateTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();

    [Fact]
    public void Create_with_valid_account_id_succeeds_with_a_nonempty_lock()
    {
        var state = PriceBookAccountState.Create(AccountId);

        Assert.Equal(AccountId, state.AccountId);
        Assert.NotEqual(Guid.Empty, state.PublishLockVersion);
    }

    [Fact]
    public void Create_with_empty_account_id_throws()
    {
        Assert.Throws<ArgumentException>(() => PriceBookAccountState.Create(Guid.Empty));
    }

    [Fact]
    public void Bump_rotates_the_publish_lock_version()
    {
        var state = PriceBookAccountState.Create(AccountId);
        var before = state.PublishLockVersion;

        state.Bump();

        Assert.NotEqual(before, state.PublishLockVersion);
    }

    [Fact]
    public void Bump_called_twice_produces_two_distinct_tokens()
    {
        var state = PriceBookAccountState.Create(AccountId);

        state.Bump();
        var afterFirst = state.PublishLockVersion;
        state.Bump();

        Assert.NotEqual(afterFirst, state.PublishLockVersion);
    }
}
