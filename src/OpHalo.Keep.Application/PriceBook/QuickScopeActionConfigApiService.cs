using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>One proposed slot in an Owner/Admin's replace-set write. Exactly one of
/// <see cref="CatalogItemId"/>/<see cref="OfferingAssemblyId"/> is expected — the same exclusivity
/// <see cref="QuickScopeAction.Create"/> enforces in-domain.</summary>
public sealed record ReplaceQuickScopeActionSlotApiCommand(int Order, Guid? CatalogItemId, Guid? OfferingAssemblyId);

public sealed record ReplaceQuickScopeActionSetApiCommand(IReadOnlyList<ReplaceQuickScopeActionSlotApiCommand> Slots);

/// <summary>One configured slot as seen by Owner/Admin configuration. Unlike the field read
/// (<see cref="QuickScopeActionFieldReadApiService"/>), this always includes every configured slot
/// — <see cref="IsEligible"/> false marks a target that became inactive/ineligible after
/// configuration, per build-log/119: the server never silently drops it, and correction is an
/// explicit Owner/Admin action.</summary>
public sealed record QuickScopeActionConfigRow(
    Guid Id,
    int Order,
    Guid? CatalogItemId,
    Guid? OfferingAssemblyId,
    string TargetDisplayName,
    bool IsEligible);

