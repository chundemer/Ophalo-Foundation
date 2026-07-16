using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.IntakeSetup;
using OpHalo.Keep.Application.Services;
using OpHalo.Keep.Core.Entities;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.UnitTests.Keep;

public class CreateIntakeSmsHandoffServiceTests
{
    private static readonly DateTime Now       = new(2026, 7, 16, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid     AccountId = Guid.NewGuid();
    private static readonly Guid     UserId    = Guid.NewGuid();

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static CreateIntakeSmsHandoffService BuildSut(
        FakePersistence?          persistence    = null,
        AccountUserRole           role           = AccountUserRole.Owner,
        AccountAccessPosture      posture        = AccountAccessPosture.FullAccess,
        bool                      permitted      = true,
        bool                      featureEnabled = true,
        bool                      isAuthenticated = true,
        bool                      hasActiveLink  = true)
    {
        persistence ??= new FakePersistence(role, hasActiveLink);
        return new CreateIntakeSmsHandoffService(
            persistence,
            new KeepTokenService(),
            new FakeCurrentUser(UserId, AccountId, isAuthenticated),
            new FakeUserAccessPolicy(permitted),
            new FakeAccountAccessPolicy(posture),
            new FakeFeatureAccessPolicy(featureEnabled),
            new FakeClock(Now));
    }

    private static CreateIntakeSmsHandoffCommand ValidCommand() =>
        new("https://app.ophalo.com");

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
    public async Task Execute_returns_forbidden_for_operator_role()
    {
        // Operator lacks Keep.SettingsManage
        var sut = BuildSut(role: AccountUserRole.Operator, permitted: false);
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
    public async Task Execute_returns_forbidden_when_feature_disabled()
    {
        var sut = BuildSut(featureEnabled: false);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    // ---------------------------------------------------------------------------
    // No active intake link
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_returns_no_active_link_when_account_has_no_intake_link()
    {
        var sut = BuildSut(hasActiveLink: false);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepPublicIntakeLink.NoActiveLink", result.Error.Code);
    }

    // ---------------------------------------------------------------------------
    // Successful creation
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_success_returns_raw_token_and_15min_expiry()
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
        var persistence = new FakePersistence(AccountUserRole.Owner, true);
        var sut = BuildSut(persistence: persistence);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.True(result.IsSuccess);

        var stored = persistence.StoredHandoff!;
        // Hash must differ from raw token
        Assert.NotEqual(result.Value.RawToken, stored.HandoffTokenHash);
        // Stored hash must equal the canonical SHA-256 of the raw token
        Assert.Equal(KeepIntakeSmsHandoff.HashToken(result.Value.RawToken), stored.HandoffTokenHash);
        // Raw token must not appear in the stored hash
        Assert.DoesNotContain(result.Value.RawToken, stored.HandoffTokenHash);
    }

    [Fact]
    public async Task Execute_success_message_body_contains_intake_link()
    {
        var persistence = new FakePersistence(AccountUserRole.Owner, true, slug: "acme-plumbing");
        var sut = BuildSut(persistence: persistence);
        var result = await sut.ExecuteAsync(new CreateIntakeSmsHandoffCommand("https://app.ophalo.com"));
        Assert.True(result.IsSuccess);
        Assert.Contains("https://app.ophalo.com/keep/acme-plumbing", persistence.StoredHandoff!.MessageBody);
    }

    [Fact]
    public async Task Execute_success_trailing_slash_base_url_produces_clean_intake_link()
    {
        var persistence = new FakePersistence(AccountUserRole.Owner, true, slug: "my-biz");
        var sut = BuildSut(persistence: persistence);
        // AppBaseUrl has a trailing slash — the stored URL must not contain a double slash
        var result = await sut.ExecuteAsync(new CreateIntakeSmsHandoffCommand("https://app.ophalo.com/"));
        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("//keep", persistence.StoredHandoff!.MessageBody);
        Assert.Contains("https://app.ophalo.com/keep/my-biz", persistence.StoredHandoff!.MessageBody);
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

    private sealed class FakePersistence(
        AccountUserRole role,
        bool hasActiveLink,
        string slug = "test-biz") : IKeepIntakeSmsHandoffPersistence
    {
        public KeepIntakeSmsHandoff? StoredHandoff { get; private set; }

        public Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<AccountUserSnapshot?>(
                new AccountUserSnapshot(id, AccountId, role, MembershipStatus.Active));

        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<AccountAccessSnapshot?>(new AccountAccessSnapshot(
                id,
                AccountLifecycleState.Active,
                AccountPurpose.Business,
                AccountPlan.Starter,
                AccountCommercialState.Active,
                AccountOperatingMode.Standard,
                null,
                null));

        public Task<KeepPublicIntakeLink?> FindActiveLinkByAccountAsync(Guid accountId, CancellationToken ct)
        {
            if (!hasActiveLink) return Task.FromResult<KeepPublicIntakeLink?>(null);
            var link = KeepPublicIntakeLink.Create(accountId, slug, "hash_placeholder");
            return Task.FromResult<KeepPublicIntakeLink?>(link);
        }

        public Task CreateAsync(KeepIntakeSmsHandoff handoff, CancellationToken ct)
        {
            StoredHandoff = handoff;
            return Task.CompletedTask;
        }

        public Task<KeepIntakeSmsHandoffLookupResult?> FindValidByHashAsync(
            string tokenHash, DateTime nowUtc, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    private sealed class FakeUserAccessPolicy(bool permitted) : IUserAccessPolicy
    {
        public bool IsPermitted(AccountUserRole role, MembershipStatus status, AccountPurpose purpose, string key) =>
            permitted;
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
