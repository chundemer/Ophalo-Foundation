using OpHalo.Foundation.Application.Feedback;
using OpHalo.Foundation.Core.Entities.Feedback;
using OpHalo.Foundation.Infrastructure.Persistence;

namespace OpHalo.Foundation.Infrastructure.Feedback;

/// <summary>
/// EF Core implementation of <see cref="IFeedbackPersistence"/>. Runs against the request-scoped
/// <see cref="OpHaloDbContext"/>; each method commits its own unit of work.
/// </summary>
public sealed class EfFeedbackPersistence(OpHaloDbContext db) : IFeedbackPersistence
{
    public async Task AddAsync(FeedbackSubmission submission, CancellationToken cancellationToken)
    {
        db.FeedbackSubmissions.Add(submission);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(FeedbackSubmission submission, CancellationToken cancellationToken)
    {
        db.FeedbackSubmissions.Update(submission);
        await db.SaveChangesAsync(cancellationToken);
    }
}
