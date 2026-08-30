using Microsoft.EntityFrameworkCore;
using Npgsql;
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
/// Proves <see cref="EfActualWorkFinancialResolutionPersistence"/> and the
/// <c>keep_actual_work_line_financial_resolutions</c> /
/// <c>keep_actual_work_office_financial_dispositions</c> mappings (build-log/135 §4 Batch 2)
/// against real PostgreSQL: every check constraint, the three-column line FK (drift D2),
/// component-by-component effective resolution (§5 proof 2), and the append seam's transaction
/// boundary — every persistence assertion reads through a fresh <see cref="OpHaloDbContext"/> so
/// it proves committed data, not a writer context's change tracker.
/// </summary>
[Collection("Postgres")]
public sealed class ActualWorkFinancialResolutionPersistenceTests
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime Now = PostgresFixture.FixedNow;

    private readonly PostgresFixture _fixture;

    private Guid AccountId { get; set; }
    private Guid OwnerId { get; set; }
    private Guid OtherAccountId { get; set; }
    private Guid RequestId { get; set; }

    public ActualWorkFinancialResolutionPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();

        (AccountId, OwnerId) = await SeedAccountAsync(ctx, "Test Business", "owner@awfr.example.com");
        (OtherAccountId, _) = await SeedAccountAsync(ctx, "Other Business", "owner2@awfr.example.com");
        RequestId = await SeedRequestAsync(ctx, AccountId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OpHaloDbContext CreateContext() => _fixture.CreateContext();

    // -------------------------------------------------------------------------
    // Check constraints
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Resolution_with_no_resolved_value_violates_value_present()
    {
        var (visitId, lineIds) = await SeedSubmittedVisitAsync(1);

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertResolutionRawAsync(AccountId, visitId, lineIds[0], sell: null, cost: null, "Other", "receipt"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Contains("value_present", ex.ConstraintName);
    }

    [Fact]
    public async Task Resolution_with_a_negative_value_violates_non_negative()
    {
        var (visitId, lineIds) = await SeedSubmittedVisitAsync(1);

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertResolutionRawAsync(AccountId, visitId, lineIds[0], sell: -1m, cost: null, "Other", "receipt"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Contains("non_negative", ex.ConstraintName);
    }

    [Fact]
    public async Task Resolution_with_a_blank_reason_violates_reason_present()
    {
        var (visitId, lineIds) = await SeedSubmittedVisitAsync(1);

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertResolutionRawAsync(AccountId, visitId, lineIds[0], sell: 100m, cost: null, "Other", "   "));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Contains("reason_present", ex.ConstraintName);
    }

    [Fact]
    public async Task Disposition_with_a_blank_reason_violates_reason_present()
    {
        var (visitId, _) = await SeedSubmittedVisitAsync(0);

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertDispositionRawAsync(AccountId, visitId, "NoCharge", "  "));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Contains("reason_present", ex.ConstraintName);
    }

    // -------------------------------------------------------------------------
    // Three-column line FK (drift D2)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Resolution_naming_a_same_account_line_from_a_different_visit_violates_the_line_FK()
    {
        var (_, firstVisitLineIds) = await SeedSubmittedVisitAsync(1);
        var (secondVisitId, _) = await SeedSubmittedVisitAsync(1);

        // secondVisitId is a real visit; firstVisitLineIds[0] is a real, same-account line — but it
        // belongs to the first visit, so (account_id, second_visit_id, first_line_id) has no match
        // in keep_actual_work_lines(account_id, actual_work_id, id).
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertResolutionRawAsync(
                AccountId, secondVisitId, firstVisitLineIds[0], sell: 100m, cost: null, "Other", "receipt"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
    }

    // -------------------------------------------------------------------------
    // Component-by-component effective resolution (§5 proof 2)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Two_sell_price_rows_on_one_line_keep_both_and_the_newer_is_effective()
    {
        var (visitId, lineIds) = await SeedSubmittedVisitAsync(1);
        var lineId = lineIds[0];

        var older = await AppendResolutionAsync(visitId, lineId, sell: 100m, cost: null, Now);
        var newer = await AppendResolutionAsync(visitId, lineId, sell: 120m, cost: null, Now.AddHours(1));

        await using var readerCtx = CreateContext();
        var reader = new EfActualWorkFinancialResolutionPersistence(readerCtx);
        var rows = await reader.GetResolutionsForVisitAsync(AccountId, visitId, CancellationToken.None);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { newer, older }, rows.Select(r => r.Id).ToArray());

        var effectiveSell = rows.First(r => r.ResolvedUnitSellPrice is not null);
        Assert.Equal(120m, effectiveSell.ResolvedUnitSellPrice);
        Assert.Equal(newer, effectiveSell.Id);
    }

    [Fact]
    public async Task A_newer_cost_only_row_does_not_erase_an_earlier_resolved_sell_price()
    {
        var (visitId, lineIds) = await SeedSubmittedVisitAsync(1);
        var lineId = lineIds[0];

        // Row A (older): sell price only. Row B (newer): direct cost only.
        var rowA = await AppendResolutionAsync(visitId, lineId, sell: 150m, cost: null, Now);
        var rowB = await AppendResolutionAsync(visitId, lineId, sell: null, cost: 60m, Now.AddHours(1));

        await using var readerCtx = CreateContext();
        var reader = new EfActualWorkFinancialResolutionPersistence(readerCtx);
        var rows = await reader.GetResolutionsForVisitAsync(AccountId, visitId, CancellationToken.None);

        var effectiveSell = rows.First(r => r.ResolvedUnitSellPrice is not null);
        var effectiveCost = rows.First(r => r.ResolvedUnitStandardExpectedDirectCost is not null);

        // Effective state: A's sell price AND B's direct cost, each with its own provenance row.
        Assert.Equal(150m, effectiveSell.ResolvedUnitSellPrice);
        Assert.Equal(rowA, effectiveSell.Id);
        Assert.Equal(FinancialResolutionBasis.SupplierReceipt, effectiveSell.Basis);

        Assert.Equal(60m, effectiveCost.ResolvedUnitStandardExpectedDirectCost);
        Assert.Equal(rowB, effectiveCost.Id);
    }

    [Fact]
    public async Task A_component_with_no_supplying_row_stays_unresolved()
    {
        var (visitId, lineIds) = await SeedSubmittedVisitAsync(1);

        // Only a sell price is ever resolved for this line.
        await AppendResolutionAsync(visitId, lineIds[0], sell: 90m, cost: null, Now);

        await using var readerCtx = CreateContext();
        var reader = new EfActualWorkFinancialResolutionPersistence(readerCtx);
        var rows = await reader.GetResolutionsForVisitAsync(AccountId, visitId, CancellationToken.None);

        Assert.DoesNotContain(rows, r => r.ResolvedUnitStandardExpectedDirectCost is not null);
    }

    // -------------------------------------------------------------------------
    // Append seam transaction boundary
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AddResolutionAsync_is_visible_to_a_fresh_context_only_after_the_caller_saves()
    {
        var (visitId, lineIds) = await SeedSubmittedVisitAsync(1);
        var resolution = ActualWorkLineFinancialResolution.Create(
            AccountId, visitId, lineIds[0], 100m, null,
            FinancialResolutionBasis.SupplierReceipt, "supplier receipt", OwnerId, Now).Value;

        await using var writerCtx = CreateContext();
        var writer = new EfActualWorkFinancialResolutionPersistence(writerCtx);
        await writer.AddResolutionAsync(resolution, CancellationToken.None);

        // Fresh context, before the caller's SaveChangesAsync — nothing is committed yet.
        await using (var beforeSaveCtx = CreateContext())
        {
            var beforeSaveReader = new EfActualWorkFinancialResolutionPersistence(beforeSaveCtx);
            Assert.Empty(await beforeSaveReader.GetResolutionsForVisitAsync(
                AccountId, visitId, CancellationToken.None));
        }

        await writerCtx.SaveChangesAsync();

        // Another fresh context — the row is now retrievable through the read seam.
        await using (var afterSaveCtx = CreateContext())
        {
            var afterSaveReader = new EfActualWorkFinancialResolutionPersistence(afterSaveCtx);
            var rows = await afterSaveReader.GetResolutionsForVisitAsync(
                AccountId, visitId, CancellationToken.None);
            Assert.Single(rows);
            Assert.Equal(resolution.Id, rows[0].Id);
        }
    }

    [Fact]
    public async Task AddDispositionAsync_is_visible_to_a_fresh_context_only_after_the_caller_saves()
    {
        var (visitId, _) = await SeedSubmittedVisitAsync(0);
        var disposition = ActualWorkOfficeFinancialDisposition.Create(
            AccountId, visitId, OfficeFinancialDispositionKind.NoCharge, "customer goodwill", OwnerId, Now).Value;

        await using var writerCtx = CreateContext();
        var writer = new EfActualWorkFinancialResolutionPersistence(writerCtx);
        await writer.AddDispositionAsync(disposition, CancellationToken.None);

        await using (var beforeSaveCtx = CreateContext())
        {
            var beforeSaveReader = new EfActualWorkFinancialResolutionPersistence(beforeSaveCtx);
            Assert.Empty(await beforeSaveReader.GetDispositionsForVisitAsync(
                AccountId, visitId, CancellationToken.None));
        }

        await writerCtx.SaveChangesAsync();

        await using (var afterSaveCtx = CreateContext())
        {
            var afterSaveReader = new EfActualWorkFinancialResolutionPersistence(afterSaveCtx);
            var rows = await afterSaveReader.GetDispositionsForVisitAsync(
                AccountId, visitId, CancellationToken.None);
            Assert.Single(rows);
            Assert.Equal(disposition.Id, rows[0].Id);
        }
    }

    [Fact]
    public async Task GetDispositionsForVisitAsync_returns_every_row_newest_first()
    {
        var (visitId, _) = await SeedSubmittedVisitAsync(0);

        var first = await AppendDispositionAsync(visitId, "first pass", Now);
        var second = await AppendDispositionAsync(visitId, "corrected", Now.AddHours(2));

        await using var readerCtx = CreateContext();
        var reader = new EfActualWorkFinancialResolutionPersistence(readerCtx);
        var rows = await reader.GetDispositionsForVisitAsync(AccountId, visitId, CancellationToken.None);

        Assert.Equal(new[] { second, first }, rows.Select(r => r.Id).ToArray());
    }

    // -------------------------------------------------------------------------
    // BL136 D6c (slice 4e-ii-b-2): superseded-source mutation rejection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateResolutionAsync_with_a_stale_version_on_a_superseded_visit_returns_VersionMismatch()
    {
        var (visitId, lineIds, _) = await SeedSupersededVisitAsync(1);
        var resolution = ActualWorkLineFinancialResolution.Create(
            AccountId, visitId, lineIds[0], 100m, null,
            FinancialResolutionBasis.SupplierReceipt, "supplier receipt", OwnerId, Now).Value;

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkFinancialResolutionPersistence(ctx);

        var outcome = await persistence.CreateResolutionAsync(resolution, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ActualWorkResolutionResult.VersionMismatch, outcome.Result);
    }

    [Fact]
    public async Task CreateResolutionAsync_with_the_current_version_on_a_superseded_visit_returns_Superseded()
    {
        var (visitId, lineIds, currentVersion) = await SeedSupersededVisitAsync(1);
        var resolution = ActualWorkLineFinancialResolution.Create(
            AccountId, visitId, lineIds[0], 100m, null,
            FinancialResolutionBasis.SupplierReceipt, "supplier receipt", OwnerId, Now).Value;

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkFinancialResolutionPersistence(ctx);

        var outcome = await persistence.CreateResolutionAsync(resolution, currentVersion, CancellationToken.None);

        Assert.Equal(ActualWorkResolutionResult.Superseded, outcome.Result);

        await using var verifyCtx = CreateContext();
        var reader = new EfActualWorkFinancialResolutionPersistence(verifyCtx);
        Assert.Empty(await reader.GetResolutionsForVisitAsync(AccountId, visitId, CancellationToken.None));
    }

    [Fact]
    public async Task RecordDispositionAsync_with_a_stale_version_on_a_superseded_visit_returns_VersionMismatch()
    {
        var (visitId, _, _) = await SeedSupersededVisitAsync(0);
        var disposition = ActualWorkOfficeFinancialDisposition.Create(
            AccountId, visitId, OfficeFinancialDispositionKind.NoCharge, "customer goodwill", OwnerId, Now).Value;

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkFinancialResolutionPersistence(ctx);

        var outcome = await persistence.RecordDispositionAsync(disposition, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ActualWorkDispositionResult.VersionMismatch, outcome.Result);
    }

    [Fact]
    public async Task RecordDispositionAsync_with_the_current_version_on_a_superseded_visit_returns_Superseded()
    {
        var (visitId, _, currentVersion) = await SeedSupersededVisitAsync(0);
        var disposition = ActualWorkOfficeFinancialDisposition.Create(
            AccountId, visitId, OfficeFinancialDispositionKind.NoCharge, "customer goodwill", OwnerId, Now).Value;

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkFinancialResolutionPersistence(ctx);

        var outcome = await persistence.RecordDispositionAsync(disposition, currentVersion, CancellationToken.None);

        Assert.Equal(ActualWorkDispositionResult.Superseded, outcome.Result);

        await using var verifyCtx = CreateContext();
        var reader = new EfActualWorkFinancialResolutionPersistence(verifyCtx);
        Assert.Empty(await reader.GetDispositionsForVisitAsync(AccountId, visitId, CancellationToken.None));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Seeds a submitted source visit (with <paramref name="lineCount"/> lines) plus a
    /// submitted successor, then supersedes the source through the domain method — which bumps the
    /// source's concurrency token. Returns the source id, its line ids, and its post-supersede
    /// token (the "current client" version for the mutation-rejection proofs).</summary>
    private async Task<(Guid VisitId, IReadOnlyList<Guid> LineIds, Guid CurrentVersion)> SeedSupersededVisitAsync(int lineCount)
    {
        var (sourceId, lineIds) = await SeedSubmittedVisitAsync(lineCount);
        var (successorId, _) = await SeedSubmittedVisitAsync(0);

        await using var ctx = CreateContext();
        var source = await ctx.Set<ActualWork>().SingleAsync(x => x.Id == sourceId);
        var result = source.Supersede(successorId, OwnerId, "Replaced during office review.", Now);
        Assert.True(result.IsSuccess);
        await ctx.SaveChangesAsync();

        return (sourceId, lineIds, source.ConcurrencyVersion);
    }

    private async Task<Guid> AppendResolutionAsync(
        Guid visitId, Guid lineId, decimal? sell, decimal? cost, DateTime resolvedAt)
    {
        var basis = sell is not null && cost is null
            ? FinancialResolutionBasis.SupplierReceipt
            : FinancialResolutionBasis.Other;
        var resolution = ActualWorkLineFinancialResolution.Create(
            AccountId, visitId, lineId, sell, cost, basis, "office resolution", OwnerId, resolvedAt).Value;

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkFinancialResolutionPersistence(ctx);
        await persistence.AddResolutionAsync(resolution, CancellationToken.None);
        await ctx.SaveChangesAsync();
        return resolution.Id;
    }

    private async Task<Guid> AppendDispositionAsync(Guid visitId, string reason, DateTime disposedAt)
    {
        var disposition = ActualWorkOfficeFinancialDisposition.Create(
            AccountId, visitId, OfficeFinancialDispositionKind.NoCharge, reason, OwnerId, disposedAt).Value;

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkFinancialResolutionPersistence(ctx);
        await persistence.AddDispositionAsync(disposition, CancellationToken.None);
        await ctx.SaveChangesAsync();
        return disposition.Id;
    }

    private async Task InsertResolutionRawAsync(
        Guid accountId, Guid visitId, Guid lineId, decimal? sell, decimal? cost, string basis, string reason)
    {
        await using var ctx = CreateContext();
        await ctx.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO keep_actual_work_line_financial_resolutions
              (id, created_at_utc, updated_at_utc, account_id, actual_work_id, actual_work_line_id,
               resolved_unit_sell_price, resolved_unit_standard_expected_direct_cost, basis, reason,
               resolved_by_account_user_id, resolved_at_utc)
            VALUES
              ({Guid.NewGuid()}, {Now}, {Now}, {accountId}, {visitId}, {lineId},
               {sell}, {cost}, {basis}, {reason}, {OwnerId}, {Now})");
    }

    private async Task InsertDispositionRawAsync(Guid accountId, Guid visitId, string kind, string reason)
    {
        await using var ctx = CreateContext();
        await ctx.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO keep_actual_work_office_financial_dispositions
              (id, created_at_utc, updated_at_utc, account_id, actual_work_id, kind, reason,
               disposed_by_account_user_id, disposed_at_utc)
            VALUES
              ({Guid.NewGuid()}, {Now}, {Now}, {accountId}, {visitId}, {kind}, {reason},
               {OwnerId}, {Now})");
    }

    /// <summary>Creates a Draft with <paramref name="lineCount"/> lines and submits it, mirroring
    /// the production path before any office resolution runs. A zero-line submit carries a
    /// diagnostic outcome so it is a valid submission.</summary>
    private async Task<(Guid VisitId, IReadOnlyList<Guid> LineIds)> SeedSubmittedVisitAsync(int lineCount)
    {
        await using var ctx = CreateContext();
        var persistence = new EfActualWorkPersistence(ctx);
        var visit = ActualWork.Create(AccountId, RequestId, OwnerId).Value;
        for (var i = 0; i < lineCount; i++)
            ActualWorkTestData.AddLine(visit, null, null, $"Line {i + 1}", "each", 1m, null, null, null, null, OwnerId);
        await persistence.AddAsync(visit, CancellationToken.None);

        var lineIds = visit.Lines.Select(l => l.Id).ToArray();

        var submission = new EfActualWorkSubmissionPersistence(ctx, new EfActualWorkReviewSignalReconciliation(ctx));
        var outcome = await submission.SubmitAsync(
            AccountId, visit.Id, visit.ConcurrencyVersion,
            lineCount == 0 ? ActualWorkOutcome.DiagnosticOnly : null,
            lineCount == 0 ? "Diagnostic visit, no work performed." : null,
            Now, CancellationToken.None);
        Assert.Equal(ActualWorkSubmissionResult.Committed, outcome.Result);

        return (visit.Id, lineIds);
    }

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
