using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpHalo.Api.Helpers;
using OpHalo.Foundation.Application.Devices;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;

namespace OpHalo.Api.Accounts;

public static class AccountDeviceEndpoints
{
    public static void MapAccountDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/me/devices/{appInstallationId}", Register).RequireAuthorization();
        app.MapDelete("/me/devices/{appInstallationId}", Revoke).RequireAuthorization();
    }

    // -------------------------------------------------------------------------
    // PUT /me/devices/{appInstallationId}
    // -------------------------------------------------------------------------

    private static async Task<IResult> Register(
        [FromRoute] string appInstallationId,
        [FromBody] RegisterDeviceBody body,
        AccountUserDeviceService service,
        CancellationToken ct)
    {
        if (!TryParseInstallationId(appInstallationId, out var installId))
            return ErrorHttpMapper.ToHttpResult(AccountUserDeviceErrors.InvalidAppInstallationId);

        if (body.PushToken is not null && string.IsNullOrWhiteSpace(body.PushToken))
            return ValidationProblem("pushToken must be null or non-empty.", "Validation.PushTokenInvalid");

        if (body.PushToken is not null && body.PushToken.Length > 1024)
            return ValidationProblem("pushToken must not exceed 1024 characters.", "Validation.PushTokenTooLong");

        if (body.AppVersion is not null && body.AppVersion.Length > 50)
            return ValidationProblem("appVersion must not exceed 50 characters.", "Validation.AppVersionTooLong");

        if (body.DeviceName is not null && body.DeviceName.Length > 200)
            return ValidationProblem("deviceName must not exceed 200 characters.", "Validation.DeviceNameTooLong");

        var platform = ParsePlatform(body.Platform);
        if (platform is null)
            return ValidationProblem("platform must be 'ios' or 'android'.", "Validation.PlatformInvalid");

        var result = await service.RegisterAsync(installId, platform.Value, body.PushToken, body.AppVersion, body.DeviceName, ct);

        if (result.IsFailure)
            return ErrorHttpMapper.ToHttpResult(result.Error);

        var r = result.Value;
        return Results.Ok(new
        {
            appInstallationId = r.AppInstallationId,
            platform = r.Platform,
            status = r.Status,
            tokenFingerprint = r.TokenFingerprint,
            tokenLastFour = r.TokenLastFour,
            appVersion = r.AppVersion,
            deviceName = r.DeviceName,
            createdAtUtc = r.CreatedAtUtc,
            lastSeenAtUtc = r.LastSeenAtUtc
        });
    }

    // -------------------------------------------------------------------------
    // DELETE /me/devices/{appInstallationId}
    // -------------------------------------------------------------------------

    private static async Task<IResult> Revoke(
        [FromRoute] string appInstallationId,
        AccountUserDeviceService service,
        CancellationToken ct)
    {
        if (!TryParseInstallationId(appInstallationId, out var installId))
            return ErrorHttpMapper.ToHttpResult(AccountUserDeviceErrors.InvalidAppInstallationId);

        var result = await service.RevokeAsync(installId, ct);

        return result.IsFailure
            ? ErrorHttpMapper.ToHttpResult(result.Error)
            : Results.NoContent();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Validates appInstallationId as a strict UUID v4 in hyphenated format.
    /// Accepts the route value as a string to allow version verification — a plain
    /// {id:guid} constraint would pass non-v4 GUIDs without error.
    /// </summary>
    private static bool TryParseInstallationId(string value, out Guid installId)
    {
        installId = default;
        if (!Guid.TryParseExact(value, "D", out var g) || g == Guid.Empty)
            return false;
        // UUID v4: version nibble is the first character of the third group (index 14 in "D" format).
        if (value[14] != '4')
            return false;
        installId = g;
        return true;
    }

    private static AccountUserDevicePlatform? ParsePlatform(string? raw) =>
        raw switch
        {
            "ios" => AccountUserDevicePlatform.Ios,
            "android" => AccountUserDevicePlatform.Android,
            _ => null
        };

    private static IResult ValidationProblem(string detail, string code) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed.",
            detail: detail,
            type: "about:blank",
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

internal sealed record RegisterDeviceBody(
    string? Platform,
    string? PushToken,
    string? AppVersion,
    string? DeviceName);
