using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Application.Services;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.UnitTests.Keep;

public class KeepCreateBusinessRequestServiceTests
{
    private static readonly DateTime Now  = new(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid UserId    = Guid.NewGuid();

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static CreateBusinessRequestService BuildSut(
        FakeOperatePersistence? operate = null,
        FakeReadPersistence? read = null,
        FakeBusinessPersistence? business = null,
        AccountUserRole role = AccountUserRole.Owner,
        AccountAccessPosture posture = AccountAccessPosture.FullAccess,
        bool permitted = true,
        bool featureEnabled = true,
        bool isAuthenticated = true)
    {
        operate  ??= HappyOperatePersistence(role);
        read     ??= new FakeReadPersistence();
        business ??= HappyBusinessPersistence();

        return new CreateBusinessRequestService(
            operate,
            read,
            business,
            new KeepTokenService(),
            new FakeCurrentUser(UserId, AccountId, isAuthenticated),
            new FakeUserAccessPolicy(permitted),
            new FakeAccountAccessPolicy(posture),
            new FakeFeatureAccessPolicy(featureEnabled),
            new FakeClock(Now));
    }

    private static FakeOperatePersistence HappyOperatePersistence(AccountUserRole role = AccountUserRole.Owner) =>
        new()
        {
            UserSnapshot    = new AccountUserSnapshot(UserId, AccountId, role, MembershipStatus.Active),
            AccountSnapshot = ActiveSnapshot(),
            ActorDisplayName = "Owner User"
        };

    private static FakeBusinessPersistence HappyBusinessPersistence()
    {
        var p = new FakeBusinessPersistence();
        p.CommitResults.Enqueue(BusinessRequestCommitResult.Committed);
        return p;
    }

    private static AccountAccessSnapshot ActiveSnapshot() => new(
        AccountId,
        AccountLifecycleState.Active,
        AccountPurpose.Business,
        AccountPlan.Starter,
        AccountCommercialState.Active,
        AccountOperatingMode.Standard,
        null,
        null);

    private static CreateBusinessRequestCommand ValidCommand() => new(
        "Jane Doe", "0499 888 777", "jane@example.com", "Fix the boiler");

    // ---------------------------------------------------------------------------
    // Auth — unauthenticated
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_returns_unauthorized_when_not_authenticated()
    {
        var sut = BuildSut(isAuthenticated: false);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("auth.unauthorized", result.Error.Code);
    }

    // ---------------------------------------------------------------------------
    // Auth — role
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_returns_forbidden_when_user_snapshot_missing()
    {
        var operate = HappyOperatePersistence();
        operate.UserSnapshot = null;
        var sut = BuildSut(operate: operate);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_forbidden_for_viewer()
    {
        var sut = BuildSut(role: AccountUserRole.Viewer);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    [Theory]
    [InlineData(AccountUserRole.Owner)]
    [InlineData(AccountUserRole.Admin)]
    [InlineData(AccountUserRole.Operator)]
    public async Task Execute_succeeds_for_allowed_roles(AccountUserRole role)
    {
        var sut = BuildSut(role: role);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.True(result.IsSuccess);
    }

    // ---------------------------------------------------------------------------
    // Auth — permission / access / feature
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_returns_forbidden_when_permission_denied()
    {
        var sut = BuildSut(permitted: false);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_forbidden_when_account_blocked()
    {
        var sut = BuildSut(posture: AccountAccessPosture.Blocked);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_forbidden_in_offseason()
    {
        var sut = BuildSut(posture: AccountAccessPosture.ReadOnly);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_forbidden_when_feature_disabled()
    {
        var sut = BuildSut(featureEnabled: false);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    // ---------------------------------------------------------------------------
    // Validation — shared pipeline wired (sample of each stage)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_returns_error_when_customer_name_blank()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new CreateBusinessRequestCommand("  ", "0499888777", null, "Desc"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.CustomerNameRequired", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_error_when_phone_blank()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new CreateBusinessRequestCommand("Jane", "", null, "Desc"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.CustomerPhoneRequired", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_error_when_description_blank()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new CreateBusinessRequestCommand("Jane", "0499888777", null, ""));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.DescriptionRequired", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_error_for_invalid_phone_characters()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new CreateBusinessRequestCommand("Jane", "049#9888777", null, "Desc"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.CustomerPhoneInvalidCharacters", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_error_for_invalid_phone_format()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new CreateBusinessRequestCommand("Jane", "123", null, "Desc"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.CustomerPhoneInvalidFormat", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_error_for_invalid_email()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(
            new CreateBusinessRequestCommand("Jane", "0499888777", "not-an-email", "Desc"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.CustomerEmailInvalid", result.Error.Code);
    }

    // Validation runs BEFORE actor display-name DB lookup.
    [Fact]
    public async Task Execute_does_not_call_actor_lookup_when_validation_fails()
    {
        var operate = HappyOperatePersistence();
        var sut = BuildSut(operate: operate);
        await sut.ExecuteAsync(new CreateBusinessRequestCommand("", "0499888777", null, "Desc"));
        Assert.Equal(0, operate.ActorLookupCount);
    }

    // ---------------------------------------------------------------------------
    // Happy path — new customer
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_succeeds_and_returns_detail_result_for_new_customer()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value.RequestId);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.PageToken));
    }

    [Fact]
    public async Task Execute_detail_result_available_actions_maps_shared_decision_for_Owner()
    {
        // Newly created request is Received, non-terminal, no participation.
        // Owner with write = all capabilities; AllowedStatuses excludes current "received".
        var sut = BuildSut(role: AccountUserRole.Owner);
        var result = await sut.ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        var actions = result.Value.AvailableActions;

        Assert.True(actions.CanChangeStatus);
        Assert.True(actions.CanSendBusinessUpdate);
        Assert.True(actions.CanAddInternalNote);
        Assert.True(actions.CanLogExternalContact);
        Assert.True(actions.CanAssignResponsible);
        Assert.True(actions.CanWatch);
        Assert.False(actions.CanMarkFeedbackReviewed);

        // AllowedStatuses: Received → excludes "received", maps to string slugs.
        Assert.DoesNotContain("received", actions.AllowedStatuses);
        Assert.Contains("scheduled", actions.AllowedStatuses);
        Assert.Contains("in_progress", actions.AllowedStatuses);
        Assert.Contains("pending_customer", actions.AllowedStatuses);
        Assert.Contains("resolved", actions.AllowedStatuses);
        Assert.Contains("cancelled", actions.AllowedStatuses);
    }

    // ---------------------------------------------------------------------------
    // Happy path — existing customer reuse
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_reuses_existing_customer_for_same_canonical_phone()
    {
        var existingCustomer = KeepCustomer.Create(AccountId, "Old Name", "0499888777", null);
        var business = HappyBusinessPersistence();
        business.ExistingCustomer = existingCustomer;

        var sut = BuildSut(business: business);
        var result = await sut.ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        Assert.Equal(existingCustomer.Id, business.CommittedCustomer!.Id);
    }

    // ---------------------------------------------------------------------------
    // Retry — token collision
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_retries_on_unique_token_collision()
    {
        var business = new FakeBusinessPersistence();
        business.CommitResults.Enqueue(BusinessRequestCommitResult.UniqueTokenCollision);
        business.CommitResults.Enqueue(BusinessRequestCommitResult.Committed);

        var sut = BuildSut(business: business);
        var result = await sut.ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, business.CommitAttempts);
    }

    // ---------------------------------------------------------------------------
    // Retry — phone collision (race recovery)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_recovers_from_phone_collision_and_reuses_winning_customer()
    {
        var winningCustomer = KeepCustomer.Create(AccountId, "Winning Jane", "0499888777", null);

        var business = new FakeBusinessPersistence();
        business.CommitResults.Enqueue(BusinessRequestCommitResult.CustomerCanonicalPhoneCollision);
        business.CommitResults.Enqueue(BusinessRequestCommitResult.Committed);
        business.CustomerAfterCollision = winningCustomer;

        var sut = BuildSut(business: business);
        var result = await sut.ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        Assert.Equal(winningCustomer.Id, business.CommittedCustomer!.Id);
        Assert.Equal(2, business.CommitAttempts);
    }

    // ---------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------

    private sealed class FakeCurrentUser(Guid userId, Guid accountId, bool isAuthenticated) : ICurrentUser
    {
        public Guid UserId        => userId;
        public Guid AccountId     => accountId;
        public bool IsAuthenticated => isAuthenticated;
        public bool IsVerified    => true;
    }

    private sealed class FakeOperatePersistence : IKeepRequestOperatePersistence
    {
        public AccountUserSnapshot? UserSnapshot      { get; set; }
        public AccountAccessSnapshot? AccountSnapshot { get; set; }
        public string? ActorDisplayName               { get; set; }
        public int ActorLookupCount                   { get; private set; }

        public Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(UserSnapshot);

        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(AccountSnapshot);

        public Task<string?> GetActorDisplayNameAsync(Guid id, CancellationToken ct)
        {
            ActorLookupCount++;
            return Task.FromResult(ActorDisplayName);
        }

        public Task<KeepRequest?> GetVisibleRequestForUpdateAsync(Guid r, Guid a, Guid u, KeepRequestVisibilityScope s, CancellationToken ct) => throw new NotImplementedException();
        public Task<KeepResponsePolicy?> GetResponsePolicyAsync(Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task CommitAsync(KeepRequest r, KeepRequestEvent? e, CancellationToken ct) => throw new NotImplementedException();
        public Task<List<KeepRequestParticipant>> GetParticipantsForUpdateAsync(Guid r, Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task<ParticipantTargetInfo?> GetParticipantTargetAsync(Guid u, Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<ParticipantCandidateRecord>> GetParticipantCandidatesAsync(Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task CommitParticipationAsync(IReadOnlyList<KeepRequestParticipant> n, KeepRequestEvent? e, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class FakeReadPersistence : IKeepRequestDetailPersistence
    {
        public Task<IReadOnlyList<KeepRequestEvent>> GetAllEventsAsync(Guid r, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KeepRequestEvent>>([]);

        public Task<IReadOnlyList<KeepParticipantProjection>> GetParticipantsAsync(Guid r, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KeepParticipantProjection>>([]);

        public Task<string?> GetAccountBusinessNameAsync(Guid a, CancellationToken ct) =>
            Task.FromResult<string?>("Test Business");

        public Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid u, CancellationToken ct) => throw new NotImplementedException();
        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task<KeepRequest?> GetRequestAsync(Guid r, Guid a, Guid u, KeepRequestVisibilityScope s, CancellationToken ct) => throw new NotImplementedException();
        public Task<KeepRequestPageLookup?> GetRequestByPageTokenAsync(string t, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<KeepRequestEvent>> GetCustomerVisibleEventsAsync(Guid r, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class FakeBusinessPersistence : IKeepBusinessRequestPersistence
    {
        public KeepCustomer? ExistingCustomer    { get; set; }
        public KeepCustomer? CustomerAfterCollision { get; set; }
        public KeepCustomer? CommittedCustomer   { get; private set; }
        public int CommitAttempts                { get; private set; }
        public Queue<BusinessRequestCommitResult> CommitResults { get; } = new();

        private bool _collisionHappened;

        public Task<KeepCustomer?> FindCustomerByCanonicalPhoneAsync(Guid a, string p, CancellationToken ct)
        {
            // After a phone collision the service re-reads — return the winning customer.
            if (_collisionHappened && CustomerAfterCollision is not null)
                return Task.FromResult<KeepCustomer?>(CustomerAfterCollision);
            return Task.FromResult(ExistingCustomer);
        }

        public Task<bool> PageTokenExistsAsync(string t, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<bool> ReferenceCodeExistsAsync(Guid a, string r, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<BusinessRequestCommitResult> CommitBusinessRequestAsync(
            KeepCustomer customer, KeepRequest request, KeepRequestEvent ev, CancellationToken ct)
        {
            CommitAttempts++;
            CommittedCustomer = customer;
            var result = CommitResults.Dequeue();
            if (result == BusinessRequestCommitResult.CustomerCanonicalPhoneCollision)
                _collisionHappened = true;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeUserAccessPolicy(bool permitted) : IUserAccessPolicy
    {
        public bool IsPermitted(AccountUserRole role, MembershipStatus status, AccountPurpose purpose, string key)
        {
            if (!permitted) return false;
            if (role == AccountUserRole.Viewer && key == PermissionKeys.Keep.RequestsOperate) return false;
            return true;
        }
    }

    private sealed class FakeAccountAccessPolicy(AccountAccessPosture posture) : IAccountAccessPolicy
    {
        public AccountAccessDecision Evaluate(AccountAccessContext context) =>
            new(posture, AccountAccessReason.None, null);
    }

    private sealed class FakeFeatureAccessPolicy(bool enabled) : IFeatureAccessPolicy
    {
        public bool IsEnabled(AccountPlan plan, string key) => enabled;
        public int GetLimit(AccountPlan plan, string key) => 0;
        public int ResolveLimit(AccountEntitlements e, string key) => 0;
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }
}
