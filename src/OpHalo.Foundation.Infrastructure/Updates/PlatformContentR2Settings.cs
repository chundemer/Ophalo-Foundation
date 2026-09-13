namespace OpHalo.Foundation.Infrastructure.Updates;

/// <summary>
/// Bound from the "R2:PlatformContent" configuration section (ADR-503). Scoped to the
/// `ophalo-platform-content` bucket — founder-authored editorial content only (Help & Updates
/// feed and guide images).
/// </summary>
public sealed class PlatformContentR2Settings
{
    public string CloudflareAccountId { get; init; } = string.Empty;
    public string BucketName { get; init; } = string.Empty;
    public string AccessKeyId { get; init; } = string.Empty;
    public string SecretAccessKey { get; init; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CloudflareAccountId)
        && !string.IsNullOrWhiteSpace(BucketName)
        && !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey);
}
