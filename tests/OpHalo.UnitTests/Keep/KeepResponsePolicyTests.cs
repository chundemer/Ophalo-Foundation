using OpHalo.Keep.Core.Entities;

namespace OpHalo.UnitTests.Keep;

public class KeepResponsePolicyTests
{
    static readonly Guid AccountId = Guid.NewGuid();

    static KeepResponsePolicy NewPolicy(
        int first = 15,
        int standard = 240,
        int priority = 60,
        int threshold = 5) =>
        KeepResponsePolicy.Create(AccountId, first, standard, priority, threshold);

    // --- Create ---

    [Fact]
    public void Create_sets_all_fields()
    {
        var policy = NewPolicy();

        Assert.Equal(AccountId, policy.AccountId);
        Assert.Equal(15, policy.FirstResponseTargetMinutes);
        Assert.Equal(240, policy.StandardResponseTargetMinutes);
        Assert.Equal(60, policy.PriorityResponseTargetMinutes);
        Assert.Equal(5, policy.StatusCheckThresholdDays);
    }

    [Fact]
    public void Create_requires_non_empty_account_id() =>
        Assert.Throws<ArgumentException>(() =>
            KeepResponsePolicy.Create(Guid.Empty, 15, 240, 60, 5));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_rejects_non_positive_first_response(int minutes) =>
        Assert.Throws<ArgumentException>(() =>
            KeepResponsePolicy.Create(AccountId, minutes, 240, 60, 5));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_rejects_non_positive_standard_response(int minutes) =>
        Assert.Throws<ArgumentException>(() =>
            KeepResponsePolicy.Create(AccountId, 15, minutes, 60, 5));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_rejects_non_positive_priority_response(int minutes) =>
        Assert.Throws<ArgumentException>(() =>
            KeepResponsePolicy.Create(AccountId, 15, 240, minutes, 5));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_rejects_non_positive_status_check_threshold(int days) =>
        Assert.Throws<ArgumentException>(() =>
            KeepResponsePolicy.Create(AccountId, 15, 240, 60, days));

    // --- Update ---

    [Fact]
    public void Update_changes_all_fields()
    {
        var policy = NewPolicy();

        policy.Update(30, 480, 90, 10);

        Assert.Equal(30, policy.FirstResponseTargetMinutes);
        Assert.Equal(480, policy.StandardResponseTargetMinutes);
        Assert.Equal(90, policy.PriorityResponseTargetMinutes);
        Assert.Equal(10, policy.StatusCheckThresholdDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Update_rejects_non_positive_first_response(int minutes) =>
        Assert.Throws<ArgumentException>(() => NewPolicy().Update(minutes, 240, 60, 5));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Update_rejects_non_positive_standard_response(int minutes) =>
        Assert.Throws<ArgumentException>(() => NewPolicy().Update(15, minutes, 60, 5));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Update_rejects_non_positive_priority_response(int minutes) =>
        Assert.Throws<ArgumentException>(() => NewPolicy().Update(15, 240, minutes, 5));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Update_rejects_non_positive_status_check_threshold(int days) =>
        Assert.Throws<ArgumentException>(() => NewPolicy().Update(15, 240, 60, days));
}
