namespace OpHalo.Api.Keep;

public sealed record PublicIntakeRequest(
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail,
    string Description);
