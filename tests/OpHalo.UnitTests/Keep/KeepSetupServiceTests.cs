using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.UnitTests.Keep;

public class KeepSetupServiceTests
{
    private static readonly DateTime Now       = new(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid     AccountId = Guid.NewGuid();
    private static readonly Guid     UserId    = Guid.NewGuid();

    private static KeepSetupService BuildSut(FakeSetupPersistence persistence) =>
        new(
            persistence,
            new FakeCurrentUser(UserId, AccountId),
            new FakeUserAccessPolicy(),
            new FakeAccountAccessPolicy(),
            new FakeClock(Now));

    private static FakeSetupPersistence HappyPersistence(KeepBusinessProfile? profile = null) => new()
    {
        UserSnapshot = new AccountUserSnapshot(UserId, AccountId, AccountUserRole.Owner, MembershipStatus.Active),
        AccountSnapshot = new AccountAccessSnapshot(
            AccountId,
            AccountLifecycleState.Active,
            AccountPurpose.Business,
            AccountPlan.Starter,
            AccountCommercialState.Active,
            AccountOperatingMode.Standard,
            null, null),
        Account = Account.CreateVerified("Acme Services", AccountPurpose.Business, "America/Chicago"),
        Profile = profile,
    };

    // --- UpdateProfileAsync ---

    [Fact]
    public async Task UpdateProfile_saves_valid_public_identity_and_returns_it()
    {
        var persistence = HappyPersistence();
        var sut = BuildSut(persistence);

        var result = await sut.UpdateProfileAsync(
            "Acme Services", "America/Chicago", null, null,
            "https://cdn.example.com/logo.png", "https://acme.example.com");

        Assert.True(result.IsSuccess);
        Assert.Equal("https://cdn.example.com/logo.png", result.Value.LogoUrl);
        Assert.Equal("https://acme.example.com", result.Value.WebsiteUrl);
        Assert.NotNull(persistence.SavedProfile);
        Assert.Equal("https://cdn.example.com/logo.png", persistence.SavedProfile!.LogoUrl);
    }

    [Fact]
    public async Task UpdateProfile_invalid_logo_url_fails_without_saving()
    {
        var persistence = HappyPersistence();
        var sut = BuildSut(persistence);

        var result = await sut.UpdateProfileAsync(
            "Acme Services", "America/Chicago", null, null, "not-a-url", null);

        Assert.True(result.IsFailure);
        Assert.Equal(KeepBusinessProfileErrors.LogoUrlInvalid, result.Error);
        Assert.Null(persistence.SavedProfile);
    }

    [Fact]
    public async Task UpdateProfile_invalid_website_url_fails_without_saving()
    {
        var persistence = HappyPersistence();
        var sut = BuildSut(persistence);

        var result = await sut.UpdateProfileAsync(
            "Acme Services", "America/Chicago", null, null, null, "http://acme.example.com");

        Assert.True(result.IsFailure);
        Assert.Equal(KeepBusinessProfileErrors.WebsiteUrlInvalid, result.Error);
        Assert.Null(persistence.SavedProfile);
    }

    // --- GetSetupAsync ---

    [Fact]
    public async Task GetSetup_returns_logo_and_website_from_existing_profile()
    {
        var profile = KeepBusinessProfile.Create(AccountId);
        profile.UpdatePublicIdentity("https://cdn.example.com/logo.png", "https://acme.example.com");
        var sut = BuildSut(HappyPersistence(profile));

        var result = await sut.GetSetupAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("https://cdn.example.com/logo.png", result.Value.LogoUrl);
        Assert.Equal("https://acme.example.com", result.Value.WebsiteUrl);
    }

    [Fact]
    public async Task GetSetup_returns_null_logo_and_website_when_no_profile()
    {
        var sut = BuildSut(HappyPersistence());

        var result = await sut.GetSetupAsync();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.LogoUrl);
        Assert.Null(result.Value.WebsiteUrl);
    }

    // --- Fakes ---

    private sealed class FakeCurrentUser(Guid userId, Guid accountId) : ICurrentUser
    {
        public Guid UserId          => userId;
        public Guid AccountId       => accountId;
        public bool IsAuthenticated => true;
        public bool IsVerified      => true;
    }

    private sealed class FakeSetupPersistence : IKeepSetupPersistence
    {
        public AccountUserSnapshot?   UserSnapshot    { get; set; }
        public AccountAccessSnapshot? AccountSnapshot { get; set; }
        public Account?                Account         { get; set; }
        public KeepBusinessProfile?    Profile         { get; set; }
        public KeepBusinessProfile?    SavedProfile    { get; private set; }

        public Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(UserSnapshot);

        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(AccountSnapshot);

        public Task<(Account account, KeepBusinessProfile? profile)> GetProfileDataAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult((Account!, Profile));

        public Task<KeepResponsePolicy?> GetPolicyAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult<KeepResponsePolicy?>(null);

        public Task SaveProfileAsync(Account account, KeepBusinessProfile profile, KeepProductOpsEvent? opsEvent, CancellationToken ct)
        {
            SavedProfile = profile;
            return Task.CompletedTask;
        }

        public Task SavePolicyAsync(KeepResponsePolicy policy, bool isNew, KeepProductOpsEvent? opsEvent, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class FakeUserAccessPolicy : IUserAccessPolicy
    {
        public bool IsPermitted(AccountUserRole role, MembershipStatus status, AccountPurpose purpose, string key) => true;
    }

    private sealed class FakeAccountAccessPolicy : IAccountAccessPolicy
    {
        public AccountAccessDecision Evaluate(AccountAccessContext context) =>
            new(AccountAccessPosture.FullAccess, AccountAccessReason.None, null);
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }
}
