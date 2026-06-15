namespace OpHalo.Keep.Application.PublicIntake;

public sealed record CreateKeepPublicIntakeCommand(
    string PublicIntakeToken,
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail,
    string Description);
