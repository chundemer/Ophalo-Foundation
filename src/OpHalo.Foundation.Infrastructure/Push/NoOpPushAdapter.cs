using OpHalo.Foundation.Application.Push;

namespace OpHalo.Foundation.Infrastructure.Push;

/// <summary>
/// Delivery stub used while real APNs/FCM adapters are pending (ADR-352/353).
/// All sends succeed silently. Swap for a real adapter once delivery gates are cleared.
/// </summary>
public sealed class NoOpPushAdapter : IPushAdapter
{
    public Task<PushDeliveryResult> SendAsync(PushMessage message, CancellationToken ct = default)
        => Task.FromResult(PushDeliveryResult.NoOp);
}
