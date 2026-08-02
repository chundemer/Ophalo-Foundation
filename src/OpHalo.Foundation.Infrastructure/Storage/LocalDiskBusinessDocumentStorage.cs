using Microsoft.Extensions.Logging;
using OpHalo.Foundation.Application.Abstractions.Storage;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Infrastructure.Storage;

/// <summary>
/// Dev-only business-document storage. Used only when R2 config is absent in Development
/// (ADR-471); never wired for any other environment. Writes under the OS temp directory.
/// </summary>
public sealed class LocalDiskBusinessDocumentStorage : IBusinessDocumentStorage
{
    private readonly string _rootDirectory;
    private readonly ILogger<LocalDiskBusinessDocumentStorage> _logger;

    public LocalDiskBusinessDocumentStorage(ILogger<LocalDiskBusinessDocumentStorage> logger)
    {
        _logger = logger;
        _rootDirectory = Path.Combine(Path.GetTempPath(), "ophalo-dev-business-documents");
        Directory.CreateDirectory(_rootDirectory);
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
            var path = ResolvePath(objectKey);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            await using var file = File.Create(path);
            await content.CopyToAsync(file, cancellationToken);

            _logger.LogInformation("[DEV STORAGE] Wrote {ObjectKey} to {Path}", objectKey, path);
            return Result<string>.Success(objectKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Dev-storage write failed for object key {ObjectKey}.", objectKey);
            return Result<string>.Failure(BusinessDocumentStorageErrors.UploadFailed);
        }
    }

    public Task DeleteBestEffortAsync(
        Guid accountId,
        DocumentPurpose purpose,
        string objectKey,
        CancellationToken cancellationToken)
    {
        if (!BusinessDocumentObjectKey.BelongsTo(accountId, purpose, objectKey))
        {
            _logger.LogWarning(
                "Refusing best-effort dev-storage delete: object key {ObjectKey} does not match the account/purpose it was generated for.",
                objectKey);
            return Task.CompletedTask;
        }

        try
        {
            var path = ResolvePath(objectKey);
            if (!IsUnderRoot(path))
            {
                _logger.LogWarning("Refusing best-effort dev-storage delete: {ObjectKey} resolves outside the storage root.", objectKey);
                return Task.CompletedTask;
            }

            File.Delete(path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Best-effort dev-storage delete failed for object key {ObjectKey}.", objectKey);
        }

        return Task.CompletedTask;
    }

    private string ResolvePath(string objectKey) => Path.Combine(_rootDirectory, objectKey);

    private bool IsUnderRoot(string path) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(_rootDirectory) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}
