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

public sealed record GetActualWorkPerformerCandidatesResult(
    IReadOnlyList<ActualWorkPerformerCandidateItem> Candidates);

public sealed record ActualWorkPerformerCandidateItem(
    Guid AccountUserId,
    string DisplayName,
    string Role);

/// <summary>
/// ADR-494 D2: account-wide read of the members who may be recorded as the performer of Actual Work
/// — an active, account-scoped staff member holding both <c>RequestsOperate</c> and
/// <c>ActualWorkCapture</c> under the account's purpose (<see cref="ActualWorkPerformerEligibility"/>).
///
/// Unlike <see cref="GetActualWorkRecorderCandidatesService"/> this is <b>not</b> Owner/Admin-only:
/// the office-transcription workflow has an Operator recording a paper ticket on a technician's
/// behalf, so the caller gate is exactly the performer predicate (any active member with
/// <c>RequestsOperate</c> + <c>ActualWorkCapture</c>). It does not reuse the recorder-candidate
/// service, which would 403 that transcriber.
///
/// Composition order (ADR-462): account access gate → feature resolver → caller permission. A
/// caller missing the price-book entitlement or either permission receives an opaque 403; the
/// response never enumerates members the caller could not otherwise see and carries no permission
/// detail. Eligibility is not request-scoped, so this list is not either.
/// </summary>
public sealed class GetActualWorkPerformerCandidatesService(
    IKeepRequestOperatePersistence operatePersistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IAccountFeatureAccessResolver featureAccessResolver,
    IClock clock)
{
    private static readonly Error Unauthorized = Error.Create("auth.unauthorized", "Authentication required.");
    private static readonly Error Forbidden = Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<GetActualWorkPerformerCandidatesResult>> ExecuteAsync(CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated)
            return Result<GetActualWorkPerformerCandidatesResult>.Failure(Unauthorized);

        var userSnapshot = await operatePersistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result<GetActualWorkPerformerCandidatesResult>.Failure(Forbidden);

        var accountSnapshot = await operatePersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<GetActualWorkPerformerCandidatesResult>.Failure(Forbidden);

        // Gate 1: account access. Candidate lookup is read metadata — Blocked-only denies; a
        // ReadOnly (e.g. OffSeason) account may still resolve the list.
        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState, accountSnapshot.Purpose, accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc, accountSnapshot.PastDueGraceEndsAtUtc, accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: true, clock.UtcNow);
        if (accountAccessPolicy.Evaluate(accessContext).IsBlocked)
            return Result<GetActualWorkPerformerCandidatesResult>.Failure(Forbidden);

        // Gate 2: entitlement (plan or active capability-package enrollment).
        var entitled = await featureAccessResolver.IsEnabledAsync(
            currentUser.AccountId, new AccountFeatureAccessContext(accountSnapshot.Plan),
            CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, ct);
        if (!entitled)
            return Result<GetActualWorkPerformerCandidatesResult>.Failure(Forbidden);

        // Gate 3: caller permission — the performer predicate itself (no Owner/Admin gate). An
        // Operator office transcriber passes; a Viewer does not.
        if (!ActualWorkPerformerEligibility.IsEligible(
                userAccessPolicy, userSnapshot.Role, userSnapshot.MembershipStatus, accountSnapshot.Purpose))
            return Result<GetActualWorkPerformerCandidatesResult>.Failure(Forbidden);

        // GetParticipantCandidatesAsync returns active Owner/Admin/Operator members; filter to the
        // ones who actually hold the performer predicate under this account's purpose.
        var records = await operatePersistence.GetParticipantCandidatesAsync(currentUser.AccountId, ct);

        var candidates = records
            .Where(r => ActualWorkPerformerEligibility.IsEligible(
                userAccessPolicy, r.Role, MembershipStatus.Active, accountSnapshot.Purpose))
            .Select(r => new ActualWorkPerformerCandidateItem(
                r.AccountUserId, r.DisplayName, KeepRequestDetailMapper.MapRole(r.Role)))
            .ToList();

        return Result<GetActualWorkPerformerCandidatesResult>.Success(
            new GetActualWorkPerformerCandidatesResult(candidates));
    }
}
