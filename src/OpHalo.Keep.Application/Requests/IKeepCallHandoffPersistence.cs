using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.Requests;

public interface IKeepCallHandoffPersistence
{
    Task CreateAsync(KeepCallHandoff handoff, CancellationToken ct);

    /// <summary>
    /// Returns the phone for a valid (non-expired) handoff token, or null if the token hash
    /// is not found or the record has expired. Expired/invalid cases are intentionally
    /// indistinguishable to callers.
    /// </summary>
    Task<KeepCallHandoffLookupResult?> FindValidByHashAsync(string tokenHash, DateTime nowUtc, CancellationToken ct);
}

public sealed record KeepCallHandoffLookupResult(string CustomerPhone, DateTime ExpiresAtUtc);
