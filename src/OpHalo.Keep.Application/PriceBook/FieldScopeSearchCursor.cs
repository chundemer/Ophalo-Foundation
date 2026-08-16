using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpHalo.Keep.Application.Requests;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// Encodes and decodes the merged polymorphic field-scope search cursor (build-log/121, ADR-486).
/// Unlike <see cref="CatalogItemListCursor"/>/<see cref="OfferingAssemblyListCursor"/>, this cursor
/// carries a resume position for *two* independent raw streams (catalog items and assemblies), so
/// <see cref="FieldScopeSearchApiService"/> can resume merging from exactly where the previous page
/// stopped without duplicating or skipping a row in either stream. Each stream is re-queried from
/// its stored position on every request (a position at/near the true end simply yields an empty or
/// short result again — cheap and safe), so no separate "exhausted" flag is persisted. The assembly
/// position is always the last row actually returned on a page, never a raw scan-ahead point — see
/// <see cref="FieldScopeSearchApiService"/>'s remarks for why the distinction matters.
/// </summary>
public static class FieldScopeSearchCursor
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Encode(
        IKeepRequestListCursorProtector protector,
        string queryFingerprint,
        FieldScopeSearchCursorState state)
    {
        var payload = new FieldScopeSearchCursorPayload(
            CurrentVersion,
            queryFingerprint,
            state.CatalogPosition is null ? null : (int)state.CatalogPosition.Rank,
            state.CatalogPosition?.DisplayName,
            state.CatalogPosition?.LastId,
            state.AssemblyPosition is null ? null : (int)state.AssemblyPosition.Rank,
            state.AssemblyPosition?.Name,
            state.AssemblyPosition?.LastId);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return protector.Protect(json);
    }

    public static bool TryDecode(
        IKeepRequestListCursorProtector protector,
        string cursor,
        string expectedFingerprint,
        out FieldScopeSearchCursorState? state)
    {
        state = null;
        if (!protector.TryUnprotect(cursor, out var json) || json is null)
            return false;
        try
        {
            var payload = JsonSerializer.Deserialize<FieldScopeSearchCursorPayload>(json, JsonOptions);
            if (payload is null || payload.Version != CurrentVersion) return false;
            if (payload.Fingerprint != expectedFingerprint) return false;

            CatalogItemListCursorPosition? catalogPosition = null;
            if (payload.CatalogRank is not null)
            {
                if (!Enum.IsDefined((CatalogItemMatchRank)payload.CatalogRank.Value)) return false;
                if (payload.CatalogDisplayName is null || payload.CatalogLastId is null) return false;
                catalogPosition = new CatalogItemListCursorPosition(
                    (CatalogItemMatchRank)payload.CatalogRank.Value, payload.CatalogDisplayName, payload.CatalogLastId.Value);
            }

            OfferingAssemblySearchCursorPosition? assemblyPosition = null;
            if (payload.AssemblyRank is not null)
            {
                if (!Enum.IsDefined((CatalogItemMatchRank)payload.AssemblyRank.Value)) return false;
                if (payload.AssemblyName is null || payload.AssemblyLastId is null) return false;
                assemblyPosition = new OfferingAssemblySearchCursorPosition(
                    (CatalogItemMatchRank)payload.AssemblyRank.Value, payload.AssemblyName, payload.AssemblyLastId.Value);
            }

            state = new FieldScopeSearchCursorState(catalogPosition, assemblyPosition);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>SHA-256 fingerprint of the normalized search term — the only scope-affecting
    /// filter this endpoint has. Excludes limit and cursor, so a cursor issued for one query is
    /// rejected when reused with a different one.</summary>
    public static string ComputeFingerprint(string searchTerm)
    {
        var canonical = new { search = searchTerm.Trim().ToLowerInvariant() };
        var json = JsonSerializer.Serialize(canonical, JsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>Decoded/pre-encode cursor state: each stream's resume position, null meaning start.</summary>
public sealed record FieldScopeSearchCursorState(
    CatalogItemListCursorPosition? CatalogPosition,
    OfferingAssemblySearchCursorPosition? AssemblyPosition);

public sealed record FieldScopeSearchCursorPayload(
    int Version,
    string Fingerprint,
    int? CatalogRank,
    string? CatalogDisplayName,
    Guid? CatalogLastId,
    int? AssemblyRank,
    string? AssemblyName,
    Guid? AssemblyLastId);
