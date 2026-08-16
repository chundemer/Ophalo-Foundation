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
/// Proves <see cref="EfProposedScopePersistence"/> (ordinary create/edit, the ADR-464-style
/// one-open-draft race) and <see cref="EfProposedScopeSubmissionPersistence"/> (the atomic
/// submit/signal-reconciliation transaction, ADR-463) against real PostgreSQL (Session 3.3a.2).
///
/// Run after generating the ProposedScopeAndLine/KeepRequestWorkSignal migrations:
///   dotnet ef migrations add KeepRequestWorkSignal \
///     --project src/OpHalo.Foundation.Infrastructure \
///     --startup-project src/OpHalo.Keep.Infrastructure
/// </summary>
[Collection("Postgres")]
public sealed class ProposedScopeSubmissionTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime Now = PostgresFixture.FixedNow;

    private readonly PostgresFixture _fixture;

    private Guid AccountId { get; set; }
    private Guid OwnerId { get; set; }
    private Guid OtherAccountId { get; set; }
    private Guid RequestId { get; set; }
    private Guid CustomerId { get; set; }

    public ProposedScopeSubmissionTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();

        (AccountId, OwnerId) = await SeedAccountAsync(ctx, "Test Business", "owner@proposed-scope.example.com");
        (OtherAccountId, _) = await SeedAccountAsync(ctx, "Other Business", "owner2@proposed-scope.example.com");
        (RequestId, CustomerId) = await SeedRequestAsync(ctx, AccountId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OpHaloDbContext CreateContext() => _fixture.CreateContext();

    // -------------------------------------------------------------------------
    // IProposedScopePersistence — ordinary create/edit
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AddAsync_persists_the_scope()
    {
        await using var ctx = CreateContext();
        var persistence = new EfProposedScopePersistence(ctx);
        var scope = ProposedScope.Create(AccountId, RequestId, OwnerId).Value;

        var result = await persistence.AddAsync(scope, CancellationToken.None);

        Assert.Equal(ProposedScopeCommitResult.Committed, result);

        await using var verifyCtx = CreateContext();
        var reloaded = await verifyCtx.Set<ProposedScope>().SingleAsync(x => x.Id == scope.Id);
        Assert.Equal(ProposedScopeStatus.Draft, reloaded.Status);
    }

    [Fact]
    public async Task AddAsync_rejects_a_second_open_draft_for_the_same_request()
    {
        await using var ctx = CreateContext();
        var persistence = new EfProposedScopePersistence(ctx);
        var first = ProposedScope.Create(AccountId, RequestId, OwnerId).Value;
        var firstResult = await persistence.AddAsync(first, CancellationToken.None);

        await using var secondCtx = CreateContext();
        var secondPersistence = new EfProposedScopePersistence(secondCtx);
        var second = ProposedScope.Create(AccountId, RequestId, OwnerId).Value;
        var secondResult = await secondPersistence.AddAsync(second, CancellationToken.None);

        Assert.Equal(ProposedScopeCommitResult.Committed, firstResult);
        Assert.Equal(ProposedScopeCommitResult.DraftAlreadyOpenForRequest, secondResult);
    }

    [Fact]
    public async Task GetByIdAsync_for_a_wrong_account_id_returns_null()
    {
        await using var ctx = CreateContext();
        var persistence = new EfProposedScopePersistence(ctx);
        var scope = ProposedScope.Create(AccountId, RequestId, OwnerId).Value;
        await persistence.AddAsync(scope, CancellationToken.None);

        await using var readCtx = CreateContext();
        var readPersistence = new EfProposedScopePersistence(readCtx);
        var reloaded = await readPersistence.GetByIdAsync(OtherAccountId, scope.Id, CancellationToken.None);

        Assert.Null(reloaded);
    }

    [Fact]
    public async Task CommitAsync_persists_a_line_added_to_a_loaded_scope()
    {
        var catalogItemId = await SeedActiveCatalogItemAsync();

        await using var ctx = CreateContext();
        var persistence = new EfProposedScopePersistence(ctx);
        var scope = ProposedScope.Create(AccountId, RequestId, OwnerId).Value;
        await persistence.AddAsync(scope, CancellationToken.None);

        await using var editCtx = CreateContext();
        var editPersistence = new EfProposedScopePersistence(editCtx);
        var loaded = await editPersistence.GetByIdAsync(AccountId, scope.Id, CancellationToken.None);
        loaded!.AddLine(
            ProposedScopeLineType.KnownCatalogItem, catalogItemId, null, 1m, false,
            null, null, null, 0, "Drain Pan", "each", null, null, OwnerId);

        var commitResult = await editPersistence.CommitAsync(loaded, CancellationToken.None);

        Assert.Equal(ProposedScopeCommitResult.Committed, commitResult);

        await using var verifyCtx = CreateContext();
        var reloaded = await verifyCtx.Set<ProposedScope>().Include(x => x.Lines).SingleAsync(x => x.Id == scope.Id);
        Assert.Single(reloaded.Lines);
    }

    [Fact]
    public async Task CommitAsync_with_a_stale_row_fails_with_ConcurrencyConflict()
    {
        var scopeId = await SeedDraftScopeWithLineAsync();

        // Two independently loaded copies of the same row; the first writer wins.
        await using var ctxA = CreateContext();
        var loadedA = await new EfProposedScopePersistence(ctxA).GetByIdAsync(AccountId, scopeId, CancellationToken.None);
        await using var ctxB = CreateContext();
        var loadedB = await new EfProposedScopePersistence(ctxB).GetByIdAsync(AccountId, scopeId, CancellationToken.None);

        var submitA = loadedA!.Submit(Now);
        Assert.True(submitA.IsSuccess);
        await new EfProposedScopePersistence(ctxA).CommitAsync(loadedA, CancellationToken.None);

        var submitB = loadedB!.Submit(Now);
        Assert.True(submitB.IsSuccess);
        var staleResult = await new EfProposedScopePersistence(ctxB).CommitAsync(loadedB, CancellationToken.None);

        Assert.Equal(ProposedScopeCommitResult.ConcurrencyConflict, staleResult);
    }

    // -------------------------------------------------------------------------
    // IProposedScopeSubmissionPersistence — atomic submit/signal
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SubmitAsync_transitions_the_scope_and_raises_the_signal()
    {
        var scopeId = await SeedDraftScopeWithLineAsync();

        await using var ctx = CreateContext();
        var persistence = new EfProposedScopeSubmissionPersistence(ctx);

        var outcome = await persistence.SubmitAsync(AccountId, scopeId, await GetVersionAsync(scopeId), Now, CancellationToken.None);

        Assert.Equal(ProposedScopeSubmissionResult.Committed, outcome.Result);
        Assert.NotNull(outcome.ConcurrencyVersion);

        await using var verifyCtx = CreateContext();
        var scope = await verifyCtx.Set<ProposedScope>().SingleAsync(x => x.Id == scopeId);
        Assert.Equal(ProposedScopeStatus.SubmittedToOffice, scope.Status);
        Assert.Equal(outcome.ConcurrencyVersion, scope.ConcurrencyVersion);

        var signal = await GetSignalAsync(verifyCtx);
        Assert.NotNull(signal);
        Assert.Equal(Now, signal!.RaisedAtUtc);
        Assert.Null(signal.ResolvedAtUtc);
    }

    [Fact]
    public async Task SubmitAsync_when_the_signal_is_already_active_does_not_reset_RaisedAtUtc()
    {
        var firstScopeId = await SeedDraftScopeWithLineAsync();
        await using (var ctx = CreateContext())
        {
            var persistence = new EfProposedScopeSubmissionPersistence(ctx);
            await persistence.SubmitAsync(AccountId, firstScopeId, await GetVersionAsync(firstScopeId), Now, CancellationToken.None);
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

        var secondScopeId = await SeedDraftScopeWithLineAsync();
        var later = Now.AddHours(2);
        await using (var ctx = CreateContext())
        {
            var persistence = new EfProposedScopeSubmissionPersistence(ctx);
            var outcome = await persistence.SubmitAsync(
                AccountId, secondScopeId, await GetVersionAsync(secondScopeId), later, CancellationToken.None);
            Assert.Equal(ProposedScopeSubmissionResult.Committed, outcome.Result);
        }

        // The WHERE clause on the upsert's conflict target means DO UPDATE never fires for an
        // already-active row — not RaisedAtUtc alone, the entire row (including
        // ConcurrencyVersion/UpdatedAtUtc) must be byte-for-byte untouched by the second submit.
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
        var firstScopeId = await SeedDraftScopeWithLineAsync();
        await using (var ctx = CreateContext())
        {
            var persistence = new EfProposedScopeSubmissionPersistence(ctx);
            await persistence.SubmitAsync(AccountId, firstScopeId, await GetVersionAsync(firstScopeId), Now, CancellationToken.None);
        }

        // Simulate a future office-review resolution (Session 3.5 has no resolve method yet).
        await using (var resolveCtx = CreateContext())
        {
            await resolveCtx.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE keep_request_work_signals SET resolved_at_utc = {Now} WHERE account_id = {AccountId}");
        }

        var secondScopeId = await SeedDraftScopeWithLineAsync();
        var reopenedAt = Now.AddDays(3);
        await using (var ctx = CreateContext())
        {
            var persistence = new EfProposedScopeSubmissionPersistence(ctx);
            var outcome = await persistence.SubmitAsync(
                AccountId, secondScopeId, await GetVersionAsync(secondScopeId), reopenedAt, CancellationToken.None);
            Assert.Equal(ProposedScopeSubmissionResult.Committed, outcome.Result);
        }

        await using var verifyCtx = CreateContext();
        var signal = await GetSignalAsync(verifyCtx);
        Assert.NotNull(signal);
        Assert.Equal(reopenedAt, signal!.RaisedAtUtc);
        Assert.Null(signal.ResolvedAtUtc);
    }

    [Fact]
    public async Task SubmitAsync_for_an_unknown_scope_id_returns_NotFound()
    {
        await using var ctx = CreateContext();
        var persistence = new EfProposedScopeSubmissionPersistence(ctx);

        var outcome = await persistence.SubmitAsync(AccountId, Guid.NewGuid(), Guid.NewGuid(), Now, CancellationToken.None);

        Assert.Equal(ProposedScopeSubmissionResult.NotFound, outcome.Result);
    }

    [Fact]
    public async Task SubmitAsync_for_a_wrong_account_id_returns_NotFound()
    {
        var scopeId = await SeedDraftScopeAsync();

        await using var ctx = CreateContext();
        var persistence = new EfProposedScopeSubmissionPersistence(ctx);

        var outcome = await persistence.SubmitAsync(OtherAccountId, scopeId, await GetVersionAsync(scopeId), Now, CancellationToken.None);

        Assert.Equal(ProposedScopeSubmissionResult.NotFound, outcome.Result);
    }

    [Fact]
    public async Task SubmitAsync_with_a_stale_expected_version_returns_VersionMismatch()
    {
        var scopeId = await SeedDraftScopeAsync();

        await using var ctx = CreateContext();
        var persistence = new EfProposedScopeSubmissionPersistence(ctx);

        var outcome = await persistence.SubmitAsync(AccountId, scopeId, Guid.NewGuid(), Now, CancellationToken.None);

        Assert.Equal(ProposedScopeSubmissionResult.VersionMismatch, outcome.Result);

        await using var verifyCtx = CreateContext();
        var scope = await verifyCtx.Set<ProposedScope>().SingleAsync(x => x.Id == scopeId);
        Assert.Equal(ProposedScopeStatus.Draft, scope.Status);
    }

    [Fact]
    public async Task SubmitAsync_for_a_terminal_request_returns_RequestTerminal_and_does_not_mutate_the_scope()
    {
        var scopeId = await SeedDraftScopeAsync();

        await using (var closeCtx = CreateContext())
        {
            var request = await closeCtx.Set<KeepRequest>().SingleAsync(x => x.Id == RequestId);
            // Received -> Closed is not a direct transition (only Resolved -> Closed is);
            // Received -> Cancelled is, and Cancelled is equally terminal for this proof.
            var closeResult = request.ChangeStatus(
                KeepRequestStatus.Cancelled, "Customer cancelled", OwnerId, "Test Owner", Now);
            Assert.True(closeResult.IsSuccess);
            await closeCtx.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var persistence = new EfProposedScopeSubmissionPersistence(ctx);
        var outcome = await persistence.SubmitAsync(AccountId, scopeId, await GetVersionAsync(scopeId), Now, CancellationToken.None);

        Assert.Equal(ProposedScopeSubmissionResult.RequestTerminal, outcome.Result);

        await using var verifyCtx = CreateContext();
        var scope = await verifyCtx.Set<ProposedScope>().SingleAsync(x => x.Id == scopeId);
        Assert.Equal(ProposedScopeStatus.Draft, scope.Status);
        Assert.Null(await GetSignalAsync(verifyCtx));
    }

    [Fact]
    public async Task SubmitAsync_for_a_zero_line_scope_returns_EmptySubmit_and_does_not_mutate_the_scope()
    {
        var scopeId = await SeedDraftScopeAsync();

        await using var ctx = CreateContext();
        var persistence = new EfProposedScopeSubmissionPersistence(ctx);
        var outcome = await persistence.SubmitAsync(AccountId, scopeId, await GetVersionAsync(scopeId), Now, CancellationToken.None);

        Assert.Equal(ProposedScopeSubmissionResult.EmptySubmit, outcome.Result);

        await using var verifyCtx = CreateContext();
        var scope = await verifyCtx.Set<ProposedScope>().SingleAsync(x => x.Id == scopeId);
        Assert.Equal(ProposedScopeStatus.Draft, scope.Status);
        Assert.Null(await GetSignalAsync(verifyCtx));
    }

    [Fact]
    public async Task SubmitAsync_observes_a_terminal_transition_that_commits_while_it_waits_for_the_row_lock()
    {
        // Proves the FOR UPDATE lock (not just an AsNoTracking read) is what makes the terminal
        // check honest: a concurrent request-close that has already written its UPDATE — and so
        // already holds Postgres's row lock — but not yet committed must make SubmitAsync's own
        // FOR UPDATE actually wait, not read a stale non-terminal snapshot.
        var scopeId = await SeedDraftScopeAsync();
        var version = await GetVersionAsync(scopeId);

        await using var closeCtx = CreateContext();
        await using var closeTx = await closeCtx.Database.BeginTransactionAsync();
        var request = await closeCtx.Set<KeepRequest>().SingleAsync(x => x.Id == RequestId);
        var closeResult = request.ChangeStatus(KeepRequestStatus.Cancelled, "Customer cancelled", OwnerId, "Test Owner", Now);
        Assert.True(closeResult.IsSuccess);
        // Issues the UPDATE and takes the row lock now, while closeTx is still open (uncommitted).
        await closeCtx.SaveChangesAsync();

        await using var submitCtx = CreateContext();
        // Pre-establish the connection outside the timed race, matching this codebase's existing
        // concurrency-test convention (e.g. the ADR-466 create-race tests) — first-query
        // connection-pool latency would otherwise let the race finish before it starts.
        await submitCtx.Database.CanConnectAsync();
        var submitTask = Task.Run(() => new EfProposedScopeSubmissionPersistence(submitCtx)
            .SubmitAsync(AccountId, scopeId, version, Now, CancellationToken.None));

        // Give SubmitAsync's FOR UPDATE time to actually reach the lock and start waiting before
        // releasing it — the assertion below holds regardless (closeCtx's SaveChangesAsync above
        // already took the lock before submitTask was created), but this biases the test toward
        // genuinely exercising the wait rather than racing past it.
        await Task.Delay(200);
        await closeTx.CommitAsync();

        var outcome = await submitTask;

        Assert.Equal(ProposedScopeSubmissionResult.RequestTerminal, outcome.Result);

        await using var verifyCtx = CreateContext();
        var scope = await verifyCtx.Set<ProposedScope>().SingleAsync(x => x.Id == scopeId);
        Assert.Equal(ProposedScopeStatus.Draft, scope.Status);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> SeedDraftScopeAsync()
    {
        await using var ctx = CreateContext();
        var persistence = new EfProposedScopePersistence(ctx);
        var scope = ProposedScope.Create(AccountId, RequestId, OwnerId).Value;
        await persistence.AddAsync(scope, CancellationToken.None);
        return scope.Id;
    }

    /// <summary>Session 4a's <c>EmptySubmit</c> domain rule means a Draft scope with zero lines can
    /// never reach <c>Committed</c>; every successful-submit proof needs at least one line.</summary>
    private async Task<Guid> SeedDraftScopeWithLineAsync()
    {
        var catalogItemId = await SeedActiveCatalogItemAsync();

        await using var ctx = CreateContext();
        var persistence = new EfProposedScopePersistence(ctx);
        var scope = ProposedScope.Create(AccountId, RequestId, OwnerId).Value;
        scope.AddLine(
            ProposedScopeLineType.KnownCatalogItem, catalogItemId, null, 1m, false,
            null, null, null, 0, "Drain Pan", "each", null, null, OwnerId);
        await persistence.AddAsync(scope, CancellationToken.None);
        return scope.Id;
    }

    private async Task<Guid> GetVersionAsync(Guid scopeId)
    {
        await using var ctx = CreateContext();
        return await ctx.Set<ProposedScope>().Where(x => x.Id == scopeId).Select(x => x.ConcurrencyVersion).SingleAsync();
    }

    private async Task<KeepRequestWorkSignal?> GetSignalAsync(OpHaloDbContext ctx) =>
        await ctx.Set<KeepRequestWorkSignal>()
            .FirstOrDefaultAsync(x =>
                x.AccountId == AccountId &&
                x.KeepRequestId == RequestId &&
                x.SourceModuleKey == KeepRequestWorkSignalKeys.Modules.PriceBookQuotesMaterials &&
                x.SignalKey == KeepRequestWorkSignalKeys.Signals.ProposedScopeNeedsOfficeReview);

    private async Task<Guid> SeedActiveCatalogItemAsync()
    {
        await using var ctx = CreateContext();
        var catalogItemId = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_catalog_items (
                id, account_id, type, display_name, external_key, normalized_external_key,
                category_id, unit_of_measure, currency, is_common_item, active_state,
                current_price_book_version_line_id, source_actual_work_line_id, concurrency_version,
                created_at_utc, updated_at_utc)
            VALUES (
                {catalogItemId}, {AccountId}, 'Material', {"Test Item " + catalogItemId}, NULL, NULL,
                NULL, 'each', 'USD', false, 'Active',
                NULL, NULL, {Guid.NewGuid()},
                {Now}, {Now})
            """);
        return catalogItemId;
    }

    private static async Task<(Guid RequestId, Guid CustomerId)> SeedRequestAsync(OpHaloDbContext ctx, Guid accountId)
    {
        var customer = KeepCustomer.Create(accountId, "Jane Customer", "+15555550100");
        ctx.Set<KeepCustomer>().Add(customer);

        var request = KeepRequest.CreateByBusiness(
            accountId, customer.Id, "Jane Customer", "+15555550100", null, "Leaky faucet",
            $"R{Guid.NewGuid():N}"[..20], $"tok_{Guid.NewGuid():N}", Now, KeepRequestSource.Phone);
        ctx.Set<KeepRequest>().Add(request);

        await ctx.SaveChangesAsync();
        return (request.Id, customer.Id);
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
