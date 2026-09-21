using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.ResponseTiming;

/// <summary>
/// Reuse boundary for every response-deadline writer (ADR-505): public intake, customer messages,
/// feedback, and inbound external contact. Domain scalars only; the Infrastructure implementation
/// owns the read consistency.
/// </summary>
public interface IKeepResponseTimingSnapshotPersistence
{
    /// <summary>
    /// Returns the account's response targets and timing bases. With no policy row the canonical
    /// unsaved defaults apply (60/240/60 minutes, all Continuous). When every basis is Continuous
    /// only the policy is loaded; when any basis is StaffedHours the policy, account timezone,
    /// weekly intervals, and closures are read from one consistent snapshot so a concurrent
    /// settings save cannot produce a torn calendar.
    /// </summary>
    Task<KeepResponseTimingSnapshot> GetResponseTimingSnapshotAsync(Guid accountId, CancellationToken ct);
}

/// <summary>One coherent policy/calendar snapshot for the business clock (ADR-505).</summary>
/// <param name="Calendar">Null when every timing basis is Continuous.</param>
public sealed record KeepResponseTimingSnapshot(
    int FirstResponseTargetMinutes,
    ResponseTimingBasis FirstResponseTimingBasis,
    int StandardResponseTargetMinutes,
    ResponseTimingBasis StandardResponseTimingBasis,
    int PriorityResponseTargetMinutes,
    ResponseTimingBasis PriorityResponseTimingBasis,
    KeepResponseTimingCalendar? Calendar);

/// <param name="TimeZoneId">The account's stored IANA id, unresolved; the resolver validates it.</param>
public sealed record KeepResponseTimingCalendar(
    string TimeZoneId,
    IReadOnlyList<KeepWeeklyIntervalSnapshot> WeeklyIntervals,
    IReadOnlyList<DateOnly> ClosureDates);
