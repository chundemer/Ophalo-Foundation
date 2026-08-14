using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpHalo.Keep.Application.Requests;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// Encodes and decodes cursor tokens for the offering/assembly list's keyset pagination (Session
/// 3.2a.2). Reuses <see cref="IKeepRequestListCursorProtector"/> — its HMAC-SHA256 signing is
/// generic string signing with no KeepRequest-specific payload knowledge, so it is safe to share
/// across list features rather than duplicating a second signing implementation. Mirrors
/// <see cref="CatalogItemListCursor"/>, minus the match-rank field this list has no search term.
/// </summary>
public static class OfferingAssemblyListCursor
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Encode(
        IKeepRequestListCursorProtector protector,
        string queryFingerprint,
        OfferingAssemblyListCursorPosition position)
    {
        var payload = new OfferingAssemblyListCursorPayload(CurrentVersion, queryFingerprint, position.Name, position.LastId);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return protector.Protect(json);
    }

    public static bool TryDecode(
        IKeepRequestListCursorProtector protector,
        string cursor,
        string expectedFingerprint,
        out OfferingAssemblyListCursorPosition? position)
    {
        position = null;
        if (!protector.TryUnprotect(cursor, out var json) || json is null)
            return false;
        try
        {
            var payload = JsonSerializer.Deserialize<OfferingAssemblyListCursorPayload>(json, JsonOptions);
            if (payload is null || payload.Version != CurrentVersion) return false;
            if (payload.Fingerprint != expectedFingerprint) return false;

            position = new OfferingAssemblyListCursorPosition(payload.Name, payload.LastId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// SHA-256 fingerprint of every scope-affecting filter (status, plus the field-safe
    /// operationally-eligible scope discriminator — Session 3.4b). Excludes limit and cursor, so
    /// equivalent filter combinations produce the same fingerprint and a cursor issued for one
    /// query is rejected when reused with a different one.
    /// </summary>
    /// <param name="fieldOperationallyEligible">
    /// True only for <c>FieldOfferingAssemblyReadApiService</c>'s list (Session 3.4c). Both the
    /// Admin and field lists query the same raw <c>ActiveState.Active</c> SQL page (the field
    /// service applies its <c>IsOperationallyEligible</c> filter after the fetch, so it can't be
    /// expressed as a SQL predicate) — without this discriminator, an Admin <c>?status=Active</c>
    /// cursor and a field-list cursor would carry an identical fingerprint despite the two
    /// endpoints returning different result sets, letting a cursor minted on one endpoint resume
    /// pagination on the other and silently skip rows only that endpoint would have shown.
    /// </param>
    public static string ComputeFingerprint(OfferingAssemblyListFilters filters, bool fieldOperationallyEligible = false)
    {
        var canonical = new
        {
            status = filters.ActiveState?.ToString() ?? string.Empty,
            fieldOperationallyEligible,
        };
        var json = JsonSerializer.Serialize(canonical, JsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed record OfferingAssemblyListCursorPayload(int Version, string Fingerprint, string Name, Guid LastId);
