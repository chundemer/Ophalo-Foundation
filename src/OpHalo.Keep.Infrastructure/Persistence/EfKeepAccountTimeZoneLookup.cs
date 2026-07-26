using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Requests;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfKeepAccountTimeZoneLookup(OpHaloDbContext dbContext) : IKeepAccountTimeZoneLookup
{
    public Task<string?> GetAccountTimeZoneAsync(Guid accountId, CancellationToken ct) =>
        dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => a.TimeZone)
            .Cast<string?>()
            .FirstOrDefaultAsync(ct);
}
