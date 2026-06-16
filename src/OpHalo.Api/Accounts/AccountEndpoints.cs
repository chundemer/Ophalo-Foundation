using Microsoft.AspNetCore.Http;
using OpHalo.Api.Auth;
using OpHalo.Api.Helpers;
using OpHalo.Foundation.Application.Auth;
using OpHalo.Foundation.Application.Members;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.Foundation.Infrastructure.Security;

namespace OpHalo.Api.Accounts;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/accounts/me/invite", SendInvite).RequireAuthorization();
        app.MapPost("/accounts/invite/accept", AcceptInvite).RequireRateLimiting("auth");

        // Member management (Phase 5E-C — ADR-078..082)
        app.MapGet("/accounts/me/members", ListMembers).RequireAuthorization();
        app.MapPatch("/accounts/me/members/{accountUserId}/role", ChangeRole).RequireAuthorization();
        app.MapPost("/accounts/me/members/{accountUserId}/resend-invite", ResendInvite).RequireAuthorization();
        app.MapPost("/accounts/me/members/{accountUserId}/suspend", Suspend).RequireAuthorization();
        app.MapPost("/accounts/me/members/{accountUserId}/reactivate", Reactivate).RequireAuthorization();
        app.MapDelete("/accounts/me/members/{accountUserId}", Remove).RequireAuthorization();
    }

    // -------------------------------------------------------------------------
    // POST /accounts/me/invite
    // -------------------------------------------------------------------------

    private static async Task<IResult> SendInvite(
        InviteBody body,
        SendInviteService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Email))
            return ValidationProblem("Email is required.", "Validation.EmailRequired");

        var role = ParseRoleForInvite(body.Role);
        if (role is null)
            return ValidationProblem("Role must be 'admin', 'operator', or 'viewer'.", "Validation.RoleInvalid");

        var result = await service.HandleAsync(body.Email, role.Value, ct);

        if (result.IsFailure)
        {
            // Intercept service-to-endpoint routing codes before ErrorHttpMapper.
            // These internal codes must never appear in the API response body.
            // Translate to the public Member.PreviouslyRemoved code + suggestedAction.
            if (result.Error.Code == "Member.PreviouslyRemovedNeedsReactivate")
                return ErrorHttpMapper.ToHttpResult(MemberErrors.PreviouslyRemoved,
                    new Dictionary<string, object?> { ["suggestedAction"] = "reactivate" });

            if (result.Error.Code == "Member.PreviouslyRemovedNeedsResend")
                return ErrorHttpMapper.ToHttpResult(MemberErrors.PreviouslyRemoved,
                    new Dictionary<string, object?> { ["suggestedAction"] = "resend_invite" });

            return ErrorHttpMapper.ToHttpResult(result.Error);
        }

        var status = result.Value == SendInviteResult.Resent ? "resent" : "sent";
        return Results.Ok(new { status });
    }

    // -------------------------------------------------------------------------
    // POST /accounts/invite/accept
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // GET /accounts/me/members
    // -------------------------------------------------------------------------

    private static async Task<IResult> ListMembers(
        MemberManagementService service,
        CancellationToken ct,
        bool includeRemoved = false)
    {
        var result = await service.ListMembersAsync(includeRemoved, ct);

        return result.IsFailure
            ? ErrorHttpMapper.ToHttpResult(result.Error)
            : Results.Ok(result.Value);
    }

    // -------------------------------------------------------------------------
    // PATCH /accounts/me/members/{accountUserId}/role
    // -------------------------------------------------------------------------

    private static async Task<IResult> ChangeRole(
        Guid accountUserId,
        ChangeRoleBody body,
        MemberManagementService service,
        CancellationToken ct)
    {
        var role = ParseRoleForManagement(body?.Role);
        if (role is null)
            return ValidationProblem(
                "Role must be 'owner', 'admin', 'operator', or 'viewer'.",
                "Validation.RoleInvalid");

        var result = await service.ChangeRoleAsync(accountUserId, role.Value, ct);

        return result.IsFailure
            ? ErrorHttpMapper.ToHttpResult(result.Error)
            : Results.Ok();
    }

    // -------------------------------------------------------------------------
    // POST /accounts/me/members/{accountUserId}/resend-invite
    // -------------------------------------------------------------------------

    private static async Task<IResult> ResendInvite(
        Guid accountUserId,
        ResendInviteBody? body,
        MemberManagementService service,
        CancellationToken ct)
    {
        var deliveryMode = ParseDeliveryMode(body?.Delivery);
        if (deliveryMode is null)
            return ValidationProblem(
                "Delivery must be 'email' or 'manual_share'.",
                "Validation.InviteDeliveryInvalid");

        var result = await service.ResendInviteAsync(accountUserId, deliveryMode.Value, ct);

        if (result.IsFailure)
            return ErrorHttpMapper.ToHttpResult(result.Error);

        return result.Value.DeliveryMode == InviteDeliveryMode.ManualShare
            ? Results.Ok(new { inviteUrl = result.Value.InviteUrl })
            : Results.Ok();
    }

    // -------------------------------------------------------------------------
    // POST /accounts/me/members/{accountUserId}/suspend
    // -------------------------------------------------------------------------

    private static async Task<IResult> Suspend(
        Guid accountUserId,
        MemberManagementService service,
        CancellationToken ct)
    {
        var result = await service.SuspendAsync(accountUserId, ct);

        return result.IsFailure
            ? ErrorHttpMapper.ToHttpResult(result.Error)
            : Results.Ok();
    }

    // -------------------------------------------------------------------------
    // POST /accounts/me/members/{accountUserId}/reactivate
    // -------------------------------------------------------------------------

    private static async Task<IResult> Reactivate(
        Guid accountUserId,
        MemberManagementService service,
        CancellationToken ct)
    {
        var result = await service.ReactivateAsync(accountUserId, ct);

        return result.IsFailure
            ? ErrorHttpMapper.ToHttpResult(result.Error)
            : Results.Ok();
    }

    // -------------------------------------------------------------------------
    // DELETE /accounts/me/members/{accountUserId}
    // -------------------------------------------------------------------------

    private static async Task<IResult> Remove(
        Guid accountUserId,
        MemberManagementService service,
        CancellationToken ct)
    {
        var result = await service.RemoveAsync(accountUserId, ct);

        return result.IsFailure
            ? ErrorHttpMapper.ToHttpResult(result.Error)
            : Results.Ok();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Invite role parsing: Owner is excluded — Owner cannot be assigned via invite (ADR-075).
    /// Returns null for "owner" or any unknown value.
    /// </summary>
    private static AccountUserRole? ParseRoleForInvite(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "admin"    => AccountUserRole.Admin,
            "operator" => AccountUserRole.Operator,
            "viewer"   => AccountUserRole.Viewer,
            _          => null
        };

    /// <summary>
    /// Member management role parsing: Owner IS accepted so callers can promote/demote
    /// the Owner role. Authorization rules in MemberManagementService enforce who can do so.
    /// Returns null for any unknown value.
    /// </summary>
    private static AccountUserRole? ParseRoleForManagement(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "owner"    => AccountUserRole.Owner,
            "admin"    => AccountUserRole.Admin,
            "operator" => AccountUserRole.Operator,
            "viewer"   => AccountUserRole.Viewer,
            _          => null
        };

    /// <summary>
    /// Parses the delivery mode for resend-invite. Null or whitespace defaults to Email.
    /// Returns null for unknown values (caller should return 400).
    /// </summary>
    private static InviteDeliveryMode? ParseDeliveryMode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? InviteDeliveryMode.Email :
        value.Trim().ToLowerInvariant() switch
        {
            "email"        => InviteDeliveryMode.Email,
            "manual_share" => InviteDeliveryMode.ManualShare,
            _              => (InviteDeliveryMode?)null
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
internal sealed record ChangeRoleBody(string? Role);
internal sealed record ResendInviteBody(string? Delivery);
