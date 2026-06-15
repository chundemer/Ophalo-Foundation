namespace OpHalo.Keep.Application.Requests;

public sealed record KeepRequestSummary(
    Guid Id,
    string ReferenceCode,
    string Status,
    string? CurrentStatusText,
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail,
    string Description,
    DateTime? LastCustomerActivityAtUtc,
    DateTime LastBusinessActivityAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
