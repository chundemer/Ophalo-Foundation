using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.SharedKernel.Abstractions;
using Xunit;

namespace OpHalo.UnitTests.Accounts;

/// <summary>
/// Locks the internal operator gate order: authenticated → caller's own Internal-purpose account
/// + internal.entitlements.manage → target account exists. The target account's commercial state
/// is deliberately never consulted — this is an operator tool, not a request/service-delivery
/// action gated by <see cref="IAccountAccessPolicy"/>.
/// </summary>
public class InternalCapabilityPackageEnrollmentApiServiceTests
{
    static readonly Guid CallerAccountId = Guid.CreateVersion7();
    static readonly Guid CallerUserId = Guid.CreateVersion7();
    static readonly Guid TargetAccountId = Guid.CreateVersion7();
    static readonly DateTime Now = new(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
    const string FeatureKey = CapabilityPackageFeatureKeys.PriceBookQuotesMaterials;

    static InternalCapabilityPackageEnrollmentApiService BuildSut(
        FakeSnapshotPersistence snapshotPersistence,
        FakeEnrollmentPersistence? enrollmentPersistence = null,
        bool isAuthenticated = true) =>
        new(
            new InternalCapabilityPackageEnrollmentService(
                enrollmentPersistence ?? new FakeEnrollmentPersistence(null), new FakeClock(Now)),
            snapshotPersistence,
            new FakeCurrentUser(CallerAccountId, CallerUserId, isAuthenticated),
            new UserAccessPolicy());

    static FoundationAccountAccessSnapshot CallerSnapshot(AccountPurpose purpose) =>
        new(CallerAccountId, AccountLifecycleState.Active, purpose, AccountPlan.Starter,
            AccountCommercialState.Active, AccountOperatingMode.Standard, null, null);

    static FoundationAccountAccessSnapshot TargetSnapshot(
        AccountCommercialState commercial = AccountCommercialState.Active) =>
        new(TargetAccountId, AccountLifecycleState.Active, AccountPurpose.Business, AccountPlan.Starter,
            commercial, AccountOperatingMode.Standard, null, null);

    [Fact]
    public async Task Unauthenticated_caller_is_denied()
    {
        var sut = BuildSut(new FakeSnapshotPersistence(), isAuthenticated: false);

        var result = await sut.GetStatusAsync(TargetAccountId, FeatureKey, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task Business_purpose_caller_is_denied_even_with_owner_role()
    {
        var persistence = new FakeSnapshotPersistence
        {
            CallerSnapshot = CallerSnapshot(AccountPurpose.Business),
            CallerRoleSnapshot = new FoundationAccountUserRoleSnapshot(AccountUserRole.Owner, MembershipStatus.Active),
            TargetSnapshot = TargetSnapshot(),
        };
        var sut = BuildSut(persistence);

        var result = await sut.GetStatusAsync(TargetAccountId, FeatureKey, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Internal_purpose_caller_below_admin_is_denied()
    {
        var persistence = new FakeSnapshotPersistence
        {
            CallerSnapshot = CallerSnapshot(AccountPurpose.Internal),
            CallerRoleSnapshot = new FoundationAccountUserRoleSnapshot(AccountUserRole.Operator, MembershipStatus.Active),
            TargetSnapshot = TargetSnapshot(),
        };
        var sut = BuildSut(persistence);

        var result = await sut.GetStatusAsync(TargetAccountId, FeatureKey, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Unknown_target_account_is_denied_not_found_without_calling_enrollment_service()
    {
        var enrollmentPersistence = new FakeEnrollmentPersistence(null);
        var persistence = new FakeSnapshotPersistence
        {
            CallerSnapshot = CallerSnapshot(AccountPurpose.Internal),
            CallerRoleSnapshot = new FoundationAccountUserRoleSnapshot(AccountUserRole.Admin, MembershipStatus.Active),
            TargetSnapshot = null,
        };
        var sut = BuildSut(persistence, enrollmentPersistence);

        var result = await sut.GetStatusAsync(TargetAccountId, FeatureKey, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.NotFound, result.Error);
        Assert.Equal(0, enrollmentPersistence.QueryCount);
    }

    [Fact]
    public async Task Blocked_target_account_commercial_state_does_not_deny_the_operator_action()
    {
        var enrollmentPersistence = new FakeEnrollmentPersistence(null);
        var persistence = new FakeSnapshotPersistence
        {
            CallerSnapshot = CallerSnapshot(AccountPurpose.Internal),
            CallerRoleSnapshot = new FoundationAccountUserRoleSnapshot(AccountUserRole.Admin, MembershipStatus.Active),
            TargetSnapshot = TargetSnapshot(commercial: AccountCommercialState.PastDue),
        };
        var sut = BuildSut(persistence, enrollmentPersistence);

        var result = await sut.EnrollAsync(TargetAccountId, FeatureKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Admin_internal_caller_can_enroll_the_target_account()
    {
        var enrollmentPersistence = new FakeEnrollmentPersistence(null);
        var persistence = new FakeSnapshotPersistence
        {
            CallerSnapshot = CallerSnapshot(AccountPurpose.Internal),
            CallerRoleSnapshot = new FoundationAccountUserRoleSnapshot(AccountUserRole.Admin, MembershipStatus.Active),
            TargetSnapshot = TargetSnapshot(),
        };
        var sut = BuildSut(persistence, enrollmentPersistence);

        var result = await sut.EnrollAsync(TargetAccountId, FeatureKey, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TargetAccountId, result.Value.AccountId);
        Assert.Equal(CallerUserId, result.Value.ChangedByAccountUserId);
        Assert.Equal(CapabilityEnrollmentStatus.Enrolled, result.Value.Status);
    }

    // --- Fakes ---

    private sealed class FakeCurrentUser(Guid accountId, Guid userId, bool isAuthenticated) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid AccountId => accountId;
        public bool IsAuthenticated => isAuthenticated;
        public bool IsVerified => true;
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }

    private sealed class FakeSnapshotPersistence : IAccountAccessSnapshotPersistence
    {
        public FoundationAccountAccessSnapshot? CallerSnapshot { get; set; }
        public FoundationAccountUserRoleSnapshot? CallerRoleSnapshot { get; set; }
        public FoundationAccountAccessSnapshot? TargetSnapshot { get; set; }

        public Task<FoundationAccountAccessSnapshot?> GetAccountAccessSnapshotAsync(
            Guid accountId, CancellationToken cancellationToken)
        {
            if (CallerSnapshot is not null && accountId == CallerSnapshot.AccountId)
                return Task.FromResult<FoundationAccountAccessSnapshot?>(CallerSnapshot);
            if (TargetSnapshot is not null && accountId == TargetSnapshot.AccountId)
                return Task.FromResult<FoundationAccountAccessSnapshot?>(TargetSnapshot);
            return Task.FromResult<FoundationAccountAccessSnapshot?>(null);
        }

        public Task<FoundationAccountUserRoleSnapshot?> GetAccountUserRoleSnapshotAsync(
            Guid accountId, Guid accountUserId, CancellationToken cancellationToken) =>
            Task.FromResult(accountId == CallerAccountId && accountUserId == CallerUserId
                ? CallerRoleSnapshot
                : null);
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
