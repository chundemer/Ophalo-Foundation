using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Application.Feedback;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;

namespace OpHalo.Foundation.Infrastructure.Feedback;

/// <summary>
/// EF Core implementation of <see cref="IFeedbackIdentityReader"/> (GAP-098, BL156). Runs against
/// the request-scoped <see cref="OpHaloDbContext"/>.
/// </summary>
public sealed class EfFeedbackIdentityReader(OpHaloDbContext db) : IFeedbackIdentityReader
{
    public async Task<FeedbackFounderIdentity?> GetAsync(
        Guid accountId, Guid accountUserId, CancellationToken cancellationToken)
    {
        var row = await db.AccountUsers
            .Where(x => x.Id == accountUserId
                        && x.AccountId == accountId
                        && x.MembershipStatus == MembershipStatus.Active)
            .Select(x => new
            {
                x.Account.BusinessName,
                SubmitterName = x.User!.Name,
                x.Role,
                x.Email,
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new FeedbackFounderIdentity(row.BusinessName, row.SubmitterName, row.Role.ToString(), row.Email);
    }
}
