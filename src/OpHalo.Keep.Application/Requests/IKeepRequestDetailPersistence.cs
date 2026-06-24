using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// Persistence contract for the operator detail and customer page read services.
/// Intent-revealing by design: named methods over raw DbSets so Keep.Application stays EF-free.
/// Snapshot methods are intentionally duplicated from IKeepRequestListPersistence — no shared
/// base until a third service needs them (decision gate, 2026-06-16).
/// Implemented by EfKeepRequestDetailPersistence in Keep.Infrastructure.
/// </summary>
public interface IKeepRequestDetailPersistence
{
    Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid accountUserId, CancellationToken ct);

    Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid accountId, CancellationToken ct);

    /// <summary>
    /// Returns the request if it belongs to the given account and passes the visibility scope;
    /// null if not found, cross-account, or the caller lacks row access.
    /// Not-found and unauthorized are intentionally indistinguishable at this layer.
    /// </summary>
    Task<KeepRequest?> GetRequestAsync(
        Guid requestId,
        Guid accountId,
        Guid currentAccountUserId,
        KeepRequestVisibilityScope scope,
        CancellationToken ct);

    /// <summary>
    /// Returns all events for the request, ordered chronologically (ascending OccurredAtUtc).
    /// </summary>
    Task<IReadOnlyList<KeepRequestEvent>> GetAllEventsAsync(Guid requestId, CancellationToken ct);

    /// <summary>
    /// Returns all participants for the request (active and detached), joined with AccountUser
    /// for display name and role. DisplayName = nonblank User.Name when the account user has a
    /// linked user with a name; falls back to AccountUser.Email.
    /// </summary>
    Task<IReadOnlyList<KeepParticipantProjection>> GetParticipantsAsync(Guid requestId, CancellationToken ct);

    /// <summary>
    /// Returns the business name for the account, or null if the account does not exist.
    /// In practice this should never be null after auth succeeds.
    /// </summary>
    Task<string?> GetAccountBusinessNameAsync(Guid accountId, CancellationToken ct);

    /// <summary>
    /// Resolves a request and its account's business name by page token.
    /// Returns null if no request matches the token.
    /// </summary>
    Task<KeepRequestPageLookup?> GetRequestByPageTokenAsync(string pageToken, CancellationToken ct);

    /// <summary>
    /// Returns only Visibility = All events for the request, ordered chronologically.
    /// Customer page must not include Internal or System events.
    /// </summary>
    Task<IReadOnlyList<KeepRequestEvent>> GetCustomerVisibleEventsAsync(Guid requestId, CancellationToken ct);

    /// <summary>
    /// Returns request IDs in ready-to-close sort order for the given account: all
    /// Resolved + AttentionLevel.None rows ordered by coalesced last-activity DESC, Id ASC.
    /// This matches the B5 group-7 (resolved_quiet) ranking used by the list service.
    /// Used by the detail service to compute next/previous navigation.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetReadyToCloseNavigationIdsAsync(Guid accountId, CancellationToken ct);
}

/// <summary>
/// Participant data joined with AccountUser for operator display.
/// DisplayName = nonblank User.Name (when linked) else AccountUser.Email.
/// MembershipStatus is used to detect stale/ineligible participants.
/// </summary>
public sealed record KeepParticipantProjection(
    Guid AccountUserId,
    ParticipationType ParticipationType,
    bool NotificationsEnabled,
    DateTime AttachedAtUtc,
    DateTime? DetachedAtUtc,
    string DisplayName,
    AccountUserRole Role,
    MembershipStatus MembershipStatus);

/// <summary>
/// The minimal data the customer page service needs to resolve a request by page token.
/// </summary>
public sealed record KeepRequestPageLookup(KeepRequest Request, string BusinessName);
