namespace OpHalo.Api.Keep;

public sealed record PrepareUpdateNotificationRequestBody(
    Guid RelatedUpdateEventId,
    string Channel);
