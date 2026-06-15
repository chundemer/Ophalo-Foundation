using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using Xunit;

namespace OpHalo.UnitTests.Accounts;

/// <summary>
/// Exit-gate matrix for the account-creation orchestration slice: the first composing caller
/// of the Phase 4 factories. Asserts the assembled graph (User + Account + owner AccountUser +
/// AccountEntitlements), the internal-account triad enforced fail-closed, plan-derived seats,
/// and primary-owner assignment. Pure domain composition — no persistence is exercised.
/// </summary>
public class AccountProvisioningServiceTests
{
    static readonly AccountProvisioningService Service = new();
    static readonly DateTime Now = new(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
    static readonly DateTime TrialEnds = new(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);

    const string Email = "Owner@Example.com";
    const string NormalizedEmail = "owner@example.com";

    static int PlanSeats(AccountPlan plan) =>
        PlanEntitlements.LimitDefaults[plan][FeatureLimitKeys.Account.UserLimit];

    // --- Happy paths ---------------------------------------------------------

    [Fact]
    public void Business_trial_provisions_full_graph()
    {
        var result = Service.CreateVerified(
            Email, "Pat Owner", "Acme Plumbing", AccountPurpose.Business, "America/New_York",
            AccountPlan.Starter, isPilot: false, Now, TrialEnds);

        Assert.True(result.IsSuccess);
        var graph = result.Value;

        Assert.Equal(NormalizedEmail, graph.User.Email);
        Assert.True(graph.User.IsEmailVerified);

        Assert.Equal("Acme Plumbing", graph.Account.BusinessName);
        Assert.Equal(AccountPurpose.Business, graph.Account.Purpose);

        Assert.Equal(AccountUserRole.Owner, graph.Owner.Role);
        Assert.Equal(MembershipStatus.Active, graph.Owner.MembershipStatus);
        Assert.Equal(graph.User.Id, graph.Owner.UserId);
        Assert.Equal(graph.Account.Id, graph.Owner.AccountId);

        Assert.Equal(graph.Account.Id, graph.Entitlements.AccountId);
        Assert.Equal(AccountPlan.Starter, graph.Entitlements.Plan);
        Assert.Equal(AccountCommercialState.Trial, graph.Entitlements.CommercialState);
        Assert.Equal(TrialEnds, graph.Entitlements.TrialEndsAtUtc);
        Assert.False(graph.Entitlements.IsPilot);
    }

    [Fact]
    public void Pilot_provisions_pilot_entitlements()
    {
        var result = Service.CreateVerified(
            Email, "Pat Owner", "Acme Plumbing", AccountPurpose.Business, "America/New_York",
            AccountPlan.Professional, isPilot: true, Now, TrialEnds);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Entitlements.IsPilot);
        Assert.Equal(AccountCommercialState.Trial, result.Value.Entitlements.CommercialState);
    }

    [Fact]
    public void Internal_provisions_internal_account_and_entitlements()
    {
        var result = Service.CreateVerified(
            Email, "Pat Admin", "OpHalo Internal", AccountPurpose.Internal, "UTC",
            AccountPlan.Internal, isPilot: false, Now, trialEndsAtUtc: null);

        Assert.True(result.IsSuccess);
        var graph = result.Value;

        Assert.Equal(AccountPurpose.Internal, graph.Account.Purpose);
        Assert.Equal(AccountPlan.Internal, graph.Entitlements.Plan);
        Assert.Equal(AccountCommercialState.Active, graph.Entitlements.CommercialState);
        Assert.Null(graph.Entitlements.TrialEndsAtUtc);
        Assert.False(graph.Entitlements.IsPilot);
        Assert.Equal(AccountUserRole.Owner, graph.Owner.Role);
    }

    // --- Primary owner -------------------------------------------------------

    [Fact]
    public void Primary_owner_is_assigned_to_the_created_owner_membership()
    {
        var result = Service.CreateVerified(
            Email, null, "Acme Plumbing", AccountPurpose.Business, "America/New_York",
            AccountPlan.Starter, isPilot: false, Now, TrialEnds);

        Assert.True(result.IsSuccess);
        Assert.Equal(result.Value.Owner.Id, result.Value.Account.PrimaryOwnerAccountUserId);
    }

    // --- Plan-derived seats --------------------------------------------------

    [Theory]
    [InlineData(AccountPlan.Starter)]
    [InlineData(AccountPlan.Professional)]
    [InlineData(AccountPlan.Business)]
    public void MaxUserSeats_is_sourced_from_plan_defaults(AccountPlan plan)
    {
        var result = Service.CreateVerified(
            Email, null, "Acme Plumbing", AccountPurpose.Business, "America/New_York",
            plan, isPilot: false, Now, TrialEnds);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanSeats(plan), result.Value.Entitlements.MaxUserSeats);
    }

    [Fact]
    public void Internal_seats_are_sourced_from_internal_plan_default()
    {
        var result = Service.CreateVerified(
            Email, null, "OpHalo Internal", AccountPurpose.Internal, "UTC",
            AccountPlan.Internal, isPilot: false, Now, trialEndsAtUtc: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanSeats(AccountPlan.Internal), result.Value.Entitlements.MaxUserSeats);
    }

    // --- Fail-closed composition rules ---------------------------------------

    [Fact]
    public void Internal_purpose_with_non_internal_plan_fails()
    {
        var result = Service.CreateVerified(
            Email, null, "OpHalo Internal", AccountPurpose.Internal, "UTC",
            AccountPlan.Starter, isPilot: false, Now, trialEndsAtUtc: null);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.InternalAccountPlanMismatch, result.Error);
    }

    [Fact]
    public void Business_purpose_with_internal_plan_fails()
    {
        var result = Service.CreateVerified(
            Email, null, "Acme Plumbing", AccountPurpose.Business, "America/New_York",
            AccountPlan.Internal, isPilot: false, Now, TrialEnds);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.InternalAccountPlanMismatch, result.Error);
    }

    [Fact]
    public void Internal_account_as_pilot_fails()
    {
        var result = Service.CreateVerified(
            Email, null, "OpHalo Internal", AccountPurpose.Internal, "UTC",
            AccountPlan.Internal, isPilot: true, Now, trialEndsAtUtc: null);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.InternalAccountCannotBePilot, result.Error);
    }

    [Fact]
    public void Internal_account_with_trial_window_fails()
    {
        var result = Service.CreateVerified(
            Email, null, "OpHalo Internal", AccountPurpose.Internal, "UTC",
            AccountPlan.Internal, isPilot: false, Now, TrialEnds);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.InternalAccountAllowsNoTrialWindow, result.Error);
    }

    [Fact]
    public void Non_internal_account_without_trial_window_fails()
    {
        var result = Service.CreateVerified(
            Email, null, "Acme Plumbing", AccountPurpose.Business, "America/New_York",
            AccountPlan.Starter, isPilot: false, Now, trialEndsAtUtc: null);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.TrialWindowRequired, result.Error);
    }
}
