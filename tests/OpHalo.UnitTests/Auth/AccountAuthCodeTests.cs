using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using Xunit;

namespace OpHalo.UnitTests.Auth;

/// <summary>
/// Locks AccountAuthCode factory guards for Phase 5C new-account codes.
/// </summary>
public class AccountAuthCodeTests
{
    private static readonly DateTime ValidIssuedAt =
        new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime ValidExpiresAt =
        new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    // --- CreateForNewAccount: happy path ---

    [Fact]
    public void CreateForNewAccount_sets_correct_entry_context_and_snapshots()
    {
        var code = AccountAuthCode.CreateForNewAccount(
            codeHash: "abc123",
            issuedAtUtc: ValidIssuedAt,
            expiresAtUtc: ValidExpiresAt,
            deliveryEmailSnapshot: "owner@example.com",
            businessName: "  Acme Plumbing  ",
            name: "  Riley  ",
            timeZone: "  America/Chicago  ");

        Assert.Equal(EntryContext.NewAccount, code.EntryContext);
        Assert.Null(code.AccountId);
        Assert.Null(code.TargetAccountUserId);
        Assert.Equal("Acme Plumbing", code.BusinessNameSnapshot);
        Assert.Equal("Riley", code.NameSnapshot);
        Assert.Equal("America/Chicago", code.TimeZoneSnapshot);
        Assert.Equal("owner@example.com", code.DeliveryEmailSnapshot);
    }

    [Fact]
    public void CreateForNewAccount_trims_null_name_to_null()
    {
        var code = AccountAuthCode.CreateForNewAccount(
            codeHash: "abc123",
            issuedAtUtc: ValidIssuedAt,
            expiresAtUtc: ValidExpiresAt,
            deliveryEmailSnapshot: "owner@example.com",
            businessName: "Acme",
            name: null,
            timeZone: "America/Chicago");

        Assert.Null(code.NameSnapshot);
    }

    [Fact]
    public void CreateForNewAccount_trims_blank_name_to_null()
    {
        var code = AccountAuthCode.CreateForNewAccount(
            codeHash: "abc123",
            issuedAtUtc: ValidIssuedAt,
            expiresAtUtc: ValidExpiresAt,
            deliveryEmailSnapshot: "owner@example.com",
            businessName: "Acme",
            name: "   ",
            timeZone: "America/Chicago");

        Assert.Null(code.NameSnapshot);
    }

    // --- CreateForNewAccount: guards ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateForNewAccount_requires_business_name(string businessName) =>
        Assert.Throws<ArgumentException>(() => AccountAuthCode.CreateForNewAccount(
            "hash", ValidIssuedAt, ValidExpiresAt, "owner@example.com",
            businessName, null, "America/Chicago"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateForNewAccount_requires_time_zone(string timeZone) =>
        Assert.Throws<ArgumentException>(() => AccountAuthCode.CreateForNewAccount(
            "hash", ValidIssuedAt, ValidExpiresAt, "owner@example.com",
            "Acme", null, timeZone));

    [Fact]
    public void CreateForNewAccount_requires_utc_issued_at() =>
        Assert.Throws<ArgumentException>(() => AccountAuthCode.CreateForNewAccount(
            "hash",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local),
            ValidExpiresAt,
            "owner@example.com", "Acme", null, "America/Chicago"));

    [Fact]
    public void CreateForNewAccount_requires_expires_after_issued() =>
        Assert.Throws<ArgumentException>(() => AccountAuthCode.CreateForNewAccount(
            "hash", ValidIssuedAt, ValidIssuedAt, "owner@example.com",
            "Acme", null, "America/Chicago"));

    // --- Create: rejects NewAccount context ---

    [Fact]
    public void Create_throws_when_entry_context_is_new_account() =>
        Assert.Throws<ArgumentException>(() => AccountAuthCode.Create(
            accountId: null,
            targetAccountUserId: null,
            codeHash: "hash",
            issuedAtUtc: ValidIssuedAt,
            expiresAtUtc: ValidExpiresAt,
            deliveryEmailSnapshot: "owner@example.com",
            entryContext: EntryContext.NewAccount));

    // --- Invalidate domain method (existing behavior, confirmed with new context) ---

    [Fact]
    public void Invalidate_on_new_account_code_succeeds()
    {
        var code = AccountAuthCode.CreateForNewAccount(
            "hash", ValidIssuedAt, ValidExpiresAt, "owner@example.com",
            "Acme", null, "America/Chicago");

        var result = code.Invalidate(ValidIssuedAt.AddSeconds(1));

        Assert.True(result.IsSuccess);
        Assert.True(code.IsInvalidated);
    }
}
