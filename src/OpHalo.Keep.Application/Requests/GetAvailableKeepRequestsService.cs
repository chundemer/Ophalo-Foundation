using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed class GetAvailableKeepRequestsService(
    IKeepRequestListPersistence persistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IClock clock,
    IKeepRequestListCursorProtector cursorProtector)
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 50;

    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<GetAvailableKeepRequestsResult>> ExecuteAsync(
        KeepAvailableRequestQuery query,
        CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated)
            return Result<GetAvailableKeepRequestsResult>.Failure(Unauthorized);

        var userSnapshot = await persistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result<GetAvailableKeepRequestsResult>.Failure(Forbidden);

        var accountSnapshot = await persistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<GetAvailableKeepRequestsResult>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                userSnapshot.Role,
                userSnapshot.MembershipStatus,
                accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsView))
            return Result<GetAvailableKeepRequestsResult>.Failure(Forbidden);

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
            return Result<GetAvailableKeepRequestsResult>.Failure(Forbidden);

        if (!featurePolicy.IsEnabled(accountSnapshot.Plan, FeatureKeys.Keep.OperatorQueue))
            return Result<GetAvailableKeepRequestsResult>.Failure(Forbidden);

        // Operator-only: Owner/Admin and Viewer use GET /keep/requests?view=unassigned.
        if (userSnapshot.Role != AccountUserRole.Operator)
            return Result<GetAvailableKeepRequestsResult>.Failure(Forbidden);

        // Limit: default 20, valid range 1–50.
        var limit = query.Limit ?? DefaultLimit;
        if (limit < 1 || limit > MaxLimit)
            return Result<GetAvailableKeepRequestsResult>.Failure(KeepRequestErrors.RequestListInvalidLimit);

        // Cursor: fingerprinted to route version, accountId, and accountUserId so cursors
        // cannot be replayed across accounts, users, or endpoints.
        var fingerprint = ComputeFingerprint(currentUser.AccountId, userSnapshot.AccountUserId);
        KeepRequestAvailableCursorPayload? cursorPayload = null;
        if (!string.IsNullOrEmpty(query.Cursor))
        {
            if (!KeepRequestAvailableCursor.TryDecode(
                    cursorProtector, query.Cursor, fingerprint, out cursorPayload))
                return Result<GetAvailableKeepRequestsResult>.Failure(KeepRequestErrors.RequestListInvalidCursor);
        }

        DateTime? cursorCreatedAtUtc = cursorPayload is not null
            ? new DateTime(cursorPayload.CreatedAtTick, DateTimeKind.Utc)
            : null;

        var rows = await persistence.GetAvailableRequestsAsync(
            currentUser.AccountId,
            userSnapshot.AccountUserId,
            limit + 1,
            cursorCreatedAtUtc,
            cursorPayload?.LastId,
            ct);

        var hasMore = rows.Count > limit;
        var page = hasMore ? rows.Take(limit).ToList() : (IReadOnlyList<KeepRequestAvailableRow>)rows;

        var isOffSeason = accountSnapshot.OperatingMode == AccountOperatingMode.OffSeason;
        var canOperate = userAccessPolicy.IsPermitted(
            userSnapshot.Role,
            userSnapshot.MembershipStatus,
            accountSnapshot.Purpose,
            PermissionKeys.Keep.RequestsOperate);

        // Equivalent coarse policy primitive: ApplyAvailable guarantees non-terminal rows on which
        // the current eligible user is never the Responsible, so CanSelfAssign reduces to canWrite.
        // CanWatch additionally honours the policy's "no current participation" condition: a row the
        // user already Watches reports CanWatch=false, matching KeepRequestActionPolicy (G4e-3).
        var canWrite = canOperate && !isOffSeason;

        var items = page
            .Select(r => new KeepRequestAvailableItem(
                RequestId:          r.RequestId,
                ReferenceCode:      r.ReferenceCode,
                CustomerName:       r.CustomerName,
                Status:             MapStatus(r.Status),
                CreatedAtUtc:       r.CreatedAtUtc,
                AttentionSinceUtc:  r.AttentionSinceUtc,
                NextAttentionAtUtc: r.NextAttentionAtUtc,
                PriorityBand:       MapPriorityBand(r.PriorityBand),
                AttentionLevel:     MapAttentionLevel(r.AttentionLevel),
                DescriptionPreview: BuildDescriptionPreview(r.RawDescriptionPrefix, r.DescriptionWasTruncated),
                Version:            r.Version,
                CanSelfAssign:      canWrite,
                CanWatch:           canWrite && !r.CurrentUserIsWatching))
            .ToList();

        string? nextCursor = null;
        if (hasMore && page.Count > 0)
        {
            var last = page[^1];
            nextCursor = KeepRequestAvailableCursor.Encode(
                cursorProtector, fingerprint, last.RequestId, last.CreatedAtUtc);
        }

        var pageInfo = new KeepRequestPageInfo(Limit: limit, HasMore: hasMore, NextCursor: nextCursor);
        return Result<GetAvailableKeepRequestsResult>.Success(
            new GetAvailableKeepRequestsResult(items, pageInfo));
    }

    // --- Helpers ---

    private static string ComputeFingerprint(Guid accountId, Guid accountUserId)
    {
        var raw = $"available:v1:{accountId}:{accountUserId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildDescriptionPreview(string rawPrefix, bool wasTruncated)
    {
        if (string.IsNullOrEmpty(rawPrefix)) return string.Empty;

        var sb = new StringBuilder(rawPrefix.Length);
        var scalars = 0;
        // Truncated: collect 159 scalars then append '…'. Not truncated: all ≤ 160 by DB guarantee.
        var limit = wasTruncated ? 159 : 160;

        foreach (var rune in rawPrefix.EnumerateRunes())
        {
            if (scalars >= limit) break;
            sb.Append(Rune.IsWhiteSpace(rune) ? " " : rune.ToString());
            scalars++;
        }

        if (wasTruncated) sb.Append('…');
        return sb.ToString();
    }

    private static string MapStatus(KeepRequestStatus status) => status switch
    {
        KeepRequestStatus.Received        => "received",
        KeepRequestStatus.Scheduled       => "scheduled",
        KeepRequestStatus.InProgress      => "in_progress",
        KeepRequestStatus.PendingCustomer => "pending_customer",
        KeepRequestStatus.Resolved        => "resolved",
        KeepRequestStatus.Closed          => "closed",
        KeepRequestStatus.Cancelled       => "cancelled",
        _ => throw new InvalidOperationException($"Unknown KeepRequestStatus: {status}")
    };

    private static string MapPriorityBand(PriorityBand band) => band switch
    {
        PriorityBand.Priority => "priority",
        PriorityBand.Standard => "standard",
        _ => throw new InvalidOperationException($"Unknown PriorityBand: {band}")
    };

    private static string MapAttentionLevel(AttentionLevel level) => level switch
    {
        AttentionLevel.None           => "none",
        AttentionLevel.Waiting        => "waiting",
        AttentionLevel.NeedsAttention => "needs_attention",
        AttentionLevel.Overdue        => "overdue",
        _ => throw new InvalidOperationException($"Unknown AttentionLevel: {level}")
    };
}

