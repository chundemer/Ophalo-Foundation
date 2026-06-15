using Microsoft.AspNetCore.Http;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Infrastructure.Security;

namespace OpHalo.Api.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").RequireRateLimiting("auth");
        group.MapGet("/me", Me).RequireAuthorization();
        group.MapPost("/logout", Logout).RequireAuthorization();
    }

    private static IResult Me(ICurrentUser currentUser) =>
        Results.Ok(new
        {
            accountUserId = currentUser.UserId,
            accountId = currentUser.AccountId,
            isAuthenticated = currentUser.IsAuthenticated,
            isVerified = currentUser.IsVerified
        });

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
}
