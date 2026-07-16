using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.IntakeSetup;

public interface IKeepIntakeSmsHandoffPersistence
{
    Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid accountUserId, CancellationToken ct);
    Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid accountId, CancellationToken ct);
    Task<KeepPublicIntakeLink?> FindActiveLinkByAccountAsync(Guid accountId, CancellationToken ct);
    Task CreateAsync(KeepIntakeSmsHandoff handoff, CancellationToken ct);
    Task<KeepIntakeSmsHandoffLookupResult?> FindValidByHashAsync(string tokenHash, DateTime nowUtc, CancellationToken ct);
}

public sealed record KeepIntakeSmsHandoffLookupResult(string MessageBody, DateTime ExpiresAtUtc);
