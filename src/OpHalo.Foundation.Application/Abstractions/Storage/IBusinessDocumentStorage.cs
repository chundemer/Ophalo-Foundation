using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Abstractions.Storage;

/// <summary>
/// Private business-document storage seam (ADR-471). Implementations are infrastructure
/// concerns — application code never references a provider directly. All objects are private;
/// this seam only writes/removes them. Reads are a later concern for the modules that need them.
/// </summary>
public interface IBusinessDocumentStorage
{
    /// <summary>
    /// Stores <paramref name="content"/> under a new opaque object key scoped to
    /// <paramref name="accountId"/> and <paramref name="purpose"/>, and returns that key.
    /// Callers must not construct or assume a key format. Does not enforce size or row limits —
    /// callers are responsible for bounding what they stream in.
    /// </summary>
    Task<Result<string>> PutAsync(
        Guid accountId,
        DocumentPurpose purpose,
        Stream content,
        CancellationToken cancellationToken);

    /// <summary>
    /// Best-effort removal of a previously stored object, used to clean up after a later step in
    /// the same operation fails (for example, a source file staged successfully but the import
    /// could not be created). <paramref name="accountId"/> and <paramref name="purpose"/> must
    /// match the values <paramref name="objectKey"/> was generated with — implementations verify
    /// the key belongs to that exact account/purpose prefix before deleting, so a malformed or
    /// unrelated key can never cause deletion of another object. Implementations must not throw for
    /// a provider or validation failure; failures are logged, not surfaced, since the caller has
    /// already failed and cannot act on a further failure here. Cancellation still propagates.
    /// </summary>
    Task DeleteBestEffortAsync(
        Guid accountId,
        DocumentPurpose purpose,
        string objectKey,
        CancellationToken cancellationToken);
}