// --- Query / Result types ---

public sealed record KeepAvailableRequestQuery(int? Limit = null, string? Cursor = null);

public sealed record GetAvailableKeepRequestsResult(
    IReadOnlyList<KeepRequestAvailableItem> Requests,
    KeepRequestPageInfo PageInfo);

public sealed record KeepRequestAvailableItem(
    Guid RequestId,
    string ReferenceCode,
    string CustomerName,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? AttentionSinceUtc,
    DateTime? NextAttentionAtUtc,
    string PriorityBand,
    string AttentionLevel,
    string DescriptionPreview,
    Guid Version,
    bool CanSelfAssign,
    bool CanWatch);

// --- Cursor mechanics (Application concern; shares the HMAC protector injected in DI) ---

internal static class KeepRequestAvailableCursor
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Encode(
        IKeepRequestListCursorProtector protector,
        string fingerprint,
        Guid lastId,
        DateTime createdAtUtc)
    {
        var payload = new KeepRequestAvailableCursorPayload(
            CurrentVersion, fingerprint, lastId, createdAtUtc.Ticks);
        return protector.Protect(JsonSerializer.Serialize(payload, JsonOptions));
    }

    public static bool TryDecode(
        IKeepRequestListCursorProtector protector,
        string cursor,
        string expectedFingerprint,
        out KeepRequestAvailableCursorPayload? payload)
    {
        payload = null;
        if (!protector.TryUnprotect(cursor, out var json) || json is null)
            return false;
        try
        {
            payload = JsonSerializer.Deserialize<KeepRequestAvailableCursorPayload>(json, JsonOptions);
            if (payload is null || payload.Version != CurrentVersion) return false;
            if (payload.Fingerprint != expectedFingerprint) return false;
            if (payload.LastId == Guid.Empty) return false;
            if (payload.CreatedAtTick < 0 || payload.CreatedAtTick > DateTime.MaxValue.Ticks) return false;
            return true;
        }
        catch { return false; }
    }
}

internal sealed record KeepRequestAvailableCursorPayload(
    int Version,
    string Fingerprint,
    Guid LastId,
    long CreatedAtTick);
