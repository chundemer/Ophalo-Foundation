using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OpHalo.Api.Keep;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Pure-logic tests for the strict X-Keep-OfferingAssembly-Version parser (Session 3.2a.1).
/// No host or database is required — the parser only inspects an IHeaderDictionary. Mirrors
/// <see cref="CatalogItemVersionHeaderTests"/> for the OfferingAssembly-scoped header.
/// </summary>
public class OfferingAssemblyVersionHeaderTests
{
    private static IHeaderDictionary HeadersWith(params string[]? values) =>
        values is null
            ? new HeaderDictionary()
            : new HeaderDictionary
            {
                [OfferingAssemblyVersionHeader.HeaderName] = new StringValues(values)
            };

    [Fact]
    public void Absent_header_is_required()
    {
        var result = OfferingAssemblyVersionHeader.Parse(new HeaderDictionary());

        Assert.True(result.IsFailure);
        Assert.Equal("OfferingAssembly.ExpectedVersionRequired", result.Error.Code);
    }

    [Fact]
    public void Valid_single_guid_d_value_succeeds()
    {
        var expected = Guid.NewGuid();

        var result = OfferingAssemblyVersionHeader.Parse(HeadersWith(expected.ToString("D")));

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("{11111111-1111-1111-1111-111111111111}")]
    public void Present_but_unusable_value_is_invalid(string raw)
    {
        var result = OfferingAssemblyVersionHeader.Parse(HeadersWith(raw));

        Assert.True(result.IsFailure);
        Assert.Equal("OfferingAssembly.ExpectedVersionInvalid", result.Error.Code);
    }

    [Fact]
    public void Duplicate_header_lines_are_invalid()
    {
        var result = OfferingAssemblyVersionHeader.Parse(HeadersWith(
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D")));

        Assert.True(result.IsFailure);
        Assert.Equal("OfferingAssembly.ExpectedVersionInvalid", result.Error.Code);
    }
}
