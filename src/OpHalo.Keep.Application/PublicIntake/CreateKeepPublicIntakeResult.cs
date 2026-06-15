namespace OpHalo.Keep.Application.PublicIntake;

public sealed record CreateKeepPublicIntakeResult(
    Guid RequestId,
    string ReferenceCode,
    string PageToken);
