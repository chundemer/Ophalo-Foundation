using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Setup;

/// <summary>
/// Owns the entire GAP-100/ADR-505 settings-mutation transaction as one atomic boundary, matching
/// <c>EfCatalogItemCreateAndActivatePersistence</c>/<c>EfPriceBookPublishPersistence</c>: loads the
/// policy and calendar, validates the complete prospective cross-aggregate state (staffed-hours
/// weekly-interval requirement, five-year reachability), writes, and appends audit rows — all
/// inside one <c>Serializable</c> transaction so two concurrent settings changes cannot each
/// validate against a stale counterpart and commit an invalid combined state.
/// </summary>
public interface IKeepResponsePolicyPersistence
{
    Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid accountUserId, CancellationToken ct);
    Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid accountId, CancellationToken ct);
    Task<string?> GetActorDisplayNameAsync(Guid accountUserId, CancellationToken ct);

    /// <summary>
    /// A null timing basis means "omitted": the persisted basis is preserved (ADR-506). The
    /// <paramref name="expectedSettingsVersion"/> must equal the version recomputed inside the
    /// transaction, otherwise the write is rejected with <c>SettingsVersionMismatch</c>.
    /// </summary>
    Task<Result> UpdatePolicyTargetsAsync(
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        int firstResponseTargetMinutes,
        int standardResponseTargetMinutes,
        int priorityResponseTargetMinutes,
        int statusCheckThresholdDays,
        ResponseTimingBasis? firstResponseTimingBasis,
        ResponseTimingBasis? standardResponseTimingBasis,
        ResponseTimingBasis? priorityResponseTimingBasis,
        string? expectedSettingsVersion,
        DateTime occurredAtUtc,
        CancellationToken ct);

    Task<Result> UpdateCalendarAsync(
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        IReadOnlyList<(DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)> weeklyIntervals,
        IReadOnlyList<DateOnly> closureDatesToAdd,
        IReadOnlyList<DateOnly> closureDatesToRemove,
        string? expectedSettingsVersion,
        DateTime occurredAtUtc,
        CancellationToken ct);

    /// <summary>
    /// Governed ADR-505 timezone-change entry point: validates the IANA identifier, re-preflights
    /// every currently-staffed-hours target's five-year reachability against the new zone, and
    /// audits the change. The account's existing <c>UpdateProfile</c> path (business name +
    /// timezone) does not go through this method and is a known bypass of this governance —
    /// tracked, not silently accepted as equivalent.
    /// </summary>
    Task<Result> UpdateTimeZoneAsync(
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        string timeZone,
        DateTime occurredAtUtc,
        CancellationToken ct);
}
