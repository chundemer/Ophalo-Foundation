namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Bound from the "MagicLinkDispatch" configuration section (GAP-095 095-1).
/// </summary>
public sealed class MagicLinkDispatchSettings
{
    /// <summary>
    /// Bounded capacity of the in-memory dispatch channel. Items enqueued beyond this depth
    /// are dropped (logged, not delivered) rather than blocking the caller. Starting value
    /// per BL155; tune from observed pilot drop-rate.
    /// </summary>
    public int QueueCapacity { get; init; } = 256;
}
