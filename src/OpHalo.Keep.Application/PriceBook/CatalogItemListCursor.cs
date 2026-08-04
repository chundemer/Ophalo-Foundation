using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpHalo.Keep.Application.Requests;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// Encodes and decodes cursor tokens for the catalog list's keyset pagination (Session 2e.3,
/// build-log/113). Reuses <see cref="IKeepRequestListCursorProtector"/> — its HMAC-SHA256 signing
/// is generic string signing with no KeepRequest-specific payload knowledge, so it is safe to
/// share across list features rather than duplicating a second signing implementation.
/// </summary>
public static class CatalogItemListCursor
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Encode(
        IKeepRequestListCursorProtector protector,
        string queryFingerprint,
        CatalogItemListCursorPosition position)
    {
        var payload = new CatalogItemListCursorPayload(
            CurrentVersion, queryFingerprint, (int)position.Rank, position.DisplayName, position.LastId);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return protector.Protect(json);
    }

    public static bool TryDecode(
        IKeepRequestListCursorProtector protector,
        string cursor,
        string expectedFingerprint,
        out CatalogItemListCursorPosition? position)
    {
        position = null;
        if (!protector.TryUnprotect(cursor, out var json) || json is null)
            return false;
        try
        {
            var payload = JsonSerializer.Deserialize<CatalogItemListCursorPayload>(json, JsonOptions);
            if (payload is null || payload.Version != CurrentVersion) return false;
            if (payload.Fingerprint != expectedFingerprint) return false;
            if (!Enum.IsDefined((CatalogItemMatchRank)payload.Rank)) return false;

            position = new CatalogItemListCursorPosition(
                (CatalogItemMatchRank)payload.Rank, payload.DisplayName, payload.LastId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// SHA-256 fingerprint of every scope-affecting filter (search, type, category, status).
    /// Excludes limit and cursor, so equivalent filter combinations produce the same fingerprint
    /// and a cursor issued for one query is rejected when reused with a different one.
    /// </summary>
    public static string ComputeFingerprint(CatalogItemListFilters filters)
    {
        var canonical = new
        {
            search = (filters.SearchTerm ?? string.Empty).Trim().ToLowerInvariant(),
            type = filters.Type?.ToString() ?? string.Empty,
            categoryId = filters.CategoryId?.ToString() ?? string.Empty,
            status = filters.ActiveState.ToString(),
        };
        var json = JsonSerializer.Serialize(canonical, JsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed record CatalogItemListCursorPayload(
    int Version,
    string Fingerprint,
    int Rank,
    string DisplayName,
    Guid LastId);
