using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// Encodes and decodes cursor tokens for keyset-based list pagination.
/// Tokens are signed by <see cref="IKeepRequestListCursorProtector"/> (HMAC-SHA256)
/// and carry a SHA-256 fingerprint of the query shape so a cursor issued for one
/// query is rejected when reused with a different one (ADR-257).
/// </summary>
public static class KeepRequestListCursor
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions FingerprintJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Encode a cursor for the last row of a page.
    /// <paramref name="secondaryDescending"/> is true for ranking groups 6–8
    /// (LastBusinessActivityAtUtc DESC); false for groups 1–5 (ascending nullable date).
    /// </summary>
    public static string Encode(
        IKeepRequestListCursorProtector protector,
        string queryFingerprint,
        Guid lastId,
        int rankingOrder,
        long? secondaryTick,
        bool secondaryDescending)
    {
        var payload = new KeepRequestListCursorPayload(
            CurrentVersion, queryFingerprint, lastId,
            rankingOrder, secondaryTick, secondaryDescending);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return protector.Protect(json);
    }

    /// <summary>
    /// Decode and validate a cursor token.
    /// Returns false if the token was tampered with, is malformed, has the wrong version,
    /// or its fingerprint does not match <paramref name="expectedFingerprint"/>.
    /// </summary>
    public static bool TryDecode(
        IKeepRequestListCursorProtector protector,
        string cursor,
        string expectedFingerprint,
        out KeepRequestListCursorPayload? payload)
    {
        payload = null;
        if (!protector.TryUnprotect(cursor, out var json) || json is null)
            return false;
        try
        {
            payload = JsonSerializer.Deserialize<KeepRequestListCursorPayload>(json, JsonOptions);
            if (payload is null || payload.Version != CurrentVersion) return false;
            if (payload.Fingerprint != expectedFingerprint) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// SHA-256 fingerprint of the query fields that determine which rows are in scope.
    /// Excludes <c>limit</c> and <c>cursor</c>. Values are normalized before hashing
    /// so equivalent queries produce the same fingerprint.
    /// </summary>
    public static string ComputeFingerprint(KeepRequestListQuery query)
    {
        var canonical = new
        {
            // Normalize null/blank/"default" to "default" so the no-view and view=default
            // fingerprints are identical — cursors remain valid across equivalent queries.
            view             = string.IsNullOrWhiteSpace(query.View) ? "default" : query.View.Trim().ToLowerInvariant(),
            status           = (query.Status ?? string.Empty).Trim().ToLowerInvariant(),
            attentionReason  = (query.AttentionReason ?? string.Empty).Trim().ToLowerInvariant(),
            assignedUserId   = query.AssignedAccountUserId?.ToString() ?? string.Empty,
            q                = (query.Q ?? string.Empty).Trim(),
            createdFrom      = (query.CreatedFrom ?? string.Empty).Trim(),
            createdTo        = (query.CreatedTo ?? string.Empty).Trim(),
            closedFrom       = (query.ClosedFrom ?? string.Empty).Trim(),
            closedTo         = (query.ClosedTo ?? string.Empty).Trim(),
            closedShortcut   = (query.ClosedShortcut ?? string.Empty).Trim().ToLowerInvariant()
        };
        var json = JsonSerializer.Serialize(canonical, FingerprintJsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// Payload embedded in a list cursor token.
/// Carries the full sort key of the last row on a page for correct keyset resumption
/// in both in-memory (Session 4A) and DB-level (Session 4B+) pagination.
/// </summary>
public sealed record KeepRequestListCursorPayload(
    int Version,
    string Fingerprint,
    Guid LastId,
    int RankingOrder,
    long? SecondaryTick,
    bool SecondaryDescending);
