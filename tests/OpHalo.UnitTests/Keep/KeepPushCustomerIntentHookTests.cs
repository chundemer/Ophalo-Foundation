using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Notifications;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Verifies that AddCustomerMessageService dispatches push for push-worthy customer intents
/// (CallRequested, CancellationRequested, TimingChangeRequested) and suppresses push for
/// badge/list-only intents. Also verifies SubmitFeedbackService dispatches UnresolvedFeedback
/// push only when the customer marks the issue unresolved (WasResolved = false).
/// </summary>
public class KeepPushCustomerIntentHookTests
{
    static readonly DateTime Now = new(2026, 6, 26, 12, 0, 0, DateTimeKind.Utc);
    static readonly Guid AccountId = Guid.NewGuid();
    static readonly Guid RequestId = Guid.NewGuid();
    static readonly Guid OperatorUserId = Guid.NewGuid();

    // --- AddCustomerMessageService tests ---

    [Theory]
    [InlineData(MessageIntent.CallRequested,         KeepPushEventKind.CallRequested)]
    [InlineData(MessageIntent.CancellationRequested, KeepPushEventKind.CancellationRequested)]
    [InlineData(MessageIntent.TimingChangeRequested, KeepPushEventKind.TimingChangeRequested)]
    public async Task AddCustomerMessage_PushWorthyIntent_DispatchesPush(
        MessageIntent intent, KeepPushEventKind expectedKind)
    {
        var notifier = new SpyPushNotifier();
        var request = MakeRequest();
        var svc = BuildAddMessageSvc(notifier, request,
            participants: [MakeParticipant(OperatorUserId, ParticipationType.Responsible)]);

        await svc.ExecuteAsync(new AddCustomerMessageCommand(
            PageToken: "tok", Intent: intent, Message: "msg",
            ExpectedVersion: request.ConcurrencyVersion), default);

        Assert.NotNull(notifier.LastContext);
        Assert.Equal(expectedKind, notifier.LastContext!.EventKind);
        Assert.Equal(AccountId, notifier.LastContext.AccountId);
        Assert.Equal(request.Id, notifier.LastContext.RequestId);
        // Anonymous customer — no actor exclusion should apply.
        Assert.Equal(Guid.Empty, notifier.LastContext.ActorAccountUserId);
    }

    [Theory]
    [InlineData(MessageIntent.GeneralMessage)]
    [InlineData(MessageIntent.Question)]
    [InlineData(MessageIntent.UpdateRequest)]
    [InlineData(MessageIntent.InformationAdded)]
    public async Task AddCustomerMessage_BadgeListOnlyIntent_NoPush(MessageIntent intent)
    {
        var notifier = new SpyPushNotifier();
        var request = MakeRequest();
        var svc = BuildAddMessageSvc(notifier, request);

        await svc.ExecuteAsync(new AddCustomerMessageCommand(
            PageToken: "tok", Intent: intent, Message: "msg",
            ExpectedVersion: request.ConcurrencyVersion), default);

        Assert.Null(notifier.LastContext);
    }

    [Fact]
    public async Task AddCustomerMessage_PushFails_MutationStillSucceeds()
    {
        var notifier = new ThrowingPushNotifier();
        var request = MakeRequest();
        var svc = BuildAddMessageSvc(notifier, request);

        var result = await svc.ExecuteAsync(new AddCustomerMessageCommand(
            PageToken: "tok", Intent: MessageIntent.CallRequested, Message: "msg",
            ExpectedVersion: request.ConcurrencyVersion), default);

        Assert.True(result.IsSuccess);
    }

    // --- SubmitFeedbackService tests ---

    [Fact]
    public async Task SubmitFeedback_Unresolved_DispatchesUnresolvedFeedbackPush()
    {
        var notifier = new SpyPushNotifier();
        var request = MakeClosedRequest();
        var svc = BuildSubmitFeedbackSvc(notifier, request,
            ownerAdminMembers: [MakeOwnerAdmin(OperatorUserId)]);

        await svc.ExecuteAsync(new SubmitFeedbackCommand(
            PageToken: "tok", WasResolved: false, Comment: "bad",
            ExpectedVersion: request.ConcurrencyVersion), default);

        Assert.NotNull(notifier.LastContext);
        Assert.Equal(KeepPushEventKind.UnresolvedFeedback, notifier.LastContext!.EventKind);
        Assert.Equal(Guid.Empty, notifier.LastContext.ActorAccountUserId);
        Assert.True(notifier.LastContext.IsTerminal);
    }

    [Fact]
    public async Task SubmitFeedback_Resolved_NoPush()
    {
        var notifier = new SpyPushNotifier();
        var request = MakeClosedRequest();
        var svc = BuildSubmitFeedbackSvc(notifier, request);

        await svc.ExecuteAsync(new SubmitFeedbackCommand(
            PageToken: "tok", WasResolved: true, Comment: (string?)null,
            ExpectedVersion: request.ConcurrencyVersion), default);

        Assert.Null(notifier.LastContext);
    }

    [Fact]
    public async Task SubmitFeedback_PushFails_MutationStillSucceeds()
    {
        var notifier = new ThrowingPushNotifier();
        var request = MakeClosedRequest();
        var svc = BuildSubmitFeedbackSvc(notifier, request);

        var result = await svc.ExecuteAsync(new SubmitFeedbackCommand(
            PageToken: "tok", WasResolved: false, Comment: "bad",
            ExpectedVersion: request.ConcurrencyVersion), default);

        Assert.True(result.IsSuccess);
    }

