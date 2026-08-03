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

public sealed record CreateCatalogItemApiCommand(
    CatalogItemType Type,
    string DisplayName,
    string UnitOfMeasure,
    string Currency,
    string? ExternalKey,
    Guid? CategoryId,
    bool IsCommonItem);

public sealed record PublishCatalogItemPriceApiCommand(decimal? Cost, decimal? SellPrice, string Reason);

/// <summary>
/// API-facing orchestration for <see cref="CatalogItem"/> mutations (Session 2a.2). Owns the
/// full auth-stack composition — <see cref="CatalogItemLifecycleService"/> deliberately does
/// not — then delegates the actual lifecycle transition to it. Locked gate order (ADR-462):
/// account access gate → account-aware feature resolver → user permission → lifecycle operation.
/// </summary>
public sealed class CatalogItemApiService(
    CatalogItemLifecycleService lifecycleService,
    PriceBookPublishService publishService,
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

    public async Task<Result<CatalogItem>> CreateDraftAsync(CreateCatalogItemApiCommand command, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<CatalogItem>.Failure(gate.Error);

        return await lifecycleService.CreateDraftAsync(
            new CreateCatalogItemCommand(
                currentUser.AccountId,
                command.Type,
                command.DisplayName,
                command.UnitOfMeasure,
                command.Currency,
                command.ExternalKey,
                command.CategoryId,
                command.IsCommonItem,
                currentUser.UserId),
            ct);
    }

    public async Task<Result<Guid>> ActivateAsync(Guid catalogItemId, Guid expectedVersion, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<Guid>.Failure(gate.Error);

        return await lifecycleService.ActivateAsync(currentUser.AccountId, catalogItemId, expectedVersion, ct);
    }

    public async Task<Result<Guid>> InactivateAsync(Guid catalogItemId, Guid expectedVersion, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<Guid>.Failure(gate.Error);

        return await lifecycleService.InactivateAsync(currentUser.AccountId, catalogItemId, expectedVersion, ct);
    }

    public async Task<Result<AddCatalogItemAliasResult>> AddAliasAsync(
        Guid catalogItemId, Guid expectedVersion, string aliasText, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<AddCatalogItemAliasResult>.Failure(gate.Error);

        return await lifecycleService.AddAliasAsync(
            currentUser.AccountId, catalogItemId, expectedVersion, aliasText, currentUser.UserId, ct);
    }

    public async Task<Result<Guid>> ActivateAliasAsync(Guid catalogItemId, Guid aliasId, Guid expectedVersion, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<Guid>.Failure(gate.Error);

        return await lifecycleService.ActivateAliasAsync(currentUser.AccountId, catalogItemId, aliasId, expectedVersion, ct);
    }

    public async Task<Result<Guid>> InactivateAliasAsync(Guid catalogItemId, Guid aliasId, Guid expectedVersion, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<Guid>.Failure(gate.Error);

        return await lifecycleService.InactivateAliasAsync(currentUser.AccountId, catalogItemId, aliasId, expectedVersion, ct);
    }

    public async Task<Result<PublishCatalogItemPriceResult>> PublishPriceAsync(
        Guid catalogItemId, PublishCatalogItemPriceApiCommand command, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<PublishCatalogItemPriceResult>.Failure(gate.Error);

        return await publishService.PublishAsync(
            new PublishCatalogItemPriceCommand(
                currentUser.AccountId,
                catalogItemId,
                command.Cost,
                command.SellPrice,
                command.Reason,
                currentUser.UserId),
            ct);
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
