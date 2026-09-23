using Microsoft.AspNetCore.Mvc;
using OpHalo.Api.Helpers;
using OpHalo.Keep.Application.PublicIntake;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Api.Keep;

/// <summary>
/// Maintainability review item 5: public-intake routes split out of <c>KeepEndpoints.cs</c> (see
/// build-log for the route-family split). No behavior change — locked by
/// <c>KeepEndpointsRouteInventoryTests</c>.
/// </summary>
public static class PublicIntakeEndpoints
{
    public static void MapPublicIntakeEndpoints(this IEndpointRouteBuilder app)
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

    // Public-safe identity projection (GAP-033/R90b-2a) — never includes email.
    private static object ToPublicIntakeInfoResponse(KeepPublicIntakeInfo info) => new
    {
        businessName = info.BusinessName,
        logoUrl = info.LogoUrl,
        websiteUrl = info.WebsiteUrl,
        phone = info.Phone
    };
}
