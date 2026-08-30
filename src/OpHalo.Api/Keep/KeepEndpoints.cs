using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OpHalo.Api.Helpers;
using OpHalo.Foundation.Application.Auth;
using OpHalo.Keep.Application.IntakeSetup;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Application.PublicIntake;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Api.Keep;

public static class KeepEndpoints
{
    public static void MapKeepEndpoints(this IEndpointRouteBuilder app)
    {
        // Public intake — anonymous, rate limited (ADR-051, ADR-059, ADR-060, ADR-429)
        app.MapGet("/keep/public-intake/token/{publicIntakeToken}/info",
            async (string publicIntakeToken, CreateKeepPublicIntakeService service, CancellationToken ct) =>
            {
                var info = await service.GetInfoByTokenAsync(publicIntakeToken, ct);
                return info is not null ? Results.Ok(ToPublicIntakeInfoResponse(info)) : Results.NotFound();
            })
            .RequireRateLimiting("public-intake");

        app.MapGet("/keep/public-intake/slug/{slug}/info",
            async (string slug, CreateKeepPublicIntakeService service, CancellationToken ct) =>
            {
                var info = await service.GetInfoBySlugAsync(slug, ct);
                return info is not null ? Results.Ok(ToPublicIntakeInfoResponse(info)) : Results.NotFound();
            })
            .RequireRateLimiting("public-intake");

        app.MapPost("/keep/public-intake/token/{publicIntakeToken}", HandlePublicIntake)
           .RequireRateLimiting("public-intake");

        app.MapPost("/keep/public-intake/slug/{slug}", HandlePublicIntakeBySlug)
           .RequireRateLimiting("public-intake");

        // Intake setup — authenticated, Owner/Admin only (GAP-001)
        app.MapGet("/keep/setup/intake", async (KeepIntakeSetupService service, CancellationToken ct) =>
        {
            var result = await service.GetOrEnsureStatusAsync(ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPost("/keep/setup/intake/ensure", async (KeepIntakeSetupService service, CancellationToken ct) =>
        {
            var result = await service.EnsureAsync(ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPost("/keep/setup/intake/replace", async (ReplaceIntakeBody? body, KeepIntakeSetupService service, CancellationToken ct) =>
        {
            var result = await service.ReplaceAsync(body?.Confirmation, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPut("/keep/setup/intake/link-name", async (RenameLinkNameBody body, KeepIntakeSetupService service, CancellationToken ct) =>
        {
            var result = await service.RenameAsync(body.DesiredName, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Intake SMS handoff creation — authenticated, Owner/Admin only (R88f-c, GAP-018)
        app.MapPost("/keep/setup/intake/sms-handoff", async (
            IntakeSmsHandoffBody body,
            CreateIntakeSmsHandoffService service,
            IOptions<MagicLinkSettings> appSettings,
            CancellationToken ct) =>
        {
            var result = await service.ExecuteAsync(new CreateIntakeSmsHandoffCommand(body.CustomerPhone), ct);
            if (!result.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(result.Error);
            var publicBaseUrl = appSettings.Value.PublicBaseUrl.TrimEnd('/');
            var handoffUrl = $"{publicBaseUrl}/keep/intake-sms/{result.Value.RawToken}";
            return Results.Ok(new
            {
                handoffUrl,
                customerPhone = result.Value.CustomerPhone,
                messageBody = result.Value.MessageBody,
                expiresAtUtc = result.Value.ExpiresAtUtc,
            });
        }).RequireAuthorization();

        // Intake SMS handoff resolve — public, rate-limited, no-store cache (R88f-c, GAP-018)
        // Expired, invalid, and legacy blank-phone tokens are intentionally indistinguishable (404).
        app.MapGet("/keep/intake-sms/{handoffToken}", async (
            string handoffToken,
            HttpContext httpContext,
            IKeepIntakeSmsHandoffPersistence persistence,
            IClock clock,
            CancellationToken ct) =>
        {
            httpContext.Response.Headers.CacheControl = "no-store, private";
            var tokenHash = KeepIntakeSmsHandoff.HashToken(handoffToken);
            var result = await persistence.FindValidByHashAsync(tokenHash, clock.UtcNow, ct);
            return result is null
                ? Results.NotFound()
                : Results.Ok(new { result.CustomerPhone, result.MessageBody, result.ExpiresAtUtc });
        }).RequireRateLimiting("public-intake");

        // Business profile + response policy — authenticated, Owner/Admin only (S12a)
        app.MapGet("/keep/setup", async (KeepSetupService service, CancellationToken ct) =>
        {
            var result = await service.GetSetupAsync(ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPut("/keep/setup/profile", async (UpdateProfileBody body, KeepSetupService service, CancellationToken ct) =>
        {
            var result = await service.UpdateProfileAsync(
                body.BusinessName, body.TimeZone, body.CustomerFacingPhone, body.CustomerFacingEmail,
                body.LogoUrl, body.WebsiteUrl, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPut("/keep/setup/policy", async (UpdatePolicyBody body, KeepSetupService service, CancellationToken ct) =>
        {
            var result = await service.UpdatePolicyAsync(
                body.FirstResponseTargetMinutes, body.StandardResponseTargetMinutes,
                body.PriorityResponseTargetMinutes, body.StatusCheckThresholdDays, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapGet("/keep/setup/onboarding", async (KeepOnboardingService service, CancellationToken ct) =>
        {
            var result = await service.GetChecklistAsync(ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPost("/keep/setup/onboarding/marks/quick-capture-exercise", async (KeepOnboardingService service, CancellationToken ct) =>
        {
            var result = await service.MarkStepCompleteAsync(KeepOnboardingManualStep.QuickCaptureExercise, ct);
            return result.IsSuccess ? Results.NoContent() : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPost("/keep/setup/onboarding/marks/tracker-review", async (KeepOnboardingService service, CancellationToken ct) =>
        {
            var result = await service.MarkStepCompleteAsync(KeepOnboardingManualStep.TrackerReview, ct);
            return result.IsSuccess ? Results.NoContent() : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPost("/keep/setup/onboarding/marks/spam-classification", async (KeepOnboardingService service, CancellationToken ct) =>
        {
            var result = await service.MarkStepCompleteAsync(KeepOnboardingManualStep.SpamClassification, ct);
            return result.IsSuccess ? Results.NoContent() : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapGet("/keep/setup/guided", async (KeepBusinessSetupService service, CancellationToken ct) =>
        {
            var result = await service.GetBusinessSetupAsync(ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPost("/keep/setup/guided/defer/{step:int}", async (int step, KeepBusinessSetupService service, CancellationToken ct) =>
        {
            var result = await service.DeferStepAsync((KeepSetupStep)step, ct);
            return result.IsSuccess ? Results.NoContent() : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Operator list — requires authenticated session (Phase 5A, extended Session 4A)
        app.MapGet("/keep/requests", async (
            HttpRequest request,
            GetKeepRequestListService service,
            CancellationToken ct) =>
        {
            var bind = KeepRequestListQueryBinding.Bind(request.Query);
            if (!bind.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(bind.Error);

            var result = await service.ExecuteAsync(bind.Value, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Business-created request — authenticated, Owner/Admin/Operator (G3b)
        app.MapPost("/keep/requests", async (
            CreateBusinessRequestBody body,
            CreateBusinessRequestService service,
            CancellationToken ct) =>
        {
            var command = new CreateBusinessRequestCommand(
                body.CustomerName, body.CustomerPhone, body.CustomerEmail, body.Description, body.Source,
                body.ServiceAddressLine1, body.ServiceAddressLine2, body.ServiceCity,
                body.ServiceState, body.ServiceZip, body.ExistingCustomerId);
            var result = await service.ExecuteAsync(command, ct);
            return result.IsSuccess
                ? Results.Created($"/keep/requests/{result.Value.RequestId}", result.Value)
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Phone lookup gate — authenticated, mirrors create posture (S13b)
        app.MapGet("/keep/requests/lookup", async (
            string? phone,
            LookupKeepRequestByPhoneService service,
            CancellationToken ct) =>
        {
            var result = await service.ExecuteAsync(phone, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Available queue — Operator-only dedicated surface (G4d)
        app.MapGet("/keep/requests/available", async (
            HttpRequest request,
            GetAvailableKeepRequestsService service,
            CancellationToken ct) =>
        {
            var bind = KeepAvailableRequestQueryBinding.Bind(request.Query);
            if (!bind.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(bind.Error);

            var result = await service.ExecuteAsync(bind.Value, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Operator request detail — authenticated, scoped to caller's account (Phase 8-B1-β)
        app.MapGet("/keep/requests/{requestId:guid}", async (
            Guid requestId,
            string? navView,
            GetKeepRequestDetailService service,
            CancellationToken ct) =>
        {
            var result = await service.ExecuteAsync(requestId, navView, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // GAP-050: compact related-work indicator — a dedicated read so mutation responses
        // never carry (and therefore never stale-overwrite) this data in the frontend cache.
        app.MapGet("/keep/requests/{requestId:guid}/related-work", async (
            Guid requestId,
            GetKeepRequestRelatedWorkService service,
            CancellationToken ct) =>
        {
            var result = await service.ExecuteAsync(requestId, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Change request status — authenticated, operator write (Phase 8-B2-alpha)
        app.MapPatch("/keep/requests/{requestId:guid}/status", async (
            Guid requestId,
            string? navView,
            HttpRequest httpRequest,
            ChangeStatusRequestBody body,
            ChangeKeepRequestStatusService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new ChangeKeepRequestStatusCommand(requestId, body.Status, body.Message, versionResult.Value, navView);
            var result = await service.ExecuteAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Classify request as Spam or Test — authenticated, Owner/Admin only (ADR-349/350, S7e)
        app.MapPost("/keep/requests/{requestId:guid}/classify", async (
            Guid requestId,
            HttpRequest httpRequest,
            ClassifyRequestBody body,
            ClassifyKeepRequestService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new ClassifyKeepRequestCommand(requestId, body.TargetStatus, body.Reason, versionResult.Value);
            var result = await service.ExecuteAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Share intent clearing — authenticated, operator write (S11b)
        app.MapPost("/keep/requests/{requestId:guid}/share-intent", async (
            Guid requestId,
            ShareIntentBody body,
            ClearShareIntentService service,
            CancellationToken ct) =>
        {
            var command = new ClearShareIntentCommand(requestId, body.Method);
            var result = await service.ExecuteAsync(command, ct);
            return result.IsSuccess ? Results.NoContent() : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // SMS handoff token creation — authenticated, operator write (S25a)
        app.MapPost("/keep/requests/{requestId:guid}/sms-handoff", async (
            Guid requestId,
            SmsHandoffBody body,
            CreateSmsHandoffService service,
            IOptions<MagicLinkSettings> appSettings,
            CancellationToken ct) =>
        {
            var command = new CreateSmsHandoffCommand(requestId, body.MessageBody);
            var result = await service.ExecuteAsync(command, ct);
            if (!result.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(result.Error);
            var publicBaseUrl = appSettings.Value.PublicBaseUrl.TrimEnd('/');
            var handoffUrl = $"{publicBaseUrl}/keep/share-sms/{result.Value.RawToken}";
            return Results.Ok(new { handoffUrl, expiresAtUtc = result.Value.ExpiresAtUtc });
        }).RequireAuthorization();

        // SMS handoff token resolve — public, no auth required (S25a)
        // Returns phone + message for a valid token; 404 for expired or invalid tokens.
        // Expired and invalid cases are intentionally indistinguishable (no payload leakage).
        app.MapGet("/keep/share-sms/{handoffToken}", async (
            HttpContext httpContext,
            string handoffToken,
            IKeepSmsHandoffPersistence handoffPersistence,
            IClock clock,
            CancellationToken ct) =>
        {
            httpContext.Response.Headers.CacheControl = "no-store, private";
            var tokenHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(handoffToken))).ToLowerInvariant();
            var handoff = await handoffPersistence.FindValidByHashAsync(tokenHash, clock.UtcNow, ct);
            return handoff is null
                ? Results.NotFound()
                : Results.Ok(new { handoff.CustomerPhone, handoff.MessageBody, handoff.ExpiresAtUtc });
        }).RequireRateLimiting("public-intake");

        // Call handoff token creation — authenticated, operator write (ADR-448, GAP-020)
        app.MapPost("/keep/requests/{requestId:guid}/call-handoff", async (
            Guid requestId,
            CreateCallHandoffService service,
            IOptions<MagicLinkSettings> appSettings,
            CancellationToken ct) =>
        {
            var command = new CreateCallHandoffCommand(requestId);
            var result = await service.ExecuteAsync(command, ct);
            if (!result.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(result.Error);
            var publicBaseUrl = appSettings.Value.PublicBaseUrl.TrimEnd('/');
            var handoffUrl = $"{publicBaseUrl}/keep/share-call/{result.Value.RawToken}";
            return Results.Ok(new { handoffUrl, expiresAtUtc = result.Value.ExpiresAtUtc });
        }).RequireAuthorization();

        // Call handoff token resolve — public, no auth required (ADR-448, GAP-020)
        // Returns phone for a valid token; 404 for expired or invalid tokens.
        // Expired and invalid cases are intentionally indistinguishable (no payload leakage).
        app.MapGet("/keep/share-call/{handoffToken}", async (
            HttpContext httpContext,
            string handoffToken,
            IKeepCallHandoffPersistence handoffPersistence,
            IClock clock,
            CancellationToken ct) =>
        {
            httpContext.Response.Headers.CacheControl = "no-store, private";
            var tokenHash = KeepCallHandoff.HashToken(handoffToken);
            var handoff = await handoffPersistence.FindValidByHashAsync(tokenHash, clock.UtcNow, ct);
            return handoff is null
                ? Results.NotFound()
                : Results.Ok(new { handoff.CustomerPhone, handoff.ExpiresAtUtc });
        }).RequireRateLimiting("public-intake");

        // Add business update — authenticated, operator write (Phase 8-B2-beta)
        app.MapPost("/keep/requests/{requestId:guid}/business-updates", async (
            Guid requestId,
            HttpRequest httpRequest,
            BusinessUpdateRequestBody body,
            AddBusinessUpdateService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new AddBusinessUpdateCommand(requestId, body.Message, body.SetStatus, versionResult.Value);
            var result = await service.ExecuteAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Add internal note — authenticated, operator write (Phase 8-B2-beta)
        app.MapPost("/keep/requests/{requestId:guid}/internal-notes", async (
            Guid requestId,
            HttpRequest httpRequest,
            InternalNoteRequestBody body,
            AddInternalNoteService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new AddInternalNoteCommand(requestId, body.Note, versionResult.Value);
            var result = await service.ExecuteAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Log external contact — authenticated, operator write (Phase 8-B5/Session 2B)
        app.MapPost("/keep/requests/{requestId:guid}/external-contact", async (
            Guid requestId,
            HttpRequest httpRequest,
            ExternalContactRequestBody body,
            LogExternalContactService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new LogExternalContactCommand(
                requestId, body.Direction, body.Channel, body.Outcome,
                body.RequiresBusinessFollowUp, body.Summary, versionResult.Value);
            var result = await service.ExecuteAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Prepare update notification — authenticated, operator write (GAP-052a, ADR-451)
        app.MapPost("/keep/requests/{requestId:guid}/notification-preparation", async (
            Guid requestId,
            HttpRequest httpRequest,
            PrepareUpdateNotificationRequestBody body,
            PrepareUpdateNotificationService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new PrepareUpdateNotificationCommand(
                requestId, body.RelatedUpdateEventId, body.Channel, versionResult.Value);
            var result = await service.ExecuteAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Confirm update notification — authenticated, operator write (GAP-052a, ADR-451)
        app.MapPost("/keep/requests/{requestId:guid}/notification-confirmation", async (
            Guid requestId,
            HttpRequest httpRequest,
            ConfirmUpdateNotificationRequestBody body,
            ConfirmUpdateNotificationService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new ConfirmUpdateNotificationCommand(
                requestId, body.RelatedUpdateEventId, body.Channel, versionResult.Value);
            var result = await service.ExecuteAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Acknowledge attention — authenticated, operator write (Phase 8-B2-gamma)
        app.MapPost("/keep/requests/{requestId:guid}/attention/acknowledge", async (
            Guid requestId,
            HttpRequest httpRequest,
            AcknowledgeAttentionRequestBody body,
            AcknowledgeAttentionService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new AcknowledgeAttentionCommand(requestId, body.Reason, versionResult.Value);
            var result = await service.ExecuteAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Mark feedback reviewed — authenticated, Owner/Admin write (Phase 8-B5/Session 5B, ADR-274)
        app.MapPost("/keep/requests/{requestId:guid}/feedback-review", async (
            Guid requestId,
            HttpRequest httpRequest,
            FeedbackReviewRequestBody body,
            MarkFeedbackReviewedService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new MarkFeedbackReviewedCommand(requestId, body.Note, versionResult.Value);
            var result = await service.ExecuteAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Participant candidates — authenticated, Owner/Admin read (Phase 8-B5/Session 3B, ADR-235)
        app.MapGet("/keep/requests/participant-candidates", async (
            GetParticipantCandidatesService service,
            CancellationToken ct) =>
        {
            var result = await service.ExecuteAsync(ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Assign/transfer responsible — authenticated, Owner/Admin write (Phase 8-B5/Session 3B, ADR-230)
        app.MapPut("/keep/requests/{requestId:guid}/responsible", async (
            Guid requestId,
            HttpRequest httpRequest,
            SetResponsibleRequestBody body,
            ManageResponsibleService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new SetResponsibleCommand(requestId, body.AccountUserId, body.Note, versionResult.Value);
            var result = await service.SetAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Clear responsible — authenticated, Owner/Admin write (Phase 8-B5/Session 3B, ADR-230)
        app.MapDelete("/keep/requests/{requestId:guid}/responsible", async (
            Guid requestId,
            HttpRequest httpRequest,
            [FromBody] ClearResponsibleRequestBody? body,
            ManageResponsibleService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new ClearResponsibleCommand(requestId, body?.Note, versionResult.Value);
            var result = await service.ClearAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Add watcher (managed) — authenticated, Owner/Admin write (Phase 8-B5/Session 3B, ADR-230)
        app.MapPut("/keep/requests/{requestId:guid}/watchers/{accountUserId:guid}", async (
            Guid requestId,
            Guid accountUserId,
            HttpRequest httpRequest,
            WatcherRequestBody? body,
            ManageWatcherService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new AddWatcherCommand(requestId, accountUserId, body?.Note, versionResult.Value);
            var result = await service.AddAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Remove watcher (managed) — authenticated, Owner/Admin write (Phase 8-B5/Session 3B, ADR-230)
        app.MapDelete("/keep/requests/{requestId:guid}/watchers/{accountUserId:guid}", async (
            Guid requestId,
            Guid accountUserId,
            HttpRequest httpRequest,
            [FromBody] WatcherRequestBody? body,
            ManageWatcherService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new RemoveWatcherCommand(requestId, accountUserId, body?.Note, versionResult.Value);
            var result = await service.RemoveAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Self-watch — authenticated, operator write (Phase 8-B5/Session 3B, ADR-230)
        app.MapPut("/keep/requests/{requestId:guid}/watch", async (
            Guid requestId,
            HttpRequest httpRequest,
            SelfWatchService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.WatchAsync(requestId, versionResult.Value, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Self-unwatch — authenticated, operator write (Phase 8-B5/Session 3B, ADR-230)
        app.MapDelete("/keep/requests/{requestId:guid}/watch", async (
            Guid requestId,
            HttpRequest httpRequest,
            SelfWatchService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.UnwatchAsync(requestId, versionResult.Value, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Mute — authenticated, operator write (Phase 8-B5/Session 3B, ADR-230)
        app.MapPut("/keep/requests/{requestId:guid}/mute", async (
            Guid requestId,
            HttpRequest httpRequest,
            MuteService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.MuteAsync(requestId, versionResult.Value, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Unmute — authenticated, operator write (Phase 8-B5/Session 3B, ADR-230)
        app.MapDelete("/keep/requests/{requestId:guid}/mute", async (
            Guid requestId,
            HttpRequest httpRequest,
            MuteService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.UnmuteAsync(requestId, versionResult.Value, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Follow Up On — authenticated, versioned operator write (P6b-2/ADR-337)
        app.MapPut("/keep/requests/{requestId:guid}/follow-up-on", async (
            Guid requestId,
            HttpRequest httpRequest,
            SetFollowUpOnRequestBody body,
            ManageRequestTimingService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            if (!DateOnly.TryParseExact(body.Date, "yyyy-MM-dd", out var date))
                return ErrorHttpMapper.ToHttpResult(KeepRequestErrors.InvalidDateFormat);

            var command = new SetFollowUpOnCommand(requestId, date, body.Reason, body.Note, versionResult.Value);
            var result = await service.SetFollowUpOnAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapDelete("/keep/requests/{requestId:guid}/follow-up-on", async (
            Guid requestId,
            HttpRequest httpRequest,
            ManageRequestTimingService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new ClearFollowUpOnCommand(requestId, versionResult.Value);
            var result = await service.ClearFollowUpOnAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Planned For — authenticated, versioned operator write (P6b-2/ADR-338)
        app.MapPut("/keep/requests/{requestId:guid}/planned-for", async (
            Guid requestId,
            HttpRequest httpRequest,
            SetPlannedForRequestBody body,
            ManageRequestTimingService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            if (!DateOnly.TryParseExact(body.Date, "yyyy-MM-dd", out var date))
                return ErrorHttpMapper.ToHttpResult(KeepRequestErrors.InvalidDateFormat);

            var command = new SetPlannedForCommand(requestId, date, versionResult.Value);
            var result = await service.SetPlannedForAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapDelete("/keep/requests/{requestId:guid}/planned-for", async (
            Guid requestId,
            HttpRequest httpRequest,
            ManageRequestTimingService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new ClearPlannedForCommand(requestId, versionResult.Value);
            var result = await service.ClearPlannedForAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Follow-up resolution — authenticated, versioned operator write (ADR-440, S83b)
        app.MapPost("/keep/requests/{requestId:guid}/follow-up-resolution", async (
            Guid requestId,
            HttpRequest httpRequest,
            ResolveFollowUpBody body,
            ManageRequestTimingService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            DateOnly? newDate = null;
            if (body.NewDate is not null)
            {
                if (!DateOnly.TryParseExact(body.NewDate, "yyyy-MM-dd", out var parsedDate))
                    return ErrorHttpMapper.ToHttpResult(KeepRequestErrors.InvalidDateFormat);
                newDate = parsedDate;
            }

            var command = new ResolveFollowUpCommand(
                requestId, body.Outcome, body.CompletionReason,
                body.Note, newDate, body.NewFollowUpReason, versionResult.Value);
            var result = await service.ResolveFollowUpAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        // Service Location — authenticated, versioned operator write (GAP-006)
        app.MapPut("/keep/requests/{requestId:guid}/service-location", async (
            Guid requestId,
            HttpRequest httpRequest,
            UpdateServiceLocationBody body,
            UpdateServiceLocationService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new UpdateServiceLocationCommand(
                requestId,
                body.AddressLine1,
                body.AddressLine2,
                body.City,
                body.State,
                body.Zip,
                versionResult.Value);
            var result = await service.ExecuteAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPut("/keep/requests/{requestId:guid}/priority", async (
            Guid requestId,
            HttpRequest httpRequest,
            BusinessPriorityRequest body,
            SetBusinessPriorityService service,
            CancellationToken ct) =>
        {
            var versionResult = KeepRequestVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            BusinessPriority? priority = null;
            if (!string.IsNullOrWhiteSpace(body.Priority))
            {
                priority = body.Priority.ToLowerInvariant() switch
                {
                    "routine" => BusinessPriority.Routine,
                    "soon"    => BusinessPriority.Soon,
                    "urgent"  => BusinessPriority.Urgent,
                    _         => (BusinessPriority?)null
                };
                if (priority is null)
                    return Results.UnprocessableEntity(new { error = "Invalid priority value. Expected: routine, soon, or urgent." });
            }

            var command = new SetBusinessPriorityCommand(requestId, priority, versionResult.Value);
            var result = await service.ExecuteAsync(command, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

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

        // GAP-055 — Owner/Admin-only, reason-required Draft recorder-ownership transfer.
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

            var command = new TransferActualWorkDraftRecorderApiCommand(body.NewRecorderAccountUserId, body.Reason);
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

        // Customer page — anonymous, resolved by page token (Phase 8-B1-β)
        // Returns 200 (active) or 410 (expired). Expired body: { businessName, referenceCode, isExpired, newRequestUrl }.
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
        });

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

    private static async Task<IResult> HandlePublicIntake(
        [FromRoute] string publicIntakeToken,
        [FromBody] PublicIntakeRequest body,
        CreateKeepPublicIntakeService service,
        CancellationToken ct)
    {
        var command = new CreateKeepPublicIntakeCommand(
            publicIntakeToken,
            body.CustomerName,
            body.CustomerPhone,
            body.CustomerEmail,
            body.Description,
            body.ServiceAddressLine1 ?? string.Empty,
            body.ServiceAddressLine2,
            body.ServiceCity ?? string.Empty,
            body.ServiceState ?? string.Empty,
            body.ServiceZip,
            Enum.TryParse<IntakeUrgency>(body.Urgency, ignoreCase: true, out var urgency) ? urgency : IntakeUrgency.Routine,
            Enum.TryParse<ContactPreference>(body.ContactPreference, ignoreCase: true, out var contactPref) ? contactPref : ContactPreference.NoPreference);

        var result = await service.ExecuteAsync(command, ct);

        if (!result.IsSuccess)
            return ErrorHttpMapper.ToHttpResult(result.Error);

        return Results.Created(
            (string?)null,
            new { result.Value.RequestId, result.Value.ReferenceCode, result.Value.PageToken });
    }

    private static async Task<IResult> HandlePublicIntakeBySlug(
        [FromRoute] string slug,
        [FromBody] PublicIntakeRequest body,
        CreateKeepPublicIntakeService service,
        CancellationToken ct)
    {
        var command = new CreateKeepPublicIntakeCommand(
            string.Empty,
            body.CustomerName,
            body.CustomerPhone,
            body.CustomerEmail,
            body.Description,
            body.ServiceAddressLine1 ?? string.Empty,
            body.ServiceAddressLine2,
            body.ServiceCity ?? string.Empty,
            body.ServiceState ?? string.Empty,
            body.ServiceZip,
            Enum.TryParse<IntakeUrgency>(body.Urgency, ignoreCase: true, out var urgency2) ? urgency2 : IntakeUrgency.Routine,
            Enum.TryParse<ContactPreference>(body.ContactPreference, ignoreCase: true, out var contactPref2) ? contactPref2 : ContactPreference.NoPreference);

        var result = await service.ExecuteBySlugAsync(slug, command, ct);

        if (!result.IsSuccess)
            return ErrorHttpMapper.ToHttpResult(result.Error);

        return Results.Created(
            (string?)null,
            new { result.Value.RequestId, result.Value.ReferenceCode, result.Value.PageToken });
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

    // Public-safe identity projection (GAP-033/R90b-2a) — never includes email.
    private static object ToPublicIntakeInfoResponse(KeepPublicIntakeInfo info) => new
    {
        businessName = info.BusinessName,
        logoUrl = info.LogoUrl,
        websiteUrl = info.WebsiteUrl,
        phone = info.Phone
    };

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
        lines = draft.Lines.Select(ToLineHistoryResponse),
    };

    private static object ToSubmittedVisitResponse(ActualWorkSubmittedVisitEntry visit) => new
    {
        id = visit.Id,
        status = visit.Status.ToString(),
        outcome = visit.Outcome?.ToString(),
        completionNote = visit.CompletionNote,
        submittedAtUtc = visit.SubmittedAtUtc,
        lines = visit.Lines.Select(ToLineHistoryResponse),
    };

    private static object ToActualWorkReviewQueueEntryResponse(ActualWorkReviewQueueEntry entry) => new
    {
        actualWorkId = entry.ActualWorkId,
        requestId = entry.RequestId,
        referenceCode = entry.ReferenceCode,
        customerName = entry.CustomerName,
        submittedAtUtc = entry.SubmittedAtUtc,
        hasIncompleteFinancialData = entry.HasIncompleteFinancialData,
        incompleteLineCount = entry.IncompleteLineCount,
        totalSalesPrice = entry.TotalSalesPrice,
        totalStandardExpectedDirectCost = entry.TotalStandardExpectedDirectCost,
        totalMargin = entry.TotalMargin,
    };

    private static object ToActualWorkFinancialDetailResponse(ActualWorkFinancialDetailResult result) => new
    {
        id = result.Id,
        requestId = result.RequestId,
        status = result.Status.ToString(),
        outcome = result.Outcome?.ToString(),
        completionNote = result.CompletionNote,
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

file sealed record ActualWorkTransferRecorderBody(Guid NewRecorderAccountUserId, string Reason);

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

/// <summary>Build-log/129's 5d-i preflight lock: <see cref="IncludedOptionalItemIds"/> names the
/// assembly's optional item ids to include; optional items default out (null/empty means none).</summary>
file sealed record ActualWorkExpandAssemblyBody(Guid OfferingAssemblyId, IReadOnlyList<Guid>? IncludedOptionalItemIds);

file sealed record ActualWorkExpandAssemblyResponse(
    IReadOnlyList<Guid> LineIds, IReadOnlyList<Guid> SkippedCatalogItemIds, Guid ActualWorkConcurrencyVersion);

// Follow-up resolution request body (ADR-440, S83b).
// NewDate / NewFollowUpReason are only used when Outcome == "move".
file sealed record ResolveFollowUpBody(
    string Outcome,
    string? CompletionReason,
    string? Note,
    string? NewDate,
    string? NewFollowUpReason);
