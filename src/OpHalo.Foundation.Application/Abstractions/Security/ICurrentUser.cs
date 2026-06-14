namespace OpHalo.Foundation.Application.Abstractions.Security;

/// <summary>
/// Represents the currently authenticated user resolved from the trusted server-side
/// session. Handlers use this to derive identity — HttpContext never leaks into the
/// application layer.
/// <para>UserId = AccountUser.Id (the person). AccountId = Account.Id (the business).</para>
/// </summary>
/// <remarks>
/// Canonical, single definition. Collapsed in Phase 3 from the reference repo's
/// duplicate/chained pair (OpHalo.Shared.Abstractions.ICurrentUser and
/// OpHalo.Application.Abstractions.Security.ICurrentUser, the latter inheriting the
/// former). Identity is a Foundation concern, so it lives in Foundation.Application —
/// the SharedKernel must not contain CurrentUser (build plan §3.3, §8).
/// </remarks>
public interface ICurrentUser
{
    Guid UserId { get; }

    Guid AccountId { get; }

    bool IsAuthenticated { get; }

    /// <summary>
    /// Descriptive verification flag. Currently equivalent to a valid authenticated session.
    /// Do not use as a separate authorization gate unless an unverified-session flow exists.
    /// </summary>
    bool IsVerified { get; }
}
