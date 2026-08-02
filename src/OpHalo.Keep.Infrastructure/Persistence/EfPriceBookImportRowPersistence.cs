using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfPriceBookImportRowPersistence(OpHaloDbContext dbContext) : IPriceBookImportRowPersistence
{
    public Task<PriceBookImportRow?> GetByIdAsync(Guid accountId, Guid rowId, CancellationToken ct) =>
        dbContext.Set<PriceBookImportRow>()
            .FirstOrDefaultAsync(x => x.AccountId == accountId && x.Id == rowId, ct);

    public Task CommitAsync(PriceBookImportRow row, CancellationToken ct) =>
        dbContext.SaveChangesAsync(ct);

    public Task<int> CountByStatusAsync(
        Guid accountId, Guid importId, PriceBookImportRowValidationStatus status, CancellationToken ct) =>
        dbContext.Set<PriceBookImportRow>()
            .CountAsync(x => x.AccountId == accountId && x.PriceBookImportId == importId && x.ValidationStatus == status, ct);

    public Task<bool> ExternalKeyDuplicateInImportAsync(
        Guid accountId, Guid importId, string externalKey, Guid excludeRowId, CancellationToken ct) =>
        dbContext.Set<PriceBookImportRow>()
            .AnyAsync(x => x.AccountId == accountId
                && x.PriceBookImportId == importId
                && x.Id != excludeRowId
                && x.ProposedExternalKey == externalKey, ct);
}
