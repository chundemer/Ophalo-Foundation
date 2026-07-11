namespace OpHalo.Api.Keep;

public sealed record BusinessPriorityRequest(string? Priority, Guid ExpectedVersion);
