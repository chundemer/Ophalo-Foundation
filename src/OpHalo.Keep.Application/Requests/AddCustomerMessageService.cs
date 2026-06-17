using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed class AddCustomerMessageService(
    KeepPublicCustomerAccessGuard guard,
    IKeepCustomerWritePersistence persistence,
    IClock clock)
{
    public async Task<Result<KeepCustomerPageResult>> ExecuteAsync(
        AddCustomerMessageCommand command, CancellationToken ct = default)
    {
        var guardResult = await guard.EvaluateAsync(command.PageToken, ct);
        if (!guardResult.IsSuccess)
            return Result<KeepCustomerPageResult>.Failure(guardResult.Error);

        var context = guardResult.Value;

        // Expired token: return safe tombstone immediately — do not mutate anything.
        if (context.IsExpired)
            return Result<KeepCustomerPageResult>.Success(
                KeepCustomerPageMapper.BuildExpiredResult(context));

        var request = await persistence.GetRequestForUpdateAsync(context.RequestId, ct);
        if (request is null)
            return Result<KeepCustomerPageResult>.Failure(KeepRequestErrors.NotFound);

        var policy = await persistence.GetResponsePolicyAsync(context.AccountId, ct);
        var firstResponse = policy?.FirstResponseTargetMinutes ?? 60;
        var standard     = policy?.StandardResponseTargetMinutes ?? 240;
        var priority     = policy?.PriorityResponseTargetMinutes ?? 60;

        var domainResult = request.AddCustomerMessage(
            command.Intent, command.Message, firstResponse, standard, priority, clock.UtcNow);
        if (!domainResult.IsSuccess)
            return Result<KeepCustomerPageResult>.Failure(domainResult.Error);

        await persistence.CommitAsync(request, domainResult.Value, ct);

        var events = await persistence.GetCustomerVisibleEventsAsync(context.RequestId, ct);

        return Result<KeepCustomerPageResult>.Success(
            KeepCustomerPageMapper.BuildActiveResult(context, events));
    }
}
