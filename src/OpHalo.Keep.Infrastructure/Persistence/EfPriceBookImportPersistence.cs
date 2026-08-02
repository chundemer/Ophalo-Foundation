using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfPriceBookImportPersistence(OpHaloDbContext dbContext) : IPriceBookImportPersistence
{
    // No .Include(x => x.Rows) — the Staged -> Validated transition never needs the row set, and
    // this seam must never load a potentially very large import's rows just to flip its status.
    public Task<PriceBookImport?> GetByIdAsync(Guid accountId, Guid importId, CancellationToken ct) =>
        dbContext.Set<PriceBookImport>()
            .FirstOrDefaultAsync(x => x.AccountId == accountId && x.Id == importId, ct);

    public Task CommitAsync(PriceBookImport import, CancellationToken ct) =>
        dbContext.SaveChangesAsync(ct);
}
