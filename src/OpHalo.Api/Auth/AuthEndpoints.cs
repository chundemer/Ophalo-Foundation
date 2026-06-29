using Microsoft.AspNetCore.Http;
using OpHalo.Api.Helpers;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Auth;
using OpHalo.Foundation.Application.Members;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Security;

namespace OpHalo.Api.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").RequireRateLimiting("auth");
        group.MapPost("/start", Start);
        group.MapPost("/signin", SignIn);
        group.MapPost("/exchange", Exchange);
        group.MapGet("/me", Me).RequireAuthorization();
        group.MapPost("/logout", Logout).RequireAuthorization();
    }

    private static async Task<IResult> Start(
        StartBody body,
        StartAuthService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Email))
            return ValidationProblem("Email is required.", "Validation.EmailRequired");
        if (string.IsNullOrWhiteSpace(body.BusinessName))
            return ValidationProblem("Business name is required.", "Validation.BusinessNameRequired");
        if (string.IsNullOrWhiteSpace(body.TimeZone))
            return ValidationProblem("Time zone is required.", "Validation.TimeZoneRequired");
        if (!IsValidIanaTimeZone(body.TimeZone))
            return ValidationProblem("Time zone is not a valid IANA time zone identifier.", "Validation.TimeZoneInvalid");

        var result = await service.HandleAsync(body.Email, body.BusinessName, body.Name, body.TimeZone, ct);

        if (result.IsFailure)
            return ErrorHttpMapper.ToHttpResult(result.Error);

        return Results.Ok();
    }

    private static async Task<IResult> SignIn(
        SignInBody body,
        SignInAuthService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Email))
            return ValidationProblem("Email is required.", "Validation.EmailRequired");

        var result = await service.HandleAsync(body.Email, ct);

        if (result.IsFailure)
            return ErrorHttpMapper.ToHttpResult(result.Error);

        return Results.Ok();
    }

    private static async Task<IResult> Exchange(
        ExchangeBody body,
        ExchangeAuthService service,
        HttpContext httpContext,
        AuthCookieOptionsFactory cookieFactory,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Code))
            return ValidationProblem("Code is required.", "Validation.CodeRequired");

        var clientType = ParseClientType(body.ClientType);
        if (clientType is null)
            return ValidationProblem("Invalid client type.", "Validation.InvalidClientType");

        var exchangeResult = await service.HandleAsync(body.Code, clientType.Value, body.DeviceName, ct);

        if (exchangeResult.Result.IsFailure)
        {
            var contextString = ToEntryContextString(exchangeResult.EntryContext);

            // ErrorHttpMapper owns the HTTP status — entryContext is additive metadata only.
            // This ensures session creation failures (503) and other non-422 errors are never
            // misclassified just because an EntryContext was available on the code.
            if (contextString is not null)
                return ErrorHttpMapper.ToHttpResult(
                    exchangeResult.Result.Error,
                    new Dictionary<string, object?> { ["entryContext"] = contextString });

            return ErrorHttpMapper.ToHttpResult(exchangeResult.Result.Error);
        }

        var token = exchangeResult.Result.Value;

        // Mobile: return raw token in response body for Bearer transport.
        // Browser: set HttpOnly cookie only — raw token must not appear in the response body.
        if (clientType == SessionClientType.MobileApp)
            return Results.Ok(new { sessionToken = token.RawToken, expiresAtUtc = token.ExpiresAtUtc });

        httpContext.Response.Cookies.Append(
            AuthConstants.CookieName,
            token.RawToken,
            cookieFactory.ForCreate(token.ExpiresAtUtc));

        return Results.Ok();
    }

    private static async Task<IResult> Me(
        ICurrentUser currentUser,
        IMemberManagementPersistence members,
        CancellationToken ct)
    {
        var role = await members.GetAccountUserRoleAsync(currentUser.UserId, ct);
        var accountRole = role switch
        {
            AccountUserRole.Owner    => "owner",
            AccountUserRole.Admin    => "admin",
            AccountUserRole.Operator => "operator",
            AccountUserRole.Viewer   => "viewer",
            null                     => "unknown",
            _                        => "unknown"
        };
        return Results.Ok(new
        {
            accountUserId = currentUser.UserId,
            accountId = currentUser.AccountId,
            isAuthenticated = currentUser.IsAuthenticated,
            isVerified = currentUser.IsVerified,
            accountRole
        });
    }

    private static async Task<IResult> Logout(
        HttpContext httpContext,
        IAccountSessionService sessionService,
        AuthCookieOptionsFactory cookieFactory,
        CancellationToken ct)
    {
        string? rawToken = null;

        var authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();
        if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
        {
            var bearerToken = authHeader["Bearer ".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(bearerToken))
                rawToken = bearerToken;
        }

        if (rawToken is null
            && httpContext.Request.Cookies.TryGetValue(AuthConstants.CookieName, out var cookieToken)
            && !string.IsNullOrWhiteSpace(cookieToken))
        {
            rawToken = cookieToken;
        }

        if (rawToken is not null)
            await sessionService.RevokeSessionByHash(SessionHasher.HashToken(rawToken), ct);

        httpContext.Response.Cookies.Delete(AuthConstants.CookieName, cookieFactory.ForDelete());
        return Results.Ok();
    }

    // --- Helpers ---

    /// <summary>
    /// Accepts only null/empty/"browser" → Browser and "mobile_app" → MobileApp.
    /// Returns null (rejected) for any unrecognised value, including "admin".
    /// </summary>
    private static SessionClientType? ParseClientType(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "browser" => SessionClientType.Browser,
            "mobile_app" => SessionClientType.MobileApp,
            _ => null
        };

    private static string? ToEntryContextString(EntryContext? entryContext) =>
        entryContext switch
        {
            EntryContext.NewAccount => "new_account",
            EntryContext.ExistingMember => "existing_member",
            _ => null
        };

    private static bool IsValidIanaTimeZone(string tz) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _);

    private static IResult ValidationProblem(string detail, string code) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed.",
            detail: detail,
            type: "about:blank",
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

internal sealed record StartBody(string? Email, string? BusinessName, string? Name, string? TimeZone);
internal sealed record SignInBody(string? Email);
internal sealed record ExchangeBody(string? Code, string? ClientType, string? DeviceName);
