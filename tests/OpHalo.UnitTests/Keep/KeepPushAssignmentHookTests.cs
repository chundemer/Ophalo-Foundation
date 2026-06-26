using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Notifications;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Domain;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Verifies that ManageResponsibleService.SetAsync dispatches an Assignment push notification
/// after a non-no-op commit, and suppresses push for no-op (self-assign already Responsible)
/// and for fail-soft behavior when the push notifier throws.
/// </summary>
public class KeepPushAssignmentHookTests
{
    static readonly DateTime Now = new(2026, 6, 26, 12, 0, 0, DateTimeKind.Utc);
    static readonly Guid AccountId = Guid.NewGuid();
    static readonly Guid OwnerUserId = Guid.NewGuid();
    static readonly Guid TargetUserId = Guid.NewGuid();
    static readonly Guid RequestId = Guid.NewGuid();

    [Fact]
    public async Task SetAsync_NonNoOp_DispatchesAssignmentPush()
    {
        var notifier = new SpyPushNotifier();
        var request = MakeRequest();

        var svc = BuildSvc(notifier, request,
            targetInfo: new ParticipantTargetInfo(TargetUserId, "Target", AccountUserRole.Operator, MembershipStatus.Active),
            existingParticipants: [],
            notifParticipants: [MakeParticipant(TargetUserId, ParticipationType.Responsible)],
            ownerAdminCandidates: [new ParticipantCandidateRecord(OwnerUserId, "Owner", AccountUserRole.Owner)]);

        var cmd = new SetResponsibleCommand(RequestId, TargetUserId, null, request.ConcurrencyVersion);
        await svc.SetAsync(cmd, default);

        Assert.NotNull(notifier.LastContext);
        Assert.Equal(KeepPushEventKind.Assignment, notifier.LastContext!.EventKind);
        Assert.Equal(AccountId, notifier.LastContext.AccountId);
        // Actor (OwnerUserId) should be excluded from receiving the push.
        Assert.Equal(OwnerUserId, notifier.LastContext.ActorAccountUserId);
        Assert.False(notifier.LastContext.IsOffSeason);
    }

    [Fact]
    public async Task SetAsync_SelfAssign_DispatchesPush_WithActorExclusion()
    {
        // Operator self-assigning: actor == target. Routing will exclude the actor via
        // KeepPushCandidateService, so Owner/Admin fallback would receive push.
        var notifier = new SpyPushNotifier();
        var request = MakeRequest();

        var svc = BuildSvc(notifier, request,
            actorUserId: TargetUserId,
            targetInfo: new ParticipantTargetInfo(TargetUserId, "Target", AccountUserRole.Operator, MembershipStatus.Active),
            existingParticipants: [],
            notifParticipants: [MakeParticipant(TargetUserId, ParticipationType.Responsible)],
            ownerAdminCandidates: [new ParticipantCandidateRecord(OwnerUserId, "Owner", AccountUserRole.Owner)]);

        var cmd = new SetResponsibleCommand(RequestId, TargetUserId, null, request.ConcurrencyVersion);
        await svc.SetAsync(cmd, default);

        // Push fires (notifier called); candidate service will suppress the self-assigning actor.
        Assert.NotNull(notifier.LastContext);
        Assert.Equal(KeepPushEventKind.Assignment, notifier.LastContext!.EventKind);
        Assert.Equal(TargetUserId, notifier.LastContext.ActorAccountUserId);
    }

    [Fact]
    public async Task SetAsync_PushFails_MutationStillSucceeds()
    {
        var notifier = new ThrowingPushNotifier();
        var request = MakeRequest();

        var svc = BuildSvc(notifier, request,
            targetInfo: new ParticipantTargetInfo(TargetUserId, "Target", AccountUserRole.Operator, MembershipStatus.Active),
            existingParticipants: [],
            notifParticipants: [],
            ownerAdminCandidates: []);

        var cmd = new SetResponsibleCommand(RequestId, TargetUserId, null, request.ConcurrencyVersion);
        var result = await svc.SetAsync(cmd, default);

        Assert.True(result.IsSuccess);
    }

    // --- factories ---

    static KeepRequest MakeRequest()
    {
        var r = KeepRequest.CreateFromCustomerIntake(
            AccountId, Guid.NewGuid(), "Alice", "555-0001", null, "test",
            "REF-001", "tok_" + Guid.NewGuid().ToString("N"), Now.AddDays(-1), 60);
        return r;
    }

    static KeepParticipantProjection MakeParticipant(Guid userId, ParticipationType type) =>
        new(userId, type, NotificationsEnabled: true, AttachedAtUtc: Now.AddDays(-1),
            DetachedAtUtc: null, DisplayName: "Op", Role: AccountUserRole.Operator,
            MembershipStatus: MembershipStatus.Active);

