using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Application.Devices;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Infrastructure.Persistence;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Integration tests for ADR-355/356: account-user-scoped device registration and revocation.
/// PUT /me/devices/{appInstallationId} and DELETE /me/devices/{appInstallationId}.
/// </summary>
public sealed class AccountUserDeviceApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    // Valid UUID v4 install IDs.
    private static readonly string InstallId1 = "a1b2c3d4-e5f6-4000-8000-1a2b3c4d5e6f";
    private static readonly string InstallId2 = "b2c3d4e5-f6a7-4111-9111-2b3c4d5e6f7a";

    // Sentinel push tokens — obviously unique so any log appearance is unmistakable.
    private const string Token1 = "DEVICE_TEST_PUSH_TOKEN_ALPHA_XZQ1A2B3C4D5E6F7G8H9";
    private const string Token2 = "DEVICE_TEST_PUSH_TOKEN_BETA_XZQ9Z8Y7X6W5V4U3T2S1";
    private const string Token3 = "DEVICE_TEST_PUSH_TOKEN_GAMMA_XZQ0P1O2N3M4L5K6J7I8";

    private Guid _ownerAccountUserId;
    private Guid _ownerAccountId;
    private string _ownerCookie = string.Empty;

    public AccountUserDeviceApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: "owner@device-tests.com",
            name: "Device Owner",
            businessName: "Device Test Co",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(result.IsSuccess);
        var graph = result.Value;

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Users.Add(graph.User);
        db.Accounts.Add(graph.Account);
        db.AccountUsers.Add(graph.Owner);
        db.AccountEntitlements.Add(graph.Entitlements);

        var ownerFk = db.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerFk.CurrentValue = null;
        await db.SaveChangesAsync();
        ownerFk.CurrentValue = graph.Owner.Id;
        await db.SaveChangesAsync();

        _ownerAccountId = graph.Account.Id;
        _ownerAccountUserId = graph.Owner.Id;

        var rawToken = await _factory.SeedSessionAsync(_ownerAccountUserId, _ownerAccountId);
        _ownerCookie = $"ophalo.sid={rawToken}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // =========================================================================
    // Auth
    // =========================================================================

    [Fact]
    public async Task Put_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync($"/me/devices/{InstallId1}",
            new { platform = "ios", pushToken = Token1 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.DeleteAsync($"/me/devices/{InstallId1}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =========================================================================
    // Validation — appInstallationId
    // =========================================================================

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]     // empty GUID
    [InlineData("a1b2c3d4-e5f6-1000-8000-1a2b3c4d5e6f")]   // v1 not v4
    [InlineData("a1b2c3d4-e5f6-5000-8000-1a2b3c4d5e6f")]   // v5 not v4
    public async Task Put_InvalidAppInstallationId_Returns400(string badId)
    {
        var client = AuthRequest(_ownerCookie);
        var response = await client.PutAsJsonAsync($"/me/devices/{badId}",
            new { platform = "ios", pushToken = Token1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertCode(response, "AccountUserDevice.InvalidAppInstallationId");
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("a1b2c3d4-e5f6-1000-8000-1a2b3c4d5e6f")]
    public async Task Delete_InvalidAppInstallationId_Returns400(string badId)
    {
        var client = AuthRequest(_ownerCookie);
        var response = await client.DeleteAsync($"/me/devices/{badId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertCode(response, "AccountUserDevice.InvalidAppInstallationId");
    }

    // =========================================================================
    // Validation — body fields
    // =========================================================================

    [Fact]
    public async Task Put_NullPushToken_Returns200_WithNullTokenFields()
    {
        var client = AuthRequest(_ownerCookie);
        var response = await client.PutAsJsonAsync($"/me/devices/{InstallId1}",
            new { platform = "ios" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(InstallId1, body.GetProperty("appInstallationId").GetString(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal("ios", body.GetProperty("platform").GetString());
        Assert.Equal("active", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("tokenFingerprint").ValueKind == JsonValueKind.Null,
            "tokenFingerprint must be null for a push-ineligible device");
        Assert.True(body.GetProperty("tokenLastFour").ValueKind == JsonValueKind.Null,
            "tokenLastFour must be null for a push-ineligible device");
    }

    [Fact]
    public async Task Put_WhitespacePushToken_Returns400()
    {
        var client = AuthRequest(_ownerCookie);
        var response = await client.PutAsJsonAsync($"/me/devices/{InstallId1}",
            new { platform = "ios", pushToken = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertCode(response, "Validation.PushTokenInvalid");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("web")]
    [InlineData("ANDROID")]  // case-insensitive parsing only accepts lowercase
    public async Task Put_InvalidPlatform_Returns400(string? platform)
    {
        var client = AuthRequest(_ownerCookie);
        var response = await client.PutAsJsonAsync($"/me/devices/{InstallId1}",
            new { platform, pushToken = Token1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertCode(response, "Validation.PlatformInvalid");
    }

    // =========================================================================
    // Validation — field lengths
    // =========================================================================

    [Fact]
    public async Task Put_PushTokenExceedsMaxLength_Returns400()
    {
        var client = AuthRequest(_ownerCookie);
        var oversized = new string('x', 1025);
        var response = await client.PutAsJsonAsync($"/me/devices/{InstallId1}",
            new { platform = "ios", pushToken = oversized });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertCode(response, "Validation.PushTokenTooLong");
    }

    [Fact]
    public async Task Put_AppVersionExceedsMaxLength_Returns400()
    {
        var client = AuthRequest(_ownerCookie);
        var response = await client.PutAsJsonAsync($"/me/devices/{InstallId1}",
            new { platform = "ios", pushToken = Token1, appVersion = new string('1', 51) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertCode(response, "Validation.AppVersionTooLong");
    }

    [Fact]
    public async Task Put_DeviceNameExceedsMaxLength_Returns400()
    {
        var client = AuthRequest(_ownerCookie);
        var response = await client.PutAsJsonAsync($"/me/devices/{InstallId1}",
            new { platform = "ios", pushToken = Token1, deviceName = new string('d', 201) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertCode(response, "Validation.DeviceNameTooLong");
    }

    // =========================================================================
    // Register — happy path
    // =========================================================================

    [Fact]
    public async Task Put_ValidIos_CreatesActiveDevice_ReturnsSafeMetadata()
    {
        var client = AuthRequest(_ownerCookie);
        var response = await client.PutAsJsonAsync($"/me/devices/{InstallId1}",
            new { platform = "ios", pushToken = Token1, appVersion = "1.0.0", deviceName = "iPhone 16" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(InstallId1, body.GetProperty("appInstallationId").GetString(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal("ios", body.GetProperty("platform").GetString());
        Assert.Equal("active", body.GetProperty("status").GetString());
        Assert.NotEmpty(body.GetProperty("tokenFingerprint").GetString()!);
        Assert.Equal(4, body.GetProperty("tokenLastFour").GetString()!.Length);
        Assert.Equal("1.0.0", body.GetProperty("appVersion").GetString());
        Assert.Equal("iPhone 16", body.GetProperty("deviceName").GetString());
    }

    [Fact]
    public async Task Put_ResponseDoesNotContainRawPushToken()
    {
        var client = AuthRequest(_ownerCookie);
        var response = await client.PutAsJsonAsync($"/me/devices/{InstallId1}",
            new { platform = "ios", pushToken = Token1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Token1, rawBody);
        Assert.DoesNotContain("pushToken", rawBody);
    }

    // =========================================================================
    // Register — idempotent upsert
    // =========================================================================

    [Fact]
    public async Task Put_SameInstallIdTwice_IsIdempotent_UpdatesTokenAndMetadata()
    {
        var client = AuthRequest(_ownerCookie);

        // First registration.
        await client.PutAsJsonAsync($"/me/devices/{InstallId1}",
            new { platform = "ios", pushToken = Token1, appVersion = "1.0.0" });

        // Second registration with new token and metadata.
        var response = await client.PutAsJsonAsync($"/me/devices/{InstallId1}",
            new { platform = "ios", pushToken = Token2, appVersion = "1.1.0", deviceName = "Updated iPhone" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("active", body.GetProperty("status").GetString());
        Assert.Equal("ios", body.GetProperty("platform").GetString());
        Assert.Equal("1.1.0", body.GetProperty("appVersion").GetString());
        Assert.Equal("Updated iPhone", body.GetProperty("deviceName").GetString());

        // Fingerprint changed — Token2 fingerprint differs from Token1.
        var fingerprint = body.GetProperty("tokenFingerprint").GetString()!;
        Assert.NotEmpty(fingerprint);

        // Confirm only one device row exists for this install.
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var devices = db.AccountUserDevices
            .Where(d => d.AccountUserId == _ownerAccountUserId)
            .ToList();
        Assert.Single(devices);
        Assert.Equal(AccountUserDeviceStatus.Active, devices[0].Status);
    }

    // =========================================================================
    // Register — platform mismatch
    // =========================================================================

    [Fact]
    public async Task Put_PlatformMismatch_Returns400()
    {
        var client = AuthRequest(_ownerCookie);

        // Register as iOS.
        await client.PutAsJsonAsync($"/me/devices/{InstallId1}",
            new { platform = "ios", pushToken = Token1 });

        // Attempt to re-register same install as Android.
        var response = await client.PutAsJsonAsync($"/me/devices/{InstallId1}",
            new { platform = "android", pushToken = Token2 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertCode(response, "AccountUserDevice.PlatformMismatch");
    }

    // =========================================================================
    // Register — token-to-user rebinding
    // =========================================================================

    [Fact]
    public async Task Put_SameTokenDifferentUser_RevokesOldBinding_ActivatesNew()
    {
        // Seed a second account user.
        var (secondAccountUserId, secondCookie) = await SeedSecondUserAsync();

        var ownerClient = AuthRequest(_ownerCookie);
        var secondClient = AuthRequest(secondCookie);

        // Owner registers with Token1 on InstallId1.
        var ownerReg = await ownerClient.PutAsJsonAsync($"/me/devices/{InstallId1}",
            new { platform = "ios", pushToken = Token1 });
        Assert.Equal(HttpStatusCode.OK, ownerReg.StatusCode);

        // Second user registers the same Token1 on their own install (InstallId2).
        var secondReg = await secondClient.PutAsJsonAsync($"/me/devices/{InstallId2}",
            new { platform = "ios", pushToken = Token1 });
        Assert.Equal(HttpStatusCode.OK, secondReg.StatusCode);

        // Owner's device should now be Revoked.
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var ownerDevice = db.AccountUserDevices
            .FirstOrDefault(d => d.AccountUserId == _ownerAccountUserId && d.AppInstallationId == Guid.Parse(InstallId1));
        Assert.NotNull(ownerDevice);
        Assert.Equal(AccountUserDeviceStatus.Revoked, ownerDevice.Status);
        Assert.NotNull(ownerDevice.RevokedAtUtc);

        // Second user's device should be Active.
        var secondDevice = db.AccountUserDevices
            .FirstOrDefault(d => d.AccountUserId == secondAccountUserId && d.AppInstallationId == Guid.Parse(InstallId2));
        Assert.NotNull(secondDevice);
        Assert.Equal(AccountUserDeviceStatus.Active, secondDevice.Status);
    }

    // =========================================================================
    // Revoke — DELETE
    // =========================================================================

    [Fact]
    public async Task Delete_ExistingDevice_Returns204_AndRevokesDevice()
    {
        var client = AuthRequest(_ownerCookie);

        // Register first.
        await client.PutAsJsonAsync($"/me/devices/{InstallId1}",
            new { platform = "ios", pushToken = Token1 });

        // Revoke.
        var response = await client.DeleteAsync($"/me/devices/{InstallId1}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Confirm device is Revoked in DB.
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var device = db.AccountUserDevices
            .FirstOrDefault(d => d.AccountUserId == _ownerAccountUserId && d.AppInstallationId == Guid.Parse(InstallId1));
        Assert.NotNull(device);
        Assert.Equal(AccountUserDeviceStatus.Revoked, device.Status);
        Assert.NotNull(device.RevokedAtUtc);
    }

    [Fact]
    public async Task Delete_NonExistentDevice_Returns204()
    {
        var client = AuthRequest(_ownerCookie);
        var response = await client.DeleteAsync($"/me/devices/{InstallId1}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_AlreadyRevoked_Returns204_Idempotent()
    {
        var client = AuthRequest(_ownerCookie);

        await client.PutAsJsonAsync($"/me/devices/{InstallId1}",
            new { platform = "ios", pushToken = Token1 });

        // First delete.
        await client.DeleteAsync($"/me/devices/{InstallId1}");

        // Second delete — still 204.
        var response = await client.DeleteAsync($"/me/devices/{InstallId1}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // =========================================================================
    // OffSeason — must not block device operations
    // =========================================================================

    [Fact]
    public async Task Put_OffSeasonAccount_Returns200()
    {
        await SetAccountOffSeasonAsync();

        var client = AuthRequest(_ownerCookie);
        var response = await client.PutAsJsonAsync($"/me/devices/{InstallId1}",
            new { platform = "ios", pushToken = Token3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OffSeasonAccount_Returns204()
    {
        await SetAccountOffSeasonAsync();

        var client = AuthRequest(_ownerCookie);
        var response = await client.DeleteAsync($"/me/devices/{InstallId1}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // =========================================================================
    // Delivery eligibility — account classification gate
    // =========================================================================

    [Theory]
    [InlineData(AccountClassification.Production, 1)]
    [InlineData(AccountClassification.Pilot, 1)]
    [InlineData(AccountClassification.Demo, 0)]
    [InlineData(AccountClassification.InternalTest, 0)]
    public async Task FindActiveDevicesForDelivery_FiltersByAccountClassification(
        AccountClassification classification,
        int expectedDeviceCount)
    {
        var (accountId, accountUserId) = await SeedDeliveryDeviceAccountAsync(classification);

        await using var scope = _factory.CreateScope();
        var persistence = scope.ServiceProvider.GetRequiredService<IAccountUserDevicePersistence>();

        var devices = await persistence.FindActiveDevicesForDeliveryAsync(
            accountId,
            [accountUserId],
            CancellationToken.None);

        Assert.Equal(expectedDeviceCount, devices.Count);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private HttpClient AuthRequest(string cookie)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    private async Task<(Guid AccountUserId, string Cookie)> SeedSecondUserAsync()
    {
        var now = DateTime.UtcNow;
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var user = User.CreateVerified("second@device-tests.com", null, now);
        var member = AccountUser.CreateOwner(_ownerAccountId, user.Id, user.Email, user.Email);
        db.Users.Add(user);
        db.AccountUsers.Add(member);
        await db.SaveChangesAsync();

        var rawToken = await _factory.SeedSessionAsync(member.Id, _ownerAccountId);
        return (member.Id, $"ophalo.sid={rawToken}");
    }

    private async Task SetAccountOffSeasonAsync()
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var entitlements = await db.AccountEntitlements
            .FirstAsync(e => e.AccountId == _ownerAccountId);

        entitlements.MarkPastDue(DateTime.UtcNow, gracePeriodDays: 7);
        entitlements.ResolvePastDue();
        var enterResult = entitlements.EnterOffSeason();
        Assert.True(enterResult.IsSuccess);
        await db.SaveChangesAsync();
    }

    private async Task<(Guid AccountId, Guid AccountUserId)> SeedDeliveryDeviceAccountAsync(AccountClassification classification)
    {
        var now = DateTime.UtcNow;
        var user = User.CreateVerified($"delivery-{classification.ToString().ToLowerInvariant()}@device-tests.com", null, now);
        var account = Account.CreateVerified($"{classification} Delivery Co", AccountPurpose.Business, "Australia/Sydney");
        var owner = AccountUser.CreateOwner(account.Id, user.Id, user.Email, user.Email);
        var entitlements = AccountEntitlements.Create(
            account.Id,
            AccountPlan.Trial,
            maxUserSeats: 5,
            now.AddDays(30),
            classification);
        var device = AccountUserDevice.Create(
            account.Id,
            owner.Id,
            Guid.CreateVersion7(),
            AccountUserDevicePlatform.Ios,
            $"DELIVERY_GATE_TOKEN_{classification}",
            $"delivery-gate-fingerprint-{classification}".ToLowerInvariant(),
            "1234",
            appVersion: "1.0.0",
            deviceName: $"{classification} test phone",
            now);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        db.Users.Add(user);
        db.Accounts.Add(account);
        db.AccountUsers.Add(owner);
        db.AccountEntitlements.Add(entitlements);
        db.AccountUserDevices.Add(device);

        var ownerFk = db.Entry(account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerFk.CurrentValue = null;
        await db.SaveChangesAsync();
        ownerFk.CurrentValue = owner.Id;
        await db.SaveChangesAsync();

        return (account.Id, owner.Id);
    }

    private static async Task AssertCode(HttpResponseMessage response, string expectedCode)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var code = body.TryGetProperty("code", out var c) ? c.GetString() : null;
        Assert.Equal(expectedCode, code);
    }
}
