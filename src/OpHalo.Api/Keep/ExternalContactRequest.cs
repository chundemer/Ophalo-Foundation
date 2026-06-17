namespace OpHalo.Api.Keep;

public sealed record ExternalContactRequestBody(
    string Direction,
    string Channel,
    string? Outcome,
    bool? RequiresBusinessFollowUp,
    string? Summary);
