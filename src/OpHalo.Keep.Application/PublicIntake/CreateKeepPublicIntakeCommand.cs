using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.PublicIntake;

public sealed record CreateKeepPublicIntakeCommand(
    string PublicIntakeToken,
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail,
    string Description,
    string ServiceAddressLine1,
    string? ServiceAddressLine2,
    string ServiceCity,
    string ServiceState,
    string? ServiceZip,
    IntakeUrgency IntakeUrgency = IntakeUrgency.Routine);
