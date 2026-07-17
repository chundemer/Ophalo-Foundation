using Microsoft.Extensions.Options;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Application.Auth;
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

    private const string ValidPhone      = "5551234567";
    private const string ValidPublicBase = "https://public.ophalo.com";

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
        bool                      hasActiveLink  = true,
        string                    publicBaseUrl  = ValidPublicBase)
    {
        persistence ??= new FakePersistence(role, hasActiveLink);
        return new CreateIntakeSmsHandoffService(
            persistence,
            new KeepTokenService(),
            new FakeCurrentUser(UserId, AccountId, isAuthenticated),
            new FakeUserAccessPolicy(permitted),
            new FakeAccountAccessPolicy(posture),
            new FakeFeatureAccessPolicy(featureEnabled),
            Options.Create(new MagicLinkSettings { PublicBaseUrl = publicBaseUrl }),
            new FakeClock(Now));
    }

    private static CreateIntakeSmsHandoffCommand ValidCommand(string phone = ValidPhone) =>
        new(phone);

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
    // Configuration guard
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_returns_not_configured_when_PublicBaseUrl_is_blank()
    {
        var sut = BuildSut(publicBaseUrl: "");
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("App.NotConfigured", result.Error.Code);
    }

    // ---------------------------------------------------------------------------
    // Phone validation
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Execute_returns_phone_required_for_blank_input(string? phone)
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new CreateIntakeSmsHandoffCommand(phone!));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.CustomerPhoneRequired", result.Error.Code);
    }

    [Theory]
    [InlineData("abc5551234567")]  // letters — must be rejected before normalization strips them
    [InlineData("555@1234567")]
    [InlineData("555#1234567")]
    public async Task Execute_returns_invalid_characters_for_disallowed_chars(string phone)
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new CreateIntakeSmsHandoffCommand(phone));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.CustomerPhoneInvalidCharacters", result.Error.Code);
    }

    [Theory]
    [InlineData("555123456")]    // 9 digits
    [InlineData("55512345678")]  // 11 digits, no leading 1 to strip
    public async Task Execute_returns_invalid_format_for_wrong_digit_count(string phone)
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new CreateIntakeSmsHandoffCommand(phone));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.CustomerPhoneInvalidFormat", result.Error.Code);
    }

    [Theory]
    [InlineData("+15551234567")]  // +1 country code stripped → 10 digits
    [InlineData("15551234567")]   // leading 1 stripped → 10 digits
    [InlineData("(555) 123-4567")] // formatted with allowed chars → 10 digits
    public async Task Execute_succeeds_for_valid_phone_variations(string phone)
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new CreateIntakeSmsHandoffCommand(phone));
        Assert.True(result.IsSuccess);
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
    public async Task Execute_success_returns_canonical_phone_and_message_body()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(ValidCommand("(555) 123-4567"));
        Assert.True(result.IsSuccess);
        Assert.Equal(ValidPhone, result.Value.CustomerPhone);
        Assert.Equal(
            $"Submit your request here: {ValidPublicBase}/keep/s/test-biz",
            result.Value.MessageBody);
    }

    [Fact]
    public async Task Execute_success_stores_hash_not_raw_token()
    {
        var persistence = new FakePersistence(AccountUserRole.Owner, true);
        var sut = BuildSut(persistence: persistence);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.True(result.IsSuccess);

        var stored = persistence.StoredHandoff!;
        Assert.NotEqual(result.Value.RawToken, stored.HandoffTokenHash);
        Assert.Equal(KeepIntakeSmsHandoff.HashToken(result.Value.RawToken), stored.HandoffTokenHash);
        Assert.DoesNotContain(result.Value.RawToken, stored.HandoffTokenHash);
    }

    [Fact]
    public async Task Execute_success_stores_canonical_phone()
    {
        var persistence = new FakePersistence(AccountUserRole.Owner, true);
        var sut = BuildSut(persistence: persistence);
        // +1 prefix must be stripped to canonical 10 digits before storage
        var result = await sut.ExecuteAsync(new CreateIntakeSmsHandoffCommand("+15551234567"));
        Assert.True(result.IsSuccess);
        Assert.Equal("5551234567", persistence.StoredHandoff!.CustomerPhone);
    }

    [Fact]
    public async Task Execute_success_message_body_contains_public_intake_link()
    {
        var persistence = new FakePersistence(AccountUserRole.Owner, true, slug: "acme-plumbing");
        var sut = BuildSut(persistence: persistence, publicBaseUrl: "https://public.ophalo.com");
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.True(result.IsSuccess);
        Assert.Contains("https://public.ophalo.com/keep/s/acme-plumbing", persistence.StoredHandoff!.MessageBody);
    }

    [Fact]
    public async Task Execute_success_trailing_slash_public_base_url_produces_clean_intake_link()
    {
        var persistence = new FakePersistence(AccountUserRole.Owner, true, slug: "my-biz");
        var sut = BuildSut(persistence: persistence, publicBaseUrl: "https://public.ophalo.com/");
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("//keep", persistence.StoredHandoff!.MessageBody);
        Assert.Contains("https://public.ophalo.com/keep/s/my-biz", persistence.StoredHandoff!.MessageBody);
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
