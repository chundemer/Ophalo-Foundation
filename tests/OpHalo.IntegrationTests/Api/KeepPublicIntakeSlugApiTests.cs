using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Services;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Integration tests for POST /keep/public-intake/slug/{slug} (S22r2 / ADR-429).
/// Covers: current slug, active alias, revoked-link alias, unknown slug, casing
/// normalization, off-season account (blocked), and cross-account isolation.
/// </summary>
public sealed class KeepPublicIntakeSlugApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    private Guid _accountBId;

    // Account A (active) — current slug
    private const string ActiveSlug = "alpha-plumbing";

    // Active alias pointing to account A's link
    private const string AliasSlug = "alpha-plumbing-old";

    // Alias whose intake link has been revoked
    private const string RevokedLinkAliasSlug = "revoked-link-alias";

    // Account B (active) — cross-account isolation check
    private const string AccountBSlug = "beta-electric";

    // Account C (off-season / blocked) — should return 422
    private const string OffSeasonSlug = "offseason-plumbing";

    public KeepPublicIntakeSlugApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        var now = DateTime.UtcNow;
        var tokenService = new KeepTokenService();

        // --- Account A: active ---
        var provisionA = new AccountProvisioningService().CreateVerified(
            email: "owner@slug-api-tests-a.com",
            name: "Owner A",
            businessName: "Alpha Plumbing",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));
        Assert.True(provisionA.IsSuccess);
        var graphA = provisionA.Value;

        // --- Account B: active, different slug ---
        var provisionB = new AccountProvisioningService().CreateVerified(
            email: "owner@slug-api-tests-b.com",
            name: "Owner B",
            businessName: "Beta Electric",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));
        Assert.True(provisionB.IsSuccess);
        var graphB = provisionB.Value;
        _accountBId = graphB.Account.Id;

        // --- Account C: off-season (read-only) ---
        var provisionC = new AccountProvisioningService().CreateVerified(
            email: "owner@slug-api-tests-c.com",
            name: "Owner C",
            businessName: "OffSeason Plumbing",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));
        Assert.True(provisionC.IsSuccess);
        var graphC = provisionC.Value;

        // Put account C into OffSeason: Trial → PastDue → Active → OffSeason
        graphC.Entitlements.MarkPastDue(now, gracePeriodDays: 7);
        graphC.Entitlements.ResolvePastDue();
        var offSeasonResult = graphC.Entitlements.EnterOffSeason();
        Assert.True(offSeasonResult.IsSuccess, $"EnterOffSeason failed: {offSeasonResult.Error}");

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        // Persist all three accounts
        foreach (var g in new[] { graphA, graphB, graphC })
        {
            db.Users.Add(g.User);
            db.Accounts.Add(g.Account);
            db.AccountUsers.Add(g.Owner);
            db.AccountEntitlements.Add(g.Entitlements);

            var ownerFk = db.Entry(g.Account).Property(a => a.PrimaryOwnerAccountUserId);
            ownerFk.CurrentValue = null;
            await db.SaveChangesAsync();
            ownerFk.CurrentValue = g.Owner.Id;
            await db.SaveChangesAsync();
        }

        // Account A — active link with current slug "alpha-plumbing"
        var linkA = KeepPublicIntakeLink.Create(
            graphA.Account.Id, ActiveSlug, tokenService.HashPublicIntakeToken("token_slug_a"));
        db.Set<KeepPublicIntakeLink>().Add(linkA);
        await db.SaveChangesAsync();

        // Active alias for linkA
        var activeAlias = KeepPublicIntakeSlugAlias.Create(graphA.Account.Id, linkA.Id, AliasSlug);
        db.Set<KeepPublicIntakeSlugAlias>().Add(activeAlias);

        // Revoked link — revoke it before persisting so it never occupies the active-per-account slot.
        var revokedLink = KeepPublicIntakeLink.Create(
            graphA.Account.Id, "alpha-plumbing-revoked-link",
            tokenService.HashPublicIntakeToken("token_slug_a_revoked"));
        revokedLink.Revoke(now);
        db.Set<KeepPublicIntakeLink>().Add(revokedLink);
        await db.SaveChangesAsync();

        var revokedAlias = KeepPublicIntakeSlugAlias.Create(
            graphA.Account.Id, revokedLink.Id, RevokedLinkAliasSlug);
        db.Set<KeepPublicIntakeSlugAlias>().Add(revokedAlias);

        // Account B — active link
        var linkB = KeepPublicIntakeLink.Create(
            graphB.Account.Id, AccountBSlug, tokenService.HashPublicIntakeToken("token_slug_b"));
        db.Set<KeepPublicIntakeLink>().Add(linkB);

        // Account C (off-season) — active link
        var linkC = KeepPublicIntakeLink.Create(
            graphC.Account.Id, OffSeasonSlug, tokenService.HashPublicIntakeToken("token_slug_c"));
        db.Set<KeepPublicIntakeLink>().Add(linkC);

        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static object ValidBody(string name = "Test Customer") =>
        new { customerName = name, customerPhone = "0411111111", description = "I need help" };

    private Task<HttpResponseMessage> PostSlug(string slug, object? body = null) =>
        _client.PostAsJsonAsync(
            $"/keep/public-intake/slug/{slug}",
            body ?? ValidBody());

    // -------------------------------------------------------------------------
    // Test 1 — current active slug → 201
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Slug_CurrentActiveSlug_Returns201()
    {
        var res = await PostSlug(ActiveSlug);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<SlugIntakeBody>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.RequestId);
        Assert.False(string.IsNullOrWhiteSpace(body.ReferenceCode));
    }

    // -------------------------------------------------------------------------
    // Test 2 — active alias → 201
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Slug_ActiveAlias_Returns201()
    {
        var res = await PostSlug(AliasSlug);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<SlugIntakeBody>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.RequestId);
    }

    // -------------------------------------------------------------------------
    // Test 3 — alias whose link is revoked → 422 unavailable
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Slug_AliasWithRevokedLink_Returns422()
    {
        var res = await PostSlug(RevokedLinkAliasSlug);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Test 4 — unknown slug → 422 unavailable
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Slug_UnknownSlug_Returns422()
    {
        var res = await PostSlug("no-such-slug-xyz");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Test 5 — uppercase slug variant → 201 (casing normalization proof)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Slug_UppercaseVariant_Returns201()
    {
        var res = await PostSlug(ActiveSlug.ToUpperInvariant());
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Test 6 — off-season account → 422 (IsReadOnly blocks intake)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Slug_OffSeasonAccount_Returns422()
    {
        var res = await PostSlug(OffSeasonSlug);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Test 7 — cross-account slug isolation: account B slug posts to account B
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Slug_AccountBSlug_SubmitsToAccountB()
    {
        var res = await PostSlug(AccountBSlug);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<SlugIntakeBody>();
        Assert.NotNull(body);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var request = await db.Set<KeepRequest>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == body.RequestId);
        Assert.NotNull(request);
        Assert.Equal(_accountBId, request.AccountId);
    }

    // -------------------------------------------------------------------------
    // Response body record
    // -------------------------------------------------------------------------

    private sealed record SlugIntakeBody(Guid RequestId, string ReferenceCode, string PageToken);
}
