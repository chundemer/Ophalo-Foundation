using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.PublicIntake;
using OpHalo.Keep.Application.Services;
using OpHalo.Keep.Core.Entities;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.UnitTests.Keep;

public class KeepPublicIntakeServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid AccountId = Guid.NewGuid();

    // --- Helpers ----------------------------------------------------------------

    private static CreateKeepPublicIntakeService BuildSut(
        FakeIntakePersistence? persistence = null,
        AccountAccessPosture posture = AccountAccessPosture.FullAccess,
        bool featureEnabled = true)
    {
        persistence ??= HappyPathPersistence();
        return new CreateKeepPublicIntakeService(
            persistence,
            new KeepTokenService(),
            new FakeAccountAccessPolicy(posture),
            new FakeFeatureAccessPolicy(featureEnabled),
            new FakeClock(Now));
    }

    private static FakeIntakePersistence HappyPathPersistence()
    {
        var tokenService = new KeepTokenService();
        var rawToken = tokenService.GeneratePublicIntakeToken();
        var tokenHash = tokenService.HashPublicIntakeToken(rawToken);
        var link = KeepPublicIntakeLink.Create(AccountId, "test-slug", tokenHash);

        var persistence = new FakeIntakePersistence
        {
            RawToken = rawToken,
            LinkToReturn = link,
            AccountSnapshotToReturn = ActiveSnapshot()
        };
        persistence.CommitResults.Enqueue(PublicIntakeCommitResult.Committed);
        return persistence;
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

    private static CreateKeepPublicIntakeCommand ValidCommand(FakeIntakePersistence persistence) => new(
        persistence.RawToken,
        "Jane Doe",
        "555-1234",
        "jane@example.com",
        "Help with the boiler");

    // --- Validation errors -------------------------------------------------------

    [Fact]
    public async Task Execute_returns_error_when_customer_name_missing()
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var command = new CreateKeepPublicIntakeCommand(p.RawToken, "", "555-1234", null, "Desc");

        var result = await sut.ExecuteAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.CustomerNameRequired", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_error_when_customer_phone_missing()
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var command = new CreateKeepPublicIntakeCommand(p.RawToken, "Jane", "  ", null, "Desc");

        var result = await sut.ExecuteAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.CustomerPhoneRequired", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_error_when_description_missing()
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var command = new CreateKeepPublicIntakeCommand(p.RawToken, "Jane", "5551234", null, "");

        var result = await sut.ExecuteAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.DescriptionRequired", result.Error.Code);
    }

    // --- Token guard ------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Execute_returns_unavailable_when_token_blank(string token)
    {
        var sut = BuildSut();
        var command = new CreateKeepPublicIntakeCommand(token, "Jane", "5551234", null, "Desc");

        var result = await sut.ExecuteAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal("keep.public_intake.unavailable", result.Error.Code);
    }

    // --- Link / account gates ---------------------------------------------------

    [Fact]
    public async Task Execute_returns_unavailable_when_link_not_found()
    {
        var p = HappyPathPersistence();
        p.LinkToReturn = null;
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync(ValidCommand(p));

        Assert.False(result.IsSuccess);
        Assert.Equal("keep.public_intake.unavailable", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_unavailable_when_link_is_revoked()
    {
        var p = HappyPathPersistence();
        p.LinkToReturn!.Revoke(Now);
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync(ValidCommand(p));

        Assert.False(result.IsSuccess);
        Assert.Equal("keep.public_intake.unavailable", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_unavailable_when_account_snapshot_missing()
    {
        var p = HappyPathPersistence();
        p.AccountSnapshotToReturn = null;
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync(ValidCommand(p));

        Assert.False(result.IsSuccess);
        Assert.Equal("keep.public_intake.unavailable", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_unavailable_when_account_is_blocked()
    {
        var sut = BuildSut(posture: AccountAccessPosture.Blocked);

        var p = HappyPathPersistence();
        var result = await sut.ExecuteAsync(ValidCommand(p));

        Assert.False(result.IsSuccess);
        Assert.Equal("keep.public_intake.unavailable", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_unavailable_when_account_is_readonly_because_intake_is_a_write()
    {
        var sut = BuildSut(posture: AccountAccessPosture.ReadOnly);

        var p = HappyPathPersistence();
        var result = await sut.ExecuteAsync(ValidCommand(p));

        Assert.False(result.IsSuccess);
        Assert.Equal("keep.public_intake.unavailable", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_unavailable_when_feature_not_enabled()
    {
        var sut = BuildSut(featureEnabled: false);

        var p = HappyPathPersistence();
        var result = await sut.ExecuteAsync(ValidCommand(p));

        Assert.False(result.IsSuccess);
        Assert.Equal("keep.public_intake.unavailable", result.Error.Code);
    }

    // --- Happy paths ------------------------------------------------------------

    [Fact]
    public async Task Execute_succeeds_and_returns_result_for_new_customer()
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync(ValidCommand(p));

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value.RequestId);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.ReferenceCode));
        Assert.False(string.IsNullOrWhiteSpace(result.Value.PageToken));
    }

    [Fact]
    public async Task Execute_succeeds_for_existing_customer_and_updates_contact_info()
    {
        var p = HappyPathPersistence();
        var existingCustomer = KeepCustomer.Create(AccountId, "Old Name", "555-1234");
        p.ExistingCustomer = existingCustomer;
        p.CommitResults.Enqueue(PublicIntakeCommitResult.Committed);
        var sut = BuildSut(p);

        var command = new CreateKeepPublicIntakeCommand(p.RawToken, "New Name", "555-1234", "new@email.com", "Desc");
        var result = await sut.ExecuteAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", existingCustomer.Name);
    }

    [Fact]
    public async Task Execute_retries_on_token_collision_and_succeeds()
    {
        var p = HappyPathPersistence();
        p.CommitResults.Clear();
        p.CommitResults.Enqueue(PublicIntakeCommitResult.UniqueTokenCollision);
        p.CommitResults.Enqueue(PublicIntakeCommitResult.Committed);
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync(ValidCommand(p));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, p.CommitCallCount);
    }

    [Fact]
    public async Task Execute_throws_after_max_attempts()
    {
        var p = HappyPathPersistence();
        p.CommitResults.Clear();
        for (var i = 0; i < 5; i++)
            p.CommitResults.Enqueue(PublicIntakeCommitResult.UniqueTokenCollision);
        var sut = BuildSut(p);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ExecuteAsync(ValidCommand(p)));
    }

    // --- Length validation ------------------------------------------------------

    [Fact]
    public async Task Execute_returns_error_when_name_exceeds_200_chars()
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var command = new CreateKeepPublicIntakeCommand(p.RawToken, new string('A', 201), "5551234", null, "Desc");

        var result = await sut.ExecuteAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.CustomerNameTooLong", result.Error.Code);
        Assert.Equal(0, p.CommitCallCount);
    }

    [Fact]
    public async Task Execute_accepts_name_at_200_chars()
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var command = new CreateKeepPublicIntakeCommand(p.RawToken, new string('A', 200), "5551234", null, "Desc");

        var result = await sut.ExecuteAsync(command);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Execute_returns_error_when_phone_exceeds_50_chars()
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var command = new CreateKeepPublicIntakeCommand(p.RawToken, "Jane", new string('1', 51), null, "Desc");

        var result = await sut.ExecuteAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.CustomerPhoneTooLong", result.Error.Code);
        Assert.Equal(0, p.CommitCallCount);
    }

    [Fact]
    public async Task Execute_returns_error_when_email_exceeds_320_chars()
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var longEmail = new string('a', 315) + "@x.com"; // 321 chars > 320
        var command = new CreateKeepPublicIntakeCommand(p.RawToken, "Jane", "5551234", longEmail, "Desc");

        var result = await sut.ExecuteAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.CustomerEmailTooLong", result.Error.Code);
        Assert.Equal(0, p.CommitCallCount);
    }

    [Fact]
    public async Task Execute_returns_error_when_description_exceeds_4000_chars()
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var command = new CreateKeepPublicIntakeCommand(p.RawToken, "Jane", "5551234", null, new string('X', 4001));

        var result = await sut.ExecuteAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.DescriptionTooLong", result.Error.Code);
        Assert.Equal(0, p.CommitCallCount);
    }

    [Fact]
    public async Task Execute_accepts_description_at_4000_chars()
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var command = new CreateKeepPublicIntakeCommand(p.RawToken, "Jane", "5551234", null, new string('X', 4000));

        var result = await sut.ExecuteAsync(command);

        Assert.True(result.IsSuccess);
    }

    // --- Phone character validation ---------------------------------------------

    [Theory]
    [InlineData("555abc1234")]      // letters rejected
    [InlineData("555#1234")]        // unsupported symbol
    [InlineData("5551234@test")]    // @ rejected
    [InlineData("555+1234")]        // + in middle rejected
    [InlineData("++5551234")]       // repeated + rejected
    public async Task Execute_returns_error_when_phone_has_invalid_characters(string phone)
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var command = new CreateKeepPublicIntakeCommand(p.RawToken, "Jane", phone, null, "Desc");

        var result = await sut.ExecuteAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.CustomerPhoneInvalidCharacters", result.Error.Code);
        Assert.Equal(0, p.CommitCallCount);
    }

    [Theory]
    [InlineData("+61 412 345 678")]  // leading + allowed
    [InlineData("(02) 9876-5432")]   // parens and hyphen
    [InlineData("555.123.4567")]     // dots allowed
    [InlineData("5551234")]          // plain digits
    public async Task Execute_accepts_phone_with_valid_formatting_characters(string phone)
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var command = new CreateKeepPublicIntakeCommand(p.RawToken, "Jane", phone, null, "Desc");

        var result = await sut.ExecuteAsync(command);

        Assert.True(result.IsSuccess);
    }

    // --- Phone digit-count validation -------------------------------------------

    [Theory]
    [InlineData("123456")]       // 6 digits — too few
    [InlineData("1234567890123456")] // 16 digits — too many
    public async Task Execute_returns_error_when_phone_digit_count_out_of_range(string phone)
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var command = new CreateKeepPublicIntakeCommand(p.RawToken, "Jane", phone, null, "Desc");

        var result = await sut.ExecuteAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.CustomerPhoneInvalidFormat", result.Error.Code);
        Assert.Equal(0, p.CommitCallCount);
    }

    // --- Email syntax validation ------------------------------------------------

    [Theory]
    [InlineData("notanemail")]
    [InlineData("missing@")]
    [InlineData("@nodomain")]
    public async Task Execute_returns_error_when_email_is_malformed(string email)
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var command = new CreateKeepPublicIntakeCommand(p.RawToken, "Jane", "5551234", email, "Desc");

        var result = await sut.ExecuteAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.CustomerEmailInvalid", result.Error.Code);
        Assert.Equal(0, p.CommitCallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Execute_accepts_omitted_or_blank_email(string? email)
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var command = new CreateKeepPublicIntakeCommand(p.RawToken, "Jane", "5551234", email, "Desc");

        var result = await sut.ExecuteAsync(command);

        Assert.True(result.IsSuccess);
    }

    // --- Email preservation -----------------------------------------------------

    [Fact]
    public async Task Execute_preserves_existing_customer_email_when_repeat_intake_omits_email()
    {
        var p = HappyPathPersistence();
        var existingCustomer = KeepCustomer.Create(AccountId, "Jane", "555-1234", "original@example.com");
        p.ExistingCustomer = existingCustomer;
        p.CommitResults.Enqueue(PublicIntakeCommitResult.Committed);
        var sut = BuildSut(p);

        // Second intake omits email
        var command = new CreateKeepPublicIntakeCommand(p.RawToken, "Jane Updated", "555-1234", null, "Desc");
        var result = await sut.ExecuteAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal("original@example.com", existingCustomer.Email); // preserved
        Assert.Equal("Jane Updated", existingCustomer.Name);           // always updated
    }

    [Fact]
    public async Task Execute_replaces_existing_customer_email_when_nonblank_email_supplied()
    {
        var p = HappyPathPersistence();
        var existingCustomer = KeepCustomer.Create(AccountId, "Jane", "555-1234", "old@example.com");
        p.ExistingCustomer = existingCustomer;
        p.CommitResults.Enqueue(PublicIntakeCommitResult.Committed);
        var sut = BuildSut(p);

        var command = new CreateKeepPublicIntakeCommand(p.RawToken, "Jane", "555-1234", "new@example.com", "Desc");
        var result = await sut.ExecuteAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal("new@example.com", existingCustomer.Email);
    }

    // --- Concurrent customer race recovery --------------------------------------

    [Fact]
    public async Task Execute_recovers_from_customer_canonical_phone_collision_and_succeeds()
    {
        var p = HappyPathPersistence();
        // First FindCustomer call: no existing customer (both threads see null simultaneously).
        // Second FindCustomer call (after collision): winning customer is now visible.
        var winningCustomer = KeepCustomer.Create(AccountId, "Jane Original", "555-1234");
        p.CustomerResults.Enqueue(null);           // initial lookup: no customer yet
        p.CustomerResults.Enqueue(winningCustomer); // post-collision re-read

        p.CommitResults.Clear();
        p.CommitResults.Enqueue(PublicIntakeCommitResult.CustomerCanonicalPhoneCollision);
        p.CommitResults.Enqueue(PublicIntakeCommitResult.Committed);

        var sut = BuildSut(p);
        var command = new CreateKeepPublicIntakeCommand(p.RawToken, "Jane New", "555-1234", "new@example.com", "Desc");
        var result = await sut.ExecuteAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, p.CommitCallCount);
        Assert.Equal("Jane New", winningCustomer.Name);          // UpdateContactInfo applied
        Assert.Equal("new@example.com", winningCustomer.Email);  // email updated (non-blank)
    }

    [Fact]
    public async Task Execute_preserves_winning_customer_email_after_collision_when_intake_omits_email()
    {
        var p = HappyPathPersistence();
        var winningCustomer = KeepCustomer.Create(AccountId, "Jane Original", "555-1234", "original@example.com");
        p.CustomerResults.Enqueue(null);
        p.CustomerResults.Enqueue(winningCustomer);

        p.CommitResults.Clear();
        p.CommitResults.Enqueue(PublicIntakeCommitResult.CustomerCanonicalPhoneCollision);
        p.CommitResults.Enqueue(PublicIntakeCommitResult.Committed);

        var sut = BuildSut(p);
        var command = new CreateKeepPublicIntakeCommand(p.RawToken, "Jane New", "555-1234", null, "Desc");
        await sut.ExecuteAsync(command);

        Assert.Equal("original@example.com", winningCustomer.Email); // preserved — omission is not a clear command
    }

    [Fact]
    public async Task Execute_counts_collision_against_max_attempts_and_throws_after_exhaustion()
    {
        var p = HappyPathPersistence();
        // Queue null for each FindCustomer call (5 initial + up to 4 post-collision re-reads)
        for (var i = 0; i < 10; i++)
            p.CustomerResults.Enqueue(null);

        p.CommitResults.Clear();
        for (var i = 0; i < 5; i++)
            p.CommitResults.Enqueue(PublicIntakeCommitResult.CustomerCanonicalPhoneCollision);

        var sut = BuildSut(p);

        // Throws because re-read after each collision returns null (FindCustomer → null → exception)
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync(ValidCommand(p)));
    }

    // --- Fakes ------------------------------------------------------------------

    private sealed class FakeIntakePersistence : IKeepIntakePersistence
    {
        public string RawToken { get; set; } = string.Empty;
        public KeepPublicIntakeLink? LinkToReturn { get; set; }
        public AccountAccessSnapshot? AccountSnapshotToReturn { get; set; }
        public KeepCustomer? ExistingCustomer { get; set; }
        public Queue<PublicIntakeCommitResult> CommitResults { get; } = new();
        public int CommitCallCount { get; private set; }

        public Task<KeepPublicIntakeLink?> FindActivePublicIntakeLinkByTokenHashAsync(
            string tokenHash, CancellationToken ct) =>
            Task.FromResult(LinkToReturn);

        public Task<KeepPublicIntakeLink?> FindActivePublicIntakeLinkBySlugAsync(
            string slug, CancellationToken ct) =>
            Task.FromResult(LinkToReturn);

        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(
            Guid accountId, CancellationToken ct) =>
            Task.FromResult(AccountSnapshotToReturn);

        public Queue<KeepCustomer?> CustomerResults { get; } = new();

        public Task<KeepCustomer?> FindCustomerByCanonicalPhoneAsync(
            Guid accountId, string canonicalPhone, CancellationToken ct) =>
            Task.FromResult(CustomerResults.Count > 0 ? CustomerResults.Dequeue() : ExistingCustomer);

        public KeepResponsePolicy? ResponsePolicyToReturn { get; set; }

        public Task<KeepResponsePolicy?> GetResponsePolicyAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult(ResponsePolicyToReturn);

        public Task<bool> PageTokenExistsAsync(string pageToken, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<bool> ReferenceCodeExistsAsync(Guid accountId, string referenceCode, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<PublicIntakeCommitResult> CommitPublicIntakeAsync(
            KeepCustomer customer, KeepRequest request, KeepRequestEvent requestEvent, CancellationToken ct)
        {
            CommitCallCount++;
            var result = CommitResults.Count > 0
                ? CommitResults.Dequeue()
                : PublicIntakeCommitResult.Committed;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeAccountAccessPolicy(AccountAccessPosture posture) : IAccountAccessPolicy
    {
        public AccountAccessDecision Evaluate(AccountAccessContext context) =>
            new(posture, AccountAccessReason.None, null);
    }

    private sealed class FakeFeatureAccessPolicy(bool enabled) : IFeatureAccessPolicy
    {
        public bool IsEnabled(AccountPlan plan, string featureKey) => enabled;
        public int GetLimit(AccountPlan plan, string limitKey) => 0;
        public int ResolveLimit(AccountEntitlements entitlements, string limitKey) => 0;
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }
}