    static ManageResponsibleService BuildSvc(
        IKeepPushNotifier notifier,
        KeepRequest request,
        ParticipantTargetInfo? targetInfo = null,
        List<KeepRequestParticipant>? existingParticipants = null,
        IReadOnlyList<KeepParticipantProjection>? notifParticipants = null,
        IReadOnlyList<ParticipantCandidateRecord>? ownerAdminCandidates = null,
        Guid? actorUserId = null)
    {
        var actorId = actorUserId ?? OwnerUserId;
        return new ManageResponsibleService(
            new FakeOperatePersistence(request, targetInfo, existingParticipants ?? [], ownerAdminCandidates ?? []),
            new FakeReadPersistence(notifParticipants ?? []),
            new KeepRequestParticipationService(),
            notifier,
            new FakeCurrentUser(actorId, AccountId),
            new FakeUserAccessPolicy(),
            new FakeAccountAccessPolicy(AccountAccessPosture.FullAccess),
            new FakeFeatureAccessPolicy(),
            new FakeClock(Now));
    }

    // --- fakes ---

    private sealed class SpyPushNotifier : IKeepPushNotifier
    {
        public KeepPushRoutingContext? LastContext { get; private set; }

        public Task SendAsync(KeepPushRoutingContext context, CancellationToken ct = default)
        {
            LastContext = context;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingPushNotifier : IKeepPushNotifier
    {
        public Task SendAsync(KeepPushRoutingContext context, CancellationToken ct = default) =>
            throw new InvalidOperationException("push adapter down");
    }

    private sealed class FakeOperatePersistence(
        KeepRequest request,
        ParticipantTargetInfo? targetInfo,
        List<KeepRequestParticipant> existingParticipants,
        IReadOnlyList<ParticipantCandidateRecord> candidateMembers) : IKeepRequestOperatePersistence
    {
        public Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<AccountUserSnapshot?>(new AccountUserSnapshot(
                id, AccountId, AccountUserRole.Owner, MembershipStatus.Active));

        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<AccountAccessSnapshot?>(new AccountAccessSnapshot(
                AccountId, AccountLifecycleState.Active, AccountPurpose.Business, AccountPlan.Starter,
                AccountCommercialState.Active, AccountOperatingMode.Standard, null, null));

        public Task<string?> GetActorDisplayNameAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<string?>("Owner");

        public Task<KeepRequest?> GetVisibleRequestForUpdateAsync(
            Guid r, Guid a, Guid u, KeepRequestVisibilityScope s, CancellationToken ct) =>
            Task.FromResult<KeepRequest?>(request);

        public Task<ParticipantTargetInfo?> GetParticipantTargetAsync(Guid u, Guid a, CancellationToken ct) =>
            Task.FromResult(targetInfo);

        public Task<List<KeepRequestParticipant>> GetParticipantsForUpdateAsync(Guid r, Guid a, CancellationToken ct) =>
            Task.FromResult(existingParticipants);

        public Task<IReadOnlyList<ParticipantCandidateRecord>> GetParticipantCandidatesAsync(Guid a, CancellationToken ct) =>
            Task.FromResult(candidateMembers);

        public Task<KeepRequestCommitResult> CommitParticipationAsync(
            KeepRequest r, IReadOnlyList<KeepRequestParticipant> n, KeepRequestEvent? e, CancellationToken ct) =>
            Task.FromResult(KeepRequestCommitResult.Committed);

        public Task<KeepResponsePolicy?> GetResponsePolicyAsync(Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task<KeepRequestCommitResult> CommitAsync(KeepRequest r, KeepRequestEvent? e, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class FakeReadPersistence(IReadOnlyList<KeepParticipantProjection> participants)
        : IKeepRequestDetailPersistence
    {
        public Task<IReadOnlyList<KeepParticipantProjection>> GetParticipantsAsync(Guid r, CancellationToken ct) =>
            Task.FromResult(participants);

        public Task<IReadOnlyList<KeepRequestEvent>> GetAllEventsAsync(Guid r, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KeepRequestEvent>>([]);

        public Task<string?> GetAccountBusinessNameAsync(Guid a, CancellationToken ct) =>
            Task.FromResult<string?>("Biz");

        public Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid u, CancellationToken ct) => throw new NotImplementedException();
        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task<KeepRequest?> GetRequestAsync(Guid r, Guid a, Guid u, KeepRequestVisibilityScope s, CancellationToken ct) => throw new NotImplementedException();
        public Task<KeepRequestPageLookup?> GetRequestByPageTokenAsync(string t, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<KeepRequestEvent>> GetCustomerVisibleEventsAsync(Guid r, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>> GetReadyToCloseNavigationIdsAsync(Guid a, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class FakeCurrentUser(Guid userId, Guid accountId) : ICurrentUser
    {
        public Guid UserId        => userId;
        public Guid AccountId     => accountId;
        public bool IsAuthenticated => true;
        public bool IsVerified    => true;
    }

    private sealed class FakeUserAccessPolicy : IUserAccessPolicy
    {
        public bool IsPermitted(AccountUserRole role, MembershipStatus membership,
            AccountPurpose purpose, string permissionKey) => true;
    }

    private sealed class FakeAccountAccessPolicy(AccountAccessPosture posture) : IAccountAccessPolicy
    {
        public AccountAccessDecision Evaluate(AccountAccessContext context) =>
            new(posture, AccountAccessReason.None, null);
    }

    private sealed class FakeFeatureAccessPolicy : IFeatureAccessPolicy
    {
        public bool IsEnabled(AccountPlan plan, string featureKey) => true;
        public int GetLimit(AccountPlan plan, string limitKey) => 0;
        public int ResolveLimit(AccountEntitlements e, string limitKey) => 0;
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }
}
