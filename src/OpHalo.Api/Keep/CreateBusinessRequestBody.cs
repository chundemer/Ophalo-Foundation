namespace OpHalo.Api.Keep;

public sealed record CreateBusinessRequestBody(
    string? CustomerName,
    string? CustomerPhone,
    string? CustomerEmail,
    string? Description,
    string? Source);
