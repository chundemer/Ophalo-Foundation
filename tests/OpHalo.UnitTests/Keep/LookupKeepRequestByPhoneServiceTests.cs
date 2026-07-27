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

public class LookupKeepRequestByPhoneServiceTests
{
    private static readonly DateTime Now       = new(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid AccountId     = Guid.NewGuid();
    private static readonly Guid OtherAccountId = Guid.NewGuid();
    private static readonly Guid UserId        = Guid.NewGuid();

    private static LookupKeepRequestByPhoneService BuildSut(FakeBusinessPersistence business) =>
        new(
            new FakeOperatePersistence(),
            business,
            new FakeCurrentUser(UserId, AccountId),
            new FakeUserAccessPolicy(),
            new FakeAccountAccessPolicy(),
            new FakeFeatureAccessPolicy(),
            new FakeClock(Now));

    private static KeepRequest LegacyRequest(Guid accountId, string customerPhone, string name, string? email) =>
        KeepRequest.CreateByBusiness(
            accountId,
            Guid.NewGuid(),
            name,
            customerPhone,
            email,
            "Fix the boiler",
            "REF-1",
            "token-1",
            Now.AddDays(-30),
            KeepRequestSource.Phone);

    [Fact]
    public async Task Falls_back_to_legacy_request_phone_match_when_no_customer_row()
    {
        var business = new FakeBusinessPersistence
        {
            LegacyMatch = LegacyRequest(AccountId, "(555) 555-0100", "Legacy Larry", "larry@example.com"),
        };
        var sut = BuildSut(business);

        var result = await sut.ExecuteAsync("5555550100");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Customer);
        Assert.NotNull(result.Value.Prefill);
        Assert.Equal("Legacy Larry", result.Value.Prefill!.Name);
        Assert.Equal("larry@example.com", result.Value.Prefill.Email);
        Assert.Empty(result.Value.ActiveRequests);
    }

    [Fact]
    public async Task Returns_no_prefill_when_neither_customer_nor_legacy_request_match()
    {
        var business = new FakeBusinessPersistence { LegacyMatch = null };
        var sut = BuildSut(business);

        var result = await sut.ExecuteAsync("5555550199");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Customer);
        Assert.Null(result.Value.Prefill);
    }

    [Fact]
    public async Task Does_not_query_legacy_fallback_when_a_customer_row_already_matches()
    {
        var customer = KeepCustomer.Create(AccountId, "Sarah Mitchell", "5555550100", "sarah@example.com");
        var business = new FakeBusinessPersistence
        {
            ExistingCustomer = customer,
            // If the service queried the fallback despite a customer match, this would surface as
            // a prefill on the result and fail the assertion below.
            LegacyMatch = LegacyRequest(AccountId, "5555550100", "Should Not Appear", null),
        };
        var sut = BuildSut(business);

        var result = await sut.ExecuteAsync("5555550100");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Customer);
        Assert.Null(result.Value.Prefill);
        Assert.Equal(0, business.LegacyLookupCalls);
    }

    [Fact]
    public async Task Legacy_fallback_is_scoped_to_the_caller_account()
    {
        var business = new FakeBusinessPersistence
        {
            LegacyMatch = LegacyRequest(OtherAccountId, "5555550100", "Cross Account Cathy", null),
        };
        var sut = BuildSut(business);

        var result = await sut.ExecuteAsync("5555550100");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Prefill);
    }

    // ---------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------

    private sealed class FakeBusinessPersistence : IKeepBusinessRequestPersistence
    {
        public KeepCustomer? ExistingCustomer { get; set; }
        public KeepRequest? LegacyMatch       { get; set; }
        public int LegacyLookupCalls          { get; private set; }

        public Task<KeepCustomer?> FindCustomerByCanonicalPhoneAsync(Guid accountId, string canonicalPhone, CancellationToken ct) =>
            Task.FromResult(ExistingCustomer);

        public Task<IReadOnlyList<KeepRequest>> FindActiveRequestsByCustomerIdAsync(
            Guid accountId, Guid customerId, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KeepRequest>>(Array.Empty<KeepRequest>());

        public Task<KeepRequest?> FindMostRecentRequestByCustomerPhoneAsync(
            Guid accountId, string canonicalPhone, CancellationToken ct)
        {
            LegacyLookupCalls++;
            // Mirrors real persistence account isolation: only return a match scoped to accountId.
            return Task.FromResult(LegacyMatch is not null && LegacyMatch.AccountId == accountId ? LegacyMatch : null);
        }

        public Task<bool> PageTokenExistsAsync(string pageToken, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<bool> ReferenceCodeExistsAsync(Guid accountId, string referenceCode, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<BusinessRequestCommitResult> CommitBusinessRequestAsync(
            KeepCustomer customer, KeepRequest request, KeepRequestEvent requestEvent, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    private sealed class FakeOperatePersistence : IKeepRequestOperatePersistence
    {
        public Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<AccountUserSnapshot?>(new AccountUserSnapshot(
                UserId, AccountId, AccountUserRole.Owner, MembershipStatus.Active));

        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<AccountAccessSnapshot?>(new AccountAccessSnapshot(
                AccountId,
                AccountLifecycleState.Active,
                AccountPurpose.Business,
                AccountPlan.Starter,
                AccountCommercialState.Active,
                AccountOperatingMode.Standard,
                null,
                null));

        public Task<string?> GetActorDisplayNameAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<string?>("Owner User");

        public Task<KeepRequest?> GetVisibleRequestForUpdateAsync(Guid r, Guid a, Guid u, KeepRequestVisibilityScope s, CancellationToken ct) => throw new NotImplementedException();
        public Task<KeepResponsePolicy?> GetResponsePolicyAsync(Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> IsCustomerVisibleBusinessUpdateEventAsync(Guid r, Guid a, Guid e, CancellationToken ct) => throw new NotImplementedException();
        public Task<KeepRequestCommitResult> CommitAsync(KeepRequest r, KeepRequestEvent? e, CancellationToken ct) => throw new NotImplementedException();
        public Task<List<KeepRequestParticipant>> GetParticipantsForUpdateAsync(Guid r, Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task<ParticipantTargetInfo?> GetParticipantTargetAsync(Guid u, Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<ParticipantCandidateRecord>> GetParticipantCandidatesAsync(Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task<KeepRequestCommitResult> CommitParticipationAsync(KeepRequest r, IReadOnlyList<KeepRequestParticipant> n, KeepRequestEvent? e, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class FakeCurrentUser(Guid userId, Guid accountId) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid AccountId => accountId;
        public bool IsAuthenticated => true;
        public bool IsVerified => true;
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

    private sealed class FakeFeatureAccessPolicy : IFeatureAccessPolicy
    {
        public bool IsEnabled(AccountPlan plan, string key) => true;
        public int GetLimit(AccountPlan plan, string key) => 0;
        public int ResolveLimit(AccountEntitlements e, string key) => 0;
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }
}
