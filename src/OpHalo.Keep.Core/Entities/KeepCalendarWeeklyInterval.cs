using OpHalo.Foundation.Core.Entities.Shared;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// One account's staffed-open interval for a single weekday (ADR-505 V1 calendar).
/// A weekday with no row is closed. The interval is half-open: OpensAt is included,
/// ClosesAt is excluded, and OpensAt must precede ClosesAt on the same local day.
/// </summary>
public sealed class KeepCalendarWeeklyInterval : BaseEntity
{
    public Guid AccountId { get; private set; }
    public DayOfWeek Weekday { get; private set; }
    public TimeOnly OpensAt { get; private set; }
    public TimeOnly ClosesAt { get; private set; }

    public static KeepCalendarWeeklyInterval Create(
        Guid accountId,
        DayOfWeek weekday,
        TimeOnly opensAt,
        TimeOnly closesAt)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (opensAt >= closesAt)
            throw new ArgumentException("Open time must be before close time on the same local day.", nameof(opensAt));

        return new KeepCalendarWeeklyInterval
        {
            AccountId = accountId,
            Weekday = weekday,
            OpensAt = opensAt,
            ClosesAt = closesAt
        };
    }

    public void Update(TimeOnly opensAt, TimeOnly closesAt)
    {
        if (opensAt >= closesAt)
            throw new ArgumentException("Open time must be before close time on the same local day.", nameof(opensAt));

        OpensAt = opensAt;
        ClosesAt = closesAt;
    }
}
