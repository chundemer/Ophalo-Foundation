using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Errors;

public static class KeepResponsePolicyErrors
{
    public static readonly Error StaffedTimingRequiresWeeklyInterval = Error.Create(
        "KeepResponsePolicy.StaffedTimingRequiresWeeklyInterval",
        "A response target can only use staffed-hours timing when the account has at least one weekly open interval.");

    public static readonly Error LastWeeklyIntervalRequired = Error.Create(
        "KeepResponsePolicy.LastWeeklyIntervalRequired",
        "The last weekly open interval cannot be removed while a response target uses staffed-hours timing.");

    public static readonly Error StaffedHoursTargetUnreachable = Error.Create(
        "KeepResponsePolicy.StaffedHoursTargetUnreachable",
        "This response target cannot be reached within five years under the account's calendar. Adjust the target or the calendar.");

    public static readonly Error ConcurrentSettingsChange = Error.Create(
        "KeepResponsePolicy.ConcurrentSettingsChange",
        "These settings were just updated by another change. Please retry.");

    public static readonly Error InvalidTimeZone = Error.Create(
        "KeepResponsePolicy.InvalidTimeZone",
        "Time zone is not a valid IANA time zone identifier.");

    public static readonly Error DuplicateWeekday = Error.Create(
        "KeepResponsePolicy.DuplicateWeekday",
        "The weekly calendar cannot include more than one interval for the same weekday.");

    public static readonly Error DuplicateClosureDate = Error.Create(
        "KeepResponsePolicy.DuplicateClosureDate",
        "The same closure date cannot be listed more than once in the same change.");

    public static readonly Error OverlappingClosureChange = Error.Create(
        "KeepResponsePolicy.OverlappingClosureChange",
        "The same closure date cannot be both added and removed in the same change.");
}
