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
    private Guid AccountOwnerAccountUserId { get; set; }
    private Guid SecondAccountId { get; set; }
    private Guid SecondAccountOwnerAccountUserId { get; set; }

    public async Task InitializeAsync()
    {
        await using var ctx = CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();

        // G1 adds FK constraints from Keep entities → accounts. Seed two real accounts.
        (AccountId, AccountOwnerAccountUserId) = await SeedAccountAsync(ctx, "Test Business", "owner@test.example.com");
        (SecondAccountId, SecondAccountOwnerAccountUserId) = await SeedAccountAsync(ctx, "Other Business", "owner2@test.example.com");
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
        ctx.AccountEntitlements.Add(graph.Entitlements);

        // Two-phase save: Account ↔ AccountUser circular FK (ADR-019 / PersistenceProofTests pattern).
        var ownerIdEntry = ctx.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerIdEntry.CurrentValue = null;
        await ctx.SaveChangesAsync();
        ownerIdEntry.CurrentValue = graph.Owner.Id;
        await ctx.SaveChangesAsync();

        return (graph.Account.Id, graph.Owner.Id); // Owner.Id is AccountUser.Id, not User.Id
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
            accountUserId: SecondAccountOwnerAccountUserId,
            participationType: ParticipationType.Responsible,
            notificationsEnabled: true,
            attachedAtUtc: Now);

        await using var ctx2 = CreateContext();
        ctx2.Set<KeepRequestParticipant>().Add(participant);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23503", pgEx.SqlState);
        Assert.Equal("fk_keep_request_participants_keep_requests_account_id_request_", pgEx.ConstraintName);
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
            accountUserId: SecondAccountOwnerAccountUserId,
            participationType: ParticipationType.Watching,
            notificationsEnabled: false,
            attachedAtUtc: Now);

        await using var ctx2 = CreateContext();
        ctx2.Set<KeepRequestParticipant>().Add(participant);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23503", pgEx.SqlState);
        Assert.Equal("fk_keep_request_participants_account_users_account_id_account_", pgEx.ConstraintName);
    }

    // -------------------------------------------------------------------------
    // Tests 13–16: FirstResponseEventId FK (fk_keep_requests_first_response_event)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FirstResponseEvent_and_request_pointer_commit_in_single_SaveChanges()
    {
        // Seed customer + request (customer-origin so ChangeStatus+message triggers first-response).
        var customer = KeepCustomer.Create(AccountId, "Jane", "0412345620");
        var request = KeepRequest.CreateFromCustomerIntake(
            AccountId, customer.Id, "Jane", "0412345620", null,
            "Description", "FREV01", "tok_frev1", Now, 60);

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepCustomer>().Add(customer);
            ctx.Set<KeepRequest>().Add(request);
            await ctx.SaveChangesAsync();
        }

        // Load request (tracked), call domain method — sets FirstResponseEventId = statusEvent.Id.
        Guid committedEventId;
        await using (var ctx = CreateContext())
        {
            var loaded = await ctx.Set<KeepRequest>().FindAsync(request.Id);
            var outcome = loaded!.ChangeStatus(
                KeepRequestStatus.InProgress,
                "Now in progress",
                AccountOwnerAccountUserId,
                "Test Actor",
                Now);
            Assert.True(outcome.IsSuccess);
            var statusEvent = outcome.Value.StatusChangedEvent!;
            committedEventId = statusEvent.Id;
            ctx.Set<KeepRequestEvent>().Add(statusEvent);
            // EF must INSERT statusEvent first, then UPDATE loaded.FirstResponseEventId.
            await ctx.SaveChangesAsync();
        }

        // Verify the pointer was persisted.
        await using var readCtx = CreateContext();
        var saved = await readCtx.Set<KeepRequest>().FindAsync(request.Id);
        Assert.NotNull(saved);
        Assert.Equal(committedEventId, saved.FirstResponseEventId);
    }

    [Fact]
    public async Task FirstResponseEventId_pointing_to_another_requests_event_is_rejected()
    {
        // Seed two requests under the same account; save an event for the second request.
        var c1 = KeepCustomer.Create(AccountId, "Jane", "0412345621");
        var c2 = KeepCustomer.Create(AccountId, "Bob", "0412345622");
        var req1 = KeepRequest.CreateFromCustomerIntake(AccountId, c1.Id, "Jane", "0412345621", null, "Desc", "FREV02", "tok_frev2", Now, 60);
        var req2 = KeepRequest.CreateFromCustomerIntake(AccountId, c2.Id, "Bob", "0412345622", null, "Desc", "FREV03", "tok_frev3", Now, 60);
        var ev2 = KeepRequestEvent.CreateRequestCreated(req2.Id, AccountId, Now);

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepCustomer>().AddRange(c1, c2);
            ctx.Set<KeepRequest>().AddRange(req1, req2);
            ctx.Set<KeepRequestEvent>().Add(ev2);
            await ctx.SaveChangesAsync();
        }

        // Attempt: point req1.FirstResponseEventId to ev2 (which belongs to req2, not req1).
        await using var ctx2 = CreateContext();
        var loaded = await ctx2.Set<KeepRequest>().FindAsync(req1.Id);
        ctx2.Entry(loaded!).Property(r => r.FirstResponseEventId).CurrentValue = ev2.Id;

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23503", pgEx.SqlState);
        Assert.Equal("fk_keep_requests_first_response_event", pgEx.ConstraintName);
    }

    [Fact]
    public async Task FirstResponseEventId_pointing_to_another_accounts_event_is_rejected()
    {
        // Seed req1 under AccountId; seed req2+event under SecondAccountId.
        var c1 = KeepCustomer.Create(AccountId, "Jane", "0412345623");
        var req1 = KeepRequest.CreateFromCustomerIntake(AccountId, c1.Id, "Jane", "0412345623", null, "Desc", "FREV04", "tok_frev4", Now, 60);
        var c2 = KeepCustomer.Create(SecondAccountId, "Bob", "0412345624");
        var req2 = KeepRequest.CreateFromCustomerIntake(SecondAccountId, c2.Id, "Bob", "0412345624", null, "Desc", "FREV05", "tok_frev5", Now, 60);
        var ev2 = KeepRequestEvent.CreateRequestCreated(req2.Id, SecondAccountId, Now);

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepCustomer>().AddRange(c1, c2);
            ctx.Set<KeepRequest>().AddRange(req1, req2);
            ctx.Set<KeepRequestEvent>().Add(ev2);
            await ctx.SaveChangesAsync();
        }

        // Attempt: point req1.FirstResponseEventId to ev2 (AccountId2, wrong account and request).
        await using var ctx2 = CreateContext();
        var loaded = await ctx2.Set<KeepRequest>().FindAsync(req1.Id);
        ctx2.Entry(loaded!).Property(r => r.FirstResponseEventId).CurrentValue = ev2.Id;

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23503", pgEx.SqlState);
        Assert.Equal("fk_keep_requests_first_response_event", pgEx.ConstraintName);
    }

    [Fact]
    public async Task FirstResponseEventId_pointing_to_nonexistent_event_is_rejected()
    {
        var customer = KeepCustomer.Create(AccountId, "Jane", "0412345625");
        var request = KeepRequest.CreateFromCustomerIntake(
            AccountId, customer.Id, "Jane", "0412345625", null,
            "Desc", "FREV06", "tok_frev6", Now, 60);

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepCustomer>().Add(customer);
            ctx.Set<KeepRequest>().Add(request);
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = CreateContext();
        var loaded = await ctx2.Set<KeepRequest>().FindAsync(request.Id);
        ctx2.Entry(loaded!).Property(r => r.FirstResponseEventId).CurrentValue = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23503", pgEx.SqlState);
        Assert.Equal("fk_keep_requests_first_response_event", pgEx.ConstraintName);
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

    // -------------------------------------------------------------------------
    // G3b persistence commit proof — KeepIntakeCommitHelper via KeepBusinessRequestPersistence
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BusinessCommit_named_page_token_collision_returns_UniqueTokenCollision_and_tracker_is_clean()
    {
        // Arrange: seed a request that owns the token.
        var existingCustomer = KeepCustomer.Create(AccountId, "Existing", "0400111001");
        const string conflictingToken = "biz-tok-collision-test-001";

        await using (var ctx = CreateContext())
        {
            var r = KeepRequest.CreateByBusiness(
                AccountId, existingCustomer.Id, "Existing", "0400111001", null,
                "Prior request", "BIZREF001", conflictingToken, Now, KeepRequestSource.Phone);
            ctx.Set<KeepCustomer>().Add(existingCustomer);
            ctx.Set<KeepRequest>().Add(r);
            ctx.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(r.Id, AccountId, AccountOwnerAccountUserId, "Owner", Now));
            await ctx.SaveChangesAsync();
        }

        // Act: try to commit a new request with the same page token.
        await using var bizCtx = CreateContext();
        var sut = new OpHalo.Keep.Infrastructure.Persistence.KeepBusinessRequestPersistence(bizCtx);

        var newCustomer = KeepCustomer.Create(AccountId, "Jane", "0400111002");
        var newRequest = KeepRequest.CreateByBusiness(
            AccountId, newCustomer.Id, "Jane", "0400111002", null,
            "New request", "BIZREF002", conflictingToken, Now, KeepRequestSource.Phone);
        var newEvent = KeepRequestEvent.CreateRequestCreated(newRequest.Id, AccountId, AccountOwnerAccountUserId, "Owner", Now);

        var result = await sut.CommitBusinessRequestAsync(newCustomer, newRequest, newEvent, CancellationToken.None);

        // Assert outcome and tracker clean-up (so a retry can reuse the same context).
        Assert.Equal(OpHalo.Keep.Application.Requests.BusinessRequestCommitResult.UniqueTokenCollision, result);
        Assert.Equal(EntityState.Detached, bizCtx.Entry(newRequest).State);
        Assert.Equal(EntityState.Detached, bizCtx.Entry(newEvent).State);
        Assert.Equal(EntityState.Detached, bizCtx.Entry(newCustomer).State);
    }

    [Fact]
    public async Task BusinessCommit_named_reference_code_collision_returns_UniqueTokenCollision()
    {
        var existingCustomer = KeepCustomer.Create(AccountId, "Existing", "0400111003");
        const string conflictingRef = "SAME-BIZ-REF";

        await using (var ctx = CreateContext())
        {
            var r = KeepRequest.CreateByBusiness(
                AccountId, existingCustomer.Id, "Existing", "0400111003", null,
                "Prior request", conflictingRef, "biz-tok-ref-001", Now, KeepRequestSource.Phone);
            ctx.Set<KeepCustomer>().Add(existingCustomer);
            ctx.Set<KeepRequest>().Add(r);
            ctx.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(r.Id, AccountId, AccountOwnerAccountUserId, "Owner", Now));
            await ctx.SaveChangesAsync();
        }

        await using var bizCtx = CreateContext();
        var sut = new OpHalo.Keep.Infrastructure.Persistence.KeepBusinessRequestPersistence(bizCtx);

        var newCustomer = KeepCustomer.Create(AccountId, "Jane", "0400111004");
        var newRequest = KeepRequest.CreateByBusiness(
            AccountId, newCustomer.Id, "Jane", "0400111004", null,
            "Collision request", conflictingRef, "biz-tok-ref-002", Now, KeepRequestSource.Phone);
        var newEvent = KeepRequestEvent.CreateRequestCreated(newRequest.Id, AccountId, AccountOwnerAccountUserId, "Owner", Now);

        var result = await sut.CommitBusinessRequestAsync(newCustomer, newRequest, newEvent, CancellationToken.None);

        Assert.Equal(OpHalo.Keep.Application.Requests.BusinessRequestCommitResult.UniqueTokenCollision, result);
    }

    [Fact]
    public async Task BusinessCommit_canonical_phone_collision_returns_CustomerCanonicalPhoneCollision()
    {
        // Seed the winning customer first so the constraint is in place.
        var winningCustomer = KeepCustomer.Create(AccountId, "Winning Jane", "0400111005");

        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepCustomer>().Add(winningCustomer);
            await ctx.SaveChangesAsync();
        }

        await using var bizCtx = CreateContext();
        var sut = new OpHalo.Keep.Infrastructure.Persistence.KeepBusinessRequestPersistence(bizCtx);

        // Different KeepCustomer entity, same canonical phone — simulates race.
        var racingCustomer = KeepCustomer.Create(AccountId, "Racing Jane", "04001 1100 5"); // same digits
        var request = KeepRequest.CreateByBusiness(
            AccountId, racingCustomer.Id, "Racing Jane", "04001 1100 5", null,
            "Race request", "BIZRACE001", "biz-tok-race-001", Now, KeepRequestSource.Phone);
        var ev = KeepRequestEvent.CreateRequestCreated(request.Id, AccountId, AccountOwnerAccountUserId, "Owner", Now);

        var result = await sut.CommitBusinessRequestAsync(racingCustomer, request, ev, CancellationToken.None);

        Assert.Equal(OpHalo.Keep.Application.Requests.BusinessRequestCommitResult.CustomerCanonicalPhoneCollision, result);
        Assert.Equal(EntityState.Detached, bizCtx.Entry(request).State);
        Assert.Equal(EntityState.Detached, bizCtx.Entry(ev).State);
        Assert.Equal(EntityState.Detached, bizCtx.Entry(racingCustomer).State);
    }

    [Fact]
    public async Task BusinessCommit_unrelated_constraint_violation_propagates()
    {
        // Use a cross-account FK violation (23503) — not caught by the helper.
        await using var bizCtx = CreateContext();
        var sut = new OpHalo.Keep.Infrastructure.Persistence.KeepBusinessRequestPersistence(bizCtx);

        var unknownCustomerId = Guid.NewGuid(); // no row for this customer
        var fakeCustomer = KeepCustomer.Create(AccountId, "Ghost", "0400111006");
        // Manually set customer as tracked but with an ID that doesn't match the request's customerId.
        // Instead, create a request referencing a non-existent customer from a different account.
        var requestWithBadCustomer = KeepRequest.CreateByBusiness(
            AccountId, unknownCustomerId, "Ghost", "0400111006", null,
            "Bad request", "BIZBAD001", "biz-tok-bad-001", Now, KeepRequestSource.Phone);
        var ev = KeepRequestEvent.CreateRequestCreated(requestWithBadCustomer.Id, AccountId, AccountOwnerAccountUserId, "Owner", Now);

        // CommitAsync adds the customer entity; skip that by attaching a tracked version.
        // The request references an unknown customerId — EF will attempt to insert and hit FK 23503.
        // Pass a customer whose EF-tracked state is Detached so the helper treats it as new,
        // but give it a different Id so the FK still violates.
        var ex = await Assert.ThrowsAsync<DbUpdateException>(
            () => sut.CommitBusinessRequestAsync(fakeCustomer, requestWithBadCustomer, ev, CancellationToken.None));

        var pgEx = ex.InnerException as Npgsql.NpgsqlException;
        Assert.NotNull(pgEx);
    }

    [Fact]
    public async Task BusinessCommit_tracker_is_clean_after_phone_collision_allowing_retry()
    {
        // Seed winning customer.
        var winner = KeepCustomer.Create(AccountId, "Winner", "0400111007");
        await using (var ctx = CreateContext())
        {
            ctx.Set<KeepCustomer>().Add(winner);
            await ctx.SaveChangesAsync();
        }

        await using var bizCtx = CreateContext();
        var sut = new OpHalo.Keep.Infrastructure.Persistence.KeepBusinessRequestPersistence(bizCtx);

        // Attempt 1: phone collision.
        var racer = KeepCustomer.Create(AccountId, "Racer", "04001 1100 7");
        var r1 = KeepRequest.CreateByBusiness(AccountId, racer.Id, "Racer", "04001 1100 7", null, "Desc", "RETRY001", "biz-tok-retry-001", Now, KeepRequestSource.Phone);
        var e1 = KeepRequestEvent.CreateRequestCreated(r1.Id, AccountId, AccountOwnerAccountUserId, "Owner", Now);
        var outcome1 = await sut.CommitBusinessRequestAsync(racer, r1, e1, CancellationToken.None);
        Assert.Equal(OpHalo.Keep.Application.Requests.BusinessRequestCommitResult.CustomerCanonicalPhoneCollision, outcome1);

        // Attempt 2 after recovery: reuse the winning customer — should commit.
        var winnerRefresh = await bizCtx.Set<KeepCustomer>()
            .FirstAsync(c => c.AccountId == AccountId && c.CanonicalPhone == winner.CanonicalPhone);
        var r2 = KeepRequest.CreateByBusiness(AccountId, winnerRefresh.Id, "Winner", "0400111007", null, "Retry desc", "RETRY002", "biz-tok-retry-002", Now, KeepRequestSource.Phone);
        var e2 = KeepRequestEvent.CreateRequestCreated(r2.Id, AccountId, AccountOwnerAccountUserId, "Owner", Now);
        var outcome2 = await sut.CommitBusinessRequestAsync(winnerRefresh, r2, e2, CancellationToken.None);
        Assert.Equal(OpHalo.Keep.Application.Requests.BusinessRequestCommitResult.Committed, outcome2);

        // Confirm DB state: one customer, one committed request.
        await using var readCtx = CreateContext();
        var customers = await readCtx.Set<KeepCustomer>()
            .Where(c => c.AccountId == AccountId && c.CanonicalPhone == winner.CanonicalPhone)
            .ToListAsync();
        Assert.Single(customers);
        var requests = await readCtx.Set<KeepRequest>()
            .Where(r => r.AccountId == AccountId && r.KeepCustomerId == customers[0].Id)
            .ToListAsync();
        Assert.Single(requests);
    }

    // -------------------------------------------------------------------------
    // GAP-025 — FindMostRecentRequestByCustomerPhoneAsync legacy-phone fallback
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FindMostRecentRequestByCustomerPhone_finds_match_behind_many_newer_decoys_sharing_trailing_digits()
    {
        // Regression for a prior Take(50)-after-Contains(last4) implementation: seed 60 newer,
        // same-account decoy requests whose CustomerPhone merely contains the same trailing 4
        // digits (but isn't the same number), so a candidate-capped query would exclude the real
        // match entirely. The real match is the oldest row.
        const string targetCanonical = "5551234999";
        await using (var seedCtx = CreateContext())
        {
            // KeepRequest.KeepCustomerId FKs to a real KeepCustomer row — the fallback under test
            // reads only KeepRequest.CustomerPhone, so a single shared customer row is enough.
            var customer = KeepCustomer.Create(AccountId, "Shared Customer", "5550000000");
            seedCtx.Set<KeepCustomer>().Add(customer);
            await seedCtx.SaveChangesAsync();

            var target = KeepRequest.CreateByBusiness(
                AccountId, customer.Id, "Legacy Larry", "(555) 123-4999", "larry@example.com",
                "Old furnace job", "GAP025TARGET", "gap025-target-tok", Now, KeepRequestSource.Phone);
            seedCtx.Set<KeepRequest>().Add(target);
            await seedCtx.SaveChangesAsync();

            for (var i = 0; i < 60; i++)
            {
                // Shares the trailing "4999" substring with the target but is a different number,
                // and is seeded strictly newer so a recency-ordered Take(50) would rank ahead of it.
                var decoy = KeepRequest.CreateByBusiness(
                    AccountId, customer.Id, $"Decoy {i}", $"555-{i:D3}-4999", null,
                    "Unrelated job", $"GAP025DECOY{i:D3}", $"gap025-decoy-tok-{i:D3}",
                    Now.AddMinutes(i + 1), KeepRequestSource.Phone);
                seedCtx.Set<KeepRequest>().Add(decoy);
            }
            await seedCtx.SaveChangesAsync();
        }

        await using var readCtx = CreateContext();
        var sut = new OpHalo.Keep.Infrastructure.Persistence.KeepBusinessRequestPersistence(readCtx);

        var match = await sut.FindMostRecentRequestByCustomerPhoneAsync(AccountId, targetCanonical, CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal("GAP025TARGET", match!.ReferenceCode);
        Assert.Equal("Legacy Larry", match.CustomerName);
    }

    [Fact]
    public async Task FindMostRecentRequestByCustomerPhone_matches_leading_country_code_and_is_account_scoped()
    {
        const string canonical = "5559998888";

        await using (var seedCtx = CreateContext())
        {
            var ownedCustomer = KeepCustomer.Create(AccountId, "Owned Olivia", "5550000001");
            var crossCustomer = KeepCustomer.Create(SecondAccountId, "Cross Cathy", "5550000002");
            seedCtx.Set<KeepCustomer>().Add(ownedCustomer);
            seedCtx.Set<KeepCustomer>().Add(crossCustomer);
            await seedCtx.SaveChangesAsync();

            // Own account, stored with a leading "1" country code — must still match.
            var owned = KeepRequest.CreateByBusiness(
                AccountId, ownedCustomer.Id, "Owned Olivia", "+1 (555) 999-8888", null,
                "Job", "GAP025CC", "gap025-cc-tok", Now, KeepRequestSource.Phone);
            seedCtx.Set<KeepRequest>().Add(owned);

            // Other account, same phone — must not leak across accounts.
            var crossAccount = KeepRequest.CreateByBusiness(
                SecondAccountId, crossCustomer.Id, "Cross Cathy", "555-999-8888", null,
                "Job", "GAP025XACCT", "gap025-xacct-tok", Now.AddMinutes(5), KeepRequestSource.Phone);
            seedCtx.Set<KeepRequest>().Add(crossAccount);
            await seedCtx.SaveChangesAsync();
        }

        await using var readCtx = CreateContext();
        var sut = new OpHalo.Keep.Infrastructure.Persistence.KeepBusinessRequestPersistence(readCtx);

        var match = await sut.FindMostRecentRequestByCustomerPhoneAsync(AccountId, canonical, CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal("GAP025CC", match!.ReferenceCode);
    }

    [Fact]
    public async Task FindMostRecentRequestByCustomerPhone_matches_a_separator_PhoneNormalizer_strips_that_the_write_path_never_produces()
    {
        // Regression for a prior chained string.Replace(" ","").Replace("-","")... draft that only
        // stripped the punctuation KeepRequestInputValidator happens to allow on write. PhoneNormalizer
        // strips *every* non-ASCII-digit character, so a legacy row using an unanticipated separator
        // (e.g. from a since-removed validation rule, an import, or a manual DB edit) must still match.
        const string canonical = "5551234999";

        await using (var seedCtx = CreateContext())
        {
            var customer = KeepCustomer.Create(AccountId, "Slash Sam", "5550000003");
            seedCtx.Set<KeepCustomer>().Add(customer);
            await seedCtx.SaveChangesAsync();

            var legacy = KeepRequest.CreateByBusiness(
                AccountId, customer.Id, "Slash Sam", "555/123/4999", null,
                "Job", "GAP025SLASH", "gap025-slash-tok", Now, KeepRequestSource.Phone);
            seedCtx.Set<KeepRequest>().Add(legacy);
            await seedCtx.SaveChangesAsync();
        }

        await using var readCtx = CreateContext();
        var sut = new OpHalo.Keep.Infrastructure.Persistence.KeepBusinessRequestPersistence(readCtx);

        var match = await sut.FindMostRecentRequestByCustomerPhoneAsync(AccountId, canonical, CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal("GAP025SLASH", match!.ReferenceCode);
    }

    [Fact]
    public async Task FindMostRecentRequestByCustomerPhone_returns_null_when_no_request_matches()
    {
        await using var readCtx = CreateContext();
        var sut = new OpHalo.Keep.Infrastructure.Persistence.KeepBusinessRequestPersistence(readCtx);

        var match = await sut.FindMostRecentRequestByCustomerPhoneAsync(AccountId, "5550001111", CancellationToken.None);

        Assert.Null(match);
    }

    // =========================================================================
    // G5b — Optimistic-concurrency race via two DbContexts
    // =========================================================================

    [Fact]
    public async Task OperatePersistence_ConcurrentCommits_FirstWins_SecondReturnsConflict()
    {
        // Seed a request using the test infrastructure directly.
        var customer = KeepCustomer.Create(AccountId, "Race Customer", "0499888001");
        Guid requestId;
        Guid seedVersion;

        await using (var seedCtx = CreateContext())
        {
            seedCtx.Set<KeepCustomer>().Add(customer);
            await seedCtx.SaveChangesAsync();

            var request = KeepRequest.CreateFromCustomerIntake(
                AccountId, customer.Id,
                "Race Customer", "0499888001", null,
                "Concurrency race job", "RACE001", "race-tok-001", Now, 60);
            seedCtx.Set<KeepRequest>().Add(request);
            seedCtx.Set<KeepRequestEvent>().Add(
                KeepRequestEvent.CreateRequestCreated(request.Id, AccountId, Now));
            await seedCtx.SaveChangesAsync();

            requestId = request.Id;
            seedVersion = request.ConcurrencyVersion;
        }

        // Load the same row into two independent contexts simultaneously.
        await using var ctx1 = CreateContext();
        await using var ctx2 = CreateContext();

        var req1 = await ctx1.Set<KeepRequest>().SingleAsync(r => r.Id == requestId);
        var req2 = await ctx2.Set<KeepRequest>().SingleAsync(r => r.Id == requestId);

        // Both contexts see the same seed version.
        Assert.Equal(seedVersion, req1.ConcurrencyVersion);
        Assert.Equal(seedVersion, req2.ConcurrencyVersion);

        // First commit: should succeed.
        var sut1 = new OpHalo.Keep.Infrastructure.Persistence.EfKeepRequestOperatePersistence(ctx1);
        var event1 = KeepRequestEvent.CreateInternalNote(
            requestId, AccountId, AccountOwnerAccountUserId, "owner@test.example.com", "First note", Now);
        var result1 = await sut1.CommitAsync(req1, event1, CancellationToken.None);
        Assert.Equal(OpHalo.Keep.Application.Requests.KeepRequestCommitResult.Committed, result1);

        var winningVersion = req1.ConcurrencyVersion;
        Assert.NotEqual(seedVersion, winningVersion);

        // Second commit: row was already rotated — version mismatch must return Conflict.
        var sut2 = new OpHalo.Keep.Infrastructure.Persistence.EfKeepRequestOperatePersistence(ctx2);
        var event2 = KeepRequestEvent.CreateInternalNote(
            requestId, AccountId, AccountOwnerAccountUserId, "owner@test.example.com", "Losing note", Now);
        var result2 = await sut2.CommitAsync(req2, event2, CancellationToken.None);
        Assert.Equal(OpHalo.Keep.Application.Requests.KeepRequestCommitResult.Conflict, result2);

        // Confirm the winning version and event persisted; the losing event did not.
        await using var verifyCtx = CreateContext();
        var persisted = await verifyCtx.Set<KeepRequest>().SingleAsync(r => r.Id == requestId);
        Assert.Equal(winningVersion, persisted.ConcurrencyVersion);

        var events = await verifyCtx.Set<KeepRequestEvent>()
            .Where(e => e.RequestId == requestId)
            .ToListAsync();
        Assert.Single(events, e => e.Content == "First note");
        Assert.DoesNotContain(events, e => e.Content == "Losing note");
    }

    [Fact]
    public async Task ParticipationCommit_FirstWriterWins_ParticipantAndEventRolledBack()
    {
        // Seed one request, one RequestCreated event, and one active Watching participant.
        Guid requestId;
        Guid seedVersion;
        Guid participantId;

        await using (var seedCtx = CreateContext())
        {
            var customer = KeepCustomer.Create(AccountId, "Race Customer", "0499777001");
            seedCtx.Set<KeepCustomer>().Add(customer);
            await seedCtx.SaveChangesAsync();

            var request = KeepRequest.CreateFromCustomerIntake(
                AccountId, customer.Id,
                "Race Customer", "0499777001", null,
                "Participation race job", "PRACE001", "prace-tok-001", Now, 60);
            seedCtx.Set<KeepRequest>().Add(request);
            seedCtx.Set<KeepRequestEvent>().Add(
                KeepRequestEvent.CreateRequestCreated(request.Id, AccountId, AccountOwnerAccountUserId, "Owner", Now));

            var participant = KeepRequestParticipant.Create(
                request.Id, AccountId, AccountOwnerAccountUserId,
                ParticipationType.Watching, notificationsEnabled: true, Now);
            seedCtx.Set<KeepRequestParticipant>().Add(participant);
            await seedCtx.SaveChangesAsync();

            requestId     = request.Id;
            seedVersion   = request.ConcurrencyVersion;
            participantId = participant.Id;
        }

        // Load the same request and participant into two independent contexts.
        await using var ctx1 = CreateContext();
        await using var ctx2 = CreateContext();

        var req1 = await ctx1.Set<KeepRequest>().SingleAsync(r => r.Id == requestId);
        var req2 = await ctx2.Set<KeepRequest>().SingleAsync(r => r.Id == requestId);

        Assert.Equal(seedVersion, req1.ConcurrencyVersion);
        Assert.Equal(seedVersion, req2.ConcurrencyVersion);

        // Winner: mute the participant (set NotificationsEnabled=false), emit Muted event.
        var trackedParticipant1 = await ctx1.Set<KeepRequestParticipant>().SingleAsync(p => p.Id == participantId);
        trackedParticipant1.SetNotificationsEnabled(false);
        var winnerEvent = KeepRequestEvent.CreateParticipationChanged(
            requestId, AccountId,
            actorAccountUserId: AccountOwnerAccountUserId, actorDisplayName: "Owner",
            participationAction: ParticipationAction.Muted,
            targetAccountUserId: AccountOwnerAccountUserId, targetDisplayName: "Owner",
            previousResponsibleAccountUserId: null, internalNote: null,
            notificationIntentKind: null, notificationIntendedRecipientAccountUserId: null,
            occurredAtUtc: Now);

        // Loser: call Detach() so EF tracks a real participant-row mutation to roll back.
        var trackedParticipant2 = await ctx2.Set<KeepRequestParticipant>().SingleAsync(p => p.Id == participantId);
        trackedParticipant2.Detach(Now.AddSeconds(1));
        var loserEvent = KeepRequestEvent.CreateParticipationChanged(
            requestId, AccountId,
            actorAccountUserId: AccountOwnerAccountUserId, actorDisplayName: "Owner",
            participationAction: ParticipationAction.SelfUnwatched,
            targetAccountUserId: AccountOwnerAccountUserId, targetDisplayName: "Owner",
            previousResponsibleAccountUserId: null, internalNote: null,
            notificationIntentKind: null, notificationIntendedRecipientAccountUserId: null,
            occurredAtUtc: Now);

        var sut1 = new OpHalo.Keep.Infrastructure.Persistence.EfKeepRequestOperatePersistence(ctx1);
        var result1 = await sut1.CommitParticipationAsync(
            req1, new List<KeepRequestParticipant>(), winnerEvent, CancellationToken.None);
        Assert.Equal(OpHalo.Keep.Application.Requests.KeepRequestCommitResult.Committed, result1);

        var winningVersion = req1.ConcurrencyVersion;
        Assert.NotEqual(seedVersion, winningVersion);

        var sut2 = new OpHalo.Keep.Infrastructure.Persistence.EfKeepRequestOperatePersistence(ctx2);
        var result2 = await sut2.CommitParticipationAsync(
            req2, new List<KeepRequestParticipant>(), loserEvent, CancellationToken.None);
        Assert.Equal(OpHalo.Keep.Application.Requests.KeepRequestCommitResult.Conflict, result2);

        // Verify: request version equals the winner; participant is still Watching with
        // NotificationsEnabled=false; winner Muted event exists; losing SelfUnwatched event does not.
        await using var verifyCtx = CreateContext();
        var persistedRequest = await verifyCtx.Set<KeepRequest>().SingleAsync(r => r.Id == requestId);
        Assert.Equal(winningVersion, persistedRequest.ConcurrencyVersion);

        var persistedParticipant = await verifyCtx.Set<KeepRequestParticipant>()
            .SingleAsync(p => p.Id == participantId);
        Assert.Null(persistedParticipant.DetachedAtUtc);
        Assert.Equal(ParticipationType.Watching, persistedParticipant.ParticipationType);
        Assert.False(persistedParticipant.NotificationsEnabled);

        var persistedEvents = await verifyCtx.Set<KeepRequestEvent>()
            .Where(e => e.RequestId == requestId)
            .ToListAsync();
        Assert.Contains(persistedEvents, e => e.Id == winnerEvent.Id);
        Assert.DoesNotContain(persistedEvents, e => e.Id == loserEvent.Id);
    }

    // =========================================================================
    // G5d — Operator/customer cross-path race
    // =========================================================================

    [Fact]
    public async Task OperatorAndCustomerRace_OperatorWins_CustomerEventAndStateRolledBack()
    {
        // Seed one customer-origin request in Received state with its RequestCreated event.
        Guid requestId;
        Guid seedVersion;
        Guid requestCreatedEventId;

        await using (var seedCtx = CreateContext())
        {
            var customer = KeepCustomer.Create(AccountId, "Race Customer", "0499666001");
            seedCtx.Set<KeepCustomer>().Add(customer);
            await seedCtx.SaveChangesAsync();

            var request = KeepRequest.CreateFromCustomerIntake(
                AccountId, customer.Id,
                "Race Customer", "0499666001", null,
                "Cross-path race job", "XRACE001", "xrace-tok-001", Now, 60);
            var createdEvent = KeepRequestEvent.CreateRequestCreated(request.Id, AccountId, Now);
            seedCtx.Set<KeepRequest>().Add(request);
            seedCtx.Set<KeepRequestEvent>().Add(createdEvent);
            await seedCtx.SaveChangesAsync();

            requestId            = request.Id;
            seedVersion          = request.ConcurrencyVersion;
            requestCreatedEventId = createdEvent.Id;
        }

        // Load the same request into two independent contexts.
        await using var ctx1 = CreateContext();
        await using var ctx2 = CreateContext();

        var req1 = await ctx1.Set<KeepRequest>().SingleAsync(r => r.Id == requestId);
        var req2 = await ctx2.Set<KeepRequest>().SingleAsync(r => r.Id == requestId);

        Assert.Equal(seedVersion, req1.ConcurrencyVersion);
        Assert.Equal(seedVersion, req2.ConcurrencyVersion);

        // Winning operator path: business update on context 1.
        var winnerResult = req1.AddBusinessUpdate(
            "Operator winning update", AccountOwnerAccountUserId, "Owner", Now.AddMinutes(1));
        Assert.True(winnerResult.IsSuccess);
        var winnerEvent = winnerResult.Value!;

        var sut1 = new OpHalo.Keep.Infrastructure.Persistence.EfKeepRequestOperatePersistence(ctx1);
        var commitResult1 = await sut1.CommitAsync(req1, winnerEvent, CancellationToken.None);
        Assert.Equal(OpHalo.Keep.Application.Requests.KeepRequestCommitResult.Committed, commitResult1);

        var winnerVersion = req1.ConcurrencyVersion;
        Assert.NotEqual(seedVersion, winnerVersion);

        // Losing customer path: customer message on context 2.
        var loserResult = req2.AddCustomerMessage(
            MessageIntent.GeneralMessage, "Customer losing message", 60, 240, 60, Now.AddMinutes(2));
        Assert.True(loserResult.IsSuccess);
        var loserEvent = loserResult.Value!;

        var sut2 = new OpHalo.Keep.Infrastructure.Persistence.EfKeepCustomerWritePersistence(ctx2);
        var commitResult2 = await sut2.CommitAsync(req2, loserEvent, CancellationToken.None);
        Assert.Equal(OpHalo.Keep.Application.Requests.KeepRequestCommitResult.Conflict, commitResult2);

        // Fresh-context assertions: request state matches the operator winner only.
        await using var verifyCtx = CreateContext();
        var persisted = await verifyCtx.Set<KeepRequest>().SingleAsync(r => r.Id == requestId);

        Assert.Equal(winnerVersion, persisted.ConcurrencyVersion);
        Assert.Equal(Now.AddMinutes(1), persisted.LastBusinessActivityAt);
        // ADR-451: a page-only business update never sets first response.
        Assert.Null(persisted.FirstRespondedAtUtc);
        Assert.Null(persisted.FirstResponderAccountUserId);
        Assert.Null(persisted.FirstResponseEventId);

        // Losing customer mutation rolled back: LastCustomerActivityAt stays at intake seed.
        Assert.Equal(Now, persisted.LastCustomerActivityAt);

        // Attention reflects the winning business response, not the losing customer message.
        Assert.Equal(AttentionLevel.None, persisted.AttentionLevel);
        Assert.Equal(WaitingDirection.None, persisted.WaitingDirection);
        Assert.Null(persisted.AttentionReason);
        Assert.Null(persisted.AttentionSinceUtc);
        Assert.Null(persisted.NextAttentionAtUtc);

        // Event rollback: RequestCreated + winner event exist; loser event does not.
        var persistedEvents = await verifyCtx.Set<KeepRequestEvent>()
            .Where(e => e.RequestId == requestId)
            .ToListAsync();
        Assert.Contains(persistedEvents, e => e.Id == requestCreatedEventId);
        Assert.Contains(persistedEvents, e => e.Id == winnerEvent.Id);
        Assert.DoesNotContain(persistedEvents, e => e.Id == loserEvent.Id);
    }

    // --- GAP-007b: batch list-preview precedence, redaction, and bounded truncation ---

    [Fact]
    public async Task GetLatestPreviewEventsAsync_customer_message_beats_more_recent_business_update_and_contact()
    {
        Guid requestId;
        await using (var seedCtx = CreateContext())
        {
            var customer = KeepCustomer.Create(AccountId, "Preview Customer", "0499777001");
            seedCtx.Set<KeepCustomer>().Add(customer);
            await seedCtx.SaveChangesAsync();

            var request = KeepRequest.CreateFromCustomerIntake(
                AccountId, customer.Id, "Preview Customer", "0499777001", null,
                "Original description", "PREV0001", "prev-tok-001", Now, 60);
            seedCtx.Set<KeepRequest>().Add(request);
            seedCtx.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(request.Id, AccountId, Now));
            await seedCtx.SaveChangesAsync();

            // Oldest: customer message. Newer: business update and external contact — both
            // more recent, but precedence must still pick the customer message.
            var customerMsg = request.AddCustomerMessage(
                MessageIntent.GeneralMessage, "Can you come Tuesday?", 60, 240, 60, Now.AddMinutes(1));
            Assert.True(customerMsg.IsSuccess);
            seedCtx.Set<KeepRequestEvent>().Add(customerMsg.Value);
            await seedCtx.SaveChangesAsync();

            var businessUpdate = request.AddBusinessUpdate(
                "We'll be there Wednesday instead", AccountOwnerAccountUserId, "Owner", Now.AddMinutes(2));
            Assert.True(businessUpdate.IsSuccess);
            seedCtx.Set<KeepRequestEvent>().Add(businessUpdate.Value);
            await seedCtx.SaveChangesAsync();

            var outboundContact = request.LogOutboundExternalContact(
                CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer, false, null,
                AccountOwnerAccountUserId, "Owner", Now.AddMinutes(3));
            Assert.True(outboundContact.IsSuccess);
            seedCtx.Set<KeepRequestEvent>().Add(outboundContact.Value);
            await seedCtx.SaveChangesAsync();

            requestId = request.Id;
        }

        await using var ctx = CreateContext();
        var sut = new OpHalo.Keep.Infrastructure.Persistence.KeepRequestListPersistence(
            ctx, new OpHalo.Foundation.Infrastructure.Services.SystemClock());

        var previews = await sut.GetLatestPreviewEventsAsync([requestId], CancellationToken.None);

        var preview = previews[requestId];
        Assert.Equal("customer_message", preview.PreviewSource);
        Assert.Equal("Can you come Tuesday?", preview.PreviewText);
        Assert.False(preview.PreviewTruncated);
        Assert.Equal(Now.AddMinutes(1), preview.PreviewAtUtc);
    }

    [Fact]
    public async Task GetLatestPreviewEventsAsync_falls_through_tiers_and_redacts_internal_and_bounds_text()
    {
        Guid requestId;
        await using (var seedCtx = CreateContext())
        {
            var customer = KeepCustomer.Create(AccountId, "Preview Customer 2", "0499777002");
            seedCtx.Set<KeepCustomer>().Add(customer);
            await seedCtx.SaveChangesAsync();

            var request = KeepRequest.CreateFromCustomerIntake(
                AccountId, customer.Id, "Preview Customer 2", "0499777002", null,
                "Original description two", "PREV0002", "prev-tok-002", Now, 60);
            seedCtx.Set<KeepRequest>().Add(request);
            seedCtx.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(request.Id, AccountId, Now));
            await seedCtx.SaveChangesAsync();

            // Internal note must never surface as a preview, even though it's the latest event.
            var internalNote = request.AddInternalNote(
                "SSN 000-00-0000 do not share with customer", AccountOwnerAccountUserId, "Owner", Now.AddMinutes(1));
            Assert.True(internalNote.IsSuccess);
            seedCtx.Set<KeepRequestEvent>().Add(internalNote.Value);
            await seedCtx.SaveChangesAsync();

            // No customer message, no business update: external contact tier wins, label only.
            var outboundContact = request.LogOutboundExternalContact(
                CommunicationChannel.Phone, ExternalContactOutcome.SpokeWithCustomer, false, null,
                AccountOwnerAccountUserId, "Owner", Now.AddMinutes(2));
            Assert.True(outboundContact.IsSuccess);
            seedCtx.Set<KeepRequestEvent>().Add(outboundContact.Value);
            await seedCtx.SaveChangesAsync();

            requestId = request.Id;
        }

        await using var ctx = CreateContext();
        var sut = new OpHalo.Keep.Infrastructure.Persistence.KeepRequestListPersistence(
            ctx, new OpHalo.Foundation.Infrastructure.Services.SystemClock());

        var previews = await sut.GetLatestPreviewEventsAsync([requestId], CancellationToken.None);

        var preview = previews[requestId];
        Assert.Equal("external_contact", preview.PreviewSource);
        Assert.Equal("Called customer", preview.PreviewText);
        Assert.DoesNotContain("000-00-0000", preview.PreviewText);
        Assert.Equal(Now.AddMinutes(2), preview.PreviewAtUtc);
    }

    [Fact]
    public async Task GetLatestPreviewEventsAsync_bounds_message_text_server_side_and_marks_truncated()
    {
        Guid requestId;
        var longMessage = new string('a', 4000);

        await using (var seedCtx = CreateContext())
        {
            var customer = KeepCustomer.Create(AccountId, "Preview Customer 3", "0499777003");
            seedCtx.Set<KeepCustomer>().Add(customer);
            await seedCtx.SaveChangesAsync();

            var request = KeepRequest.CreateFromCustomerIntake(
                AccountId, customer.Id, "Preview Customer 3", "0499777003", null,
                "Original description three", "PREV0003", "prev-tok-003", Now, 60);
            seedCtx.Set<KeepRequest>().Add(request);
            seedCtx.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(request.Id, AccountId, Now));
            await seedCtx.SaveChangesAsync();

            var customerMsg = request.AddCustomerMessage(
                MessageIntent.GeneralMessage, longMessage, 60, 240, 60, Now.AddMinutes(1));
            Assert.True(customerMsg.IsSuccess);
            seedCtx.Set<KeepRequestEvent>().Add(customerMsg.Value);
            await seedCtx.SaveChangesAsync();

            requestId = request.Id;
        }

        await using var ctx = CreateContext();
        var sut = new OpHalo.Keep.Infrastructure.Persistence.KeepRequestListPersistence(
            ctx, new OpHalo.Foundation.Infrastructure.Services.SystemClock());

        var previews = await sut.GetLatestPreviewEventsAsync([requestId], CancellationToken.None);

        var preview = previews[requestId];
        Assert.Equal("customer_message", preview.PreviewSource);
        Assert.True(preview.PreviewTruncated);
        // Server-bounded to exactly 160 chars: never the full 4000-char body, regardless of
        // how the client renders it.
        Assert.Equal(160, preview.PreviewText!.Length);
    }

    [Fact]
    public async Task GetLatestPreviewEventsAsync_business_update_beats_newer_external_contact_when_no_customer_message()
    {
        Guid requestId;
        await using (var seedCtx = CreateContext())
        {
            var customer = KeepCustomer.Create(AccountId, "Preview Customer 4", "0499777004");
            seedCtx.Set<KeepCustomer>().Add(customer);
            await seedCtx.SaveChangesAsync();

            var request = KeepRequest.CreateFromCustomerIntake(
                AccountId, customer.Id, "Preview Customer 4", "0499777004", null,
                "Original description four", "PREV0004", "prev-tok-004", Now, 60);
            seedCtx.Set<KeepRequest>().Add(request);
            seedCtx.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(request.Id, AccountId, Now));
            await seedCtx.SaveChangesAsync();

            // No customer message at all. Business update is older; external contact is newer —
            // precedence must still pick the business update over the more recent contact log.
            var businessUpdate = request.AddBusinessUpdate(
                "We'll swing by Thursday morning", AccountOwnerAccountUserId, "Owner", Now.AddMinutes(1));
            Assert.True(businessUpdate.IsSuccess);
            seedCtx.Set<KeepRequestEvent>().Add(businessUpdate.Value);
            await seedCtx.SaveChangesAsync();

            var outboundContact = request.LogOutboundExternalContact(
                CommunicationChannel.Sms, null, false, "Texted a Thursday reminder",
                AccountOwnerAccountUserId, "Owner", Now.AddMinutes(2));
            Assert.True(outboundContact.IsSuccess);
            seedCtx.Set<KeepRequestEvent>().Add(outboundContact.Value);
            await seedCtx.SaveChangesAsync();

            requestId = request.Id;
        }

        await using var ctx = CreateContext();
        var sut = new OpHalo.Keep.Infrastructure.Persistence.KeepRequestListPersistence(
            ctx, new OpHalo.Foundation.Infrastructure.Services.SystemClock());

        var previews = await sut.GetLatestPreviewEventsAsync([requestId], CancellationToken.None);

        var preview = previews[requestId];
        Assert.Equal("business_update", preview.PreviewSource);
        Assert.Equal("We'll swing by Thursday morning", preview.PreviewText);
        Assert.Equal(Now.AddMinutes(1), preview.PreviewAtUtc);
    }

    // --- ADR-450: internal-note-presence batch read ---

    [Fact]
    public async Task GetInternalNotePresenceAsync_true_for_internal_note_added_false_for_no_note()
    {
        Guid requestWithNote, requestWithoutNote;
        await using (var seedCtx = CreateContext())
        {
            var customer1 = KeepCustomer.Create(AccountId, "Note Customer", "0499777010");
            var customer2 = KeepCustomer.Create(AccountId, "No Note Customer", "0499777011");
            seedCtx.Set<KeepCustomer>().AddRange(customer1, customer2);
            await seedCtx.SaveChangesAsync();

            var req1 = KeepRequest.CreateFromCustomerIntake(
                AccountId, customer1.Id, "Note Customer", "0499777010", null,
                "Has a note", "PREV0010", "prev-tok-010", Now, 60);
            var req2 = KeepRequest.CreateFromCustomerIntake(
                AccountId, customer2.Id, "No Note Customer", "0499777011", null,
                "No note here", "PREV0011", "prev-tok-011", Now, 60);
            seedCtx.Set<KeepRequest>().AddRange(req1, req2);
            seedCtx.Set<KeepRequestEvent>().AddRange(
                KeepRequestEvent.CreateRequestCreated(req1.Id, AccountId, Now),
                KeepRequestEvent.CreateRequestCreated(req2.Id, AccountId, Now));
            await seedCtx.SaveChangesAsync();

            var note = req1.AddInternalNote(
                "Keep an eye on this one", AccountOwnerAccountUserId, "Owner", Now.AddMinutes(1));
            Assert.True(note.IsSuccess);
            seedCtx.Set<KeepRequestEvent>().Add(note.Value);
            await seedCtx.SaveChangesAsync();

            requestWithNote = req1.Id;
            requestWithoutNote = req2.Id;
        }

        await using var ctx = CreateContext();
        var sut = new OpHalo.Keep.Infrastructure.Persistence.KeepRequestListPersistence(
            ctx, new OpHalo.Foundation.Infrastructure.Services.SystemClock());

        var presence = await sut.GetInternalNotePresenceAsync(
            AccountId, [requestWithNote, requestWithoutNote], CancellationToken.None);

        Assert.Contains(requestWithNote, presence);
        Assert.DoesNotContain(requestWithoutNote, presence);
    }

    [Fact]
    public async Task GetInternalNotePresenceAsync_true_for_nonempty_participation_internal_note()
    {
        Guid requestId;
        await using (var seedCtx = CreateContext())
        {
            var customer = KeepCustomer.Create(AccountId, "Participation Note Customer", "0499777012");
            seedCtx.Set<KeepCustomer>().Add(customer);
            await seedCtx.SaveChangesAsync();

            var request = KeepRequest.CreateFromCustomerIntake(
                AccountId, customer.Id, "Participation Note Customer", "0499777012", null,
                "Needs a follow-up", "PREV0012", "prev-tok-012", Now, 60);
            seedCtx.Set<KeepRequest>().Add(request);
            seedCtx.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(request.Id, AccountId, Now));
            await seedCtx.SaveChangesAsync();

            var participationEvent = KeepRequestEvent.CreateParticipationChanged(
                request.Id, AccountId, AccountOwnerAccountUserId, "Owner",
                ParticipationAction.ResponsibleAssigned, AccountOwnerAccountUserId, "Owner",
                previousResponsibleAccountUserId: null,
                internalNote: "Assigning to myself, handling personally",
                notificationIntentKind: null,
                notificationIntendedRecipientAccountUserId: null,
                occurredAtUtc: Now.AddMinutes(1));
            seedCtx.Set<KeepRequestEvent>().Add(participationEvent);
            await seedCtx.SaveChangesAsync();

            requestId = request.Id;
        }

        await using var ctx = CreateContext();
        var sut = new OpHalo.Keep.Infrastructure.Persistence.KeepRequestListPersistence(
            ctx, new OpHalo.Foundation.Infrastructure.Services.SystemClock());

        var presence = await sut.GetInternalNotePresenceAsync(AccountId, [requestId], CancellationToken.None);

        Assert.Contains(requestId, presence);
    }

    [Fact]
    public async Task GetInternalNotePresenceAsync_false_for_empty_participation_note_and_feedback_review_note()
    {
        Guid requestId;
        await using (var seedCtx = CreateContext())
        {
            var customer = KeepCustomer.Create(AccountId, "No Content Note Customer", "0499777013");
            seedCtx.Set<KeepCustomer>().Add(customer);
            await seedCtx.SaveChangesAsync();

            var request = KeepRequest.CreateFromCustomerIntake(
                AccountId, customer.Id, "No Content Note Customer", "0499777013", null,
                "Needs a check", "PREV0013", "prev-tok-013", Now, 60);
            seedCtx.Set<KeepRequest>().Add(request);
            seedCtx.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(request.Id, AccountId, Now));
            await seedCtx.SaveChangesAsync();

            // ParticipationChanged with no internal-note text — must not count.
            var participationEvent = KeepRequestEvent.CreateParticipationChanged(
                request.Id, AccountId, AccountOwnerAccountUserId, "Owner",
                ParticipationAction.WatcherAdded, AccountOwnerAccountUserId, "Owner",
                previousResponsibleAccountUserId: null,
                internalNote: null,
                notificationIntentKind: null,
                notificationIntendedRecipientAccountUserId: null,
                occurredAtUtc: Now.AddMinutes(1));
            seedCtx.Set<KeepRequestEvent>().Add(participationEvent);
            await seedCtx.SaveChangesAsync();

            requestId = request.Id;
        }

        await using var ctx = CreateContext();
        var sut = new OpHalo.Keep.Infrastructure.Persistence.KeepRequestListPersistence(
            ctx, new OpHalo.Foundation.Infrastructure.Services.SystemClock());

        var presence = await sut.GetInternalNotePresenceAsync(AccountId, [requestId], CancellationToken.None);

        Assert.DoesNotContain(requestId, presence);
    }

    [Fact]
    public async Task GetInternalNotePresenceAsync_is_account_scoped()
    {
        Guid requestId;
        await using (var seedCtx = CreateContext())
        {
            var customer = KeepCustomer.Create(AccountId, "Cross Account Note Customer", "0499777014");
            seedCtx.Set<KeepCustomer>().Add(customer);
            await seedCtx.SaveChangesAsync();

            var request = KeepRequest.CreateFromCustomerIntake(
                AccountId, customer.Id, "Cross Account Note Customer", "0499777014", null,
                "Needs a check", "PREV0014", "prev-tok-014", Now, 60);
            seedCtx.Set<KeepRequest>().Add(request);
            seedCtx.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(request.Id, AccountId, Now));
            await seedCtx.SaveChangesAsync();

            var note = request.AddInternalNote(
                "Owned by AccountId, not SecondAccountId", AccountOwnerAccountUserId, "Owner", Now.AddMinutes(1));
            Assert.True(note.IsSuccess);
            seedCtx.Set<KeepRequestEvent>().Add(note.Value);
            await seedCtx.SaveChangesAsync();

            requestId = request.Id;
        }

        await using var ctx = CreateContext();
        var sut = new OpHalo.Keep.Infrastructure.Persistence.KeepRequestListPersistence(
            ctx, new OpHalo.Foundation.Infrastructure.Services.SystemClock());

        // Querying under SecondAccountId must not see AccountId's internal note, even though
        // the request ID is passed explicitly — the AccountId filter must win.
        var presence = await sut.GetInternalNotePresenceAsync(SecondAccountId, [requestId], CancellationToken.None);

        Assert.DoesNotContain(requestId, presence);
    }

    // --- GAP-065 Slice 3a: GetPendingFinancialReviewCountsAsync -----------------------------------

    private static ActualWork SubmittedZeroLineVisit(Guid accountId, Guid requestId, Guid userId, DateTime at)
    {
        var visit = ActualWork.Create(accountId, requestId, userId).Value;
        Assert.True(visit.Submit(at, ActualWorkOutcome.DiagnosticOnly, "Visited").IsSuccess);
        return visit;
    }

    [Fact]
    public async Task GetPendingFinancialReviewCountsAsync_counts_only_live_submitted_unreviewed_visits()
    {
        Guid requestWithPending, requestWithNone;
        await using (var seedCtx = CreateContext())
        {
            var customer = KeepCustomer.Create(AccountId, "Review Cue Customer", "0499777020");
            seedCtx.Set<KeepCustomer>().Add(customer);
            await seedCtx.SaveChangesAsync();

            var req1 = KeepRequest.CreateFromCustomerIntake(
                AccountId, customer.Id, "Review Cue Customer", "0499777020", null,
                "Two pending visits", "PREV0020", "prev-tok-020", Now, 60);
            var req2 = KeepRequest.CreateFromCustomerIntake(
                AccountId, customer.Id, "Review Cue Customer", "0499777020", null,
                "Nothing pending", "PREV0021", "prev-tok-021", Now, 60);
            seedCtx.Set<KeepRequest>().AddRange(req1, req2);
            await seedCtx.SaveChangesAsync();

            // req1: 2 live submitted/unreviewed visits (count), plus one reviewed and one
            // superseded (both excluded). The superseding successor is still a Draft, so it also
            // proves a non-Submitted visit is excluded (only one open Draft per request is allowed).
            var pendingA = SubmittedZeroLineVisit(AccountId, req1.Id, AccountOwnerAccountUserId, Now);
            var pendingB = SubmittedZeroLineVisit(AccountId, req1.Id, AccountOwnerAccountUserId, Now.AddMinutes(1));

            var reviewed = SubmittedZeroLineVisit(AccountId, req1.Id, AccountOwnerAccountUserId, Now.AddMinutes(2));
            Assert.True(reviewed.MarkReviewed(
                AccountOwnerAccountUserId, null, Now.AddMinutes(3),
                financialDataComplete: true, zeroLineDispositionSatisfied: true).IsSuccess);

            var superseded = SubmittedZeroLineVisit(AccountId, req1.Id, AccountOwnerAccountUserId, Now.AddMinutes(4));
            var successor = ActualWork.Create(AccountId, req1.Id, AccountOwnerAccountUserId).Value;

            seedCtx.Set<ActualWork>().AddRange(pendingA, pendingB, reviewed, superseded, successor);
            await seedCtx.SaveChangesAsync();

            Assert.True(superseded.Supersede(
                successor.Id, AccountOwnerAccountUserId, "Recorded in error", Now.AddMinutes(5)).IsSuccess);
            await seedCtx.SaveChangesAsync();

            requestWithPending = req1.Id;
            requestWithNone = req2.Id;
        }

        await using var ctx = CreateContext();
        var sut = new OpHalo.Keep.Infrastructure.Persistence.KeepRequestListPersistence(
            ctx, new OpHalo.Foundation.Infrastructure.Services.SystemClock());

        var counts = await sut.GetPendingFinancialReviewCountsAsync(
            AccountId, [requestWithPending, requestWithNone], CancellationToken.None);

        Assert.Equal(2, counts[requestWithPending]);
        Assert.DoesNotContain(requestWithNone, counts);
    }

    [Fact]
    public async Task GetPendingFinancialReviewCountsAsync_is_account_scoped()
    {
        Guid requestId;
        await using (var seedCtx = CreateContext())
        {
            var customer = KeepCustomer.Create(AccountId, "Cross Account Cue Customer", "0499777022");
            seedCtx.Set<KeepCustomer>().Add(customer);
            await seedCtx.SaveChangesAsync();

            var request = KeepRequest.CreateFromCustomerIntake(
                AccountId, customer.Id, "Cross Account Cue Customer", "0499777022", null,
                "Owned by AccountId", "PREV0022", "prev-tok-022", Now, 60);
            seedCtx.Set<KeepRequest>().Add(request);
            await seedCtx.SaveChangesAsync();

            seedCtx.Set<ActualWork>().Add(
                SubmittedZeroLineVisit(AccountId, request.Id, AccountOwnerAccountUserId, Now));
            await seedCtx.SaveChangesAsync();

            requestId = request.Id;
        }

        await using var ctx = CreateContext();
        var sut = new OpHalo.Keep.Infrastructure.Persistence.KeepRequestListPersistence(
            ctx, new OpHalo.Foundation.Infrastructure.Services.SystemClock());

        var counts = await sut.GetPendingFinancialReviewCountsAsync(
            SecondAccountId, [requestId], CancellationToken.None);

        Assert.DoesNotContain(requestId, counts);
    }
}
