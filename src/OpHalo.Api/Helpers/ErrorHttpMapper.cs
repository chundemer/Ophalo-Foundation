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
    /// <param name="extraExtensions">
    /// Optional fields merged into the ProblemDetails extensions alongside "code".
    /// Use to attach context-specific metadata (e.g. "entryContext") without overriding
    /// the HTTP status that the error code maps to.
    /// </param>
    public static IResult ToHttpResult(Error error, IReadOnlyDictionary<string, object?>? extraExtensions = null)
    {
        var (statusCode, title, detailOverride) = GetProblemMeta(error);
        return CreateProblem(statusCode, title, error, detailOverride, extraExtensions);
    }

    // Separates status/title routing from response building so extraExtensions can be
    // threaded in once rather than repeated across every switch arm.
    private static (int StatusCode, string Title, string? DetailOverride) GetProblemMeta(Error error) =>
        error.Code switch
        {
            // --- Foundation auth codes (explicit — do not match patterns below) ---
            "auth.unauthorized" => (StatusCodes.Status401Unauthorized, "Unauthorized.",
                "Authentication is required to access this resource."),

            "auth.forbidden" => (StatusCodes.Status403Forbidden, "Forbidden.", null),

            // --- Keep-specific codes (explicit) ---
            "keep.public_intake.unavailable" => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),

            // --- 400 — validation / malformed client request ---
            var c when c.Contains("Validation") => (StatusCodes.Status400BadRequest, "Validation failed.", null),

            // --- 401 — authentication failure ---
            var c when c.EndsWith(".Unauthorized") => (StatusCodes.Status401Unauthorized, "Unauthorized.",
                "Authentication is required to access this resource."),

            var c when c.EndsWith(".InvalidCredentials") => (StatusCodes.Status401Unauthorized, "Unauthorized.",
                "Invalid credentials."),

            // --- 402 — commercial state; trial lapsed, subscription expired, or past-due grace elapsed ---
            var c when c.EndsWith(".TrialExpired") => (StatusCodes.Status402PaymentRequired, "Payment required.", null),

            // Explicit match — Account.Expired resolves to 402, not the generic .Expired → 422 below.
            var c when c == "Account.Expired" => (StatusCodes.Status402PaymentRequired, "Payment required.", null),

            // Explicit match — Account.PilotFull resolves to 409, not the default 400.
            var c when c == "Account.PilotFull" => (StatusCodes.Status409Conflict, "Conflict.", null),

            // --- Keep request operator-write codes (explicit where suffix patterns do not cover) ---
            // NotFound → covered by .NotFound suffix; Forbidden → covered by .Forbidden suffix;
            // InvalidStatusTransition → covered by .InvalidStatusTransition suffix.
            var c when c == "KeepRequest.InvalidStatus" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            var c when c == "KeepRequest.MessageRequired" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            var c when c == "KeepRequest.MessageTooLong" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            var c when c == "KeepRequest.TerminalState" => (StatusCodes.Status409Conflict, "Conflict.", null),

            // Explicit match — Invite.SeatLimitReached resolves to 409; no suffix pattern covers it.
            var c when c == "Invite.SeatLimitReached" => (StatusCodes.Status409Conflict, "Conflict.", null),

            // --- Member-management codes (explicit where suffix patterns do not cover) ---
            var c when c == "Member.OwnerLimitReached" => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c == "Member.LastOwner" => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c == "Member.SeatLimitReached" => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c == "Member.PreviouslyRemoved" => (StatusCodes.Status409Conflict, "Conflict.", null),

            // Safety-net only — these internal routing codes must be intercepted and translated
            // by the SendInvite endpoint BEFORE reaching this path. If they arrive here, the
            // status is 409 (not the default 400), but the response body will expose the
            // internal code name rather than the public "Member.PreviouslyRemoved" contract.
            var c when c == "Member.PreviouslyRemovedNeedsReactivate" => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c == "Member.PreviouslyRemovedNeedsResend" => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c.EndsWith(".PastDueBlocked") => (StatusCodes.Status402PaymentRequired, "Payment required.", null),

            // --- 403 — authenticated but forbidden by business rules ---
            var c when c.EndsWith(".AccessDenied") => (StatusCodes.Status403Forbidden, "Forbidden.", null),

            var c when c.EndsWith(".Forbidden") => (StatusCodes.Status403Forbidden, "Forbidden.", null),

            var c when c.EndsWith(".Suspended") => (StatusCodes.Status403Forbidden, "Forbidden.", null),

            var c when c.EndsWith(".AdminRequired") => (StatusCodes.Status403Forbidden, "Forbidden.", null),

            var c when c.EndsWith(".InconsistentState") => (StatusCodes.Status403Forbidden, "Forbidden.", null),

            // --- 404 — resource does not exist ---
            var c when c.EndsWith(".NotFound") => (StatusCodes.Status404NotFound, "Resource not found.", null),

            // --- 409 — valid request but current state conflicts ---
            var c when c.EndsWith(".AlreadySent") => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c.EndsWith(".AlreadyClosed") => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c.EndsWith(".AlreadyActedOn") => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c.EndsWith(".AlreadyActive") => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c.EndsWith(".AlreadySuspended") => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c.EndsWith(".EmailAlreadyInUse") => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c.EndsWith(".Cancelled") => (StatusCodes.Status409Conflict, "Conflict.", null),

            // --- 422 — auth exchange failures (expired, used, superseded) ---
            var c when c.EndsWith(".InvalidToken") => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),

            var c when c.EndsWith(".Expired") => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),

            var c when c.EndsWith(".AlreadyConsumed") => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),

            var c when c.EndsWith(".CannotConsumeInvalidated") => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),

            var c when c.EndsWith(".AlreadyVerified") => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),

            // --- 422 — domain-rule transition rejections ---
            var c when c.EndsWith(".CannotReopen") => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),

            var c when c.EndsWith(".CannotReactivate") => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),

            var c when c.EndsWith(".NotSuspended") => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),

            var c when c.EndsWith(".CannotModifySelf") => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),

            var c when c.EndsWith(".CannotModifyOwner") => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),

            var c when c.EndsWith(".PrimaryOwnerProtected") => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),

            var c when c.EndsWith(".InvalidStatusTransition") => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),

            // --- 503 — session creation or delivery failures ---
            var c when c.EndsWith(".SessionCreationFailed") => (StatusCodes.Status503ServiceUnavailable, "Service unavailable.", null),

            var c when c.EndsWith(".DeliveryFailed") => (StatusCodes.Status503ServiceUnavailable, "Service unavailable.", null),

            // --- default — generic client error ---
            _ => (StatusCodes.Status400BadRequest, "Bad request.", null)
        };

    private static IResult CreateProblem(
        int statusCode,
        string title,
        Error error,
        string? detailOverride = null,
        IReadOnlyDictionary<string, object?>? extraExtensions = null)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };

        if (extraExtensions is not null)
            foreach (var (key, value) in extraExtensions)
            {
                if (string.Equals(key, "code", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("extraExtensions must not override the reserved 'code' extension.", nameof(extraExtensions));
                extensions[key] = value;
            }

        return Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detailOverride ?? error.Message,
            type: "about:blank",
            extensions: extensions);
    }
}
