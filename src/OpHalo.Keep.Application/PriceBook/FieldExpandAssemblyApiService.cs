using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>Expand-assembly command (Session 3.4e, build-log/118): the caller only ever supplies
/// which assembly and which optional associated items to exclude — no snapshot, no display order,
/// no line ids. Everything else is server-resolved inside the atomic expansion transaction.</summary>
public sealed record ExpandAssemblyApiCommand(Guid OfferingAssemblyId, IReadOnlyCollection<Guid> ExcludedOptionalItemIds);

/// <summary>
/// API-facing orchestration for the technician-reachable, atomic assembly-expansion endpoint
/// (Session 3.4e, build-log/118): the sole path for a <c>PrimaryOffering</c>/<c>AssociatedItem</c>
/// line, beside <see cref="FieldProposedScopeSelectionApiService"/> (the sole path for
/// <c>KnownCatalogItem</c>/<c>OffCatalogItem</c>). Gate composition (including the BL142 Session 1
/// server-owned release gate, <see cref="IReleaseGatePolicy"/>, ADR-496) and row-visibility
/// ordering match <see cref="FieldProposedScopeSelectionApiService"/> exactly, restated here per
/// build-log/118 ("stated explicitly, not inherited by reference") — visibility is checked (via
/// <see cref="EditProposedScopeService.VerifyRequestVisibleAsync"/>) before the atomic expansion
/// transaction ever runs, so an invisible scope 404s before the assembly's existence/eligibility is
/// evaluated.
/// </summary>
public sealed class FieldExpandAssemblyApiService(
    EditProposedScopeService editService,
    IAccountAccessSnapshotPersistence snapshotPersistence,
    ICurrentUser currentUser,
    IAccountAccessPolicy accountAccessPolicy,
    IAccountFeatureAccessResolver featureAccessResolver,
    IReleaseGatePolicy releaseGatePolicy,
    IUserAccessPolicy userAccessPolicy,
    IClock clock)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<ExpandAssemblyResultValue>> ExpandAssemblyAsync(
        Guid proposedScopeId, ExpandAssemblyApiCommand command, Guid expectedVersion, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<ExpandAssemblyResultValue>.Failure(gate.Error);

        // Row visibility before the atomic expansion transaction ever runs - an invisible scope
        // must 404 before the assembly's existence/eligibility is evaluated.
        var visibility = await editService.VerifyRequestVisibleAsync(
            currentUser.AccountId, proposedScopeId, currentUser.UserId, gate.Value, ct);
        if (visibility.IsFailure)
            return Result<ExpandAssemblyResultValue>.Failure(visibility.Error);

        return await editService.ExpandAssemblyAsync(
            new ExpandAssemblyCommand(
                currentUser.AccountId, proposedScopeId, expectedVersion, command.OfferingAssemblyId,
                command.ExcludedOptionalItemIds, currentUser.UserId),
            ct);
    }

    /// <summary>Returns the row-authorization scope (derived from role) on success - same contract as
    /// <see cref="FieldProposedScopeSelectionApiService"/>'s identical private method, restated here
    /// per build-log/118 ("stated explicitly, not inherited by reference").</summary>
    private async Task<Result<KeepRequestVisibilityScope>> AuthorizeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<KeepRequestVisibilityScope>.Failure(Unauthorized);

        // Gate 1 - account access (commercial/lifecycle). A mutation, so both Blocked and
        // ReadOnly (e.g. OffSeason) deny - matches ProposedScopeApiService's mutation posture.
        var accountSnapshot = await snapshotPersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);

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
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);

        // Gate 2 - Price Book entitlement (ADR-462): plan or active capability-package enrollment.
        var featureContext = new AccountFeatureAccessContext(accountSnapshot.Plan);
        var enabled = await featureAccessResolver.IsEnabledAsync(
            currentUser.AccountId, featureContext, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, ct);
        if (!enabled)
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);

        // Gate 2b - server-owned release gate (BL142 Session 1, ADR-496): entitlement alone never
        // exposes Proposed Work before it is explicitly released. Fails closed independently of
        // gate 2 above.
        if (!releaseGatePolicy.IsProposedWorkReleased())
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);

        // Gate 3 - user permissions: RequestsOperate (B2 operator-write gate) AND ScopeCapture
        // (ADR-480) - two independent permission checks, not a single combined key.
        var roleSnapshot = await snapshotPersistence.GetAccountUserRoleSnapshotAsync(
            currentUser.AccountId, currentUser.UserId, ct);
        if (roleSnapshot is null)
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsOperate) ||
            !userAccessPolicy.IsPermitted(
                roleSnapshot.Role, roleSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.ScopeCapture))
        {
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);
        }

        var scope = roleSnapshot.Role is AccountUserRole.Owner or AccountUserRole.Admin
            ? KeepRequestVisibilityScope.AccountWide
            : KeepRequestVisibilityScope.MyWork;

        return Result<KeepRequestVisibilityScope>.Success(scope);
    }
}
