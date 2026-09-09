using System.Text;
using System.Text.Json;
using OpHalo.Api.Helpers;
using OpHalo.Foundation.Application.Feedback;
using OpHalo.Foundation.Core.Entities.Feedback.Enums;

namespace OpHalo.Api.Feedback;

/// <summary>
/// Foundation-owned in-product feedback endpoint (GAP-038 / 038-2b, BL149; route per ADR-501 —
/// flat, no <c>/api/v1</c> prefix). Sits behind the authenticated-app boundary with a
/// per-<c>account_user</c> fixed-window rate limit.
///
/// Gated by <c>Feedback:Enabled</c> (default <c>false</c>): while off the route is not mapped at
/// all, so callers get a framework <c>404</c>. The submission UI and flag activation land in
/// 038-2d; the retry worker, backlog alert and retention sweep land in 038-2c.
/// </summary>
public static class FeedbackEndpoints
{
    /// <summary>Rate-limiter policy name; partition + limits are defined in <c>Program.cs</c>.</summary>
    public const string RateLimitPolicy = "feedback";

    /// <summary>Cap on the serialised <c>context</c> blob; oversized context is dropped, not rejected.</summary>
    public const int MaxContextJsonBytes = 4_096;

    public static void MapFeedbackEndpoints(this IEndpointRouteBuilder app, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Feedback:Enabled"))
            return;

        app.MapPost("/feedback", SubmitFeedback)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicy);
    }

    private static async Task<IResult> SubmitFeedback(
        SubmitFeedbackRequest request,
        FeedbackSubmissionService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!TryResolveCategory(request.Category, out var category))
            return Results.Problem(
                title: "Bad request.",
                detail: "Unknown feedback category.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "feedback.category_invalid" });

        var contextJson = NormaliseContext(request.Context, loggerFactory);

        var result = await service.SubmitAsync(request.Message, category, contextJson, cancellationToken);

        if (result.IsFailure)
        {
            return result.Error.Code switch
            {
                "feedback.message_too_long" => Results.Problem(
                    title: "Payload too large.",
                    detail: result.Error.Message,
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code }),
                "feedback.persist_failed" => Results.Problem(
                    title: "Service unavailable.",
                    detail: result.Error.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code }),
                "feedback.message_required" => Results.Problem(
                    title: "Bad request.",
                    detail: result.Error.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code }),
                _ => ErrorHttpMapper.ToHttpResult(result.Error),
            };
        }

        return result.Value == FeedbackSubmissionOutcome.Delivered
            ? Results.Ok(new { status = "delivered" })
            : Results.Accepted(value: new { status = "queued" });
    }

    private static bool TryResolveCategory(string? raw, out FeedbackCategory category)
    {
        category = FeedbackCategory.Other;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        switch (raw.Trim().ToLowerInvariant())
        {
            case "bug": category = FeedbackCategory.Bug; return true;
            case "confusing": category = FeedbackCategory.Confusing; return true;
            case "missing_thing": category = FeedbackCategory.MissingThing; return true;
            case "too_slow": category = FeedbackCategory.TooSlow; return true;
            case "other": category = FeedbackCategory.Other; return true;
            default: return false;
        }
    }

    /// <summary>
    /// Re-serialises the client-supplied context object to a compact string. Non-object input or
    /// an over-cap blob is dropped to <c>null</c> (with a log) rather than failing an otherwise
    /// valid submission (BL149 P4).
    /// </summary>
    private static string? NormaliseContext(JsonElement? context, ILoggerFactory loggerFactory)
    {
        if (context is not { ValueKind: JsonValueKind.Object } element)
            return null;

        var json = JsonSerializer.Serialize(element);
        if (Encoding.UTF8.GetByteCount(json) > MaxContextJsonBytes)
        {
            loggerFactory.CreateLogger(typeof(FeedbackEndpoints))
                .LogInformation("Feedback context blob exceeded {Cap} bytes; dropped.", MaxContextJsonBytes);
            return null;
        }

        return json;
    }

    /// <param name="Message">Required feedback body; trimmed, 1–4,000 chars.</param>
    /// <param name="Category">Optional: bug | confusing | missing_thing | too_slow | other.</param>
    /// <param name="Context">Optional opaque non-PII context object (route, build, platform, …).</param>
    public sealed record SubmitFeedbackRequest(
        string? Message,
        string? Category,
        JsonElement? Context);
}
