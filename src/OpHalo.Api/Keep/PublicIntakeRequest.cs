namespace OpHalo.Api.Keep;

public sealed record PublicIntakeRequest(
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail,
    string Description,
    string? ServiceAddressLine1,
    string? ServiceAddressLine2,
    string? ServiceCity,
    string? ServiceState,
    string? ServiceZip);
