using OpHalo.Keep.Application.Services;

namespace OpHalo.UnitTests.Keep;

public class KeepTokenServiceTests
{
    private readonly KeepTokenService _sut = new();

    // --- Page token ---

    [Fact]
    public void GeneratePageToken_produces_url_safe_base64()
    {
        var token = _sut.GeneratePageToken();

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.DoesNotContain("+", token);
        Assert.DoesNotContain("/", token);
        Assert.DoesNotContain("=", token);
    }

    [Fact]
    public void GeneratePageToken_is_unique_across_calls()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => _sut.GeneratePageToken()).ToList();
        Assert.Equal(100, tokens.Distinct().Count());
    }

    // --- Public intake token ---

    [Fact]
    public void GeneratePublicIntakeToken_produces_url_safe_base64()
    {
        var token = _sut.GeneratePublicIntakeToken();

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.DoesNotContain("+", token);
        Assert.DoesNotContain("/", token);
        Assert.DoesNotContain("=", token);
    }

    // --- Hash ---

    [Fact]
    public void HashPublicIntakeToken_is_lowercase_hex_64_chars()
    {
        var hash = _sut.HashPublicIntakeToken("some-raw-token");

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]+$", hash);
    }

    [Fact]
    public void HashPublicIntakeToken_is_deterministic()
    {
        const string raw = "same-token";
        Assert.Equal(_sut.HashPublicIntakeToken(raw), _sut.HashPublicIntakeToken(raw));
    }

    [Fact]
    public void HashPublicIntakeToken_differs_for_different_inputs()
    {
        Assert.NotEqual(
            _sut.HashPublicIntakeToken("token-a"),
            _sut.HashPublicIntakeToken("token-b"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void HashPublicIntakeToken_rejects_blank_input(string raw) =>
        Assert.Throws<ArgumentException>(() => _sut.HashPublicIntakeToken(raw));

    // --- Reference code ---

    [Fact]
    public void GenerateReferenceCode_is_8_chars_from_safe_alphabet()
    {
        var code = _sut.GenerateReferenceCode();

        Assert.Equal(8, code.Length);
        Assert.Matches("^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]+$", code);
    }

    [Fact]
    public void GenerateReferenceCode_excludes_ambiguous_chars()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => _sut.GenerateReferenceCode()).ToList();
        var allChars = string.Concat(codes);

        Assert.DoesNotContain('0', allChars);
        Assert.DoesNotContain('1', allChars);
        Assert.DoesNotContain('I', allChars);
        Assert.DoesNotContain('L', allChars);
        Assert.DoesNotContain('O', allChars);
    }
}
