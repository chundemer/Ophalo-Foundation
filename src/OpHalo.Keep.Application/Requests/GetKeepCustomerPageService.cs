using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed class GetKeepCustomerPageService(
    KeepPublicCustomerAccessGuard guard,
    IKeepRequestDetailPersistence persistence,
    IKeepCustomerWritePersistence writePersistence,
    IClock clock)
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

        // Record page-view telemetry for all non-expired accessible pages (ADR-341).
        // Load a tracked entity, apply debounce, and commit only when the window has elapsed.
        var tracked = await writePersistence.GetRequestForUpdateAsync(context.RequestId, ct);
        if (tracked is not null && tracked.RecordCustomerPageView(clock.UtcNow))
            await writePersistence.CommitPageViewAsync(tracked, ct);

        var events = await persistence.GetCustomerVisibleEventsAsync(context.RequestId, ct);

        return Result<KeepCustomerPageResult>.Success(
            KeepCustomerPageMapper.BuildActiveResult(context, events));
    }
}
