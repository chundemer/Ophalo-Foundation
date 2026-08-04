using OpHalo.Keep.Core.Domain;

namespace OpHalo.UnitTests.Keep;

public class SkuNormalizerTests
{
    [Theory]
    [InlineData("cop34",   "cop34")]
    [InlineData("COP-34",  "cop34")]
    [InlineData("cop 34",  "cop34")]
    [InlineData(" COP_34 ","cop34")]
    [InlineData("CP20",    "cp20")]
    public void Normalize_strips_punctuation_and_whitespace_and_lowercases(string raw, string expected)
    {
        Assert.Equal(expected, SkuNormalizer.Normalize(raw));
    }

    [Theory]
    [InlineData("---")]
    [InlineData("___")]
    [InlineData("!!!")]
    [InlineData("")]
    public void Normalize_of_no_alphanumeric_input_returns_empty(string raw)
    {
        Assert.Equal(string.Empty, SkuNormalizer.Normalize(raw));
    }

    [Fact]
    public void Normalize_ignores_non_ascii_letters()
    {
        Assert.Equal("caf", SkuNormalizer.Normalize("café"));
    }
}
