using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.UnitTests.Keep;

public class KeepSetupServiceTests
{
    private static readonly DateTime Now       = new(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid     AccountId = Guid.NewGuid();
    private static readonly Guid     UserId    = Guid.NewGuid();

    private static KeepSetupService BuildSut(FakeSetupPersistence persistence, FakePolicyPersistence? policyPersistence = null) =>
        new(
            persistence,
            new FakeCurrentUser(UserId, AccountId),
            new FakeUserAccessPolicy(),
            new FakeAccountAccessPolicy(),
            new FakeClock(Now),
            new KeepResponsePolicyService(
                policyPersistence ?? new FakePolicyPersistence { UserSnapshot = persistence.UserSnapshot, AccountSnapshot = persistence.AccountSnapshot },
                new FakeCurrentUser(UserId, AccountId),
                new FakeUserAccessPolicy(),
                new FakeAccountAccessPolicy(),
                new FakeClock(Now)));

    private static FakePolicyPersistence PolicyFor(FakeSetupPersistence p) =>
        new() { UserSnapshot = p.UserSnapshot, AccountSnapshot = p.AccountSnapshot };

    // ── UpdateCalendarAsync (5c-1b) ──────────────────────────────────────────

    [Fact]
    public async Task UpdateCalendar_DiffsClosuresAndPassesVersionThroughUnchanged()
    {
        var persistence = HappyPersistence();
        persistence.Calendar = new KeepCalendarSnapshot([], [new DateOnly(2026, 12, 25), new DateOnly(2026, 12, 26)]);
        var policy = PolicyFor(persistence);
        var sut = BuildSut(persistence, policy);

        var result = await sut.UpdateCalendarAsync(
            [new KeepSetupWeeklyIntervalInput("monday", "08:00", "17:30")],
            ["2026-12-26", "2027-01-01"],
            expectedSettingsVersion: null);

        Assert.True(result.IsSuccess);
        Assert.Null(policy.LastExpectedVersion);
        Assert.Equal([new DateOnly(2027, 1, 1)], policy.LastAdd);
        Assert.Equal([new DateOnly(2026, 12, 25)], policy.LastRemove);
        Assert.Equal((DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(17, 30)), Assert.Single(policy.LastIntervals));
    }

    [Fact]
    public async Task UpdateCalendar_VersionMismatchFromGovernedPath_IsReturnedUnchanged()
    {
        var persistence = HappyPersistence();
        var policy = PolicyFor(persistence);
        policy.Failure = KeepResponsePolicyErrors.SettingsVersionMismatch;

        var result = await BuildSut(persistence, policy).UpdateCalendarAsync([], [], "stale");

        Assert.Equal(KeepResponsePolicyErrors.SettingsVersionMismatch, result.Error);
        Assert.Equal("stale", policy.LastExpectedVersion);
    }

    [Fact]
    public async Task UpdateCalendar_DuplicateClosureDateInRequest_Fails422Error()
    {
        var persistence = HappyPersistence();
        var policy = PolicyFor(persistence);

        var result = await BuildSut(persistence, policy).UpdateCalendarAsync([], ["2026-12-25", "2026-12-25"], "v");

        Assert.Equal(KeepResponsePolicyErrors.DuplicateClosureDate, result.Error);
        Assert.False(policy.Called);
    }

    [Theory]
    [InlineData("Funday", "08:00", "17:00", "2026-12-25")]
    [InlineData("1", "08:00", "17:00", "2026-12-25")]
    [InlineData("Monday", "8am", "17:00", "2026-12-25")]
    [InlineData("Monday", "08:00", "17:00", "25/12/2026")]
    public async Task UpdateCalendar_ParseFailure_ReturnsValidationErrorWithoutWriting(
        string weekday, string opens, string closes, string closure)
    {
        var persistence = HappyPersistence();
        var policy = PolicyFor(persistence);

        var result = await BuildSut(persistence, policy).UpdateCalendarAsync(
            [new KeepSetupWeeklyIntervalInput(weekday, opens, closes)], [closure], "v");

        Assert.Equal("KeepSetup.CalendarValidation", result.Error.Code);
        Assert.False(policy.Called);
    }

    [Fact]
    public async Task UpdateCalendar_NullCollections_ReturnsValidationError()
    {
        var persistence = HappyPersistence();
        var result = await BuildSut(persistence).UpdateCalendarAsync(null, null, "v");
        Assert.Equal("KeepSetup.CalendarValidation", result.Error.Code);
    }

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

    [Fact]
    public async Task UpdateProfile_rejected_timezone_governance_fails_the_whole_save()
    {
        // ADR-505 batch 4c: a rejected governed timezone change (e.g. an unreachable
        // staffed-hours target under the new zone) must fail the entire profile save — the
        // business-name change staged earlier in the same call must never be persisted either.
        var error = OpHalo.Keep.Core.Errors.KeepResponsePolicyErrors.StaffedHoursTargetUnreachable;
        var persistence = HappyPersistence();
        persistence.TimeZoneGovernanceFailure = error;
        var sut = BuildSut(persistence);

        var result = await sut.UpdateProfileAsync(
            "New Business Name", "America/New_York", null, null, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
        Assert.Null(persistence.SavedProfile);
    }

    // --- GetSetupAsync ---

    [Fact]
    public async Task GetSetup_without_a_policy_returns_defaults_continuous_bases_and_a_version()
    {
        var sut = BuildSut(HappyPersistence());

        var result = await sut.GetSetupAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("Continuous", result.Value.ResponsePolicy.FirstResponseTimingBasis);
        Assert.Equal("Continuous", result.Value.ResponsePolicy.StandardResponseTimingBasis);
        Assert.Equal("Continuous", result.Value.ResponsePolicy.PriorityResponseTimingBasis);
        Assert.Empty(result.Value.Calendar.WeeklyIntervals);
        Assert.Empty(result.Value.Calendar.ClosureDates);
        Assert.False(string.IsNullOrEmpty(result.Value.SettingsVersion));
    }

    [Fact]
    public async Task GetSetup_returns_string_bases_and_calendar_and_a_version_that_tracks_the_state()
    {
        var persistence = HappyPersistence();
        var policy = KeepResponsePolicy.Create(AccountId, 60, 240, 60, 5);
        policy.UpdateTargetsAndTimingBasis(
            60, 240, 60, 5,
            ResponseTimingBasis.StaffedHours, ResponseTimingBasis.Continuous, ResponseTimingBasis.StaffedHours);
        persistence.Policy = policy;
        persistence.Calendar = new KeepCalendarSnapshot(
            [
                new KeepWeeklyIntervalSnapshot(DayOfWeek.Tuesday, new TimeOnly(8, 30), new TimeOnly(17, 0)),
                new KeepWeeklyIntervalSnapshot(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(16, 45)),
            ],
            [new DateOnly(2026, 12, 25)]);
        var sut = BuildSut(persistence);

        var first = await sut.GetSetupAsync();

        Assert.True(first.IsSuccess);
        Assert.Equal("StaffedHours", first.Value.ResponsePolicy.FirstResponseTimingBasis);
        Assert.Equal("Continuous", first.Value.ResponsePolicy.StandardResponseTimingBasis);
        Assert.Equal("StaffedHours", first.Value.ResponsePolicy.PriorityResponseTimingBasis);
        Assert.Equal(
            [("Monday", "08:00", "16:45"), ("Tuesday", "08:30", "17:00")],
            first.Value.Calendar.WeeklyIntervals.Select(i => (i.Weekday, i.OpensAt, i.ClosesAt)));
        Assert.Equal(["2026-12-25"], first.Value.Calendar.ClosureDates);

        persistence.Calendar = new KeepCalendarSnapshot([], []);
        var second = await sut.GetSetupAsync();

        Assert.NotEqual(first.Value.SettingsVersion, second.Value.SettingsVersion);
    }

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
        public string?                 ActorDisplayName { get; set; } = "Owner";

        /// <summary>
        /// When set, <see cref="SaveProfileWithTimeZoneAsync"/> fails with this error instead of
        /// saving — simulates a rejected governed timezone change, to prove the whole profile
        /// save (including the already-staged business-name change) is discarded, not partially
        /// applied.
        /// </summary>
        public Error? TimeZoneGovernanceFailure { get; set; }

        public Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(UserSnapshot);

        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(AccountSnapshot);

        public Task<string?> GetActorDisplayNameAsync(Guid accountUserId, CancellationToken ct) =>
            Task.FromResult(ActorDisplayName);

        public Task<(Account account, KeepBusinessProfile? profile)> GetProfileDataAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult((Account!, Profile));

        public KeepResponsePolicy? Policy { get; set; }
        public KeepCalendarSnapshot Calendar { get; set; } = new([], []);

        public Task<KeepResponsePolicy?> GetPolicyAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult(Policy);

        public Task<KeepCalendarSnapshot> GetCalendarAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult(Calendar);

        public Task<Result> SaveProfileWithTimeZoneAsync(
            Account account, KeepBusinessProfile profile, KeepProductOpsEvent? opsEvent,
            Guid actorAccountUserId, string actorDisplayName, string timeZone, DateTime occurredAtUtc, CancellationToken ct)
        {
            if (TimeZoneGovernanceFailure is { } error)
                return Task.FromResult(Result.Failure(error));

            SavedProfile = profile;
            return Task.FromResult(Result.Success());
        }

        public Task SavePolicyAsync(KeepResponsePolicy policy, bool isNew, KeepProductOpsEvent? opsEvent, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class FakePolicyPersistence : IKeepResponsePolicyPersistence
    {
        public AccountUserSnapshot?   UserSnapshot    { get; set; }
        public AccountAccessSnapshot? AccountSnapshot { get; set; }
        public Error? Failure { get; set; }
        public bool Called { get; private set; }
        public string? LastExpectedVersion { get; private set; }
        public IReadOnlyList<(DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)> LastIntervals { get; private set; } = [];
        public IReadOnlyList<DateOnly> LastAdd { get; private set; } = [];
        public IReadOnlyList<DateOnly> LastRemove { get; private set; } = [];

        public Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid id, CancellationToken ct) => Task.FromResult(UserSnapshot);
        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid id, CancellationToken ct) => Task.FromResult(AccountSnapshot);
        public Task<string?> GetActorDisplayNameAsync(Guid accountUserId, CancellationToken ct) => Task.FromResult<string?>("Owner");

        public Task<Result> UpdateCalendarAsync(
            Guid accountId, Guid actorAccountUserId, string actorDisplayName,
            IReadOnlyList<(DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)> weeklyIntervals,
            IReadOnlyList<DateOnly> closureDatesToAdd, IReadOnlyList<DateOnly> closureDatesToRemove,
            string? expectedSettingsVersion, DateTime occurredAtUtc, CancellationToken ct)
        {
            Called = true;
            LastExpectedVersion = expectedSettingsVersion;
            LastIntervals = weeklyIntervals;
            LastAdd = closureDatesToAdd;
            LastRemove = closureDatesToRemove;
            return Task.FromResult(Failure is { } e ? Result.Failure(e) : Result.Success());
        }

        public Task<Result> UpdatePolicyTargetsAsync(
            Guid accountId, Guid actorAccountUserId, string actorDisplayName, int a, int b, int c, int d,
            ResponseTimingBasis? e, ResponseTimingBasis? f, ResponseTimingBasis? g,
            string? expectedSettingsVersion, DateTime occurredAtUtc, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Result> UpdateTimeZoneAsync(
            Guid accountId, Guid actorAccountUserId, string actorDisplayName, string timeZone,
            DateTime occurredAtUtc, CancellationToken ct) =>
            throw new NotSupportedException();
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
