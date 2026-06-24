using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Api.Keep;

/// <summary>
/// Parses and structurally validates GET /keep/requests query parameters.
/// Returns a typed failure so Program.cs stays thin (ADR-257).
///
/// Responsibilities:
///   - reject unknown parameters;
///   - reject duplicate scalar parameters (including same name with different casing);
///   - parse non-integer limit (service enforces the 1–100 range);
///   - parse invalid-GUID assignedAccountUserId;
///   - pass all other values through as raw strings for the service to validate semantically.
/// </summary>
public static class KeepRequestListQueryBinding
{
    private static readonly HashSet<string> KnownParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "view", "status", "attentionReason", "assignedAccountUserId",
        "q", "createdFrom", "createdTo", "closedFrom", "closedTo", "closedShortcut",
        "limit", "cursor"
    };

    public static Result<KeepRequestListQuery> Bind(IQueryCollection query)
    {
        // Normalize all query keys into a case-insensitive dictionary.
        // This catches same-logical-param with different casing (?view=a&View=b)
        // which IQueryCollection may present as separate case-distinct keys.
        var normalized = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in query.Keys)
        {
            if (!normalized.TryAdd(key, query[key]))
                return Result<KeepRequestListQuery>.Failure(KeepRequestErrors.RequestListDuplicateParameter);
        }

        // Unknown parameters — ADR-257: do not silently ignore unknown query params.
        foreach (var key in normalized.Keys)
        {
            if (!KnownParams.Contains(key))
                return Result<KeepRequestListQuery>.Failure(KeepRequestErrors.RequestListUnknownParameter);
        }

        // Duplicate scalar parameters — all list query params are scalar (1 value each).
        // Catches ?limit=10&limit=20 which the query parser merges into StringValues(Count=2).
        foreach (var (_, values) in normalized)
        {
            if (values.Count > 1)
                return Result<KeepRequestListQuery>.Failure(KeepRequestErrors.RequestListDuplicateParameter);
        }

        // Parse limit: non-integer (including empty string) returns InvalidLimit.
        // Range check is deferred to the service.
        int? limit = null;
        if (normalized.TryGetValue("limit", out var limitVals))
        {
            if (!int.TryParse(limitVals[0], out var parsed))
                return Result<KeepRequestListQuery>.Failure(KeepRequestErrors.RequestListInvalidLimit);
            limit = parsed;
        }

        // Parse assignedAccountUserId: malformed value (including empty string) is a type
        // error distinct from "filter not yet available" — surfaces in 4A and 4B.
        Guid? assignedUserId = null;
        if (normalized.TryGetValue("assignedAccountUserId", out var assignedVals))
        {
            if (!Guid.TryParse(assignedVals[0], out var parsed))
                return Result<KeepRequestListQuery>.Failure(
                    KeepRequestErrors.RequestListInvalidAssignedAccountUserId);
            assignedUserId = parsed;
        }

        return Result<KeepRequestListQuery>.Success(new KeepRequestListQuery(
            View:                   Get(normalized, "view"),
            Status:                 Get(normalized, "status"),
            AttentionReason:        Get(normalized, "attentionReason"),
            AssignedAccountUserId:  assignedUserId,
            Q:                      Get(normalized, "q"),
            CreatedFrom:            Get(normalized, "createdFrom"),
            CreatedTo:              Get(normalized, "createdTo"),
            ClosedFrom:             Get(normalized, "closedFrom"),
            ClosedTo:               Get(normalized, "closedTo"),
            ClosedShortcut:         Get(normalized, "closedShortcut"),
            Limit:                  limit,
            Cursor:                 Get(normalized, "cursor")));
    }

    private static string? Get(Dictionary<string, StringValues> d, string key) =>
        d.TryGetValue(key, out var v) ? v.FirstOrDefault() : null;
}
