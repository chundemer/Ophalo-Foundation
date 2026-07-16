using OpHalo.Keep.Core.Domain;

namespace OpHalo.UnitTests.Keep;

public class PhoneNormalizerTests
{
    // --- Normalize ---

    [Theory]
    [InlineData("5551234567",       "5551234567")] // plain 10-digit — unchanged
    [InlineData("0412 345 678",     "0412345678")] // spaces stripped
    [InlineData("(555) 123-4567",   "5551234567")] // parens and dash stripped
    [InlineData("+1 (555) 000-0099","5550000099")] // +1 country code stripped
    [InlineData("15551234567",      "5551234567")] // 11-digit starting with 1 stripped
    [InlineData("61412222010",      "61412222010")] // 11-digit not starting with 1 — preserved
    public void Normalize_produces_canonical_digits(string raw, string expected)
    {
        Assert.Equal(expected, PhoneNormalizer.Normalize(raw));
    }

    [Fact]
    public void Normalize_empty_input_returns_empty()
    {
        Assert.Equal(string.Empty, PhoneNormalizer.Normalize(""));
    }

    // --- IsValidLength ---

    [Fact]
    public void IsValidLength_true_for_exactly_10_digits()
    {
        Assert.True(PhoneNormalizer.IsValidLength("5551234567"));
    }

    [Theory]
    [InlineData("")]            // 0 digits
    [InlineData("555123456")]   // 9 digits
    [InlineData("55512345678")] // 11 digits
    [InlineData("555123")]      // 6 digits
    public void IsValidLength_false_for_non_10_digits(string canonical)
    {
        Assert.False(PhoneNormalizer.IsValidLength(canonical));
    }
}
