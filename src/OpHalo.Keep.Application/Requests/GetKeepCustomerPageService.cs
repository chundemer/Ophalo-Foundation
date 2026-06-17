using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed class GetKeepCustomerPageService(
    KeepPublicCustomerAccessGuard guard,
    IKeepRequestDetailPersistence persistence)
{
    public async Task<Result<KeepCustomerPageResult>> ExecuteAsync(
        string pageToken, CancellationToken ct = default)
    {
        var guardResult = await guard.EvaluateAsync(pageToken, ct);
        if (!guardResult.IsSuccess)
            return Result<KeepCustomerPageResult>.Failure(guardResult.Error);

        var context = guardResult.Value;

        if (context.IsExpired)
            return Result<KeepCustomerPageResult>.Success(
                KeepCustomerPageMapper.BuildExpiredResult(context));

        var events = await persistence.GetCustomerVisibleEventsAsync(context.RequestId, ct);

        return Result<KeepCustomerPageResult>.Success(
            KeepCustomerPageMapper.BuildActiveResult(context, events));
    }
}
