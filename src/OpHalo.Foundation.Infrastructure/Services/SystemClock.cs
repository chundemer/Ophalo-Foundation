using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.Foundation.Infrastructure.Services;

/// <summary>
/// Wall-clock implementation of <see cref="IClock"/>. Ported from the reference app,
/// collapsed onto the single SharedKernel <c>IClock</c> (Phase 3) — the legacy dual
/// AppClock/SharedClock implementation is no longer needed.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
