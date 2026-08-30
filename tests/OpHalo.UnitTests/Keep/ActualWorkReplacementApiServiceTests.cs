using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// ADR-494 D6 replacement-copy orchestration (4e-ii-a). Verifies the successor aggregate is built
/// faithfully from the source, the Owner/Admin gate and no-open-Draft precondition fail closed
/// without touching the supersession seam, the correction reason reaches the seam unchanged, and the
/// seam's outcomes map to the stable Actual Work errors. The public route is deliberately not mapped
/// until 4e-ii-c, so this service-level unit test is the coverage vector.
/// </summary>
public sealed class ActualWorkReplacementApiServiceTests
{
    private static readonly Guid AccountId = Guid.CreateVersion7();
    private static readonly Guid RequestId = Guid.CreateVersion7();
    private static readonly Guid ActingUser = Guid.CreateVersion7();
    private static readonly Guid PerformerA = Guid.CreateVersion7();
    private static readonly Guid PerformerB = Guid.CreateVersion7();
    private static readonly Guid SourceId = Guid.CreateVersion7();
    private static readonly DateTime Now = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    // --- successor construction ---

    [Fact]
    public async Task Replacement_builds_a_Draft_successor_copying_every_line_field_and_the_visit_note()
    {
        var source = LinedSubmittedSource();
        var seam = new CapturingSupersession();
        var sut = CreateSut(seam, source, AccountUserRole.Owner);

        var result = await sut.CreateReplacementAsync(SourceId, source.ConcurrencyVersion, "Wrong panel size.", default);

        Assert.True(result.IsSuccess);
        var successor = Assert.IsType<ActualWork>(seam.CapturedSuccessor);
        Assert.Equal(ActualWorkStatus.Draft, successor.Status);
        Assert.Equal(AccountId, successor.AccountId);
        Assert.Equal(RequestId, successor.RequestId);
        Assert.Equal(ActingUser, successor.CreatedByUserId);
        Assert.Equal(ActingUser, successor.RecorderAccountUserId);
        Assert.Null(successor.ReviewedAtUtc);
        Assert.Null(successor.SubmittedAtUtc);
        Assert.Equal("Field context note.", successor.VisitNote);

        Assert.Collection(
            successor.Lines,
            line =>
            {
                var src = source.Lines.First();
                Assert.Equal(src.CatalogItemId, line.CatalogItemId);
                Assert.Equal(src.PriceBookVersionLineId, line.PriceBookVersionLineId);
                Assert.Equal("Drain Pan", line.DisplayNameSnapshot);
                Assert.Equal("each", line.UnitOfMeasureSnapshot);
                Assert.Equal(2m, line.ActualQuantity);
                Assert.Equal(42.50m, line.SellPriceSnapshot);
                Assert.Equal(18.00m, line.StandardExpectedDirectCostSnapshot);
                Assert.Equal("first note", line.Note);
                Assert.Equal(src.CommercialBaselineSourceLineId, line.CommercialBaselineSourceLineId);
                Assert.Equal(PerformerA, line.PerformedByAccountUserId);
            },
            line =>
            {
                Assert.Null(line.CatalogItemId);
                Assert.Null(line.PriceBookVersionLineId);
                Assert.Equal("3/4 copper elbow", line.DisplayNameSnapshot);
                Assert.Equal(3m, line.ActualQuantity);
                Assert.Null(line.SellPriceSnapshot);
                Assert.Equal(PerformerB, line.PerformedByAccountUserId);
            });
    }

    [Fact]
    public async Task Replacement_reaches_the_seam_with_the_source_id_version_actor_and_reason_unchanged()
    {
        var source = LinedSubmittedSource();
        var seam = new CapturingSupersession();
        var sut = CreateSut(seam, source, AccountUserRole.Admin);

        await sut.CreateReplacementAsync(SourceId, source.ConcurrencyVersion, "  Duplicate visit.  ", default);

        Assert.Equal(AccountId, seam.CapturedAccountId);
        Assert.Equal(SourceId, seam.CapturedSourceId);
        Assert.Equal(source.ConcurrencyVersion, seam.CapturedExpectedVersion);
        Assert.Equal(ActingUser, seam.CapturedByUser);
        Assert.Equal("  Duplicate visit.  ", seam.CapturedReason);
        Assert.Equal(Now, seam.CapturedNowUtc);
    }

    [Fact]
    public async Task Replacement_of_a_zero_line_source_copies_the_editable_outcome_and_completion_note()
    {
        var source = ZeroLineSubmittedSource();
        var seam = new CapturingSupersession();
        var sut = CreateSut(seam, source, AccountUserRole.Owner);

        var result = await sut.CreateReplacementAsync(SourceId, source.ConcurrencyVersion, "Re-do disposition.", default);

        Assert.True(result.IsSuccess);
        var successor = seam.CapturedSuccessor!;
        Assert.Empty(successor.Lines);
        Assert.Equal(ActualWorkOutcome.NoAccess, successor.Outcome);
        Assert.Equal("Customer not home.", successor.CompletionNote);
    }

