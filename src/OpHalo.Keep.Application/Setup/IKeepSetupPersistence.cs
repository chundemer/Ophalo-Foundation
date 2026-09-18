using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Core.Entities;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Setup;

public interface IKeepSetupPersistence
{
    Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid accountUserId, CancellationToken ct);
    Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid accountId, CancellationToken ct);
    Task<string?> GetActorDisplayNameAsync(Guid accountUserId, CancellationToken ct);
    Task<(Account account, KeepBusinessProfile? profile)> GetProfileDataAsync(Guid accountId, CancellationToken ct);
    Task<KeepResponsePolicy?> GetPolicyAsync(Guid accountId, CancellationToken ct);

    /// <summary>
    /// Persists <paramref name="account"/> (already staged with its non-timezone profile
    /// changes) and <paramref name="profile"/> together with a governed timezone change, all in
    /// one atomic transaction (ADR-505 batch 4c): if <paramref name="timeZone"/> differs from the
    /// account's current value, it is IANA-validated, every currently-staffed-hours target is
    /// re-preflighted against it, and a settings audit row is appended — a rejection fails the
    /// whole save, including the already-staged business-name/profile fields, so a partial apply
    /// (timezone changed but the rest of the profile not saved, or vice versa) is impossible.
    /// </summary>
    Task<Result> SaveProfileWithTimeZoneAsync(
        Account account,
        KeepBusinessProfile profile,
        KeepProductOpsEvent? opsEvent,
        Guid actorAccountUserId,
        string actorDisplayName,
        string timeZone,
        DateTime occurredAtUtc,
        CancellationToken ct);

    Task SavePolicyAsync(KeepResponsePolicy policy, bool isNew, KeepProductOpsEvent? opsEvent, CancellationToken ct);
}
