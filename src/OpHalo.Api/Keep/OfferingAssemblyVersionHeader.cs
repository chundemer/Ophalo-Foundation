using Microsoft.AspNetCore.Http;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Api.Keep;

/// <summary>
/// Strict parser for the <c>X-Keep-OfferingAssembly-Version</c> optimistic-concurrency header
/// (Session 3.2a.1), matching the existing <c>X-Keep-*-Version</c> contract
/// (ADR-330-335, DEF-074; see <see cref="CatalogItemVersionHeader"/>). Parsing lives at the API
/// edge so Application/Core stay transport-neutral.
///
/// Contract:
///   - the header must be present exactly once (a single header line, no comma-combined value);
///   - the value is trimmed and must match the canonical GUID "D" shape
///     (8-4-4-4-12, no braces/parentheses, no quotes, no wildcard);
///   - <see cref="System.Guid.Empty"/> is rejected;
///   - an absent header returns <see cref="OfferingAssemblyErrors.ExpectedVersionRequired"/>;
///   - any present-but-unusable value returns <see cref="OfferingAssemblyErrors.ExpectedVersionInvalid"/>.
///
/// Creates do not carry the header.
/// </summary>
public static class OfferingAssemblyVersionHeader
{
    public const string HeaderName = "X-Keep-OfferingAssembly-Version";

    public static Result<Guid> Parse(IHeaderDictionary headers)
    {
        // Only an absent header is "Required". Anything present-but-unusable is "Invalid":
        // header presence is the sole discriminator between the two errors.
        if (!headers.TryGetValue(HeaderName, out var values))
            return Result<Guid>.Failure(OfferingAssemblyErrors.ExpectedVersionRequired);

        // Present with no single usable value: zero values (defensive), a duplicate/multi-line
        // header, or a blank value all fail closed as Invalid.
        if (values.Count != 1)
            return Result<Guid>.Failure(OfferingAssemblyErrors.ExpectedVersionInvalid);

        var trimmed = (values[0] ?? string.Empty).Trim();

        // Canonical "D" shape only — blank, braced, parenthesized, quoted, wildcard, or
        // comma-combined values do not parse and fail closed; Guid.Empty is rejected.
        if (!Guid.TryParseExact(trimmed, "D", out var version) || version == Guid.Empty)
            return Result<Guid>.Failure(OfferingAssemblyErrors.ExpectedVersionInvalid);

        return Result<Guid>.Success(version);
    }
}
