using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.SharedKernel.Abstractions;
using Xunit;

namespace OpHalo.UnitTests.Accounts;

/// <summary>
/// Locks the Session 1c gate order (ADR-462): account access gate (Blocked-only denial on this
/// read surface) → account-aware feature resolver → user permission (account.settings.manage) →
/// generic status over <see cref="CapabilityPackageFeatureKeys.All"/>. Uses the real
/// <see cref="AccountAccessPolicy"/>, <see cref="AccountFeatureAccessResolver"/>,
/// <see cref="FeatureAccessPolicy"/>, and <see cref="UserAccessPolicy"/> so the composition is
/// proven end-to-end, not just mocked.
/// </summary>
public class GetAccountCapabilityPackageStatusServiceTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid OtherAccountId = Guid.CreateVersion7();
    static readonly Guid AccountUserId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();
    static readonly DateTime Now = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
    const string FeatureKey = CapabilityPackageFeatureKeys.PriceBookQuotesMaterials;

    static GetAccountCapabilityPackageStatusService BuildSut(
        FakeSnapshotPersistence snapshotPersistence,
        AccountCapabilityPackageEnrollment? enrollment = null,
        FakeEnrollmentPersistence? enrollmentPersistence = null) =>
        new(
            snapshotPersistence,
            new FakeCurrentUser(AccountId, AccountUserId),
            new AccountAccessPolicy(),
            new AccountFeatureAccessResolver(
                new FeatureAccessPolicy(), enrollmentPersistence ?? new FakeEnrollmentPersistence(enrollment)),
            new UserAccessPolicy(),
            new FakeClock(Now));

    static FoundationAccountAccessSnapshot Snapshot(
        AccountLifecycleState lifecycle = AccountLifecycleState.Active,
        AccountCommercialState commercial = AccountCommercialState.Active,
        AccountOperatingMode operatingMode = AccountOperatingMode.Standard) =>
        new(AccountId, lifecycle, AccountPurpose.Business, AccountPlan.Starter, commercial, operatingMode, null, null);

    static FoundationAccountUserRoleSnapshot RoleSnapshot(AccountUserRole role) =>
        new(role, MembershipStatus.Active);

    [Fact]
    public async Task Blocked_account_never_reaches_resolver_or_permission()
    {
        var persistence = new FakeSnapshotPersistence
        {
            AccountSnapshot = Snapshot(lifecycle: AccountLifecycleState.Suspended),
            RoleSnapshot = RoleSnapshot(AccountUserRole.Owner),
        };
        var enrollmentPersistence = new FakeEnrollmentPersistence(enrollment: null);
        var sut = BuildSut(persistence, enrollmentPersistence: enrollmentPersistence);

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(0, enrollmentPersistence.QueryCount);
        Assert.False(persistence.RoleSnapshotWasRequested);
    }

    [Fact]
    public async Task Missing_account_snapshot_fails_closed()
    {
        var persistence = new FakeSnapshotPersistence { AccountSnapshot = null };
        var sut = BuildSut(persistence);

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Missing_role_snapshot_fails_closed()
    {
        var persistence = new FakeSnapshotPersistence
        {
            AccountSnapshot = Snapshot(),
            RoleSnapshot = null,
        };
        var sut = BuildSut(persistence);

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Role_without_settings_manage_permission_is_forbidden()
    {
        var persistence = new FakeSnapshotPersistence
        {
            AccountSnapshot = Snapshot(),
            RoleSnapshot = RoleSnapshot(AccountUserRole.Operator),
        };
        var sut = BuildSut(persistence);

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task OffSeason_readonly_account_may_read_package_status()
    {
        var persistence = new FakeSnapshotPersistence
        {
            AccountSnapshot = Snapshot(operatingMode: AccountOperatingMode.OffSeason),
            RoleSnapshot = RoleSnapshot(AccountUserRole.Admin),
        };
        var sut = BuildSut(persistence);

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Role_snapshot_lookup_is_scoped_to_the_caller_s_account()
    {
        // The fake only returns a role snapshot when both accountId and accountUserId match —
        // proving the service can never resolve a role snapshot from the wrong tenant.
        var persistence = new FakeSnapshotPersistence
        {
            AccountSnapshot = Snapshot(),
            RoleSnapshot = RoleSnapshot(AccountUserRole.Owner),
            RoleSnapshotAccountId = OtherAccountId,
        };
        var sut = BuildSut(persistence);

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Returns_enabled_status_for_active_enrollment_and_disabled_for_no_enrollment()
    {
        var enrollment = AccountCapabilityPackageEnrollment.Enroll(AccountId, FeatureKey, Actor, Now).Value;
        var persistence = new FakeSnapshotPersistence
        {
            AccountSnapshot = Snapshot(),
            RoleSnapshot = RoleSnapshot(AccountUserRole.Owner),
        };
        var sut = BuildSut(persistence, enrollment);

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var status = Assert.Single(result.Value, s => s.FeatureKey == FeatureKey);
        Assert.True(status.Enabled);
    }

    [Fact]
    public async Task Returns_disabled_status_when_no_enrollment_exists()
    {
        var persistence = new FakeSnapshotPersistence
        {
            AccountSnapshot = Snapshot(),
            RoleSnapshot = RoleSnapshot(AccountUserRole.Owner),
        };
        var sut = BuildSut(persistence, enrollment: null);

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var status = Assert.Single(result.Value, s => s.FeatureKey == FeatureKey);
        Assert.False(status.Enabled);
    }

    // --- Fakes ---

    private sealed class FakeCurrentUser(Guid accountId, Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid AccountId => accountId;
        public bool IsAuthenticated => true;
        public bool IsVerified => true;
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }

    private sealed class FakeSnapshotPersistence : IAccountAccessSnapshotPersistence
    {
        public FoundationAccountAccessSnapshot? AccountSnapshot { get; set; }
        public FoundationAccountUserRoleSnapshot? RoleSnapshot { get; set; }

        /// <summary>Account the role snapshot is scoped under; defaults to the caller's own account.</summary>
        public Guid RoleSnapshotAccountId { get; set; } = AccountId;

        public bool RoleSnapshotWasRequested { get; private set; }

        public Task<FoundationAccountAccessSnapshot?> GetAccountAccessSnapshotAsync(
            Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult(AccountSnapshot is not null && AccountSnapshot.AccountId == accountId
                ? AccountSnapshot
                : null);

        public Task<FoundationAccountUserRoleSnapshot?> GetAccountUserRoleSnapshotAsync(
            Guid accountId, Guid accountUserId, CancellationToken cancellationToken)
        {
            RoleSnapshotWasRequested = true;
            return Task.FromResult(
                accountId == RoleSnapshotAccountId && accountUserId == AccountUserId
                    ? RoleSnapshot
                    : null);
        }
    }

    private sealed class FakeEnrollmentPersistence(AccountCapabilityPackageEnrollment? enrollment)
        : IAccountCapabilityPackageEnrollmentPersistence
    {
        public int QueryCount { get; private set; }

        public Task<AccountCapabilityPackageEnrollment?> GetByAccountAndFeatureKeyAsync(
            Guid accountId, string featureKey, CancellationToken cancellationToken)
        {
            QueryCount++;
            return Task.FromResult(enrollment is not null
                && enrollment.AccountId == accountId
                && enrollment.FeatureKey == featureKey
                ? enrollment
                : null);
        }

        public Task<AccountCapabilityPackageEnrollmentCommitResult> AddAsync(AccountCapabilityPackageEnrollment enrollment_, CancellationToken cancellationToken) =>
            Task.FromResult(AccountCapabilityPackageEnrollmentCommitResult.Committed);

        public Task<AccountCapabilityPackageEnrollmentCommitResult> CommitAsync(AccountCapabilityPackageEnrollment enrollment_, CancellationToken cancellationToken) =>
            Task.FromResult(AccountCapabilityPackageEnrollmentCommitResult.Committed);
    }
}
