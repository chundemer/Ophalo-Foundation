using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfKeepCallHandoffPersistence(OpHaloDbContext dbContext) : IKeepCallHandoffPersistence
{
    public async Task CreateAsync(KeepCallHandoff handoff, CancellationToken ct)
    {
        dbContext.Set<KeepCallHandoff>().Add(handoff);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<KeepCallHandoffLookupResult?> FindValidByHashAsync(
        string tokenHash, DateTime nowUtc, CancellationToken ct)
    {
        var row = await dbContext.Set<KeepCallHandoff>()
            .AsNoTracking()
            .Where(h => h.HandoffTokenHash == tokenHash && h.ExpiresAtUtc > nowUtc && h.DeletedAtUtc == null)
            .Select(h => new { h.CustomerPhone, h.ExpiresAtUtc })
            .FirstOrDefaultAsync(ct);

        return row is null
            ? null
            : new KeepCallHandoffLookupResult(row.CustomerPhone, row.ExpiresAtUtc);
    }
}
