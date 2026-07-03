using Microsoft.Extensions.Logging;
using OpHalo.Foundation.Application.Devices;
using OpHalo.Foundation.Application.Push;

namespace OpHalo.Keep.Application.Notifications;

/// <summary>
/// Full push delivery pipeline: candidate routing → device lookup → payload build → adapter send.
/// All exceptions are caught and logged; the calling mutation is never failed by push errors (ADR-354).
/// </summary>
public sealed class KeepPushNotifier(
    KeepPushCandidateService candidateService,
    IAccountUserDevicePersistence devicePersistence,
    IPushAdapter pushAdapter,
    ILogger<KeepPushNotifier> logger) : IKeepPushNotifier
{
    // High-urgency events: short TTL ~1h, high priority.
    private static readonly IReadOnlySet<KeepPushEventKind> HighPriorityKinds =
        new HashSet<KeepPushEventKind>
        {
            KeepPushEventKind.CallRequested,
            KeepPushEventKind.CancellationRequested,
            KeepPushEventKind.TimingChangeRequested
        };

    public async Task SendAsync(KeepPushRoutingContext context, CancellationToken ct = default)
    {
        try
        {
            var candidateIds = candidateService.GetCandidates(context);
            if (candidateIds.Count == 0)
                return;

            var devices = await devicePersistence.FindActiveDevicesForDeliveryAsync(
                context.AccountId, candidateIds, ct);

            if (devices.Count == 0)
                return;

            var display = KeepPushDisplayMapper.GetDisplay(context.EventKind);
            var eventKindString = KeepPushDisplayMapper.ToPayloadString(context.EventKind);
            var collapseKey = $"{context.AccountId}:{context.RequestId}";
            var priority = HighPriorityKinds.Contains(context.EventKind) ? PushPriority.High : PushPriority.Normal;
            var ttl = priority == PushPriority.High ? TimeSpan.FromHours(1) : TimeSpan.FromHours(8);

            var payload = new Dictionary<string, string>
            {
                ["type"] = "keep_request_attention",
                ["accountId"] = context.AccountId.ToString(),
                ["requestId"] = context.RequestId.ToString(),
                ["eventKind"] = eventKindString,
                ["deepLink"] = $"ophalo://keep/requests/{context.RequestId}"
            };

            foreach (var device in devices)
            {
                var message = new PushMessage(
                    Platform: device.Platform,
                    DeviceToken: device.PushToken!,
                    Title: display.Title,
                    Body: display.Body,
                    EventKind: eventKindString,
                    CollapseKey: collapseKey,
                    Priority: priority,
                    Ttl: ttl,
                    Payload: payload);

                try
                {
                    await pushAdapter.SendAsync(message, ct);
                }
                catch (Exception ex)
                {
                    // Fail-soft: log without token, continue to remaining devices.
                    logger.LogWarning(ex,
                        "Push delivery failed for account {AccountId} request {RequestId} device {DeviceId}",
                        context.AccountId, context.RequestId, device.Id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Push candidate resolution failed for account {AccountId} request {RequestId}",
                context.AccountId, context.RequestId);
        }
    }
}
