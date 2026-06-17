using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Errors;

namespace OpHalo.IntegrationTests.Persistence;

/// <summary>
/// Proves the Keep schema (KeepCustomer, KeepRequest, KeepRequestEvent,
/// KeepPublicIntakeLink) against real PostgreSQL using the same Testcontainers
/// fixture as the Foundation proof tests.
///
/// Run after generating the KeepDomain migration:
///   dotnet ef migrations add KeepDomain \
///     --project src/OpHalo.Foundation.Infrastructure \
///     --startup-project src/OpHalo.Keep.Infrastructure \
///     --context OpHaloDbContext
/// </summary>
[Collection("Postgres")]
public sealed class KeepPersistenceProofTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public KeepPersistenceProofTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OpHaloDbContext CreateContext() => _fixture.CreateContext();

    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly DateTime Now = PostgresFixture.FixedNow;

    // -------------------------------------------------------------------------
    // Test 1: Keep migration applies
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Keep_migration_applies_and_creates_all_tables()
    {
        await using var ctx = CreateContext();

        var tables = await ctx.Database
            .SqlQuery<string>($"""
                SELECT table_name AS "Value"
                FROM information_schema.tables
                WHERE table_schema = 'public'
                """)
            .ToListAsync();

        Assert.Contains("keep_customers", tables);
        Assert.Contains("keep_requests", tables);
        Assert.Contains("keep_request_events", tables);
        Assert.Contains("keep_public_intake_links", tables);
    }

    // -------------------------------------------------------------------------
    // Test 2: KeepRequest + KeepRequestEvent round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task KeepRequest_and_event_round_trip()
    {
        var customer = KeepCustomer.Create(AccountId, "Jane Smith", "0412345678");
        var request = KeepRequest.Create(
            AccountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Burst pipe in bathroom", "PQRS7842", "tok_abc123", Now, 60);
        var ev = KeepRequestEvent.CreateRequestCreated(request.Id, AccountId, Now);

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepCustomer>().Add(customer);
            ctx.Set<KeepRequest>().Add(request);
            ctx.Set<KeepRequestEvent>().Add(ev);
            await ctx.SaveChangesAsync();
        }

        await using var readCtx = CreateContext();

        var savedRequest = await readCtx.Set<KeepRequest>().FindAsync(request.Id);
        var savedEvent = await readCtx.Set<KeepRequestEvent>().FindAsync(ev.Id);

        Assert.NotNull(savedRequest);
        Assert.Equal("Jane Smith", savedRequest.CustomerName);
        Assert.Equal("PQRS7842", savedRequest.ReferenceCode);
        Assert.Equal(OpHalo.Keep.Core.Entities.Enums.KeepRequestStatus.Received, savedRequest.Status);
        Assert.Equal(Now, savedRequest.LastBusinessActivityAt);

        Assert.NotNull(savedEvent);
        Assert.Equal(request.Id, savedEvent.RequestId);
        Assert.Equal(OpHalo.Keep.Core.Entities.Enums.KeepRequestEventType.RequestCreated, savedEvent.EventType);
        Assert.Equal(OpHalo.Keep.Core.Entities.Enums.KeepRequestEventVisibility.System, savedEvent.Visibility);
    }

    // -------------------------------------------------------------------------
    // Test 3: KeepCustomer (AccountId, PrimaryPhone) uniqueness enforced
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Duplicate_account_phone_customer_is_rejected()
    {
        var first = KeepCustomer.Create(AccountId, "Jane Smith", "0412345678");

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepCustomer>().Add(first);
            await ctx.SaveChangesAsync();
        }

        var duplicate = KeepCustomer.Create(AccountId, "Jane S.", "0412345678");

        await using var ctx2 = CreateContext();
        ctx2.Set<KeepCustomer>().Add(duplicate);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23505", pgEx.SqlState);
        Assert.Equal("ix_keep_customers_account_phone", pgEx.ConstraintName);
    }

    // -------------------------------------------------------------------------
    // Test 4: Duplicate page token rejected
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Duplicate_page_token_is_rejected()
    {
        var customer = KeepCustomer.Create(AccountId, "Jane", "0412345678");
        var r1 = KeepRequest.Create(AccountId, customer.Id, "Jane", "04123", null, "Desc", "REF1", "shared-tok", Now, 60);

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepCustomer>().Add(customer);
            ctx.Set<KeepRequest>().Add(r1);
            await ctx.SaveChangesAsync();
        }

        var r2 = KeepRequest.Create(AccountId, customer.Id, "Jane", "04123", null, "Other desc", "REF2", "shared-tok", Now, 60);

        await using var ctx2 = CreateContext();
        ctx2.Set<KeepRequest>().Add(r2);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23505", pgEx.SqlState);
        Assert.Equal("ix_keep_requests_page_token", pgEx.ConstraintName);
    }

    // -------------------------------------------------------------------------
    // Test 5: Duplicate (AccountId, ReferenceCode) rejected
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Duplicate_account_reference_code_is_rejected()
    {
        var customer = KeepCustomer.Create(AccountId, "Jane", "0412345678");
        var r1 = KeepRequest.Create(AccountId, customer.Id, "Jane", "04123", null, "Desc", "SAME-REF", "tok1", Now, 60);

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepCustomer>().Add(customer);
            ctx.Set<KeepRequest>().Add(r1);
            await ctx.SaveChangesAsync();
        }

        var r2 = KeepRequest.Create(AccountId, customer.Id, "Jane", "04123", null, "Other", "SAME-REF", "tok2", Now, 60);

        await using var ctx2 = CreateContext();
        ctx2.Set<KeepRequest>().Add(r2);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23505", pgEx.SqlState);
        Assert.Equal("ix_keep_requests_account_reference_code", pgEx.ConstraintName);
    }

    // -------------------------------------------------------------------------
    // Test 6: Public intake link — active unique constraints
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Active_slug_uniqueness_enforced()
    {
        var first = KeepPublicIntakeLink.Create(AccountId, "acme-plumbing", new string('a', 64));

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepPublicIntakeLink>().Add(first);
            await ctx.SaveChangesAsync();
        }

        var duplicate = KeepPublicIntakeLink.Create(Guid.NewGuid(), "acme-plumbing", new string('b', 64));

        await using var ctx2 = CreateContext();
        ctx2.Set<KeepPublicIntakeLink>().Add(duplicate);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23505", pgEx.SqlState);
        Assert.Equal("ix_keep_public_intake_links_active_slug", pgEx.ConstraintName);
    }

    [Fact]
    public async Task Revoked_slug_allows_new_active_link_on_same_slug()
    {
        var first = KeepPublicIntakeLink.Create(AccountId, "acme-plumbing", new string('a', 64));

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepPublicIntakeLink>().Add(first);
            await ctx.SaveChangesAsync();
        }

        // Revoke the first link.
        await using (var ctx = CreateContext())
        {
            var link = await ctx.Set<KeepPublicIntakeLink>().FindAsync(first.Id);
            link!.Revoke(Now);
            await ctx.SaveChangesAsync();
        }

        // Second link with same slug should now be accepted.
        var second = KeepPublicIntakeLink.Create(AccountId, "acme-plumbing", new string('b', 64));

        await using var ctx2 = CreateContext();
        ctx2.Set<KeepPublicIntakeLink>().Add(second);
        await ctx2.SaveChangesAsync(); // must not throw

        var saved = await ctx2.Set<KeepPublicIntakeLink>().FindAsync(second.Id);
        Assert.NotNull(saved);
        Assert.True(saved.IsActive);
    }

    // -------------------------------------------------------------------------
    // Test 7: Soft-delete filter hides Keep rows
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Soft_deleted_keep_rows_are_hidden_by_query_filter()
    {
        var customer = KeepCustomer.Create(AccountId, "Jane", "0499999999");

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepCustomer>().Add(customer);
            await ctx.SaveChangesAsync();
        }

        await using (var mutCtx = CreateContext())
        {
            mutCtx.Set<KeepCustomer>().Attach(customer);
            customer.DeletedAtUtc = Now;
            await mutCtx.SaveChangesAsync();
        }

        await using var readCtx = CreateContext();

        Assert.Null(await readCtx.Set<KeepCustomer>().FirstOrDefaultAsync(c => c.Id == customer.Id));
        Assert.True(await readCtx.Set<KeepCustomer>().IgnoreQueryFilters().AnyAsync(c => c.Id == customer.Id));
    }
}
