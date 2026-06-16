using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed class GetKeepCustomerPageService(
    IKeepRequestDetailPersistence persistence,
    IClock clock)
{
    public async Task<Result<KeepCustomerPageResult>> ExecuteAsync(
        string pageToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pageToken))
            return Result<KeepCustomerPageResult>.Failure(KeepRequestErrors.NotFound);

        var lookup = await persistence.GetRequestByPageTokenAsync(pageToken, ct);
        if (lookup is null)
            return Result<KeepCustomerPageResult>.Failure(KeepRequestErrors.NotFound);

        var request = lookup.Request;

        if (request.ExpiresAtUtc.HasValue && request.ExpiresAtUtc.Value <= clock.UtcNow)
            return Result<KeepCustomerPageResult>.Success(new KeepCustomerPageResult(
                BusinessName: lookup.BusinessName,
                ReferenceCode: request.ReferenceCode,
                IsExpired: true,
                NewRequestUrl: null,
                Status: null,
                Description: null,
                CurrentStatusText: null,
                IsTerminal: null,
                FeedbackWasResolved: null,
                FeedbackSubmittedAtUtc: null,
                ExpiresAtUtc: null,
                Events: null,
                AllowedActions: null));

        var events = await persistence.GetCustomerVisibleEventsAsync(request.Id, ct);

        return Result<KeepCustomerPageResult>.Success(new KeepCustomerPageResult(
            BusinessName: lookup.BusinessName,
            ReferenceCode: request.ReferenceCode,
            IsExpired: false,
            NewRequestUrl: null,
            Status: MapStatus(request.Status),
            Description: request.Description,
            CurrentStatusText: request.CurrentStatusText,
            IsTerminal: request.IsTerminal,
            FeedbackWasResolved: request.FeedbackWasResolved,
            FeedbackSubmittedAtUtc: request.FeedbackSubmittedAtUtc,
            ExpiresAtUtc: request.ExpiresAtUtc,
            // Defensive filter even though persistence already scopes to Visibility = All.
            // A future persistence change must not accidentally expose internal events
            // through this public endpoint.
            Events: events
                .Where(e => e.Visibility == KeepRequestEventVisibility.All)
                .Select(MapEvent)
                .ToList(),
            AllowedActions: []));
    }

    private static KeepCustomerPageEventItem MapEvent(KeepRequestEvent e) => new(
        MapEventType(e.EventType),
        e.Content,
        e.OccurredAtUtc,
        MapActorLabel(e.ActorType));

    private static string MapStatus(KeepRequestStatus status) => status switch
    {
        KeepRequestStatus.Received        => "received",
        KeepRequestStatus.InProgress      => "in_progress",
        KeepRequestStatus.PendingCustomer => "pending_customer",
        KeepRequestStatus.Resolved        => "resolved",
        KeepRequestStatus.Closed          => "closed",
        KeepRequestStatus.Cancelled       => "cancelled",
        _ => throw new InvalidOperationException($"Unknown KeepRequestStatus: {status}")
    };

    private static string MapEventType(KeepRequestEventType type) => type switch
    {
        KeepRequestEventType.RequestCreated        => "request_created",
        KeepRequestEventType.StatusChanged         => "status_changed",
        KeepRequestEventType.MessageAdded          => "message_added",
        KeepRequestEventType.RequestClosed         => "request_closed",
        KeepRequestEventType.RequestCancelled      => "request_cancelled",
        KeepRequestEventType.InternalNoteAdded     => "internal_note_added",
        KeepRequestEventType.AttentionAcknowledged => "attention_acknowledged",
        _ => throw new InvalidOperationException($"Unknown KeepRequestEventType: {type}")
    };

    private static string MapActorLabel(ActorType actorType) => actorType switch
    {
        ActorType.Customer    => "customer",
        ActorType.AccountUser => "business",
        ActorType.System      => "system",
        _ => throw new InvalidOperationException($"Unknown ActorType: {actorType}")
    };
}
