namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// Resolves the account's IANA timezone for business-day computations (ADR-451). Narrow by
/// design: only LogExternalContactService needs this today.
/// </summary>
public interface IKeepAccountTimeZoneLookup
{
    Task<string?> GetAccountTimeZoneAsync(Guid accountId, CancellationToken ct);
}
