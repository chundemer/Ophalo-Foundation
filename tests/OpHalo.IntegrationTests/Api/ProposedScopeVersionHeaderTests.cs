using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OpHalo.Api.Keep;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Pure-logic tests for the strict X-Keep-ProposedScope-Version parser (Session 3.3b). No host or
/// database is required — the parser only inspects an IHeaderDictionary. Mirrors
/// <see cref="OfferingAssemblyVersionHeaderTests"/> for the ProposedScope-scoped header.
/// </summary>
public class ProposedScopeVersionHeaderTests
{
    private static IHeaderDictionary HeadersWith(params string[]? values) =>
        values is null
            ? new HeaderDictionary()
            : new HeaderDictionary
            {
                [ProposedScopeVersionHeader.HeaderName] = new StringValues(values)
            };

    [Fact]
    public void Absent_header_is_required()
    {
        var result = ProposedScopeVersionHeader.Parse(new HeaderDictionary());

        Assert.True(result.IsFailure);
        Assert.Equal("ProposedScope.ExpectedVersionRequired", result.Error.Code);
    }

    [Fact]
    public void Valid_single_guid_d_value_succeeds()
    {
        var expected = Guid.NewGuid();

        var result = ProposedScopeVersionHeader.Parse(HeadersWith(expected.ToString("D")));

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
        var result = ProposedScopeVersionHeader.Parse(HeadersWith(raw));

        Assert.True(result.IsFailure);
        Assert.Equal("ProposedScope.ExpectedVersionInvalid", result.Error.Code);
    }

    [Fact]
    public void Duplicate_header_lines_are_invalid()
    {
        var result = ProposedScopeVersionHeader.Parse(HeadersWith(
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D")));

        Assert.True(result.IsFailure);
        Assert.Equal("ProposedScope.ExpectedVersionInvalid", result.Error.Code);
    }
}
