using OpHalo.Keep.Core.Domain;

namespace OpHalo.UnitTests.Keep;

public class PhoneDisplayFormatterTests
{
    // --- Format ---

    [Theory]
    [InlineData("5551234567",        "(555) 123-4567")] // canonical 10-digit
    [InlineData("(555) 123-4567",    "(555) 123-4567")] // already formatted — re-normalized
    [InlineData("+1 (555) 000-0099", "(555) 000-0099")] // +1 country code normalized then formatted
    [InlineData("1-555-000-0099",    "(555) 000-0099")] // leading 1 dropped then formatted
    public void Format_renders_canonical_north_american_numbers(string raw, string expected)
    {
        Assert.Equal(expected, PhoneDisplayFormatter.Format(raw));
    }

    [Theory]
    [InlineData("+61412345678",   "+61412345678")]  // international — returned trimmed, untouched
    [InlineData("555-1234",       "555-1234")]      // partial number — untouched
    [InlineData("5551234567x12",  "5551234567x12")] // extension — not canonical, untouched
    [InlineData("  555 000 123  ", "555 000 123")]  // non-canonical digit count — trimmed only
    public void Format_passes_through_non_canonical_values_trimmed(string raw, string expected)
    {
        Assert.Equal(expected, PhoneDisplayFormatter.Format(raw));
    }

    // --- FormatConfigured ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatConfigured_returns_null_for_unset_values(string? raw)
    {
        Assert.Null(PhoneDisplayFormatter.FormatConfigured(raw));
    }

    [Fact]
    public void FormatConfigured_formats_a_canonical_configured_number()
    {
        Assert.Equal("(555) 010-0199", PhoneDisplayFormatter.FormatConfigured("5550100199"));
    }

    [Fact]
    public void FormatConfigured_passes_through_a_non_canonical_configured_number()
    {
        Assert.Equal("+61412345678", PhoneDisplayFormatter.FormatConfigured("+61412345678"));
    }
}
