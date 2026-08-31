using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.IntakeSetup;

public interface IKeepIntakeSmsHandoffPersistence
{
    Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid accountUserId, CancellationToken ct);
    Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid accountId, CancellationToken ct);
    Task<KeepPublicIntakeLink?> FindActiveLinkByAccountAsync(Guid accountId, CancellationToken ct);

    /// <summary>
    /// Server-derived sender/business identity for the handoff message. Never sourced from
    /// the browser request. Returns null when the account/user context cannot be resolved.
    /// </summary>
    Task<IntakeSmsHandoffSenderContext?> GetSenderContextAsync(Guid accountId, Guid accountUserId, CancellationToken ct);

    Task CreateAsync(KeepIntakeSmsHandoff handoff, CancellationToken ct);
    Task<KeepIntakeSmsHandoffLookupResult?> FindValidByHashAsync(string tokenHash, DateTime nowUtc, CancellationToken ct);
}

public sealed record KeepIntakeSmsHandoffLookupResult(string CustomerPhone, string MessageBody, DateTime ExpiresAtUtc);

/// <summary>
/// Staff display name and business identity used to compose the handoff SMS.
/// <paramref name="ConfiguredPublicBusinessPhone"/> is the KeepBusinessProfile customer-facing
/// phone, or null when none is configured. Never a staff member's personal phone or email.
/// </summary>
public sealed record IntakeSmsHandoffSenderContext(
    string StaffDisplayName,
    string BusinessName,
    string? ConfiguredPublicBusinessPhone);
