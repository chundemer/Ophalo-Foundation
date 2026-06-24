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
            "KeepPublicIntakeLink.NoActiveLink" => (StatusCodes.Status404NotFound, "Resource not found.", null),

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

            // --- Shared request-creation validation errors (G2/G3b) ---
            var c when c == "KeepRequest.CustomerNameRequired"           => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.CustomerPhoneRequired"          => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.DescriptionRequired"            => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.CustomerNameTooLong"            => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.CustomerPhoneTooLong"           => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.CustomerPhoneInvalidCharacters" => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.CustomerPhoneInvalidFormat"     => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.CustomerEmailTooLong"           => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.CustomerEmailInvalid"           => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.DescriptionTooLong"             => (StatusCodes.Status400BadRequest, "Bad request.", null),

            // --- Keep request operator-write codes (explicit where suffix patterns do not cover) ---
            // NotFound → covered by .NotFound suffix; Forbidden → covered by .Forbidden suffix;
            // InvalidStatusTransition → covered by .InvalidStatusTransition suffix.
            var c when c == "KeepRequest.InvalidStatus" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            var c when c == "KeepRequest.MessageRequired" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            var c when c == "KeepRequest.MessageTooLong" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            var c when c == "KeepRequest.TerminalState" => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c == "KeepRequest.BusinessUpdateMessageTooLong" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            var c when c == "KeepRequest.NoteRequired" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            var c when c == "KeepRequest.NoteTooLong" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            var c when c == "KeepRequest.AttentionReasonRequired" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            var c when c == "KeepRequest.AttentionReasonTooLong" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            var c when c == "KeepRequest.AttentionNotRaised"               => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c == "KeepRequest.AttentionRequiresFeedbackReview"  => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c == "KeepRequest.CustomerMessageTooLong" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            var c when c == "KeepRequest.FeedbackResolutionRequired" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            var c when c == "KeepRequest.FeedbackCommentTooLong" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            var c when c == "KeepRequest.FeedbackUnavailable" => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c == "KeepRequest.FeedbackAlreadySubmitted" => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c == "KeepRequest.OffSeasonUnavailable" => (StatusCodes.Status409Conflict, "Conflict.", null),

            // --- Optimistic concurrency (G5/ADR-332/334) ---
            // Header parsing failures are 400; a stale token / EF race is a 409. Conflict
            // behavior is wired by G5b–d; the mapping is defined here in G5a.
            var c when c == "KeepRequest.ExpectedVersionRequired" => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.ExpectedVersionInvalid"  => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.RequestChanged"          => (StatusCodes.Status409Conflict, "Conflict.", null),

            // --- Request list query validation errors (ADR-257/258, Sessions 4A/4B) ---
            var c when c == "KeepRequest.RequestListInvalidView"              => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.RequestListViewNotYetAvailable"      => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.RequestListFilterNotYetAvailable"    => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.RequestListInvalidLimit"             => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.RequestListInvalidCursor"            => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.RequestListInvalidDateFormat"        => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.RequestListInvalidClosedShortcut"   => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.RequestListContradictoryParameters"       => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.RequestListInvalidAssignedAccountUserId" => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.RequestListUnknownParameter"             => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.RequestListDuplicateParameter"           => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.RequestListInvalidStatus"                => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.RequestListInvalidAttentionReason"       => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.RequestListHistoryViewForbidden"         => (StatusCodes.Status403Forbidden, "Forbidden.", null),

            // --- Participation write errors (ADR-222..235 / Session 3B) ---
            var c when c == "KeepRequest.ParticipationTargetIneligible"           => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),
            var c when c == "KeepRequest.ParticipationOperatorCannotAssignOther"  => (StatusCodes.Status403Forbidden, "Forbidden.", null),
            var c when c == "KeepRequest.ParticipationOperatorCannotClear"        => (StatusCodes.Status403Forbidden, "Forbidden.", null),
            var c when c == "KeepRequest.ParticipationRequestAlreadyAssigned"     => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "KeepRequest.ParticipationNoteTooLong"                => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.ParticipationMuteRequiresActiveParticipation" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "KeepRequest.ParticipationCannotUnwatchResponsible"   => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "KeepRequest.ParticipationResponsibleCannotWatch"     => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "KeepRequest.ParticipationStateCorrupt"               => (StatusCodes.Status409Conflict, "Conflict.", null),

            // --- Close permission errors (ADR-343 / P6f-1) ---
            var c when c == "KeepRequest.CloseRequiresOwnerOrAdmin" => (StatusCodes.Status403Forbidden, "Forbidden.", null),
            var c when c == "KeepRequest.CloseBlockedByAttention"   => (StatusCodes.Status409Conflict, "Conflict.", null),

            // --- Follow Up On / Planned For errors (ADR-337/338 / P6b-2) ---
            var c when c == "KeepRequest.FollowUpOnRequiresActiveRequest" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "KeepRequest.PlannedForRequiresActiveRequest" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "KeepRequest.FollowUpOnReasonRequired"        => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.FollowUpOnNoteRequired"          => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.FollowUpOnNoteTooLong"           => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.InvalidDateFormat"               => (StatusCodes.Status400BadRequest, "Bad request.", null),

            // --- Feedback review errors (ADR-276 / Session 5B) ---
            var c when c == "KeepRequest.FeedbackReviewUnavailable"              => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "KeepRequest.FeedbackAlreadyReviewed"                => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "KeepRequest.FeedbackReviewNoteTooLong"              => (StatusCodes.Status400BadRequest, "Bad request.", null),

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
