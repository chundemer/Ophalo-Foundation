using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Persistence;

/// <summary>
/// Proves <see cref="EfActualWorkReviewPersistence"/> — the atomic mark-reviewed/signal-resolve
/// transaction (Batch 6, build-log/129) — against real PostgreSQL. Mirrors
/// <see cref="ActualWorkSubmissionTests"/>'s fixture shape; the signal-resolve proofs are the
/// inverse of that class's raise/reopen proofs.
/// </summary>
[Collection("Postgres")]
public sealed class ActualWorkReviewPersistenceTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime Now = PostgresFixture.FixedNow;

    private readonly PostgresFixture _fixture;

    private Guid AccountId { get; set; }
    private Guid OwnerId { get; set; }
    private Guid OtherAccountId { get; set; }
    private Guid RequestId { get; set; }

    public ActualWorkReviewPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();

        (AccountId, OwnerId) = await SeedAccountAsync(ctx, "Test Business", "owner@actual-work-review.example.com");
        (OtherAccountId, _) = await SeedAccountAsync(ctx, "Other Business", "owner2@actual-work-review.example.com");
        RequestId = await SeedRequestAsync(ctx, AccountId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OpHaloDbContext CreateContext() => _fixture.CreateContext();

    [Fact]
    public async Task MarkReviewedAsync_on_the_only_submitted_visit_commits_and_resolves_the_signal()
    {
        var visitId = await SeedSubmittedVisitAsync();

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkReviewPersistence(ctx);

        var outcome = await persistence.MarkReviewedAsync(
            AccountId, visitId, await GetVersionAsync(visitId), OwnerId, "Looks good.", Now, CancellationToken.None);

        Assert.Equal(ActualWorkReviewResult.Committed, outcome.Result);
        Assert.NotNull(outcome.ConcurrencyVersion);

        await using var verifyCtx = CreateContext();
        var visit = await verifyCtx.Set<ActualWork>().SingleAsync(x => x.Id == visitId);
        Assert.Equal(ActualWorkStatus.Submitted, visit.Status);
        Assert.Equal(Now, visit.ReviewedAtUtc);
        Assert.Equal(OwnerId, visit.ReviewedByAccountUserId);
        Assert.Equal("Looks good.", visit.ReviewNote);

        var signal = await GetSignalAsync(verifyCtx);
        Assert.NotNull(signal);
        Assert.Equal(Now, signal!.ResolvedAtUtc);
    }

    [Fact]
    public async Task MarkReviewedAsync_with_another_unreviewed_submitted_visit_on_the_request_leaves_the_signal_active()
    {
        var firstVisitId = await SeedSubmittedVisitAsync();
        var secondVisitId = await SeedSubmittedVisitAsync();

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkReviewPersistence(ctx);

        var outcome = await persistence.MarkReviewedAsync(
            AccountId, firstVisitId, await GetVersionAsync(firstVisitId), OwnerId, null, Now, CancellationToken.None);

        Assert.Equal(ActualWorkReviewResult.Committed, outcome.Result);

        await using var verifyCtx = CreateContext();
        var signal = await GetSignalAsync(verifyCtx);
        Assert.NotNull(signal);
        Assert.Null(signal!.ResolvedAtUtc);

        var secondVisit = await verifyCtx.Set<ActualWork>().SingleAsync(x => x.Id == secondVisitId);
        Assert.Null(secondVisit.ReviewedAtUtc);
    }

    [Fact]
    public async Task MarkReviewedAsync_reviewing_the_last_remaining_unreviewed_visit_then_resolves_the_signal()
    {
        var firstVisitId = await SeedSubmittedVisitAsync();
        var secondVisitId = await SeedSubmittedVisitAsync();

        await using (var ctx = CreateContext())
        {
            var persistence = new EfActualWorkReviewPersistence(ctx);
            await persistence.MarkReviewedAsync(
                AccountId, firstVisitId, await GetVersionAsync(firstVisitId), OwnerId, null, Now, CancellationToken.None);
        }

        var later = Now.AddHours(1);
        await using (var ctx = CreateContext())
        {
            var persistence = new EfActualWorkReviewPersistence(ctx);
            var outcome = await persistence.MarkReviewedAsync(
                AccountId, secondVisitId, await GetVersionAsync(secondVisitId), OwnerId, null, later, CancellationToken.None);
            Assert.Equal(ActualWorkReviewResult.Committed, outcome.Result);
        }

        await using var verifyCtx = CreateContext();
        var signal = await GetSignalAsync(verifyCtx);
        Assert.NotNull(signal);
        Assert.Equal(later, signal!.ResolvedAtUtc);
    }

    [Fact]
    public async Task MarkReviewedAsync_for_an_unknown_visit_id_returns_NotFound()
    {
        await using var ctx = CreateContext();
        var persistence = new EfActualWorkReviewPersistence(ctx);

        var outcome = await persistence.MarkReviewedAsync(
            AccountId, Guid.NewGuid(), Guid.NewGuid(), OwnerId, null, Now, CancellationToken.None);

        Assert.Equal(ActualWorkReviewResult.NotFound, outcome.Result);
    }

    [Fact]
    public async Task MarkReviewedAsync_for_a_wrong_account_id_returns_NotFound()
    {
        var visitId = await SeedSubmittedVisitAsync();

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkReviewPersistence(ctx);

        var outcome = await persistence.MarkReviewedAsync(
            OtherAccountId, visitId, await GetVersionAsync(visitId), OwnerId, null, Now, CancellationToken.None);

        Assert.Equal(ActualWorkReviewResult.NotFound, outcome.Result);
    }

    [Fact]
    public async Task MarkReviewedAsync_with_a_stale_expected_version_returns_VersionMismatch()
    {
        var visitId = await SeedSubmittedVisitAsync();

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkReviewPersistence(ctx);

        var outcome = await persistence.MarkReviewedAsync(
            AccountId, visitId, Guid.NewGuid(), OwnerId, null, Now, CancellationToken.None);

        Assert.Equal(ActualWorkReviewResult.VersionMismatch, outcome.Result);

        await using var verifyCtx = CreateContext();
        var visit = await verifyCtx.Set<ActualWork>().SingleAsync(x => x.Id == visitId);
        Assert.Null(visit.ReviewedAtUtc);
    }

    [Fact]
    public async Task MarkReviewedAsync_for_a_draft_visit_returns_NotSubmitted()
    {
        await using var ctx = CreateContext();
        var visitId = await SeedDraftVisitAsync(ctx);
        var persistence = new EfActualWorkReviewPersistence(ctx);

        var outcome = await persistence.MarkReviewedAsync(
            AccountId, visitId, await GetVersionAsync(visitId), OwnerId, null, Now, CancellationToken.None);

        Assert.Equal(ActualWorkReviewResult.NotSubmitted, outcome.Result);
    }

    [Fact]
    public async Task MarkReviewedAsync_reviewed_twice_returns_AlreadyReviewed_and_does_not_overwrite_the_first_review()
    {
        var visitId = await SeedSubmittedVisitAsync();

        await using (var ctx = CreateContext())
        {
            var persistence = new EfActualWorkReviewPersistence(ctx);
            await persistence.MarkReviewedAsync(
                AccountId, visitId, await GetVersionAsync(visitId), OwnerId, "First review.", Now, CancellationToken.None);
        }

        await using var ctx2 = CreateContext();
        var persistence2 = new EfActualWorkReviewPersistence(ctx2);
        var outcome = await persistence2.MarkReviewedAsync(
            AccountId, visitId, await GetVersionAsync(visitId), OwnerId, "Second review.", Now.AddHours(1), CancellationToken.None);

        Assert.Equal(ActualWorkReviewResult.AlreadyReviewed, outcome.Result);

        await using var verifyCtx = CreateContext();
        var visit = await verifyCtx.Set<ActualWork>().SingleAsync(x => x.Id == visitId);
        Assert.Equal(Now, visit.ReviewedAtUtc);
        Assert.Equal("First review.", visit.ReviewNote);
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

    /// <summary>Creates a Draft with one line and submits it through
    /// <see cref="EfActualWorkSubmissionPersistence"/>, so the ADR-463 signal is raised exactly as
    /// it would be in production before a review ever runs.</summary>
    private async Task<Guid> SeedSubmittedVisitAsync()
    {
        await using var ctx = CreateContext();
        var persistence = new EfActualWorkPersistence(ctx);
        var visit = ActualWork.Create(AccountId, RequestId, OwnerId).Value;
        visit.AddLine(null, null, "Drain pan replacement", "each", 1m, null, null, null, null, OwnerId);
        await persistence.AddAsync(visit, CancellationToken.None);

        var submissionPersistence = new EfActualWorkSubmissionPersistence(ctx);
        var outcome = await submissionPersistence.SubmitAsync(
            AccountId, visit.Id, visit.ConcurrencyVersion, null, null, Now, CancellationToken.None);
        Assert.Equal(ActualWorkSubmissionResult.Committed, outcome.Result);

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
