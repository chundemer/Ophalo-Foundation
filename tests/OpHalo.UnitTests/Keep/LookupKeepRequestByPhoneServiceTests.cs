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

    private static KeepRequest LegacyRequest(Guid accountId, Guid customerId, string customerPhone, string name, string? email) =>
        KeepRequest.CreateByBusiness(
            accountId,
            customerId,
            name,
            customerPhone,
            email,
            "Fix the boiler",
            "REF-1",
            "token-1",
            Now.AddDays(-30),
            KeepRequestSource.Phone);

    [Fact]
    public async Task Falls_back_to_possible_match_when_no_customer_row_matches_canonical_phone()
    {
        var candidateId = Guid.NewGuid();
        var candidate = KeepCustomer.Create(AccountId, "Legacy Larry", "555-555-0199", "larry@example.com");
        var business = new FakeBusinessPersistence
        {
            LegacyMatch = LegacyRequest(AccountId, candidateId, "(555) 555-0100", "Legacy Larry", "larry@example.com"),
            CandidateCustomer = candidate,
        };
        var sut = BuildSut(business);

        var result = await sut.ExecuteAsync("5555550100");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Customer);
        Assert.Empty(result.Value.ActiveRequests);
        Assert.NotNull(result.Value.PossibleCustomer);
        Assert.Equal(candidate.Id, result.Value.PossibleCustomer!.CandidateCustomerId);
        Assert.Equal("Legacy Larry", result.Value.PossibleCustomer.Name);
        Assert.Equal("larry@example.com", result.Value.PossibleCustomer.Email);
    }

    [Fact]
    public async Task Returns_no_possible_customer_when_neither_customer_nor_legacy_request_match()
    {
        var business = new FakeBusinessPersistence { LegacyMatch = null };
        var sut = BuildSut(business);

        var result = await sut.ExecuteAsync("5555550199");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Customer);
        Assert.Null(result.Value.PossibleCustomer);
    }

    [Fact]
    public async Task Does_not_query_legacy_fallback_when_a_customer_row_already_matches()
    {
        var customer = KeepCustomer.Create(AccountId, "Sarah Mitchell", "5555550100", "sarah@example.com");
        var business = new FakeBusinessPersistence
        {
            ExistingCustomer = customer,
            // If the service queried the fallback despite a customer match, this would surface as
            // a possible-customer result and fail the assertion below.
            LegacyMatch = LegacyRequest(AccountId, Guid.NewGuid(), "5555550100", "Should Not Appear", null),
        };
        var sut = BuildSut(business);

        var result = await sut.ExecuteAsync("5555550100");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Customer);
        Assert.Null(result.Value.PossibleCustomer);
        Assert.Equal(0, business.LegacyLookupCalls);
    }

    [Fact]
    public async Task Legacy_fallback_is_scoped_to_the_caller_account()
    {
        var business = new FakeBusinessPersistence
        {
            LegacyMatch = LegacyRequest(OtherAccountId, Guid.NewGuid(), "5555550100", "Cross Account Cathy", null),
        };
        var sut = BuildSut(business);

        var result = await sut.ExecuteAsync("5555550100");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.PossibleCustomer);
    }

    [Fact]
    public async Task Possible_customer_active_requests_are_scoped_to_the_candidate_customer_id_not_raw_phone()
    {
        var candidate = KeepCustomer.Create(AccountId, "Legacy Larry", "555-555-0199", null);
        var activeRequest = LegacyRequest(AccountId, candidate.Id, "555-555-0199", "Legacy Larry", null);
        var business = new FakeBusinessPersistence
        {
            LegacyMatch = LegacyRequest(AccountId, candidate.Id, "(555) 555-0100", "Legacy Larry", null),
            CandidateCustomer = candidate,
            ActiveRequestsByCandidateId = new List<KeepRequest> { activeRequest },
        };
        var sut = BuildSut(business);

        var result = await sut.ExecuteAsync("5555550100");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.PossibleCustomer);
        Assert.Single(result.Value.PossibleCustomer!.ActiveRequests);
        Assert.Equal(candidate.Id, business.LastActiveRequestsCustomerId);
    }

    [Fact]
    public async Task Returns_no_possible_customer_when_candidate_customer_row_is_missing()
    {
        var business = new FakeBusinessPersistence
        {
            LegacyMatch = LegacyRequest(AccountId, Guid.NewGuid(), "5555550100", "Legacy Larry", null),
            CandidateCustomer = null,
        };
        var sut = BuildSut(business);

        var result = await sut.ExecuteAsync("5555550100");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.PossibleCustomer);
    }

    // ---------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------

    private sealed class FakeBusinessPersistence : IKeepBusinessRequestPersistence
    {
        public KeepCustomer? ExistingCustomer   { get; set; }
        public KeepRequest? LegacyMatch         { get; set; }
        public KeepCustomer? CandidateCustomer  { get; set; }
        public List<KeepRequest> ActiveRequestsByCandidateId { get; set; } = new();
        public Guid? LastActiveRequestsCustomerId { get; private set; }
        public int LegacyLookupCalls            { get; private set; }

        public Task<KeepCustomer?> FindCustomerByCanonicalPhoneAsync(Guid accountId, string canonicalPhone, CancellationToken ct) =>
            Task.FromResult(ExistingCustomer);

        public Task<KeepCustomer?> FindCustomerByIdAsync(Guid accountId, Guid customerId, CancellationToken ct) =>
            Task.FromResult(CandidateCustomer is not null && CandidateCustomer.AccountId == accountId ? CandidateCustomer : null);

        public Task<IReadOnlyList<KeepRequest>> FindActiveRequestsByCustomerIdAsync(
            Guid accountId, Guid customerId, int take, CancellationToken ct)
        {
            LastActiveRequestsCustomerId = customerId;
            return Task.FromResult<IReadOnlyList<KeepRequest>>(ActiveRequestsByCandidateId);
        }

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
