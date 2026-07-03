using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Application.Auth;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence;

namespace OpHalo.Foundation.Infrastructure.Auth;

public sealed class EfMobileHandoffCodePersistence(OpHaloDbContext db) : IMobileHandoffCodePersistence
{
    public async Task CreateAsync(MobileHandoffCode code, CancellationToken cancellationToken)
    {
        db.MobileHandoffCodes.Add(code);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<MobileHandoffCode?> FindByHashAsync(string codeHash, CancellationToken cancellationToken) =>
        db.MobileHandoffCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CodeHash == codeHash, cancellationToken);

    public async Task<bool> ConsumeAsync(Guid codeId, DateTime consumedAtUtc, CancellationToken cancellationToken)
    {
        var affected = await db.MobileHandoffCodes
            .Where(c => c.Id == codeId && c.ConsumedAtUtc == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.ConsumedAtUtc, consumedAtUtc),
                cancellationToken);

        return affected > 0;
    }
}
