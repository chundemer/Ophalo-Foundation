namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// Canonical unsaved-policy fallback targets (ADR-505). Used by every no-policy writer path
/// so an account with no <see cref="KeepResponsePolicy"/> row reports the same continuous-timing
/// defaults everywhere.
/// </summary>
public static class KeepResponsePolicyDefaults
{
    public const int FirstResponseTargetMinutes = 60;
    public const int StandardResponseTargetMinutes = 240;
    public const int PriorityResponseTargetMinutes = 60;
}