    // --- factories ---

    static KeepRequest MakeRequest()
    {
        return KeepRequest.CreateFromCustomerIntake(
            AccountId, Guid.NewGuid(), "Alice", "555-0001", null, "test",
            "REF-001", "tok_" + Guid.NewGuid().ToString("N"), Now.AddDays(-1), 60);
    }

    static KeepRequest MakeClosedRequest()
    {
        var r = MakeRequest();
        var actor = Guid.NewGuid();
        r.ChangeStatus(KeepRequestStatus.Resolved, null, actor, "Op", Now.AddHours(-2));
        r.ChangeStatus(KeepRequestStatus.Closed, null, actor, "Op", Now.AddHours(-1));
        return r;
    }

    static KeepParticipantProjection MakeParticipant(Guid userId, ParticipationType type) =>
        new(userId, type, NotificationsEnabled: true, AttachedAtUtc: Now.AddDays(-1),
            DetachedAtUtc: null, DisplayName: "Op", Role: AccountUserRole.Operator,
            MembershipStatus: MembershipStatus.Active);

    static ParticipantCandidateRecord MakeOwnerAdmin(Guid userId) =>
        new(userId, "Owner", AccountUserRole.Owner);

    static KeepPublicCustomerAccessGuard BuildGuard(KeepRequest request) =>
        new(
            new FakeGuardDetailPersistence(request),
            new FakeAccessPolicy(),
            new FakeFeaturePolicy(),
            new FakeClock(Now));

    static AddCustomerMessageService BuildAddMessageSvc(
        IKeepPushNotifier notifier,
        KeepRequest request,
        IReadOnlyList<KeepParticipantProjection>? participants = null,
        IReadOnlyList<ParticipantCandidateRecord>? ownerAdminMembers = null) =>
        new(
            BuildGuard(request),
            new FakeCustomerPersistence(request, participants ?? [], ownerAdminMembers ?? []),
            notifier,
            new FakeClock(Now));

    static SubmitFeedbackService BuildSubmitFeedbackSvc(
        IKeepPushNotifier notifier,
        KeepRequest request,
        IReadOnlyList<KeepParticipantProjection>? participants = null,
        IReadOnlyList<ParticipantCandidateRecord>? ownerAdminMembers = null) =>
        new(
            BuildGuard(request),
            new FakeCustomerPersistence(request, participants ?? [], ownerAdminMembers ?? []),
            notifier,
            new FakeClock(Now));

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

    private sealed class FakeGuardDetailPersistence(KeepRequest request)
        : IKeepRequestDetailPersistence
    {
        public Task<KeepRequestPageLookup?> GetRequestByPageTokenAsync(string t, CancellationToken ct) =>
            Task.FromResult<KeepRequestPageLookup?>(new KeepRequestPageLookup(request, "Biz", null, null, null));

        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<AccountAccessSnapshot?>(new AccountAccessSnapshot(
                AccountId, AccountLifecycleState.Active, AccountPurpose.Business, AccountPlan.Starter,
                AccountCommercialState.Active, AccountOperatingMode.Standard, null, null));

        public Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task<KeepRequest?> GetRequestAsync(Guid r, Guid a, Guid u, KeepRequestVisibilityScope s, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<KeepRequestEvent>> GetAllEventsAsync(Guid r, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<KeepParticipantProjection>> GetParticipantsAsync(Guid r, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> GetAccountBusinessNameAsync(Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<KeepRequestEvent>> GetCustomerVisibleEventsAsync(Guid r, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>> GetReadyToCloseNavigationIdsAsync(Guid a, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class FakeCustomerPersistence(
        KeepRequest request,
        IReadOnlyList<KeepParticipantProjection> participants,
        IReadOnlyList<ParticipantCandidateRecord> ownerAdminMembers) : IKeepCustomerWritePersistence
    {
        public Task<KeepRequest?> GetRequestForUpdateAsync(Guid requestId, CancellationToken ct) =>
            Task.FromResult<KeepRequest?>(request);

        public Task<KeepResponsePolicy?> GetResponsePolicyAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult<KeepResponsePolicy?>(null);

        public Task<KeepRequestCommitResult> CommitAsync(KeepRequest r, KeepRequestEvent e, CancellationToken ct) =>
            Task.FromResult(KeepRequestCommitResult.Committed);

        public Task<KeepRequestCommitResult> CommitFeedbackAsync(KeepRequest r, KeepRequestEvent e, CancellationToken ct) =>
            Task.FromResult(KeepRequestCommitResult.Committed);

        public Task<IReadOnlyList<KeepRequestEvent>> GetCustomerVisibleEventsAsync(Guid requestId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KeepRequestEvent>>([]);

        public Task CommitPageViewAsync(KeepRequest r, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<KeepParticipantProjection>> GetParticipantsAsync(Guid requestId, CancellationToken ct) =>
            Task.FromResult(participants);

        public Task<IReadOnlyList<ParticipantCandidateRecord>> GetActiveOwnerAdminMembersAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult(ownerAdminMembers);
    }

    private sealed class FakeAccessPolicy : IAccountAccessPolicy
    {
        public AccountAccessDecision Evaluate(AccountAccessContext context) =>
            new(AccountAccessPosture.FullAccess, AccountAccessReason.None, null);
    }

    private sealed class FakeFeaturePolicy : IFeatureAccessPolicy
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
