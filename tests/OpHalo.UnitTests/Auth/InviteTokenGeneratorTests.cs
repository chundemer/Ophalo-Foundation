using OpHalo.Foundation.Application.Auth;
using Xunit;

namespace OpHalo.UnitTests.Auth;

public sealed class InviteTokenGeneratorTests
{
    // --- GenerateRawToken ---

    [Fact]
    public void GenerateRawToken_ProducesUrlSafeBase64()
    {
        var token = InviteTokenGenerator.GenerateRawToken();
        Assert.False(token.Contains('+'), "Must not contain '+'");
        Assert.False(token.Contains('/'), "Must not contain '/'");
        Assert.False(token.Contains('='), "Must not contain padding '='");
    }

    [Fact]
    public void GenerateRawToken_Has43Characters()
    {
        // 32 random bytes → 43 URL-safe Base64 chars (no padding).
        var token = InviteTokenGenerator.GenerateRawToken();
        Assert.Equal(43, token.Length);
    }

    [Fact]
    public void GenerateRawToken_IsUnique()
    {
        var t1 = InviteTokenGenerator.GenerateRawToken();
        var t2 = InviteTokenGenerator.GenerateRawToken();
        Assert.NotEqual(t1, t2);
    }

    // --- HashToken ---

    [Fact]
    public void HashToken_ProducesUppercaseHex()
    {
        var token = InviteTokenGenerator.GenerateRawToken();
        var hash = InviteTokenGenerator.HashToken(token);
        Assert.Equal(hash, hash.ToUpperInvariant());
    }

    [Fact]
    public void HashToken_Produces64CharHex()
    {
        // SHA-256 → 32 bytes → 64 uppercase hex chars.
        var hash = InviteTokenGenerator.HashToken(InviteTokenGenerator.GenerateRawToken());
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void HashToken_IsDeterministic()
    {
        var token = InviteTokenGenerator.GenerateRawToken();
        Assert.Equal(InviteTokenGenerator.HashToken(token), InviteTokenGenerator.HashToken(token));
    }

    [Fact]
    public void HashToken_DifferentTokensProduceDifferentHashes()
    {
        var h1 = InviteTokenGenerator.HashToken(InviteTokenGenerator.GenerateRawToken());
        var h2 = InviteTokenGenerator.HashToken(InviteTokenGenerator.GenerateRawToken());
        Assert.NotEqual(h1, h2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void HashToken_ThrowsOnBlankInput(string blank)
    {
        Assert.Throws<ArgumentException>(() => InviteTokenGenerator.HashToken(blank));
    }
}
