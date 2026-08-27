using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Requests;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

public sealed record GetActualWorkRecorderCandidatesResult(
    IReadOnlyList<ActualWorkRecorderCandidateItem> Candidates);

public sealed record ActualWorkRecorderCandidateItem(
    Guid AccountUserId,
    string DisplayName,
    string Role);

/// <summary>
/// Account-wide, Owner/Admin-only read of the members who are eligible to hold an Actual Work
/// Draft as its recorder (1a-ii recovery UI). Eligibility is exactly the GAP-055 recorder
/// predicate — an active member with both <c>RequestsOperate</c> and <c>ActualWorkCapture</c> —
/// the same invariant <see cref="ActualWorkDraftApiService.TransferRecorderAsync"/> enforces
/// server-side (1a-i). Recorder eligibility is not request-scoped, so this list is not either.
///
/// Composition order (ADR-462): account access gate → feature resolver → caller permission →
/// Owner/Admin gate. A non-Owner/Admin caller and a caller without the price-book entitlement each
/// receive an opaque 403; the response never enumerates members the caller could not otherwise
/// see, and it carries no permission detail.
/// </summary>
public sealed class GetActualWorkRecorderCandidatesService(
    IKeepRequestOperatePersistence operatePersistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IAccountFeatureAccessResolver featureAccessResolver,
    IClock clock)
{
    private static readonly Error Unauthorized = Error.Create("auth.unauthorized", "Authentication required.");
    private static readonly Error Forbidden = Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<GetActualWorkRecorderCandidatesResult>> ExecuteAsync(CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated)
            return Result<GetActualWorkRecorderCandidatesResult>.Failure(Unauthorized);

        var userSnapshot = await operatePersistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result<GetActualWorkRecorderCandidatesResult>.Failure(Forbidden);

        var accountSnapshot = await operatePersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<GetActualWorkRecorderCandidatesResult>.Failure(Forbidden);

        // Gate 1: account access. Candidate lookup is read metadata — Blocked-only denies; a
        // ReadOnly (e.g. OffSeason) account may still resolve the list.
        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState, accountSnapshot.Purpose, accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc, accountSnapshot.PastDueGraceEndsAtUtc, accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: true, clock.UtcNow);
        if (accountAccessPolicy.Evaluate(accessContext).IsBlocked)
            return Result<GetActualWorkRecorderCandidatesResult>.Failure(Forbidden);

        // Gate 2: entitlement (plan or active capability-package enrollment).
        var entitled = await featureAccessResolver.IsEnabledAsync(
            currentUser.AccountId, new AccountFeatureAccessContext(accountSnapshot.Plan),
            CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, ct);
        if (!entitled)
            return Result<GetActualWorkRecorderCandidatesResult>.Failure(Forbidden);

        // Gate 3: caller permission. Gate 4: Owner/Admin only — a recorder transfer is an
        // office-role recovery action, never a field-role one.
        if (!userAccessPolicy.IsPermitted(
                userSnapshot.Role, userSnapshot.MembershipStatus, accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsOperate))
            return Result<GetActualWorkRecorderCandidatesResult>.Failure(Forbidden);

        if (userSnapshot.Role is not (AccountUserRole.Owner or AccountUserRole.Admin))
            return Result<GetActualWorkRecorderCandidatesResult>.Failure(Forbidden);

        // All rows returned here are active Owner/Admin/Operator members; filter to the ones who
        // actually hold the recorder predicate under this account's purpose.
        var records = await operatePersistence.GetParticipantCandidatesAsync(currentUser.AccountId, ct);

        var candidates = records
            .Where(r =>
                userAccessPolicy.IsPermitted(
                    r.Role, MembershipStatus.Active, accountSnapshot.Purpose,
                    PermissionKeys.Keep.RequestsOperate) &&
                userAccessPolicy.IsPermitted(
                    r.Role, MembershipStatus.Active, accountSnapshot.Purpose,
                    PermissionKeys.Keep.ActualWorkCapture))
            .Select(r => new ActualWorkRecorderCandidateItem(
                r.AccountUserId, r.DisplayName, KeepRequestDetailMapper.MapRole(r.Role)))
            .ToList();

        return Result<GetActualWorkRecorderCandidatesResult>.Success(
            new GetActualWorkRecorderCandidatesResult(candidates));
    }
}
