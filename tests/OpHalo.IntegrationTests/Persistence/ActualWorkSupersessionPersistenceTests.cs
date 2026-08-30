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
/// Proves <see cref="EfActualWorkSupersessionPersistence"/> — the atomic supersede-source +
/// insert-replacement-Draft + resolve-if-clear transaction (ADR-494 D4/D6/D6b, 4e-i) — against real
/// PostgreSQL. The signal-stranding regression (D8) is the central case: a fully-superseded request
/// must clear <c>ActualWorkNeedsOfficeReview</c>.
/// </summary>
[Collection("Postgres")]
public sealed class ActualWorkSupersessionPersistenceTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime Now = PostgresFixture.FixedNow;

    private readonly PostgresFixture _fixture;

    private Guid AccountId { get; set; }
    private Guid OwnerId { get; set; }
    private Guid RequestId { get; set; }

    public ActualWorkSupersessionPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();

        (AccountId, OwnerId) = await SeedAccountAsync(ctx, "Test Business", "owner@actual-work-supersede.example.com");
        RequestId = await SeedRequestAsync(ctx, AccountId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OpHaloDbContext CreateContext() => _fixture.CreateContext();

    private EfActualWorkSupersessionPersistence CreateSut(OpHaloDbContext ctx) =>
        new(ctx, new EfActualWorkReviewSignalReconciliation(ctx));

    [Fact]
    public async Task SupersedeAsync_marks_the_source_inserts_the_successor_and_resolves_the_signal()
    {
        var sourceId = await SeedSubmittedVisitAsync();
        var successor = ActualWork.Create(AccountId, RequestId, OwnerId).Value;

        await using var ctx = CreateContext();
        var outcome = await CreateSut(ctx).SupersedeAsync(
            AccountId, sourceId, await GetVersionAsync(sourceId), successor, OwnerId, "Wrong panel size.", Now,
            CancellationToken.None);

        Assert.Equal(ActualWorkSupersessionResult.Committed, outcome.Result);
        Assert.Equal(successor.Id, outcome.SuccessorId);

        await using var verify = CreateContext();
        var source = await verify.Set<ActualWork>().SingleAsync(x => x.Id == sourceId);
        Assert.Equal(Now, source.SupersededAtUtc);
        Assert.Equal(successor.Id, source.SupersededByActualWorkId);
        Assert.Equal(OwnerId, source.SupersededByAccountUserId);
        Assert.Equal("Wrong panel size.", source.SupersessionReason);
        Assert.Equal(ActualWorkStatus.Submitted, source.Status);

        var persistedSuccessor = await verify.Set<ActualWork>().SingleAsync(x => x.Id == successor.Id);
        Assert.Equal(ActualWorkStatus.Draft, persistedSuccessor.Status);
        Assert.Equal(RequestId, persistedSuccessor.RequestId);

        var signal = await GetSignalAsync(verify);
        Assert.NotNull(signal);
        Assert.Equal(Now, signal!.ResolvedAtUtc);
    }

    [Fact]
    public async Task SupersedeAsync_leaves_the_signal_active_when_another_unreviewed_visit_remains()
    {
        var firstId = await SeedSubmittedVisitAsync();
        await SeedSubmittedVisitAsync();
        var successor = ActualWork.Create(AccountId, RequestId, OwnerId).Value;

        await using var ctx = CreateContext();
        var outcome = await CreateSut(ctx).SupersedeAsync(
            AccountId, firstId, await GetVersionAsync(firstId), successor, OwnerId, "Correcting the first visit.", Now,
            CancellationToken.None);

        Assert.Equal(ActualWorkSupersessionResult.Committed, outcome.Result);

        await using var verify = CreateContext();
        var signal = await GetSignalAsync(verify);
        Assert.NotNull(signal);
        Assert.Null(signal!.ResolvedAtUtc);
    }

    [Fact]
    public async Task SupersedeAsync_with_a_stale_source_version_returns_VersionMismatch()
    {
        var sourceId = await SeedSubmittedVisitAsync();
        var successor = ActualWork.Create(AccountId, RequestId, OwnerId).Value;

        await using var ctx = CreateContext();
        var outcome = await CreateSut(ctx).SupersedeAsync(
            AccountId, sourceId, Guid.NewGuid(), successor, OwnerId, "reason", Now, CancellationToken.None);

        Assert.Equal(ActualWorkSupersessionResult.VersionMismatch, outcome.Result);

        await using var verify = CreateContext();
        var source = await verify.Set<ActualWork>().SingleAsync(x => x.Id == sourceId);
        Assert.Null(source.SupersededAtUtc);
        Assert.False(await verify.Set<ActualWork>().AnyAsync(x => x.Id == successor.Id));
    }

    [Fact]
    public async Task SupersedeAsync_returns_DraftAlreadyOpenForRequest_when_an_open_draft_exists()
    {
        var sourceId = await SeedSubmittedVisitAsync();
        await using (var seedCtx = CreateContext())
            await SeedDraftVisitAsync(seedCtx);
        var successor = ActualWork.Create(AccountId, RequestId, OwnerId).Value;

        await using var ctx = CreateContext();
        var outcome = await CreateSut(ctx).SupersedeAsync(
            AccountId, sourceId, await GetVersionAsync(sourceId), successor, OwnerId, "reason", Now,
            CancellationToken.None);

        Assert.Equal(ActualWorkSupersessionResult.DraftAlreadyOpenForRequest, outcome.Result);

        await using var verify = CreateContext();
        var source = await verify.Set<ActualWork>().SingleAsync(x => x.Id == sourceId);
        Assert.Null(source.SupersededAtUtc);
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

    private async Task<Guid> SeedSubmittedVisitAsync()
    {
        await using var ctx = CreateContext();
        var persistence = new EfActualWorkPersistence(ctx);
        var visit = ActualWork.Create(AccountId, RequestId, OwnerId).Value;
        ActualWorkTestData.AddLine(visit, null, null, "Off-catalog part", "each", 1m, null, null, null, null, OwnerId);
        await persistence.AddAsync(visit, CancellationToken.None);

        var submission = new EfActualWorkSubmissionPersistence(ctx, new EfActualWorkReviewSignalReconciliation(ctx));
        var result = await submission.SubmitAsync(
            AccountId, visit.Id, visit.ConcurrencyVersion, null, null, Now, CancellationToken.None);
        Assert.Equal(ActualWorkSubmissionResult.Committed, result.Result);
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
