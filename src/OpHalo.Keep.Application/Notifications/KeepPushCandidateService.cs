using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.Notifications;

/// <summary>
/// Lightweight DTO carrying participant eligibility info for routing decisions.
/// Populated from participant rows + account membership snapshot by the notifier caller.
/// </summary>
public sealed record KeepPushParticipantInfo(
    Guid AccountUserId,
    ParticipationType Type,
    bool IsActive,
    bool NotificationsEnabled,
    AccountUserRole Role,
    MembershipStatus MembershipStatus);

/// <summary>
/// Account members eligible for Owner/Admin fallback when no Responsible/Watcher is routable.
/// </summary>
public sealed record KeepPushMemberInfo(
    Guid AccountUserId,
    AccountUserRole Role,
    MembershipStatus MembershipStatus);

/// <summary>
/// All inputs needed to determine push delivery candidates for a single event (ADR-360).
/// </summary>
public sealed record KeepPushRoutingContext(
    Guid AccountId,
    Guid RequestId,
    KeepPushEventKind EventKind,
    Guid ActorAccountUserId,
    bool IsTerminal,
    bool IsOffSeason,
    IReadOnlyList<KeepPushParticipantInfo> Participants,
    IReadOnlyList<KeepPushMemberInfo> FallbackMembers);

/// <summary>
/// Pure routing/suppression service — no DB, no async (ADR-354/ADR-360).
/// Returns eligible recipient AccountUserIds in routing priority order.
/// </summary>
public sealed class KeepPushCandidateService
{
    /// <summary>
    /// Returns AccountUserIds that should receive a push for the given event context.
    /// Returns empty when the event is badge/list-only, the account is OffSeason,
    /// the request is terminal, or no eligible recipients remain after suppression.
    /// </summary>
    public IReadOnlyList<Guid> GetCandidates(KeepPushRoutingContext context)
    {
        if (context.IsOffSeason)
            return [];

        if (context.IsTerminal)
            return [];

        // Step 1: eligible active Responsible, unmuted, non-actor.
        var responsible = context.Participants
            .Where(p =>
                p.IsActive &&
                p.Type == ParticipationType.Responsible &&
                IsEligible(p, context.ActorAccountUserId))
            .Select(p => p.AccountUserId)
            .ToList();

        if (responsible.Count > 0)
            return responsible;

        // Step 2: eligible active unmuted Watchers.
        var watchers = context.Participants
            .Where(p =>
                p.IsActive &&
                p.Type == ParticipationType.Watching &&
                IsEligible(p, context.ActorAccountUserId))
            .Select(p => p.AccountUserId)
            .ToList();

        if (watchers.Count > 0)
            return watchers;

        // Step 3: Owner/Admin fallback from account members not already in participants.
        var participantUserIds = context.Participants
            .Where(p => p.IsActive)
            .Select(p => p.AccountUserId)
            .ToHashSet();

        var fallback = context.FallbackMembers
            .Where(m =>
                m.AccountUserId != context.ActorAccountUserId &&
                m.MembershipStatus == MembershipStatus.Active &&
                (m.Role == AccountUserRole.Owner || m.Role == AccountUserRole.Admin) &&
                !participantUserIds.Contains(m.AccountUserId))
            .Select(m => m.AccountUserId)
            .ToList();

        return fallback;
    }

    private static bool IsEligible(KeepPushParticipantInfo p, Guid actorId) =>
        p.AccountUserId != actorId &&
        p.NotificationsEnabled &&
        p.MembershipStatus == MembershipStatus.Active &&
        p.Role != AccountUserRole.Viewer;
}
