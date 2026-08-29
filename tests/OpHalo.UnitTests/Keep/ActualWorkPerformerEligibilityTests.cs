using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.PriceBook;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// ADR-494 D2: the shared performer-eligibility predicate. A performer must be an active member
/// holding both <c>RequestsOperate</c> and <c>ActualWorkCapture</c> — the recorder predicate minus
/// the Owner/Admin restriction, so an Operator office transcriber qualifies.
/// </summary>
public sealed class ActualWorkPerformerEligibilityTests
{
    private readonly UserAccessPolicy _policy = new();

    [Theory]
    [InlineData(AccountUserRole.Owner)]
    [InlineData(AccountUserRole.Admin)]
    [InlineData(AccountUserRole.Operator)]
    public void ActiveStaffRoles_AreEligible(AccountUserRole role)
    {
        Assert.True(ActualWorkPerformerEligibility.IsEligible(
            _policy, role, MembershipStatus.Active, AccountPurpose.Business));
    }

    [Fact]
    public void Viewer_IsNotEligible()
    {
        Assert.False(ActualWorkPerformerEligibility.IsEligible(
            _policy, AccountUserRole.Viewer, MembershipStatus.Active, AccountPurpose.Business));
    }

    [Theory]
    [InlineData(MembershipStatus.Invited)]
    [InlineData(MembershipStatus.Suspended)]
    [InlineData(MembershipStatus.Removed)]
    public void NonActiveOperator_IsNotEligible(MembershipStatus status)
    {
        Assert.False(ActualWorkPerformerEligibility.IsEligible(
            _policy, AccountUserRole.Operator, status, AccountPurpose.Business));
    }
}
