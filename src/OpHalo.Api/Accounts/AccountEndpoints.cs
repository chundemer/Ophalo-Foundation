using Microsoft.AspNetCore.Http;
using OpHalo.Api.Auth;
using OpHalo.Api.Helpers;
using OpHalo.Foundation.Application.Auth;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Security;

namespace OpHalo.Api.Accounts;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/accounts/me/invite", SendInvite).RequireAuthorization();
        app.MapPost("/accounts/invite/accept", AcceptInvite).RequireRateLimiting("auth");
    }

    private static async Task<IResult> SendInvite(
        InviteBody body,
        SendInviteService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Email))
            return ValidationProblem("Email is required.", "Validation.EmailRequired");

        var role = ParseRole(body.Role);
        if (role is null)
            return ValidationProblem("Role must be 'admin', 'operator', or 'viewer'.", "Validation.RoleInvalid");

        var result = await service.HandleAsync(body.Email, role.Value, ct);

        if (result.IsFailure)
            return ErrorHttpMapper.ToHttpResult(result.Error);

        var status = result.Value == SendInviteResult.Resent ? "resent" : "sent";
        return Results.Ok(new { status });
    }

    private static async Task<IResult> AcceptInvite(
        AcceptInviteBody body,
        AcceptInviteService service,
        HttpContext httpContext,
        AuthCookieOptionsFactory cookieFactory,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Token))
            return ValidationProblem("A token is required.", "Validation.TokenRequired");

        var result = await service.HandleAsync(body.Token, ct);

        if (result.IsFailure)
            return ErrorHttpMapper.ToHttpResult(result.Error);

        var token = result.Value;

        httpContext.Response.Cookies.Append(
            AuthConstants.CookieName,
            token.RawToken,
            cookieFactory.ForCreate(token.ExpiresAtUtc));

        return Results.Ok(new { status = "accepted", destination = "/keep" });
    }

    // --- Helpers ---

    /// <summary>
    /// Accepts "admin", "operator", "viewer". Returns null for "owner" or any unknown value.
    /// Owner is reserved for CreateOwner — it cannot be assigned via invite (ADR-075).
    /// </summary>
    private static AccountUserRole? ParseRole(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "admin" => AccountUserRole.Admin,
            "operator" => AccountUserRole.Operator,
            "viewer" => AccountUserRole.Viewer,
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

internal sealed record InviteBody(string? Email, string? Role);
internal sealed record AcceptInviteBody(string? Token);
