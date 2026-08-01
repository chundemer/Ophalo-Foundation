using Microsoft.AspNetCore.Http;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Api.Keep;

/// <summary>
/// Strict parser for the <c>X-Keep-CatalogCategory-Version</c> optimistic-concurrency header
/// (Session 2b.3), mirroring <see cref="CatalogItemVersionHeader"/> for the CatalogCategory-scoped
/// header (ADR-330–335, DEF-074).
///
/// Contract:
///   - the header must be present exactly once (a single header line, no comma-combined value);
///   - the value is trimmed and must match the canonical GUID "D" shape
///     (8-4-4-4-12, no braces/parentheses, no quotes, no wildcard);
///   - <see cref="System.Guid.Empty"/> is rejected;
///   - an absent header returns <see cref="CatalogCategoryErrors.ExpectedVersionRequired"/>;
///   - any present-but-unusable value returns <see cref="CatalogCategoryErrors.ExpectedVersionInvalid"/>.
///
/// Create does not carry the header.
/// </summary>
public static class CatalogCategoryVersionHeader
{
    public const string HeaderName = "X-Keep-CatalogCategory-Version";

    public static Result<Guid> Parse(IHeaderDictionary headers)
    {
        if (!headers.TryGetValue(HeaderName, out var values))
            return Result<Guid>.Failure(CatalogCategoryErrors.ExpectedVersionRequired);

        if (values.Count != 1)
            return Result<Guid>.Failure(CatalogCategoryErrors.ExpectedVersionInvalid);

        var trimmed = (values[0] ?? string.Empty).Trim();

        if (!Guid.TryParseExact(trimmed, "D", out var version) || version == Guid.Empty)
            return Result<Guid>.Failure(CatalogCategoryErrors.ExpectedVersionInvalid);

        return Result<Guid>.Success(version);
    }
}
