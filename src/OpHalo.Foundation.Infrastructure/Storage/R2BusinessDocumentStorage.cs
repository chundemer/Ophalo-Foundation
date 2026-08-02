using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using OpHalo.Foundation.Application.Abstractions.Storage;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Infrastructure.Storage;

/// <summary>
/// Private Cloudflare R2 implementation of <see cref="IBusinessDocumentStorage"/> (ADR-471),
/// accessed through the S3-compatible AWS SDK. Production/pilot backend; local disk is
/// development/test only.
/// </summary>
public sealed class R2BusinessDocumentStorage : IBusinessDocumentStorage, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly string _bucketName;
    private readonly ILogger<R2BusinessDocumentStorage> _logger;

    public R2BusinessDocumentStorage(R2Settings settings, ILogger<R2BusinessDocumentStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsConfigured)
            throw new InvalidOperationException("R2Settings is incomplete; storage cannot start.");

        _bucketName = settings.BucketName;
        _logger = logger;

        var config = new AmazonS3Config
        {
            ServiceURL = $"https://{settings.CloudflareAccountId}.r2.cloudflarestorage.com",
            ForcePathStyle = true,
        };
        _client = new AmazonS3Client(
            new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey),
            config);
    }

    public async Task<Result<string>> PutAsync(
        Guid accountId,
        DocumentPurpose purpose,
        Stream content,
        CancellationToken cancellationToken)
    {
        var objectKey = BusinessDocumentObjectKey.Generate(accountId, purpose);

        try
        {
            await _client.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = _bucketName,
                    Key = objectKey,
                    InputStream = content,
                    AutoCloseStream = false,
                },
                cancellationToken);

            return Result<string>.Success(objectKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "R2 upload failed for object key {ObjectKey}.", objectKey);
            return Result<string>.Failure(BusinessDocumentStorageErrors.UploadFailed);
        }
    }

    public async Task DeleteBestEffortAsync(
        Guid accountId,
        DocumentPurpose purpose,
        string objectKey,
        CancellationToken cancellationToken)
    {
        if (!BusinessDocumentObjectKey.BelongsTo(accountId, purpose, objectKey))
        {
            _logger.LogWarning(
                "Refusing best-effort R2 delete: object key {ObjectKey} does not match the account/purpose it was generated for.",
                objectKey);
            return;
        }

        try
        {
            await _client.DeleteObjectAsync(_bucketName, objectKey, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Best-effort R2 delete failed for object key {ObjectKey}; object may be orphaned.", objectKey);
        }
    }

    public void Dispose() => _client.Dispose();
}
