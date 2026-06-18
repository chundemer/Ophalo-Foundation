using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed class SubmitFeedbackService(
    KeepPublicCustomerAccessGuard guard,
    IKeepCustomerWritePersistence persistence,
    IClock clock)
{
    public async Task<Result<KeepCustomerPageResult>> ExecuteAsync(
        SubmitFeedbackCommand command, CancellationToken ct = default)
    {
        var guardResult = await guard.EvaluateAsync(command.PageToken, ct);
        if (!guardResult.IsSuccess)
            return Result<KeepCustomerPageResult>.Failure(guardResult.Error);

        var context = guardResult.Value;

        if (context.IsOffSeason)
            return Result<KeepCustomerPageResult>.Failure(KeepRequestErrors.OffSeasonUnavailable);

        if (context.IsExpired)
            return Result<KeepCustomerPageResult>.Success(
                KeepCustomerPageMapper.BuildExpiredResult(context));

        var request = await persistence.GetRequestForUpdateAsync(context.RequestId, ct);
        if (request is null)
            return Result<KeepCustomerPageResult>.Failure(KeepRequestErrors.NotFound);

        var policy = await persistence.GetResponsePolicyAsync(context.AccountId, ct);
        var priority = policy?.PriorityResponseTargetMinutes ?? 60;

        var domainResult = request.SubmitFeedback(command.WasResolved, command.Comment, priority, clock.UtcNow);
        if (!domainResult.IsSuccess)
            return Result<KeepCustomerPageResult>.Failure(domainResult.Error);

        await persistence.CommitFeedbackAsync(request, ct);

        var events = await persistence.GetCustomerVisibleEventsAsync(context.RequestId, ct);

        var updatedContext = context with
        {
            FeedbackWasResolved = request.FeedbackWasResolved,
            FeedbackSubmittedAtUtc = request.FeedbackSubmittedAtUtc
        };

        return Result<KeepCustomerPageResult>.Success(
            KeepCustomerPageMapper.BuildActiveResult(updatedContext, events));
    }
}
