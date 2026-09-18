using OpHalo.Foundation.Core.Entities.Shared;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// A full-day local-date closure of an account's calendar (ADR-505 V1: holidays and
/// ad-hoc shutdowns). Evaluated against the account-local date, never a UTC instant.
/// </summary>
public sealed class KeepCalendarClosure : BaseEntity
{
    public Guid AccountId { get; private set; }
    public DateOnly ClosureDate { get; private set; }
    public string? Label { get; private set; }

    public static KeepCalendarClosure Create(Guid accountId, DateOnly closureDate, string? label = null)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));

        return new KeepCalendarClosure
        {
            AccountId = accountId,
            ClosureDate = closureDate,
            Label = label
        };
    }
}
