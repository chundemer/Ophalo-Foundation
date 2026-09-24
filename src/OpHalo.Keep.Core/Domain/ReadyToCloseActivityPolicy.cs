using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Core.Domain;

/// <summary>
/// DEF-063: the sole definition of the ready-to-close customer-activity warning signal. A
/// `Resolved` request may still have customer activity the business has not yet followed up on —
/// internal notes do not advance `LastBusinessActivityAt`, so this catches staff acknowledging
/// attention without completing real business follow-up. Shared by the list row projection
/// (`KeepRequestSummary.ReadyToClose`) and the Request Detail projection
/// (`KeepRequestDetailResult.ReadyToClose`) so there is one rule, not two independently
/// maintained comparisons. This is a read-only, informational signal: it does not affect close
/// eligibility, status transitions, or attention computation.
/// </summary>
public static class ReadyToCloseActivityPolicy
{
    public static bool HasCustomerActivityAfterResolution(
        KeepRequestStatus status,
        DateTime? lastCustomerActivityAt,
        DateTime? lastBusinessActivityAt) =>
        status == KeepRequestStatus.Resolved
        && lastCustomerActivityAt.HasValue
        && lastBusinessActivityAt.HasValue
        && lastCustomerActivityAt.Value > lastBusinessActivityAt.Value;
}
