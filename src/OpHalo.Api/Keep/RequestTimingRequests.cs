namespace OpHalo.Api.Keep;

public sealed record SetFollowUpOnRequestBody(string Date, string Reason, string? Note);
public sealed record SetPlannedForRequestBody(string Date);
