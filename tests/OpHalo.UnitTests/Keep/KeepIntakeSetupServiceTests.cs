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

// GAP-036b: the server independently validates the exact, case-sensitive REPLACE confirmation
// before revoking the active link — the client-side disabled button is a UX gate only.
public class KeepIntakeSetupServiceTests
{
    private static readonly DateTime Now       = new(2026, 7, 19, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid     AccountId = Guid.NewGuid();
    private static readonly Guid     UserId    = Guid.NewGuid();

    private static KeepIntakeSetupService BuildSut(
        FakePersistence       persistence,
        AccountAccessPosture  posture         = AccountAccessPosture.FullAccess,
        bool                  permitted       = true,
        bool                  featureEnabled  = true,
        bool                  isAuthenticated = true)
    {
        return new KeepIntakeSetupService(
            persistence,
            new KeepTokenService(),
            new FakeCurrentUser(UserId, AccountId, isAuthenticated),
            new FakeUserAccessPolicy(permitted),
            new FakeAccountAccessPolicy(posture),
            new FakeFeatureAccessPolicy(featureEnabled),
            new FakeClock(Now));
    }

    [Fact]
    public async Task ReplaceAsync_returns_confirmation_invalid_when_missing()
    {
        var persistence = new FakePersistence(AccountUserRole.Owner, hasActiveLink: true);
        var sut = BuildSut(persistence);
        var result = await sut.ReplaceAsync(confirmation: null);

        Assert.False(result.IsSuccess);
        Assert.Equal("KeepPublicIntakeLink.ReplaceConfirmationInvalid", result.Error.Code);
        Assert.False(persistence.ReplaceCommitted);
    }

    [Fact]
    public async Task ReplaceAsync_returns_confirmation_invalid_when_incorrect_case()
    {
        var persistence = new FakePersistence(AccountUserRole.Owner, hasActiveLink: true);
        var sut = BuildSut(persistence);
        var result = await sut.ReplaceAsync(confirmation: "replace");

        Assert.False(result.IsSuccess);
        Assert.Equal("KeepPublicIntakeLink.ReplaceConfirmationInvalid", result.Error.Code);
        Assert.False(persistence.ReplaceCommitted);
    }

    [Fact]
    public async Task ReplaceAsync_returns_forbidden_before_checking_confirmation()
    {
        var persistence = new FakePersistence(AccountUserRole.Owner, hasActiveLink: true);
        var sut = BuildSut(persistence, permitted: false);
        var result = await sut.ReplaceAsync(confirmation: null);

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.forbidden", result.Error.Code);
        Assert.False(persistence.ReplaceCommitted);
    }

    [Fact]
    public async Task ReplaceAsync_returns_no_active_link_when_confirmation_correct_but_no_link()
    {
        var persistence = new FakePersistence(AccountUserRole.Owner, hasActiveLink: false);
        var sut = BuildSut(persistence);
        var result = await sut.ReplaceAsync(confirmation: "REPLACE");

        Assert.False(result.IsSuccess);
        Assert.Equal("KeepPublicIntakeLink.NoActiveLink", result.Error.Code);
        Assert.False(persistence.ReplaceCommitted);
    }

    [Fact]
    public async Task ReplaceAsync_commits_replacement_when_confirmation_exact()
    {
        var persistence = new FakePersistence(AccountUserRole.Owner, hasActiveLink: true);
        var sut = BuildSut(persistence);
        var result = await sut.ReplaceAsync(confirmation: KeepIntakeSetupService.ReplaceConfirmationValue);

        Assert.True(result.IsSuccess);
        Assert.True(persistence.ReplaceCommitted);
        Assert.True(result.Value.StaleLinksWarning);
    }

    // ---------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------

    private sealed class FakePersistence(
        AccountUserRole role,
        bool hasActiveLink,
        string slug = "test-biz") : IKeepIntakeSetupPersistence
    {
        public bool ReplaceCommitted { get; private set; }

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

        public Task<string?> GetAccountBusinessNameAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult<string?>("Test Biz");

        public Task<KeepPublicIntakeLink?> FindActiveLinkByAccountAsync(Guid accountId, CancellationToken ct)
        {
            if (!hasActiveLink) return Task.FromResult<KeepPublicIntakeLink?>(null);
            var link = KeepPublicIntakeLink.Create(accountId, slug, "hash_placeholder");
            return Task.FromResult<KeepPublicIntakeLink?>(link);
        }

        public Task<bool> SlugExistsAsync(string slug, CancellationToken ct) => Task.FromResult(false);

        public Task<EnsureIntakeLinkCommitResult> CommitEnsureAsync(KeepPublicIntakeLink link, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task CommitReplaceAsync(KeepPublicIntakeLink oldLink, KeepPublicIntakeLink newLink, CancellationToken ct)
        {
            ReplaceCommitted = true;
            return Task.CompletedTask;
        }

        public Task<RenameIntakeLinkCommitResult> CommitRenameAsync(KeepPublicIntakeLink link, KeepPublicIntakeSlugAlias alias, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    private sealed class FakeCurrentUser(Guid userId, Guid accountId, bool isAuthenticated) : ICurrentUser
    {
        public Guid UserId          => userId;
        public Guid AccountId       => accountId;
        public bool IsAuthenticated => isAuthenticated;
        public bool IsVerified      => true;
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
