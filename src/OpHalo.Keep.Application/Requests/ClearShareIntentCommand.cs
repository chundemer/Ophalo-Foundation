namespace OpHalo.Keep.Application.Requests;

public sealed record ClearShareIntentCommand(Guid RequestId, string Method);
