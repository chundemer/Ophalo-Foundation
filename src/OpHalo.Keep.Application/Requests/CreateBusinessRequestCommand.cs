namespace OpHalo.Keep.Application.Requests;

public sealed record CreateBusinessRequestCommand(
    string? CustomerName,
    string? CustomerPhone,
    string? CustomerEmail,
    string? Description);
