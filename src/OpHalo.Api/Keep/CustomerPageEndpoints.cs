using OpHalo.Api.Helpers;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;

namespace OpHalo.Api.Keep;

/// <summary>
/// Maintainability review item 5: customer-page routes (page view, customer messages, feedback)
/// split out of <c>KeepEndpoints.cs</c>. No behavior change — locked by
/// <c>KeepEndpointsRouteInventoryTests</c>.
/// </summary>
public static class CustomerPageEndpoints
{
    public static void MapCustomerPageEndpoints(this IEndpointRouteBuilder app)
    {
        // Customer page — anonymous, resolved by page token (Phase 8-B1-β)
        // Returns 200 (active) or 410 (expired). Expired body: { businessName, referenceCode, isExpired, newRequestUrl }.
        // Rate limited (AUDIT-V6-A / F6.1): the same composite IP+pageToken "customer-write"
        // policy as every other {pageToken} route (ADR-129) — this route isn't read-only either
        // (it debounce-writes CustomerPageLastViewedAtUtc), and was the one anonymous route
        // missing a limiter entirely.
        app.MapGet("/keep/r/{pageToken}", async (
            string pageToken,
            GetKeepCustomerPageService service,
            CancellationToken ct) =>
        {
            var result = await service.ExecuteAsync(pageToken, ct);
            if (!result.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(result.Error);

            var page = result.Value;
            return page.IsExpired
                ? Results.Json(page, statusCode: StatusCodes.Status410Gone)
                : Results.Ok(page);
        }).RequireRateLimiting("customer-write");

        // Customer message routes — anonymous, one route per intent, rate limited (ADR-129..131, ADR-342)
        app.MapPost("/keep/r/{pageToken}/question",
            (string pageToken, CustomerMessageBody body, HttpRequest httpRequest, AddCustomerMessageService service, CancellationToken ct) =>
                HandleCustomerMessage(pageToken, MessageIntent.Question, body.Message, httpRequest, service, ct))
            .RequireRateLimiting("customer-write");

        app.MapPost("/keep/r/{pageToken}/update_request",
            (string pageToken, CustomerMessageBody body, HttpRequest httpRequest, AddCustomerMessageService service, CancellationToken ct) =>
                HandleCustomerMessage(pageToken, MessageIntent.UpdateRequest, body.Message, httpRequest, service, ct))
            .RequireRateLimiting("customer-write");

        app.MapPost("/keep/r/{pageToken}/information_added",
            (string pageToken, CustomerMessageBody body, HttpRequest httpRequest, AddCustomerMessageService service, CancellationToken ct) =>
                HandleCustomerMessage(pageToken, MessageIntent.InformationAdded, body.Message, httpRequest, service, ct))
            .RequireRateLimiting("customer-write");

        app.MapPost("/keep/r/{pageToken}/call_requested",
            (string pageToken, CustomerMessageBody body, HttpRequest httpRequest, AddCustomerMessageService service, CancellationToken ct) =>
                HandleCustomerMessage(pageToken, MessageIntent.CallRequested, body.Message, httpRequest, service, ct))
            .RequireRateLimiting("customer-write");

        app.MapPost("/keep/r/{pageToken}/timing_change_requested",
            (string pageToken, CustomerMessageBody body, HttpRequest httpRequest, AddCustomerMessageService service, CancellationToken ct) =>
                HandleCustomerMessage(pageToken, MessageIntent.TimingChangeRequested, body.Message, httpRequest, service, ct))
            .RequireRateLimiting("customer-write");

        app.MapPost("/keep/r/{pageToken}/cancellation_requested",
            (string pageToken, CustomerMessageBody body, HttpRequest httpRequest, AddCustomerMessageService service, CancellationToken ct) =>
                HandleCustomerMessage(pageToken, MessageIntent.CancellationRequested, body.Message, httpRequest, service, ct))
            .RequireRateLimiting("customer-write");

        app.MapPost("/keep/r/{pageToken}/feedback",
            (string pageToken, FeedbackBody body, HttpRequest httpRequest, SubmitFeedbackService service, CancellationToken ct) =>
                HandleFeedback(pageToken, body, httpRequest, service, ct))
            .RequireRateLimiting("customer-write");
    }

    private static async Task<IResult> HandleCustomerMessage(
        string pageToken,
        MessageIntent intent,
        string message,
        HttpRequest httpRequest,
        AddCustomerMessageService service,
        CancellationToken ct)
    {
        var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
        if (!versionResult.IsSuccess)
            return ErrorHttpMapper.ToHttpResult(versionResult.Error);

        var command = new AddCustomerMessageCommand(pageToken, intent, message, versionResult.Value);
        var result = await service.ExecuteAsync(command, ct);
        if (!result.IsSuccess)
            return ErrorHttpMapper.ToHttpResult(result.Error);

        var page = result.Value;
        return page.IsExpired
            ? Results.Json(page, statusCode: StatusCodes.Status410Gone)
            : Results.Ok(page);
    }

    private static async Task<IResult> HandleFeedback(
        string pageToken,
        FeedbackBody body,
        HttpRequest httpRequest,
        SubmitFeedbackService service,
        CancellationToken ct)
    {
        var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
        if (!versionResult.IsSuccess)
            return ErrorHttpMapper.ToHttpResult(versionResult.Error);

        if (body.WasResolved is null)
            return ErrorHttpMapper.ToHttpResult(KeepRequestErrors.FeedbackResolutionRequired);

        var command = new SubmitFeedbackCommand(pageToken, body.WasResolved.Value, body.Comment, versionResult.Value);
        var result = await service.ExecuteAsync(command, ct);
        if (!result.IsSuccess)
            return ErrorHttpMapper.ToHttpResult(result.Error);

        var page = result.Value;
        return page.IsExpired
            ? Results.Json(page, statusCode: StatusCodes.Status410Gone)
            : Results.Ok(page);
    }
}
