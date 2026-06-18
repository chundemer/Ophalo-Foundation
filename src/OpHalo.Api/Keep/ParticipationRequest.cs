namespace OpHalo.Api.Keep;

public sealed record SetResponsibleRequestBody(
    Guid AccountUserId,
    string? Note);

public sealed record ClearResponsibleRequestBody(
    string? Note);

public sealed record WatcherRequestBody(
    string? Note);
