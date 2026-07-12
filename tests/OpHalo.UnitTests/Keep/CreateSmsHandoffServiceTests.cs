using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.UnitTests.Keep;

public class CreateSmsHandoffServiceTests
{
    private static readonly DateTime Now       = new(2026, 7, 12, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid     AccountId = Guid.NewGuid();
    private static readonly Guid     UserId    = Guid.NewGuid();

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static CreateSmsHandoffService BuildSut(
        FakeOperatePersistence? operate  = null,
        FakeHandoffPersistence? handoff  = null,
        AccountUserRole role             = AccountUserRole.Owner,
        AccountAccessPosture posture     = AccountAccessPosture.FullAccess,
        bool permitted                   = true,
        bool featureEnabled              = true,
        bool isAuthenticated             = true)
    {
        operate ??= HappyOperatePersistence(role);
        handoff ??= new FakeHandoffPersistence();
        return new CreateSmsHandoffService(
            operate,
            handoff,
            new FakeCurrentUser(UserId, AccountId, isAuthenticated),
            new FakeUserAccessPolicy(permitted),
            new FakeAccountAccessPolicy(posture),
            new FakeFeatureAccessPolicy(featureEnabled),
            new FakeClock(Now));
    }

    private static FakeOperatePersistence HappyOperatePersistence(
        AccountUserRole role  = AccountUserRole.Owner,
        string? customerPhone = "0499888777")
    {
        var request = KeepRequest.CreateByBusiness(
            AccountId, Guid.NewGuid(), "Jane", customerPhone ?? "0499888777", null, "Desc",
            "REF-001", "tok_abc", Now, KeepRequestSource.Phone);

        return new FakeOperatePersistence
        {
            UserSnapshot     = new AccountUserSnapshot(UserId, AccountId, role, MembershipStatus.Active),
            AccountSnapshot  = ActiveSnapshot(),
            ActorDisplayName = "Owner User",
            Request          = customerPhone is null ? null : request,
            NoPhone          = customerPhone is null,
        };
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

    private static CreateSmsHandoffCommand ValidCommand() =>
        new(Guid.NewGuid(), "Hi Jane — ACME Plumbing created a private request page for you: https://ophalo.com/r/tok_abc");

    // ---------------------------------------------------------------------------
    // Auth
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_returns_unauthorized_when_not_authenticated()
    {
        var sut = BuildSut(isAuthenticated: false);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("auth.unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_viewer_blocked_for_viewer_role()
    {
        var sut = BuildSut(role: AccountUserRole.Viewer);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.SmsHandoffViewerBlocked", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_offseason_blocked_when_account_read_only()
    {
        var sut = BuildSut(posture: AccountAccessPosture.ReadOnly);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.SmsHandoffOffSeasonBlocked", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_offseason_blocked_when_account_blocked()
    {
        var sut = BuildSut(posture: AccountAccessPosture.Blocked);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.SmsHandoffOffSeasonBlocked", result.Error.Code);
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
    // Input validation
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public async Task Execute_returns_message_required_for_empty_body(string? messageBody)
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new CreateSmsHandoffCommand(Guid.NewGuid(), messageBody!));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.SmsHandoffMessageRequired", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_message_too_long_when_body_exceeds_2000_chars()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new CreateSmsHandoffCommand(Guid.NewGuid(), new string('x', 2001)));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.SmsHandoffMessageTooLong", result.Error.Code);
    }

    // ---------------------------------------------------------------------------
    // Row access
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_returns_not_found_when_request_inaccessible()
    {
        var operate = HappyOperatePersistence();
        operate.Request = null;
        var sut = BuildSut(operate: operate);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_customer_phone_missing_when_phone_is_empty()
    {
        var operate = HappyOperatePersistence();
        // Create a request whose CustomerPhone is blank by reconstructing with empty phone
        var request = KeepRequest.CreateByBusiness(
            AccountId, Guid.NewGuid(), "Jane", "0499888777", null, "Desc",
            "REF-001", "tok_abc", Now, KeepRequestSource.Phone);
        // Use reflection to clear phone — exceptional legacy state tested at service layer
        typeof(KeepRequest)
            .GetProperty(nameof(KeepRequest.CustomerPhone))!
            .SetValue(request, string.Empty);
        operate.Request = request;
        var sut = BuildSut(operate: operate);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.SmsHandoffCustomerPhoneMissing", result.Error.Code);
    }

    // ---------------------------------------------------------------------------
    // Successful creation
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_success_returns_raw_token_and_expiry()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.RawToken);
        Assert.Equal(Now.AddMinutes(15), result.Value.ExpiresAtUtc);
    }

    [Fact]
    public async Task Execute_success_stores_hash_not_raw_token()
    {
        var handoff = new FakeHandoffPersistence();
        var sut = BuildSut(handoff: handoff);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.True(result.IsSuccess);

        var stored = handoff.StoredHandoff!;
        // Hash must differ from raw token
        Assert.NotEqual(result.Value.RawToken, stored.HandoffTokenHash);
        // Stored hash must equal the canonical hash of the raw token
        Assert.Equal(KeepSmsHandoff.HashToken(result.Value.RawToken), stored.HandoffTokenHash);
        // Raw token must never appear in the stored record
        Assert.DoesNotContain(result.Value.RawToken, stored.HandoffTokenHash);
    }

    [Fact]
    public async Task Execute_success_does_not_write_request_event_history()
    {
        var operate = HappyOperatePersistence();
        var sut = BuildSut(operate: operate);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.True(result.IsSuccess);
        // CommitAsync on the request persistence must never be called (Decision 20)
        Assert.Equal(0, operate.CommitCallCount);
    }

    [Fact]
    public async Task Execute_success_stores_trimmed_message_body()
    {
        var handoff = new FakeHandoffPersistence();
        var sut = BuildSut(handoff: handoff);
        var result = await sut.ExecuteAsync(new CreateSmsHandoffCommand(Guid.NewGuid(), "  Hello  "));
        Assert.True(result.IsSuccess);
        Assert.Equal("Hello", handoff.StoredHandoff!.MessageBody);
    }

    [Fact]
    public async Task Execute_success_stores_customer_phone_from_request()
    {
        var handoff = new FakeHandoffPersistence();
        var sut = BuildSut(handoff: handoff);
        await sut.ExecuteAsync(ValidCommand());
        Assert.Equal("0499888777", handoff.StoredHandoff!.CustomerPhone);
    }

    // ---------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------

    private sealed class FakeCurrentUser(Guid userId, Guid accountId, bool isAuthenticated) : ICurrentUser
    {
        public Guid UserId          => userId;
        public Guid AccountId       => accountId;
        public bool IsAuthenticated => isAuthenticated;
        public bool IsVerified      => true;
    }

    private sealed class FakeOperatePersistence : IKeepRequestOperatePersistence
    {
        public AccountUserSnapshot?   UserSnapshot     { get; set; }
        public AccountAccessSnapshot? AccountSnapshot  { get; set; }
        public string?                ActorDisplayName { get; set; }
        public KeepRequest?           Request          { get; set; }
        public bool                   NoPhone          { get; set; }
        public int                    CommitCallCount  { get; private set; }

        public Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(UserSnapshot);

        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(AccountSnapshot);

        public Task<string?> GetActorDisplayNameAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(ActorDisplayName);

        public Task<KeepRequest?> GetVisibleRequestForUpdateAsync(
            Guid requestId, Guid accountId, Guid currentUserId, KeepRequestVisibilityScope scope, CancellationToken ct) =>
            Task.FromResult(Request);

        public Task<KeepRequestCommitResult> CommitAsync(KeepRequest r, KeepRequestEvent? e, CancellationToken ct)
        {
            CommitCallCount++;
            return Task.FromResult(KeepRequestCommitResult.Committed);
        }

        public Task<KeepResponsePolicy?> GetResponsePolicyAsync(Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task<List<KeepRequestParticipant>> GetParticipantsForUpdateAsync(Guid r, Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task<ParticipantTargetInfo?> GetParticipantTargetAsync(Guid u, Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<ParticipantCandidateRecord>> GetParticipantCandidatesAsync(Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task<KeepRequestCommitResult> CommitParticipationAsync(KeepRequest r, IReadOnlyList<KeepRequestParticipant> n, KeepRequestEvent? e, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class FakeHandoffPersistence : IKeepSmsHandoffPersistence
    {
        public KeepSmsHandoff? StoredHandoff { get; private set; }

        public Task CreateAsync(KeepSmsHandoff handoff, CancellationToken ct)
        {
            StoredHandoff = handoff;
            return Task.CompletedTask;
        }

        public Task<KeepSmsHandoffLookupResult?> FindValidByHashAsync(string tokenHash, DateTime nowUtc, CancellationToken ct) =>
            throw new NotImplementedException();
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
