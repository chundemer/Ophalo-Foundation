using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OpHalo.Api.Keep;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Pure-logic tests for the strict X-Keep-Request-Version parser (G5a/ADR-332).
/// No host or database is required — the parser only inspects an IHeaderDictionary.
///
/// Contract: an absent header is ExpectedVersionRequired; everything present-but-unusable
/// (blank, malformed, empty GUID, duplicate/comma-combined, wildcard, quoted, braced) is
/// ExpectedVersionInvalid; a single canonical GUID-D value succeeds and trims surrounding
/// whitespace.
/// </summary>
public class KeepRequestVersionHeaderTests
{
    private static IHeaderDictionary HeadersWith(params string[]? values) =>
        values is null
            ? new HeaderDictionary()
            : new HeaderDictionary
            {
                [KeepRequestVersionHeader.HeaderName] = new StringValues(values)
            };

    [Fact]
    public void Absent_header_is_required()
    {
        var result = KeepRequestVersionHeader.Parse(new HeaderDictionary());

        Assert.True(result.IsFailure);
        Assert.Equal("KeepRequest.ExpectedVersionRequired", result.Error.Code);
    }

    [Fact]
    public void Valid_single_guid_d_value_succeeds()
    {
        var expected = Guid.NewGuid();

        var result = KeepRequestVersionHeader.Parse(HeadersWith(expected.ToString("D")));

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed()
    {
        var expected = Guid.NewGuid();

        var result = KeepRequestVersionHeader.Parse(HeadersWith($"  {expected:D}\t"));

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("")]                                              // blank
    [InlineData("   ")]                                           // whitespace only
    [InlineData("not-a-guid")]                                    // malformed
    [InlineData("00000000-0000-0000-0000-000000000000")]         // Guid.Empty rejected
    [InlineData("*")]                                             // wildcard
    [InlineData("\"11111111-1111-1111-1111-111111111111\"")]     // quoted
    [InlineData("{11111111-1111-1111-1111-111111111111}")]       // braced ("B" shape)
    [InlineData("(11111111-1111-1111-1111-111111111111)")]       // parenthesized ("P" shape)
    [InlineData("11111111111111111111111111111111")]             // "N" shape (no dashes)
    [InlineData("11111111-1111-1111-1111-111111111111,22222222-2222-2222-2222-222222222222")] // comma-combined
    public void Present_but_unusable_value_is_invalid(string raw)
    {
        var result = KeepRequestVersionHeader.Parse(HeadersWith(raw));

        Assert.True(result.IsFailure);
        Assert.Equal("KeepRequest.ExpectedVersionInvalid", result.Error.Code);
    }

    [Fact]
    public void Duplicate_header_lines_are_invalid()
    {
        var result = KeepRequestVersionHeader.Parse(HeadersWith(
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D")));

        Assert.True(result.IsFailure);
        Assert.Equal("KeepRequest.ExpectedVersionInvalid", result.Error.Code);
    }
}
