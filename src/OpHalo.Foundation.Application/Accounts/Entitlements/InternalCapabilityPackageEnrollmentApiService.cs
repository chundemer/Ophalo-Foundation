using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Accounts.Entitlements;

/// <summary>
/// Internal operator entry point for <see cref="InternalCapabilityPackageEnrollmentService"/>
/// (ADR-462). Composes the auth stack the endpoint layer must never own: authenticated → caller's
/// own Internal-purpose account + role permits <c>internal.entitlements.manage</c> → target
/// account exists. The target account's commercial/lifecycle state is deliberately not consulted —
/// this is an operator tool acting on entitlement data, not a request/service-delivery action, so
/// <see cref="IAccountAccessPolicy"/> does not apply here.
/// </summary>
public sealed class InternalCapabilityPackageEnrollmentApiService(
    InternalCapabilityPackageEnrollmentService enrollmentService,
    IAccountAccessSnapshotPersistence snapshotPersistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<CapabilityPackageEnrollmentStatus>> GetStatusAsync(
        Guid accountId, string featureKey, CancellationToken ct)
    {
        var authResult = await AuthorizeAsync(accountId, ct);
        return authResult.IsFailure
            ? Result<CapabilityPackageEnrollmentStatus>.Failure(authResult.Error)
            : await enrollmentService.GetStatusAsync(accountId, featureKey, ct);
    }

    public async Task<Result<CapabilityPackageEnrollmentStatus>> EnrollAsync(
        Guid accountId, string featureKey, CancellationToken ct)
    {
        var authResult = await AuthorizeAsync(accountId, ct);
        return authResult.IsFailure
            ? Result<CapabilityPackageEnrollmentStatus>.Failure(authResult.Error)
            : await enrollmentService.EnrollAsync(accountId, featureKey, currentUser.UserId, ct);
    }

    public async Task<Result<CapabilityPackageEnrollmentStatus>> DisableAsync(
        Guid accountId, string featureKey, Guid expectedVersion, CancellationToken ct)
    {
        var authResult = await AuthorizeAsync(accountId, ct);
        return authResult.IsFailure
            ? Result<CapabilityPackageEnrollmentStatus>.Failure(authResult.Error)
            : await enrollmentService.DisableAsync(accountId, featureKey, expectedVersion, currentUser.UserId, ct);
    }

    public async Task<Result<CapabilityPackageEnrollmentStatus>> ReenableAsync(
        Guid accountId, string featureKey, Guid expectedVersion, CancellationToken ct)
    {
        var authResult = await AuthorizeAsync(accountId, ct);
        return authResult.IsFailure
            ? Result<CapabilityPackageEnrollmentStatus>.Failure(authResult.Error)
            : await enrollmentService.ReenableAsync(accountId, featureKey, expectedVersion, currentUser.UserId, ct);
    }

    /// <summary>
    /// Gate order: authenticated → caller's own role/purpose snapshot permits
    /// <c>internal.entitlements.manage</c> → target account exists. Caller and target account are
    /// deliberately different accounts (operator acting on a customer account), unlike the
    /// self-serve <c>GetAccountCapabilityPackageStatusService</c> where they're the same.
    /// </summary>
    private async Task<Result> AuthorizeAsync(Guid targetAccountId, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

        var callerRoleSnapshot = await snapshotPersistence.GetAccountUserRoleSnapshotAsync(
            currentUser.AccountId, currentUser.UserId, ct);
        if (callerRoleSnapshot is null)
            return Result.Failure(Forbidden);

        var callerAccountSnapshot = await snapshotPersistence.GetAccountAccessSnapshotAsync(
            currentUser.AccountId, ct);
        if (callerAccountSnapshot is null)
            return Result.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                callerRoleSnapshot.Role,
                callerRoleSnapshot.MembershipStatus,
                callerAccountSnapshot.Purpose,
                PermissionKeys.Internal.EntitlementsManage))
            return Result.Failure(Forbidden);

        var targetAccountSnapshot = await snapshotPersistence.GetAccountAccessSnapshotAsync(
            targetAccountId, ct);
        return targetAccountSnapshot is null ? Result.Failure(AccountErrors.NotFound) : Result.Success();
    }
}
