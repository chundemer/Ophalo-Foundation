using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Application.Feedback;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Feedback;
using OpHalo.Foundation.Core.Entities.Feedback.Enums;
using OpHalo.Foundation.Infrastructure.Feedback;
using OpHalo.Foundation.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Persistence;

/// <summary>
/// Proves <see cref="EfFeedbackPersistence"/> and <see cref="FeedbackSubmissionConfiguration"/>
/// against a real database (GAP-038, BL149, 038-2a-ii): persist-first round-trip with string-stored
/// enums, the confirmed-delivery body scrub persisting as SQL NULL, the strict non-null schema
/// rejecting a fabricated row, and the account/account-user foreign keys cascading.
/// </summary>
[Collection("Postgres")]
public sealed class FeedbackSubmissionPersistenceTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime Now = PostgresFixture.FixedNow;

    private readonly PostgresFixture _fixture;
    private Guid _accountId;
    private Guid _accountUserId;

    public FeedbackSubmissionPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();

        (_accountId, _accountUserId) = await SeedAccountAsync(ctx);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OpHaloDbContext CreateContext() => _fixture.CreateContext();

    private FeedbackSubmission NewSubmission(string message = "the scope dialog froze", FeedbackCategory category = FeedbackCategory.Bug) =>
        FeedbackSubmission.Create(
            accountId: _accountId,
            accountUserId: _accountUserId,
            message: message,
            category: category,
            contextJson: """{"route":"/requests"}""",
            nowUtc: Now);

    [Fact]
    public async Task AddAsync_round_trips_the_submission_with_string_stored_enums()
    {
        var submission = NewSubmission(category: FeedbackCategory.MissingThing);
        await using (var writeCtx = CreateContext())
            await new EfFeedbackPersistence(writeCtx).AddAsync(submission, CancellationToken.None);

        await using var readCtx = CreateContext();
        var stored = await readCtx.FeedbackSubmissions.AsNoTracking().SingleAsync(x => x.Id == submission.Id);

        Assert.Equal("the scope dialog froze", stored.Message);
        Assert.Equal(FeedbackCategory.MissingThing, stored.Category);
        Assert.Equal(FeedbackDeliveryState.Pending, stored.DeliveryState);
        Assert.Equal(Now, stored.CreatedAtUtc);
        Assert.Equal(Now, stored.NextAttemptAtUtc);
        Assert.Equal(0, stored.AttemptCount);

        // Enums are persisted as their names, not ordinals.
        var categoryText = await readCtx.Database
            .SqlQuery<string>($"SELECT category AS \"Value\" FROM feedback_submissions WHERE id = {submission.Id}")
            .SingleAsync();
        Assert.Equal("MissingThing", categoryText);
    }

    [Fact]
    public async Task UpdateAsync_persists_the_confirmed_delivery_scrub_as_null()
    {
        var submission = NewSubmission();
        await using (var writeCtx = CreateContext())
            await new EfFeedbackPersistence(writeCtx).AddAsync(submission, CancellationToken.None);

        submission.MarkAttempted(Now.AddSeconds(1));
        submission.MarkDelivered(Now.AddSeconds(2));

        await using (var updateCtx = CreateContext())
            await new EfFeedbackPersistence(updateCtx).UpdateAsync(submission, CancellationToken.None);

        await using var readCtx = CreateContext();
        var stored = await readCtx.FeedbackSubmissions.AsNoTracking().SingleAsync(x => x.Id == submission.Id);

        Assert.Equal(FeedbackDeliveryState.Delivered, stored.DeliveryState);
        Assert.Null(stored.Message);
        Assert.Null(stored.ContextJson);
        Assert.Null(stored.NextAttemptAtUtc);
        Assert.Equal(Now.AddSeconds(2), stored.DeliveredAtUtc);
        Assert.Equal(1, stored.AttemptCount);

        var nullCount = await readCtx.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM feedback_submissions WHERE id = {submission.Id} AND message IS NULL AND context_json IS NULL")
            .SingleAsync();
        Assert.Equal(1, nullCount);
    }

    [Fact]
    public async Task Schema_rejects_a_row_with_null_fact_columns()
    {
        await using var ctx = CreateContext();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO feedback_submissions
                (id, account_id, account_user_id, message, category, context_json, created_at_utc,
                 delivery_state, attempt_count, last_attempt_at_utc, next_attempt_at_utc, delivered_at_utc)
            VALUES
                ({Guid.NewGuid()}, {_accountId}, {_accountUserId}, {"x"}, NULL, NULL, {Now},
                 {"Pending"}, {0}, NULL, {Now}, NULL)
            """));

        Assert.Equal(PostgresErrorCodes.NotNullViolation, ex.SqlState);
    }

    [Fact]
    public async Task Deleting_the_account_user_cascades_to_its_feedback()
    {
        var submission = NewSubmission();
        await using (var writeCtx = CreateContext())
            await new EfFeedbackPersistence(writeCtx).AddAsync(submission, CancellationToken.None);

        await using (var deleteCtx = CreateContext())
        {
            // Clear the account -> owner back-reference so the membership row can be removed.
            await deleteCtx.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE accounts SET primary_owner_account_user_id = NULL WHERE id = {_accountId}");
            await deleteCtx.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM account_users WHERE id = {_accountUserId}");
        }

        await using var readCtx = CreateContext();
        Assert.False(await readCtx.FeedbackSubmissions.AsNoTracking().AnyAsync(x => x.Id == submission.Id));
    }

    [Fact]
    public async Task GetDueForRetryAsync_returns_only_due_pending_rows_oldest_first()
    {
        var overdue = NewSubmissionAt(Now.AddMinutes(-30));
        var justDue = NewSubmissionAt(Now.AddMinutes(-1));
        var future = NewSubmissionAt(Now.AddMinutes(30));
        var delivered = Delivered(Now.AddMinutes(-20));

        await using (var ctx = CreateContext())
        {
            var persistence = new EfFeedbackPersistence(ctx);
            foreach (var submission in new[] { future, overdue, justDue, delivered })
                await persistence.AddAsync(submission, CancellationToken.None);
        }

        await using var readCtx = CreateContext();
        var due = await new EfFeedbackPersistence(readCtx).GetDueForRetryAsync(Now, 10, CancellationToken.None);

        Assert.Equal(new[] { overdue.Id, justDue.Id }, due.Select(x => x.Id).ToArray());
    }

    [Fact]
    public async Task CountAndOldestPendingBeforeAsync_counts_old_pending_rows_and_reports_the_oldest()
    {
        var old1 = NewSubmissionAt(Now.AddMinutes(-40));
        var old2 = NewSubmissionAt(Now.AddMinutes(-20));
        var recent = NewSubmissionAt(Now.AddMinutes(-2));
        var deliveredOld = Delivered(Now.AddMinutes(-50));

        await using (var ctx = CreateContext())
        {
            var persistence = new EfFeedbackPersistence(ctx);
            foreach (var submission in new[] { old1, old2, recent, deliveredOld })
                await persistence.AddAsync(submission, CancellationToken.None);
        }

        await using var readCtx = CreateContext();
        var (count, oldest) = await new EfFeedbackPersistence(readCtx)
            .CountAndOldestPendingBeforeAsync(Now.AddMinutes(-15), CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(Now.AddMinutes(-40), oldest);
    }

    [Fact]
    public async Task DeleteExpiredAsync_removes_only_aged_delivered_and_abandoned_rows()
    {
        var freshDelivered = Delivered(Now.AddDays(-3));
        var staleDelivered = Delivered(Now.AddDays(-8));
        var freshAbandoned = Abandoned(Now.AddDays(-10));
        var staleAbandoned = Abandoned(Now.AddDays(-31));
        var agedPending = NewSubmissionAt(Now.AddDays(-40));

        await using (var ctx = CreateContext())
        {
            var persistence = new EfFeedbackPersistence(ctx);
            foreach (var submission in new[] { freshDelivered, staleDelivered, freshAbandoned, staleAbandoned, agedPending })
                await persistence.AddAsync(submission, CancellationToken.None);
        }

        await using var sweepCtx = CreateContext();
        var deleted = await new EfFeedbackPersistence(sweepCtx)
            .DeleteExpiredAsync(Now.AddDays(-7), Now.AddDays(-30), CancellationToken.None);

        Assert.Equal(2, deleted);

        await using var readCtx = CreateContext();
        var remaining = await readCtx.FeedbackSubmissions.AsNoTracking().Select(x => x.Id).ToListAsync();
        Assert.Equal(
            new[] { freshDelivered.Id, freshAbandoned.Id, agedPending.Id }.OrderBy(x => x).ToArray(),
            remaining.OrderBy(x => x).ToArray());
    }

    private FeedbackSubmission NewSubmissionAt(DateTime createdAtUtc) =>
        FeedbackSubmission.Create(
            accountId: _accountId,
            accountUserId: _accountUserId,
            message: "retry me",
            category: FeedbackCategory.Bug,
            contextJson: null,
            nowUtc: createdAtUtc);

    private FeedbackSubmission Delivered(DateTime deliveredAtUtc)
    {
        var submission = NewSubmissionAt(deliveredAtUtc.AddMinutes(-5));
        submission.MarkAttempted(deliveredAtUtc.AddMinutes(-1));
        submission.MarkDelivered(deliveredAtUtc);
        return submission;
    }

    private FeedbackSubmission Abandoned(DateTime createdAtUtc)
    {
        var submission = NewSubmissionAt(createdAtUtc);
        for (var attempt = 0; attempt < FeedbackDeliveryWorker.MaxAttempts; attempt++)
            submission.MarkAttempted(createdAtUtc);
        submission.MarkAbandoned(createdAtUtc.AddHours(4));
        return submission;
    }

    private static async Task<(Guid AccountId, Guid AccountUserId)> SeedAccountAsync(OpHaloDbContext ctx)
    {
        var result = new AccountProvisioningService().CreateVerified(
            email: "owner@feedback-submission-persistence.example.com",
            name: "Test Owner",
            businessName: "Feedback Persistence Test Co",
            purpose: AccountPurpose.Business,
            timeZone: "UTC",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: Now,
            trialEndsAtUtc: Now.AddDays(30));

        if (result.IsFailure)
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
