using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Feedback;
using OpHalo.Foundation.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Persistence;

/// <summary>
/// Proves <see cref="EfFeedbackIdentityReader"/> against a real database (GAP-098, BL156): an
/// active membership resolves business/submitter/role/email, and a missing or non-active
/// membership (removed/suspended) resolves <c>null</c> so the caller falls back to IDs-only
/// delivery — never stale contact data.
/// </summary>
[Collection("Postgres")]
public sealed class EfFeedbackIdentityReaderTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime Now = PostgresFixture.FixedNow;

    private readonly PostgresFixture _fixture;

    public EfFeedbackIdentityReaderTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OpHaloDbContext CreateContext() => _fixture.CreateContext();

    [Fact]
    public async Task Active_membership_resolves_business_submitter_role_and_email()
    {
        var (accountId, accountUserId) = await SeedOwnerAsync("active-identity-reader.example.com", "Jane Doe");

        await using var ctx = CreateContext();
        var identity = await new EfFeedbackIdentityReader(ctx).GetAsync(accountId, accountUserId, CancellationToken.None);

        Assert.NotNull(identity);
        Assert.Equal("Identity Reader Test Co", identity!.BusinessName);
        Assert.Equal("Jane Doe", identity.SubmitterName);
        Assert.Equal("Owner", identity.Role);
        Assert.Equal("active-identity-reader.example.com", identity.Email);
    }

    [Fact]
    public async Task Missing_account_user_resolves_null()
    {
        await using var ctx = CreateContext();
        var identity = await new EfFeedbackIdentityReader(ctx)
            .GetAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Null(identity);
    }

    [Fact]
    public async Task Suspended_membership_resolves_null_never_stale_contact_data()
    {
        var (accountId, accountUserId) = await SeedOwnerAsync("suspended-identity-reader.example.com", "John Roe");

        await using (var writeCtx = CreateContext())
        {
            var accountUser = await writeCtx.AccountUsers.SingleAsync(x => x.Id == accountUserId);
            Assert.True(accountUser.Suspend().IsSuccess);
            await writeCtx.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var identity = await new EfFeedbackIdentityReader(ctx).GetAsync(accountId, accountUserId, CancellationToken.None);

        Assert.Null(identity);
    }

    private async Task<(Guid AccountId, Guid AccountUserId)> SeedOwnerAsync(string email, string name)
    {
        await using var ctx = CreateContext();

        var result = new AccountProvisioningService().CreateVerified(
            email: email,
            name: name,
            businessName: "Identity Reader Test Co",
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
