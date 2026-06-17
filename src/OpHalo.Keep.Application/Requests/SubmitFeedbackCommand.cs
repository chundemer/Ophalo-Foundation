namespace OpHalo.Keep.Application.Requests;

public sealed record SubmitFeedbackCommand(
    string PageToken,
    bool WasResolved,
    string? Comment);
