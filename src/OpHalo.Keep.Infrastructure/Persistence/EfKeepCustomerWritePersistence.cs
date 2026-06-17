using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfKeepCustomerWritePersistence(OpHaloDbContext dbContext) : IKeepCustomerWritePersistence
{
    public async Task<KeepRequest?> GetRequestForUpdateAsync(Guid requestId, CancellationToken ct) =>
        await dbContext.Set<KeepRequest>()
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);

    public async Task<KeepResponsePolicy?> GetResponsePolicyAsync(Guid accountId, CancellationToken ct) =>
        await dbContext.Set<KeepResponsePolicy>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AccountId == accountId, ct);

    public async Task CommitAsync(KeepRequest request, KeepRequestEvent newEvent, CancellationToken ct)
    {
        dbContext.Set<KeepRequestEvent>().Add(newEvent);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<KeepRequestEvent>> GetCustomerVisibleEventsAsync(
        Guid requestId, CancellationToken ct) =>
        await dbContext.Set<KeepRequestEvent>()
            .AsNoTracking()
            .Where(e => e.RequestId == requestId && e.Visibility == KeepRequestEventVisibility.All)
            .OrderBy(e => e.OccurredAtUtc)
            .ToListAsync(ct);
}
