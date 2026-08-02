using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// Persistence seam for the <see cref="PriceBookImport"/> root only (Session 2c.1b). Deliberately
/// never includes <see cref="PriceBookImport.Rows"/> — loading the root for the
/// <c>Staged -&gt; Validated</c> transition must never pull a potentially very large row set into
/// memory. Every read is scoped by <c>accountId</c> directly in the query, never filtered after
/// load, so a cross-account id can never resolve to another account's row.
/// </summary>
public interface IPriceBookImportPersistence
{
    Task<PriceBookImport?> GetByIdAsync(Guid accountId, Guid importId, CancellationToken ct);

    Task CommitAsync(PriceBookImport import, CancellationToken ct);
}
