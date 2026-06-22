using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.Requests;

public sealed record AddCustomerMessageCommand(
    string PageToken,
    MessageIntent Intent,
    string Message,
    Guid ExpectedVersion);
