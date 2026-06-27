using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.Setup;

public interface IKeepSetupPersistence
{
    Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid accountUserId, CancellationToken ct);
    Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid accountId, CancellationToken ct);
    Task<(Account account, KeepBusinessProfile? profile)> GetProfileDataAsync(Guid accountId, CancellationToken ct);
    Task<KeepResponsePolicy?> GetPolicyAsync(Guid accountId, CancellationToken ct);
    Task SaveProfileAsync(Account account, KeepBusinessProfile profile, CancellationToken ct);
    Task SavePolicyAsync(KeepResponsePolicy policy, bool isNew, CancellationToken ct);
}
