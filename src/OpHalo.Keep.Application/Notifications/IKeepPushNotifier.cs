namespace OpHalo.Keep.Application.Notifications;

/// <summary>
/// Post-commit push notification pipeline (ADR-354).
/// Called explicitly by mutation services after successful persistence.
/// Failures are fail-soft — they must never fail the calling request.
/// </summary>
public interface IKeepPushNotifier
{
    Task SendAsync(KeepPushRoutingContext context, CancellationToken ct = default);
}
