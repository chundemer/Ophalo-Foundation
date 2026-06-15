namespace OpHalo.Foundation.Infrastructure.Email;

/// <summary>Bound from the "Resend" configuration section.</summary>
public sealed class ResendSettings
{
    public string ApiKey { get; init; } = string.Empty;
    public string FromAddress { get; init; } = string.Empty;
}
