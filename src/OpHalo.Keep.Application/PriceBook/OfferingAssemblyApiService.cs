using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

public sealed record CreateOfferingAssemblyWithItemsApiItem(
    Guid CatalogItemId, decimal DefaultQuantity, bool IsOptional, int DisplayOrder);

public sealed record CreateOfferingAssemblyWithItemsApiCommand(
    Guid PrimaryCatalogItemId,
    string Name,
    PriceTreatment PriceTreatment,
    IReadOnlyList<CreateOfferingAssemblyWithItemsApiItem> Items);

/// <summary>
/// API-facing orchestration for <see cref="OfferingAssembly"/> mutations (Session 3.2a.1). Owns
/// the full auth-stack composition — <see cref="OfferingAssemblyLifecycleService"/> deliberately
/// does not — then delegates the actual lifecycle transition to it. Locked gate order (ADR-462),
/// same as <see cref="CatalogItemApiService"/>: account access gate (mutation: Blocked and
/// ReadOnly both deny) → account-aware feature resolver → user permission → lifecycle operation.
/// </summary>
public sealed class OfferingAssemblyApiService(
    OfferingAssemblyLifecycleService lifecycleService,
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

    public async Task<Result<OfferingAssembly>> CreateWithItemsAsync(
        CreateOfferingAssemblyWithItemsApiCommand command, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<OfferingAssembly>.Failure(gate.Error);

        return await lifecycleService.CreateWithItemsAsync(
            new CreateOfferingAssemblyWithItemsCommand(
                currentUser.AccountId,
                command.PrimaryCatalogItemId,
                command.Name,
                command.PriceTreatment,
                command.Items
                    .Select(i => new CreateOfferingAssemblyItemInput(i.CatalogItemId, i.DefaultQuantity, i.IsOptional, i.DisplayOrder))
                    .ToList(),
                currentUser.UserId),
            ct);
    }

    public async Task<Result<Guid>> ActivateAsync(Guid offeringAssemblyId, Guid expectedVersion, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<Guid>.Failure(gate.Error);

        return await lifecycleService.ActivateAsync(currentUser.AccountId, offeringAssemblyId, expectedVersion, ct);
    }

    public async Task<Result<Guid>> InactivateAsync(Guid offeringAssemblyId, Guid expectedVersion, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<Guid>.Failure(gate.Error);

        return await lifecycleService.InactivateAsync(currentUser.AccountId, offeringAssemblyId, expectedVersion, ct);
    }

    private async Task<Result> AuthorizeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

        // Gate 1 — account access (commercial/lifecycle). A mutation, so both Blocked and
        // ReadOnly (e.g. OffSeason) deny — unlike the capability-status read surface.
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
        if (decision.IsBlocked || decision.IsReadOnly)
            return Result.Failure(Forbidden);

        // Gate 2 — account-aware feature resolver (entitlement-only: plan or active enrollment).
        var featureContext = new AccountFeatureAccessContext(accountSnapshot.Plan);
        var enabled = await featureAccessResolver.IsEnabledAsync(
            currentUser.AccountId, featureContext, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, ct);
        if (!enabled)
            return Result.Failure(Forbidden);

        // Gate 3 — user permission.
        var roleSnapshot = await snapshotPersistence.GetAccountUserRoleSnapshotAsync(
            currentUser.AccountId, currentUser.UserId, ct);
        if (roleSnapshot is null)
            return Result.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                roleSnapshot.Role,
                roleSnapshot.MembershipStatus,
                accountSnapshot.Purpose,
                PermissionKeys.Keep.PriceBookCatalogManage))
            return Result.Failure(Forbidden);

        return Result.Success();
    }
}
