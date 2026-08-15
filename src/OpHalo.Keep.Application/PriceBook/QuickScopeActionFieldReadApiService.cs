using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>One currently eligible configured Quick scope action, price-free, as seen by the field
/// composer. Unlike <see cref="QuickScopeActionConfigRow"/>, ineligible configured slots are
/// omitted entirely rather than marked — a technician has no repair action to take on them
/// (build-log/119).</summary>
public sealed record QuickScopeActionFieldRow(
    Guid Id,
    int Order,
    Guid? CatalogItemId,
    Guid? OfferingAssemblyId,
    string TargetDisplayName);

/// <summary>
/// API-facing orchestration for the technician-reachable, price-free Quick scope action read
/// (Session 3, build-log/119): the ordered set of currently eligible configured actions only. Sits
/// beside <see cref="FieldCatalogReadApiService"/>/<see cref="FieldOfferingAssemblyReadApiService"/>
/// rather than reusing <see cref="QuickScopeActionConfigApiService"/> — gate 3 is
/// <c>RequestsOperate</c> AND <c>ScopeCapture</c> (ADR-480), not <c>PriceBookCatalogManage</c>, and
/// <see cref="QuickScopeActionFieldRow"/> structurally carries no price field.
/// </summary>
public sealed class QuickScopeActionFieldReadApiService(
    IQuickScopeActionPersistence persistence,
    ICatalogReadPersistence catalogReadPersistence,
    IOfferingAssemblyPersistence offeringAssemblyPersistence,
    IAccountAccessSnapshotPersistence snapshotPersistence,
    ICurrentUser currentUser,
    IAccountAccessPolicy accountAccessPolicy,
    IAccountFeatureAccessResolver featureAccessResolver,
    IUserAccessPolicy userAccessPolicy,
    IClock clock)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<IReadOnlyList<QuickScopeActionFieldRow>>> ListAsync(CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<IReadOnlyList<QuickScopeActionFieldRow>>.Failure(gate.Error);

        var actions = await persistence.ListForAccountAsync(currentUser.AccountId, ct);

        var rows = new List<QuickScopeActionFieldRow>(actions.Count);
        foreach (var action in actions)
        {
            if (action.CatalogItemId is { } catalogItemId)
            {
                var detail = await catalogReadPersistence.GetItemDetailAsync(currentUser.AccountId, catalogItemId, ct);
                if (detail is null || !detail.Item.IsCommonItem || detail.Item.ActiveState != CatalogItemActiveState.Active)
                    continue;

                rows.Add(new QuickScopeActionFieldRow(action.Id, action.Order, catalogItemId, null, detail.Item.DisplayName));
            }
            else
            {
                var offeringAssemblyId = action.OfferingAssemblyId!.Value;
                var assemblyDetail = await offeringAssemblyPersistence.GetDetailAsync(currentUser.AccountId, offeringAssemblyId, ct);
                if (assemblyDetail is null
                    || assemblyDetail.ActiveState != CatalogActiveState.Active
                    || !assemblyDetail.Eligibility.IsEligible)
                    continue;

                rows.Add(new QuickScopeActionFieldRow(action.Id, action.Order, null, offeringAssemblyId, assemblyDetail.Name));
            }
        }

        return Result<IReadOnlyList<QuickScopeActionFieldRow>>.Success(rows);
    }

    private async Task<Result> AuthorizeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

        // Gate 1 — account access. A read: only Blocked denies, matching FieldCatalogReadApiService.
        var accountSnapshot = await snapshotPersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result.Failure(Forbidden);

        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState,
            accountSnapshot.Purpose,
            accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc,
            accountSnapshot.PastDueGraceEndsAtUtc,
            accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: false,
            clock.UtcNow);

        var decision = accountAccessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked)
            return Result.Failure(Forbidden);

        // Gate 2 — Price Book entitlement (ADR-462): plan or active capability-package enrollment.
        var featureContext = new AccountFeatureAccessContext(accountSnapshot.Plan);
        var enabled = await featureAccessResolver.IsEnabledAsync(
            currentUser.AccountId, featureContext, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, ct);
        if (!enabled)
            return Result.Failure(Forbidden);

        // Gate 3 — RequestsOperate (B2 operator gate) AND ScopeCapture (ADR-480), not
        // PriceBookCatalogManage — matches FieldCatalogReadApiService/FieldOfferingAssemblyReadApiService.
        var roleSnapshot = await snapshotPersistence.GetAccountUserRoleSnapshotAsync(
            currentUser.AccountId, currentUser.UserId, ct);
        if (roleSnapshot is null)
            return Result.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsOperate) ||
            !userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.ScopeCapture))
        {
            return Result.Failure(Forbidden);
        }

        return Result.Success();
    }
}
