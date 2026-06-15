namespace OpHalo.Api.Keep;

public sealed record PublicIntakeRequest(
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail,
    string Description,
    bool? EmailNotificationsEnabled); // accepted for legacy contract continuity (ADR-059), ignored until notifications phase
