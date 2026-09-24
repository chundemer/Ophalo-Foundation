using OpHalo.Keep.Core.Domain;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Focused coverage for <see cref="ReadyToCloseActivityPolicy"/> (DEF-063): the sole definition of
/// the ready-to-close customer-activity warning signal, shared by the list row projection
/// (<c>GetKeepRequestListService.BuildReadyToCloseInfo</c>) and the Request Detail projection
/// (<c>KeepRequestDetailMapper.ToDetailResult</c>).
/// </summary>
public class ReadyToCloseActivityPolicyTests
{
    private static readonly DateTime Earlier = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void True_when_resolved_and_customer_activity_after_business_activity()
    {
        Assert.True(ReadyToCloseActivityPolicy.HasCustomerActivityAfterResolution(
            KeepRequestStatus.Resolved, lastCustomerActivityAt: Later, lastBusinessActivityAt: Earlier));
    }

    [Fact]
    public void False_when_resolved_and_business_activity_after_customer_activity()
    {
        Assert.False(ReadyToCloseActivityPolicy.HasCustomerActivityAfterResolution(
            KeepRequestStatus.Resolved, lastCustomerActivityAt: Earlier, lastBusinessActivityAt: Later));
    }

    [Theory]
    [InlineData(KeepRequestStatus.Received)]
    [InlineData(KeepRequestStatus.InProgress)]
    [InlineData(KeepRequestStatus.PendingCustomer)]
    [InlineData(KeepRequestStatus.Scheduled)]
    [InlineData(KeepRequestStatus.Closed)]
    [InlineData(KeepRequestStatus.Cancelled)]
    [InlineData(KeepRequestStatus.Spam)]
    [InlineData(KeepRequestStatus.Test)]
    public void False_when_status_is_not_resolved_even_with_qualifying_timestamps(KeepRequestStatus status)
    {
        Assert.False(ReadyToCloseActivityPolicy.HasCustomerActivityAfterResolution(
            status, lastCustomerActivityAt: Later, lastBusinessActivityAt: Earlier));
    }

    [Fact]
    public void False_when_last_customer_activity_is_null()
    {
        Assert.False(ReadyToCloseActivityPolicy.HasCustomerActivityAfterResolution(
            KeepRequestStatus.Resolved, lastCustomerActivityAt: null, lastBusinessActivityAt: Earlier));
    }

    [Fact]
    public void False_when_last_business_activity_is_null()
    {
        Assert.False(ReadyToCloseActivityPolicy.HasCustomerActivityAfterResolution(
            KeepRequestStatus.Resolved, lastCustomerActivityAt: Later, lastBusinessActivityAt: null));
    }
}
