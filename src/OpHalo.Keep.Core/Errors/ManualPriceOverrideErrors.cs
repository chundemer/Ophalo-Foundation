using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Errors;

public static class ManualPriceOverrideErrors
{
    public static readonly Error ReasonRequired =
        Error.Create("ManualPriceOverride.ReasonRequired", "A reason is required for a manual price override.");

    public static readonly Error ReasonTooLong =
        Error.Create("ManualPriceOverride.ReasonTooLong", "Reason must not exceed 500 characters.");
}
