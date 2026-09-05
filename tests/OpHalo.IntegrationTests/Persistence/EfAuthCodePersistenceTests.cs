using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Auth;
using OpHalo.Foundation.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Persistence;

/// <summary>
/// Proves <see cref="EfAuthCodePersistence"/>'s MultipleMembers invalidation scoping (GAP-068
/// Slice 2a). MultipleMembers codes share NewAccount's null-TargetAccountUserId shape, so
/// CommitSignInCodeAsync/CommitStartCodeAsync must key invalidation by
/// (DeliveryEmailSnapshot, EntryContext) instead of TargetAccountUserId — otherwise issuing a
/// second MultipleMembers code for one email would either fail to supersede a prior
/// MultipleMembers code for that same email, or wrongly invalidate an unrelated null-target
/// code (a NewAccount code for the same email, or a MultipleMembers code for a different email).
/// </summary>
[Collection("Postgres")]
public sealed class EfAuthCodePersistenceTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime Now = PostgresFixture.FixedNow;
    private const string Email = "owner@ef-auth-code-persistence.example.com";
    private const string OtherEmail = "other@ef-auth-code-persistence.example.com";

    private readonly PostgresFixture _fixture;

    public EfAuthCodePersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OpHaloDbContext CreateContext() => _fixture.CreateContext();

    private static AccountAuthCode MultipleMembersCode(string email, string codeHash) =>
        AccountAuthCode.CreateForMultipleMembers(
            codeHash: codeHash,
            issuedAtUtc: Now,
            expiresAtUtc: Now.AddHours(24),
            deliveryEmailSnapshot: email);

    private static AccountAuthCode NewAccountCode(string email, string codeHash) =>
        AccountAuthCode.CreateForNewAccount(
            codeHash: codeHash,
            issuedAtUtc: Now,
            expiresAtUtc: Now.AddHours(24),
            deliveryEmailSnapshot: email,
            businessName: "Acme",
            name: null,
            timeZone: "America/Chicago");

    [Fact]
    public async Task CommitSignInCodeAsync_second_MultipleMembers_code_supersedes_first_same_email_only()
    {
        await using var seedCtx = CreateContext();

        var firstForEmail = MultipleMembersCode(Email, "first-email");
        var newAccountForEmail = NewAccountCode(Email, "new-account-email");
        var firstForOther = MultipleMembersCode(OtherEmail, "first-other");

        await new EfAuthCodePersistence(seedCtx).CommitSignInCodeAsync(firstForEmail, CancellationToken.None);
        await using (var ctx2 = CreateContext())
            await new EfAuthCodePersistence(ctx2).CommitSignInCodeAsync(newAccountForEmail, CancellationToken.None);
        await using (var ctx3 = CreateContext())
            await new EfAuthCodePersistence(ctx3).CommitSignInCodeAsync(firstForOther, CancellationToken.None);

        var secondForEmail = MultipleMembersCode(Email, "second-email");
        await using (var ctx4 = CreateContext())
            await new EfAuthCodePersistence(ctx4).CommitSignInCodeAsync(secondForEmail, CancellationToken.None);

        await using var assertCtx = CreateContext();
        var reloadedFirstForEmail = await assertCtx.AccountAuthCodes.FindAsync(firstForEmail.Id);
        var reloadedNewAccountForEmail = await assertCtx.AccountAuthCodes.FindAsync(newAccountForEmail.Id);
        var reloadedFirstForOther = await assertCtx.AccountAuthCodes.FindAsync(firstForOther.Id);
        var reloadedSecondForEmail = await assertCtx.AccountAuthCodes.FindAsync(secondForEmail.Id);

        Assert.NotNull(reloadedFirstForEmail!.InvalidatedAtUtc);
        Assert.Null(reloadedNewAccountForEmail!.InvalidatedAtUtc);
        Assert.Null(reloadedFirstForOther!.InvalidatedAtUtc);
        Assert.Null(reloadedSecondForEmail!.InvalidatedAtUtc);
    }

    [Fact]
    public async Task CommitStartCodeAsync_second_MultipleMembers_code_supersedes_first_same_email_only()
    {
        await using var seedCtx = CreateContext();

        var firstForEmail = MultipleMembersCode(Email, "start-first-email");
        var newAccountForEmail = NewAccountCode(Email, "start-new-account-email");
        var firstForOther = MultipleMembersCode(OtherEmail, "start-first-other");

        await new EfAuthCodePersistence(seedCtx).CommitStartCodeAsync(firstForEmail, CancellationToken.None);
        await using (var ctx2 = CreateContext())
            await new EfAuthCodePersistence(ctx2).CommitStartCodeAsync(newAccountForEmail, CancellationToken.None);
        await using (var ctx3 = CreateContext())
            await new EfAuthCodePersistence(ctx3).CommitStartCodeAsync(firstForOther, CancellationToken.None);

        var secondForEmail = MultipleMembersCode(Email, "start-second-email");
        await using (var ctx4 = CreateContext())
            await new EfAuthCodePersistence(ctx4).CommitStartCodeAsync(secondForEmail, CancellationToken.None);

        await using var assertCtx = CreateContext();
        var reloadedFirstForEmail = await assertCtx.AccountAuthCodes.FindAsync(firstForEmail.Id);
        var reloadedNewAccountForEmail = await assertCtx.AccountAuthCodes.FindAsync(newAccountForEmail.Id);
        var reloadedFirstForOther = await assertCtx.AccountAuthCodes.FindAsync(firstForOther.Id);
        var reloadedSecondForEmail = await assertCtx.AccountAuthCodes.FindAsync(secondForEmail.Id);

        Assert.NotNull(reloadedFirstForEmail!.InvalidatedAtUtc);
        Assert.Null(reloadedNewAccountForEmail!.InvalidatedAtUtc);
        Assert.Null(reloadedFirstForOther!.InvalidatedAtUtc);
        Assert.Null(reloadedSecondForEmail!.InvalidatedAtUtc);
    }
}
