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
    private int _priceBookVersionNumber;

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
        var persistence = new EfActualWorkReviewPersistence(ctx, new EfActualWorkFinancialResolutionPersistence(ctx), new EfActualWorkReviewSignalReconciliation(ctx));

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
        var persistence = new EfActualWorkReviewPersistence(ctx, new EfActualWorkFinancialResolutionPersistence(ctx), new EfActualWorkReviewSignalReconciliation(ctx));

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
            var persistence = new EfActualWorkReviewPersistence(ctx, new EfActualWorkFinancialResolutionPersistence(ctx), new EfActualWorkReviewSignalReconciliation(ctx));
            await persistence.MarkReviewedAsync(
                AccountId, firstVisitId, await GetVersionAsync(firstVisitId), OwnerId, null, Now, CancellationToken.None);
        }

        var later = Now.AddHours(1);
        await using (var ctx = CreateContext())
        {
            var persistence = new EfActualWorkReviewPersistence(ctx, new EfActualWorkFinancialResolutionPersistence(ctx), new EfActualWorkReviewSignalReconciliation(ctx));
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
        var persistence = new EfActualWorkReviewPersistence(ctx, new EfActualWorkFinancialResolutionPersistence(ctx), new EfActualWorkReviewSignalReconciliation(ctx));

        var outcome = await persistence.MarkReviewedAsync(
            AccountId, Guid.NewGuid(), Guid.NewGuid(), OwnerId, null, Now, CancellationToken.None);

        Assert.Equal(ActualWorkReviewResult.NotFound, outcome.Result);
    }

    [Fact]
    public async Task MarkReviewedAsync_for_a_wrong_account_id_returns_NotFound()
    {
        var visitId = await SeedSubmittedVisitAsync();

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkReviewPersistence(ctx, new EfActualWorkFinancialResolutionPersistence(ctx), new EfActualWorkReviewSignalReconciliation(ctx));

        var outcome = await persistence.MarkReviewedAsync(
            OtherAccountId, visitId, await GetVersionAsync(visitId), OwnerId, null, Now, CancellationToken.None);

        Assert.Equal(ActualWorkReviewResult.NotFound, outcome.Result);
    }

    [Fact]
    public async Task MarkReviewedAsync_with_a_stale_expected_version_returns_VersionMismatch()
    {
        var visitId = await SeedSubmittedVisitAsync();

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkReviewPersistence(ctx, new EfActualWorkFinancialResolutionPersistence(ctx), new EfActualWorkReviewSignalReconciliation(ctx));

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
        var persistence = new EfActualWorkReviewPersistence(ctx, new EfActualWorkFinancialResolutionPersistence(ctx), new EfActualWorkReviewSignalReconciliation(ctx));

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
            var persistence = new EfActualWorkReviewPersistence(ctx, new EfActualWorkFinancialResolutionPersistence(ctx), new EfActualWorkReviewSignalReconciliation(ctx));
            await persistence.MarkReviewedAsync(
                AccountId, visitId, await GetVersionAsync(visitId), OwnerId, "First review.", Now, CancellationToken.None);
        }

        await using var ctx2 = CreateContext();
        var persistence2 = new EfActualWorkReviewPersistence(ctx2, new EfActualWorkFinancialResolutionPersistence(ctx2), new EfActualWorkReviewSignalReconciliation(ctx2));
        var outcome = await persistence2.MarkReviewedAsync(
            AccountId, visitId, await GetVersionAsync(visitId), OwnerId, "Second review.", Now.AddHours(1), CancellationToken.None);

        Assert.Equal(ActualWorkReviewResult.AlreadyReviewed, outcome.Result);

        await using var verifyCtx = CreateContext();
        var visit = await verifyCtx.Set<ActualWork>().SingleAsync(x => x.Id == visitId);
        Assert.Equal(Now, visit.ReviewedAtUtc);
        Assert.Equal("First review.", visit.ReviewNote);
    }

    // --- BL135 §4 Batch 3b-ii: hard billing-readiness review gate ---

    [Fact]
    public async Task MarkReviewedAsync_is_blocked_and_does_not_advance_review_state_when_a_line_is_financially_incomplete()
    {
        var visitId = await SeedSubmittedVisitWithIncompleteLineAsync();

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkReviewPersistence(ctx, new EfActualWorkFinancialResolutionPersistence(ctx), new EfActualWorkReviewSignalReconciliation(ctx));

        var outcome = await persistence.MarkReviewedAsync(
            AccountId, visitId, await GetVersionAsync(visitId), OwnerId, null, Now, CancellationToken.None);

        Assert.Equal(ActualWorkReviewResult.BlockedIncompleteFinancials, outcome.Result);

        await using var verifyCtx = CreateContext();
        var visit = await verifyCtx.Set<ActualWork>().SingleAsync(x => x.Id == visitId);
        Assert.Null(visit.ReviewedAtUtc);
        Assert.Null(visit.ReviewedByAccountUserId);

        var signal = await GetSignalAsync(verifyCtx);
        Assert.NotNull(signal);
        Assert.Null(signal!.ResolvedAtUtc);
    }

    [Fact]
    public async Task MarkReviewedAsync_is_blocked_on_a_zero_line_visit_without_a_no_charge_disposition()
    {
        var visitId = await SeedSubmittedZeroLineVisitAsync();

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkReviewPersistence(ctx, new EfActualWorkFinancialResolutionPersistence(ctx), new EfActualWorkReviewSignalReconciliation(ctx));

        var outcome = await persistence.MarkReviewedAsync(
            AccountId, visitId, await GetVersionAsync(visitId), OwnerId, null, Now, CancellationToken.None);

        Assert.Equal(ActualWorkReviewResult.BlockedZeroLineDisposition, outcome.Result);

        await using var verifyCtx = CreateContext();
        var visit = await verifyCtx.Set<ActualWork>().SingleAsync(x => x.Id == visitId);
        Assert.Null(visit.ReviewedAtUtc);
    }

    [Fact]
    public async Task MarkReviewedAsync_commits_once_the_incomplete_line_is_resolved()
    {
        var visitId = await SeedSubmittedVisitWithIncompleteLineAsync();
        var lineId = await GetFirstLineIdAsync(visitId);
        await InsertResolutionRawAsync(visitId, lineId, sell: 90m, cost: 30m);

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkReviewPersistence(ctx, new EfActualWorkFinancialResolutionPersistence(ctx), new EfActualWorkReviewSignalReconciliation(ctx));

        var outcome = await persistence.MarkReviewedAsync(
            AccountId, visitId, await GetVersionAsync(visitId), OwnerId, null, Now, CancellationToken.None);

        Assert.Equal(ActualWorkReviewResult.Committed, outcome.Result);
    }

    [Fact]
    public async Task MarkReviewedAsync_commits_with_mixed_snapshot_and_resolution_provenance()
    {
        // Line carries a captured sell-price snapshot but no direct-cost snapshot; a resolution
        // supplies only the missing direct cost. The Batch 3b-ii gate must agree with the read
        // projection that this line is now complete.
        var (catalogItemId, priceBookVersionLineId) = await SeedCatalogItemWithSnapshotAsync();
        var visitId = await SeedSubmittedVisitAsync(v => ActualWorkTestData.AddLine(
            v, catalogItemId, priceBookVersionLineId, "Partial-snapshot line", "each", 1m, 42.50m, null, null, null, OwnerId));
        var lineId = await GetFirstLineIdAsync(visitId);
        await InsertResolutionRawAsync(visitId, lineId, sell: null, cost: 45m);

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkReviewPersistence(ctx, new EfActualWorkFinancialResolutionPersistence(ctx), new EfActualWorkReviewSignalReconciliation(ctx));

        var outcome = await persistence.MarkReviewedAsync(
            AccountId, visitId, await GetVersionAsync(visitId), OwnerId, null, Now, CancellationToken.None);

        Assert.Equal(ActualWorkReviewResult.Committed, outcome.Result);
    }

    [Fact]
    public async Task MarkReviewedAsync_commits_on_a_zero_line_visit_once_a_no_charge_disposition_exists()
    {
        var visitId = await SeedSubmittedZeroLineVisitAsync();
        await InsertNoChargeDispositionRawAsync(visitId);

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkReviewPersistence(ctx, new EfActualWorkFinancialResolutionPersistence(ctx), new EfActualWorkReviewSignalReconciliation(ctx));

        var outcome = await persistence.MarkReviewedAsync(
            AccountId, visitId, await GetVersionAsync(visitId), OwnerId, null, Now, CancellationToken.None);

        Assert.Equal(ActualWorkReviewResult.Committed, outcome.Result);

        await using var verifyCtx = CreateContext();
        var signal = await GetSignalAsync(verifyCtx);
        Assert.NotNull(signal);
        Assert.Equal(Now, signal!.ResolvedAtUtc);
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

    /// <summary>Creates a Draft with one financially-complete catalog-backed line (both snapshots
    /// captured) and submits it through <see cref="EfActualWorkSubmissionPersistence"/>, so the
    /// ADR-463 signal is raised exactly as it would be in production before a review ever runs.
    /// Financially complete so it clears the BL135 §4 Batch 3b-ii review gate.</summary>
    private async Task<Guid> SeedSubmittedVisitAsync()
    {
        var (catalogItemId, priceBookVersionLineId) = await SeedCatalogItemWithSnapshotAsync();
        return await SeedSubmittedVisitAsync(v => ActualWorkTestData.AddLine(
            v, catalogItemId, priceBookVersionLineId, "Drain pan replacement", "each", 1m, 42.50m, 18.00m, null, null, OwnerId));
    }

    private async Task<(Guid CatalogItemId, Guid PriceBookVersionLineId)> SeedCatalogItemWithSnapshotAsync()
    {
        var catalogItemId = Guid.NewGuid();
        await using var ctx = CreateContext();
        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_catalog_items (
                id, account_id, type, display_name, external_key, normalized_external_key,
                category_id, unit_of_measure, currency, is_common_item, active_state,
                current_price_book_version_line_id, source_actual_work_line_id, concurrency_version,
                created_at_utc, updated_at_utc)
            VALUES (
                {catalogItemId}, {AccountId}, 'Material', {"Test Item " + catalogItemId}, NULL, NULL,
                NULL, 'each', 'USD', false, 'Active', NULL, NULL, {Guid.NewGuid()}, {Now}, {Now})
            """);

        var version = PriceBookVersion.CreatePublished(
            AccountId, Interlocked.Increment(ref _priceBookVersionNumber), OwnerId, Now, catalogItemId, "Test Item", CatalogItemType.Material,
            "each", "USD", 18.00m, 42.50m, PriceBookLinePricingMode.StandalonePrice).Value;
        ctx.Set<PriceBookVersion>().Add(version);
        await ctx.SaveChangesAsync();
        return (catalogItemId, version.Lines.Single().Id);
    }

    private async Task<Guid> SeedSubmittedVisitAsync(Action<ActualWork> addLines, ActualWorkOutcome? outcome = null, string? completionNote = null)
    {
        await using var ctx = CreateContext();
        var persistence = new EfActualWorkPersistence(ctx);
        var visit = ActualWork.Create(AccountId, RequestId, OwnerId).Value;
        addLines(visit);
        await persistence.AddAsync(visit, CancellationToken.None);

        var submissionPersistence = new EfActualWorkSubmissionPersistence(ctx, new EfActualWorkReviewSignalReconciliation(ctx));
        var outcomeResult = await submissionPersistence.SubmitAsync(
            AccountId, visit.Id, visit.ConcurrencyVersion, outcome, completionNote, Now, CancellationToken.None);
        Assert.Equal(ActualWorkSubmissionResult.Committed, outcomeResult.Result);

        return visit.Id;
    }

    /// <summary>Submitted visit with one custom line carrying neither snapshot — financially
    /// incomplete, so it is blocked by the Batch 3b-ii review gate until resolved.</summary>
    private Task<Guid> SeedSubmittedVisitWithIncompleteLineAsync() =>
        SeedSubmittedVisitAsync(v => ActualWorkTestData.AddLine(v, null, null, "Off-catalog part", "each", 1m, null, null, null, null, OwnerId));

    /// <summary>Submitted zero-line diagnostic visit — financially vacuously complete, but blocked
    /// by the Batch 3b-ii review gate until a no-charge disposition exists.</summary>
    private Task<Guid> SeedSubmittedZeroLineVisitAsync() =>
        SeedSubmittedVisitAsync(_ => { }, ActualWorkOutcome.DiagnosticOnly, "Diagnostic visit, no work performed.");

    private async Task<Guid> GetFirstLineIdAsync(Guid visitId)
    {
        await using var ctx = CreateContext();
        return await ctx.Set<ActualWork>().Where(x => x.Id == visitId)
            .SelectMany(x => x.Lines).Select(l => l.Id).FirstAsync();
    }

    private async Task InsertResolutionRawAsync(Guid visitId, Guid lineId, decimal? sell, decimal? cost)
    {
        await using var ctx = CreateContext();
        await ctx.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO keep_actual_work_line_financial_resolutions
              (id, created_at_utc, updated_at_utc, account_id, actual_work_id, actual_work_line_id,
               resolved_unit_sell_price, resolved_unit_standard_expected_direct_cost, basis, reason,
               resolved_by_account_user_id, resolved_at_utc)
            VALUES
              ({Guid.NewGuid()}, {Now}, {Now}, {AccountId}, {visitId}, {lineId},
               {sell}, {cost}, {nameof(FinancialResolutionBasis.SupplierReceipt)},
               {"office resolution"}, {OwnerId}, {Now})");
    }

    private async Task InsertNoChargeDispositionRawAsync(Guid visitId)
    {
        await using var ctx = CreateContext();
        await ctx.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO keep_actual_work_office_financial_dispositions
              (id, created_at_utc, updated_at_utc, account_id, actual_work_id, kind, reason,
               disposed_by_account_user_id, disposed_at_utc)
            VALUES
              ({Guid.NewGuid()}, {Now}, {Now}, {AccountId}, {visitId},
               {nameof(OfficeFinancialDispositionKind.NoCharge)}, {"customer goodwill"}, {OwnerId}, {Now})");
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
