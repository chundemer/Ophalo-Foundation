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
            "keep.public_intake.unavailable"         => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),
            "keep.public_intake.slug_taken"          => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),
            "keep.public_intake.staff_not_permitted" => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),

            // Service location validation — public intake only (S22d)
            "KeepRequest.ServiceAddressLine1Required" => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),
            "KeepRequest.ServiceCityRequired"         => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),
            "KeepRequest.ServiceStateRequired"        => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),
            "KeepRequest.ServiceStateInvalid"         => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),
            "KeepPublicIntakeLink.NoActiveLink" => (StatusCodes.Status404NotFound, "Resource not found.", null),

            // Referenced catalog item/offering-assembly does not exist for the account — a missing
            // target, not malformed input, so 404 rather than the generic 400 default.
            "ScopeNudgeRule.TargetNotFound" => (StatusCodes.Status404NotFound, "Resource not found.", null),

            // Same shape as ScopeNudgeRule.TargetNotFound (build-log/129, 5d-ii-b) — a missing
            // configured target, not malformed input.
            "ActualWorkNudgeRule.TargetNotFound" => (StatusCodes.Status404NotFound, "Resource not found.", null),

            // Malformed trigger query parameter shape (missing/duplicate/combined) on the field
            // nudge-read endpoint — build-log/123.
            "ScopeNudgeRule.TriggerQueryParameterInvalid" => (StatusCodes.Status400BadRequest, "Bad request.", null),
            "KeepPublicIntakeLink.ReplaceConfirmationInvalid" =>
                (StatusCodes.Status400BadRequest, "Bad request.", null),

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
            var c when c == "KeepRequest.SourceRequired"              => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.InvalidSource"               => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.SourceCannotBePublicIntake"  => (StatusCodes.Status400BadRequest, "Bad request.", null),

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

            // --- CatalogItem concurrency/uniqueness conflicts (Session 2a.2) ---
            var c when c == "CatalogItem.VersionMismatch"         => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "CatalogItem.ExternalKeyAlreadyExists" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "CatalogItem.ExpectedVersionRequired" => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "CatalogItem.ExpectedVersionInvalid"  => (StatusCodes.Status400BadRequest, "Bad request.", null),

            // --- AccountCapabilityPackageEnrollment concurrency/state conflicts (internal entitlement ops) ---
            var c when c == "AccountCapabilityPackageEnrollment.VersionMismatch" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "AccountCapabilityPackageEnrollment.AlreadyEnrolled" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "AccountCapabilityPackageEnrollment.AlreadyDisabled" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "AccountCapabilityPackageEnrollment.EnrollmentAlreadyExists" => (StatusCodes.Status409Conflict, "Conflict.", null),

            // --- OfferingAssembly concurrency/uniqueness conflicts (Session 3.2a.1) ---
            var c when c == "OfferingAssembly.VersionMismatch"                => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "OfferingAssembly.PrimaryCatalogItemAlreadyClaimed" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "OfferingAssembly.ExpectedVersionRequired"        => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "OfferingAssembly.ExpectedVersionInvalid"         => (StatusCodes.Status400BadRequest, "Bad request.", null),

            // --- OfferingAssemblyItem conflicts (Session 3.2b) — the Item segment breaks the
            // generic .NotFound/.AlreadyActive/.NotActive suffix matches below, same reason
            // CatalogItemAlias needed explicit entries.
            var c when c == "OfferingAssembly.ItemNotFound"      => (StatusCodes.Status404NotFound, "Resource not found.", null),
            var c when c == "OfferingAssembly.ItemAlreadyExists" => (StatusCodes.Status409Conflict, "Conflict.", null),

            // --- ProposedScope concurrency/state conflicts (Session 3.3b) ---
            var c when c == "ProposedScope.VersionMismatch"           => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "ProposedScope.DraftAlreadyOpenForRequest" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "ProposedScope.NotDraft"                  => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "ProposedScope.ExpectedVersionRequired"   => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "ProposedScope.ExpectedVersionInvalid"    => (StatusCodes.Status400BadRequest, "Bad request.", null),

            // Session 4a: submitting a scope with zero lines — an unprocessable-content rule on
            // the current state, not a concurrency conflict, matching the 422 precedent used for
            // other current-content validation failures.
            var c when c == "ProposedScope.EmptySubmit" => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),

            // Session 4b Slice 2: the five-second undo-delete window has closed (or the snapshot
            // was already consumed/cleaned up) — same current-state-validation reasoning as
            // EmptySubmit, not a version conflict. A second restore after a successful one is a
            // real conflict on the now-live line, matching NotDraft/VersionMismatch's 409.
            var c when c == "ProposedScope.RestoreExpired" => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),
            var c when c == "ProposedScope.RestoreLineAlreadyExists" => (StatusCodes.Status409Conflict, "Conflict.", null),

            // --- ProposedScopeLine conflicts (Session 3.3b) — the Line segment breaks the generic
            // .NotFound suffix match below, same reason OfferingAssembly.ItemNotFound needed one.
            var c when c == "ProposedScope.LineNotFound" => (StatusCodes.Status404NotFound, "Resource not found.", null),

            // --- Field-select (Session 3.4d) — same Line-segment reasoning for the 404; the other
            // two are client-input rejections, mapped 400.
            var c when c == "ProposedScope.LineCatalogItemNotFound" => (StatusCodes.Status404NotFound, "Resource not found.", null),
            var c when c == "ProposedScope.LineOffCatalogDescriptionInvalidCharacters" => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "ProposedScope.FieldSelectLineTypeInvalid" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            // --- expand-assembly (Session 3.4e) ---
            var c when c == "ProposedScope.ExpandAssemblyNotOperationallyEligible" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "ProposedScope.ExpandExclusionItemInvalid" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            // --- Quick scope action configuration (Session 3, build-log/119) ---
            var c when c == "QuickScopeAction.TargetNotEligible" => (StatusCodes.Status409Conflict, "Conflict.", null),

            // --- CatalogCategory concurrency/uniqueness conflicts (Session 2b.3) ---
            var c when c == "CatalogCategory.VersionMismatch"         => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "CatalogCategory.NameAlreadyExists"       => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c == "ScopeNudgeRule.DuplicateTrigger" => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c == "ActualWorkNudgeRule.DuplicateTrigger" => (StatusCodes.Status409Conflict, "Conflict.", null),

            // --- Direct Actual Work draft conflicts (ADR-487, build-log/129, Batch 3) ---
            var c when c == "ActualWork.VersionMismatch"           => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "ActualWork.DraftAlreadyOpenForRequest" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "ActualWork.NotDraft"                  => (StatusCodes.Status409Conflict, "Conflict.", null),
            // GAP-055 option C — an ineligible transfer target is a semantically-invalid target,
            // not malformed input; mirrors KeepRequest.ParticipationTargetIneligible.
            var c when c == "ActualWork.RecorderTransferTargetIneligible" => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),

            // --- Direct Actual Work expand-assembly (build-log/129, 5d-i) ---
            var c when c == "ActualWork.ExpandAssemblyNotOperationallyEligible" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "ActualWork.ExpandInclusionItemInvalid" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            // --- Direct Actual Work review (Batch 6, build-log/129) ---
            var c when c == "ActualWork.NotSubmitted"     => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "ActualWork.AlreadyReviewed"  => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "ActualWork.ReviewNoteTooLong" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            // --- Financial resolution mutation (BL135 §4 Batch 3a-ii, ADR-493) ---
            var c when c == "ActualWork.FinancialResolutionLineNotFound" => (StatusCodes.Status404NotFound, "Resource not found.", null),
            var c when c == "ActualWork.FinancialResolutionSnapshotComponentAlreadyValid" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "ActualWork.FinancialResolutionVisitAlreadyReviewed" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "ActualWork.FinancialResolutionValueRequired" => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "ActualWork.FinancialResolutionValueNegative" => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "ActualWork.FinancialResolutionInvalidBasis" => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "ActualWork.FinancialResolutionReasonRequired" => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "ActualWork.FinancialResolutionReasonTooLong" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            // --- Office financial disposition mutation (BL135 §4 Batch 3b-i, ADR-493) ---
            var c when c == "ActualWork.DispositionInvalidKind" => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "ActualWork.DispositionReasonRequired" => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "ActualWork.DispositionReasonTooLong" => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "ActualWork.DispositionVisitHasLines" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "ActualWork.DispositionVisitAlreadyReviewed" => (StatusCodes.Status409Conflict, "Conflict.", null),

            // --- Price book publish lock conflict (Session 2d.2, ADR-470) ---
            var c when c == "PriceBookVersion.PublishLockConflict" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "PriceBookVersion.CatalogItemNotActive" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "CatalogCategory.ExpectedVersionRequired" => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "CatalogCategory.ExpectedVersionInvalid"  => (StatusCodes.Status400BadRequest, "Bad request.", null),

            // --- CatalogItemAlias conflicts (Session 2b.3) — the Alias segment breaks the
            // generic .NotFound/.AlreadyActive/.NotActive suffix matches below, so these need
            // explicit entries.
            var c when c == "CatalogItem.AliasAlreadyExists" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "CatalogItem.AliasAlreadyActive" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "CatalogItem.AliasNotActive"     => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "CatalogItem.AliasNotFound"      => (StatusCodes.Status404NotFound, "Resource not found.", null),

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

            // --- Share intent errors (S11b) ---
            var c when c == "KeepRequest.ShareIntentViewerBlocked"    => (StatusCodes.Status403Forbidden, "Forbidden.", null),
            var c when c == "KeepRequest.ShareIntentOffSeasonBlocked" => (StatusCodes.Status403Forbidden, "Forbidden.", null),
            var c when c == "KeepRequest.ShareIntentInvalidMethod"    => (StatusCodes.Status400BadRequest, "Bad request.", null),

            // --- SMS handoff errors (S25a) ---
            var c when c == "KeepRequest.SmsHandoffViewerBlocked"       => (StatusCodes.Status403Forbidden, "Forbidden.", null),
            var c when c == "KeepRequest.SmsHandoffOffSeasonBlocked"    => (StatusCodes.Status403Forbidden, "Forbidden.", null),
            var c when c == "KeepRequest.SmsHandoffMessageRequired"     => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.SmsHandoffMessageTooLong"      => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.SmsHandoffCustomerPhoneMissing" => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),

            // --- Call handoff errors (ADR-448, GAP-020) ---
            var c when c == "KeepRequest.CallHandoffViewerBlocked"        => (StatusCodes.Status403Forbidden, "Forbidden.", null),
            var c when c == "KeepRequest.CallHandoffOffSeasonBlocked"     => (StatusCodes.Status403Forbidden, "Forbidden.", null),
            var c when c == "KeepRequest.CallHandoffCustomerPhoneMissing" => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),

            // --- Detail navigation errors (P6f-4) ---
            var c when c == "KeepRequest.RequestDetailInvalidNavView" => (StatusCodes.Status400BadRequest, "Bad request.", null),

            // --- Spam/Test classification errors (ADR-349/350, S7e) ---
            var c when c == "KeepRequest.ClassificationRequiresOwnerOrAdmin" => (StatusCodes.Status403Forbidden, "Forbidden.", null),
            var c when c == "KeepRequest.InvalidClassification"              => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),
            var c when c == "KeepRequest.ClassificationReasonTooLong"        => (StatusCodes.Status400BadRequest, "Bad request.", null),

            // --- Follow Up On / Planned For errors (ADR-337/338 / P6b-2) ---
            var c when c == "KeepRequest.FollowUpOnRequiresActiveRequest" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "KeepRequest.PlannedForRequiresActiveRequest" => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "KeepRequest.FollowUpOnReasonRequired"        => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.FollowUpOnNoteRequired"          => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.FollowUpOnNoteTooLong"           => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.InvalidDateFormat"               => (StatusCodes.Status400BadRequest, "Bad request.", null),

            // --- Follow-up resolution errors (ADR-440 / S83b) ---
            var c when c == "KeepRequest.FollowUpOnNotSet"                  => (StatusCodes.Status409Conflict, "Conflict.", null),
            var c when c == "KeepRequest.FollowUpOnInvalidOutcome"          => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.FollowUpOnCompletionReasonRequired" => (StatusCodes.Status400BadRequest, "Bad request.", null),
            var c when c == "KeepRequest.FollowUpOnMoveRequiresDate"        => (StatusCodes.Status400BadRequest, "Bad request.", null),

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

            // Session 2b.3: a repeated inactivate is a state conflict, not malformed input.
            // Covers CatalogItem/CatalogCategory/CatalogItemAlias .NotActive; also corrects
            // CatalogItem.NotActive (shipped in 2a.2), which previously fell through to the
            // default 400 for lack of a matching rule.
            var c when c.EndsWith(".NotActive") => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c.EndsWith(".AlreadySuspended") => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c.EndsWith(".EmailAlreadyInUse") => (StatusCodes.Status409Conflict, "Conflict.", null),

            var c when c.EndsWith(".Cancelled") => (StatusCodes.Status409Conflict, "Conflict.", null),

            // --- 422 — auth exchange failures (expired, used, superseded) ---
            var c when c == "AuthCode.MobileNewAccountUnsupported" => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity.", null),

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
