using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Core.Constants;

namespace OpHalo.Foundation.Infrastructure.Security;

/// <summary>
/// Resolves the authenticated account user identity from the current HTTP request.
/// IsAuthenticated is the strong OpHalo gate — it requires both framework authentication
/// and valid, parseable OpHalo identity claims.
///
/// UserId  = AccountUser.Id — from ClaimTypes.NameIdentifier set by SessionAuthenticationHandler.
/// AccountId = Account.Id  — from AuthConstants.AccountIdClaimType ("account_id").
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid UserId => ParseClaim(ClaimTypes.NameIdentifier);

    public Guid AccountId => ParseClaim(AuthConstants.AccountIdClaimType);

    /// <summary>
    /// True only when the framework confirms authentication AND both required OpHalo
    /// identity claims are present and valid (non-empty GUIDs).
    /// </summary>
    public bool IsAuthenticated =>
        httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true
        && UserId != Guid.Empty
        && AccountId != Guid.Empty;

    public bool IsVerified
    {
        get
        {
            if (!IsAuthenticated) return false;
            var value = httpContextAccessor.HttpContext?
                .User.FindFirst(AuthConstants.IsVerifiedClaimType)?.Value;
            return bool.TryParse(value, out var result) && result;
        }
    }

    private Guid ParseClaim(string claimType)
    {
        var value = httpContextAccessor.HttpContext?
            .User.FindFirst(claimType)?.Value;
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }
}
