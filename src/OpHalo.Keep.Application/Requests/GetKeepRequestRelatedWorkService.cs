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

/// <summary>
/// GAP-050: a compact, account-scoped indicator of other requests for the same customer.
/// A dedicated read endpoint (not a KeepRequestDetailResult field) so mutation responses never
/// carry — and therefore never overwrite the frontend's cached copy of — this data.
/// Auth/scope preamble mirrors GetKeepRequestDetailService so the not-found/forbidden boundary
/// is identical: cross-account and row-inaccessible requests are indistinguishable at this layer.
/// Ranking, the deterministic tie-break, the `take` cap, and the exact total all execute in the
/// persistence query itself (see EfKeepRequestDetailPersistence.GetOtherCustomerRequestsAsync) —
/// this service never materializes a prolific customer's full related-request set in memory.
/// </summary>
public sealed class GetKeepRequestRelatedWorkService(
    IKeepRequestDetailPersistence persistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IClock clock)
{
    private const int MaxItems = 3;

    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<KeepRequestRelatedWorkResult>> ExecuteAsync(
        Guid requestId, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated)
            return Result<KeepRequestRelatedWorkResult>.Failure(Unauthorized);

        var userSnapshot = await persistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result<KeepRequestRelatedWorkResult>.Failure(Forbidden);

        var accountSnapshot = await persistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<KeepRequestRelatedWorkResult>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                userSnapshot.Role,
                userSnapshot.MembershipStatus,
                accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsView))
            return Result<KeepRequestRelatedWorkResult>.Failure(Forbidden);

        var nowUtc = clock.UtcNow;

        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState,
            accountSnapshot.Purpose,
            accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc,
            accountSnapshot.PastDueGraceEndsAtUtc,
            accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: true,
            nowUtc);

        var decision = accountAccessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked)
            return Result<KeepRequestRelatedWorkResult>.Failure(Forbidden);

        if (!featurePolicy.IsEnabled(accountSnapshot.Plan, FeatureKeys.Keep.OperatorQueue))
            return Result<KeepRequestRelatedWorkResult>.Failure(Forbidden);

        KeepRequestVisibilityScope scope;
        switch (userSnapshot.Role)
        {
            case AccountUserRole.Owner:
            case AccountUserRole.Admin:
            case AccountUserRole.Viewer:
                scope = KeepRequestVisibilityScope.AccountWide;
                break;
            case AccountUserRole.Operator:
                scope = KeepRequestVisibilityScope.MyWork;
                break;
            default:
                return Result<KeepRequestRelatedWorkResult>.Failure(Forbidden);
        }

        var request = await persistence.GetRequestAsync(
            requestId, currentUser.AccountId, currentUser.UserId, scope, ct);
        if (request is null)
            return Result<KeepRequestRelatedWorkResult>.Failure(KeepRequestErrors.NotFound);

        var queryResult = await persistence.GetOtherCustomerRequestsAsync(
            request.KeepCustomerId, requestId, currentUser.AccountId, currentUser.UserId, scope, MaxItems, ct);

        var items = queryResult.Items
            .Select(r => new KeepRequestRelatedWorkItem(
                r.RequestId,
                r.ReferenceCode,
                KeepRequestDetailMapper.MapStatus(r.Status),
                r.LatestActivityAtUtc))
            .ToList();

        return Result<KeepRequestRelatedWorkResult>.Success(
            new KeepRequestRelatedWorkResult(queryResult.TotalCount, items));
    }
}

public sealed record KeepRequestRelatedWorkResult(int TotalCount, IReadOnlyList<KeepRequestRelatedWorkItem> Items);

public sealed record KeepRequestRelatedWorkItem(
    Guid RequestId,
    string ReferenceCode,
    string Status,
    DateTime LastActivityAtUtc);
