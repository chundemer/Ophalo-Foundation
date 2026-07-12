using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.Requests;

public interface IKeepSmsHandoffPersistence
{
    Task CreateAsync(KeepSmsHandoff handoff, CancellationToken ct);

    /// <summary>
    /// Returns the phone and message for a valid (non-expired) handoff token, or null if the
    /// token hash is not found or the record has expired. Expired/invalid cases are
    /// intentionally indistinguishable to callers.
    /// </summary>
    Task<KeepSmsHandoffLookupResult?> FindValidByHashAsync(string tokenHash, DateTime nowUtc, CancellationToken ct);
}

public sealed record KeepSmsHandoffLookupResult(string CustomerPhone, string MessageBody, DateTime ExpiresAtUtc);
