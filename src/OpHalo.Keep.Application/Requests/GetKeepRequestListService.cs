using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Core.Domain;
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

    // Sentinel RankingOrder values embedded in cursor payloads to identify sort mode.
    // Neither value collides with real B5 ranking orders (1–8).
    private const int HistorySortSentinel = 0;
    private const int FeedbackReviewSortSentinel = 99;

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

    // History views, feedback_review, and unassigned require Owner/Admin (ADR-248/242, G4d).
    // Operator uses the dedicated Available route; Viewer receives 403 for unassigned.
    private static readonly HashSet<string> OwnerAdminOnlyViews = new(StringComparer.OrdinalIgnoreCase)
    {
        "feedback_review", "closed_history", "cancelled_history", "all_history", "unassigned"
    };

    private static readonly Dictionary<string, HistoryViewKind> HistoryViewKinds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["closed_history"]    = HistoryViewKind.Closed,
            ["cancelled_history"] = HistoryViewKind.Cancelled,
            ["all_history"]       = HistoryViewKind.All
        };

    private static readonly Dictionary<string, ActiveViewKind> ActiveViewKinds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["default"]         = ActiveViewKind.Default,
            ["assigned_to_me"]  = ActiveViewKind.AssignedToMe,
            ["watching"]        = ActiveViewKind.Watching,
            ["unassigned"]      = ActiveViewKind.Unassigned,
            ["needs_attention"] = ActiveViewKind.NeedsAttention,
            ["feedback_review"] = ActiveViewKind.FeedbackReview
        };

    // Slug → enum maps for validation (ADR-257; individual values validated before contradictions).
    private static readonly Dictionary<string, KeepRequestStatus> StatusSlugs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["received"]         = KeepRequestStatus.Received,
            ["scheduled"]        = KeepRequestStatus.Scheduled,
            ["in_progress"]      = KeepRequestStatus.InProgress,
            ["pending_customer"] = KeepRequestStatus.PendingCustomer,
            ["resolved"]         = KeepRequestStatus.Resolved,
            ["closed"]           = KeepRequestStatus.Closed,
            ["cancelled"]        = KeepRequestStatus.Cancelled
        };

    private static readonly Dictionary<string, AttentionReason> AttentionReasonSlugs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["customer_message"]        = AttentionReason.CustomerMessage,
            ["update_request"]          = AttentionReason.UpdateRequest,
            ["schedule_change_request"] = AttentionReason.ScheduleChangeRequest,
            ["change_or_cancel_request"]= AttentionReason.ChangeOrCancelRequest,
            ["complaint"]               = AttentionReason.Complaint,
            ["first_response_due"]      = AttentionReason.FirstResponseDue,
            ["unresolved_feedback"]     = AttentionReason.UnresolvedFeedback
        };

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

        // Read is not blocked by OffSeason; only Blocked lifecycle blocks reads.
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

        // --- Query validation pipeline (ADR-257/258) ---
        // Order: unknown view → date format → status slug → attentionReason slug →
        //        contradictions → view role auth → limit → cursor.
        // Individual values are validated before combinations so clients see specific errors.

        query ??= new KeepRequestListQuery();
        var normalizedView = NormalizeView(query.View);

        // 1. Unknown view.
        if (!ValidViews.Contains(normalizedView))
            return Result<GetKeepRequestListResult>.Failure(KeepRequestErrors.RequestListInvalidView);

        // 2. Date format.
        var dateError = ValidateDateFormats(query);
        if (dateError is not null)
            return Result<GetKeepRequestListResult>.Failure(dateError);

        // 3. Status slug — validates individual value before contradiction check.
        KeepRequestStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!StatusSlugs.TryGetValue(query.Status.Trim(), out var st))
                return Result<GetKeepRequestListResult>.Failure(KeepRequestErrors.RequestListInvalidStatus);
            parsedStatus = st;
        }

        // 4. AttentionReason slug — same rationale.
        AttentionReason? parsedAttentionReason = null;
        if (!string.IsNullOrWhiteSpace(query.AttentionReason))
        {
            if (!AttentionReasonSlugs.TryGetValue(query.AttentionReason.Trim(), out var ar))
                return Result<GetKeepRequestListResult>.Failure(KeepRequestErrors.RequestListInvalidAttentionReason);
            parsedAttentionReason = ar;
        }

        // 5. Contradictions — view+status combinations, closedFrom/closedTo scope.
        var contradictionError = ValidateContradictions(normalizedView, parsedStatus, query);
        if (contradictionError is not null)
            return Result<GetKeepRequestListResult>.Failure(contradictionError);

        // 6. View role authorization (ADR-242/248, G4d).
        var role = userSnapshot.Role;
        var isOwnerOrAdmin = role is AccountUserRole.Owner or AccountUserRole.Admin;

        // Explicit role → scope (G4d). Unknown/future roles fail closed — never use role != Operator
        // as account-wide authorization.
        KeepRequestVisibilityScope scope;
        if (role is AccountUserRole.Owner or AccountUserRole.Admin or AccountUserRole.Viewer)
            scope = KeepRequestVisibilityScope.AccountWide;
        else if (role is AccountUserRole.Operator)
            scope = KeepRequestVisibilityScope.MyWork;
        else
            return Result<GetKeepRequestListResult>.Failure(Forbidden);

        if (OwnerAdminOnlyViews.Contains(normalizedView) && !isOwnerOrAdmin)
            return Result<GetKeepRequestListResult>.Failure(KeepRequestErrors.RequestListHistoryViewForbidden);

        // 7. Limit.
        var limit = query.Limit ?? DefaultLimit;
        if (limit < 1 || limit > MaxLimit)
            return Result<GetKeepRequestListResult>.Failure(KeepRequestErrors.RequestListInvalidLimit);

        // 9. Cursor — fingerprint binds the cursor to the current canonical query shape.
        var fingerprint = KeepRequestListCursor.ComputeFingerprint(query);
        KeepRequestListCursorPayload? cursorPayload = null;
        if (!string.IsNullOrEmpty(query.Cursor))
        {
            if (!KeepRequestListCursor.TryDecode(cursorProtector, query.Cursor, fingerprint, out cursorPayload))
                return Result<GetKeepRequestListResult>.Failure(KeepRequestErrors.RequestListInvalidCursor);
        }

        // --- Build filters ---
        // Dates are already format-validated above; Parse cannot fail here.
        var now = System.Globalization.CultureInfo.InvariantCulture;
        DateTimeOffset? createdFrom = null, createdTo = null, closedFrom = null, closedTo = null;
        if (!string.IsNullOrWhiteSpace(query.CreatedFrom))
            createdFrom = DateTimeOffset.Parse(query.CreatedFrom.Trim(), now);
        if (!string.IsNullOrWhiteSpace(query.CreatedTo))
            createdTo = DateTimeOffset.Parse(query.CreatedTo.Trim(), now);
        if (!string.IsNullOrWhiteSpace(query.ClosedFrom))
            closedFrom = DateTimeOffset.Parse(query.ClosedFrom.Trim(), now);
        if (!string.IsNullOrWhiteSpace(query.ClosedTo))
            closedTo = DateTimeOffset.Parse(query.ClosedTo.Trim(), now);

        var trimmedQ = string.IsNullOrWhiteSpace(query.Q) ? null : query.Q.Trim();
        var isSearch = !string.IsNullOrEmpty(trimmedQ);

        var filters = new KeepRequestListFilters(
            Status: parsedStatus,
            AttentionReason: parsedAttentionReason,
            AssignedAccountUserId: query.AssignedAccountUserId,
            Q: trimmedQ,
            CreatedFrom: createdFrom,
            CreatedTo: createdTo,
            ClosedFrom: closedFrom,
            ClosedTo: closedTo,
            IsOwnerOrAdmin: isOwnerOrAdmin);

        // --- Fetch data ---
        var nowUtc = clock.UtcNow;
        var isOffSeason = accountSnapshot.OperatingMode == AccountOperatingMode.OffSeason;
        var currentAccountUserId = userSnapshot.AccountUserId;

        var canOperate = userAccessPolicy.IsPermitted(
            userSnapshot.Role,
            userSnapshot.MembershipStatus,
            accountSnapshot.Purpose,
            PermissionKeys.Keep.RequestsOperate);

        IReadOnlyList<KeepRequestSummary> page;
        bool hasMore;
        IReadOnlyList<KeepRequest> historyPageEntities = [];
        var isFeedbackReview = normalizedView == "feedback_review";
        var isHistoryView = HistoryViewKinds.TryGetValue(normalizedView, out var historyViewKind);

        if (isHistoryView)
        {
            // History path: DB-level sort (TerminatedAtUtc DESC, Id ASC) and keyset cursor.
            DateTime? histCursorAt = null;
            Guid? histCursorId = null;
            if (cursorPayload is not null && cursorPayload.SecondaryTick.HasValue)
            {
                histCursorAt = new DateTime(cursorPayload.SecondaryTick.Value, DateTimeKind.Utc);
                histCursorId = cursorPayload.LastId;
            }

            var rawHistory = await persistence.GetHistoryRequestsAsync(
                currentUser.AccountId, historyViewKind, filters,
                histCursorAt, histCursorId, limit + 1, ct);

            hasMore = rawHistory.Count > limit;
            historyPageEntities = rawHistory.Take(limit).ToList();

            Dictionary<Guid, KeepRequestParticipantSummary> histParticipants;
            if (historyPageEntities.Count > 0)
            {
                histParticipants = await persistence.GetParticipantSummariesAsync(
                    historyPageEntities.Select(r => r.Id).ToList(),
                    currentAccountUserId, currentUser.AccountId, ct);
            }
            else
            {
                histParticipants = [];
            }

            page = historyPageEntities
                .Select(r => ToSummary(r, role, canOperate, isOwnerOrAdmin, isOffSeason, nowUtc,
                    histParticipants.GetValueOrDefault(r.Id), normalizedView))
                .ToList();
        }
        else
        {
            // Active-view path: DB filtering → in-memory B5 ranking sort → cursor skip.
            var activeViewKind = ActiveViewKinds[normalizedView];

            var rawRequests = await persistence.GetActiveViewRequestsAsync(
                currentUser.AccountId, currentAccountUserId, activeViewKind, filters, scope, ct);

            Dictionary<Guid, KeepRequestParticipantSummary> participants;
            if (rawRequests.Count > 0)
            {
                participants = await persistence.GetParticipantSummariesAsync(
                    rawRequests.Select(r => r.Id).ToList(),
                    currentAccountUserId, currentUser.AccountId, ct);
            }
            else
            {
                participants = [];
            }

            var comparer = isFeedbackReview
                ? (IComparer<KeepRequestSummary>)FeedbackReviewComparer.Instance
                : RequestListComparer.Instance;

            var allSummaries = rawRequests
                .Select(r => ToSummary(r, role, canOperate, isOwnerOrAdmin, isOffSeason, nowUtc,
                    participants.GetValueOrDefault(r.Id), normalizedView))
                .Order(comparer)
                .ToList();

            IReadOnlyList<KeepRequestSummary> sorted = allSummaries;
            if (cursorPayload is not null)
            {
                var startIndex = FindCursorStartIndex(sorted, cursorPayload, isFeedbackReview);
                sorted = sorted.Skip(startIndex).ToList();
            }

            var sliced = sorted.Take(limit + 1).ToList();
            hasMore = sliced.Count > limit;
            page = sliced.Take(limit).ToList();
        }

        // --- Next cursor ---
        string? nextCursor = null;
        if (hasMore && page.Count > 0)
        {
            nextCursor = isHistoryView
                ? KeepRequestListCursor.Encode(
                    cursorProtector, fingerprint,
                    historyPageEntities[^1].Id,
                    HistorySortSentinel,
                    historyPageEntities[^1].TerminatedAtUtc?.Ticks,
                    secondaryDescending: true)
                : EncodeCursorForActiveView(page[^1], fingerprint, isFeedbackReview);
        }

        var pageInfo = new KeepRequestPageInfo(Limit: limit, HasMore: hasMore, NextCursor: nextCursor);

        // --- View counts (ADR-241/259) ---
        var viewCounts = await persistence.GetViewCountsAsync(
            currentUser.AccountId, currentAccountUserId, isOwnerOrAdmin, scope, ct);

        var listContext = new KeepRequestListContext(
            View: normalizedView,
            IsDefaultCommandCenter: normalizedView == "default",
            IsHistory: isHistoryView,
            IsSearch: isSearch);

        return Result<GetKeepRequestListResult>.Success(
            new GetKeepRequestListResult(page, pageInfo, viewCounts, listContext));
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

        if (trimmed.IndexOf('T', StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        return HasExplicitOffset(trimmed);
    }

    private static bool HasExplicitOffset(string s)
    {
        if (s.EndsWith("Z", StringComparison.OrdinalIgnoreCase)) return true;

        if (s.Length < 6) return false;
        var tail = s[^6..];
        return (tail[0] == '+' || tail[0] == '-')
            && char.IsDigit(tail[1]) && char.IsDigit(tail[2])
            && tail[3] == ':'
            && char.IsDigit(tail[4]) && char.IsDigit(tail[5]);
    }

    private static Error? ValidateContradictions(
        string normalizedView,
        KeepRequestStatus? parsedStatus,
        KeepRequestListQuery query)
    {
        if (parsedStatus.HasValue)
        {
            var isTerminal = parsedStatus.Value is KeepRequestStatus.Closed or KeepRequestStatus.Cancelled;
            var isActive = !isTerminal;

            // History views are restricted to their own terminal statuses.
            if (normalizedView == "closed_history" && parsedStatus.Value != KeepRequestStatus.Closed)
                return KeepRequestErrors.RequestListContradictoryParameters;
            if (normalizedView == "cancelled_history" && parsedStatus.Value != KeepRequestStatus.Cancelled)
                return KeepRequestErrors.RequestListContradictoryParameters;
            if (normalizedView == "all_history" && isActive)
                return KeepRequestErrors.RequestListContradictoryParameters;

            // feedback_review contains only Closed rows (ADR-242).
            if (normalizedView == "feedback_review" && parsedStatus.Value != KeepRequestStatus.Closed)
                return KeepRequestErrors.RequestListContradictoryParameters;

            // Active operational views cannot be combined with terminal statuses (ADR-257).
            if (ActiveOnlyViews.Contains(normalizedView) && isTerminal)
                return KeepRequestErrors.RequestListContradictoryParameters;
        }

        // closedFrom/closedTo filter by TerminatedAtUtc — only meaningful for history views (ADR-258).
        var hasClosedDate = !string.IsNullOrWhiteSpace(query.ClosedFrom)
                         || !string.IsNullOrWhiteSpace(query.ClosedTo);
        if (hasClosedDate && normalizedView is not ("closed_history" or "cancelled_history" or "all_history"))
            return KeepRequestErrors.RequestListContradictoryParameters;

        return null;
    }

    // --- Cursor helpers ---

    private string EncodeCursorForActiveView(
        KeepRequestSummary last,
        string fingerprint,
        bool isFeedbackReview)
    {
        // Feedback review sorts by AttentionSinceUtc ASC — use dedicated sentinel so the
        // cursor secondary tick consistently captures AttentionSinceUtc regardless of ranking order.
        if (isFeedbackReview)
            return KeepRequestListCursor.Encode(
                cursorProtector, fingerprint, last.Id,
                FeedbackReviewSortSentinel,
                last.Attention.AttentionSinceUtc?.Ticks,
                secondaryDescending: false);

        var rankingOrder = last.Ranking.RankingOrder;
        var descending = rankingOrder is 6 or 7 or 8;
        return KeepRequestListCursor.Encode(
            cursorProtector, fingerprint, last.Id, rankingOrder, GetSecondarySortTick(last), descending);
    }

    private static int FindCursorStartIndex(
        IReadOnlyList<KeepRequestSummary> sorted,
        KeepRequestListCursorPayload cursor,
        bool isFeedbackReview)
    {
        for (var i = 0; i < sorted.Count; i++)
        {
            if (IsAfterCursor(sorted[i], cursor, isFeedbackReview))
                return i;
        }
        return sorted.Count;
    }

    private static bool IsAfterCursor(
        KeepRequestSummary row,
        KeepRequestListCursorPayload cursor,
        bool isFeedbackReview)
    {
        if (isFeedbackReview)
        {
            // feedback_review sort: AttentionSinceUtc ASC, Id ASC.
            var cmp = CompareSecondaryTicks(row.Attention.AttentionSinceUtc?.Ticks, cursor.SecondaryTick, false);
            return cmp != 0 ? cmp > 0 : row.Id.CompareTo(cursor.LastId) > 0;
        }

        if (row.Ranking.RankingOrder != cursor.RankingOrder)
            return row.Ranking.RankingOrder > cursor.RankingOrder;

        var tickCmp = CompareSecondaryTicks(
            GetSecondarySortTick(row), cursor.SecondaryTick, cursor.SecondaryDescending);
        if (tickCmp != 0) return tickCmp > 0;

        return row.Id.CompareTo(cursor.LastId) > 0;
    }

    private static long? GetSecondarySortTick(KeepRequestSummary row) =>
        row.Ranking.RankingOrder switch
        {
            1 or 2 or 4 => (row.Attention.NextAttentionAtUtc ?? row.Attention.FirstResponseDueAtUtc)?.Ticks,
            3            => row.Attention.AttentionSinceUtc?.Ticks,
            5            => row.Attention.FirstResponseDueAtUtc?.Ticks,
            _            => (long?)(row.LastBusinessActivityAtUtc ?? row.LastCustomerActivityAtUtc ?? row.CreatedAtUtc).Ticks
        };

    private static int CompareSecondaryTicks(long? rowTick, long? cursorTick, bool descending)
    {
        if (rowTick is null && cursorTick is null) return 0;
        if (rowTick is null) return 1;    // nulls last in both directions
        if (cursorTick is null) return -1;
        return descending
            ? cursorTick.Value.CompareTo(rowTick.Value)
            : rowTick.Value.CompareTo(cursorTick.Value);
    }

    // --- Row mapping ---

    private static KeepRequestSummary ToSummary(
        KeepRequest r,
        AccountUserRole role,
        bool canOperate,
        bool isOwnerOrAdmin,
        bool isOffSeason,
        DateTime nowUtc,
        KeepRequestParticipantSummary? participation,
        string normalizedView)
    {
        var isPostClose = r.Status == KeepRequestStatus.Closed
            && r.AttentionReason == AttentionReason.UnresolvedFeedback
            && r.AttentionLevel != AttentionLevel.None;

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

        // Evaluate action policy per request; list uses the decision for write affordances.
        // Phone/email presence, first-response-overdue suppression, and row context
        // remain presentation conditions here (ADR-328).
        var canWrite = canOperate && !isOffSeason;
        var actorContext = new KeepRequestActionContext(
            Role:                role,
            CanWrite:            canWrite,
            ActiveParticipation: participation?.CurrentUserParticipationType,
            NotificationsEnabled: participation?.CurrentUserNotificationsEnabled);
        var actionDecision = KeepRequestActionPolicy.Evaluate(r, actorContext);

        var quickActions = BuildQuickActions(r, canOperate, isPostClose, firstResponseOverdue, actionDecision);
        var contactActions = BuildContactActions(r, canOperate, isPostClose, actionDecision);
        var actions = new KeepRequestActionsInfo(quickActions, contactActions);

        var isUnassigned = participation is null || participation.ResponsibleCount == 0;
        var canAssignFromList = actionDecision.CanAssignResponsible;
        var canSelfAssignFromList = normalizedView == "unassigned"
            && actionDecision.CanSelfAssignResponsible
            && isUnassigned;

        var rowContext = ComputeRowContext(r, isPostClose, firstResponsePending, firstResponseOverdue, canSelfAssignFromList);
        var participationInfo = BuildParticipationInfo(participation, canAssignFromList, canSelfAssignFromList);
        var notificationInfo = BuildNotificationInfo(canOperate, isOffSeason, participation);

        // Aging metadata: only for unreviewed negative feedback in active review state.
        // isPostClose already captures Closed + UnresolvedFeedback + attention raised.
        var feedbackReviewAgeBucket = isPostClose && r.FeedbackSubmittedAtUtc.HasValue
            ? MapFeedbackReviewAgeBucket(FeedbackReviewPolicy.ComputeAgeBucket(r.FeedbackSubmittedAtUtc.Value, nowUtc))
            : (string?)null;
        var feedbackReviewDueAtUtc = isPostClose && r.FeedbackSubmittedAtUtc.HasValue
            ? FeedbackReviewPolicy.ComputeReviewDueAtUtc(r.FeedbackSubmittedAtUtc.Value)
            : (DateTime?)null;

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
            Version: r.ConcurrencyVersion,
            IsTerminal: r.IsTerminal,
            IsPostCloseFollowUp: isPostClose,
            RowContext: rowContext,
            Attention: attention,
            Ranking: ranking,
            Preview: preview,
            Actions: actions,
            Participation: participationInfo,
            CurrentUserNotification: notificationInfo,
            FeedbackReviewAgeBucket: feedbackReviewAgeBucket,
            FeedbackReviewDueAtUtc: feedbackReviewDueAtUtc);
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
        bool firstResponseOverdue,
        KeepRequestActionDecision actionDecision)
    {
        var openDetail = QuickActionDefs.OpenDetail;

        if (isPostClose)
        {
            // ReviewFeedback gated by policy; suppressed for OffSeason and non-Owner/Admin (ADR-328).
            return actionDecision.CanMarkFeedbackReviewed
                ? [openDetail, QuickActionDefs.ReviewFeedback]
                : [openDetail];
        }

        if (!canOperate)
            return [openDetail];

        if (r.IsTerminal)
            return [openDetail];

        var actions = new List<KeepQuickAction> { openDetail };

        // CanLogExternalContact gates the contact quick action (ADR-328).
        var hasContactMethods = !string.IsNullOrWhiteSpace(r.CustomerPhone)
            || !string.IsNullOrWhiteSpace(r.CustomerEmail);
        if (hasContactMethods && actionDecision.CanLogExternalContact)
            actions.Add(QuickActionDefs.ContactCustomer);

        // CanSendBusinessUpdate gates the customer update quick action (ADR-328).
        if (actionDecision.CanSendBusinessUpdate)
        {
            actions.Add(new KeepQuickAction(
                "post_customer_update", "Update customer", "customer_visible",
                ClearsAttention: r.WaitingDirection == WaitingDirection.Business && r.AttentionLevel != AttentionLevel.None,
                CountsFirstResponse: false,
                ChangesStatus: false,
                EffectSummaryCode: "customer_visible_status_unchanged"));
        }

        // Policy-derived; first-response-overdue suppression is a presentation condition (ADR-328).
        var isFirstResponseOverdueNoResponse = firstResponseOverdue && r.FirstRespondedAtUtc is null;
        if (actionDecision.CanAcknowledgeAttention && !isFirstResponseOverdueNoResponse)
            actions.Add(QuickActionDefs.AcknowledgeAttention);

        return actions;
    }

    private static IReadOnlyList<ContactActionItem> BuildContactActions(
        KeepRequest r, bool canOperate, bool isPostClose, KeepRequestActionDecision actionDecision)
    {
        if (!canOperate || r.IsTerminal || isPostClose || !actionDecision.CanLogExternalContact)
            return [];

        var actions = new List<ContactActionItem>();

        if (!string.IsNullOrWhiteSpace(r.CustomerPhone))
            actions.Add(new ContactActionItem("call", true, r.CustomerPhone));

        if (!string.IsNullOrWhiteSpace(r.CustomerEmail))
            actions.Add(new ContactActionItem("email", true, r.CustomerEmail));

        return actions;
    }

    private static string ComputeRowContext(
        KeepRequest r,
        bool isPostClose,
        bool firstResponsePending,
        bool firstResponseOverdue,
        bool canSelfAssignFromList)
    {
        if (isPostClose) return "feedback_review";
        if (r.Status == KeepRequestStatus.Closed) return "closed_history";
        if (r.Status == KeepRequestStatus.Cancelled) return "cancelled_history";
        if (r.AttentionLevel != AttentionLevel.None) return "needs_attention";
        if (firstResponsePending || firstResponseOverdue) return "first_response";
        if (r.Status == KeepRequestStatus.PendingCustomer) return "waiting_on_customer";
        if (canSelfAssignFromList) return "unassigned_available";
        return "active_work";
    }

    private static KeepRequestParticipationInfo BuildParticipationInfo(
        KeepRequestParticipantSummary? participation,
        bool canAssignFromList,
        bool canSelfAssignFromList)
    {
        if (participation is null)
            return new KeepRequestParticipationInfo(
                0, 0, false, true, "none", null, null, null, canAssignFromList, canSelfAssignFromList);

        var participationType = participation.CurrentUserParticipationType switch
        {
            ParticipationType.Responsible => "responsible",
            ParticipationType.Watching => "watching",
            _ => "none"
        };

        return new KeepRequestParticipationInfo(
            ResponsibleCount: participation.ResponsibleCount,
            WatchingCount: participation.WatchingCount,
            HasResponsible: participation.ResponsibleCount > 0,
            IsUnassigned: participation.ResponsibleCount == 0,
            CurrentUserParticipationType: participationType,
            CurrentUserNotificationsEnabled: participation.CurrentUserNotificationsEnabled,
            ResponsibleDisplayName: participation.ResponsibleDisplayName,
            ResponsibleIsStale: participation.ResponsibleIsStale,
            CanAssignFromList: canAssignFromList,
            CanSelfAssignFromList: canSelfAssignFromList);
    }

    private static KeepRequestNotificationInfo BuildNotificationInfo(
        bool canOperate,
        bool isOffSeason,
        KeepRequestParticipantSummary? participation)
    {
        if (!canOperate)
            return new KeepRequestNotificationInfo(false, false, "viewer");

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
        KeepRequestStatus.Received       => "received",
        KeepRequestStatus.Scheduled      => "scheduled",
        KeepRequestStatus.InProgress     => "in_progress",
        KeepRequestStatus.PendingCustomer => "pending_customer",
        KeepRequestStatus.Resolved       => "resolved",
        KeepRequestStatus.Closed         => "closed",
        KeepRequestStatus.Cancelled      => "cancelled",
        _ => throw new InvalidOperationException($"Unknown KeepRequestStatus: {status}")
    };

    private static string MapAttentionLevel(AttentionLevel level) => level switch
    {
        AttentionLevel.None         => "none",
        AttentionLevel.Waiting      => "waiting",
        AttentionLevel.NeedsAttention => "needs_attention",
        AttentionLevel.Overdue      => "overdue",
        _ => throw new InvalidOperationException($"Unknown AttentionLevel: {level}")
    };

    private static string MapWaitingDirection(WaitingDirection direction) => direction switch
    {
        WaitingDirection.None     => "none",
        WaitingDirection.Business => "business",
        WaitingDirection.Customer => "customer",
        _ => throw new InvalidOperationException($"Unknown WaitingDirection: {direction}")
    };

    private static string MapAttentionReason(AttentionReason reason) => reason switch
    {
        AttentionReason.CustomerMessage       => "customer_message",
        AttentionReason.UpdateRequest         => "update_request",
        AttentionReason.ScheduleChangeRequest => "schedule_change_request",
        AttentionReason.ChangeOrCancelRequest => "change_or_cancel_request",
        AttentionReason.Complaint             => "complaint",
        AttentionReason.FirstResponseDue      => "first_response_due",
        AttentionReason.UnresolvedFeedback    => "unresolved_feedback",
        _ => throw new InvalidOperationException($"Unknown AttentionReason: {reason}")
    };

    private static string MapFeedbackReviewAgeBucket(FeedbackReviewAgeBucket bucket) => bucket switch
    {
        FeedbackReviewAgeBucket.New     => "new",
        FeedbackReviewAgeBucket.Aging   => "aging",
        FeedbackReviewAgeBucket.Overdue => "overdue",
        _ => throw new InvalidOperationException($"Unknown FeedbackReviewAgeBucket: {bucket}")
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

    // B5 operational sort: attention-first ranking (ADR-252 default/assigned_to_me/watching/unassigned/needs_attention).
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
                1 or 2 or 4 => CompareNullableDatesAsc(
                    x.Attention.NextAttentionAtUtc ?? x.Attention.FirstResponseDueAtUtc,
                    y.Attention.NextAttentionAtUtc ?? y.Attention.FirstResponseDueAtUtc),

                3 => CompareNullableDatesAsc(x.Attention.AttentionSinceUtc, y.Attention.AttentionSinceUtc),

                5 => CompareNullableDatesAsc(x.Attention.FirstResponseDueAtUtc, y.Attention.FirstResponseDueAtUtc),

                _ => (y.LastBusinessActivityAtUtc ?? y.LastCustomerActivityAtUtc ?? y.CreatedAtUtc)
                        .CompareTo(x.LastBusinessActivityAtUtc ?? x.LastCustomerActivityAtUtc ?? x.CreatedAtUtc)
            };

            return secondaryCmp != 0 ? secondaryCmp : x.Id.CompareTo(y.Id);
        }

        private static int CompareNullableDatesAsc(DateTime? a, DateTime? b)
        {
            if (a is null && b is null) return 0;
            if (a is null) return 1;
            if (b is null) return -1;
            return a.Value.CompareTo(b.Value);
        }
    }

    // feedback_review sort: oldest unresolved-feedback attention first (ADR-252).
    private sealed class FeedbackReviewComparer : IComparer<KeepRequestSummary>
    {
        public static readonly FeedbackReviewComparer Instance = new();

        public int Compare(KeepRequestSummary? x, KeepRequestSummary? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var cmp = CompareNullableDatesAsc(x.Attention.AttentionSinceUtc, y.Attention.AttentionSinceUtc);
            return cmp != 0 ? cmp : x.Id.CompareTo(y.Id);
        }

        private static int CompareNullableDatesAsc(DateTime? a, DateTime? b)
        {
            if (a is null && b is null) return 0;
            if (a is null) return 1;
            if (b is null) return -1;
            return a.Value.CompareTo(b.Value);
        }
    }
}
