using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Errors;

public static class QuickScopeActionErrors
{
    public static readonly Error TargetRequired =
        Error.Create("QuickScopeAction.TargetRequired", "A catalog item or offering/assembly target is required.");

    public static readonly Error TargetMustBeExclusive =
        Error.Create("QuickScopeAction.TargetMustBeExclusive", "A quick scope action must target exactly one catalog item or offering/assembly, not both.");

    public static readonly Error OrderOutOfRange =
        Error.Create("QuickScopeAction.OrderOutOfRange", "Order must be between 1 and 6.");

    public static readonly Error TooManySlots =
        Error.Create("QuickScopeAction.TooManySlots", "At most six quick scope actions may be configured.");

    public static readonly Error DuplicateOrder =
        Error.Create("QuickScopeAction.DuplicateOrder", "Each quick scope action must have a distinct order.");

    public static readonly Error DuplicateTarget =
        Error.Create("QuickScopeAction.DuplicateTarget", "Each catalog item or offering/assembly may be configured as a quick scope action only once.");
}
