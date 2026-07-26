namespace OpHalo.Api.Keep;

public sealed record ConfirmUpdateNotificationRequestBody(
    Guid RelatedUpdateEventId,
    string Channel);
