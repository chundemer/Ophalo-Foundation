namespace OpHalo.Foundation.Infrastructure.Storage;

/// <summary>
/// Bound from the "R2:BusinessDocuments" configuration section (ADR-503). Scoped to the
/// `ophalo-business-documents` bucket — private tenant/customer artifacts only.
/// </summary>
public sealed class BusinessDocumentsR2Settings
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
