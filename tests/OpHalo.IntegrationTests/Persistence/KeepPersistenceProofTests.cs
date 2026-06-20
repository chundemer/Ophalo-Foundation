using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.IntegrationTests.Persistence;

/// <summary>
/// Proves the Keep schema (KeepCustomer, KeepRequest, KeepRequestEvent,
/// KeepPublicIntakeLink) against real PostgreSQL using the same Testcontainers
/// fixture as the Foundation proof tests.
///
/// Run after generating the KeepG1AccountSafeSchema migration:
///   dotnet ef migrations add KeepG1AccountSafeSchema \
///     --project src/OpHalo.Foundation.Infrastructure \
///     --startup-project src/OpHalo.Keep.Infrastructure \
///     --context OpHaloDbContext
/// </summary>
[Collection("Postgres")]
public sealed class KeepPersistenceProofTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public KeepPersistenceProofTests(PostgresFixture fixture) => _fixture = fixture;

    private static readonly DateTime Now = PostgresFixture.FixedNow;

    // Instance IDs — set during InitializeAsync after seeding real account rows.
    private Guid AccountId { get; set; }
    private Guid SecondAccountId { get; set; }
    private Guid SecondAccountOwnerUserId { get; set; }

    public async Task InitializeAsync()
    {
        await using var ctx = CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();

        // G1 adds FK constraints from Keep entities → accounts. Seed two real accounts.
        (AccountId, _) = await SeedAccountAsync(ctx, "Test Business", "owner@test.example.com");
        (SecondAccountId, SecondAccountOwnerUserId) = await SeedAccountAsync(ctx, "Other Business", "owner2@test.example.com");
    }

    private static async Task<(Guid AccountId, Guid OwnerUserId)> SeedAccountAsync(
        OpHaloDbContext ctx, string businessName, string email)
    {
        var result = new AccountProvisioningService().CreateVerified(
            email: email,
            name: "Test Owner",
            businessName: businessName,
            purpose: AccountPurpose.Business,
            timeZone: "UTC",
            plan: AccountPlan.Trial,
            isPilot: false,
            nowUtc: Now,
            trialEndsAtUtc: Now.AddDays(30));

        if (!result.IsSuccess)
            throw new InvalidOperationException($"Failed to provision account: {result.Error}");

        var graph = result.Value;
        ctx.Users.Add(graph.User);
        ctx.Accounts.Add(graph.Account);
        ctx.AccountUsers.Add(graph.Owner);
        ctx.AccountEntitlements.Add(graph.Entitlements);

        // Two-phase save: Account ↔ AccountUser circular FK (ADR-019 / PersistenceProofTests pattern).
        var ownerIdEntry = ctx.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerIdEntry.CurrentValue = null;
        await ctx.SaveChangesAsync();
        ownerIdEntry.CurrentValue = graph.Owner.Id;
        await ctx.SaveChangesAsync();

        return (graph.Account.Id, graph.Owner.Id);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OpHaloDbContext CreateContext() => _fixture.CreateContext();

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
        var request = KeepRequest.CreateFromCustomerIntake(
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
        // Customer-origin: LastCustomerActivityAt = Now, LastBusinessActivityAt = null (D3).
        Assert.Equal(Now, savedRequest.LastCustomerActivityAt);
        Assert.Null(savedRequest.LastBusinessActivityAt);

        Assert.NotNull(savedEvent);
        Assert.Equal(request.Id, savedEvent.RequestId);
        Assert.Equal(OpHalo.Keep.Core.Entities.Enums.KeepRequestEventType.RequestCreated, savedEvent.EventType);
        Assert.Equal(OpHalo.Keep.Core.Entities.Enums.KeepRequestEventVisibility.System, savedEvent.Visibility);
    }

    // -------------------------------------------------------------------------
    // Test 3: KeepCustomer canonical-phone uniqueness (account-scoped)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Duplicate_canonical_phone_within_account_is_rejected()
    {
        // Two distinct display forms that normalize to the same canonical digits.
        var first = KeepCustomer.Create(AccountId, "Jane Smith", "0412 345 678");

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepCustomer>().Add(first);
            await ctx.SaveChangesAsync();
        }

        var duplicate = KeepCustomer.Create(AccountId, "Jane S.", "0412-345-678"); // same canonical

        await using var ctx2 = CreateContext();
        ctx2.Set<KeepCustomer>().Add(duplicate);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23505", pgEx.SqlState);
        Assert.Equal("ix_keep_customers_account_canonical_phone", pgEx.ConstraintName);
    }

    [Fact]
    public async Task Same_canonical_phone_under_different_accounts_is_accepted()
    {
        var forAccount1 = KeepCustomer.Create(AccountId, "Jane Smith", "0412 345 678");
        var forAccount2 = KeepCustomer.Create(SecondAccountId, "Jane S.", "0412-345-678");

        await using var ctx = CreateContext();
        ctx.Set<KeepCustomer>().AddRange(forAccount1, forAccount2);
        await ctx.SaveChangesAsync(); // must not throw
    }

    // -------------------------------------------------------------------------
    // Test 4: Cross-account FK — request cannot reference a customer from another account
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Request_cannot_reference_customer_from_different_account()
    {
        // Customer belongs to AccountId.
        var customer = KeepCustomer.Create(AccountId, "Jane", "0412345678");

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepCustomer>().Add(customer);
            await ctx.SaveChangesAsync();
        }

        // Request uses SecondAccountId but references customer.Id from AccountId — FK violation.
        var crossAccountRequest = KeepRequest.CreateFromCustomerIntake(
            SecondAccountId, customer.Id,
            "Jane", "0412345678", null,
            "Description", "CROSS01", "tok_cross1", Now, 60);

        await using var ctx2 = CreateContext();
        ctx2.Set<KeepRequest>().Add(crossAccountRequest);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23503", pgEx.SqlState); // foreign key violation
    }

    // -------------------------------------------------------------------------
    // Test 5: Cross-account FK — event cannot reference a request from another account
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Event_cannot_reference_request_from_different_account()
    {
        var customer = KeepCustomer.Create(AccountId, "Jane", "0444444444");
        var request = KeepRequest.CreateFromCustomerIntake(
            AccountId, customer.Id,
            "Jane", "0444444444", null,
            "Description", "EVTREF1", "tok_evt1", Now, 60);

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepCustomer>().Add(customer);
            ctx.Set<KeepRequest>().Add(request);
            await ctx.SaveChangesAsync();
        }

        // Event uses SecondAccountId but references request.Id from AccountId — FK violation.
        var crossAccountEvent = KeepRequestEvent.CreateRequestCreated(
            request.Id, SecondAccountId, Now);

        await using var ctx2 = CreateContext();
        ctx2.Set<KeepRequestEvent>().Add(crossAccountEvent);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23503", pgEx.SqlState); // foreign key violation
    }

    // -------------------------------------------------------------------------
    // Test 6: Duplicate page token rejected
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Duplicate_page_token_is_rejected()
    {
        var customer = KeepCustomer.Create(AccountId, "Jane", "0412399999");
        var r1 = KeepRequest.CreateFromCustomerIntake(AccountId, customer.Id, "Jane", "0412399999", null, "Desc", "REF1", "shared-tok", Now, 60);

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepCustomer>().Add(customer);
            ctx.Set<KeepRequest>().Add(r1);
            await ctx.SaveChangesAsync();
        }

        var r2 = KeepRequest.CreateFromCustomerIntake(AccountId, customer.Id, "Jane", "0412399999", null, "Other desc", "REF2", "shared-tok", Now, 60);

        await using var ctx2 = CreateContext();
        ctx2.Set<KeepRequest>().Add(r2);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23505", pgEx.SqlState);
        Assert.Equal("ix_keep_requests_page_token", pgEx.ConstraintName);
    }

    // -------------------------------------------------------------------------
    // Test 7: Duplicate (AccountId, ReferenceCode) rejected
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Duplicate_account_reference_code_is_rejected()
    {
        var customer = KeepCustomer.Create(AccountId, "Jane", "0412388888");
        var r1 = KeepRequest.CreateFromCustomerIntake(AccountId, customer.Id, "Jane", "0412388888", null, "Desc", "SAME-REF", "tok1ref", Now, 60);

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepCustomer>().Add(customer);
            ctx.Set<KeepRequest>().Add(r1);
            await ctx.SaveChangesAsync();
        }

        var r2 = KeepRequest.CreateFromCustomerIntake(AccountId, customer.Id, "Jane", "0412388888", null, "Other", "SAME-REF", "tok2ref", Now, 60);

        await using var ctx2 = CreateContext();
        ctx2.Set<KeepRequest>().Add(r2);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23505", pgEx.SqlState);
        Assert.Equal("ix_keep_requests_account_reference_code", pgEx.ConstraintName);
    }

    // -------------------------------------------------------------------------
    // Test 8: Public intake link — one active link per account
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Second_active_intake_link_for_same_account_is_rejected()
    {
        var first = KeepPublicIntakeLink.Create(AccountId, "acme-plumbing", new string('a', 64));

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepPublicIntakeLink>().Add(first);
            await ctx.SaveChangesAsync();
        }

        var second = KeepPublicIntakeLink.Create(AccountId, "acme-plumbing-v2", new string('b', 64));

        await using var ctx2 = CreateContext();
        ctx2.Set<KeepPublicIntakeLink>().Add(second);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23505", pgEx.SqlState);
        Assert.Equal("ix_keep_public_intake_links_account_active", pgEx.ConstraintName);
    }

    [Fact]
    public async Task Revoked_link_allows_new_active_link_for_same_account()
    {
        var first = KeepPublicIntakeLink.Create(AccountId, "acme-beta", new string('c', 64));

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepPublicIntakeLink>().Add(first);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            var link = await ctx.Set<KeepPublicIntakeLink>().FindAsync(first.Id);
            link!.Revoke(Now);
            await ctx.SaveChangesAsync();
        }

        var second = KeepPublicIntakeLink.Create(AccountId, "acme-beta-v2", new string('d', 64));

        await using var ctx2 = CreateContext();
        ctx2.Set<KeepPublicIntakeLink>().Add(second);
        await ctx2.SaveChangesAsync(); // must not throw

        var saved = await ctx2.Set<KeepPublicIntakeLink>().FindAsync(second.Id);
        Assert.NotNull(saved);
        Assert.True(saved.IsActive);
    }

    // -------------------------------------------------------------------------
    // Test 9: Active slug uniqueness enforced
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Active_slug_uniqueness_enforced()
    {
        var first = KeepPublicIntakeLink.Create(AccountId, "slug-test-unique", new string('e', 64));

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepPublicIntakeLink>().Add(first);
            await ctx.SaveChangesAsync();
        }

        var duplicate = KeepPublicIntakeLink.Create(SecondAccountId, "slug-test-unique", new string('f', 64));

        await using var ctx2 = CreateContext();
        ctx2.Set<KeepPublicIntakeLink>().Add(duplicate);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23505", pgEx.SqlState);
        Assert.Equal("ix_keep_public_intake_links_active_slug", pgEx.ConstraintName);
    }

    // -------------------------------------------------------------------------
    // Test 11: Cross-account FK — participant cannot reference request from another account
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Participant_cannot_reference_request_from_different_account()
    {
        var customer = KeepCustomer.Create(AccountId, "Jane", "0412345612");
        var request = KeepRequest.CreateFromCustomerIntake(
            AccountId, customer.Id, "Jane", "0412345612", null,
            "Description", "PREF01", "tok_pref1", Now, 60);

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepCustomer>().Add(customer);
            ctx.Set<KeepRequest>().Add(request);
            await ctx.SaveChangesAsync();
        }

        // Participant AccountId = SecondAccountId but RequestId is owned by AccountId.
        // The composite FK (AccountId, RequestId) → KeepRequest(AccountId, Id) must reject this.
        var participant = KeepRequestParticipant.Create(
            requestId: request.Id,
            accountId: SecondAccountId,
            accountUserId: SecondAccountOwnerUserId,
            participationType: ParticipationType.Responsible,
            notificationsEnabled: true,
            attachedAtUtc: Now);

        await using var ctx2 = CreateContext();
        ctx2.Set<KeepRequestParticipant>().Add(participant);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23503", pgEx.SqlState);
    }

    // -------------------------------------------------------------------------
    // Test 12: Cross-account FK — participant AccountUser must belong to the same account
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Participant_AccountUser_must_belong_to_same_account()
    {
        var customer = KeepCustomer.Create(AccountId, "Jane", "0412345613");
        var request = KeepRequest.CreateFromCustomerIntake(
            AccountId, customer.Id, "Jane", "0412345613", null,
            "Description", "PREF02", "tok_pref2", Now, 60);

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepCustomer>().Add(customer);
            ctx.Set<KeepRequest>().Add(request);
            await ctx.SaveChangesAsync();
        }

        // Participant AccountId = AccountId (correct) but AccountUserId is from SecondAccountId.
        // The composite FK (AccountId, AccountUserId) → AccountUser(AccountId, Id) must reject this.
        var participant = KeepRequestParticipant.Create(
            requestId: request.Id,
            accountId: AccountId,
            accountUserId: SecondAccountOwnerUserId,
            participationType: ParticipationType.Watching,
            notificationsEnabled: false,
            attachedAtUtc: Now);

        await using var ctx2 = CreateContext();
        ctx2.Set<KeepRequestParticipant>().Add(participant);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23503", pgEx.SqlState);
    }

    // -------------------------------------------------------------------------
    // Test 10: Soft-delete filter hides Keep rows
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
