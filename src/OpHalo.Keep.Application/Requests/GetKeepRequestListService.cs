using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed class GetKeepRequestListService(
    IKeepRequestListPersistence persistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IClock clock,
    IKeepRequestListCursorProtector cursorProtector)
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    private static readonly HashSet<string> ValidViews = new(StringComparer.OrdinalIgnoreCase)
    {
        "default", "assigned_to_me", "watching", "unassigned",
        "needs_attention", "feedback_review", "closed_history",
        "cancelled_history", "all_history"
    };

    // Active-only views: terminal-status filter would be contradictory (ADR-257).
    private static readonly HashSet<string> ActiveOnlyViews = new(StringComparer.OrdinalIgnoreCase)
    {
        "default", "assigned_to_me", "watching", "unassigned", "needs_attention"
    };

    private static readonly HashSet<string> TerminalStatusSlugs =
        new(["closed", "cancelled"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ActiveStatusSlugs =
        new(["received", "scheduled", "in_progress", "pending_customer", "resolved"],
            StringComparer.OrdinalIgnoreCase);

    public async Task<Result<GetKeepRequestListResult>> ExecuteAsync(
        KeepRequestListQuery? query = null,
        CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated)
            return Result<GetKeepRequestListResult>.Failure(Unauthorized);

        var userSnapshot = await persistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result<GetKeepRequestListResult>.Failure(Forbidden);

        var accountSnapshot = await persistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<GetKeepRequestListResult>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                userSnapshot.Role,
                userSnapshot.MembershipStatus,
                accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsView))
            return Result<GetKeepRequestListResult>.Failure(Forbidden);

        // This is a read — OffSeason (ReadOnly) does not block it; only Blocked does.
        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState,
            accountSnapshot.Purpose,
            accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc,
            accountSnapshot.PastDueGraceEndsAtUtc,
            accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: true,
            clock.UtcNow);

        var decision = accountAccessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked)
            return Result<GetKeepRequestListResult>.Failure(Forbidden);

        if (!featurePolicy.IsEnabled(accountSnapshot.Plan, FeatureKeys.Keep.OperatorQueue))
            return Result<GetKeepRequestListResult>.Failure(Forbidden);

        // --- Query validation (ADR-257/258) ---
        // Order matters: format errors must surface before the 4A placeholder gates so callers
        // get specific codes (RequestListInvalidDateFormat, RequestListContradictoryParameters)
        // rather than the generic not-yet-available codes.

        query ??= new KeepRequestListQuery();
        var normalizedView = NormalizeView(query.View);

        // 1. Unknown view — must fail before everything else.
        if (!ValidViews.Contains(normalizedView))
            return Result<GetKeepRequestListResult>.Failure(KeepRequestErrors.RequestListInvalidView);

        // 2. Date format — validated before the filter gate so that malformed dates return
        //    RequestListInvalidDateFormat, not RequestListFilterNotYetAvailable (ADR-258).
        var dateError = ValidateDateFormats(query);
        if (dateError is not null)
            return Result<GetKeepRequestListResult>.Failure(dateError);

        // 3. Contradictions — validated before the "not yet available" gates so that
        //    view+status contradictions are explicit rather than shadowed (ADR-257).
        var contradictionError = ValidateContradictions(normalizedView, query);
        if (contradictionError is not null)
            return Result<GetKeepRequestListResult>.Failure(contradictionError);

        // 4. Filter/search gate (Session 4A — 4B replaces with real execution).
        if (HasFilters(query))
            return Result<GetKeepRequestListResult>.Failure(KeepRequestErrors.RequestListFilterNotYetAvailable);

        // 5. View executability gate (Session 4A — 4B replaces with real execution).
        if (normalizedView != "default")
            return Result<GetKeepRequestListResult>.Failure(KeepRequestErrors.RequestListViewNotYetAvailable);

        // 6. Limit.
        var limit = query.Limit ?? DefaultLimit;
        if (limit < 1 || limit > MaxLimit)
            return Result<GetKeepRequestListResult>.Failure(KeepRequestErrors.RequestListInvalidLimit);

        // 7. Cursor — fingerprint binds the cursor to the current canonical query shape.
        var fingerprint = KeepRequestListCursor.ComputeFingerprint(query);
        KeepRequestListCursorPayload? cursorPayload = null;
        if (!string.IsNullOrEmpty(query.Cursor))
        {
            if (!KeepRequestListCursor.TryDecode(cursorProtector, query.Cursor, fingerprint, out cursorPayload))
                return Result<GetKeepRequestListResult>.Failure(KeepRequestErrors.RequestListInvalidCursor);
        }

        // --- Data fetch ---
        var nowUtc = clock.UtcNow;
        var role = userSnapshot.Role;
        var isOwnerOrAdmin = role is AccountUserRole.Owner or AccountUserRole.Admin;
        var isOffSeason = accountSnapshot.OperatingMode == AccountOperatingMode.OffSeason;

        var canOperate = userAccessPolicy.IsPermitted(
            userSnapshot.Role,
            userSnapshot.MembershipStatus,
            accountSnapshot.Purpose,
            PermissionKeys.Keep.RequestsOperate);

        var requests = await persistence.GetDefaultListRequestsAsync(
            currentUser.AccountId, includeClosedUnresolvedFeedback: isOwnerOrAdmin, ct);

        Dictionary<Guid, KeepRequestParticipantSummary> participantSummaries;
        if (requests.Count > 0)
        {
            var requestIds = requests.Select(r => r.Id).ToList();
            participantSummaries = await persistence.GetParticipantSummariesAsync(
                requestIds, userSnapshot.AccountUserId, currentUser.AccountId, ct);
        }
        else
        {
            participantSummaries = [];
        }

        var allSummaries = requests
            .Select(r => ToSummary(r, canOperate, isOwnerOrAdmin, isOffSeason, nowUtc,
                participantSummaries.GetValueOrDefault(r.Id)))
            .Order(RequestListComparer.Instance)
            .ToList();

        // --- Cursor skip ---
        IReadOnlyList<KeepRequestSummary> sorted = allSummaries;
        if (cursorPayload is not null)
        {
            var startIndex = FindCursorStartIndex(sorted, cursorPayload);
            sorted = sorted.Skip(startIndex).ToList();
        }

        // --- limit+1 slice, hasMore, next cursor ---
        var sliced = sorted.Take(limit + 1).ToList();
        var hasMore = sliced.Count > limit;
        var page = sliced.Take(limit).ToList();

        string? nextCursor = null;
        if (hasMore && page.Count > 0)
            nextCursor = EncodeCursor(page[^1], fingerprint);

        var pageInfo = new KeepRequestPageInfo(Limit: limit, HasMore: hasMore, NextCursor: nextCursor);
        var listContext = new KeepRequestListContext(
            View: "default",
            IsDefaultCommandCenter: true,
            IsHistory: false,
            IsSearch: false);

        return Result<GetKeepRequestListResult>.Success(
            new GetKeepRequestListResult(page, pageInfo, ViewCounts: null, listContext));
    }

    // --- Validation helpers ---

    private static string NormalizeView(string? view) =>
        string.IsNullOrWhiteSpace(view) ? "default" : view.Trim().ToLowerInvariant();

    private static Error? ValidateDateFormats(KeepRequestListQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.CreatedFrom) && !IsValidIso8601(query.CreatedFrom))
            return KeepRequestErrors.RequestListInvalidDateFormat;
        if (!string.IsNullOrWhiteSpace(query.CreatedTo) && !IsValidIso8601(query.CreatedTo))
            return KeepRequestErrors.RequestListInvalidDateFormat;
        if (!string.IsNullOrWhiteSpace(query.ClosedFrom) && !IsValidIso8601(query.ClosedFrom))
            return KeepRequestErrors.RequestListInvalidDateFormat;
        if (!string.IsNullOrWhiteSpace(query.ClosedTo) && !IsValidIso8601(query.ClosedTo))
            return KeepRequestErrors.RequestListInvalidDateFormat;
        return null;
    }

    // Must be a full ISO-8601/RFC3339 datetime with explicit UTC (Z) or numeric offset (ADR-258).
    // Date-only strings ("2026-06-18") are rejected — no 'T' separator.
    // Unzoned datetimes ("2026-06-18T10:00:00") are rejected — no explicit offset.
    private static bool IsValidIso8601(string value)
    {
        var trimmed = value.Trim();

        if (!DateTimeOffset.TryParse(
                trimmed,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out _))
            return false;

        // Reject date-only values: must contain 'T' (ISO 8601 date/time separator).
        if (trimmed.IndexOf('T', StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        // Require explicit UTC or offset marker.
        return HasExplicitOffset(trimmed);
    }

    private static bool HasExplicitOffset(string s)
    {
        // Z suffix = UTC.
        if (s.EndsWith("Z", StringComparison.OrdinalIgnoreCase)) return true;

        // ±HH:mm suffix — check last 6 chars, e.g. "+10:00" or "-05:30".
        if (s.Length < 6) return false;
        var tail = s[^6..];
        return (tail[0] == '+' || tail[0] == '-')
            && char.IsDigit(tail[1]) && char.IsDigit(tail[2])
            && tail[3] == ':'
            && char.IsDigit(tail[4]) && char.IsDigit(tail[5]);
    }

    private static Error? ValidateContradictions(string normalizedView, KeepRequestListQuery query)
    {
        var status = query.Status?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(status)) return null;

        // History views are restricted to their respective terminal statuses.
        if (normalizedView == "closed_history" && !string.Equals(status, "closed", StringComparison.Ordinal))
            return KeepRequestErrors.RequestListContradictoryParameters;
        if (normalizedView == "cancelled_history" && !string.Equals(status, "cancelled", StringComparison.Ordinal))
            return KeepRequestErrors.RequestListContradictoryParameters;
        if (normalizedView == "all_history" && ActiveStatusSlugs.Contains(status))
            return KeepRequestErrors.RequestListContradictoryParameters;

        // Active operational views cannot be combined with terminal statuses.
        if (ActiveOnlyViews.Contains(normalizedView) && TerminalStatusSlugs.Contains(status))
            return KeepRequestErrors.RequestListContradictoryParameters;

        return null;
    }

    // Session 4A: any non-blank filter/search param blocks execution (4B replaces).
    // Blank Q behaves like no search (ADR-258), so whitespace-only Q is not a filter.
    private static bool HasFilters(KeepRequestListQuery query) =>
        !string.IsNullOrWhiteSpace(query.Status) ||
        !string.IsNullOrWhiteSpace(query.AttentionReason) ||
        query.AssignedAccountUserId.HasValue ||
        !string.IsNullOrWhiteSpace(query.Q) ||
        !string.IsNullOrWhiteSpace(query.CreatedFrom) ||
        !string.IsNullOrWhiteSpace(query.CreatedTo) ||
        !string.IsNullOrWhiteSpace(query.ClosedFrom) ||
        !string.IsNullOrWhiteSpace(query.ClosedTo);

    // --- Cursor helpers ---

    private string EncodeCursor(KeepRequestSummary last, string fingerprint)
    {
        var rankingOrder = last.Ranking.RankingOrder;
        var descending = rankingOrder is 6 or 7 or 8;
        var tick = GetSecondarySortTick(last);
        return KeepRequestListCursor.Encode(
            cursorProtector, fingerprint, last.Id, rankingOrder, tick, descending);
    }

    private static int FindCursorStartIndex(
        IReadOnlyList<KeepRequestSummary> sorted,
        KeepRequestListCursorPayload cursor)
    {
        for (var i = 0; i < sorted.Count; i++)
        {
            if (IsAfterCursor(sorted[i], cursor))
                return i;
        }
        return sorted.Count;
    }

    private static bool IsAfterCursor(KeepRequestSummary row, KeepRequestListCursorPayload cursor)
    {
        if (row.Ranking.RankingOrder != cursor.RankingOrder)
            return row.Ranking.RankingOrder > cursor.RankingOrder;

        var cmp = CompareSecondaryTicks(
            GetSecondarySortTick(row), cursor.SecondaryTick, cursor.SecondaryDescending);
        if (cmp != 0) return cmp > 0;

        return row.Id.CompareTo(cursor.LastId) > 0;
    }

    private static long? GetSecondarySortTick(KeepRequestSummary row) =>
        row.Ranking.RankingOrder switch
        {
            1 or 2 or 4 => (row.Attention.NextAttentionAtUtc ?? row.Attention.FirstResponseDueAtUtc)?.Ticks,
            3            => row.Attention.AttentionSinceUtc?.Ticks,
            5            => row.Attention.FirstResponseDueAtUtc?.Ticks,
            _            => (long?)row.LastBusinessActivityAtUtc.Ticks
        };

    private static int CompareSecondaryTicks(long? rowTick, long? cursorTick, bool descending)
    {
        if (rowTick is null && cursorTick is null) return 0;
        if (rowTick is null) return 1;    // nulls last in both sort directions
        if (cursorTick is null) return -1;
        // DESC: higher value appears earlier, so "after cursor" means lower value → reverse compare.
        return descending
            ? cursorTick.Value.CompareTo(rowTick.Value)
            : rowTick.Value.CompareTo(cursorTick.Value);
    }

    // --- Row mapping (unchanged from Session 3C) ---

    private static KeepRequestSummary ToSummary(
        KeepRequest r,
        bool canOperate,
        bool isOwnerOrAdmin,
        bool isOffSeason,
        DateTime nowUtc,
        KeepRequestParticipantSummary? participation)
    {
        var isPostClose = r.Status == KeepRequestStatus.Closed
            && r.AttentionReason == AttentionReason.UnresolvedFeedback
            && r.AttentionLevel != AttentionLevel.None;

        // Guard: first-response flags must be false for terminal rows.
        var firstResponsePending = !r.IsTerminal
            && r.FirstRespondedAtUtc is null
            && r.FirstResponseDueAtUtc.HasValue
            && r.FirstResponseDueAtUtc.Value > nowUtc;

        var firstResponseOverdue = !r.IsTerminal
            && r.FirstRespondedAtUtc is null
            && r.FirstResponseDueAtUtc.HasValue
            && r.FirstResponseDueAtUtc.Value <= nowUtc;

        var overdueBusinessWaiting = r.WaitingDirection == WaitingDirection.Business
            && r.NextAttentionAtUtc.HasValue
            && r.NextAttentionAtUtc.Value < nowUtc;

        var isOverdue = overdueBusinessWaiting || firstResponseOverdue;

        var attention = new KeepRequestAttentionInfo(
            AttentionLevel: MapAttentionLevel(r.AttentionLevel),
            WaitingDirection: MapWaitingDirection(r.WaitingDirection),
            AttentionReason: r.AttentionReason.HasValue ? MapAttentionReason(r.AttentionReason.Value) : null,
            PriorityBand: r.PriorityBand == PriorityBand.Priority ? "priority" : "standard",
            AttentionSinceUtc: r.AttentionSinceUtc,
            NextAttentionAtUtc: r.NextAttentionAtUtc,
            FirstResponseDueAtUtc: r.FirstResponseDueAtUtc,
            FirstRespondedAtUtc: r.FirstRespondedAtUtc,
            FirstResponsePending: firstResponsePending,
            FirstResponseOverdue: firstResponseOverdue);

        var (rankingGroup, rankingOrder) = ComputeRankingGroup(
            r, isPostClose, firstResponsePending, firstResponseOverdue, overdueBusinessWaiting);

        var severity = ComputeSeverity(r, isOverdue, isPostClose, firstResponsePending);

        var elapsedSinceUtc = r.WaitingDirection == WaitingDirection.Business
            ? r.AttentionSinceUtc
            : firstResponsePending || firstResponseOverdue
                ? r.CreatedAtUtc
                : (DateTime?)null;

        var dueAtUtc = r.WaitingDirection == WaitingDirection.Business
            ? r.NextAttentionAtUtc
            : firstResponsePending || firstResponseOverdue
                ? r.FirstResponseDueAtUtc
                : (DateTime?)null;

        var ranking = new KeepRequestRankingInfo(
            RankingGroup: rankingGroup,
            RankingOrder: rankingOrder,
            RankingReason: rankingGroup,
            Severity: severity,
            IsOverdue: isOverdue,
            ElapsedSinceUtc: elapsedSinceUtc,
            DueAtUtc: dueAtUtc,
            IsPostClose: isPostClose);

        var preview = new KeepRequestPreviewInfo(null, null, false);

        var quickActions = BuildQuickActions(r, canOperate, isPostClose, firstResponseOverdue);
        var contactActions = BuildContactActions(r, canOperate, isPostClose);
        var actions = new KeepRequestActionsInfo(quickActions, contactActions);

        var canAssignFromList = isOwnerOrAdmin && canOperate && !isOffSeason && !r.IsTerminal;
        var participationInfo = BuildParticipationInfo(participation, canAssignFromList);
        var notificationInfo = BuildNotificationInfo(canOperate, isOffSeason, participation);

        return new KeepRequestSummary(
            Id: r.Id,
            ReferenceCode: r.ReferenceCode,
            Status: MapStatus(r.Status),
            CurrentStatusText: r.CurrentStatusText,
            CustomerName: r.CustomerName,
            CustomerPhone: r.CustomerPhone,
            CustomerEmail: r.CustomerEmail,
            Description: r.Description,
            LastCustomerActivityAtUtc: r.LastCustomerActivityAt,
            LastBusinessActivityAtUtc: r.LastBusinessActivityAt,
            CreatedAtUtc: r.CreatedAtUtc,
            UpdatedAtUtc: r.UpdatedAtUtc,
            IsTerminal: r.IsTerminal,
            IsPostCloseFollowUp: isPostClose,
            Attention: attention,
            Ranking: ranking,
            Preview: preview,
            Actions: actions,
            Participation: participationInfo,
            CurrentUserNotification: notificationInfo);
    }

    private static (string group, int order) ComputeRankingGroup(
        KeepRequest r,
        bool isPostClose,
        bool firstResponsePending,
        bool firstResponseOverdue,
        bool overdueBusinessWaiting)
    {
        if (overdueBusinessWaiting || firstResponseOverdue)
            return ("overdue_business_waiting", 1);

        // Check post-close before priority band: post-close rows have WaitingDirection=Business
        // and PriorityBand=Priority, so they would incorrectly match group 2 without this guard.
        if (isPostClose)
            return ("post_close_unresolved_feedback", 3);

        if (r.PriorityBand == PriorityBand.Priority
            && r.WaitingDirection == WaitingDirection.Business)
            return ("priority_business_waiting", 2);

        if (r.WaitingDirection == WaitingDirection.Business)
            return ("standard_business_waiting", 4);

        if (firstResponsePending)
            return ("first_response_pending", 5);

        if (r.Status == KeepRequestStatus.PendingCustomer)
            return ("waiting_on_customer", 6);

        if (r.Status == KeepRequestStatus.Resolved && r.AttentionLevel == AttentionLevel.None)
            return ("resolved_quiet", 7);

        return ("active", 8);
    }

    private static string ComputeSeverity(KeepRequest r, bool isOverdue, bool isPostClose, bool firstResponsePending)
    {
        if (isOverdue || isPostClose)
            return "danger";

        if (r.AttentionReason is AttentionReason.Complaint
            or AttentionReason.ScheduleChangeRequest
            or AttentionReason.ChangeOrCancelRequest)
            return "danger";

        if (r.PriorityBand == PriorityBand.Priority && r.WaitingDirection == WaitingDirection.Business)
            return "priority";

        if (r.WaitingDirection == WaitingDirection.Business)
            return "attention";

        if (firstResponsePending)
            return "attention";

        if (r.Status == KeepRequestStatus.PendingCustomer
            || (r.Status == KeepRequestStatus.Resolved && r.AttentionLevel == AttentionLevel.None))
            return "neutral";

        return "muted";
    }

    private static IReadOnlyList<KeepQuickAction> BuildQuickActions(
        KeepRequest r,
        bool canOperate,
        bool isPostClose,
        bool firstResponseOverdue)
    {
        var openDetail = QuickActionDefs.OpenDetail;

        if (isPostClose)
            return [openDetail, QuickActionDefs.ReviewFeedback];

        if (!canOperate)
            return [openDetail];

        if (r.IsTerminal)
            return [openDetail];

        var hasContactMethods = !string.IsNullOrWhiteSpace(r.CustomerPhone)
            || !string.IsNullOrWhiteSpace(r.CustomerEmail);

        // ClearsAttention is state-aware: only true when the request is business-waiting.
        // For PendingCustomer/quiet-Resolved/active-no-attention, no attention to clear.
        var postCustomerUpdate = new KeepQuickAction(
            "post_customer_update", "Update customer", "customer_visible",
            ClearsAttention: r.WaitingDirection == WaitingDirection.Business && r.AttentionLevel != AttentionLevel.None,
            CountsFirstResponse: false,
            ChangesStatus: false,
            EffectSummaryCode: "customer_visible_status_unchanged");

        var actions = new List<KeepQuickAction> { openDetail };

        if (hasContactMethods)
            actions.Add(QuickActionDefs.ContactCustomer);

        actions.Add(postCustomerUpdate);

        // acknowledge_attention: shown when request has active attention, but NOT when the
        // attention is specifically a first-response that is overdue with no response yet —
        // in that case the right action is to respond, not acknowledge.
        var hasAttention = r.AttentionLevel != AttentionLevel.None;
        var isFirstResponseOverdueNoResponse = firstResponseOverdue && r.FirstRespondedAtUtc is null;

        if (hasAttention && !isFirstResponseOverdueNoResponse)
            actions.Add(QuickActionDefs.AcknowledgeAttention);

        return actions;
    }

    private static IReadOnlyList<ContactActionItem> BuildContactActions(
        KeepRequest r, bool canOperate, bool isPostClose)
    {
        if (!canOperate || r.IsTerminal || isPostClose)
            return [];

        var actions = new List<ContactActionItem>();

        if (!string.IsNullOrWhiteSpace(r.CustomerPhone))
            actions.Add(new ContactActionItem("call", true, r.CustomerPhone));

        if (!string.IsNullOrWhiteSpace(r.CustomerEmail))
            actions.Add(new ContactActionItem("email", true, r.CustomerEmail));

        return actions;
    }

    private static KeepRequestParticipationInfo BuildParticipationInfo(
        KeepRequestParticipantSummary? participation,
        bool canAssignFromList)
    {
        if (participation is null)
            return new KeepRequestParticipationInfo(
                0, 0, false, true, "none", null, null, null, canAssignFromList);

        var participationType = participation.CurrentUserParticipationType switch
        {
            ParticipationType.Responsible => "responsible",
            ParticipationType.Watching => "watching",
            _ => "none"
        };

        // ResponsibleCount is the effective eligible count (ADR-226):
        // HasResponsible and IsUnassigned follow from it directly.
        return new KeepRequestParticipationInfo(
            ResponsibleCount: participation.ResponsibleCount,
            WatchingCount: participation.WatchingCount,
            HasResponsible: participation.ResponsibleCount > 0,
            IsUnassigned: participation.ResponsibleCount == 0,
            CurrentUserParticipationType: participationType,
            CurrentUserNotificationsEnabled: participation.CurrentUserNotificationsEnabled,
            ResponsibleDisplayName: participation.ResponsibleDisplayName,
            ResponsibleIsStale: participation.ResponsibleIsStale,
            CanAssignFromList: canAssignFromList);
    }

    private static KeepRequestNotificationInfo BuildNotificationInfo(
        bool canOperate,
        bool isOffSeason,
        KeepRequestParticipantSummary? participation)
    {
        if (!canOperate)
            return new KeepRequestNotificationInfo(false, false, "viewer");

        // Off-season suppresses request notifications account-wide.
        if (isOffSeason)
            return new KeepRequestNotificationInfo(false, false, "off_season");

        if (participation?.CurrentUserParticipationType != null)
            return new KeepRequestNotificationInfo(
                true,
                participation.CurrentUserNotificationsEnabled ?? false,
                null);

        return new KeepRequestNotificationInfo(true, false, "not_participating");
    }

    private static string MapStatus(KeepRequestStatus status) => status switch
    {
        KeepRequestStatus.Received => "received",
        KeepRequestStatus.Scheduled => "scheduled",
        KeepRequestStatus.InProgress => "in_progress",
        KeepRequestStatus.PendingCustomer => "pending_customer",
        KeepRequestStatus.Resolved => "resolved",
        KeepRequestStatus.Closed => "closed",
        KeepRequestStatus.Cancelled => "cancelled",
        _ => throw new InvalidOperationException($"Unknown KeepRequestStatus: {status}")
    };

    private static string MapAttentionLevel(AttentionLevel level) => level switch
    {
        AttentionLevel.None => "none",
        AttentionLevel.Waiting => "waiting",
        AttentionLevel.NeedsAttention => "needs_attention",
        AttentionLevel.Overdue => "overdue",
        _ => throw new InvalidOperationException($"Unknown AttentionLevel: {level}")
    };

    private static string MapWaitingDirection(WaitingDirection direction) => direction switch
    {
        WaitingDirection.None => "none",
        WaitingDirection.Business => "business",
        WaitingDirection.Customer => "customer",
        _ => throw new InvalidOperationException($"Unknown WaitingDirection: {direction}")
    };

    private static string MapAttentionReason(AttentionReason reason) => reason switch
    {
        AttentionReason.CustomerMessage => "customer_message",
        AttentionReason.UpdateRequest => "update_request",
        AttentionReason.ScheduleChangeRequest => "schedule_change_request",
        AttentionReason.ChangeOrCancelRequest => "change_or_cancel_request",
        AttentionReason.Complaint => "complaint",
        AttentionReason.FirstResponseDue => "first_response_due",
        AttentionReason.UnresolvedFeedback => "unresolved_feedback",
        _ => throw new InvalidOperationException($"Unknown AttentionReason: {reason}")
    };

    private static class QuickActionDefs
    {
        public static readonly KeepQuickAction OpenDetail = new(
            "open_detail", "Open detail", "internal",
            false, false, false, "opens_detail");

        public static readonly KeepQuickAction ContactCustomer = new(
            "contact_customer", "Contact customer", "external_affordance",
            false, false, false, "external_contact_only");

        public static readonly KeepQuickAction AcknowledgeAttention = new(
            "acknowledge_attention", "Mark handled", "internal",
            true, false, false, "internal_clears_attention");

        public static readonly KeepQuickAction ReviewFeedback = new(
            "review_feedback", "Review feedback", "internal",
            false, false, false, "opens_detail_feedback");
    }

    private sealed class RequestListComparer : IComparer<KeepRequestSummary>
    {
        public static readonly RequestListComparer Instance = new();

        public int Compare(KeepRequestSummary? x, KeepRequestSummary? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var orderCmp = x.Ranking.RankingOrder.CompareTo(y.Ranking.RankingOrder);
            if (orderCmp != 0) return orderCmp;

            int secondaryCmp = x.Ranking.RankingOrder switch
            {
                // Groups 1, 2, 4: ascending NextAttentionAtUtc (most overdue/urgent first).
                1 or 2 or 4 => CompareNullableDatesAsc(
                    x.Attention.NextAttentionAtUtc ?? x.Attention.FirstResponseDueAtUtc,
                    y.Attention.NextAttentionAtUtc ?? y.Attention.FirstResponseDueAtUtc),

                // Group 3 (post-close): ascending AttentionSinceUtc (oldest unresolved first).
                3 => CompareNullableDatesAsc(x.Attention.AttentionSinceUtc, y.Attention.AttentionSinceUtc),

                // Group 5 (first-response pending): ascending FirstResponseDueAtUtc (soonest due first).
                5 => CompareNullableDatesAsc(x.Attention.FirstResponseDueAtUtc, y.Attention.FirstResponseDueAtUtc),

                // Groups 6, 7, 8: most recently active first (descending LastBusinessActivityAtUtc).
                _ => y.LastBusinessActivityAtUtc.CompareTo(x.LastBusinessActivityAtUtc)
            };

            return secondaryCmp != 0 ? secondaryCmp : x.Id.CompareTo(y.Id);
        }

        private static int CompareNullableDatesAsc(DateTime? a, DateTime? b)
        {
            if (a is null && b is null) return 0;
            if (a is null) return 1;  // nulls last
            if (b is null) return -1;
            return a.Value.CompareTo(b.Value);
        }
    }
}
