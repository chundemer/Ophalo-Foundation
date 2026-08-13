using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Accounts.Entitlements;

public sealed record CapabilityPackageEnrollmentStatus(
    Guid AccountId,
    string FeatureKey,
    CapabilityEnrollmentStatus Status,
    DateTime? EnabledAtUtc,
    DateTime? DisabledAtUtc,
    Guid ChangedByAccountUserId,
    Guid ConcurrencyVersion);

/// <summary>
/// Orchestrates <see cref="AccountCapabilityPackageEnrollment"/> enroll/disable/reenable against
/// persistence (internal entitlement operator path, ADR-462). Deliberately takes
/// <c>accountId</c>/actor ids as plain parameters rather than resolving them itself — auth-stack
/// composition is owned by the caller (<see cref="InternalCapabilityPackageEnrollmentApiService"/>),
/// matching <c>OfferingAssemblyLifecycleService</c>.
/// </summary>
public sealed class InternalCapabilityPackageEnrollmentService(
    IAccountCapabilityPackageEnrollmentPersistence persistence,
    IClock clock)
{
    public async Task<Result<CapabilityPackageEnrollmentStatus>> GetStatusAsync(
        Guid accountId, string featureKey, CancellationToken ct)
    {
        if (!CapabilityPackageFeatureKeys.IsAllowed(featureKey))
            return Result<CapabilityPackageEnrollmentStatus>.Failure(AccountCapabilityPackageEnrollmentErrors.UnknownFeatureKey);

        var enrollment = await persistence.GetByAccountAndFeatureKeyAsync(accountId, featureKey, ct);
        return enrollment is null
            ? Result<CapabilityPackageEnrollmentStatus>.Failure(AccountCapabilityPackageEnrollmentErrors.NotFound)
            : Result<CapabilityPackageEnrollmentStatus>.Success(ToStatus(enrollment));
    }

    /// <summary>
    /// Creates the first-ever row for this (AccountId, FeatureKey) pair. A row already existing
    /// in any status — Enrolled or Disabled — means Enroll must not run again; the uniqueness
    /// constraint keeps every later transition on that same row, so a disabled account is
    /// re-granted via <see cref="ReenableAsync"/>, never a second Enroll.
    /// </summary>
    public async Task<Result<CapabilityPackageEnrollmentStatus>> EnrollAsync(
        Guid accountId, string featureKey, Guid changedByAccountUserId, CancellationToken ct)
    {
        if (!CapabilityPackageFeatureKeys.IsAllowed(featureKey))
            return Result<CapabilityPackageEnrollmentStatus>.Failure(AccountCapabilityPackageEnrollmentErrors.UnknownFeatureKey);

        var existing = await persistence.GetByAccountAndFeatureKeyAsync(accountId, featureKey, ct);
        if (existing is not null)
            return Result<CapabilityPackageEnrollmentStatus>.Failure(AccountCapabilityPackageEnrollmentErrors.AlreadyEnrolled);

        var createResult = AccountCapabilityPackageEnrollment.Enroll(
            accountId, featureKey, changedByAccountUserId, clock.UtcNow);
        if (createResult.IsFailure)
            return Result<CapabilityPackageEnrollmentStatus>.Failure(createResult.Error);

        var enrollment = createResult.Value;
        var commitResult = await persistence.AddAsync(enrollment, ct);
        return commitResult switch
        {
            AccountCapabilityPackageEnrollmentCommitResult.Committed =>
                Result<CapabilityPackageEnrollmentStatus>.Success(ToStatus(enrollment)),
            AccountCapabilityPackageEnrollmentCommitResult.AlreadyExists =>
                Result<CapabilityPackageEnrollmentStatus>.Failure(AccountCapabilityPackageEnrollmentErrors.EnrollmentAlreadyExists),
            _ => throw new InvalidOperationException($"Unhandled {nameof(AccountCapabilityPackageEnrollmentCommitResult)}: {commitResult}"),
        };
    }

    public Task<Result<CapabilityPackageEnrollmentStatus>> DisableAsync(
        Guid accountId, string featureKey, Guid expectedVersion, Guid changedByAccountUserId, CancellationToken ct) =>
        ApplyTransitionAsync(accountId, featureKey, expectedVersion,
            enrollment => enrollment.Disable(changedByAccountUserId, clock.UtcNow), ct);

    public Task<Result<CapabilityPackageEnrollmentStatus>> ReenableAsync(
        Guid accountId, string featureKey, Guid expectedVersion, Guid changedByAccountUserId, CancellationToken ct) =>
        ApplyTransitionAsync(accountId, featureKey, expectedVersion,
            enrollment => enrollment.Reenable(changedByAccountUserId, clock.UtcNow), ct);

    private async Task<Result<CapabilityPackageEnrollmentStatus>> ApplyTransitionAsync(
        Guid accountId,
        string featureKey,
        Guid expectedVersion,
        Func<AccountCapabilityPackageEnrollment, Result> transition,
        CancellationToken ct)
    {
        if (!CapabilityPackageFeatureKeys.IsAllowed(featureKey))
            return Result<CapabilityPackageEnrollmentStatus>.Failure(AccountCapabilityPackageEnrollmentErrors.UnknownFeatureKey);

        var enrollment = await persistence.GetByAccountAndFeatureKeyAsync(accountId, featureKey, ct);
        if (enrollment is null)
            return Result<CapabilityPackageEnrollmentStatus>.Failure(AccountCapabilityPackageEnrollmentErrors.NotFound);

        if (enrollment.ConcurrencyVersion != expectedVersion)
            return Result<CapabilityPackageEnrollmentStatus>.Failure(AccountCapabilityPackageEnrollmentErrors.VersionMismatch);

        var transitionResult = transition(enrollment);
        if (transitionResult.IsFailure)
            return Result<CapabilityPackageEnrollmentStatus>.Failure(transitionResult.Error);

        var commitResult = await persistence.CommitAsync(enrollment, ct);
        return commitResult switch
        {
            AccountCapabilityPackageEnrollmentCommitResult.Committed =>
                Result<CapabilityPackageEnrollmentStatus>.Success(ToStatus(enrollment)),
            AccountCapabilityPackageEnrollmentCommitResult.ConcurrencyConflict =>
                Result<CapabilityPackageEnrollmentStatus>.Failure(AccountCapabilityPackageEnrollmentErrors.VersionMismatch),
            _ => throw new InvalidOperationException($"Unhandled {nameof(AccountCapabilityPackageEnrollmentCommitResult)}: {commitResult}"),
        };
    }

    private static CapabilityPackageEnrollmentStatus ToStatus(AccountCapabilityPackageEnrollment enrollment) =>
        new(enrollment.AccountId,
            enrollment.FeatureKey,
            enrollment.Status,
            enrollment.EnabledAt,
            enrollment.DisabledAt,
            enrollment.ChangedByAccountUserId,
            enrollment.ConcurrencyVersion);
}
