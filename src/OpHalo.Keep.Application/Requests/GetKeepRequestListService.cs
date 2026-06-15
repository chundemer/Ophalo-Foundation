using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed class GetKeepRequestListService(
    IKeepRequestListPersistence persistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IClock clock)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<GetKeepRequestListResult>> ExecuteAsync(CancellationToken ct = default)
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

        var requests = await persistence.GetOpenRequestsAsync(currentUser.AccountId, ct);
        var summaries = requests.Select(ToSummary).ToList();

        return Result<GetKeepRequestListResult>.Success(new GetKeepRequestListResult(summaries));
    }

    private static KeepRequestSummary ToSummary(KeepRequest r) =>
        new(
            r.Id,
            r.ReferenceCode,
            MapStatus(r.Status),
            r.CurrentStatusText,
            r.CustomerName,
            r.CustomerPhone,
            r.CustomerEmail,
            r.Description,
            r.LastCustomerActivityAt,
            r.LastBusinessActivityAt,
            r.CreatedAtUtc,
            r.UpdatedAtUtc);

    private static string MapStatus(KeepRequestStatus status) => status switch
    {
        KeepRequestStatus.Received => "received",
        KeepRequestStatus.InProgress => "in_progress",
        KeepRequestStatus.PendingCustomer => "pending_customer",
        KeepRequestStatus.Resolved => "resolved",
        KeepRequestStatus.Closed => "closed",
        KeepRequestStatus.Cancelled => "cancelled",
        _ => throw new InvalidOperationException($"Unknown KeepRequestStatus: {status}")
    };
}
