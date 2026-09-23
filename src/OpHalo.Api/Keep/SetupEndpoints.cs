using Microsoft.Extensions.Options;
using OpHalo.Api.Helpers;
using OpHalo.Foundation.Application.Auth;
using OpHalo.Keep.Application.IntakeSetup;
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.Api.Keep;

/// <summary>
/// Maintainability review item 5: intake-setup, business profile/response-policy, onboarding, and
/// guided-setup routes split out of <c>KeepEndpoints.cs</c>. No behavior change — locked by
/// <c>KeepEndpointsRouteInventoryTests</c>.
/// </summary>
public static class SetupEndpoints
{
    public static void MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
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
                body.LogoUrl, body.WebsiteUrl, body.SettingsVersion, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPut("/keep/setup/policy", async (UpdatePolicyBody body, KeepSetupService service, CancellationToken ct) =>
        {
            var result = await service.UpdatePolicyAsync(
                body.FirstResponseTargetMinutes, body.StandardResponseTargetMinutes,
                body.PriorityResponseTargetMinutes, body.StatusCheckThresholdDays,
                body.FirstResponseTimingBasis, body.StandardResponseTimingBasis,
                body.PriorityResponseTimingBasis, body.SettingsVersion, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPut("/keep/setup/calendar", async (UpdateCalendarBody body, KeepSetupService service, CancellationToken ct) =>
        {
            var result = await service.UpdateCalendarAsync(
                body.WeeklyIntervals, body.Closures, body.SettingsVersion, ct);
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
    }
}
