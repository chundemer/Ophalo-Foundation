using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.SharedKernel.Abstractions;
using Xunit;

namespace OpHalo.UnitTests.Accounts;

/// <summary>
/// Locks the internal operator state-machine orchestration (ADR-462): Enroll only ever creates the
/// first row for an (AccountId, FeatureKey) pair; Disable/Reenable transition that same row and
/// enforce the caller's expected concurrency token, mirroring
/// <c>OfferingAssemblyLifecycleService.ApplyTransitionAsync</c>.
/// </summary>
public class InternalCapabilityPackageEnrollmentServiceTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();
    static readonly DateTime Now = new(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
    const string FeatureKey = CapabilityPackageFeatureKeys.PriceBookQuotesMaterials;

    static InternalCapabilityPackageEnrollmentService BuildSut(FakeEnrollmentPersistence persistence) =>
        new(persistence, new FakeClock(Now));

    [Fact]
    public async Task Enroll_unknown_feature_key_fails_without_touching_persistence()
    {
        var persistence = new FakeEnrollmentPersistence(null);
        var sut = BuildSut(persistence);

        var result = await sut.EnrollAsync(AccountId, "not.a.real.key", Actor, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountCapabilityPackageEnrollmentErrors.UnknownFeatureKey, result.Error);
        Assert.Equal(0, persistence.QueryCount);
    }

    [Fact]
    public async Task Enroll_creates_the_first_row()
    {
        var persistence = new FakeEnrollmentPersistence(null);
        var sut = BuildSut(persistence);

        var result = await sut.EnrollAsync(AccountId, FeatureKey, Actor, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CapabilityEnrollmentStatus.Enrolled, result.Value.Status);
        Assert.Equal(Actor, result.Value.ChangedByAccountUserId);
        Assert.True(persistence.AddWasCalled);
    }

    [Fact]
    public async Task Enroll_on_an_existing_enrolled_row_fails()
    {
        var existing = AccountCapabilityPackageEnrollment.Enroll(AccountId, FeatureKey, Actor, Now).Value;
        var persistence = new FakeEnrollmentPersistence(existing);
        var sut = BuildSut(persistence);

        var result = await sut.EnrollAsync(AccountId, FeatureKey, Actor, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountCapabilityPackageEnrollmentErrors.AlreadyEnrolled, result.Error);
        Assert.False(persistence.AddWasCalled);
    }

    /// <summary>
    /// The prior test only ever constructed an Enrolled row — it did not actually exercise the
    /// "regardless of its status" claim. A disabled row must fail the same way: Enroll never
    /// reactivates, that is what ReenableAsync is for.
    /// </summary>
    [Fact]
    public async Task Enroll_on_an_existing_disabled_row_fails_and_does_not_reactivate_it()
    {
        var existing = AccountCapabilityPackageEnrollment.Enroll(AccountId, FeatureKey, Actor, Now).Value;
        existing.Disable(Actor, Now);
        var persistence = new FakeEnrollmentPersistence(existing);
        var sut = BuildSut(persistence);

        var result = await sut.EnrollAsync(AccountId, FeatureKey, Actor, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountCapabilityPackageEnrollmentErrors.AlreadyEnrolled, result.Error);
        Assert.False(persistence.AddWasCalled);
        Assert.False(persistence.CommitWasCalled);
        Assert.Equal(CapabilityEnrollmentStatus.Disabled, existing.Status);
    }

    [Fact]
    public async Task Enroll_losing_the_database_insert_race_fails_enrollment_already_exists()
    {
        var persistence = new FakeEnrollmentPersistence(null)
        {
            AddResult = AccountCapabilityPackageEnrollmentCommitResult.AlreadyExists,
        };
        var sut = BuildSut(persistence);

        var result = await sut.EnrollAsync(AccountId, FeatureKey, Actor, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountCapabilityPackageEnrollmentErrors.EnrollmentAlreadyExists, result.Error);
    }

    [Fact]
    public async Task Disable_losing_the_database_commit_race_fails_version_mismatch()
    {
        var existing = AccountCapabilityPackageEnrollment.Enroll(AccountId, FeatureKey, Actor, Now).Value;
        var persistence = new FakeEnrollmentPersistence(existing)
        {
            CommitResult = AccountCapabilityPackageEnrollmentCommitResult.ConcurrencyConflict,
        };
        var sut = BuildSut(persistence);

        var result = await sut.DisableAsync(
            AccountId, FeatureKey, existing.ConcurrencyVersion, Actor, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountCapabilityPackageEnrollmentErrors.VersionMismatch, result.Error);
    }

    [Fact]
    public async Task Disable_on_a_never_enrolled_account_fails_not_found()
    {
        var persistence = new FakeEnrollmentPersistence(null);
        var sut = BuildSut(persistence);

        var result = await sut.DisableAsync(AccountId, FeatureKey, Guid.NewGuid(), Actor, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountCapabilityPackageEnrollmentErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Disable_with_a_stale_expected_version_fails_conflict_without_mutating()
    {
        var existing = AccountCapabilityPackageEnrollment.Enroll(AccountId, FeatureKey, Actor, Now).Value;
        var staleVersion = Guid.NewGuid();
        var persistence = new FakeEnrollmentPersistence(existing);
        var sut = BuildSut(persistence);

        var result = await sut.DisableAsync(AccountId, FeatureKey, staleVersion, Actor, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountCapabilityPackageEnrollmentErrors.VersionMismatch, result.Error);
        Assert.Equal(CapabilityEnrollmentStatus.Enrolled, existing.Status);
        Assert.False(persistence.CommitWasCalled);
    }

    [Fact]
    public async Task Disable_then_reenable_round_trips_through_the_state_machine()
    {
        var existing = AccountCapabilityPackageEnrollment.Enroll(AccountId, FeatureKey, Actor, Now).Value;
        var persistence = new FakeEnrollmentPersistence(existing);
        var sut = BuildSut(persistence);

        var disableResult = await sut.DisableAsync(
            AccountId, FeatureKey, existing.ConcurrencyVersion, Actor, CancellationToken.None);
        Assert.True(disableResult.IsSuccess);
        Assert.Equal(CapabilityEnrollmentStatus.Disabled, disableResult.Value.Status);

        var reenableResult = await sut.ReenableAsync(
            AccountId, FeatureKey, disableResult.Value.ConcurrencyVersion, Actor, CancellationToken.None);
        Assert.True(reenableResult.IsSuccess);
        Assert.Equal(CapabilityEnrollmentStatus.Enrolled, reenableResult.Value.Status);
    }

    [Fact]
    public async Task GetStatus_unknown_feature_key_fails_without_touching_persistence()
    {
        var persistence = new FakeEnrollmentPersistence(null);
        var sut = BuildSut(persistence);

        var result = await sut.GetStatusAsync(AccountId, "not.a.real.key", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountCapabilityPackageEnrollmentErrors.UnknownFeatureKey, result.Error);
        Assert.Equal(0, persistence.QueryCount);
    }

    // --- Fakes ---

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }

    private sealed class FakeEnrollmentPersistence(AccountCapabilityPackageEnrollment? enrollment)
        : IAccountCapabilityPackageEnrollmentPersistence
    {
        public int QueryCount { get; private set; }
        public bool AddWasCalled { get; private set; }
        public bool CommitWasCalled { get; private set; }

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

        public AccountCapabilityPackageEnrollmentCommitResult AddResult { get; set; } =
            AccountCapabilityPackageEnrollmentCommitResult.Committed;

        public AccountCapabilityPackageEnrollmentCommitResult CommitResult { get; set; } =
            AccountCapabilityPackageEnrollmentCommitResult.Committed;

        public Task<AccountCapabilityPackageEnrollmentCommitResult> AddAsync(
            AccountCapabilityPackageEnrollment enrollment_, CancellationToken cancellationToken)
        {
            AddWasCalled = true;
            return Task.FromResult(AddResult);
        }

        public Task<AccountCapabilityPackageEnrollmentCommitResult> CommitAsync(
            AccountCapabilityPackageEnrollment enrollment_, CancellationToken cancellationToken)
        {
            CommitWasCalled = true;
            return Task.FromResult(CommitResult);
        }
    }
}
