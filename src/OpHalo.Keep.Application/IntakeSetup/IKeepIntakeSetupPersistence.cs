using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.IntakeSetup;

public interface IKeepIntakeSetupPersistence
{
    Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid accountUserId, CancellationToken ct);
    Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid accountId, CancellationToken ct);
    Task<string?> GetAccountBusinessNameAsync(Guid accountId, CancellationToken ct);
    Task<KeepPublicIntakeLink?> FindActiveLinkByAccountAsync(Guid accountId, CancellationToken ct);
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct);
    Task<EnsureIntakeLinkCommitResult> CommitEnsureAsync(KeepPublicIntakeLink link, CancellationToken ct);
    Task CommitReplaceAsync(KeepPublicIntakeLink oldLink, KeepPublicIntakeLink newLink, CancellationToken ct);
}

public enum EnsureIntakeLinkCommitResult { Created = 1, AlreadyExists = 2, SlugCollision = 3 }
