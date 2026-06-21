using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Api.Keep;

/// <summary>
/// Parses and structurally validates GET /keep/requests/available query parameters.
/// Accepts only <c>limit</c> and <c>cursor</c>; rejects unknown and duplicate parameters.
/// Range validation for limit is deferred to the service (1–50).
/// </summary>
public static class KeepAvailableRequestQueryBinding
{
    private static readonly HashSet<string> KnownParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "limit", "cursor"
    };

    public static Result<KeepAvailableRequestQuery> Bind(IQueryCollection query)
    {
        var normalized = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in query.Keys)
        {
            if (!normalized.TryAdd(key, query[key]))
                return Result<KeepAvailableRequestQuery>.Failure(KeepRequestErrors.RequestListDuplicateParameter);
        }

        foreach (var key in normalized.Keys)
        {
            if (!KnownParams.Contains(key))
                return Result<KeepAvailableRequestQuery>.Failure(KeepRequestErrors.RequestListUnknownParameter);
        }

        foreach (var (_, values) in normalized)
        {
            if (values.Count > 1)
                return Result<KeepAvailableRequestQuery>.Failure(KeepRequestErrors.RequestListDuplicateParameter);
        }

        int? limit = null;
        if (normalized.TryGetValue("limit", out var limitVals))
        {
            if (!int.TryParse(limitVals[0], out var parsed))
                return Result<KeepAvailableRequestQuery>.Failure(KeepRequestErrors.RequestListInvalidLimit);
            limit = parsed;
        }

        return Result<KeepAvailableRequestQuery>.Success(new KeepAvailableRequestQuery(
            Limit:  limit,
            Cursor: Get(normalized, "cursor")));
    }

    private static string? Get(Dictionary<string, StringValues> d, string key) =>
        d.TryGetValue(key, out var v) ? v.FirstOrDefault() : null;
}