/// <summary>
/// API-facing orchestration for the Owner/Admin Quick scope action configuration surface (Session
/// 3, build-log/119): read the account's configured slots (including ineligible ones, marked for
/// repair) and atomically replace the entire set. Same gate composition and permission as
/// <see cref="CatalogCategoryApiService"/> — <c>PriceBookCatalogManage</c> — since this is catalog
/// configuration, not a field/technician action. <see cref="ReplaceAsync"/> is the only mutation:
/// there is no per-slot add/remove/reorder endpoint, matching
/// <see cref="IQuickScopeActionPersistence.ReplaceSetAsync"/>'s delete-all-then-insert contract.
/// </summary>
public sealed class QuickScopeActionConfigApiService(
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

    public async Task<Result<IReadOnlyList<QuickScopeActionConfigRow>>> GetAsync(CancellationToken ct)
    {
        var gate = await AuthorizeAsync(mutation: false, ct);
        if (gate.IsFailure)
            return Result<IReadOnlyList<QuickScopeActionConfigRow>>.Failure(gate.Error);

        var actions = await persistence.ListForAccountAsync(currentUser.AccountId, ct);
        var rows = await ResolveRowsAsync(actions, ct);
        return Result<IReadOnlyList<QuickScopeActionConfigRow>>.Success(rows);
    }

    public async Task<Result<IReadOnlyList<QuickScopeActionConfigRow>>> ReplaceAsync(
        ReplaceQuickScopeActionSetApiCommand command, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(mutation: true, ct);
        if (gate.IsFailure)
            return Result<IReadOnlyList<QuickScopeActionConfigRow>>.Failure(gate.Error);

        var actions = new List<QuickScopeAction>(command.Slots.Count);
        foreach (var slot in command.Slots)
        {
            var created = QuickScopeAction.Create(
                currentUser.AccountId, slot.Order, slot.CatalogItemId, slot.OfferingAssemblyId, currentUser.UserId);
            if (created.IsFailure)
                return Result<IReadOnlyList<QuickScopeActionConfigRow>>.Failure(created.Error);
            actions.Add(created.Value);
        }

        var setValidation = QuickScopeActionSetValidator.Validate(actions);
        if (setValidation.IsFailure)
            return Result<IReadOnlyList<QuickScopeActionConfigRow>>.Failure(setValidation.Error);

        foreach (var action in actions)
        {
            var eligibility = await CheckTargetEligibleForWriteAsync(action, ct);
            if (eligibility.IsFailure)
                return Result<IReadOnlyList<QuickScopeActionConfigRow>>.Failure(eligibility.Error);
        }

        var commitResult = await persistence.ReplaceSetAsync(currentUser.AccountId, actions, ct);
        if (commitResult != QuickScopeActionCommitResult.Committed)
        {
            var error = commitResult switch
            {
                QuickScopeActionCommitResult.OrderConflict => QuickScopeActionErrors.DuplicateOrder,
                QuickScopeActionCommitResult.TargetConflict => QuickScopeActionErrors.DuplicateTarget,
                QuickScopeActionCommitResult.ExclusiveTargetViolation => QuickScopeActionErrors.TargetMustBeExclusive,
                _ => QuickScopeActionErrors.DuplicateOrder,
            };
            return Result<IReadOnlyList<QuickScopeActionConfigRow>>.Failure(error);
        }

        var committed = await persistence.ListForAccountAsync(currentUser.AccountId, ct);
        var rows = await ResolveRowsAsync(committed, ct);
        return Result<IReadOnlyList<QuickScopeActionConfigRow>>.Success(rows);
    }

    /// <summary>Only an Active Common <see cref="CatalogItem"/> or an operationally eligible
    /// <see cref="OfferingAssembly"/> may be selected on write (build-log/119) — an existing
    /// configured slot may still show ineligible on read without being rejected here.</summary>
    private async Task<Result> CheckTargetEligibleForWriteAsync(QuickScopeAction action, CancellationToken ct)
    {
        if (action.CatalogItemId is { } catalogItemId)
        {
            var detail = await catalogReadPersistence.GetItemDetailAsync(currentUser.AccountId, catalogItemId, ct);
            if (detail is null)
                return Result.Failure(QuickScopeActionErrors.TargetNotFound);
            if (!detail.Item.IsCommonItem || detail.Item.ActiveState != CatalogItemActiveState.Active)
                return Result.Failure(QuickScopeActionErrors.TargetNotEligible);
            return Result.Success();
        }

        var offeringAssemblyId = action.OfferingAssemblyId!.Value;
        var assemblyDetail = await offeringAssemblyPersistence.GetDetailAsync(currentUser.AccountId, offeringAssemblyId, ct);
        if (assemblyDetail is null)
            return Result.Failure(QuickScopeActionErrors.TargetNotFound);
        if (assemblyDetail.ActiveState != CatalogActiveState.Active || !assemblyDetail.Eligibility.IsEligible)
            return Result.Failure(QuickScopeActionErrors.TargetNotEligible);

        return Result.Success();
    }

    private async Task<IReadOnlyList<QuickScopeActionConfigRow>> ResolveRowsAsync(
        IReadOnlyList<QuickScopeAction> actions, CancellationToken ct)
    {
        var rows = new List<QuickScopeActionConfigRow>(actions.Count);
        foreach (var action in actions)
        {
            if (action.CatalogItemId is { } catalogItemId)
            {
                var detail = await catalogReadPersistence.GetItemDetailAsync(currentUser.AccountId, catalogItemId, ct);
                var isEligible = detail is not null && detail.Item.IsCommonItem && detail.Item.ActiveState == CatalogItemActiveState.Active;
                var displayName = detail?.Item.DisplayName ?? "(deleted item)";
                rows.Add(new QuickScopeActionConfigRow(action.Id, action.Order, catalogItemId, null, displayName, isEligible));
            }
            else
            {
                var offeringAssemblyId = action.OfferingAssemblyId!.Value;
                var assemblyDetail = await offeringAssemblyPersistence.GetDetailAsync(currentUser.AccountId, offeringAssemblyId, ct);
                var isEligible = assemblyDetail is not null
                    && assemblyDetail.ActiveState == CatalogActiveState.Active
                    && assemblyDetail.Eligibility.IsEligible;
                var displayName = assemblyDetail?.Name ?? "(deleted assembly)";
                rows.Add(new QuickScopeActionConfigRow(action.Id, action.Order, null, offeringAssemblyId, displayName, isEligible));
            }
        }

        return rows;
    }

    private async Task<Result> AuthorizeAsync(bool mutation, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

        // Gate 1 — account access. Mutation denies Blocked and ReadOnly (e.g. OffSeason); read
        // denies Blocked only, matching CatalogReadApiService vs CatalogCategoryApiService.
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
        if (decision.IsBlocked || (mutation && decision.IsReadOnly))
            return Result.Failure(Forbidden);

        // Gate 2 — Price Book entitlement (ADR-462): plan or active capability-package enrollment.
        var featureContext = new AccountFeatureAccessContext(accountSnapshot.Plan);
        var enabled = await featureAccessResolver.IsEnabledAsync(
            currentUser.AccountId, featureContext, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, ct);
        if (!enabled)
            return Result.Failure(Forbidden);

        // Gate 3 — PriceBookCatalogManage (build-log/119): configuration follows the catalog
        // maintenance permission, not the field ScopeCapture permission.
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
