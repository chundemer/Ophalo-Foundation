using Microsoft.AspNetCore.Http;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Api.Helpers;

/// <summary>
/// Maps known domain/application errors to RFC 7807 ProblemDetails responses.
/// Keeps endpoints thin and gives API clients a consistent error contract.
///
/// Pattern-based matching is preferred over enumerating every error code. Specific
/// Foundation/Keep codes that don't fit the patterns are matched explicitly first.
/// </summary>
public static class ErrorHttpMapper
{
    public static IResult ToHttpResult(Error error)
    {
        return error.Code switch
        {
            // --- Foundation auth codes (explicit — do not match patterns below) ---
            "auth.unauthorized" => CreateProblem(StatusCodes.Status401Unauthorized, "Unauthorized.", error,
                detailOverride: "Authentication is required to access this resource."),

            "auth.forbidden" => CreateProblem(StatusCodes.Status403Forbidden, "Forbidden.", error),

            // --- Keep-specific codes (explicit) ---
            "keep.public_intake.unavailable" => CreateProblem(StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", error),

            // --- 400 — validation / malformed client request ---
            var c when c.Contains("Validation") => CreateProblem(StatusCodes.Status400BadRequest, "Validation failed.", error),

            // --- 401 — authentication failure ---
            var c when c.EndsWith(".Unauthorized") => CreateProblem(StatusCodes.Status401Unauthorized, "Unauthorized.", error,
                detailOverride: "Authentication is required to access this resource."),

            var c when c.EndsWith(".InvalidCredentials") => CreateProblem(StatusCodes.Status401Unauthorized, "Unauthorized.", error,
                detailOverride: "Invalid credentials."),

            // --- 402 — commercial state; trial lapsed, subscription expired, or past-due grace elapsed ---
            var c when c.EndsWith(".TrialExpired") => CreateProblem(StatusCodes.Status402PaymentRequired, "Payment required.", error),

            var c when c == "Account.Expired" => CreateProblem(StatusCodes.Status402PaymentRequired, "Payment required.", error),

            var c when c.EndsWith(".PastDueBlocked") => CreateProblem(StatusCodes.Status402PaymentRequired, "Payment required.", error),

            // --- 403 — authenticated but forbidden by business rules ---
            var c when c.EndsWith(".AccessDenied") => CreateProblem(StatusCodes.Status403Forbidden, "Forbidden.", error),

            var c when c.EndsWith(".Forbidden") => CreateProblem(StatusCodes.Status403Forbidden, "Forbidden.", error),

            var c when c.EndsWith(".Suspended") => CreateProblem(StatusCodes.Status403Forbidden, "Forbidden.", error),

            var c when c.EndsWith(".AdminRequired") => CreateProblem(StatusCodes.Status403Forbidden, "Forbidden.", error),

            var c when c.EndsWith(".InconsistentState") => CreateProblem(StatusCodes.Status403Forbidden, "Forbidden.", error),

            // --- 404 — resource does not exist ---
            var c when c.EndsWith(".NotFound") => CreateProblem(StatusCodes.Status404NotFound, "Resource not found.", error),

            // --- 409 — valid request but current state conflicts ---
            var c when c.EndsWith(".AlreadySent") => CreateProblem(StatusCodes.Status409Conflict, "Conflict.", error),

            var c when c.EndsWith(".AlreadyClosed") => CreateProblem(StatusCodes.Status409Conflict, "Conflict.", error),

            var c when c.EndsWith(".AlreadyActedOn") => CreateProblem(StatusCodes.Status409Conflict, "Conflict.", error),

            var c when c.EndsWith(".AlreadyActive") => CreateProblem(StatusCodes.Status409Conflict, "Conflict.", error),

            var c when c.EndsWith(".AlreadySuspended") => CreateProblem(StatusCodes.Status409Conflict, "Conflict.", error),

            var c when c.EndsWith(".EmailAlreadyInUse") => CreateProblem(StatusCodes.Status409Conflict, "Conflict.", error),

            var c when c.EndsWith(".Cancelled") => CreateProblem(StatusCodes.Status409Conflict, "Conflict.", error),

            // --- 422 — auth exchange failures (expired, used, superseded) ---
            var c when c.EndsWith(".InvalidToken") => CreateProblem(StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", error),

            var c when c.EndsWith(".Expired") => CreateProblem(StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", error),

            var c when c.EndsWith(".AlreadyConsumed") => CreateProblem(StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", error),

            var c when c.EndsWith(".CannotConsumeInvalidated") => CreateProblem(StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", error),

            var c when c.EndsWith(".AlreadyVerified") => CreateProblem(StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", error),

            // --- 422 — domain-rule transition rejections ---
            var c when c.EndsWith(".CannotReopen") => CreateProblem(StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", error),

            var c when c.EndsWith(".CannotReactivate") => CreateProblem(StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", error),

            var c when c.EndsWith(".NotSuspended") => CreateProblem(StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", error),

            // --- 503 — session creation or push delivery failures ---
            var c when c.EndsWith(".SessionCreationFailed") => CreateProblem(StatusCodes.Status503ServiceUnavailable, "Service unavailable.", error),

            var c when c.EndsWith(".DeliveryFailed") => CreateProblem(StatusCodes.Status503ServiceUnavailable, "Service unavailable.", error),

            // --- default — generic client error ---
            _ => CreateProblem(StatusCodes.Status400BadRequest, "Bad request.", error)
        };
    }

    private static IResult CreateProblem(
        int statusCode,
        string title,
        Error error,
        string? detailOverride = null)
    {
        return Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detailOverride ?? error.Message,
            type: "about:blank",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = error.Code
            });
    }
}