    [Fact]
    public async Task Replacement_returns_the_committed_successor_id()
    {
        var source = LinedSubmittedSource();
        var seam = new CapturingSupersession();
        var sut = CreateSut(seam, source, AccountUserRole.Owner);

        var result = await sut.CreateReplacementAsync(SourceId, source.ConcurrencyVersion, "reason", default);

        Assert.True(result.IsSuccess);
        Assert.Equal(seam.CapturedSuccessor!.Id, result.Value);
    }

    // --- fail-closed paths (the seam must never be called) ---

    [Fact]
    public async Task Replacement_by_a_non_owner_admin_is_forbidden_and_never_calls_the_seam()
    {
        var source = LinedSubmittedSource();
        var seam = new CapturingSupersession();
        var sut = CreateSut(seam, source, AccountUserRole.Operator);

        var result = await sut.CreateReplacementAsync(SourceId, source.ConcurrencyVersion, "reason", default);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.forbidden", result.Error.Code);
        Assert.False(seam.WasCalled);
    }

    [Fact]
    public async Task Replacement_is_blocked_when_the_request_already_has_an_open_draft()
    {
        var source = LinedSubmittedSource();
        var seam = new CapturingSupersession();
        var sut = CreateSut(seam, source, AccountUserRole.Owner);
        sut.Persistence.OpenDraft = ActualWork.Create(AccountId, RequestId, ActingUser).Value;

        var result = await sut.CreateReplacementAsync(SourceId, source.ConcurrencyVersion, "reason", default);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.DraftAlreadyOpenForRequest, result.Error);
        Assert.False(seam.WasCalled);
    }

    [Fact]
    public async Task Replacement_of_an_unknown_source_returns_NotFound_and_never_calls_the_seam()
    {
        var seam = new CapturingSupersession();
        var sut = CreateSut(seam, source: null, AccountUserRole.Owner);

        var result = await sut.CreateReplacementAsync(SourceId, Guid.CreateVersion7(), "reason", default);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkErrors.NotFound, result.Error);
        Assert.False(seam.WasCalled);
    }

    // --- seam outcome mapping ---

    [Theory]
    [InlineData(ActualWorkSupersessionResult.VersionMismatch, "ActualWork.VersionMismatch")]
    [InlineData(ActualWorkSupersessionResult.SourceAlreadySuperseded, "ActualWork.AlreadySuperseded")]
    [InlineData(ActualWorkSupersessionResult.SourceNotSubmitted, "ActualWork.NotSubmitted")]
    [InlineData(ActualWorkSupersessionResult.DraftAlreadyOpenForRequest, "ActualWork.DraftAlreadyOpenForRequest")]
    public async Task Replacement_maps_seam_failure_outcomes_to_the_stable_error(
        ActualWorkSupersessionResult seamResult, string expectedCode)
    {
        var source = LinedSubmittedSource();
        var seam = new CapturingSupersession { Outcome = new ActualWorkSupersessionOutcome(seamResult) };
        var sut = CreateSut(seam, source, AccountUserRole.Owner);

        var result = await sut.CreateReplacementAsync(SourceId, source.ConcurrencyVersion, "reason", default);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    // --- source builders ---

    private static ActualWork LinedSubmittedSource()
    {
        var work = ActualWork.Create(AccountId, RequestId, Guid.CreateVersion7()).Value;
        work.AddLine(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "Drain Pan", "each", 2m,
            42.50m, 18.00m, "first note", Guid.CreateVersion7(), Guid.CreateVersion7(), PerformerA);
        work.AddLine(
            null, null, "3/4 copper elbow", null, 3m,
            null, null, null, null, Guid.CreateVersion7(), PerformerB);
        work.SetVisitNote("Field context note.");
        work.Submit(Now, outcome: null, completionNote: null);
        return work;
    }

    private static ActualWork ZeroLineSubmittedSource()
    {
        var work = ActualWork.Create(AccountId, RequestId, Guid.CreateVersion7()).Value;
        work.Submit(Now, ActualWorkOutcome.NoAccess, "Customer not home.");
        return work;
    }

    // --- SUT wiring ---

    private static Sut CreateSut(CapturingSupersession seam, ActualWork? source, AccountUserRole role)
    {
        var persistence = new FakeActualWorkPersistence { Source = source };
        var service = new ActualWorkReplacementApiService(
            seam,
            persistence,
            new FakeSnapshotPersistence(role),
            new FakeCurrentUser(ActingUser, AccountId),
            new FakeAccountAccessPolicy(),
            new FakeFeatureResolver(),
            new FakeUserAccessPolicy(),
            new FakeClock(Now));
        return new Sut(service, persistence);
    }

    private sealed record Sut(ActualWorkReplacementApiService Service, FakeActualWorkPersistence Persistence)
    {
        public Task<Result<Guid>> CreateReplacementAsync(
            Guid sourceId, Guid expectedVersion, string reason, CancellationToken ct) =>
            Service.CreateReplacementAsync(sourceId, expectedVersion, reason, ct);
    }

    // --- fakes ---

    private sealed class CapturingSupersession : IActualWorkSupersessionPersistence
    {
        public ActualWorkSupersessionOutcome? Outcome { get; set; }
        public bool WasCalled { get; private set; }
        public ActualWork? CapturedSuccessor { get; private set; }
        public Guid CapturedAccountId { get; private set; }
        public Guid CapturedSourceId { get; private set; }
        public Guid CapturedExpectedVersion { get; private set; }
        public Guid CapturedByUser { get; private set; }
        public string? CapturedReason { get; private set; }
        public DateTime CapturedNowUtc { get; private set; }

        public Task<ActualWorkSupersessionOutcome> SupersedeAsync(
            Guid accountId, Guid sourceActualWorkId, Guid expectedSourceVersion, ActualWork successor,
            Guid bySupersedingAccountUserId, string reason, DateTime nowUtc, CancellationToken ct)
        {
            WasCalled = true;
            CapturedAccountId = accountId;
            CapturedSourceId = sourceActualWorkId;
            CapturedExpectedVersion = expectedSourceVersion;
            CapturedSuccessor = successor;
            CapturedByUser = bySupersedingAccountUserId;
            CapturedReason = reason;
            CapturedNowUtc = nowUtc;
            return Task.FromResult(Outcome ?? new ActualWorkSupersessionOutcome(
                ActualWorkSupersessionResult.Committed, successor.ConcurrencyVersion, successor.Id));
        }
    }

    private sealed class FakeActualWorkPersistence : IActualWorkPersistence
    {
        public ActualWork? Source { get; set; }
        public ActualWork? OpenDraft { get; set; }

        public Task<ActualWork?> GetByIdAsync(Guid accountId, Guid actualWorkId, CancellationToken ct) =>
            Task.FromResult(Source);

        public Task<ActualWork?> GetOpenDraftForRequestAsync(Guid accountId, Guid requestId, CancellationToken ct) =>
            Task.FromResult(OpenDraft);

        public Task<IReadOnlyList<ActualWork>> GetSubmittedVisitsForRequestAsync(
            Guid accountId, Guid requestId, CancellationToken ct) => throw new NotImplementedException();

        public Task<ActualWorkCommitResult> AddAsync(ActualWork actualWork, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<ActualWorkCommitResult> CommitAsync(ActualWork actualWork, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<ActualWorkCommitResult> CommitAsync(
            ActualWork actualWork, ActualWorkDraftRecorderTransfer transferEvent, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<ActualWorkCommitResult> DiscardAsync(ActualWork actualWork, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    private sealed class FakeCurrentUser(Guid userId, Guid accountId) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid AccountId => accountId;
        public bool IsAuthenticated => true;
        public bool IsVerified => true;
    }

    private sealed class FakeSnapshotPersistence(AccountUserRole role) : IAccountAccessSnapshotPersistence
    {
        public Task<FoundationAccountAccessSnapshot?> GetAccountAccessSnapshotAsync(
            Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult<FoundationAccountAccessSnapshot?>(new FoundationAccountAccessSnapshot(
                accountId,
                AccountLifecycleState.Active,
                AccountPurpose.Business,
                AccountPlan.Professional,
                AccountCommercialState.Active,
                AccountOperatingMode.Standard,
                TrialEndsAtUtc: null,
                PastDueGraceEndsAtUtc: null));

        public Task<FoundationAccountUserRoleSnapshot?> GetAccountUserRoleSnapshotAsync(
            Guid accountId, Guid accountUserId, CancellationToken cancellationToken) =>
            Task.FromResult<FoundationAccountUserRoleSnapshot?>(
                new FoundationAccountUserRoleSnapshot(role, MembershipStatus.Active));
    }

    private sealed class FakeAccountAccessPolicy : IAccountAccessPolicy
    {
        public AccountAccessDecision Evaluate(AccountAccessContext context) =>
            new(AccountAccessPosture.FullAccess, AccountAccessReason.None, null);
    }

    private sealed class FakeFeatureResolver : IAccountFeatureAccessResolver
    {
        public Task<bool> IsEnabledAsync(
            Guid accountId, AccountFeatureAccessContext? context, string featureKey, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class FakeUserAccessPolicy : IUserAccessPolicy
    {
        public bool IsPermitted(AccountUserRole role, MembershipStatus status, AccountPurpose purpose, string key) => true;
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }
}
