using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfManualPriceOverridePersistence(OpHaloDbContext dbContext) : IManualPriceOverridePersistence
{
    public async Task AddAsync(ManualPriceOverride entry, CancellationToken ct)
    {
        dbContext.Set<ManualPriceOverride>().Add(entry);
        await dbContext.SaveChangesAsync(ct);
    }
}
