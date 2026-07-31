using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OpHalo.Api.Keep;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Pure-logic tests for the strict X-Keep-CatalogItem-Version parser (Session 2a.2).
/// No host or database is required — the parser only inspects an IHeaderDictionary.
/// Mirrors <see cref="KeepRequestVersionHeaderTests"/> for the CatalogItem-scoped header.
/// </summary>
public class CatalogItemVersionHeaderTests
{
    private static IHeaderDictionary HeadersWith(params string[]? values) =>
        values is null
            ? new HeaderDictionary()
            : new HeaderDictionary
            {
                [CatalogItemVersionHeader.HeaderName] = new StringValues(values)
            };

    [Fact]
    public void Absent_header_is_required()
    {
        var result = CatalogItemVersionHeader.Parse(new HeaderDictionary());

        Assert.True(result.IsFailure);
        Assert.Equal("CatalogItem.ExpectedVersionRequired", result.Error.Code);
    }

    [Fact]
    public void Valid_single_guid_d_value_succeeds()
    {
        var expected = Guid.NewGuid();

        var result = CatalogItemVersionHeader.Parse(HeadersWith(expected.ToString("D")));

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
        var result = CatalogItemVersionHeader.Parse(HeadersWith(raw));

        Assert.True(result.IsFailure);
        Assert.Equal("CatalogItem.ExpectedVersionInvalid", result.Error.Code);
    }

    [Fact]
    public void Duplicate_header_lines_are_invalid()
    {
        var result = CatalogItemVersionHeader.Parse(HeadersWith(
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D")));

        Assert.True(result.IsFailure);
        Assert.Equal("CatalogItem.ExpectedVersionInvalid", result.Error.Code);
    }
}
