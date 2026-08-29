using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.IntegrationTests.Support;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Persistence;

/// <summary>
/// Proves <see cref="EfActualWorkSubmissionPersistence"/> — the atomic submit/signal-reconciliation
/// transaction (Batch 4, build-log/129) — against real PostgreSQL. Mirrors
/// <see cref="ProposedScopeSubmissionTests"/> exactly, with the additional zero-line/outcome
/// invariant proofs unique to <c>ActualWork.Submit</c>.
/// </summary>
[Collection("Postgres")]
public sealed class ActualWorkSubmissionTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime Now = PostgresFixture.FixedNow;

    private readonly PostgresFixture _fixture;

    private Guid AccountId { get; set; }
    private Guid OwnerId { get; set; }
    private Guid OtherAccountId { get; set; }
    private Guid RequestId { get; set; }

    public ActualWorkSubmissionTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();

        (AccountId, OwnerId) = await SeedAccountAsync(ctx, "Test Business", "owner@actual-work-submit.example.com");
        (OtherAccountId, _) = await SeedAccountAsync(ctx, "Other Business", "owner2@actual-work-submit.example.com");
        RequestId = await SeedRequestAsync(ctx, AccountId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OpHaloDbContext CreateContext() => _fixture.CreateContext();

    [Fact]
    public async Task SubmitAsync_transitions_the_visit_and_raises_the_signal()
    {
        var visitId = await SeedDraftVisitWithLineAsync();

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkSubmissionPersistence(ctx);

        var outcome = await persistence.SubmitAsync(
            AccountId, visitId, await GetVersionAsync(visitId), null, null, Now, CancellationToken.None);

        Assert.Equal(ActualWorkSubmissionResult.Committed, outcome.Result);
        Assert.NotNull(outcome.ConcurrencyVersion);

        await using var verifyCtx = CreateContext();
        var visit = await verifyCtx.Set<ActualWork>().SingleAsync(x => x.Id == visitId);
        Assert.Equal(ActualWorkStatus.Submitted, visit.Status);
        Assert.Equal(outcome.ConcurrencyVersion, visit.ConcurrencyVersion);

        var signal = await GetSignalAsync(verifyCtx);
        Assert.NotNull(signal);
        Assert.Equal(Now, signal!.RaisedAtUtc);
        Assert.Null(signal.ResolvedAtUtc);
    }

    [Fact]
    public async Task SubmitAsync_when_the_signal_is_already_active_does_not_reset_RaisedAtUtc()
    {
        var firstVisitId = await SeedDraftVisitWithLineAsync();
        await using (var ctx = CreateContext())
        {
            var persistence = new EfActualWorkSubmissionPersistence(ctx);
            await persistence.SubmitAsync(
                AccountId, firstVisitId, await GetVersionAsync(firstVisitId), null, null, Now, CancellationToken.None);
        }

        Guid versionAfterFirstSubmit;
        DateTime updatedAtAfterFirstSubmit;
        await using (var beforeCtx = CreateContext())
        {
            var before = await GetSignalAsync(beforeCtx);
            Assert.NotNull(before);
            versionAfterFirstSubmit = before!.ConcurrencyVersion;
            updatedAtAfterFirstSubmit = before.UpdatedAtUtc;
        }

        var secondVisitId = await SeedDraftVisitWithLineAsync();
        var later = Now.AddHours(2);
        await using (var ctx = CreateContext())
        {
            var persistence = new EfActualWorkSubmissionPersistence(ctx);
            var outcome = await persistence.SubmitAsync(
                AccountId, secondVisitId, await GetVersionAsync(secondVisitId), null, null, later, CancellationToken.None);
            Assert.Equal(ActualWorkSubmissionResult.Committed, outcome.Result);
        }

        await using var verifyCtx = CreateContext();
        var signal = await GetSignalAsync(verifyCtx);
        Assert.NotNull(signal);
        Assert.Equal(Now, signal!.RaisedAtUtc);
        Assert.Null(signal.ResolvedAtUtc);
        Assert.Equal(versionAfterFirstSubmit, signal.ConcurrencyVersion);
        Assert.Equal(updatedAtAfterFirstSubmit, signal.UpdatedAtUtc);
    }

    [Fact]
    public async Task SubmitAsync_reopens_a_resolved_signal()
    {
        var firstVisitId = await SeedDraftVisitWithLineAsync();
        await using (var ctx = CreateContext())
        {
            var persistence = new EfActualWorkSubmissionPersistence(ctx);
            await persistence.SubmitAsync(
                AccountId, firstVisitId, await GetVersionAsync(firstVisitId), null, null, Now, CancellationToken.None);
        }

        await using (var resolveCtx = CreateContext())
        {
            await resolveCtx.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE keep_request_work_signals SET resolved_at_utc = {Now} WHERE account_id = {AccountId}");
        }

        var secondVisitId = await SeedDraftVisitWithLineAsync();
        var reopenedAt = Now.AddDays(3);
        await using (var ctx = CreateContext())
        {
            var persistence = new EfActualWorkSubmissionPersistence(ctx);
            var outcome = await persistence.SubmitAsync(
                AccountId, secondVisitId, await GetVersionAsync(secondVisitId), null, null, reopenedAt, CancellationToken.None);
            Assert.Equal(ActualWorkSubmissionResult.Committed, outcome.Result);
        }

        await using var verifyCtx = CreateContext();
        var signal = await GetSignalAsync(verifyCtx);
        Assert.NotNull(signal);
        Assert.Equal(reopenedAt, signal!.RaisedAtUtc);
        Assert.Null(signal.ResolvedAtUtc);
    }

    [Fact]
    public async Task SubmitAsync_for_an_unknown_visit_id_returns_NotFound()
    {
        await using var ctx = CreateContext();
        var persistence = new EfActualWorkSubmissionPersistence(ctx);

        var outcome = await persistence.SubmitAsync(
            AccountId, Guid.NewGuid(), Guid.NewGuid(), null, null, Now, CancellationToken.None);

        Assert.Equal(ActualWorkSubmissionResult.NotFound, outcome.Result);
    }

    [Fact]
    public async Task SubmitAsync_for_a_wrong_account_id_returns_NotFound()
    {
        var visitId = await SeedDraftVisitWithLineAsync();

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkSubmissionPersistence(ctx);

        var outcome = await persistence.SubmitAsync(
            OtherAccountId, visitId, await GetVersionAsync(visitId), null, null, Now, CancellationToken.None);

        Assert.Equal(ActualWorkSubmissionResult.NotFound, outcome.Result);
    }

    [Fact]
    public async Task SubmitAsync_with_a_stale_expected_version_returns_VersionMismatch()
    {
        var visitId = await SeedDraftVisitWithLineAsync();

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkSubmissionPersistence(ctx);

        var outcome = await persistence.SubmitAsync(
            AccountId, visitId, Guid.NewGuid(), null, null, Now, CancellationToken.None);

        Assert.Equal(ActualWorkSubmissionResult.VersionMismatch, outcome.Result);

        await using var verifyCtx = CreateContext();
        var visit = await verifyCtx.Set<ActualWork>().SingleAsync(x => x.Id == visitId);
        Assert.Equal(ActualWorkStatus.Draft, visit.Status);
    }

    [Fact]
    public async Task SubmitAsync_for_a_terminal_request_returns_RequestTerminal_and_does_not_mutate_the_visit()
    {
        var visitId = await SeedDraftVisitWithLineAsync();

        await using (var closeCtx = CreateContext())
        {
            var request = await closeCtx.Set<KeepRequest>().SingleAsync(x => x.Id == RequestId);
            var closeResult = request.ChangeStatus(
                KeepRequestStatus.Cancelled, "Customer cancelled", OwnerId, "Test Owner", Now);
            Assert.True(closeResult.IsSuccess);
            await closeCtx.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkSubmissionPersistence(ctx);
        var outcome = await persistence.SubmitAsync(
            AccountId, visitId, await GetVersionAsync(visitId), null, null, Now, CancellationToken.None);

        Assert.Equal(ActualWorkSubmissionResult.RequestTerminal, outcome.Result);

        await using var verifyCtx = CreateContext();
        var visit = await verifyCtx.Set<ActualWork>().SingleAsync(x => x.Id == visitId);
        Assert.Equal(ActualWorkStatus.Draft, visit.Status);
        Assert.Null(await GetSignalAsync(verifyCtx));
    }

    [Fact]
    public async Task SubmitAsync_for_a_zero_line_visit_with_no_note_returns_ZeroLineCompletionNoteRequired()
    {
        await using var ctx = CreateContext();
        var visitId = await SeedDraftVisitAsync(ctx);
        var persistence = new EfActualWorkSubmissionPersistence(ctx);

        var outcome = await persistence.SubmitAsync(
            AccountId, visitId, await GetVersionAsync(visitId), ActualWorkOutcome.NoAccess, "  ", Now, CancellationToken.None);

        Assert.Equal(ActualWorkSubmissionResult.ZeroLineCompletionNoteRequired, outcome.Result);

        await using var verifyCtx = CreateContext();
        var visit = await verifyCtx.Set<ActualWork>().SingleAsync(x => x.Id == visitId);
        Assert.Equal(ActualWorkStatus.Draft, visit.Status);
        Assert.Null(await GetSignalAsync(verifyCtx));
    }

    [Fact]
    public async Task SubmitAsync_for_a_zero_line_visit_with_no_outcome_returns_ZeroLineOutcomeRequired()
    {
        await using var ctx = CreateContext();
        var visitId = await SeedDraftVisitAsync(ctx);
        var persistence = new EfActualWorkSubmissionPersistence(ctx);

        var outcome = await persistence.SubmitAsync(
            AccountId, visitId, await GetVersionAsync(visitId), null, "Nobody home", Now, CancellationToken.None);

        Assert.Equal(ActualWorkSubmissionResult.ZeroLineOutcomeRequired, outcome.Result);

        await using var verifyCtx = CreateContext();
        var visit = await verifyCtx.Set<ActualWork>().SingleAsync(x => x.Id == visitId);
        Assert.Equal(ActualWorkStatus.Draft, visit.Status);
        Assert.Null(await GetSignalAsync(verifyCtx));
    }

    [Fact]
    public async Task SubmitAsync_for_a_zero_line_visit_with_note_and_outcome_commits()
    {
        await using var ctx = CreateContext();
        var visitId = await SeedDraftVisitAsync(ctx);
        var persistence = new EfActualWorkSubmissionPersistence(ctx);

        var outcome = await persistence.SubmitAsync(
            AccountId, visitId, await GetVersionAsync(visitId),
            ActualWorkOutcome.DiagnosticOnly, "Diagnosed a failed capacitor.", Now, CancellationToken.None);

        Assert.Equal(ActualWorkSubmissionResult.Committed, outcome.Result);

        await using var verifyCtx = CreateContext();
        var visit = await verifyCtx.Set<ActualWork>().SingleAsync(x => x.Id == visitId);
        Assert.Equal(ActualWorkStatus.Submitted, visit.Status);
        Assert.Equal(ActualWorkOutcome.DiagnosticOnly, visit.Outcome);
    }

    [Fact]
    public async Task SubmitAsync_observes_a_terminal_transition_that_commits_while_it_waits_for_the_row_lock()
    {
        var visitId = await SeedDraftVisitWithLineAsync();
        var version = await GetVersionAsync(visitId);

        await using var closeCtx = CreateContext();
        await using var closeTx = await closeCtx.Database.BeginTransactionAsync();
        var request = await closeCtx.Set<KeepRequest>().SingleAsync(x => x.Id == RequestId);
        var closeResult = request.ChangeStatus(KeepRequestStatus.Cancelled, "Customer cancelled", OwnerId, "Test Owner", Now);
        Assert.True(closeResult.IsSuccess);
        await closeCtx.SaveChangesAsync();

        await using var submitCtx = CreateContext();
        await submitCtx.Database.CanConnectAsync();
        var submitTask = Task.Run(() => new EfActualWorkSubmissionPersistence(submitCtx)
            .SubmitAsync(AccountId, visitId, version, null, null, Now, CancellationToken.None));

        await Task.Delay(200);
        await closeTx.CommitAsync();

        var outcome = await submitTask;

        Assert.Equal(ActualWorkSubmissionResult.RequestTerminal, outcome.Result);

        await using var verifyCtx = CreateContext();
        var visit = await verifyCtx.Set<ActualWork>().SingleAsync(x => x.Id == visitId);
        Assert.Equal(ActualWorkStatus.Draft, visit.Status);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> SeedDraftVisitAsync(OpHaloDbContext ctx)
    {
        var persistence = new EfActualWorkPersistence(ctx);
        var visit = ActualWork.Create(AccountId, RequestId, OwnerId).Value;
        await persistence.AddAsync(visit, CancellationToken.None);
        return visit.Id;
    }

    /// <summary>A zero-line visit can only submit with a note+outcome; every ordinary
    /// successful-submit proof needs at least one line to keep those two concerns separate.</summary>
    private async Task<Guid> SeedDraftVisitWithLineAsync()
    {
        await using var ctx = CreateContext();
        var persistence = new EfActualWorkPersistence(ctx);
        var visit = ActualWork.Create(AccountId, RequestId, OwnerId).Value;
        ActualWorkTestData.AddLine(visit, null, null, "Drain pan replacement", "each", 1m, null, null, null, null, OwnerId);
        await persistence.AddAsync(visit, CancellationToken.None);
        return visit.Id;
    }

    private async Task<Guid> GetVersionAsync(Guid visitId)
    {
        await using var ctx = CreateContext();
        return await ctx.Set<ActualWork>().Where(x => x.Id == visitId).Select(x => x.ConcurrencyVersion).SingleAsync();
    }

    private async Task<KeepRequestWorkSignal?> GetSignalAsync(OpHaloDbContext ctx) =>
        await ctx.Set<KeepRequestWorkSignal>()
            .FirstOrDefaultAsync(x =>
                x.AccountId == AccountId &&
                x.KeepRequestId == RequestId &&
                x.SourceModuleKey == KeepRequestWorkSignalKeys.Modules.PriceBookQuotesMaterials &&
                x.SignalKey == KeepRequestWorkSignalKeys.Signals.ActualWorkNeedsOfficeReview);

    private static async Task<Guid> SeedRequestAsync(OpHaloDbContext ctx, Guid accountId)
    {
        var customer = KeepCustomer.Create(accountId, "Jane Customer", "+15555550100");
        ctx.Set<KeepCustomer>().Add(customer);

        var request = KeepRequest.CreateByBusiness(
            accountId, customer.Id, "Jane Customer", "+15555550100", null, "Leaky faucet",
            $"R{Guid.NewGuid():N}"[..20], $"tok_{Guid.NewGuid():N}", Now, KeepRequestSource.Phone);
        ctx.Set<KeepRequest>().Add(request);

        await ctx.SaveChangesAsync();
        return request.Id;
    }

    private static async Task<(Guid AccountId, Guid OwnerAccountUserId)> SeedAccountAsync(
        OpHaloDbContext ctx, string businessName, string email)
    {
        var result = new AccountProvisioningService().CreateVerified(
            email: email,
            name: "Test Owner",
            businessName: businessName,
            purpose: AccountPurpose.Business,
            timeZone: "UTC",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: Now,
            trialEndsAtUtc: Now.AddDays(30));

        if (!result.IsSuccess)
            throw new InvalidOperationException($"Failed to provision account: {result.Error}");

        var graph = result.Value;
        ctx.Users.Add(graph.User);
        ctx.Accounts.Add(graph.Account);
        ctx.AccountUsers.Add(graph.Owner);

        var ownerIdEntry = ctx.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerIdEntry.CurrentValue = null;
        await ctx.SaveChangesAsync();

        ctx.AccountEntitlements.Add(graph.Entitlements);
        await ctx.SaveChangesAsync();

        ownerIdEntry.CurrentValue = graph.Owner.Id;
        await ctx.SaveChangesAsync();

        return (graph.Account.Id, graph.Owner.Id);
    }
}
