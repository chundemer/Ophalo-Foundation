using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Application.Push;

public sealed record PushMessage(
    AccountUserDevicePlatform Platform,
    string DeviceToken,
    string Title,
    string Body,
    string EventKind,
    string CollapseKey,
    PushPriority Priority,
    TimeSpan Ttl,
    IReadOnlyDictionary<string, string> Payload);

public enum PushPriority { Normal = 1, High = 2 }

public sealed record PushDeliveryResult(bool IsNoOp, string? FailureReason = null)
{
    public static readonly PushDeliveryResult Sent = new(IsNoOp: false);
    public static readonly PushDeliveryResult NoOp = new(IsNoOp: true);
    public static PushDeliveryResult Failure(string reason) => new(IsNoOp: false, reason);
}

/// <summary>
/// Abstraction over APNs/FCM push delivery. V1 ships with NoOpPushAdapter only.
/// Real adapters are gated on Demo/InternalTest suppression (ADR-353).
/// Do not log DeviceToken from PushMessage — it is sensitive.
/// </summary>
public interface IPushAdapter
{
    Task<PushDeliveryResult> SendAsync(PushMessage message, CancellationToken ct = default);
}
