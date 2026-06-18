using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed record GetParticipantCandidatesResult(
    IReadOnlyList<ParticipantCandidateItem> Candidates);

public sealed record ParticipantCandidateItem(
    Guid AccountUserId,
    string DisplayName,
    string Role);

public sealed class GetParticipantCandidatesService(
    IKeepRequestOperatePersistence operatePersistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IClock clock)
{
    private static readonly Error Unauthorized = Error.Create("auth.unauthorized", "Authentication required.");
    private static readonly Error Forbidden    = Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<GetParticipantCandidatesResult>> ExecuteAsync(CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated)
            return Result<GetParticipantCandidatesResult>.Failure(Unauthorized);

        var userSnapshot = await operatePersistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result<GetParticipantCandidatesResult>.Failure(Forbidden);

        var accountSnapshot = await operatePersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<GetParticipantCandidatesResult>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(userSnapshot.Role, userSnapshot.MembershipStatus, accountSnapshot.Purpose, PermissionKeys.Keep.RequestsOperate))
            return Result<GetParticipantCandidatesResult>.Failure(Forbidden);

        // ADR-235: Owner/Admin only in 3B — Operator self-assign is deferred (DEF-045)
        // and Operators cannot add/manage watchers for others.
        if (userSnapshot.Role is not (AccountUserRole.Owner or AccountUserRole.Admin))
            return Result<GetParticipantCandidatesResult>.Failure(Forbidden);

        // Candidate lookup is read metadata — OffSeason/read-only allows reads; block hard-blocked only.
        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState, accountSnapshot.Purpose, accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc, accountSnapshot.PastDueGraceEndsAtUtc, accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: true, clock.UtcNow);
        var decision = accountAccessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked)
            return Result<GetParticipantCandidatesResult>.Failure(Forbidden);

        if (!featurePolicy.IsEnabled(accountSnapshot.Plan, FeatureKeys.Keep.OperatorQueue))
            return Result<GetParticipantCandidatesResult>.Failure(Forbidden);

        var records = await operatePersistence.GetParticipantCandidatesAsync(currentUser.AccountId, ct);

        var candidates = records
            .Select(r => new ParticipantCandidateItem(
                r.AccountUserId,
                r.DisplayName,
                KeepRequestDetailMapper.MapRole(r.Role)))
            .ToList();

        return Result<GetParticipantCandidatesResult>.Success(new GetParticipantCandidatesResult(candidates));
    }
}
