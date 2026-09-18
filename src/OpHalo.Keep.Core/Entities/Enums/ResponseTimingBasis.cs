namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// How a response target's elapsed time is measured (ADR-505).
/// </summary>
public enum ResponseTimingBasis
{
    Continuous  = 1,
    StaffedHours = 2
}
