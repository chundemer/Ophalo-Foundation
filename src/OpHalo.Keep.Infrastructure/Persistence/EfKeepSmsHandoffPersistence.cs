using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfKeepSmsHandoffPersistence(OpHaloDbContext dbContext) : IKeepSmsHandoffPersistence
{
    public async Task CreateAsync(KeepSmsHandoff handoff, CancellationToken ct)
    {
        dbContext.Set<KeepSmsHandoff>().Add(handoff);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<KeepSmsHandoffLookupResult?> FindValidByHashAsync(
        string tokenHash, DateTime nowUtc, CancellationToken ct)
    {
        var row = await dbContext.Set<KeepSmsHandoff>()
            .AsNoTracking()
            .Where(h => h.HandoffTokenHash == tokenHash && h.ExpiresAtUtc > nowUtc && !h.IsDeleted)
            .Select(h => new { h.CustomerPhone, h.MessageBody, h.ExpiresAtUtc })
            .FirstOrDefaultAsync(ct);

        return row is null
            ? null
            : new KeepSmsHandoffLookupResult(row.CustomerPhone, row.MessageBody, row.ExpiresAtUtc);
    }
}
