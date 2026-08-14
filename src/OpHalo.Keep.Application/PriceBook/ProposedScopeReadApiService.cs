using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

public sealed record CurrentProposedScopeForRequestResult(bool HasScope, ProposedScope? Scope);

/// <summary>
/// API-facing read orchestration for <see cref="ProposedScope"/> (Session 3.4a). This is the
/// technician-reachable capture entry point's entitlement probe (build-log/118, decision 1): a 403
/// from <see cref="AuthorizeAsync"/> means the caller renders nothing, so no separate
/// <c>CanCaptureProposedScope</c> flag is added to <c>AvailableActionsMetadata</c>. Gate 2 (Price
/// Book entitlement) and gate 3 (<c>RequestsOperate</c> AND <c>ScopeCapture</c>) mirror
/// <see cref="ProposedScopeApiService"/> exactly; gate 1 is read-only (Blocked-only denies —
/// <c>ReadOnly</c>, e.g. OffSeason, may still read), unlike the mutation gate.
/// </summary>
public sealed class ProposedScopeReadApiService(
    IProposedScopePersistence persistence,
    IKeepRequestDetailPersistence requestPersistence,
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

    public async Task<Result<CurrentProposedScopeForRequestResult>> GetCurrentForRequestAsync(Guid requestId, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<CurrentProposedScopeForRequestResult>.Failure(gate.Error);

        var request = await requestPersistence.GetRequestAsync(
            requestId, currentUser.AccountId, currentUser.UserId, gate.Value, ct);
        if (request is null)
            return Result<CurrentProposedScopeForRequestResult>.Failure(KeepRequestErrors.NotFound);

        var scope = await persistence.GetCurrentForRequestAsync(currentUser.AccountId, requestId, ct);
        return Result<CurrentProposedScopeForRequestResult>.Success(
            new CurrentProposedScopeForRequestResult(scope is not null, scope));
    }

    public async Task<Result<ProposedScope>> GetByIdAsync(Guid proposedScopeId, CancellationToken ct)
    {
        var gate = await AuthorizeAsync(ct);
        if (gate.IsFailure)
            return Result<ProposedScope>.Failure(gate.Error);

        var scope = await persistence.GetByIdAsync(currentUser.AccountId, proposedScopeId, ct);
        if (scope is null)
            return Result<ProposedScope>.Failure(ProposedScopeErrors.NotFound);

        var request = await requestPersistence.GetRequestAsync(
            scope.RequestId, currentUser.AccountId, currentUser.UserId, gate.Value, ct);
        if (request is null)
            return Result<ProposedScope>.Failure(KeepRequestErrors.NotFound);

        return Result<ProposedScope>.Success(scope);
    }

    /// <summary>Returns the row-authorization scope (derived from role) on success, matching
    /// <see cref="ProposedScopeApiService.AuthorizeAsync"/>'s contract.</summary>
    private async Task<Result<KeepRequestVisibilityScope>> AuthorizeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<KeepRequestVisibilityScope>.Failure(Unauthorized);

        // Gate 1 — account access (commercial/lifecycle). Read-only, so only Blocked denies;
        // ReadOnly (e.g. OffSeason) may still read.
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
        if (decision.IsBlocked)
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);

        // Gate 2 — Price Book entitlement (ADR-462): plan or active capability-package enrollment.
        var featureContext = new AccountFeatureAccessContext(accountSnapshot.Plan);
        var enabled = await featureAccessResolver.IsEnabledAsync(
            currentUser.AccountId, featureContext, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, ct);
        if (!enabled)
            return Result<KeepRequestVisibilityScope>.Failure(Forbidden);

        // Gate 3 — user permissions: RequestsOperate (B2 operator gate) AND ScopeCapture (ADR-480).
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
