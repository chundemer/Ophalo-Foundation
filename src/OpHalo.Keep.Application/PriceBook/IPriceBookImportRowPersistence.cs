using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// Persistence seam for one <see cref="PriceBookImportRow"/> at a time (Session 2c.1b). Every
/// method operates on a single row or a count/projection query — never the full row set of an
/// import — so validating, resolving, or correcting a row never loads its potentially very large
/// parent import or its sibling rows. Every read is scoped by <c>accountId</c> directly in the
/// query, never filtered after load.
/// </summary>
public interface IPriceBookImportRowPersistence
{
    Task<PriceBookImportRow?> GetByIdAsync(Guid accountId, Guid rowId, CancellationToken ct);

    Task CommitAsync(PriceBookImportRow row, CancellationToken ct);

    Task<int> CountByStatusAsync(
        Guid accountId, Guid importId, PriceBookImportRowValidationStatus status, CancellationToken ct);

    /// <summary>
    /// True when another row in the same import already carries this exact, nonblank external key.
    /// Excludes <paramref name="excludeRowId"/> so re-validating/correcting a row never flags
    /// itself as its own duplicate.
    /// </summary>
    Task<bool> ExternalKeyDuplicateInImportAsync(
        Guid accountId, Guid importId, string externalKey, Guid excludeRowId, CancellationToken ct);
}
