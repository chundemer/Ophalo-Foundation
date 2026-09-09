using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpHalo.Foundation.Application.Accounts.Provisioning;
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
