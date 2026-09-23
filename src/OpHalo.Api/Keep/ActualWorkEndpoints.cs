using OpHalo.Api.Helpers;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Api.Keep;

/// <summary>
/// Maintainability review item 5: Actual Work / Price Book routes split out of
/// <c>KeepEndpoints.cs</c>. Carries its own request/response body types (declared <c>file</c>-scoped
/// below, same visibility they had inside the original file) since they are used only by these
/// routes. No behavior change — locked by <c>KeepEndpointsRouteInventoryTests</c>.
/// </summary>
public static class ActualWorkEndpoints
{
    public static void MapActualWorkEndpoints(this IEndpointRouteBuilder app)
    {
        // Direct Actual Work — draft create/edit/discard (ADR-487, build-log/129, Batch 3).
        // Auth-stack composition lives in ActualWorkDraftApiService; thin route mapping only.
        app.MapPost("/keep/pricebook/actual-work/create", async (
            ActualWorkCreateBody body,
            ActualWorkDraftApiService service,
            CancellationToken ct) =>
        {
            var result = await service.CreateAsync(body.RequestId, body.DefaultPerformedByAccountUserId, ct);
            return result.IsSuccess ? Results.Ok(ToActualWorkResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPost("/keep/pricebook/actual-work/{actualWorkId:guid}/lines", async (
            Guid actualWorkId,
            ActualWorkAddLineBody body,
            HttpRequest httpRequest,
            ActualWorkDraftApiService service,
            CancellationToken ct) =>
        {
            var versionResult = ParseActualWorkVersion(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new AddActualWorkLineApiCommand(
                body.CatalogItemId, body.OffCatalogDescription, body.ActualQuantity, body.Note,
                body.PerformedByAccountUserId);
            var result = await service.AddLineAsync(actualWorkId, command, versionResult.Value, ct);
            return result.IsSuccess
                ? Results.Ok(new ActualWorkLineAddedResponse(result.Value.LineId, result.Value.ActualWorkConcurrencyVersion))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPost("/keep/pricebook/actual-work/{actualWorkId:guid}/expand-assembly", async (
            Guid actualWorkId,
            ActualWorkExpandAssemblyBody body,
            HttpRequest httpRequest,
            ActualWorkDraftApiService service,
            CancellationToken ct) =>
        {
            var versionResult = ParseActualWorkVersion(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new ExpandActualWorkAssemblyApiCommand(
                body.OfferingAssemblyId, body.IncludedOptionalItemIds ?? []);
            var result = await service.ExpandAssemblyAsync(actualWorkId, command, versionResult.Value, ct);
            return result.IsSuccess
                ? Results.Ok(new ActualWorkExpandAssemblyResponse(
                    result.Value.LineIds, result.Value.SkippedCatalogItemIds, result.Value.ActualWorkConcurrencyVersion))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPut("/keep/pricebook/actual-work/{actualWorkId:guid}/lines/{lineId:guid}", async (
            Guid actualWorkId,
            Guid lineId,
            ActualWorkUpdateLineBody body,
            HttpRequest httpRequest,
            ActualWorkDraftApiService service,
            CancellationToken ct) =>
        {
            var versionResult = ParseActualWorkVersion(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.UpdateLineAsync(
                actualWorkId, lineId, body.ActualQuantity, body.Note, versionResult.Value, ct);
            return result.IsSuccess
                ? Results.Ok(new ActualWorkConcurrencyVersionResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapDelete("/keep/pricebook/actual-work/{actualWorkId:guid}/lines/{lineId:guid}", async (
            Guid actualWorkId,
            Guid lineId,
            HttpRequest httpRequest,
            ActualWorkDraftApiService service,
            CancellationToken ct) =>
        {
            var versionResult = ParseActualWorkVersion(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.RemoveLineAsync(actualWorkId, lineId, versionResult.Value, ct);
            return result.IsSuccess
                ? Results.Ok(new ActualWorkConcurrencyVersionResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPost("/keep/pricebook/actual-work/{actualWorkId:guid}/submit", async (
            Guid actualWorkId,
            ActualWorkSubmitBody body,
            HttpRequest httpRequest,
            ActualWorkDraftApiService service,
            CancellationToken ct) =>
        {
            var versionResult = ParseActualWorkVersion(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            ActualWorkOutcome? outcome = null;
            if (body.Outcome is not null)
            {
                if (!Enum.TryParse<ActualWorkOutcome>(body.Outcome, ignoreCase: true, out var parsedOutcome))
                    return ErrorHttpMapper.ToHttpResult(ActualWorkErrors.InvalidOutcome);
                outcome = parsedOutcome;
            }

            var result = await service.SubmitAsync(actualWorkId, outcome, body.CompletionNote, versionResult.Value, ct);
            return result.IsSuccess
                ? Results.Ok(new ActualWorkConcurrencyVersionResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // ADR-494 D6 (4e-ii-c) — Owner/Admin office replacement of a pre-export submitted visit.
        app.MapPost("/keep/pricebook/actual-work/{actualWorkId:guid}/replace", async (
            Guid actualWorkId,
            ActualWorkReplaceBody body,
            HttpRequest httpRequest,
            ActualWorkReplacementApiService service,
            CancellationToken ct) =>
        {
            var versionResult = ParseActualWorkVersion(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.CreateReplacementAsync(
                actualWorkId, versionResult.Value, body.Reason ?? string.Empty, ct);
            return result.IsSuccess
                ? Results.Ok(new ActualWorkReplacementCreatedResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // ADR-494 D2 (4c-i-b) — recorder-only Draft ticket-default performer set/clear.
        app.MapPut("/keep/pricebook/actual-work/{actualWorkId:guid}/default-performer", async (
            Guid actualWorkId,
            ActualWorkDefaultPerformerBody body,
            HttpRequest httpRequest,
            ActualWorkDraftApiService service,
            CancellationToken ct) =>
        {
            var versionResult = ParseActualWorkVersion(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.SetDefaultPerformerAsync(
                actualWorkId, body.PerformedByAccountUserId, versionResult.Value, ct);
            return result.IsSuccess
                ? Results.Ok(new ActualWorkConcurrencyVersionResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // ADR-494 D5 (4c-ii) — recorder-only Draft visit note. Same auth + concurrency contract as
        // the line editor and default-performer route.
        app.MapPut("/keep/pricebook/actual-work/{actualWorkId:guid}/visit-note", async (
            Guid actualWorkId,
            ActualWorkVisitNoteBody body,
            HttpRequest httpRequest,
            ActualWorkDraftApiService service,
            CancellationToken ct) =>
        {
            var versionResult = ParseActualWorkVersion(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.SetVisitNoteAsync(
                actualWorkId, body.VisitNote, versionResult.Value, ct);
            return result.IsSuccess
                ? Results.Ok(new ActualWorkConcurrencyVersionResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // ADR-494 D6 (4e-ii-c) — recorder-only Draft editing of a copied zero-line disposition.
        app.MapPut("/keep/pricebook/actual-work/{actualWorkId:guid}/zero-line-disposition", async (
            Guid actualWorkId,
            ActualWorkZeroLineDispositionBody body,
            HttpRequest httpRequest,
            ActualWorkDraftApiService service,
            CancellationToken ct) =>
        {
            var versionResult = ParseActualWorkVersion(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            if (body.Outcome is null ||
                !Enum.TryParse<ActualWorkOutcome>(body.Outcome, ignoreCase: true, out var outcome))
                return ErrorHttpMapper.ToHttpResult(ActualWorkErrors.InvalidOutcome);

            var result = await service.SetZeroLineDispositionAsync(
                actualWorkId, outcome, body.CompletionNote, versionResult.Value, ct);
            return result.IsSuccess
                ? Results.Ok(new ActualWorkConcurrencyVersionResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // GAP-055 + BL136 Slice 4d — Draft recorder-ownership transfer. Owner/Admin: any request,
        // reason required. Current recorder: their own Draft only, reason omitted (a fixed system
        // reason is recorded). Every other caller gets NotFound.
        app.MapPost("/keep/pricebook/actual-work/{actualWorkId:guid}/transfer-recorder", async (
            Guid actualWorkId,
            ActualWorkTransferRecorderBody body,
            HttpRequest httpRequest,
            ActualWorkDraftApiService service,
            CancellationToken ct) =>
        {
            var versionResult = ParseActualWorkVersion(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new TransferActualWorkDraftRecorderApiCommand(
                body.NewRecorderAccountUserId, body.Reason ?? string.Empty);
            var result = await service.TransferRecorderAsync(actualWorkId, command, versionResult.Value, ct);
            return result.IsSuccess
                ? Results.Ok(new ActualWorkConcurrencyVersionResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Batch 6 — Owner/Admin-only office acknowledgement of a submitted visit.
        app.MapPost("/keep/pricebook/actual-work/{actualWorkId:guid}/review", async (
            Guid actualWorkId,
            ActualWorkReviewBody body,
            HttpRequest httpRequest,
            ActualWorkReviewApiService service,
            CancellationToken ct) =>
        {
            var versionResult = ParseActualWorkVersion(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.MarkReviewedAsync(actualWorkId, body.ReviewNote, versionResult.Value, ct);
            return result.IsSuccess
                ? Results.Ok(new ActualWorkConcurrencyVersionResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // BL135 §4 Batch 3a-ii — Owner/Admin office resolution of a missing per-line financial
        // component on a submitted, not-yet-reviewed visit. Rotates the visit concurrency version
        // so a stale review command is rejected as a conflict. Read-projection fold is Batch 3a-iii.
        app.MapPost("/keep/pricebook/actual-work/{actualWorkId:guid}/lines/{lineId:guid}/financial-resolution", async (
            Guid actualWorkId,
            Guid lineId,
            ActualWorkFinancialResolutionBody body,
            HttpRequest httpRequest,
            ActualWorkFinancialResolutionApiService service,
            CancellationToken ct) =>
        {
            var versionResult = ParseActualWorkVersion(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new ActualWorkFinancialResolutionCommand(
                body.ResolvedUnitSellPrice, body.ResolvedUnitStandardExpectedDirectCost, body.Basis, body.Reason);

            var result = await service.CreateResolutionAsync(actualWorkId, lineId, command, versionResult.Value, ct);
            return result.IsSuccess
                ? Results.Ok(new ActualWorkConcurrencyVersionResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // BL135 §4 Batch 3b-i — Owner/Admin office no-charge disposition of a zero-line submitted,
        // not-yet-reviewed visit (locked §6.2). Rotates the visit concurrency version so a stale
        // review command is rejected as a conflict. Hard review gate is Batch 3b-ii.
        app.MapPost("/keep/pricebook/actual-work/{actualWorkId:guid}/financial-disposition", async (
            Guid actualWorkId,
            ActualWorkFinancialDispositionBody body,
            HttpRequest httpRequest,
            ActualWorkOfficeFinancialDispositionApiService service,
            CancellationToken ct) =>
        {
            var versionResult = ParseActualWorkVersion(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new ActualWorkDispositionCommand(body.Kind, body.Reason);

            var result = await service.RecordDispositionAsync(actualWorkId, command, versionResult.Value, ct);
            return result.IsSuccess
                ? Results.Ok(new ActualWorkConcurrencyVersionResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapGet("/keep/pricebook/actual-work/request/{requestId:guid}/history", async (
            Guid requestId,
            ActualWorkHistoryReadApiService service,
            CancellationToken ct) =>
        {
            var result = await service.GetHistoryForRequestAsync(requestId, ct);
            return result.IsSuccess ? Results.Ok(ToActualWorkHistoryResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // 1a-ii — Owner/Admin-only account-wide list of members eligible to be assigned as an
        // Actual Work Draft recorder (drives the Draft-recovery transfer control).
        app.MapGet("/keep/pricebook/actual-work/recorder-candidates", async (
            GetActualWorkRecorderCandidatesService service,
            CancellationToken ct) =>
        {
            var result = await service.ExecuteAsync(ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // ADR-494 D2 (4c-i-b) — account-wide list of members who may be recorded as the performer of
        // Actual Work. NOT Owner/Admin-only: an Operator office transcriber records a paper ticket on
        // a technician's behalf, so the caller gate is the performer predicate itself.
        app.MapGet("/keep/pricebook/actual-work/performer-candidates", async (
            GetActualWorkPerformerCandidatesService service,
            CancellationToken ct) =>
        {
            var result = await service.ExecuteAsync(ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Batch 7 — Owner/Admin-only account-wide unreviewed-review queue.
        app.MapGet("/keep/pricebook/actual-work/review-queue", async (
            ActualWorkFinancialReadApiService service,
            CancellationToken ct) =>
        {
            var result = await service.GetReviewQueueAsync(ct);
            return result.IsSuccess
                ? Results.Ok(result.Value.Select(ToActualWorkReviewQueueEntryResponse).ToArray())
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Slice A-1 — authoritative count for the same queue, for badge/aggregate display that
        // must not force a full queue load just to get a number.
        app.MapGet("/keep/pricebook/actual-work/review-queue/count", async (
            ActualWorkFinancialReadApiService service,
            CancellationToken ct) =>
        {
            var result = await service.GetReviewQueueCountAsync(ct);
            return result.IsSuccess
                ? Results.Ok(new { count = result.Value })
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // BL138 Slice 1B-server — Owner/Admin-only, request-scoped list of submitted/unreviewed
        // visits with their three-value review readiness, for the Request Detail
        // "Pending financial reviews (N)" card. Same gate as the account-wide review queue.
        app.MapGet("/keep/pricebook/actual-work/request/{requestId:guid}/pending-financial-reviews", async (
            Guid requestId,
            ActualWorkFinancialReadApiService service,
            CancellationToken ct) =>
        {
            var result = await service.GetPendingReviewsForRequestAsync(requestId, ct);
            return result.IsSuccess
                ? Results.Ok(ToActualWorkRequestPendingReviewsResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Batch 7 — Owner/Admin-only single-visit financial detail (unreviewed or reviewed).
        app.MapGet("/keep/pricebook/actual-work/{actualWorkId:guid}/financial-detail", async (
            Guid actualWorkId,
            ActualWorkFinancialReadApiService service,
            CancellationToken ct) =>
        {
            var result = await service.GetFinancialDetailAsync(actualWorkId, ct);
            return result.IsSuccess
                ? Results.Ok(ToActualWorkFinancialDetailResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapDelete("/keep/pricebook/actual-work/{actualWorkId:guid}", async (
            Guid actualWorkId,
            HttpRequest httpRequest,
            ActualWorkDraftApiService service,
            CancellationToken ct) =>
        {
            var versionResult = ParseActualWorkVersion(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.DiscardAsync(actualWorkId, versionResult.Value, ct);
            return result.IsSuccess ? Results.NoContent() : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();
    }

    private static object ToActualWorkResponse(ActualWork actualWork) => new
    {
        id = actualWork.Id,
        requestId = actualWork.RequestId,
        status = actualWork.Status.ToString(),
        concurrencyVersion = actualWork.ConcurrencyVersion
    };

    private static object ToActualWorkHistoryResponse(ActualWorkHistoryResult result) => new
    {
        canCaptureActualWork = result.CanCaptureActualWork,
        openDraft = result.OpenDraft is null ? null : ToOpenDraftResponse(result.OpenDraft),
        openDraftHeldByOther = result.OpenDraftHeldByOther,
        submittedVisits = result.SubmittedVisits.Select(ToSubmittedVisitResponse),
    };

    private static object ToOpenDraftResponse(ActualWorkOpenDraftEntry draft) => new
    {
        id = draft.Id,
        status = draft.Status.ToString(),
        outcome = draft.Outcome?.ToString(),
        completionNote = draft.CompletionNote,
        submittedAtUtc = draft.SubmittedAtUtc,
        concurrencyVersion = draft.ConcurrencyVersion,
        isRecorder = draft.IsRecorder,
        // Populated only for the Owner/Admin non-recorder view (1a-ii recovery UI); null otherwise.
        recorderAccountUserId = draft.RecorderAccountUserId,
        recorderDisplayName = draft.RecorderDisplayName,
        // ADR-494 D2 (4c-i): persisted ticket-default performer, surfaced to both the recorder and
        // the Owner/Admin read-only view so the composer can restore its add region after a reload.
        defaultPerformedByAccountUserId = draft.DefaultPerformedByAccountUserId,
        defaultPerformerDisplayName = draft.DefaultPerformerDisplayName,
        // ADR-494 D5 (4c-ii): visit-level note, readable on history so the composer textarea
        // restores across reload. Latent until the 4c-iii field UI consumes it.
        visitNote = draft.VisitNote,
        lines = draft.Lines.Select(ToLineHistoryResponse),
    };

    private static object ToSubmittedVisitResponse(ActualWorkSubmittedVisitEntry visit) => new
    {
        id = visit.Id,
        status = visit.Status.ToString(),
        outcome = visit.Outcome?.ToString(),
        completionNote = visit.CompletionNote,
        submittedAtUtc = visit.SubmittedAtUtc,
        visitNote = visit.VisitNote,
        lines = visit.Lines.Select(ToLineHistoryResponse),
        // BL136 D6c (Slice 4e-ii-b) replacement-chain lineage, direction explicit.
        superseded = visit.Superseded,
        supersededByActualWorkId = visit.SupersededByActualWorkId,
        supersedesActualWorkId = visit.SupersedesActualWorkId,
    };

    private static object ToActualWorkReviewQueueEntryResponse(ActualWorkReviewQueueEntry entry) => new
    {
        actualWorkId = entry.ActualWorkId,
        requestId = entry.RequestId,
        referenceCode = entry.ReferenceCode,
        customerName = entry.CustomerName,
        requestStatus = entry.RequestStatus,
        submittedAtUtc = entry.SubmittedAtUtc,
        hasIncompleteFinancialData = entry.HasIncompleteFinancialData,
        incompleteLineCount = entry.IncompleteLineCount,
        totalSalesPrice = entry.TotalSalesPrice,
        totalStandardExpectedDirectCost = entry.TotalStandardExpectedDirectCost,
        totalMargin = entry.TotalMargin,
    };

    private static object ToActualWorkRequestPendingReviewsResponse(ActualWorkRequestPendingReviewsResult result) => new
    {
        count = result.Count,
        items = result.Items.Select(entry => new
        {
            actualWorkId = entry.ActualWorkId,
            submittedAtUtc = entry.SubmittedAtUtc,
            lineCount = entry.LineCount,
            recorderDisplayName = entry.RecorderDisplayName,
            reviewStatus = entry.ReviewStatus.ToString(),
        }).ToArray(),
    };

    private static object ToActualWorkFinancialDetailResponse(ActualWorkFinancialDetailResult result) => new
    {
        id = result.Id,
        requestId = result.RequestId,
        status = result.Status.ToString(),
        outcome = result.Outcome?.ToString(),
        completionNote = result.CompletionNote,
        visitNote = result.VisitNote,
        recorderAccountUserId = result.RecorderAccountUserId,
        submittedAtUtc = result.SubmittedAtUtc,
        reviewedAtUtc = result.ReviewedAtUtc,
        reviewedByAccountUserId = result.ReviewedByAccountUserId,
        reviewedByDisplayName = result.ReviewedByDisplayName,
        reviewNote = result.ReviewNote,
        hasIncompleteFinancialData = result.HasIncompleteFinancialData,
        totalSalesPrice = result.TotalSalesPrice,
        totalStandardExpectedDirectCost = result.TotalStandardExpectedDirectCost,
        totalMargin = result.TotalMargin,
        lines = result.Lines.Select(ToFinancialLineResponse),
        concurrencyVersion = result.ConcurrencyVersion,
        hasNoChargeDisposition = result.HasNoChargeDisposition,
        blockers = result.Blockers.Select(b => new
        {
            lineId = b.LineId,
            displayNameSnapshot = b.DisplayNameSnapshot,
            sellPriceMissing = b.SellPriceMissing,
            standardExpectedDirectCostMissing = b.StandardExpectedDirectCostMissing,
        }),
    };

    private static object ToFinancialLineResponse(ActualWorkFinancialLineEntry line) => new
    {
        id = line.Id,
        displayNameSnapshot = line.DisplayNameSnapshot,
        unitOfMeasureSnapshot = line.UnitOfMeasureSnapshot,
        actualQuantity = line.ActualQuantity,
        note = line.Note,
        performedByAccountUserId = line.PerformedByAccountUserId,
        performerDisplayName = line.PerformerDisplayName,
        isFinancialDataComplete = line.IsFinancialDataComplete,
        sellPriceSnapshot = line.SellPriceSnapshot,
        standardExpectedDirectCostSnapshot = line.StandardExpectedDirectCostSnapshot,
        lineSalesTotal = line.LineSalesTotal,
        lineStandardExpectedDirectCostTotal = line.LineStandardExpectedDirectCostTotal,
        lineMargin = line.LineMargin,
        sellPriceResolved = line.SellPriceResolved,
        resolvedSellPrice = line.ResolvedSellPrice,
        resolvedSellPriceBasis = line.ResolvedSellPriceBasis,
        directCostResolved = line.DirectCostResolved,
        resolvedStandardExpectedDirectCost = line.ResolvedStandardExpectedDirectCost,
        resolvedStandardExpectedDirectCostBasis = line.ResolvedStandardExpectedDirectCostBasis,
    };

    private static object ToLineHistoryResponse(ActualWorkLineHistoryEntry line) => new
    {
        id = line.Id,
        displayNameSnapshot = line.DisplayNameSnapshot,
        unitOfMeasureSnapshot = line.UnitOfMeasureSnapshot,
        actualQuantity = line.ActualQuantity,
        note = line.Note,
        performedByAccountUserId = line.PerformedByAccountUserId,
        performerDisplayName = line.PerformerDisplayName,
    };

    /// <summary>Strict parser for the <c>X-Keep-ActualWork-Version</c> optimistic-concurrency
    /// header — same contract as <see cref="ProposedScopeVersionHeader"/> (ADR-330-335, DEF-074):
    /// header must be present exactly once, canonical GUID "D" shape, Guid.Empty rejected. Inlined
    /// here rather than a dedicated header-parser file to stay within Batch 3's file gate (create
    /// carries no header, so this is used by every other draft mutation route).</summary>
    private static Result<Guid> ParseActualWorkVersion(IHeaderDictionary headers)
    {
        const string headerName = "X-Keep-ActualWork-Version";

        if (!headers.TryGetValue(headerName, out var values))
            return Result<Guid>.Failure(ActualWorkErrors.ExpectedVersionRequired);

        if (values.Count != 1)
            return Result<Guid>.Failure(ActualWorkErrors.ExpectedVersionInvalid);

        var trimmed = (values[0] ?? string.Empty).Trim();
        if (!Guid.TryParseExact(trimmed, "D", out var version) || version == Guid.Empty)
            return Result<Guid>.Failure(ActualWorkErrors.ExpectedVersionInvalid);

        return Result<Guid>.Success(version);
    }
}

file sealed record ActualWorkCreateBody(Guid RequestId, Guid? DefaultPerformedByAccountUserId = null);

file sealed record ActualWorkAddLineBody(
    Guid? CatalogItemId,
    string? OffCatalogDescription,
    decimal ActualQuantity,
    string? Note,
    Guid? PerformedByAccountUserId = null);

file sealed record ActualWorkUpdateLineBody(decimal ActualQuantity, string? Note);

/// <summary><see cref="Outcome"/> is a string parsed via <c>Enum.TryParse&lt;ActualWorkOutcome&gt;</c>
/// (matches the <c>ProposedScopeLineType</c>/<c>CatalogItemType</c>/<c>PriceTreatment</c> convention
/// — no global string-enum JSON converter is configured), one of <c>DiagnosticOnly</c>,
/// <c>NoWorkAuthorized</c>, <c>NoAccess</c> (case-insensitive).</summary>
file sealed record ActualWorkSubmitBody(string? Outcome, string? CompletionNote);

file sealed record ActualWorkReplaceBody(string? Reason);

file sealed record ActualWorkZeroLineDispositionBody(string? Outcome, string? CompletionNote);

/// <summary><c>Reason</c> is required for an Owner/Admin transfer and omitted for a recorder-initiated
/// hand-off (Slice 4d) — the service records a fixed system reason in that case.</summary>
file sealed record ActualWorkTransferRecorderBody(Guid NewRecorderAccountUserId, string? Reason = null);

/// <summary>ADR-494 D2 (4c-i-b). A null <see cref="PerformedByAccountUserId"/> clears the visit's
/// ticket default; a non-null value is revalidated server-side (empty guid collapses to 422).</summary>
file sealed record ActualWorkDefaultPerformerBody(Guid? PerformedByAccountUserId);

/// <summary>ADR-494 D5 (4c-ii). A null/blank <see cref="VisitNote"/> clears the Draft's visit note;
/// the domain trims it and rejects anything over 2,000 characters (<c>ActualWork.VisitNoteTooLong</c>).</summary>
file sealed record ActualWorkVisitNoteBody(string? VisitNote);

file sealed record ActualWorkReviewBody(string? ReviewNote);

/// <summary>BL135 §4 Batch 3a-ii. <see cref="Basis"/> is parsed to <c>FinancialResolutionBasis</c>
/// (case-insensitive) in the API service; at least one resolved value must be supplied.</summary>
file sealed record ActualWorkFinancialResolutionBody(
    decimal? ResolvedUnitSellPrice,
    decimal? ResolvedUnitStandardExpectedDirectCost,
    string? Basis,
    string? Reason);

/// <summary>BL135 §4 Batch 3b-i. <see cref="Kind"/> is parsed to
/// <c>OfficeFinancialDispositionKind</c> (trimmed, case-insensitive) in the API service;
/// <see cref="Reason"/> is validated/normalized by the domain factory.</summary>
file sealed record ActualWorkFinancialDispositionBody(string? Kind, string? Reason);

file sealed record ActualWorkLineAddedResponse(Guid LineId, Guid ActualWorkConcurrencyVersion);

file sealed record ActualWorkConcurrencyVersionResponse(Guid ConcurrencyVersion);

file sealed record ActualWorkReplacementCreatedResponse(Guid SuccessorActualWorkId);

/// <summary>Build-log/129's 5d-i preflight lock: <see cref="IncludedOptionalItemIds"/> names the
/// assembly's optional item ids to include; optional items default out (null/empty means none).</summary>
file sealed record ActualWorkExpandAssemblyBody(Guid OfferingAssemblyId, IReadOnlyList<Guid>? IncludedOptionalItemIds);

file sealed record ActualWorkExpandAssemblyResponse(
    IReadOnlyList<Guid> LineIds, IReadOnlyList<Guid> SkippedCatalogItemIds, Guid ActualWorkConcurrencyVersion);

