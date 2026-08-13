using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Core.Helpers;
using OpHalo.Foundation.Infrastructure.Persistence;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Integration tests for the internal capability-package enrollment operator path (ADR-462):
/// GET/enroll/disable/reenable under /internal/accounts/{accountId}/capability-packages/{featureKey}.
///
/// Coverage: anonymous 401, non-Internal-purpose caller 403, Internal-purpose caller below Admin
/// 403, unknown target account 404, unknown feature key 400, enroll/disable/reenable obey the
/// state machine, stale expected-version 409, and the target account resolves access through
/// AccountFeatureAccessResolver immediately after enrollment.
/// </summary>
public sealed class InternalEntitlementsApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private const string FeatureKey = CapabilityPackageFeatureKeys.PriceBookQuotesMaterials;

    private Guid _targetAccountId;
    private string _adminCookie = string.Empty;
    private string _operatorCookie = string.Empty;
    private string _businessOwnerCookie = string.Empty;

    public InternalEntitlementsApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        // Target account (a plain Business account whose entitlement gets operated on).
        var targetGraph = Provision(now, "target", AccountPurpose.Business);
        await SeedAccountGraphAsync(db, targetGraph);
        _targetAccountId = targetGraph.Account.Id;

        // Internal-purpose account with Admin and Operator members (the operator tool's callers).
        var internalGraph = Provision(now, "internal", AccountPurpose.Internal);
        await SeedAccountGraphAsync(db, internalGraph);

        var operatorUser = User.CreateVerified("operator@internal-entitlements-tests.com", null, now);
        const string operatorEmail = "operator@internal-entitlements-tests.com";
        var operatorMember = AccountUser.CreatePendingInvite(
            internalGraph.Account.Id, operatorEmail,
            EmailNormalizer.Normalize(operatorEmail),
            AccountUserRole.Operator,
            inviteTokenHash: "internal_ops_hash",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        operatorMember.Activate(operatorUser.Id, now);
        db.Users.Add(operatorUser);
        db.AccountUsers.Add(operatorMember);
        await db.SaveChangesAsync();

        _adminCookie = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(internalGraph.Owner.Id, internalGraph.Account.Id)}";
        _operatorCookie = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(operatorMember.Id, internalGraph.Account.Id)}";
        _businessOwnerCookie = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(targetGraph.Owner.Id, targetGraph.Account.Id)}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Anonymous_caller_is_denied()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/internal/accounts/{_targetAccountId}/capability-packages/{FeatureKey}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Business_purpose_caller_is_denied()
    {
        var response = await AuthClient(_businessOwnerCookie)
            .GetAsync($"/internal/accounts/{_targetAccountId}/capability-packages/{FeatureKey}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Internal_operator_below_admin_is_denied()
    {
        var response = await AuthClient(_operatorCookie)
            .PostAsync($"/internal/accounts/{_targetAccountId}/capability-packages/{FeatureKey}/enroll", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_target_account_does_not_mutate()
    {
        var response = await AuthClient(_adminCookie)
            .PostAsync($"/internal/accounts/{Guid.NewGuid()}/capability-packages/{FeatureKey}/enroll", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_feature_key_does_not_mutate()
    {
        var response = await AuthClient(_adminCookie)
            .PostAsync($"/internal/accounts/{_targetAccountId}/capability-packages/not.a.real.key/enroll", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Enroll_then_disable_then_reenable_round_trips_and_resolves_access()
    {
        var enrollResponse = await AuthClient(_adminCookie)
            .PostAsync($"/internal/accounts/{_targetAccountId}/capability-packages/{FeatureKey}/enroll", null);
        Assert.Equal(HttpStatusCode.OK, enrollResponse.StatusCode);
        var enrolled = await enrollResponse.Content.ReadFromJsonAsync<CapabilityPackageEnrollmentStatus>();
        Assert.NotNull(enrolled);
        Assert.Equal(CapabilityEnrollmentStatus.Enrolled, enrolled!.Status);

        // A second enroll on the same row is a conflict, not a silent reenable.
        var secondEnroll = await AuthClient(_adminCookie)
            .PostAsync($"/internal/accounts/{_targetAccountId}/capability-packages/{FeatureKey}/enroll", null);
        Assert.Equal(HttpStatusCode.Conflict, secondEnroll.StatusCode);

        // Founder access resolves immediately after enrollment.
        await using (var scope = _factory.CreateScope())
        {
            var resolver = scope.ServiceProvider.GetRequiredService<IAccountFeatureAccessResolver>();
            var enabled = await resolver.IsEnabledAsync(
                _targetAccountId, new AccountFeatureAccessContext(AccountPlan.Starter), FeatureKey, CancellationToken.None);
            Assert.True(enabled);
        }

        // Stale token on disable is rejected without mutating.
        var staleDisable = await AuthClient(_adminCookie).PostAsJsonAsync(
            $"/internal/accounts/{_targetAccountId}/capability-packages/{FeatureKey}/disable",
            new { concurrencyVersion = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, staleDisable.StatusCode);

        var disableResponse = await AuthClient(_adminCookie).PostAsJsonAsync(
            $"/internal/accounts/{_targetAccountId}/capability-packages/{FeatureKey}/disable",
            new { concurrencyVersion = enrolled.ConcurrencyVersion });
        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);
        var disabled = await disableResponse.Content.ReadFromJsonAsync<CapabilityPackageEnrollmentStatus>();
        Assert.NotNull(disabled);
        Assert.Equal(CapabilityEnrollmentStatus.Disabled, disabled!.Status);

        await using (var scope = _factory.CreateScope())
        {
            var resolver = scope.ServiceProvider.GetRequiredService<IAccountFeatureAccessResolver>();
            var enabled = await resolver.IsEnabledAsync(
                _targetAccountId, new AccountFeatureAccessContext(AccountPlan.Starter), FeatureKey, CancellationToken.None);
            Assert.False(enabled);
        }

        var reenableResponse = await AuthClient(_adminCookie).PostAsJsonAsync(
            $"/internal/accounts/{_targetAccountId}/capability-packages/{FeatureKey}/reenable",
            new { concurrencyVersion = disabled.ConcurrencyVersion });
        Assert.Equal(HttpStatusCode.OK, reenableResponse.StatusCode);
        var reenabled = await reenableResponse.Content.ReadFromJsonAsync<CapabilityPackageEnrollmentStatus>();
        Assert.Equal(CapabilityEnrollmentStatus.Enrolled, reenabled!.Status);

        var statusResponse = await AuthClient(_adminCookie)
            .GetAsync($"/internal/accounts/{_targetAccountId}/capability-packages/{FeatureKey}");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
    }

    private HttpClient AuthClient(string cookie)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    private static AccountProvisioningResult Provision(DateTime now, string slug, AccountPurpose purpose)
    {
        var isInternal = purpose == AccountPurpose.Internal;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}-internal-entitlements-tests.com",
            name: $"{slug} Owner",
            businessName: $"{slug} Co",
            purpose: purpose,
            timeZone: "America/Chicago",
            plan: isInternal ? AccountPlan.Internal : AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: isInternal ? null : now.AddDays(30));
        if (result.IsFailure)
            throw new Exception($"Provision failed: {result.Error.Code} — {result.Error.Message}");
        return result.Value;
    }

    private static async Task SeedAccountGraphAsync(OpHaloDbContext db, AccountProvisioningResult graph)
    {
        db.Users.Add(graph.User);
        db.Accounts.Add(graph.Account);
        db.AccountUsers.Add(graph.Owner);
        db.AccountEntitlements.Add(graph.Entitlements);

        var ownerFk = db.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerFk.CurrentValue = null;
        await db.SaveChangesAsync();
        ownerFk.CurrentValue = graph.Owner.Id;
        await db.SaveChangesAsync();
    }
}
