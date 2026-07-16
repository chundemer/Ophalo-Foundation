namespace OpHalo.Keep.Application.Requests;

public sealed record CreateBusinessRequestCommand(
    string? CustomerName,
    string? CustomerPhone,
    string? CustomerEmail,
    string? Description,
    string? Source,
    string? ServiceAddressLine1 = null,
    string? ServiceAddressLine2 = null,
    string? ServiceCity = null,
    string? ServiceState = null,
    string? ServiceZip = null);
